namespace UiProto;

/// <summary>Резервирует пространство для служебных портов ServiceLib, включая speedtest (SOCKS+21).</summary>
public static class PortRules
{
    public const int MinSocksPort = 1024;
    public const int MaxSocksPort = 65514;

    public static bool IsValidSocksPort(int port) => port is >= MinSocksPort and <= MaxSocksPort;
}
