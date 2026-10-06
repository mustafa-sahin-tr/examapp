using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ExamApp.TestSupport;

/// <summary>
/// issue #365 (S2): MinIO SDK'sından BAĞIMSIZ, sunucu tarafı SigV4 query-presign doğrulayıcı. MinIO'nun yaptığı gibi
/// istekteki yolu/query'yi DECODE edilmiş hâlinden kanonik biçime yeniden encode eder (S3 URI encoding) — böylece
/// gateway'in yolu farklı ama eşdeğer biçimde (ör. <c>%C3%A7</c> yerine ham <c>ç</c>) iletmesi doğru şekilde kabul
/// edilir, anlamı değiştiren dönüşümler (ör. <c>%2526</c> → <c>%26</c>) reddedilir. Paylaşılan dosya:
/// ExamApp.Api.Tests, ExamApp.Api.IntegrationTests ve Gateway.Tests'e link'lenir.
/// </summary>
public static class SigV4PresignVerifier
{
    public sealed record Result(bool Valid, string Reason, DateTime SignedAt, TimeSpan Expires);

    /// <summary><c>/img/{bucket}/{key}?X-Amz-...</c> göreli URL'sini gateway'in yapacağı gibi <c>/{bucket}/{key}</c>'e çevirip doğrular.</summary>
    public static Result VerifyImgUrl(string relativeImgUrl, string host, string secretKey, DateTime utcNow)
    {
        if (!relativeImgUrl.StartsWith("/img/", StringComparison.Ordinal))
            return new Result(false, "not an /img/ url", default, default);
        var rest = relativeImgUrl["/img".Length..];
        var q = rest.IndexOf('?');
        var rawPath = q < 0 ? rest : rest[..q];
        var rawQuery = q < 0 ? "" : rest[(q + 1)..];
        return Verify("GET", host, Uri.UnescapeDataString(rawPath), ParseQuery(rawQuery), secretKey, utcNow);
    }

    /// <summary>Downstream'in gördüğü (decode edilmiş) yol + query ile doğrulama.</summary>
    public static Result Verify(string method, string host, string decodedPath,
        IReadOnlyList<KeyValuePair<string, string>> decodedQuery, string secretKey, DateTime utcNow)
    {
        string Get(string name) => decodedQuery.FirstOrDefault(kv => kv.Key == name).Value ?? "";

        if (Get("X-Amz-Algorithm") != "AWS4-HMAC-SHA256")
            return new Result(false, "missing/unsupported X-Amz-Algorithm", default, default);
        var signature = Get("X-Amz-Signature");
        var credential = Get("X-Amz-Credential").Split('/');
        if (signature.Length == 0 || credential.Length != 5)
            return new Result(false, "missing signature/credential", default, default);
        if (Get("X-Amz-SignedHeaders") != "host")
            return new Result(false, "unexpected signed headers", default, default);
        var amzDate = Get("X-Amz-Date");
        var signedAt = DateTime.ParseExact(amzDate, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var expires = TimeSpan.FromSeconds(int.Parse(Get("X-Amz-Expires"), CultureInfo.InvariantCulture));

        var canonicalQuery = string.Join("&", decodedQuery
            .Where(kv => kv.Key != "X-Amz-Signature")
            .Select(kv => (K: UriEncode(kv.Key, encodeSlash: true), V: UriEncode(kv.Value, encodeSlash: true)))
            .OrderBy(kv => kv.K, StringComparer.Ordinal).ThenBy(kv => kv.V, StringComparer.Ordinal)
            .Select(kv => kv.K + "=" + kv.V));

        var canonicalRequest = string.Join("\n",
            method, UriEncode(decodedPath, encodeSlash: false), canonicalQuery, "host:" + host, "", "host", "UNSIGNED-PAYLOAD");

        var (date, region, service) = (credential[1], credential[2], credential[3]);
        var scope = $"{date}/{region}/{service}/aws4_request";
        var stringToSign = string.Join("\n", "AWS4-HMAC-SHA256", amzDate, scope, Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), date);
        key = Hmac(key, region);
        key = Hmac(key, service);
        key = Hmac(key, "aws4_request");
        var expected = Hex(Hmac(key, stringToSign));

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature)))
            return new Result(false, "SignatureDoesNotMatch", signedAt, expires);
        if (utcNow > signedAt + expires)
            return new Result(false, "expired", signedAt, expires);
        return new Result(true, "ok", signedAt, expires);
    }

    /// <summary>Go <c>url.ParseQuery</c> gibi: <c>+</c> boşluktur, sonra percent-decode.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ParseQuery(string rawQuery) =>
        rawQuery.Length == 0
            ? []
            : rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
            {
                var eq = pair.IndexOf('=');
                var k = eq < 0 ? pair : pair[..eq];
                var v = eq < 0 ? "" : pair[(eq + 1)..];
                return new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(k.Replace('+', ' ')), Uri.UnescapeDataString(v.Replace('+', ' ')));
            }).ToList();

    private static string UriEncode(string value, bool encodeSlash)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '~' || (c == '/' && !encodeSlash))
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
