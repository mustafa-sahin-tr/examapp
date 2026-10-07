using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <inheritdoc cref="IParentChildAccess"/>
/// <remarks>
/// <para>
/// Bilinçli karar (#420 review): öğrencinin hesabının admin tarafından devre dışı bırakılması (Keycloak disable / askı) veli
/// erişimini KAPATMAZ. Bu bir hesap işlemidir, bağlantı koparma değil; veli çocuğunun geçmiş özetini görmeye devam eder.
/// Erişimi yalnızca bağlantının koparılması (Revoked) ya da tarafın silinmesi (soft-delete) bitirir.
/// </para>
/// <para>
/// V3/V4 (ve sonraki) veli uçları için ZORUNLU sıra: <b>kapı → audit → veri</b>. Önce bu kapı (null → 404, audit yazılmaz),
/// sonra <see cref="IParentAccessAuditLog.RecordAsync"/> (veri okunmadan önce), en son yalnızca toplam/özet sorguları. Kapıyı
/// atlayan ya da veriyi audit'ten önce okuyan uç yazmayın (örnek: <see cref="ParentDashboardService.GetChildSummaryAsync"/>).
/// </para>
/// </remarks>
public sealed class ParentChildAccess : IParentChildAccess
{
    private readonly AppDbContext _context;

    public ParentChildAccess(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Erişim veren bağlantı: Active, koparılmamış (<c>RevokedAt</c> boş — durum makinesi Revoked'a çekerken ikisini birlikte
    /// yazar; ikisi birden kontrol edilir ki yarım yazılmış bir satır erişim vermesin) ve iki taraf da silinmemiş.
    /// <c>ParentLinkService.IsOpen</c>'un Active yarısı; Pending hiçbir zaman erişim vermez.
    /// </summary>
    public static readonly Expression<Func<ParentStudentLink, bool>> GrantsAccess =
        l => l.Status == ParentStudentLinkStatus.Active
             && l.RevokedAt == null
             && !l.Parent.IsDeleted
             && !l.Student.IsDeleted;

    public async Task<ParentChildAccessGrant?> EnsureActiveChildAsync(int parentUserId, int studentId, CancellationToken ct = default)
    {
        if (parentUserId <= 0 || studentId <= 0)
            return null;

        // Tek sorgu; IgnoreQueryFilters YOK — soft-delete global filtresi de silinmiş tarafı ayrıca eler.
        return await _context.ParentStudentLinks
            .AsNoTracking()
            .Where(l => l.Parent.UserId == parentUserId && l.StudentId == studentId)
            .Where(GrantsAccess)
            .OrderBy(l => l.Id)
            .Select(l => new ParentChildAccessGrant(
                l.Id,
                l.ParentId,
                l.StudentId,
                l.Student.UserId,
                l.Student.GradeId,
                l.Student.SchoolVerifiedAt != null ? l.Student.SchoolId : null))
            .FirstOrDefaultAsync(ct);
    }
}
