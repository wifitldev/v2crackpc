namespace ServiceLib.Handler.SysProxy;

public static class SysProxyHandler
{
    private static readonly string _tag = "SysProxyHandler";
    private static readonly Lazy<PacManager> _pacManager = new(() => new PacManager());

    public static async Task<bool> UpdateSysProxy(Config config, bool forceDisable, ESysProxyType? overrideType = null)
    {
        try
        {
            var type = overrideType ?? config.SystemProxyItem.SysProxyType;
            if (forceDisable && type != ESysProxyType.Unchanged)
            {
                type = ESysProxyType.ForcedClear;
            }

            if (type is not (ESysProxyType.Unchanged or ESysProxyType.ForcedClear or ESysProxyType.ForcedChange or ESysProxyType.Pac))
                return false;
            // Unchanged is a true no-op, including the embedded PAC server.
            if (type == ESysProxyType.Unchanged)
                return true;
            if (type == ESysProxyType.Pac && !Utils.IsWindows())
                return false;
            if (!Utils.IsWindows() && !Utils.IsLinux() && !Utils.IsMacOS())
                return false;

            // Clearing the system proxy must not depend on a valid local inbound.
            var needsPort = type is ESysProxyType.ForcedChange or ESysProxyType.Pac;
            var port = needsPort ? AppManager.Instance.GetLocalPort(EInboundProtocol.socks) : 0;
            if (needsPort && (port <= 0 || port > 65535))
            {
                return false;
            }
            switch (type)
            {
                case ESysProxyType.ForcedChange when Utils.IsWindows():
                    var (strProxy, strExceptions) = GetWindowsProxyString(config, port);
                    if (!ProxySettingWindows.SetProxy(strProxy, strExceptions, 2))
                        return false;
                    break;

                case ESysProxyType.ForcedChange when Utils.IsLinux():
                    var exceptions = SanitizeExceptions(config);
                    await ProxySettingLinux.SetProxy(Global.Loopback, port, exceptions);
                    break;

                case ESysProxyType.ForcedChange when Utils.IsMacOS():
                    var exceptions2 = SanitizeExceptions(config);
                    await ProxySettingOSX.SetProxy(Global.Loopback, port, exceptions2);
                    break;

                case ESysProxyType.ForcedClear when Utils.IsWindows():
                    if (!ProxySettingWindows.UnsetProxy())
                        return false;
                    break;

                case ESysProxyType.ForcedClear when Utils.IsLinux():
                    await ProxySettingLinux.UnsetProxy();
                    break;

                case ESysProxyType.ForcedClear when Utils.IsMacOS():
                    await ProxySettingOSX.UnsetProxy();
                    break;

                case ESysProxyType.Pac when Utils.IsWindows():
                    if (!await SetWindowsProxyPac(port))
                        return false;
                    break;
            }

            if (type != ESysProxyType.Pac && Utils.IsWindows() && _pacManager.IsValueCreated)
            {
                _pacManager.Value.Stop();
            }
        }
        catch (Exception ex)
        {
            try { Logging.SaveLog(_tag, ex); } catch { /* logging must not hide the proxy failure */ }
            return false;
        }
        return true;
    }

    private static string SanitizeExceptions(Config config)
    {
        var exceptions = config.SystemProxyItem.SystemProxyExceptions;
        if (exceptions.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var items = exceptions
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Replace(" ", string.Empty))
            .Where(item => item.Length > 0);

        return string.Join(',', items);
    }

    private static (string strProxy, string strExceptions) GetWindowsProxyString(Config config, int port)
    {
        var strExceptions = config.SystemProxyItem.SystemProxyExceptions.Replace(" ", "");
        if (config.SystemProxyItem.NotProxyLocalAddress)
        {
            strExceptions = $"<local>;{strExceptions}";
        }

        var strProxy = string.Empty;
        if (config.SystemProxyItem.SystemProxyAdvancedProtocol.IsNullOrEmpty())
        {
            strProxy = $"{Global.Loopback}:{port}";
        }
        else
        {
            strProxy = config.SystemProxyItem.SystemProxyAdvancedProtocol
                .Replace("{ip}", Global.Loopback)
                .Replace("{http_port}", port.ToString())
                .Replace("{socks_port}", port.ToString());
        }

        return (strProxy, strExceptions);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<bool> SetWindowsProxyPac(int port)
    {
        var portPac = AppManager.Instance.GetLocalPort(EInboundProtocol.pac);
        if (portPac is <= 0 or > 65535)
            return false;
        await _pacManager.Value.StartAsync(port, portPac);
        var applied = false;
        try
        {
            var strProxy = $"{Global.HttpProtocol}{Global.Loopback}:{portPac}/pac?t={DateTime.Now.Ticks}";
            applied = ProxySettingWindows.SetProxy(strProxy, "", 4);
            return applied;
        }
        finally
        {
            if (!applied) _pacManager.Value.Stop();
        }
    }
}
