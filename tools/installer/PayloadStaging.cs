using System.IO.Compression;

internal static class PayloadStaging
{
    private static readonly string[] UserItems = ["guiConfigs", "guiBackups", "guiLogs", "guiTemps", "binConfigs", "proto-settings.json", "payload-state.json"];

    private static readonly string[] RequiredFiles = ["v2crackN.exe", "bin/xray/xray.exe", "bin/sing_box/sing-box.exe", "bin/geoip.dat", "bin/geosite.dat", "bin/wintun.dll", "bin/dpi/ciadpi.exe"];

    internal static void ValidateRequiredFiles(IReadOnlyCollection<string> files)
    {
        var paths = new HashSet<string>(files.Select(p => p.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredFiles)
            if (!paths.Contains(required))
                throw new InvalidDataException("В пакете отсутствует обязательный файл: " + required);
        var allowed = new HashSet<string>(RequiredFiles, StringComparer.OrdinalIgnoreCase) { "README.txt" };
        foreach (var path in paths)
            if (!allowed.Contains(path))
                throw new InvalidDataException("Пакет содержит неизвестный файл: " + path);
    }

    private static bool IsInvalidComponent(string part)
    {
        if (part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
            part.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return true;
        var name = part.Split('.')[0];
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
        return name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               name[3] is >= '1' and <= '9';
    }

    internal static void CopyExact(Stream input, Stream output, long expectedLength)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (count > expectedLength - total)
                throw new InvalidDataException("Размер файла не соответствует размеру в пакете.");
            output.Write(buffer, 0, count);
            total += count;
        }
        if (total != expectedLength)
            throw new InvalidDataException("Размер файла не соответствует размеру в пакете.");
    }

    // Fully validate and extract before modifying the installation. Caller owns staging.
    internal static List<string> Extract(ZipArchive zip, string staging)
    {
        var root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        const int maxFiles = 256;
        const long maxFileBytes = 512L * 1024 * 1024;
        const long maxTotalBytes = 1024L * 1024 * 1024;
        if (entries.Count > maxFiles)
            throw new InvalidDataException("В пакете слишком много файлов.");
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            if (entry.Length <= 0 || entry.Length > maxFileBytes || entry.Length > maxTotalBytes - totalBytes)
                throw new InvalidDataException("Недопустимый размер файла в пакете.");
            totalBytes += entry.Length;
        }
        var volume = Path.GetPathRoot(Path.GetFullPath(staging));
        if (!string.IsNullOrEmpty(volume) && new DriveInfo(volume).AvailableFreeSpace < totalBytes)
            throw new IOException("Недостаточно свободного места во временной папке для распаковки пакета.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var entry in entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var parts = normalized.Split('/');
            if (parts.Any(IsInvalidComponent) ||
                UserItems.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Пакет содержит недопустимый путь или пользовательские данные.");
            var relative = Path.Combine(parts);
            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(destination))
                throw new InvalidDataException("Пакет содержит повторяющийся или некорректный путь.");
            result.Add(relative);
        }
        // Detect file/directory conflicts in either archive order before extracting.
        foreach (var path in paths)
        {
            for (var parent = Path.GetDirectoryName(path); parent != null && parent.StartsWith(root, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
                if (paths.Contains(parent))
                    throw new InvalidDataException("Пакет содержит конфликт файла и каталога.");
        }
        for (var i = 0; i < entries.Count; i++)
        {
            var path = Path.Combine(root, result[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entries[i].Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            CopyExact(input, output, entries[i].Length);
        }
        return result;
    }
}
