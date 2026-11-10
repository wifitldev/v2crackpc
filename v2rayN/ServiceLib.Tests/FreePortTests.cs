namespace ServiceLib.Tests;

public class FreePortTests
{
    [Test]
    public async Task OutOfRangePreferredPortDoesNotEscapeValidation()
    {
        var port = Utils.GetFreePort(65536);
        await (port is >= 1 and <= 65535).Should().BeTrue();
    }
}
