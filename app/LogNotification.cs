namespace UiProto;

/// <summary>Delivers diagnostic messages without allowing observers to affect application operations.</summary>
internal static class LogNotification
{
    public static void Publish(Action<string>? handlers, string message)
    {
        if (handlers is null || string.IsNullOrWhiteSpace(message)) return;
        var text = message.Trim();
        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try { handler(text); }
            catch (Exception) { /* logging observers must not affect connection state */ }
        }
    }
}
