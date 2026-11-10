namespace UiProto;

internal static class ProxyModeRules
{
    // A disconnected mode change only selects the desired mode for the next connection.
    public static bool ShouldUpdateSystemProxy(bool coreUp) => coreUp;

    public static bool ShouldApplyOnConnect(bool tunnelMode, bool cleanupPending) =>
        tunnelMode || cleanupPending;
}
