using System.Net;
using ServiceLib.Services;

namespace ServiceLib.Tests;

public class SensitiveDownloadTests
{
    [Test]
    public async Task SensitiveDownloadErrorsDoNotExposeUrl()
    {
        const string secret = "very-secret-test-token";
        var messages = new List<string>();
        var service = new DownloadService { SensitiveUrl = true };
        service.Error += (_, args) => messages.Add(args.GetException().ToString());

        // An invalid URI fails locally; no network or operating-system proxy changes.
        await service.TryDownloadString($"invalid://[{secret}", (IWebProxy?)null, "test");
        await (messages.Count > 0).Should().BeTrue();
        await messages.Any(message => message.Contains(secret, StringComparison.Ordinal)).Should().BeFalse();
    }
}
