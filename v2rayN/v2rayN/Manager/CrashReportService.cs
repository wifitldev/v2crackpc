using System.Net.Security;
using System.Net.Sockets;
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
    // 25 is the only port this server answers on (587/465 accept the connection
    // and then never send a banner), and it advertises STARTTLS + AUTH PLAIN.
    private const int SmtpPort = 25;
    private const string SmtpUserPassBlob =
        "fmga7W0Mr2qic9C94uE4UHIby7Hy8bQ818J/pC4EJPXil2VL2rOfk4MQtJinsWUT7tFcx1FujyaxvZa/DCykBQ==";
    private const string SmtpSalt = "cujRHcAqDBRX5zNlpaDfbA==";
    private const string KeyPass = "v2crackN-unionium-crash";
    private const string MailTo = "bugreport-pc@unionium.org";

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    private static readonly string CooldownFile = Path.Combine(Path.GetTempPath(), "v2crackN.crashreport.lock");
    private static int _sending;

    /// <summary>Network timeouts in ms - a dying process must not hang here.</summary>
    private const int TimeoutMs = 15000;

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

    /// <summary>
    ///     Delivers the report with a raw SMTP dialogue over STARTTLS.
    ///     System.Net.Mail.SmtpClient is not used: this server answers
    ///     "I'M NOT RELAY!!" to it, while the very same dialogue written
    ///     by hand is accepted - and the crash path must not depend on
    ///     a picky framework client.
    /// </summary>
    private static void Send(string subject, string body)
    {
        var creds = DecodeCredentials();
        if (creds == null)
        {
            return;
        }

        var (from, password) = creds.Value;

        using var tcp = new TcpClient();
        if (!tcp.ConnectAsync(SmtpHost, SmtpPort).Wait(TimeoutMs))
        {
            throw new IOException($"SMTP connect to {SmtpHost}:{SmtpPort} timed out");
        }
        tcp.ReceiveTimeout = TimeoutMs;
        tcp.SendTimeout = TimeoutMs;

        Stream stream = tcp.GetStream();
        SslStream? ssl = null;

        try
        {
            Expect(ReadReply(stream), "220", "banner");

            WriteLine(stream, "EHLO v2crackN.local");
            Expect(ReadReply(stream), "250", "EHLO");

            WriteLine(stream, "STARTTLS");
            Expect(ReadReply(stream), "220", "STARTTLS");

            ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            ssl.AuthenticateAsClient(SmtpHost);
            stream = ssl;

            WriteLine(stream, "EHLO v2crackN.local");
            Expect(ReadReply(stream), "250", "EHLO after TLS");

            // AUTH PLAIN: base64( NUL user NUL password )
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0" + from + "\0" + password));
            WriteLine(stream, "AUTH PLAIN " + token);
            Expect(ReadReply(stream), "235", "AUTH");

            WriteLine(stream, "MAIL FROM:<" + from + ">");
            Expect(ReadReply(stream), "250", "MAIL FROM");

            WriteLine(stream, "RCPT TO:<" + MailTo + ">");
            Expect(ReadReply(stream), "250", "RCPT TO");

            WriteLine(stream, "DATA");
            Expect(ReadReply(stream), "354", "DATA");

            var message = BuildMessage(from, subject, body);
            var bytes = Encoding.UTF8.GetBytes(message);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            Expect(ReadReply(stream), "250", "end of data");

            WriteLine(stream, "QUIT");
        }
        finally
        {
            ssl?.Dispose();
        }
    }

    /// <summary>
    ///     RFC 5321 message: ASCII headers plus a base64 body, so no encoding,
    ///     dot-stuffing or 8-bit surprise can get the server to drop it.
    /// </summary>
    private static string BuildMessage(string from, string subject, string body)
    {
        var sb = new StringBuilder();
        sb.Append("From: v2crackN <").Append(from).Append(">\r\n");
        sb.Append("To: <").Append(MailTo).Append(">\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");
        sb.Append("Date: ").Append(DateTime.Now.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append("@unionium.org>\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append("\r\n");

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        for (var i = 0; i < b64.Length; i += 76)
        {
            sb.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");
        }

        sb.Append(".\r\n");
        return sb.ToString();
    }

    /// <summary>Encodes a header as an encoded-word when it is not pure ASCII.</summary>
    private static string EncodeHeader(string value)
    {
        foreach (var c in value)
        {
            if (c > 127)
            {
                return "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
            }
        }
        return value;
    }

    private static void WriteLine(Stream stream, string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    /// <summary>Reads a (possibly multi-line) SMTP reply and returns its last line.</summary>
    private static string ReadReply(Stream stream)
    {
        var last = string.Empty;
        while (true)
        {
            var line = ReadLine(stream);
            if (line.Length == 0)
            {
                continue;
            }
            last = line;
            // "250 text" ends the reply, "250-more" continues it
            if (line.Length < 4 || line[3] != '-')
            {
                return last;
            }
        }
    }

    private static string ReadLine(Stream stream)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (true)
        {
            var n = stream.Read(one, 0, 1);
            if (n <= 0)
            {
                break;
            }
            var c = (char)one[0];
            if (c == '\n')
            {
                break;
            }
            if (c != '\r')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static void Expect(string reply, string code, string what)
    {
        if (!reply.StartsWith(code + " ", StringComparison.Ordinal) && reply != code)
        {
            throw new IOException($"SMTP {what} failed, expected {code}, got: {reply}");
        }
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
