using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Program
{
    private const string AppName = "v2crackN";
    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", AppName);
    private static readonly string AppExe = Path.Combine(InstallDir, "v2crackN.exe");
    private static readonly string DesktopShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
    private static readonly string StartMenuDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", AppName);
    private static readonly string StartMenuShortcut = Path.Combine(StartMenuDir, AppName + ".lnk");
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\v2crackN";
    private static readonly string UninstallScript = Path.Combine(InstallDir, "uninstall.ps1");
    private static readonly string AppVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
        {
            var silent = new InstallerForm();
            return silent.SilentInstall() ? 0 : 1;
        }
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            InstallerForm.SilentUninstall();
            return 0;
        }
        Application.Run(new InstallerForm());
        return 0;
    }

    private sealed class InstallerForm : Form
    {
        private readonly CheckBox _desktop = new() { Text = "Добавить ярлык на рабочий стол", Checked = true, AutoSize = true };
        private readonly CheckBox _startMenu = new() { Text = "Добавить в меню Пуск", Checked = true, AutoSize = true };
        private readonly Label _status = new() { AutoSize = true, ForeColor = Color.FromArgb(95, 105, 120) };
        private readonly Label _percent = new() { Text = "0%", AutoSize = true, ForeColor = Color.FromArgb(95, 105, 120), TextAlign = ContentAlignment.MiddleRight };
        private readonly Button _install = new() { Text = "Установить", Width = 105, Height = 34 };
        private readonly Button _repair = new() { Text = "Переустановить", Width = 112, Height = 34 };
        private readonly Button _update = new() { Text = "Обновить", Width = 100, Height = 34 };
        private readonly Button _remove = new() { Text = "Удалить", Width = 95, Height = 34 };
        private readonly Button _cancel = new() { Text = "Отмена", Width = 85, Height = 34 };
        private readonly ProgressBar _progress = new() { Height = 18, Dock = DockStyle.Bottom, Minimum = 0, Maximum = 100, Visible = false };
        private readonly Label _progressTitle = new() { Text = "", AutoSize = true, ForeColor = Color.FromArgb(95, 105, 120) };

        public InstallerForm()
        {
            Text = AppName + " — Установщик";
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(560, 400);
            Size = new Size(700, 430);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            var title = new Label { Text = AppName, Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(28, 24, 0, 0) };
            var subtitle = new Label { Text = "Клиент для Windows 10/11",  Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(95, 105, 120), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(30, 2, 0, 0) };
            var options = new GroupBox { Text = "Параметры установки", Dock = DockStyle.Top, Height = 105, Padding = new Padding(18, 12, 12, 8), Margin = new Padding(28, 20, 28, 0) };
            options.Controls.Add(_startMenu);
            options.Controls.Add(_desktop);
            _startMenu.Location = new Point(18, 58);
            _desktop.Location = new Point(18, 30);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(18, 10, 18, 8) };
            buttons.Controls.Add(_install);
            buttons.Controls.Add(_repair);
            buttons.Controls.Add(_update);
            buttons.Controls.Add(_remove);
            buttons.Controls.Add(_cancel);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 18, 28, 0) };
            body.Controls.Add(_progressTitle);
            body.Controls.Add(_percent);
            body.Controls.Add(_status);
            _status.Dock = DockStyle.Bottom;
            _status.Padding = new Padding(0, 0, 0, 10);
            _percent.Dock = DockStyle.Bottom;
            _percent.Padding = new Padding(0, 0, 0, 10);
            _progressTitle.Dock = DockStyle.Bottom;
            _progressTitle.Padding = new Padding(0, 0, 0, 8);
            Controls.Add(_progress);
            Controls.Add(buttons);
            Controls.Add(body);
            Controls.Add(options);
            Controls.Add(subtitle);
            Controls.Add(title);

            _install.Click += (_, _) => RunOperation("Установка", false);
            _repair.Click += (_, _) => RunOperation("Переустановка", true);
            _update.Click += (_, _) => RunOperation("Обновление", true);
            _remove.Click += (_, _) => RemoveApp();
            _cancel.Click += (_, _) => Close();

            var installed = File.Exists(AppExe);
            _install.Enabled = !installed;
            _repair.Enabled = installed;
            _update.Enabled = installed;
            _remove.Enabled = installed;
            var present = ReadInstalledVersion();
            _status.Text = installed
                ? "Установлено: v" + present + ". Готово к обновлению или удалению."
                : "Выберите параметры и нажмите «Установить».";
        }

        private async void RunOperation(string operation, bool replaceExisting)
        {
            SetBusy(true, operation + "…");
            try
            {
                await Task.Run(() => InstallCore(replaceExisting, _desktop.Checked, _startMenu.Checked));
                _status.Text = operation + " завершена. Добавлено в «Приложения и возможности» Windows.";
                Process.Start(new ProcessStartInfo(AppExe) { WorkingDirectory = InstallDir });
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка: " + ex.Message;
            }
            finally { SetBusy(false, _status.Text); }
        }

        /// <summary>Тихая установка для автообновления и автоматизации: installer.exe --install.</summary>
        public bool SilentInstall()
        {
            try
            {
                InstallCore(true, true, true);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Пользовательские данные внутри папки установки — их не трогаем при обновлении и удалении.</summary>
        private static readonly string[] UserItems = { "guiConfigs", "proto-settings.json", "payload-state.json" };

        /// <summary>Удаляет файлы приложения, оставляя настройки, подписки и локальную БД.</summary>
        private static void CleanInstallDir()
        {
            if (!Directory.Exists(InstallDir)) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(InstallDir))
            {
                if (UserItems.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(entry, true); }
                catch { try { File.Delete(entry); } catch { } }
            }
        }

        /// <summary>Тихое удаление: installer.exe --uninstall (без вопросов, как из «Приложений» Windows).</summary>
        public static void SilentUninstall()
        {
            StopRunningApp();
            DeleteShortcut(DesktopShortcut);
            DeleteShortcut(StartMenuShortcut);
            RemoveFromWindows();
            CleanInstallDir();
        }

        private void InstallCore(bool replaceExisting, bool desktop, bool startMenu)
        {
            StopRunningApp();
            if (replaceExisting) CleanInstallDir();
            Directory.CreateDirectory(InstallDir);
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                ?? throw new InvalidOperationException("В установщике отсутствует пакет приложения.");
            using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            var total = Math.Max(1, entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var destination = Path.GetFullPath(Path.Combine(InstallDir, entry.FullName));
                if (!destination.StartsWith(Path.GetFullPath(InstallDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Некорректный путь в пакете.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
                ReportProgress((i + 1) * 100 / total, "Распаковка: " + entry.FullName);
            }
            if (desktop) CreateShortcut(DesktopShortcut);
            else DeleteShortcut(DesktopShortcut);
            if (startMenu) CreateShortcut(StartMenuShortcut);
            else DeleteShortcut(StartMenuShortcut);
            WriteUninstallScript();
            RegisterInWindows();
        }

        private void ReportProgress(int percent, string file)
        {
            if (!IsHandleCreated) return;
            BeginInvoke(() =>
            {
                _progress.Value = percent;
                _percent.Text = percent + "%";
                _progressTitle.Text = file;
            });
        }

        private void RemoveApp()
        {
            if (MessageBox.Show("Удалить v2crackN? Пользовательские настройки и подписки сохранятся.", "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                StopRunningApp();
                DeleteShortcut(DesktopShortcut);
                DeleteShortcut(StartMenuShortcut);
                RemoveFromWindows();
                CleanInstallDir();
                _status.Text = "Приложение удалено. Запись в «Приложениях Windows» убрана.";
                _install.Enabled = true;
                _repair.Enabled = _update.Enabled = _remove.Enabled = false;
            }
            catch (Exception ex) { _status.Text = "Ошибка удаления: " + ex.Message; }
        }

        private static void StopRunningApp()
        {
            foreach (var p in Process.GetProcessesByName("v2crackN"))
            {
                try { if (p.Id != Environment.ProcessId) p.Kill(true); } catch { }
                p.Dispose();
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _status.Text = text;
            _progressTitle.Text = busy ? "Подготовка файлов…" : "";
            _percent.Text = busy ? "0%" : "";
            _progress.Value = 0;
            _progress.Visible = busy;
            _install.Enabled = _repair.Enabled = _update.Enabled = _remove.Enabled = _cancel.Enabled = !busy;
        }

        /// <summary>Версия из записи «Установленные программы», если приложение уже ставилось.</summary>
        private static string ReadInstalledVersion()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
                return key?.GetValue("DisplayVersion") as string ?? AppVersion;
            }
            catch { return AppVersion; }
        }

        /// <summary>Регистрация в Windows: Параметры → Приложения, Панель управления → Удаление программ.</summary>
        private static void RegisterInWindows()
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
            if (key is null) return;
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", AppVersion);
            key.SetValue("Publisher", AppName);
            key.SetValue("DisplayIcon", AppExe);
            key.SetValue("InstallLocation", InstallDir);
            var uninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + UninstallScript + "\"";
            var quietUninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + UninstallScript + "\" -Quiet";
            key.SetValue("UninstallString", uninstall);
            key.SetValue("QuietUninstallString", quietUninstall);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", DirectorySizeKb(InstallDir), RegistryValueKind.DWord);
            key.SetValue("URLInfoAbout", "https://github.com/wifitldev/v2crackpc");
        }

        private static void RemoveFromWindows()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }
        }

        private static int DirectorySizeKb(string dir)
        {
            try
            {
                var bytes = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                return (int)(bytes / 1024);
            }
            catch { return 0; }
        }

        /// <summary>Скрипт удаления рядом с приложением — его вызывает Windows при удалении из «Приложений».</summary>
        private static void WriteUninstallScript()
        {
            var script =
                "# v2crackN — удаление\r\n" +
                "param([switch]$Quiet)\r\n" +
                "Add-Type -AssemblyName PresentationFramework\r\n" +
                "if (-not $Quiet) { $answer = [System.Windows.MessageBox]::Show('Удалить v2crackN? Настройки, подписки и локальная БД сохранятся.', 'v2crackN', 4, 32); if ($answer -ne 'Yes') { exit } }\r\n" +
                "Get-Process v2crackN -ErrorAction SilentlyContinue | Stop-Process -Force\r\n" +
                "Remove-Item '" + DesktopShortcut.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue\r\n" +
                "Remove-Item '" + StartMenuShortcut.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue\r\n" +
                "Remove-Item 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\v2crackN' -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                "$keep = @('guiConfigs', 'proto-settings.json', 'payload-state.json')\r\n" +
                "Get-ChildItem -LiteralPath $PSScriptRoot -Force -ErrorAction SilentlyContinue | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                "if (-not $Quiet) { [System.Windows.MessageBox]::Show('v2crackN удалён. Настройки сохранены в папке приложения.', 'v2crackN', 0, 64) }\r\n";
            File.WriteAllText(UninstallScript, script);
        }

        private static void CreateShortcut(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var script = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + path.Replace("'", "''") + "');" +
                         "$s.TargetPath='" + AppExe.Replace("'", "''") + "';$s.WorkingDirectory='" + InstallDir.Replace("'", "''") + "';" +
                         "$s.IconLocation='" + AppExe.Replace("'", "''") + "';$s.Save()";
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"") { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(5000);
        }

        private static void DeleteShortcut(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            try { if (Directory.Exists(StartMenuDir) && !Directory.EnumerateFileSystemEntries(StartMenuDir).Any()) Directory.Delete(StartMenuDir); } catch { }
        }
    }
}
