namespace ExamApp.Api.Models.Requests;

/// <summary>
/// <c>PUT /api/auth/me/locale</c> gövdesi (issue #181). Değer serbest metin olarak alınır;
/// doğrulama/normalizasyon <see cref="ExamApp.Foundation.Localization.SupportedLocales"/>
/// üzerinden controller'da yapılır (örn. "tr-TR" → "tr").
/// </summary>
public class UpdatePreferredLocaleRequest
{
    public string? PreferredLocale { get; set; }
}
