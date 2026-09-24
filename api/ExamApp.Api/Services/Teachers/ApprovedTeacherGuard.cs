using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Teachers;

/// <summary>Bir kullanıcının onaylı öğretmen olup olmadığının sonucu.</summary>
public enum TeacherApprovalCheck
{
    /// <summary>Teacher kaydı var ve öğretmen HESABI admin tarafından onaylanmış (<see cref="Teacher.AccountApprovedAt"/> dolu).</summary>
    Approved = 0,

    /// <summary>Teacher kaydı var ama hesap henüz onaylanmamış (ilk başvuru Pending ya da Rejected).</summary>
    NotApproved = 1,

    /// <summary>Kullanıcıya ait (silinmemiş) Teacher kaydı yok.</summary>
    NoTeacherProfile = 2
}

/// <summary>
/// "Yalnızca ONAYLI öğretmen" kuralının tek noktası (issue #61; #287 tüm öğretmen uçlarında <c>ApprovedTeacher</c>
/// policy'si üzerinden yeniden kullanılır). Keycloak "Teacher" rolü tek başına yeterli değildir — kayıtta rol hemen
/// verilir, hesap admin onayına kadar bekler (#287).
/// <para>
/// issue #287: karar <see cref="Teacher.AccountApprovedAt"/>'a göre verilir, <see cref="Teacher.ApprovalStatus"/>'a DEĞİL.
/// ApprovalStatus mevcut başvurunun durumudur ve sonraki geçişlerde yeniden Pending olabilir (onaylı okul öğretmeni
/// bağımsız tutor başvurusu yapınca); böyle bir öğretmen öğretmen özelliklerini kaybetmez.
/// </para>
/// Admin muafiyeti çağıranın sorumluluğundadır (bu guard yalnızca Teacher tablosuna bakar).
/// </summary>
public interface IApprovedTeacherGuard
{
    Task<TeacherApprovalCheck> CheckAsync(int userId, CancellationToken ct = default);
}

/// <summary>
/// Scoped: sonuç istek boyunca kullanıcı başına önbelleklenir — aynı istekte policy handler'ı ve servis katmanı
/// (ör. <c>TopicStudyLinkService</c>) aynı kontrolü tekrar DB'ye gitmeden yapar.
/// </summary>
public sealed class ApprovedTeacherGuard : IApprovedTeacherGuard
{
    private readonly AppDbContext _context;
    private readonly Dictionary<int, TeacherApprovalCheck> _cache = new();

    public ApprovedTeacherGuard(AppDbContext context) => _context = context;

    public async Task<TeacherApprovalCheck> CheckAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return TeacherApprovalCheck.NoTeacherProfile;

        if (_cache.TryGetValue(userId, out var cached))
            return cached;

        // Canlı satır tektir (#259 filtreli unique index); OrderBy ResolveTeacherRecordAsync ile aynı deterministik sıra.
        var row = await _context.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Id)
            .Select(t => new { t.AccountApprovedAt })
            .FirstOrDefaultAsync(ct);

        var result = row switch
        {
            null => TeacherApprovalCheck.NoTeacherProfile,
            { AccountApprovedAt: not null } => TeacherApprovalCheck.Approved,
            _ => TeacherApprovalCheck.NotApproved,
        };

        _cache[userId] = result;
        return result;
    }
}
