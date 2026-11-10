namespace ServiceLib.Tests;

public class InstallerVersionTests
{
    [Test]
    public async Task IdentifiesDowngradesAndNormalizesMissingComponents()
    {
        await (VersionGuard.IsDowngrade("1.2.0.997", "1.2.0.996")
            && VersionGuard.IsDowngrade("2.0.0", "1.9.9")
            && !VersionGuard.IsDowngrade("1.2.0", "1.2.0.0")
            && !VersionGuard.IsDowngrade("1.2.0.996", "1.2.0.997")
            && !VersionGuard.IsDowngrade(null, "1.2.0.997")
            && !VersionGuard.IsDowngrade("unknown", "1.2.0.997")).Should().BeTrue();
    }
}
