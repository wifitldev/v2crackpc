using ServiceLib.Manager;
using ServiceLib.Models.Entities;

namespace ServiceLib.Tests;

public class ProfileExManagerTests
{
    [Test]
    public async Task ConcurrentUpdatesKeepOneCompleteRecord()
    {
        var saved = new List<ProfileExItem>();
        var manager = new ProfileExManager(item => { saved.Add(item); return Task.CompletedTask; });
        await Task.WhenAll(Task.Run(() => manager.SetTestDelay("id", 42)), Task.Run(() => manager.SetTestSpeed("id", 123m)));
        await manager.SaveTo();
        await (saved.Count == 1 && saved[0].Delay == 42 && saved[0].Speed == 123m).Should().BeTrue();
        var snapshot = await manager.GetProfileExs();
        snapshot.Single().Delay = 999;
        await ((await manager.GetProfileExs()).Single().Delay == 42).Should().BeTrue();
    }

    [Test]
    public async Task FailedSaveKeepsPendingItemsForRetry()
    {
        var fail = true;
        var saved = new List<string>();
        var manager = new ProfileExManager(item =>
        {
            if (fail && item.IndexId == "b") throw new InvalidOperationException();
            saved.Add(item.IndexId);
            return Task.CompletedTask;
        });
        manager.SetTestDelay("a", 1);
        manager.SetTestDelay("b", 2);
        try { await manager.SaveTo(); } catch (InvalidOperationException) { }
        fail = false;
        await manager.SaveTo();
        await (saved.Count(id => id == "a") == 1 && saved.Count(id => id == "b") == 1).Should().BeTrue();
    }

    [Test]
    public async Task ChangesDuringSaveAreNotAcknowledgedByOldSnapshot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new List<int>();
        var manager = new ProfileExManager(async item =>
        {
            saved.Add(item.Delay);
            if (saved.Count == 1) { entered.SetResult(); await release.Task; }
        });
        manager.SetTestDelay("id", 1);
        var first = manager.SaveTo();
        await entered.Task;
        manager.SetTestDelay("id", 2);
        var second = manager.SaveTo();
        release.SetResult();
        await Task.WhenAll(first, second);
        await saved.SequenceEqual([1, 2]).Should().BeTrue();
    }
}
