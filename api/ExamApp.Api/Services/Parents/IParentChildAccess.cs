using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli uçlarının TEK yetki kapısı (issue #420, epic #407 V2; V3/V4 uçları da bunu kullanır). Velinin bir öğrencinin
/// verisine erişebilmesi için aralarında <b>Active</b> (koparılmamış) bir bağlantı olmalı ve ne veli ne öğrenci
/// soft-delete edilmiş olmalı. Diğer her durum (Pending, Revoked, başka velinin çocuğu, olmayan öğrenci, silinmiş taraf,
/// veli kaydı yok) aynı şekilde <c>null</c> döner — çağıran 404'e çevirir; hangi koşulun tutmadığı sızdırılmaz.
/// Uç sırası: kapı → audit → veri (bkz. <see cref="ParentChildAccess"/> notları). Öğrenci hesabının devre dışı bırakılması
/// erişimi kapatmaz (bilinçli; yalnızca koparma/silme kapatır).
/// </summary>
public interface IParentChildAccess
{
    /// <param name="parentUserId">Velinin exam/auth user id'si (<c>Parent.UserId</c>, token'dan çözülen).</param>
    /// <param name="studentId">Öğrencinin exam DB id'si (<c>Student.Id</c>).</param>
    Task<ParentChildAccessGrant?> EnsureActiveChildAsync(int parentUserId, int studentId, CancellationToken ct = default);
}

/// <summary>
/// Erişim verildi: bağlantı ve öğrencinin kapsam bilgileri. <see cref="VerifiedSchoolId"/> yalnızca DOĞRULANMIŞ okul
/// üyeliği (#361) — atama görünürlüğü öğrencinin kendi ekranlarıyla aynı kuralla hesaplansın.
/// </summary>
public sealed record ParentChildAccessGrant(
    int LinkId,
    int ParentId,
    int StudentId,
    int StudentUserId,
    int? GradeId,
    int? VerifiedSchoolId);
