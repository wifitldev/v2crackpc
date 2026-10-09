using System.Windows;

namespace UiProto;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Аппаратный D3D-путь WPF на этой машине не отдаёт кадр (белый экран при
        // полностью корректном внутреннем рендере — проверено на минимальном
        // Hello-World; WinForms/GDI при этом работают). По умолчанию — программная
        // отрисовка: для статичного прототипа её качества и плавности достаточно.
        // --hw: попробовать аппаратный режим (если драйвер GPU будет починен).
        if (!e.Args.Any(a => string.Equals(a, "--hw", StringComparison.OrdinalIgnoreCase)))
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);

        // Палитра и язык должны существовать до разбора MainWindow.xaml
        // (там StaticResource и локализация первого окна).
        try
        {
            PayloadPrep.Ensure();
            ThemeManager.Apply(PayloadPrep.ReadUiTheme());
            Loc.SetLang(PayloadPrep.ReadUiLanguage());
        }
        catch
        {
            ThemeManager.Apply(null);
            Loc.SetLang(null);
        }
    }
}
