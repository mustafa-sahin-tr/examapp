namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Admin öğrenci listesi satırı (issue #153). Yanıt zarfı <see cref="Paged{T}"/>:
/// <c>{ pageNumber, pageSize, totalCount, items: [...] }</c>.
/// </summary>
public class AdminStudentListItemDto
{
    /// <summary>Student kaydının id'si.</summary>
    public int Id { get; set; }

    // auth-api kullanıcı id'si (UserId) bilinçli olarak dönülmez: UI kullanmıyor ve reşit olmayan kullanıcının
    // iç kimliğini gereksiz yere sızdırır. Lookup için yalnızca sunucu tarafında kullanılır.

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// MASKELİ e-posta (issue #246): <c>a***@okul.k12.tr</c> — bkz. <see cref="ExamApp.Api.Helpers.EmailMask"/>.
    /// auth-api'den çözümlenemezse boş string. Tam adres bu listede hiç dönülmez.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// KISMİ öğrenci numarası (issue #262): yalnızca son 4 karakter, <c>****1234</c>; 4 karakter veya daha kısa numara
    /// tamamen gizli (<c>****</c>); numara yoksa boş string. Bkz. <see cref="ExamApp.Api.Helpers.StudentNumberMask"/>.
    /// </summary>
    public string StudentNumber { get; set; } = string.Empty;

    /// <summary>Bağlı okul; okulsuz öğrenci için null.</summary>
    public int? SchoolId { get; set; }

    /// <summary>Okul adı (School.Name); okul bağlantısı yoksa legacy Student.SchoolName; o da yoksa null.</summary>
    public string? SchoolName { get; set; }

    /// <summary>Sınıf; atanmamışsa null.</summary>
    public int? GradeId { get; set; }

    /// <summary>Sınıf adı (Grade.Name, örn. "9. Sınıf"); sınıf yoksa null.</summary>
    public string? GradeName { get; set; }

    /// <summary>
    /// Keycloak hesap durumu: true = aktif, false = devre dışı, null = bilinmiyor
    /// (auth-api/Keycloak erişilemedi ya da kullanıcı Keycloak'ta yok).
    /// </summary>
    public bool? IsEnabled { get; set; }
}
