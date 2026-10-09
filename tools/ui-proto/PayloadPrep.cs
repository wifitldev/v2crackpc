using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UiProto;

/// <summary>
/// Готовит отдельный пейлоад прототипа (папка рядом с exe):
///  - создаёт рабочие каталоги ServiceLib;
///  - патчит копию guiNConfig.json, чтобы прототип жил отдельно от основного приложения
///    (свой порт, свой системный прокси, включённая статистика);
///  - держит резервную копию конфига и восстанавливает её, если файл пропал
///    (порт и прочие настройки при этом просто пропатчиваются из payload-state —
///    снимок копии никогда не затирает пользовательские настройки).
/// Все правки идемпотентны: повторный запуск ничего не ломает.
/// </summary>
public static class PayloadPrep
{
    /// <summary>Порт прототипа — стоковый SOCKS-порт v2rayN (HTTP живёт на +1).</summary>
    public const int ProtoPort = 10808;

    /// <summary>Порт HTTP-прокси — всегда SOCKS+1 (второй вход в конфиге, mixed).</summary>
    public static int HttpPort => ProtoPort + 1;

    /// <summary>Прежний порт прототипа (10890) — мигрируется в стоковый 10808.</summary>
    private const int LegacyProtoPort = 10890;

    /// <summary>Дефолтный порт v2rayN — признак сброшенного/нового конфига.</summary>
    private const int DefaultPort = 10808;

    /// <summary>Порт основного приложения — признак свежей копии конфига.</summary>
    private const int MainAppPort = 10810;

    public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

    private static string ConfigPath => Path.Combine(BaseDir, "guiConfigs", "guiNConfig.json");
    private static string BackupPath => Path.Combine(BaseDir, "guiConfigs", "guiNConfig.backup.json");
    private static string StatePath => Path.Combine(BaseDir, "payload-state.json");

    public static void Ensure()
    {
        foreach (var dir in new[] { "guiConfigs", "binConfigs", "guiLogs", "guiTemps", "guiBackups" })
            Directory.CreateDirectory(Path.Combine(BaseDir, dir));

        PatchConfig();
    }

    /// <summary>Желаемый socks-порт: последний выбранный пользователем либо порт по умолчанию.</summary>
    public static int LoadWantedPort()
    {
        try
        {
            if (File.Exists(StatePath) &&
                JsonNode.Parse(File.ReadAllText(StatePath)) is JsonObject state &&
                state["socksPort"] is JsonValue value &&
                value.TryGetValue<int>(out var port) &&
                port is >= 1024 and <= 65535)
            {
                // миграция: старый порт прототипа 10890 → стоковый 10808
                return port == LegacyProtoPort ? ProtoPort : port;
            }
        }
        catch
        {
            // повреждённый state — откатываемся на дефолтный порт
        }

        return ProtoPort;
    }

    /// <summary>Запомнить порт, выбранный в настройках, чтобы при следующем старте не перетирать его.</summary>
    public static void SaveWantedPort(int port)
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new Dictionary<string, int> { ["socksPort"] = port },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StatePath, json);
        }
        catch
        {
            // не критично: при следующем старте применится порт по умолчанию
        }
    }

    private static void PatchConfig()
    {
        var wantedPort = LoadWantedPort();

        JsonObject? root = ReadJson(ConfigPath);

        // файла нет или он не читается — пробуем резервную копию
        var restored = false;
        if (root is null && File.Exists(BackupPath))
        {
            root = ReadJson(BackupPath);
            if (root is not null)
            {
                File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                restored = true;
            }
        }

        if (root is null)
            return;

        var currentPort = GetLocalPort(root);

        // ВАЖНО: конфиг со сброшенным в дефолт портом НЕ восстанавливаем из резервной
        // копии целиком — там снимок первого запуска, и он затёр бы пользовательские
        // настройки (тему, язык, тумблеры, пресеты). Восстановление порта делает шаг 1
        // ниже: желаемый порт лежит в payload-state.json и просто пропатчивается.

        var changed = restored;

        // 1) свой локальный порт (свежая копия из основного приложения приходит с его портом)
        if (currentPort != wantedPort && GetInbound(root) is JsonObject inbound)
        {
            inbound["LocalPort"] = wantedPort;
            changed = true;
        }

        // 1b) второй mixed-вход: HTTP-прокси всегда живёт на SOCKS+1 (10809)
        if (GetInbound(root) is JsonObject inbound2)
        {
            var secondOn = inbound2["SecondLocalPortEnabled"] is JsonValue sv
                && sv.TryGetValue<bool>(out var flag) && flag;
            if (!secondOn)
            {
                inbound2["SecondLocalPortEnabled"] = true;
                changed = true;
            }
        }

        // 2) не хватать системный прокси на старте (в конфиге мог стоять Pac=3)
        if (root["SystemProxyItem"] is JsonObject sysProxy
            && sysProxy["SysProxyType"] is JsonValue proxyType
            && proxyType.TryGetValue<int>(out var type)
            && type == 3)
        {
            sysProxy["SysProxyType"] = 0;
            changed = true;
        }

        // 3) статистика: без неё не считается ни Download, ни Upload
        if (root["GuiItem"] is JsonObject gui)
        {
            if (gui["EnableStatistics"] is JsonValue stats && stats.TryGetValue<bool>(out var enabledStats) && !enabledStats)
            {
                gui["EnableStatistics"] = true;
                changed = true;
            }

            if (gui["DisplayRealTimeSpeed"] is JsonValue speed && speed.TryGetValue<bool>(out var enabledSpeed) && !enabledSpeed)
            {
                gui["DisplayRealTimeSpeed"] = true;
                changed = true;
            }
        }

        if (changed)
        {
            File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        // эталон создаётся один раз из уже пропатченного конфига
        if (!File.Exists(BackupPath) && File.Exists(ConfigPath))
        {
            File.Copy(ConfigPath, BackupPath, overwrite: false);
        }

        if (!File.Exists(StatePath))
        {
            SaveWantedPort(wantedPort);
        }
    }

    /// <summary>
    /// Тема из конфига (UiItem.CurrentTheme) — читается до старта ServiceLib,
    /// чтобы палитра была готова к разбору XAML первого окна.
    /// </summary>
    public static string? ReadUiTheme()
    {
        try
        {
            if (ReadJson(ConfigPath)?["UiItem"]?["CurrentTheme"] is JsonValue theme
                && theme.TryGetValue<string>(out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch
        {
            // нет конфига или он не читается — тема по умолчанию
        }

        return null;
    }

    /// <summary>
    /// Язык интерфейса из конфига (UiItem.CurrentLanguage) — читается до создания окна,
    /// чтобы первый разбор XAML уже был на нужном языке.
    /// </summary>
    public static string? ReadUiLanguage()
    {
        try
        {
            if (ReadJson(ConfigPath)?["UiItem"]?["CurrentLanguage"] is JsonValue lang
                && lang.TryGetValue<string>(out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch
        {
            // нет конфига или он не читается — язык по умолчанию
        }

        return null;
    }

    private static JsonObject? ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static JsonObject? GetInbound(JsonObject root)
        => root["Inbound"] is JsonArray { Count: > 0 } inbounds ? inbounds[0] as JsonObject : null;

    private static int GetLocalPort(JsonObject root)
        => GetInbound(root)?["LocalPort"] is JsonValue port && port.TryGetValue<int>(out var value)
            ? value
            : -1;
}
