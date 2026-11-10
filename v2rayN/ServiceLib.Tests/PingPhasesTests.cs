using UiProto;

namespace ServiceLib.Tests;

public class PingPhasesTests
{
    private static readonly PingPhases.Phase[] MixedRun = [PingPhases.Phase.Tcping, PingPhases.Phase.Realping, PingPhases.Phase.UdpTest];
    private static readonly PingPhases.Phase[] MixedDisplay = [PingPhases.Phase.Realping, PingPhases.Phase.Tcping, PingPhases.Phase.UdpTest];

    [Test]
    public async Task PhasesFor_SelectsSinglePhasePerExplicitMethod()
    {
        await (PingPhases.PhasesFor("get")[0] == PingPhases.Phase.Realping && PingPhases.PhasesFor("get").Length == 1).Should().BeTrue();
        await (PingPhases.PhasesFor("head")[0] == PingPhases.Phase.HttpHead && PingPhases.PhasesFor("head").Length == 1).Should().BeTrue();
        await (PingPhases.PhasesFor("tcp")[0] == PingPhases.Phase.Tcping && PingPhases.PhasesFor("tcp").Length == 1).Should().BeTrue();
        await (PingPhases.PhasesFor("icmp")[0] == PingPhases.Phase.Icmp && PingPhases.PhasesFor("icmp").Length == 1).Should().BeTrue();
    }

    [Test]
    public async Task PhasesFor_FallbackRunsThreeServiceLibPhases()
    {
        await PingPhases.PhasesFor("mixed").SequenceEqual(MixedRun).Should().BeTrue();
        await PingPhases.PhasesFor("").SequenceEqual(MixedRun).Should().BeTrue();
        await PingPhases.PhasesFor("bogus").SequenceEqual(MixedRun).Should().BeTrue();
    }

    [Test]
    public async Task DisplayFor_PrioritizesRealpingForMixedFallback()
    {
        await PingPhases.DisplayFor("mixed").SequenceEqual(MixedDisplay).Should().BeTrue();
        await PingPhases.DisplayFor("tcp").SequenceEqual([PingPhases.Phase.Tcping]).Should().BeTrue();
    }

    [Test]
    public async Task BestResult_WalksDisplayPriorityUntilPositiveValue()
    {
        var values = new Dictionary<PingPhases.Phase, int>
        {
            [PingPhases.Phase.Tcping] = 50,
            [PingPhases.Phase.Realping] = 120,
            [PingPhases.Phase.UdpTest] = -1,
        };
        await (PingPhases.BestResult(values, MixedDisplay) == 120).Should().BeTrue();

        // real-пинг отсутствует → приоритет уходит на следующий живой замер
        values.Remove(PingPhases.Phase.Realping);
        await (PingPhases.BestResult(values, MixedDisplay) == 50).Should().BeTrue();
    }

    [Test]
    public async Task BestResult_MissingPhasesCountAsUnavailable()
    {
        var values = new Dictionary<PingPhases.Phase, int> { [PingPhases.Phase.UdpTest] = 42 };
        await (PingPhases.BestResult(values, MixedDisplay) == 42).Should().BeTrue();
    }

    [Test]
    public async Task BestResult_AllFailingOrAbsentMeansNotFound()
    {
        var values = new Dictionary<PingPhases.Phase, int> { [PingPhases.Phase.Tcping] = -1, [PingPhases.Phase.Realping] = 0 };
        await (PingPhases.BestResult(values, MixedDisplay) == -1).Should().BeTrue();
        await (PingPhases.BestResult(new Dictionary<PingPhases.Phase, int>(), MixedDisplay) == -1).Should().BeTrue();
    }
}
