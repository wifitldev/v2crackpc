using UiProto;

namespace ServiceLib.Tests;

public class LogNotificationTests
{
    [Test]
    public async Task FailingObserverDoesNotBlockOtherObservers()
    {
        string? received = null;
        Action<string> handlers = _ => throw new InvalidOperationException();
        handlers += text => received = text;
        LogNotification.Publish(handlers, "  connected  ");
        await (received == "connected").Should().BeTrue();
    }

    [Test]
    public async Task BlankMessageIsNotDelivered()
    {
        var invoked = false;
        LogNotification.Publish(_ => invoked = true, "  ");
        await invoked.Should().BeFalse();
    }
}
