namespace UiProto;

/// <summary>Tracks an explicit connect request while application initialization is pending.</summary>
internal sealed class PendingConnection
{
    private bool _requested;

    public void Request() => _requested = true;
    public void Cancel() => _requested = false;

    public bool Take()
    {
        var requested = _requested;
        _requested = false;
        return requested;
    }
}
