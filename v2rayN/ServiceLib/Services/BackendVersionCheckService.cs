namespace ServiceLib.Services;

/// <summary>
/// v2crackNG style version check against the fork's own backend (analog of the Android
/// AngApplication.checkAppVersion()).
/// POST {"device_id","","app_version","","os","","arch":""}
/// -&gt; {"type":"outdated"|"ok","version":"...","message":"..."}
/// When the url is empty or the backend is unreachable nothing happens.
/// </summary>
public static class BackendVersionCheckService
{
    private static readonly string _tag = "BackendVersionCheckService";

    public sealed record VersionCheckResult(bool Outdated, string Version, string Message);

    /// <summary>
    /// Stable per user/machine id sent to the backend.
    /// </summary>
    public static string GetDeviceId()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}|{Global.AppName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<VersionCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var upToDate = new VersionCheckResult(false, string.Empty, string.Empty);

        if (Global.AppVersionCheckUrl.IsNullOrEmpty())
        {
            return upToDate;
        }

        try
        {
            using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

            var payload = JsonUtils.Serialize(new Dictionary<string, string>
            {
                { "device_id", GetDeviceId() },
                { "app_version", Utils.GetVersionInfo() },
                { "os", RuntimeInformation.OSDescription },
                { "arch", RuntimeInformation.OSArchitecture.ToString() },
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(Global.AppVersionCheckUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return upToDate;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return upToDate;
        }
    }

    private static VersionCheckResult Parse(string body)
    {
        try
        {
            var node = JsonUtils.ParseJson(body);
            if (node is null)
            {
                return new(false, string.Empty, string.Empty);
            }

            var type = ReadString(node, "type");
            var version = ReadString(node, "version");
            var message = ReadString(node, "message").IsNotEmpty()
                ? ReadString(node, "message")
                : ReadString(node, "msg");

            var outdated = type.Equals("outdated", StringComparison.OrdinalIgnoreCase);
            return new VersionCheckResult(outdated, version, message);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return new(false, string.Empty, string.Empty);
        }
    }

    private static string ReadString(JsonNode node, string name)
    {
        try
        {
            var value = node[name]?.ToString() ?? string.Empty;

            //JsonNode.ToString() may or may not keep the quotes, normalize it
            if (value.Length > 1 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            return value;
        }
        catch
        {
            return string.Empty;
        }
    }
}
