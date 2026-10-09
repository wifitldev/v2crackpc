using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UiProto;

/// <summary>
/// Настройки прототипа, которых нет в Config ServiceLib, — свой JSON рядом с
/// payload-state.json. Хранится способ пинга серверов (как в Happ: mixed / via Proxy
/// GET / via Proxy HEAD / TCP / ICMP). Запись — сразу при изменении, чтение — при
/// старте; выбор переживает перезапуск.
/// </summary>
public static class ProtoSettings
{
    private static string FilePath => Path.Combine(PayloadPrep.BaseDir, "proto-settings.json");

    /// <summary>Допустимые значения способа пинга; всё прочее откатывается на mixed.</summary>
    private static readonly HashSet<string> ValidMethods = ["mixed", "get", "head", "tcp", "icmp"];

    /// <summary>Способ пинга: mixed = все фазы подряд, get = via Proxy GET (дефолт).</summary>
    public static string PingMethod { get; private set; } = "get";

    static ProtoSettings() => Load();

    private static void Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject root &&
                root["pingMethod"] is JsonValue value &&
                value.TryGetValue<string>(out var method) &&
                ValidMethods.Contains(method))
            {
                PingMethod = method;
            }
        }
        catch
        {
            // повреждённый файл — остаёмся на дефолтном способе
        }
    }

    /// <summary>Выбрать способ пинга и сохранить его немедленно.</summary>
    public static void SetPingMethod(string method)
    {
        if (!ValidMethods.Contains(method))
            method = "get";

        PingMethod = method;
        Save();
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["pingMethod"] = PingMethod },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // не критично: при следующем старте применится прежний способ
        }
    }
}
