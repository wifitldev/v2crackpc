using System.IO.Compression;

namespace ServiceLib.Tests;

public class InstallerStagingTests
{
    [Test]
    public async Task ExtractRejectsUnsafeAndDuplicatePathsBeforeWriting()
    {
        foreach (var name in new[] { "../outside", "x/../guiConfigs/db", "guiConfigs/db", "bin/test:stream", "C:/file", "/rooted", "guiConfigs /db", "foo./bar", "bin/CON.txt", "bin/lpt1", "bin/a?b" })
        {
            using var data = Archive("ok.txt", name);
            using var zip = new ZipArchive(data, ZipArchiveMode.Read);
            var dir = Path.Combine(Path.GetTempPath(), "v2crackN-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var rejected = false;
                try { PayloadStaging.Extract(zip, dir); } catch (InvalidDataException) { rejected = true; }
                await (rejected && !File.Exists(Path.Combine(dir, "ok.txt"))).Should().BeTrue();
            }
            finally { Directory.Delete(dir, true); }
        }
        using var duplicate = Archive("bin/xray.exe", "BIN/XRAY.EXE");
        using var duplicateZip = new ZipArchive(duplicate, ZipArchiveMode.Read);
        var temp = Path.Combine(Path.GetTempPath(), "v2crackN-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var rejected = false;
            try { PayloadStaging.Extract(duplicateZip, temp); } catch (InvalidDataException) { rejected = true; }
            await rejected.Should().BeTrue();
        }
        finally { Directory.Delete(temp, true); }
    }

    [Test]
    public async Task RejectsFileDirectoryConflictsBeforeExtractionInEitherOrder()
    {
        foreach (var files in new[] { new[] { "ok.txt", "bin", "BIN/xray.exe" }, new[] { "ok.txt", "bin/xray.exe", "BIN" } })
        {
            using var data = Archive(files);
            using var zip = new ZipArchive(data, ZipArchiveMode.Read);
            var dir = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var rejected = false;
                try { PayloadStaging.Extract(zip, dir); } catch (InvalidDataException) { rejected = true; }
                await (rejected && !File.Exists(Path.Combine(dir, "ok.txt"))).Should().BeTrue();
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    [Test]
    public async Task ExtractValidPackageToIsolatedDirectory()
    {
        using var data = Archive("v2crackN.exe", "bin/xray/xray.exe");
        using var zip = new ZipArchive(data, ZipArchiveMode.Read);
        var dir = Path.Combine(Path.GetTempPath(), "v2crackN-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = PayloadStaging.Extract(zip, dir);
            await (files.Count == 2 && File.Exists(Path.Combine(dir, "bin", "xray", "xray.exe"))).Should().BeTrue();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task RejectsIncompletePackageAndAcceptsCompletePackage()
    {
        string[] files = ["v2crackN.exe", "bin/xray/xray.exe", "bin/sing_box/sing-box.exe", "bin/geoip.dat", "bin/geosite.dat", "bin/wintun.dll", "bin/dpi/ciadpi.exe"];
        var rejected = false;
        try { PayloadStaging.ValidateRequiredFiles(files.Skip(1).ToArray()); }
        catch (InvalidDataException) { rejected = true; }
        PayloadStaging.ValidateRequiredFiles(files);
        PayloadStaging.ValidateRequiredFiles(files.Append("README.txt").ToArray());
        var rejectedExtra = false;
        try { PayloadStaging.ValidateRequiredFiles(files.Append("my-settings.json").ToArray()); }
        catch (InvalidDataException) { rejectedExtra = true; }
        await (rejected && rejectedExtra).Should().BeTrue();
    }

    [Test]
    public async Task CompletePackageUpdatePreservesSettingsAndBackups()
    {
        string[] package = ["v2crackN.exe", "bin/xray/xray.exe", "bin/sing_box/sing-box.exe", "bin/geoip.dat", "bin/geosite.dat", "bin/wintun.dll", "bin/dpi/ciadpi.exe"];
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(Path.Combine(install, "guiConfigs"));
        Directory.CreateDirectory(Path.Combine(install, "guiBackups"));
        try
        {
            File.WriteAllText(Path.Combine(install, "v2crackN.exe"), "old version");
            File.WriteAllText(Path.Combine(install, "guiConfigs", "guiNDB.db"), "test database");
            File.WriteAllText(Path.Combine(install, "guiBackups", "backup.zip"), "test backup");
            File.WriteAllText(Path.Combine(install, "proto-settings.json"), "test settings");
            File.WriteAllText(Path.Combine(install, "payload-state.json"), "test port state");
            File.WriteAllText(Path.Combine(install, "guiConfigs", "guiNConfig.json"), "test config");
            File.WriteAllText(Path.Combine(install, "guiConfigs", "guiNConfig.backup.json"), "test config backup");
            File.WriteAllText(Path.Combine(install, "unrelated.txt"), "user file");
            using var data = Archive(package);
            using var zip = new ZipArchive(data, ZipArchiveMode.Read);
            var files = PayloadStaging.Extract(zip, stage);
            PayloadStaging.ValidateRequiredFiles(files);
            PayloadCommit.Apply(stage, install, files);
            await (File.ReadAllText(Path.Combine(install, "v2crackN.exe")) == "test"
                && File.ReadAllText(Path.Combine(install, "guiConfigs", "guiNDB.db")) == "test database"
                && File.ReadAllText(Path.Combine(install, "guiBackups", "backup.zip")) == "test backup"
                && File.ReadAllText(Path.Combine(install, "proto-settings.json")) == "test settings"
                && File.ReadAllText(Path.Combine(install, "payload-state.json")) == "test port state"
                && File.ReadAllText(Path.Combine(install, "guiConfigs", "guiNConfig.json")) == "test config"
                && File.ReadAllText(Path.Combine(install, "guiConfigs", "guiNConfig.backup.json")) == "test config backup"
                && File.ReadAllText(Path.Combine(install, "unrelated.txt")) == "user file"
                && File.Exists(Path.Combine(install, "bin", "xray", "xray.exe"))).Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task OptionalRealPackageUpdatePreservesTestData()
    {
        var archivePath = Environment.GetEnvironmentVariable("V2CRACKN_TEST_PACKAGE");
        if (string.IsNullOrEmpty(archivePath))
        {
            throw new TUnit.Core.Exceptions.SkipTestException("V2CRACKN_TEST_PACKAGE не задан");
        }
        var root = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(Path.Combine(install, "guiConfigs"));
        try
        {
            File.WriteAllText(Path.Combine(install, "v2crackN.exe"), "old app");
            var settings = Path.Combine(install, "guiConfigs", "guiNDB.db");
            using (var database = new SQLiteConnection(settings, false))
            {
                database.CreateTable<SubItem>();
                database.Insert(new SubItem { Id = "test", Remarks = "dummy subscription", Url = "https://example.com/test" });
            }
            var databaseHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(settings));
            using var zip = ZipFile.OpenRead(archivePath);
            var files = PayloadStaging.Extract(zip, stage);
            PayloadStaging.ValidateRequiredFiles(files);
            PayloadCommit.Apply(stage, install, files);
            // Reinstall the same payload: user data must survive repeated updates too.
            PayloadCommit.Apply(stage, install, files);
            var match = databaseHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(settings)));
            using (var database = new SQLiteConnection(settings, false))
                match &= database.Find<SubItem>("test")?.Remarks == "dummy subscription";
            foreach (var file in files)
            {
                using var staged = File.OpenRead(Path.Combine(stage, file));
                using var installed = File.OpenRead(Path.Combine(install, file));
                match &= System.Security.Cryptography.SHA256.HashData(staged).SequenceEqual(System.Security.Cryptography.SHA256.HashData(installed));
            }
            await match.Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task RejectsEmptyPackagedFile()
    {
        using var data = new MemoryStream();
        using (var archive = new ZipArchive(data, ZipArchiveMode.Create, true))
            archive.CreateEntry("v2crackN.exe");
        data.Position = 0;
        using var zip = new ZipArchive(data, ZipArchiveMode.Read);
        var dir = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var rejected = false;
            try { PayloadStaging.Extract(zip, dir); } catch (InvalidDataException) { rejected = true; }
            await rejected.Should().BeTrue();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task RejectsTooManyFilesBeforeExtraction()
    {
        using var data = Archive(Enumerable.Range(0, 257).Select(i => $"file{i}.txt").ToArray());
        using var zip = new ZipArchive(data, ZipArchiveMode.Read);
        var dir = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var rejected = false;
            try { PayloadStaging.Extract(zip, dir); } catch (InvalidDataException) { rejected = true; }
            await (rejected && !Directory.EnumerateFileSystemEntries(dir).Any()).Should().BeTrue();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task CopyRejectsShortAndOversizedStreams()
    {
        foreach (var expected in new long[] { 2, 4 })
        {
            using var input = new MemoryStream([1, 2, 3]);
            using var output = new MemoryStream();
            var rejected = false;
            try { PayloadStaging.CopyExact(input, output, expected); }
            catch (InvalidDataException) { rejected = true; }
            await (rejected && output.Length <= expected).Should().BeTrue();
        }
        using var exact = new MemoryStream([1, 2, 3]);
        using var result = new MemoryStream();
        PayloadStaging.CopyExact(exact, result, 3);
        await result.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }).Should().BeTrue();
    }

    private static MemoryStream Archive(params string[] paths)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var path in paths)
                using (var writer = new StreamWriter(zip.CreateEntry(path).Open())) writer.Write("test");
        stream.Position = 0;
        return stream;
    }
}
