using UiProto;

namespace ServiceLib.Tests;

public class PortRollbackTests
{
    [Test]
    public async Task StateIsSavedEvenWhenConfigThrows()
    {
        var stateCalled = false;
        var result = await PortRollback.SaveAsync(
            () => throw new IOException("config"),
            () => { stateCalled = true; return true; });
        await stateCalled.Should().BeTrue();
        await result.Should().BeFalse();
    }

    [Test]
    public async Task BothSavesMustSucceed()
    {
        await (await PortRollback.SaveAsync(() => Task.FromResult(true), () => true)).Should().BeTrue();
        await (await PortRollback.SaveAsync(() => Task.FromResult(false), () => true)).Should().BeFalse();
        await (await PortRollback.SaveAsync(() => Task.FromResult(true), () => false)).Should().BeFalse();
        await (await PortRollback.SaveAsync(() => Task.FromResult(true), () => throw new IOException("state"))).Should().BeFalse();
    }
}
