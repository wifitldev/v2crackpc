using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace v2rayN.Manager;

/// <summary>
///     Sends a crash report to the support mailbox.
///     SMTP credentials are stored encrypted (AES-256-CBC, key derived via PBKDF2)
///     so they never appear in the binary as plain text.
///     A cooldown file prevents the mailbox from being flooded when the app
///     crashes over and over in a loop.
/// </summary>
public static class CrashReportService
{
    private const string SmtpHost = "mail.unionium.org";
    private const int SmtpPort = 587;
    private const string SmtpUserPassBlob =
        "fmga7W0Mr2qic9C94uE4UHIby7Hy8bQ818J/pC4EJPXil2VL2rOfk4MQtJinsWUT7tFcx1FujyaxvZa/DCykBQ==";
    private const string SmtpSalt = "cujRHcAqDBRX5zNlpaDfbA==";
    private const string KeyPass = "v2crackN-unionium-crash";
    private const string MailTo = "bugreport-pc@unionium.org";

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    private static readonly string CooldownFile = Path.Combine(Path.GetTempPath(), "v2crackN.crashreport.lock");
    private static int _sending;

    /// <summary>
    ///     Builds and sends the report. Never throws: a failure to send a crash
    ///     report must not turn into a second crash.
    /// </summary>
    /// <param name="source">Which handler caught it (dispatcher / appdomain / task)</param>
    /// <param name="ex">Caught exception, may be null for non-exception fatal events</param>
    public static void Report(string source, Exception? ex)
    {
        try
        {
            if (Interlocked.Exchange(ref _sending, 1) == 1)
            {
                return; // already sending in another thread
            }

            try
            {
                if (!IsCooldownExpired())
                {
                    return;
                }

                // Mark the attempt BEFORE sending, so a crash during send does not loop.
                File.WriteAllText(CooldownFile, DateTime.Now.ToString("O"));

                Send(BuildSubject(source, ex), BuildBody(source, ex));
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }
        catch
        {
            // Never let crash reporting itself throw.
        }
    }

    private static bool IsCooldownExpired()
    {
        try
        {
            if (!File.Exists(CooldownFile))
            {
                return true;
            }

            var last = File.ReadAllText(CooldownFile).Trim();
            if (!DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
            {
                return true;
            }

            return DateTime.Now - when >= Cooldown;
        }
        catch
        {
            return true;
        }
    }

    private static string BuildSubject(string source, Exception? ex)
    {
        var kind = ex?.GetType().Name ?? "Fatal";
        return $"[v2crackN {Utils.GetVersionInfo()}] Crash ({source}): {kind}";
    }

    private static string BuildBody(string source, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("v2crackN crash report");
        sb.AppendLine($"Time:     {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Source:   {source}");
        sb.AppendLine($"Version:  {Utils.GetVersionInfo()}");
        sb.AppendLine($"Runtime:  {Utils.GetVersion()}");
        sb.AppendLine($"OS:       {Environment.OSVersion}");
        sb.AppendLine($"Arch:     {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Exe:      {Utils.GetExePath()}");
        sb.AppendLine();

        if (ex != null)
        {
            sb.AppendLine("=== Exception ===");
            sb.AppendLine(ex.ToString());
            if (ex.InnerException != null)
            {
                sb.AppendLine("--- Inner ---");
                sb.AppendLine(ex.InnerException.ToString());
            }
            sb.AppendLine();
        }

        var tail = ReadLogTail(Utils.GetLogPath($"{DateTime.Now:yyyy-MM-dd}.txt"));
        if (tail.IsNotEmpty())
        {
            sb.AppendLine("=== Log tail ===");
            sb.AppendLine(tail);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Last ~64 KB of today's log, so the report stays within sane mail limits.
    /// </summary>
    private static string ReadLogTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            const long max = 64 * 1024;
            var start = Math.Max(0, fs.Length - max);
            fs.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var text = reader.ReadToEnd();
            if (start > 0)
            {
                // avoid a half-cut first line
                var nl = text.IndexOfAny(['\r', '\n']);
                if (nl >= 0 && nl + 1 < text.Length)
                {
                    text = text[(nl + 1)..];
                }
            }
            return text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void Send(string subject, string body)
    {
        var creds = DecodeCredentials();
        if (creds == null)
        {
            return;
        }

        var (from, password) = creds.Value;

        using var client = new SmtpClient(SmtpHost, SmtpPort)
        {
            EnableSsl = true, // STARTTLS
            Credentials = new NetworkCredential(from, password),
            Timeout = 15000, // ms, do not hang a dying process
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        using var msg = new MailMessage
        {
            From = new MailAddress(from),
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        msg.To.Add(MailTo);

        client.Send(msg);
    }

    /// <summary>
    ///     "login|password" or null when the payload cannot be decoded.
    /// </summary>
    private static (string login, string password)? DecodeCredentials()
    {
        try
        {
            var blob = Convert.FromBase64String(SmtpUserPassBlob);
            var salt = Convert.FromBase64String(SmtpSalt);

            var key = Rfc2898DeriveBytes.Pbkdf2(KeyPass, salt, 100000, HashAlgorithmName.SHA256, 32);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = blob.Take(16).ToArray();

            using var decryptor = aes.CreateDecryptor();
            var plain = decryptor.TransformFinalBlock(blob, 16, blob.Length - 16);
            var text = Encoding.UTF8.GetString(plain);

            var sep = text.IndexOf('|');
            if (sep <= 0 || sep == text.Length - 1)
            {
                return null;
            }

            return (text[..sep], text[(sep + 1)..]);
        }
        catch
        {
            return null;
        }
    }
}
