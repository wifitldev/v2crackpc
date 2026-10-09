using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ServiceLib.Enums;

namespace UiProto;

/// <summary>
/// Живая палитра прототипа. Все цвета в XAML ссылаются на ключи этой таблицы через
/// DynamicResource, а смысловые кисти (Text1, CardBg, Accent…) видят стили как
/// StaticResource. При смене темы значения переписываются прямо в кистях, поэтому
/// обновляется весь интерфейс — и элементы, и назначенные из кода кисти — без
/// перезагрузки окна. Dark — чистый чёрный, Light — чистый белый.
/// </summary>
public static class ThemeManager
{
    /// <summary>Имя темы из ETheme, сейчас применённая.</summary>
    public static string Current { get; private set; } = nameof(ETheme.Dark);

    /// <summary>Светлая ли тема применена прямо сейчас (для заголовка окна).</summary>
    public static bool UsesLight => ResolveLight(Current);

    private static ResourceDictionary? _palette;

    // key -> (dark, light); при отсутствии светлого значения берётся тёмное.
    // Dark — чистый чёрный (#000), Light — чистый белый (#FFF); акцент один.
    private static readonly Dictionary<string, (string Dark, string Light)> Table = new()
    {
        // --- hex-ключи, использованные прямо в XAML ---
        ["0A1120"] = ("000000", "F7F8FA"), // sidebar
        ["0B1220"] = ("0B1220", "0B1220"), // label on accent thumb
        ["0C1526"] = ("000000", "FFFFFF"), // status bar
        ["0D1728"] = ("0B0B0D", "FFFFFF"), // power ring inner
        ["0E1626"] = ("000000", "FFFFFF"), // page / window background
        ["0E1728"] = ("0B0B0D", "FFFFFF"), // sidebar info card
        ["0F1728"] = ("0B0B0D", "F7F8FA"), // card 2 / picker item
        ["101A2C"] = ("1F1F24", "E4E6EA"), // toggle knob stroke
        ["101B2E"] = ("16161A", "F0F1F3"), // nav hover / power outer
        ["121B2D"] = ("0B0B0D", "FFFFFF"), // card
        ["12203A"] = ("0B0B0D", "F7F8FA"), // header status pill
        ["13233A"] = ("0F2233", "E8F3FD"), // accent soft
        ["14203A"] = ("16161A", "F0F1F3"), // mode track
        ["16233A"] = ("16161A", "F0F1F3"), // icon tiles / power glow
        ["16412E"] = ("0E2A1F", "E6F7EF"), // green chip background
        ["1A2942"] = ("16161A", "F7F8FA"), // buttons / chips / inputs
        ["1B2E4B"] = ("1F1F24", "E9EAEE"), // power button hover
        ["22314C"] = ("1F1F24", "E4E6EA"), // separators / thin borders
        ["24334F"] = ("1F1F24", "E4E6EA"), // card stroke
        ["263B58"] = ("26262E", "E4E6EA"), // power outer stroke
        ["2C3E5C"] = ("33333B", "D9DBE0"), // scrollbar thumb
        ["2E3D57"] = ("2E2E36", "D8DAE0"), // toggle off track
        ["2E4466"] = ("1F1F24", "E4E6EA"), // common border
        ["2E4C77"] = ("26262E", "E4E6EA"), // power ring stroke
        ["2E9BE6"] = ("2E9BE6", "2E9BE6"), // accent
        ["2F8A63"] = ("2F8A63", "2F8A63"), // green chip stroke
        ["34527F"] = ("26262E", "E4E6EA"), // power glow border
        ["5B6478"] = ("5B6478", "8B95A9"), // idle dot
        ["6B7994"] = ("6B7994", "7A8290"), // muted text 3
        ["6BECAC"] = ("6BECAC", "0E7D55"), // green text
        ["7E8AA0"] = ("7E8AA0", "6B7280"), // captions
        ["8B98AF"] = ("8B98AF", "565B66"), // muted text 2
        ["93A4BE"] = ("93A4BE", "59616F"), // emoji
        ["A8B7D0"] = ("A8B7D0", "36404F"), // log text
        ["A9BAD6"] = ("A9BAD6", "3F4759"), // flag emoji
        ["B7C5DC"] = ("B7C5DC", "414957"), // chip text / off mode label
        ["C3D0E5"] = ("C3D0E5", "0F1B2E"), // button text
        ["C6CEDC"] = ("C6CEDC", "FFFFFF"), // knob fill off
        ["CC0A1120"] = ("CC000000", "CC0A1120"), // overlay scrim
        ["F2F6FF"] = ("F5F6F8", "0B0B0D"), // text 1
        ["FFFFFF"] = ("FFFFFF", "FFFFFF"), // on accent

        // --- смысловые кисти: стили и код находят их как ресурсы ---
        ["SidebarBg"] = ("000000", "F7F8FA"),
        ["PageBg"] = ("000000", "FFFFFF"),
        ["CardBg"] = ("0B0B0D", "FFFFFF"),
        ["CardBg2"] = ("0B0B0D", "F7F8FA"),
        ["CardStroke"] = ("1F1F24", "E4E6EA"),
        ["Accent"] = ("2E9BE6", "2E9BE6"),
        ["AccentSoft"] = ("0F2233", "E8F3FD"),
        ["Text1"] = ("F5F6F8", "0B0B0D"),
        ["Text2"] = ("8B98AF", "565B66"),
        ["Text3"] = ("6B7994", "7A8290"),
        ["Ok"] = ("22D3EE", "0D8FA6"),

        // чип действия Block в редакторе правил
        ["BlockBg"] = ("2A1216", "FDECEE"),
        ["BlockStroke"] = ("7A2A36", "E9A8B0"),
        ["BlockText"] = ("FF7A90", "C0344A"),

        // цвет ошибки — следует за темой
        ["ErrorDark"] = ("FF7A90", "FF7A90"),
        ["ErrorLight"] = ("C0344A", "C0344A"),
    };

    /// <summary>
    /// Применить тему (имя из ETheme). Пусто/null = Dark. Вызывается до создания
    /// главного окна и при смене темы в настройках; повторный вызов безопасен.
    /// </summary>
    public static void Apply(string? theme)
    {
        Current = string.IsNullOrWhiteSpace(theme) ? nameof(ETheme.Dark) : theme!;
        var light = ResolveLight(Current);

        void ApplyCore()
        {
            if (_palette is null)
            {
                _palette = new ResourceDictionary();
                Application.Current.Resources.MergedDictionaries.Add(_palette);
            }

            foreach (var entry in Table)
            {
                var hex = light && entry.Value.Light.Length > 0 ? entry.Value.Light : entry.Value.Dark;
                if (!TryColor(hex, out var color))
                    continue;

                // WPF замораживает кисти после первого использования — такую
                // меняем только заменой, DynamicResource-потребители перерешатся сами
                if (_palette[entry.Key] is SolidColorBrush brush && !brush.IsFrozen)
                    brush.Color = color;
                else
                    _palette[entry.Key] = new SolidColorBrush(color);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ApplyCore();
        else
            dispatcher.Invoke(ApplyCore);
    }

    private static bool ResolveLight(string theme) =>
        theme.Equals(nameof(ETheme.Light), StringComparison.OrdinalIgnoreCase)
        || (theme.Equals(nameof(ETheme.FollowSystem), StringComparison.OrdinalIgnoreCase) && IsSystemLight());

    private static bool IsSystemLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryColor(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString("#" + hex)!;
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }
}
