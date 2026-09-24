using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.Teachers;

/// <summary>issue #287: öğretmen erişim kapısının (ApprovedTeacher policy) UI'ın özel davranış için okuduğu hata kodları.</summary>
public static class TeacherAccessErrorCodes
{
    /// <summary>Öğretmen hesabı henüz admin tarafından onaylanmadı (ilk başvuru Pending/Rejected ya da Teacher kaydı yok).</summary>
    public const string TeacherNotApproved = "TeacherNotApproved";
}

/// <summary>
/// issue #287: onaysız öğretmen, öğretmen özelliği gerektiren bir uca geldiğinde dönen 403 gövdesi.
/// JSON: <c>{ "success": false, "errorCode": "TeacherNotApproved", "message": "..." }</c> (mesaj istek diline göre).
/// </summary>
public sealed class TeacherNotApprovedResponseDto
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = TeacherAccessErrorCodes.TeacherNotApproved;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// issue #287: öğretmenin onay durumu — UI'ın zaten yüklediği profil yanıtlarına eklenir
/// (<c>POST api/auth/refresh</c> → <c>teacher</c> alt nesnesi, <c>GET api/teacher/check-teacher</c>).
/// </summary>
public sealed record TeacherApprovalState(bool TeacherAccountApproved, string TeacherApplicationStatus, string? RejectionReason)
{
    /// <summary>
    /// <see cref="TeacherAccountApproved"/>: öğretmen özellikleri açık mı (<see cref="Teacher.AccountApprovedAt"/> dolu).
    /// <see cref="TeacherApplicationStatus"/>: mevcut başvurunun durumu ("Pending" | "Approved" | "Rejected") — hesap onaylı
    /// öğretmenin sonraki (bağımsız tutor / okul) başvurusu da Pending olabilir. <see cref="RejectionReason"/> yalnızca
    /// Rejected iken dolu.
    /// </summary>
    public static TeacherApprovalState From(Teacher teacher) => new(
        teacher.AccountApprovedAt != null,
        teacher.ApprovalStatus.ToString(),
        teacher.ApprovalStatus == TeacherApprovalStatus.Rejected ? teacher.RejectionReason : null);
}
