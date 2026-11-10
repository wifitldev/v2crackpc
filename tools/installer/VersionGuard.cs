internal static class VersionGuard
{
    internal static bool IsDowngrade(string? installed, string current)
    {
        if (!Version.TryParse(installed, out var existing) || !Version.TryParse(current, out var incoming)) return false;
        static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        return Normalize(existing) > Normalize(incoming);
    }
}
