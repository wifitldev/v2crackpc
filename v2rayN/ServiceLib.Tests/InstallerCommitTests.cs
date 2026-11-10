namespace ServiceLib.Tests;

public class InstallerCommitTests
{
    [Test]
    public async Task ReplacingHardLinkDoesNotModifyOtherLink()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        try
        {
            var outside = Path.Combine(root, "outside.txt");
            File.WriteAllText(outside, "old");
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /H \"{Path.Combine(target, "app.exe")}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true });
            await process!.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Unable to create test hard link");
            File.WriteAllText(Path.Combine(stage, "app.exe"), "new");
            PayloadCommit.Apply(stage, target, ["app.exe"]);
            await (File.ReadAllText(outside) == "old" && File.ReadAllText(Path.Combine(target, "app.exe")) == "new").Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task RollbackDoesNotModifyOtherHardLink()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        try
        {
            var outside = Path.Combine(root, "outside.txt");
            File.WriteAllText(outside, "old");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /H \"{Path.Combine(target, "app.exe")}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true });
            await process!.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Unable to create test hard link");
            File.WriteAllText(Path.Combine(stage, "app.exe"), "new");
            var failed = false;
            try { PayloadCommit.Apply(stage, target, ["app.exe"], (_, _) => throw new IOException("simulated error")); }
            catch (IOException) { failed = true; }
            await (failed && File.ReadAllText(outside) == "old" && File.ReadAllText(Path.Combine(target, "app.exe")) == "old").Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task RejectsRedirectedDestinationDirectory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var target = Path.Combine(root, "target");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(stage, "file.txt"), "new");
            var link = Path.Combine(target, "redirect");
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true });
            await process!.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Unable to create test junction");
            var failed = false;
            try { PayloadCommit.Apply(stage, target, [Path.Combine("redirect", "file.txt")]); }
            catch (IOException) { failed = true; }
            await (failed && !File.Exists(Path.Combine(outside, "file.txt"))).Should().BeTrue();
        }
        finally
        {
            var link = Path.Combine(target, "redirect");
            if (Directory.Exists(link))
            {
                using var cleanup = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c rmdir \"{link}\"") { UseShellExecute = false, CreateNoWindow = true });
                cleanup!.WaitForExit();
                if (cleanup.ExitCode != 0) throw new IOException("Unable to remove test junction");
            }
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task LockedFileRollsBackPreviouslyReplacedFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(Path.Combine(target, "app.exe"), "old app");
            File.WriteAllText(Path.Combine(target, "locked.dll"), "old library");
            File.WriteAllText(Path.Combine(target, "settings.json"), "private");
            File.WriteAllText(Path.Combine(stage, "app.exe"), "new app");
            File.WriteAllText(Path.Combine(stage, "locked.dll"), "new library");
            var failed = false;
            using (new FileStream(Path.Combine(target, "locked.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { PayloadCommit.Apply(stage, target, ["app.exe", "locked.dll"]); }
                catch (IOException) { failed = true; }
            }
            await (failed && File.ReadAllText(Path.Combine(target, "app.exe")) == "old app"
                && File.ReadAllText(Path.Combine(target, "locked.dll")) == "old library"
                && File.ReadAllText(Path.Combine(target, "settings.json")) == "private").Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task FileThatAllowsReadingButNotReplacementRollsBackEarlierFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(Path.Combine(target, "app.exe"), "old app");
            var locked = Path.Combine(target, "locked.dll");
            File.WriteAllText(locked, "old library");
            File.WriteAllText(Path.Combine(stage, "app.exe"), "new app");
            File.WriteAllText(Path.Combine(stage, "locked.dll"), "new library");
            var failed = false;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { PayloadCommit.Apply(stage, target, ["app.exe", "locked.dll"]); }
                catch (UnauthorizedAccessException) { failed = true; }
                catch (IOException) { failed = true; }
            }
            await (failed && File.ReadAllText(Path.Combine(target, "app.exe")) == "old app"
                && File.ReadAllText(locked) == "old library").Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task LockedFirstFileLeavesInstallationUnchanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        try
        {
            var app = Path.Combine(target, "app.exe");
            File.WriteAllText(app, "old app");
            File.WriteAllText(Path.Combine(target, "settings.json"), "private");
            File.WriteAllText(Path.Combine(stage, "app.exe"), "new app");
            var failed = false;
            using (new FileStream(app, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { PayloadCommit.Apply(stage, target, ["app.exe"]); }
                catch (IOException) { failed = true; }
            }
            await (failed && File.ReadAllText(app) == "old app"
                && File.ReadAllText(Path.Combine(target, "settings.json")) == "private").Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task FailedUpdateRestoresOldFilesAndPreservesUserData()
    {
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(Path.Combine(target, "app.exe"), "old");
            File.WriteAllText(Path.Combine(target, "settings.json"), "private");
            File.WriteAllText(Path.Combine(stage, "app.exe"), "new");
            File.WriteAllText(Path.Combine(stage, "new.dll"), "new library");
            var failed = false;
            try
            {
                PayloadCommit.Apply(stage, target, ["app.exe", "new.dll"], (n, _) =>
                {
                    if (n == 2) throw new IOException("simulated failure");
                });
            }
            catch (IOException) { failed = true; }
            await (failed && File.ReadAllText(Path.Combine(target, "app.exe")) == "old"
                && File.ReadAllText(Path.Combine(target, "settings.json")) == "private"
                && !File.Exists(Path.Combine(target, "new.dll"))).Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }
}
