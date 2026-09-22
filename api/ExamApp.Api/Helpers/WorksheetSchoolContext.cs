using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #191: <see cref="WorksheetAccess"/> kural motoruna verilen "sahibin okulu / istekçinin okulu"
/// girdilerinin DB'den (<c>Teachers.SchoolId</c>) çözülmesi. Kural sınıfı DB'siz/saf kalsın diye ayrı;
/// okul değerleri her zaman sunucu tarafında çözülür, client'tan gelen değere güvenilmez.
/// </summary>
public static class WorksheetSchoolContext
{
    /// <summary>
    /// Kullanıcının okulu — <c>Teachers.SchoolId</c>'den. Öğretmen kaydı yoksa null (okulsuz sayılır).
    /// </summary>
    public static Task<int?> ResolveTeacherSchoolIdAsync(this AppDbContext context, int userId, CancellationToken ct = default)
    {
        return context.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.SchoolId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Tekil worksheet kararları (detay/atama/kopya/düzenleme) için (sahibin okulu, istekçinin okulu) çifti.
    /// Yalnızca karar için gerekliyse sorgu atar: worksheet SchoolOnly değilse, istekçi admin veya sahibi
    /// ise DB'ye gitmeden <c>(null, null)</c> döner (bu durumlarda CanView/CanAssign zaten okula bakmaz).
    /// Gerekirse tek sorguda iki Teacher satırını çeker.
    /// </summary>
    public static async Task<(int? OwnerSchoolId, int? RequesterSchoolId)> ResolveSchoolContextAsync(
        this AppDbContext context, Worksheet worksheet, int userId, bool isAdmin, CancellationToken ct = default)
    {
        if (worksheet.TeacherSharing != WorksheetTeacherSharing.SchoolOnly || isAdmin)
            return (null, null);

        var ownerUserId = worksheet.CreateUserId;
        if (!ownerUserId.HasValue || ownerUserId.Value <= 0 || ownerUserId.Value == userId)
            return (null, null);

        var rows = await context.Teachers.AsNoTracking()
            .Where(t => t.UserId == ownerUserId.Value || t.UserId == userId)
            .Select(t => new { t.UserId, t.SchoolId })
            .ToListAsync(ct);

        int? ownerSchoolId = rows.FirstOrDefault(r => r.UserId == ownerUserId.Value)?.SchoolId;
        int? requesterSchoolId = rows.FirstOrDefault(r => r.UserId == userId)?.SchoolId;
        return (ownerSchoolId, requesterSchoolId);
    }
}
