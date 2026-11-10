namespace ServiceLib.Common;

/// <summary>Finds a free test port within the valid TCP range.</summary>
public static class SpeedtestPortAllocator
{
    public static int FindNext(int startPort, Func<int, bool> isOccupied)
    {
        for (var port = Math.Max(1, startPort); port < Global.MaxPort; port++)
        {
            if (!isOccupied(port)) return port;
        }
        return -1;
    }
}
