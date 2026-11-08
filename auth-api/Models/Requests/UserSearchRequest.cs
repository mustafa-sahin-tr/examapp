using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Requests;

/// <summary>
/// Issue #1030: admin plan ataması ekranının kullanıcı araması (exam API üzerinden, yalnız servis). Ad ya da e-postada geçen
/// metin; isteğe bağlı rol süzgeci. Sonuç <see cref="MaxResults"/> ile sınırlı: tablo dökümü değil, seçim listesi.
/// </summary>
public class UserSearchRequest
{
    public const int MinQueryLength = 2;
    public const int MaxResults = 20;

    [Required, MinLength(MinQueryLength), MaxLength(100)]
    public string Query { get; set; } = string.Empty;

    /// <summary><c>Student</c> | <c>Teacher</c> | <c>Parent</c>; boş = hepsi.</summary>
    public string? Role { get; set; }
}
