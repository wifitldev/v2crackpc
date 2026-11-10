using UiProto;

namespace ServiceLib.Tests;

public class UiPortRulesTests
{
    [Test]
    public async Task MaximumPortLeavesSpaceForAllFixedInboundOffsets()
    {
        var maximumOffset = Enum.GetValues<EInboundProtocol>().Max(value => (int)value);
        await (PortRules.MaxSocksPort + maximumOffset <= 65535).Should().BeTrue();
    }

    [Test]
    public async Task SocksPort_ReservesSpaceForServicePorts()
    {
        await PortRules.IsValidSocksPort(1023).Should().BeFalse();
        await PortRules.IsValidSocksPort(1024).Should().BeTrue();
        await PortRules.IsValidSocksPort(65514).Should().BeTrue();
        await PortRules.IsValidSocksPort(65515).Should().BeFalse();
        await PortRules.IsValidSocksPort(65534).Should().BeFalse();
        await PortRules.IsValidSocksPort(65535).Should().BeFalse();
        await PortRules.IsValidSocksPort(65536).Should().BeFalse();
    }
}
