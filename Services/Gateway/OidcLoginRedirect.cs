using System.Text.RegularExpressions;

/// <summary>
/// <c>GET /oidc-login</c> → Keycloak authorization isteği (issue #347).
///
/// Eskiden gateway <c>state</c>'i kendisi kuruyordu (<c>"&lt;Server:BaseUrl&gt;~&lt;intent&gt;"</c>) ve callback
/// state'in <c>~</c> öncesini doğrulamadan yönlendirme tabanı olarak kullanıyordu (open redirect); PKCE yoktu.
/// Artık <c>state</c> ve S256 <c>code_challenge</c> login'i başlatan tarayıcıda (auth-ui) üretilip
/// sessionStorage'a yazılır; gateway yalnızca biçimlerini doğrulayıp Keycloak'a iletir.
///
/// İstemci tarafından üretilmiş state/challenge taşımayan istekler (eski <c>/oidc-login?intent=…</c>
/// bağlantıları, yer imleri) Keycloak'a gönderilmez; auth-ui'nin <c>/app/login</c> sayfasına yönlendirilir —
/// o sayfa state + PKCE'yi üretip tekrar buraya gelir. Böylece doğrulanamayan bir akış hiç başlamaz.
/// </summary>
public static class OidcLoginRedirect
{
    public const string Path = "/oidc-login";

    /// <summary>State/challenge'ı olmayan istekler login'i başlatması için buraya gönderilir.</summary>
    public const string LoginPagePath = "/app/login";

    // auth-ui 32 rastgele bayt → 43 karakter base64url üretir; biraz pay bırakılır.
    // Not: .NET regex'inde `$` sondaki tek satır sonundan önce de eşleşir; bu yüzden tüm desenler `\z` ile biter.
    private static readonly Regex StatePattern = new(@"^[A-Za-z0-9_-]{32,128}\z", RegexOptions.CultureInvariant);

    // RFC 7636 §4.2: S256 challenge = base64url(sha256) → padding'siz tam 43 karakter.
    private static readonly Regex ChallengePattern = new(@"^[A-Za-z0-9_-]{43}\z", RegexOptions.CultureInvariant);

    // BCP 47 alt kümesi: "tr", "en", "en-US" (issue #186 ui_locales).
    private static readonly Regex LocalePattern = new(@"^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})?\z", RegexOptions.CultureInvariant);

    private static readonly string[] RegisterIntents = ["student", "teacher", "parent"];

    /// <summary>
    /// Yönlendirilecek URL: geçerli state + S256 challenge varsa Keycloak <c>auth</c> (veya kayıt niyetinde
    /// <c>registrations</c>) ucu; yoksa <see cref="LoginPagePath"/> (göreli, aynı origin).
    /// </summary>
    public static string BuildRedirect(IQueryCollection query, string host, string realm, string clientId, string redirectUri)
    {
        var intent = NormalizeIntent(query["intent"].ToString());
        var uiLocales = query["ui_locales"].ToString();
        var hasLocale = LocalePattern.IsMatch(uiLocales);

        var state = query["state"].ToString();
        var codeChallenge = query["code_challenge"].ToString();
        var method = query["code_challenge_method"].ToString();

        if (!StatePattern.IsMatch(state) || !ChallengePattern.IsMatch(codeChallenge) || method != "S256")
        {
            var loginParams = new List<string>();
            if (intent is not null) loginParams.Add($"intent={intent}");
            if (hasLocale) loginParams.Add($"ui_locales={Uri.EscapeDataString(uiLocales)}");
            return loginParams.Count == 0 ? LoginPagePath : $"{LoginPagePath}?{string.Join('&', loginParams)}";
        }

        // Kayıt niyeti Keycloak'ın `registrations` ucuna gider (auth ile aynı parametreler; prompt=create
        // Keycloak 25+ ister). Niyet artık state'e gömülmez; auth-ui onu sessionStorage kaydında tutar.
        var endpoint = intent is null ? "auth" : "registrations";

        var authUrl = $"{host}/auth/realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/{endpoint}"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code&scope=openid"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + "&code_challenge_method=S256";
        if (hasLocale)
        {
            authUrl += $"&ui_locales={Uri.EscapeDataString(uiLocales)}";
        }
        return authUrl;
    }

    private static string? NormalizeIntent(string raw)
    {
        var intent = raw.Trim().ToLowerInvariant();
        return Array.IndexOf(RegisterIntents, intent) >= 0 ? intent : null;
    }
}
