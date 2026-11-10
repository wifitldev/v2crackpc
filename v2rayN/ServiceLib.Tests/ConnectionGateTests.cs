using UiProto;

namespace ServiceLib.Tests;

public class ConnectionGateTests
{
    [Test]
    public async Task OperationsDoNotOverlap()
    {
        var gate = new ConnectionGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;
        var first = gate.RunAsync(async () => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = gate.RunAsync(() => { secondEntered = true; return Task.CompletedTask; });
        try
        {
            await secondEntered.Should().BeFalse();
        }
        finally { release.SetResult(); }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        await secondEntered.Should().BeTrue();
    }

    [Test]
    public async Task ExitWaitsForFailedInFlightOperation()
    {
        var gate = new ConnectionGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exitEntered = false;
        var connect = gate.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            throw new InvalidOperationException("simulated connection failure");
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var exit = gate.RunAsync(() => { exitEntered = true; return Task.CompletedTask; });
        try { await exitEntered.Should().BeFalse(); }
        finally { release.TrySetResult(); }
        try { await connect.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (InvalidOperationException) { }
        await exit.WaitAsync(TimeSpan.FromSeconds(5));
        await exitEntered.Should().BeTrue();
    }

    [Test]
    public async Task ExceptionDoesNotLockOutNextOperation()
    {
        var gate = new ConnectionGate();
        try { await gate.RunAsync(() => Task.FromException(new InvalidOperationException("test"))); }
        catch (InvalidOperationException) { }
        var result = await gate.RunAsync(() => Task.FromResult(42)).WaitAsync(TimeSpan.FromSeconds(5));
        await (result == 42).Should().BeTrue();
    }
}
