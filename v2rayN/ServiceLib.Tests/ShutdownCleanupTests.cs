using ServiceLib.Common;

namespace ServiceLib.Tests;

public class ShutdownCleanupTests
{
    [Test]
    public async Task FailureAndFailingLoggerDoNotPreventLaterCleanup()
    {
        var calls = new List<int>();
        await ShutdownCleanup.RunAsync(
        [
            () => { calls.Add(1); throw new InvalidOperationException("first"); },
            () => { calls.Add(2); return Task.CompletedTask; },
            () => { calls.Add(3); return Task.FromException(new InvalidOperationException("third")); },
            () => { calls.Add(4); return Task.CompletedTask; },
        ], _ => throw new InvalidOperationException("logger"));

        await calls.SequenceEqual([1, 2, 3, 4]).Should().BeTrue();
    }
}
