using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

// NOT (#228): Bu dosyanın beş kopyası var (api/ExamApp.Api, auth-api, Services/BadgeService,
// Services/Gateway, finance-api/finance-api). Gateway ve finance-api ExamApp.Foundation'ı
// referans etmediği için ortak kütüphaneye taşınmadı. Kopyalar arasında YALNIZCA `Sections`
// listesi farklı olabilir; redaksiyon bölümü (işaret yorumları arası) birebir aynı tutulmalıdır
// (tests/ExamApp.Api.Tests/Helpers/StartupConfigDumpTests.cs bunu doğrular).
internal static class StartupConfigDump
{
    private static readonly string[] Sections =
    {
        "Keycloak",
        "ConnectionStrings",
        "Redis",
        "RabbitMQ",
        "MinioConfig",
        "QuestionAnalyzer",
        "Server",
        "Cors",
    };

    public static void Print(IConfiguration config, string environmentName, int? kestrelPort = null)
        => Write(Console.Out, config, environmentName, kestrelPort);

    internal static void Write(TextWriter writer, IConfiguration config, string environmentName, int? kestrelPort = null)
    {
        writer.WriteLine("========================================");
        writer.WriteLine($"🚀 Effective configuration ({environmentName})");
        if (kestrelPort.HasValue)
        {
            writer.WriteLine($"Kestrel:Port = {kestrelPort.Value}");
        }

        foreach (var section in Sections)
        {
            PrintSection(writer, config, section);
        }

        writer.WriteLine("========================================");
    }

    private static void PrintSection(TextWriter writer, IConfiguration config, string rootKey)
    {
        var section = config.GetSection(rootKey);
        if (!section.GetChildren().Any())
        {
            return;
        }

        foreach (var pair in section.AsEnumerable(makePathsRelative: true))
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            var fullKey = $"{rootKey}:{pair.Key}";
            writer.WriteLine($"{fullKey} = {Sanitize(fullKey, pair.Value)}");
        }
    }

    // REDACTION-BEGIN
    private const string Mask = "***";
    private const int MaxValueLength = 400;

    // `scheme://user:password@host` (amqp, redis, postgres URI'leri). Parola '@' içerebilir diye
    // gizli kısım greedy: authority içindeki SON '@'a kadar maskelenir.
    private static readonly Regex UriUserInfoPassword = new(
        @"(?<prefix>[a-z][a-z0-9+.\-]*://[^:/?#@\s]*):(?<secret>[^/?#\s]*)@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Herhangi bir `anahtar=` (boşluklu/önekli anahtarlar dahil: `SSL Password`, `SslPassword`);
    // dizenin başında ya da ';' (ADO/Npgsql), ',' (StackExchange.Redis), '?'/'&' (query-string)
    // veya satır sonundan sonra. Hassas olup olmadığına IsSensitiveKeyName karar verir.
    private static readonly Regex KeyValueKey = new(
        @"(?:^|(?<sep>[;,?&\r\n]))[ \t]*(?<key>[a-z][a-z0-9 _.\-]*?)[ \t]*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly char[] LineBreaks = { '\r', '\n' };

    internal static string Sanitize(string key, string value)
    {
        var lowerKey = key.ToLowerInvariant();
        var lastSegment = key[(key.LastIndexOf(':') + 1)..];
        if (lowerKey.Contains("password") ||
            lowerKey.Contains("secret") ||
            lowerKey.EndsWith(":key") ||
            lowerKey.Contains("token") ||
            lowerKey.Contains("apikey") ||
            IsSensitiveKeyName(lastSegment))
        {
            return Mask;
        }

        // Anahtar adından bağımsız olarak HER değer taranır: Aspire `ConnectionStrings:rabbitmq`
        // (amqp URI) ve `ConnectionStrings:redis` / `Redis:Configuration` (',' ayraçlı) gibi
        // biçimler de parola taşır.
        var redacted = RedactSecrets(value);

        if (redacted.Length > MaxValueLength)
        {
            return redacted.Substring(0, MaxValueLength) + "…";
        }

        return redacted;
    }

    /// <summary>
    /// Anahtar adı (harf/rakam dışı karakterler atılıp küçültülmüş) parola/gizli anahtar mı?
    /// password/passwd/pwd/psw ile biten, tam olarak "pass" olan, "secret"/"token" içeren
    /// ya da "key" ile biten (AccessKey, SecretKey, AccountKey, SharedAccessKey, ApiKey) adlar.
    /// "Database", "Keepalive", "Passfile", "Username" gibi adlar eşleşmez.
    /// </summary>
    internal static bool IsSensitiveKeyName(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        var k = sb.ToString();
        if (k.Length == 0)
        {
            return false;
        }

        return k == "pass" ||
               k.EndsWith("password", StringComparison.Ordinal) ||
               k.EndsWith("passwd", StringComparison.Ordinal) ||
               k.EndsWith("pwd", StringComparison.Ordinal) ||
               k.EndsWith("psw", StringComparison.Ordinal) ||
               k.EndsWith("key", StringComparison.Ordinal) ||
               k.Contains("secret", StringComparison.Ordinal) ||
               k.Contains("token", StringComparison.Ordinal);
    }

    internal static string RedactSecrets(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var withoutUriPasswords = UriUserInfoPassword.Replace(input, "${prefix}:" + Mask + "@");
        return RedactKeyValueSecrets(withoutUriPasswords);
    }

    private static string RedactKeyValueSecrets(string input)
    {
        var sb = new StringBuilder(input.Length);
        var copiedUpTo = 0;
        var match = KeyValueKey.Match(input);
        while (match.Success)
        {
            var valueStart = match.Index + match.Length;
            if (!IsSensitiveKeyName(match.Groups["key"].Value))
            {
                match = valueStart < input.Length ? KeyValueKey.Match(input, valueStart) : Match.Empty;
                continue;
            }

            char? separatorBeforeKey = match.Groups["sep"].Success ? match.Groups["sep"].Value[0] : null;
            var valueEnd = FindValueEnd(input, valueStart, separatorBeforeKey);
            sb.Append(input, copiedUpTo, valueStart - copiedUpTo).Append(Mask);
            copiedUpTo = valueEnd;
            match = valueEnd < input.Length ? KeyValueKey.Match(input, valueEnd) : Match.Empty;
        }

        sb.Append(input, copiedUpTo, input.Length - copiedUpTo);
        return sb.ToString();
    }

    // Değerin bittiği yer. Satır sonu her zaman ayraçtır.
    //  - ';' sonrası anahtar (Npgsql/ADO): ';'e kadar (parola ',' içerebilir).
    //  - ',' sonrası anahtar (StackExchange.Redis): ','e kadar (parola ';' içerebilir).
    //  - '?' / '&' sonrası anahtar (query-string): '&' veya '#'e kadar.
    //  - Satır başındaki anahtar belirsizdir (`Password=a,b3` Npgsql mi Redis mi?): değerin geri
    //    kalanında ',' varsa satır sonuna kadar HEPSİ maskelenir (güvenli taraf); yalnızca ';' varsa
    //    ';'e kadar; hiçbiri yoksa satır sonuna kadar.
    private static int FindValueEnd(string input, int start, char? separatorBeforeKey)
    {
        var lineEnd = input.IndexOfAny(LineBreaks, start);
        if (lineEnd < 0)
        {
            lineEnd = input.Length;
        }

        var i = SkipQuotedValue(input, start, lineEnd);

        char[] terminators;
        switch (separatorBeforeKey)
        {
            case ';':
                terminators = new[] { ';' };
                break;
            case ',':
                terminators = new[] { ',' };
                break;
            case '?':
            case '&':
                terminators = new[] { '&', '#' };
                break;
            default:
                var rest = input.AsSpan(i, lineEnd - i);
                if (rest.Contains(',') || !rest.Contains(';'))
                {
                    return lineEnd;
                }

                terminators = new[] { ';' };
                break;
        }

        var end = input.IndexOfAny(terminators, i, lineEnd - i);
        return end < 0 ? lineEnd : end;
    }

    // Tırnaklı değer (Password="a;b" / 'a,b'; çift tırnak kaçışı "" desteklenir): kapanış tırnağından
    // sonraki konumu döner; tırnak yoksa start'ı, tırnak kapanmıyorsa satır sonunu.
    private static int SkipQuotedValue(string input, int start, int lineEnd)
    {
        var i = start;
        while (i < lineEnd && (input[i] == ' ' || input[i] == '\t'))
        {
            i++;
        }

        if (i >= lineEnd || (input[i] != '"' && input[i] != '\''))
        {
            return start;
        }

        var quote = input[i++];
        while (i < lineEnd)
        {
            if (input[i] == quote)
            {
                if (i + 1 < lineEnd && input[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return lineEnd;
    }
    // REDACTION-END
}
