namespace UiProto;

/// <summary>Serializes core and system-proxy operations, including their failure paths.</summary>
internal sealed class ConnectionGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        await _semaphore.WaitAsync();
        try { return await operation(); }
        finally { _semaphore.Release(); }
    }

    public async Task RunAsync(Func<Task> operation)
    {
        await _semaphore.WaitAsync();
        try { await operation(); }
        finally { _semaphore.Release(); }
    }
}
