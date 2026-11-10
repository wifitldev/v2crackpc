using System.Diagnostics;
using System.Net.Sockets;

namespace UiProto;

/// <summary>Probes only the local IPv4 loopback listener; it does not verify remote connectivity.</summary>
internal static class LocalPortProbe
{
    public static async Task<bool> IsListeningAsync(int port, int timeoutMs = 600)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return client.Connected;
        }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) { return false; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static async Task<bool> WaitAsync(int port, int timeoutMs)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            var remaining = timeoutMs - (int)timer.ElapsedMilliseconds;
            if (await IsListeningAsync(port, Math.Min(500, Math.Max(1, remaining))))
                return true;
            remaining = timeoutMs - (int)timer.ElapsedMilliseconds;
            if (remaining > 0) await Task.Delay(Math.Min(300, remaining));
        }
        return false;
    }
}
