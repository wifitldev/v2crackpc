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
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var installerMutex = new Mutex(true, @"Local\v2crackN.Installer", out var acquired);
        if (!acquired)
        {
            if (!args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase) || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
                MessageBox.Show("Установщик v2crackN уже запущен.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        if (args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
        {
            var silent = new InstallerForm();
            return silent.SilentInstall() ? 0 : 1;
        }
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            try { InstallerForm.SilentUninstall(); return 0; }
            catch { return 1; }
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
                ? (string.IsNullOrWhiteSpace(present) ? "Версия установленного приложения неизвестна." : "Установлено: v" + present + ".") + " Готово к обновлению или удалению."
                : "Выберите параметры и нажмите «Установить».";
        }

        private bool _busy;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _status.Text = "Дождитесь завершения операции.";
            }
            base.OnFormClosing(e);
        }

        private async void RunOperation(string operation, bool replaceExisting)
        {
            SetBusy(true, operation + "…");
            var closeAfterOperation = false;
            try
            {
                var desktop = _desktop.Checked;
                var startMenu = _startMenu.Checked;
                await Task.Run(() => InstallCore(replaceExisting, desktop, startMenu));
                _status.Text = operation + " завершена. Добавлено в «Приложения и возможности» Windows.";
                try
                {
                    Process.Start(new ProcessStartInfo(AppExe) { WorkingDirectory = InstallDir });
                }
                catch (Exception launchError)
                {
                    _status.Text = operation + " завершена, но запустить приложение не удалось: " + launchError.Message;
                    return;
                }
                closeAfterOperation = true;
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка: " + ex.Message;
            }
            finally { SetBusy(false, _status.Text); }
            if (closeAfterOperation) Close();
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
        private static readonly string[] UserItems = { "guiConfigs", "guiBackups", "guiLogs", "guiTemps", "binConfigs", "proto-settings.json", "payload-state.json" };

        private static bool IsUserDataPath(string path)
        {
            var first = path.Replace('\\', '/').Split('/')[0];
            return UserItems.Contains(first, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Удаляет файлы приложения, оставляя настройки, подписки и локальную БД.</summary>
        private static void CleanInstallDir()
        {
            if (!Directory.Exists(InstallDir)) return;
            // Only remove files we own. Unknown files and directories may be user data.
            foreach (var name in new[] { "v2crackN.exe", "README.txt", "uninstall.ps1" })
            {
                var path = Path.Combine(InstallDir, name);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>Тихое удаление: installer.exe --uninstall (без вопросов, как из «Приложений» Windows).</summary>
        public static void SilentUninstall()
        {
            StopRunningApp();
            CleanInstallDir();
            DeleteShortcut(DesktopShortcut);
            DeleteShortcut(StartMenuShortcut);
            RemoveFromWindows();
        }

        private void InstallCore(bool replaceExisting, bool desktop, bool startMenu)
        {
            if (File.Exists(AppExe) && VersionGuard.IsDowngrade(ReadInstalledVersion(), AppVersion))
                throw new InvalidOperationException("Установлена более новая версия v2crackN. Используйте её установщик, чтобы не потерять совместимость настроек.");
            StopRunningApp();
            // Overlay only packaged files; preserve existing backups and unknown user files.
            Directory.CreateDirectory(InstallDir);
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                ?? throw new InvalidOperationException("В установщике отсутствует пакет приложения.");
            using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
            var staging = Path.Combine(Path.GetTempPath(), "v2crackN-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var files = PayloadStaging.Extract(zip, staging);
                PayloadStaging.ValidateRequiredFiles(files);
                StopRunningApp();
                PayloadCommit.Apply(staging, InstallDir, files,
                    (count, file) => ReportProgress(count * 100 / files.Count, "Установка: " + file));
            }
            finally
            {
                try { Directory.Delete(staging, true); } catch { /* temporary files only */ }
            }
            try
            {
                if (desktop) CreateShortcut(DesktopShortcut);
                else DeleteShortcut(DesktopShortcut);
                if (startMenu) CreateShortcut(StartMenuShortcut);
                else DeleteShortcut(StartMenuShortcut);
                WriteUninstallScript();
                RegisterInWindows();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Файлы программы установлены, но настройку ярлыков или регистрации Windows завершить не удалось. Повторите установку. Причина: " + error.Message, error);
            }
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
                CleanInstallDir();
                DeleteShortcut(DesktopShortcut);
                DeleteShortcut(StartMenuShortcut);
                RemoveFromWindows();
                _status.Text = "Приложение удалено. Запись в «Приложениях Windows» убрана.";
                _install.Enabled = true;
                _repair.Enabled = _update.Enabled = _remove.Enabled = false;
            }
            catch (Exception ex) { _status.Text = "Ошибка удаления: " + ex.Message; }
        }

        private static void StopRunningApp()
        {
            var processes = Process.GetProcessesByName("v2crackN");
            try
            {
                // Do not kill the client: it must shut down its core and proxy itself.
                // Also avoid touching another portable/development copy.
                foreach (var process in processes)
                {
                    string? path;
                    try { path = process.MainModule?.FileName; }
                    catch (Exception ex) { throw new InvalidOperationException("Не удалось проверить запущенный v2crackN. Закройте его через меню трея и повторите операцию.", ex); }
                    if (string.IsNullOrWhiteSpace(path))
                        throw new InvalidOperationException("Не удалось определить путь запущенного v2crackN. Закройте его через меню трея и повторите операцию.");
                    if (string.Equals(path, AppExe, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Закройте v2crackN через пункт «Выйти» в трее и повторите операцию.");
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            _status.Text = text;
            _progressTitle.Text = busy ? "Подготовка файлов…" : "";
            _percent.Text = busy ? "0%" : "";
            _progress.Value = 0;
            _progress.Visible = busy;
            var installed = File.Exists(AppExe);
            _install.Enabled = !busy && !installed;
            _repair.Enabled = _update.Enabled = _remove.Enabled = !busy && installed;
            _cancel.Enabled = !busy;
        }

        /// <summary>Версия из записи «Установленные программы», если приложение уже ставилось.</summary>
        private static string? ReadInstalledVersion()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
                return key?.GetValue("DisplayVersion") as string;
            }
            catch { return null; }
        }

        /// <summary>Регистрация в Windows: Параметры → Приложения, Панель управления → Удаление программ.</summary>
        private static void RegisterInWindows()
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath)
                ?? throw new InvalidOperationException("Не удалось создать запись приложения в Windows.");
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
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
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
                "$app = Join-Path $PSScriptRoot 'v2crackN.exe'\r\n" +
                "$running = @(Get-Process v2crackN -ErrorAction SilentlyContinue | Where-Object { try { $p = $_.Path; [string]::IsNullOrWhiteSpace($p) -or $p -eq $app } catch { $true } })\r\n" +
                "if ($running.Count -gt 0) { if (-not $Quiet) { [System.Windows.MessageBox]::Show('Сначала закройте v2crackN через пункт «Выйти» в трее.', 'v2crackN', 0, 48) }; exit 1 }\r\n" +
                "try { if (Test-Path -LiteralPath $app) { Remove-Item -LiteralPath $app -Force -ErrorAction Stop } } catch { if (-not $Quiet) { [System.Windows.MessageBox]::Show('Не удалось удалить программу. Закройте её и повторите попытку.', 'v2crackN', 0, 48) }; exit 1 }\r\n" +
                "Remove-Item '" + DesktopShortcut.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue\r\n" +
                "Remove-Item '" + StartMenuShortcut.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue\r\n" +
                "$reg = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\v2crackN'\r\n" +
                "try { if (Test-Path -LiteralPath $reg) { Remove-Item -LiteralPath $reg -Recurse -Force -ErrorAction Stop } } catch { if (-not $Quiet) { [System.Windows.MessageBox]::Show('Файл программы удалён, но не удалось убрать запись из Windows. Повторите удаление.', 'v2crackN', 0, 48) }; exit 1 }\r\n" +
                "@('v2crackN.exe','README.txt') | ForEach-Object { Remove-Item -LiteralPath (Join-Path $PSScriptRoot $_) -Force -ErrorAction SilentlyContinue }\r\n" +
                "if (-not $Quiet) { [System.Windows.MessageBox]::Show('v2crackN удалён. Настройки сохранены в папке приложения.', 'v2crackN', 0, 64) }\r\n";
            // Windows PowerShell 5.1 needs a UTF-8 BOM to decode Russian messages correctly.
            File.WriteAllText(UninstallScript, script, new System.Text.UTF8Encoding(true));
        }

        private static void CreateShortcut(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var script = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + path.Replace("'", "''") + "');" +
                         "$s.TargetPath='" + AppExe.Replace("'", "''") + "';$s.WorkingDirectory='" + InstallDir.Replace("'", "''") + "';" +
                         "$s.IconLocation='" + AppExe.Replace("'", "''") + "';$s.Save()";
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"") { CreateNoWindow = true, UseShellExecute = false });
            if (p is null) throw new InvalidOperationException("Не удалось запустить создание ярлыка.");
            if (!p.WaitForExit(5000)) throw new TimeoutException("Создание ярлыка не завершилось вовремя.");
            if (p.ExitCode != 0 || !File.Exists(path))
                throw new InvalidOperationException("Не удалось создать ярлык: " + path);
        }

        private static void DeleteShortcut(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            try { if (Directory.Exists(StartMenuDir) && !Directory.EnumerateFileSystemEntries(StartMenuDir).Any()) Directory.Delete(StartMenuDir); } catch { }
        }
    }
}
