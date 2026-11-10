namespace UiProto;

/// <summary>Attempts both persistence operations even when either one fails.</summary>
internal static class PortRollback
{
    public static async Task<bool> SaveAsync(Func<Task<bool>> saveConfig, Func<bool> saveState)
    {
        var configSaved = false;
        var stateSaved = false;
        try { configSaved = await saveConfig(); }
        catch (Exception) { /* caller reports incomplete rollback */ }
        try { stateSaved = saveState(); }
        catch (Exception) { /* caller reports incomplete rollback */ }
        return configSaved && stateSaved;
    }
}
