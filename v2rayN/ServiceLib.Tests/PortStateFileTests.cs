using UiProto;

namespace ServiceLib.Tests;

public class PortStateFileTests
{
    [Test]
    public async Task SaveReplacesExistingStateAndRejectsInvalidPort()
    {
        var directory = Path.Combine(Path.GetTempPath(), "v2crackn-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.json");
        try
        {
            await PortStateFile.Save(path, 10808).Should().BeTrue();
            await PortStateFile.Save(path, 10810).Should().BeTrue();
            await PortStateFile.Save(path, 65535).Should().BeFalse();
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path));
            await (json!["socksPort"]!.GetValue<int>() == 10810).Should().BeTrue();
            await (Directory.GetFiles(directory).Length == 1).Should().BeTrue();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task FailedReplacementPreservesOldStateAndCleansTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "v2crackn-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.json");
        try
        {
            await PortStateFile.Save(path, 10808).Should().BeTrue();
            if (!OperatingSystem.IsWindows()) return;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await PortStateFile.Save(path, 10810).Should().BeFalse();
            }
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path));
            await (json!["socksPort"]!.GetValue<int>() == 10808).Should().BeTrue();
            await (Directory.GetFiles(directory).Length == 1).Should().BeTrue();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task MissingDirectoryReturnsFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), "v2crackn-test-" + Guid.NewGuid().ToString("N"), "state.json");
        await PortStateFile.Save(path, 10808).Should().BeFalse();
    }
}
