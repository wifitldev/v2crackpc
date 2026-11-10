using MaterialDesignThemes.Wpf;

namespace v2rayN.Views;

public partial class AboutView
{
    public AboutView()
    {
        InitializeComponent();

        txtName.Text = Global.AppName;
        txtVersion.Text = $"V{Utils.GetVersionInfo()}";
        txtBuiltOn.Text = string.Format(ResUI.AboutBuiltOn, Global.UpstreamName, Global.UpstreamVersion);
        txtLicense.Text = string.Format(ResUI.AboutLicense, "GPL-3.0");
        txtRepo.Text = $"{Global.GithubUrl}/{Global.AppRepo}";
        btnReleases.Content = ResUI.AboutReleases;
        btnClose.Content = ResUI.menuClose;

        btnReleases.Click += (_, _) => ProcUtils.ProcessStart(Global.AppReleasePageUrl);
        btnClose.Click += (_, _) => DialogHost.Close("RootDialog");
    }
}
