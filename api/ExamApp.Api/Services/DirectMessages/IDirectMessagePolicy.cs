using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.DirectMessages;

/// <summary>Öğrencinin DB'den çözülmüş mesajlaşma bağlamı (client girdisi değil).</summary>
/// <param name="UserId">Students.UserId (exam/auth user id).</param>
/// <param name="StudentId">Canlı Students.Id — atamalar bu Id'yi hedefler.</param>
/// <param name="GradeId">Sınıf hedefli atamaların eşleşmesi için.</param>
/// <param name="SchoolId"><c>UserSchoolResolver</c> ile çözülen okul (okulsuz / belirsiz → null).</param>
public sealed record StudentMessagingContext(int UserId, int StudentId, int? GradeId, int? SchoolId);

/// <summary>Öğrencinin ilişkili olduğu (ve onaylı/aktif) öğretmen satırı — SQL projeksiyonu (member-init, filtrelenebilir).</summary>
public sealed class RelatedTeacherRow
{
    /// <summary>Teachers.Id.</summary>
    public int TeacherId { get; init; }

    /// <summary>Teachers.UserId.</summary>
    public int TeacherUserId { get; init; }

    /// <summary>(B) aynı okul — ikisi de okullu ve eşit.</summary>
    public bool ViaSchool { get; init; }

    /// <summary>(A) öğretmen öğrenciye şu an aktif bir atama yapmış.</summary>
    public bool ViaAssignment { get; init; }
}

/// <summary>
/// issue #106: doğrudan mesajlaşma yetki kuralının TEK noktası (<c>CanMessage(student, teacher)</c>). Liste ucu
/// (<see cref="RelatedTeachers"/>) ve gönderme ucu (<see cref="CanMessageAsync"/>) aynı IQueryable'ı kullanır — kural
/// yalnız burada değişir.
/// <para>
/// Öğrenci S, öğretmen T'ye yazabilir ⇔ T onaylı/aktif (<c>AccountApprovedAt != null</c> ve <c>AccountSuspendedAt == null</c>
/// — <c>ApprovedTeacherGuard</c>'ın baktığı iki kolon, satır bazında SQL'de) VE (B) aynı okul (S'nin <c>UserSchoolResolver</c>
/// okulu dolu ve <c>T.SchoolId</c>'ye eşit; null=null eşleşmez) VEYA (A) T'nin oluşturduğu (<c>WorksheetAssignment.CreateUserId</c>)
/// ve S'yi şu an kapsayan aktif bir atama var — <c>WorksheetAccess.ActiveAssignmentsFor</c> (silinmemiş, StartAt ≤ now &lt; EndAt,
/// öğrenci hedefli ya da öğrencinin sınıfı + okulu hedefli) TABANLI, ancak öğrencinin "atanan testler" listesinden FARKLI olarak
/// platform geneli (<c>IsPlatformWide</c>) sınıf atamaları sayılmaz (code review W1) — VE T, S'yi engellememiş. Onaylı booking
/// tek başına kapsam DEĞİLDİR. (B) yolu <c>DirectMessaging:AllowSameSchoolMessaging</c> bayrağına bağlıdır (varsayılan false,
/// #361 — öğrenci okul üyeliği doğrulanana kadar); kapalıyken kural yalnız (A) + onay + engel.
/// </para>
/// </summary>
public interface IDirectMessagePolicy
{
    /// <summary>Kullanıcının canlı öğrenci kaydı + okulu; öğrenci kaydı yoksa null.</summary>
    Task<StudentMessagingContext?> ResolveStudentAsync(int studentUserId, CancellationToken ct = default);

    /// <summary>
    /// A ∪ B kümesi (tekrarsız: öğretmen satırı başına bir satır, iki neden bayrakla). <paramref name="excludeBlocked"/>
    /// true iken öğrenciyi engelleyen öğretmenler dışarıda kalır (CanMessage); false iken yalnız ilişki + onay (öğretmen
    /// cevabı: engel cevabı kapatmaz).
    /// </summary>
    IQueryable<RelatedTeacherRow> RelatedTeachers(StudentMessagingContext student, DateTime nowUtc, bool excludeBlocked = true);

    /// <summary>CanMessage: öğrenci bu öğretmene ŞU AN yeni mesaj gönderebilir mi (ilişki + onay + engel yok).</summary>
    Task<bool> CanMessageAsync(StudentMessagingContext student, int teacherUserId, CancellationToken ct = default);

    /// <summary>Öğretmen cevabı: ilişki (A ∪ B) sürüyor ve öğretmen onaylı mı — engel dikkate alınmaz.</summary>
    Task<bool> HasActiveRelationAsync(StudentMessagingContext student, int teacherUserId, CancellationToken ct = default);
}
