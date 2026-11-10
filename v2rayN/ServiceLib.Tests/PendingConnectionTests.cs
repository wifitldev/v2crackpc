using UiProto;

namespace ServiceLib.Tests;

public class PendingConnectionTests
{
    [Test]
    public async Task RequestIsConsumedOnce()
    {
        var pending = new PendingConnection();
        await pending.Take().Should().BeFalse();
        pending.Request();
        pending.Request();
        await pending.Take().Should().BeTrue();
        await pending.Take().Should().BeFalse();
    }

    [Test]
    public async Task DisconnectDuringStartupCancelsQueuedConnect()
    {
        var pending = new PendingConnection();
        pending.Request();
        pending.Cancel();
        await pending.Take().Should().BeFalse();
        pending.Request();
        await pending.Take().Should().BeTrue();
    }
}
