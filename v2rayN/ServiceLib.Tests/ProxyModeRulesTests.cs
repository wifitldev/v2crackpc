using UiProto;

namespace ServiceLib.Tests;

public class ProxyModeRulesTests
{
    [Test]
    public async Task ConnectChangesProxyOnlyForTunnelOrPendingCleanup()
    {
        await ProxyModeRules.ShouldApplyOnConnect(false, false).Should().BeFalse();
        await ProxyModeRules.ShouldApplyOnConnect(true, false).Should().BeTrue();
        await ProxyModeRules.ShouldApplyOnConnect(false, true).Should().BeTrue();
        await ProxyModeRules.ShouldApplyOnConnect(true, true).Should().BeTrue();
    }

    [Test]
    public async Task DisconnectedModeChangeDoesNotTouchSystemProxy()
    {
        await ProxyModeRules.ShouldUpdateSystemProxy(false).Should().BeFalse();
        await ProxyModeRules.ShouldUpdateSystemProxy(true).Should().BeTrue();
    }
}
