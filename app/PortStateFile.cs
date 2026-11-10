using System.IO;
using System.Text.Json;

namespace UiProto;

internal static class PortStateFile
{
    public static bool Save(string path, int port)
    {
        if (!PortRules.IsValidSocksPort(port)) return false;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, int> { ["socksPort"] = port },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
