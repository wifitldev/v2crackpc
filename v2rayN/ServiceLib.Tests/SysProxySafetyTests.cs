using ServiceLib.Handler.SysProxy;
using ServiceLib.Models.Configs;

namespace ServiceLib.Tests;

public class SysProxySafetyTests
{
    [Test]
    public async Task OverrideDoesNotMutateDesiredProxyMode()
    {
        var config = new Config { SystemProxyItem = new SystemProxyItem { SysProxyType = ESysProxyType.ForcedChange } };
        var result = await SysProxyHandler.UpdateSysProxy(config, false, ESysProxyType.Unchanged);
        await result.Should().BeTrue();
        await (config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange).Should().BeTrue();
    }

    [Test]
    public async Task MissingProxySettingsReturnsFailureWithoutChangingSystemProxy()
    {
        var config = new Config { SystemProxyItem = null! };
        await (await SysProxyHandler.UpdateSysProxy(config, false)).Should().BeFalse();
        await (await SysProxyHandler.UpdateSysProxy(config, false, ESysProxyType.Unchanged)).Should().BeTrue();
    }

    [Test]
    public async Task UnknownModeFailsWithoutChangingSystemProxy()
    {
        var config = new Config { SystemProxyItem = new SystemProxyItem { SysProxyType = (ESysProxyType)999 } };
        await (await SysProxyHandler.UpdateSysProxy(config, false)).Should().BeFalse();
    }

    [Test]
    public async Task UnchangedProxyDoesNotRequireInitializedAppManager()
    {
        // Unchanged must not consult the local inbound or modify OS proxy settings.
        var config = new Config { SystemProxyItem = new SystemProxyItem { SysProxyType = ESysProxyType.Unchanged } };
        await (await SysProxyHandler.UpdateSysProxy(config, false)).Should().BeTrue();
    }
}
