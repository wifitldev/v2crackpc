namespace ServiceLib.Common;

/// <summary>Runs every cleanup action even if an earlier action fails.</summary>
public static class ShutdownCleanup
{
    public static async Task RunAsync(IEnumerable<Func<Task>> actions, Action<Exception> reportError)
    {
        foreach (var action in actions)
        {
            try { await action(); }
            catch (Exception ex)
            {
                try { reportError(ex); } catch { /* reporting must not interrupt cleanup */ }
            }
        }
    }
}
