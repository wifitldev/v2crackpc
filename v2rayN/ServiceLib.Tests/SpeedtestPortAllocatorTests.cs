using ServiceLib.Common;

namespace ServiceLib.Tests;

public class SpeedtestPortAllocatorTests
{
    [Test]
    public async Task LastValidPortIsAllocatedOnlyOnce()
    {
        var last = SpeedtestPortAllocator.FindNext(65535, _ => false);
        await (last == 65535).Should().BeTrue();
        await (SpeedtestPortAllocator.FindNext(last + 1, _ => false) == -1).Should().BeTrue();
    }

    [Test]
    public async Task OccupiedPortsAreSkippedAndExhaustionIsReported()
    {
        await (SpeedtestPortAllocator.FindNext(65533, p => p < 65535) == 65535).Should().BeTrue();
        await (SpeedtestPortAllocator.FindNext(65533, _ => true) == -1).Should().BeTrue();
        await (SpeedtestPortAllocator.FindNext(0, _ => false) == 1).Should().BeTrue();
    }
}
