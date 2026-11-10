internal static class PayloadCommit
{
    // Best-effort rollback for ordinary errors; abrupt termination is not recoverable here.
    internal static void Apply(string staging, string destination, IReadOnlyList<string> files, Action<int, string>? progress = null)
    {
        var backup = Path.Combine(Path.GetTempPath(), "v2crackN-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var changed = new List<(string Target, string? Backup)>();
        var keepBackup = false;
        try
        {
            foreach (var relative in files)
            {
                var target = Path.Combine(destination, relative);
                var parent = Path.GetDirectoryName(target)!;
                EnsureNoReparsePoints(parent);
                Directory.CreateDirectory(parent);
                EnsureNoReparsePoints(parent);
                if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Установка через ссылку на файл запрещена: " + target);
                string? saved = null;
                if (File.Exists(target))
                {
                    saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(target, saved);
                }
                ReplaceFile(Path.Combine(staging, relative), target);
                changed.Add((target, saved));
                progress?.Invoke(changed.Count, relative);
            }
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            foreach (var (target, saved) in changed.AsEnumerable().Reverse())
            {
                try
                {
                    if (saved is null) File.Delete(target);
                    else ReplaceFile(saved, target);
                }
                catch (Exception rollbackError) { failures.Add(rollbackError); }
            }
            if (failures.Count > 0)
            {
                keepBackup = true;
                throw new AggregateException($"Не удалось полностью восстановить предыдущую версию; резервные файлы: {backup}", new[] { error }.Concat(failures));
            }
            throw;
        }
        finally
        {
            if (!keepBackup)
                try { Directory.Delete(backup, true); } catch { /* temporary backup */ }
        }
    }

    private static void ReplaceFile(string source, string target)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".v2crackN-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, temporary, false);
            File.Move(temporary, target, true);
        }
        finally
        {
            // Cleanup must not mask the replacement outcome or trigger an incorrect rollback.
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* temporary file only */ }
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Установка через перенаправленный каталог запрещена: " + directory.FullName);
    }
}
