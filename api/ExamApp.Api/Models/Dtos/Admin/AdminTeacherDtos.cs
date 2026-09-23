namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Admin öğretmen listesi satırı (issue #152). Yanıt zarfı <see cref="Paged{T}"/>:
/// <c>{ pageNumber, pageSize, totalCount, items: [...] }</c>.
/// </summary>
public class AdminTeacherListItemDto
{
    /// <summary>Teacher kaydının id'si.</summary>
    public int Id { get; set; }

    /// <summary>auth-api kullanıcı id'si.</summary>
    public int UserId { get; set; }

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Bağlı okul; bağımsız/okulsuz öğretmen için null.</summary>
    public int? SchoolId { get; set; }

    /// <summary>Okul adı (School.Name); okul bağlantısı yoksa legacy Teacher.SchoolName; o da yoksa null.</summary>
    public string? SchoolName { get; set; }

    public bool IsIndependentTutor { get; set; }

    /// <summary><c>Pending</c> | <c>Approved</c> | <c>Rejected</c>.</summary>
    public string ApprovalStatus { get; set; } = string.Empty;

    /// <summary>
    /// Keycloak hesap durumu: true = aktif, false = devre dışı, null = bilinmiyor
    /// (auth-api/Keycloak erişilemedi ya da kullanıcı Keycloak'ta yok).
    /// </summary>
    public bool? IsEnabled { get; set; }
}
