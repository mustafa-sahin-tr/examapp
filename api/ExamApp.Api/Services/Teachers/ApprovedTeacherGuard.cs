using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Teachers;

/// <summary>Bir kullanıcının onaylı öğretmen olup olmadığının sonucu.</summary>
public enum TeacherApprovalCheck
{
    /// <summary>Teacher kaydı var ve ApprovalStatus == Approved.</summary>
    Approved = 0,

    /// <summary>Teacher kaydı var ama Pending / Rejected.</summary>
    NotApproved = 1,

    /// <summary>Kullanıcıya ait (silinmemiş) Teacher kaydı yok.</summary>
    NoTeacherProfile = 2
}

/// <summary>
/// "Yalnızca ONAYLI öğretmen" kuralının tek noktası (issue #61; #287 tüm öğretmen yazma uçlarında yeniden kullanacak).
/// Keycloak "Teacher" rolü tek başına yeterli değildir — başvurusu bekleyen/reddedilen öğretmen de o role sahip olabilir.
/// Admin muafiyeti çağıranın sorumluluğundadır (bu guard yalnızca Teacher tablosuna bakar).
/// </summary>
public interface IApprovedTeacherGuard
{
    Task<TeacherApprovalCheck> CheckAsync(int userId, CancellationToken ct = default);
}

public sealed class ApprovedTeacherGuard : IApprovedTeacherGuard
{
    private readonly AppDbContext _context;

    public ApprovedTeacherGuard(AppDbContext context) => _context = context;

    public async Task<TeacherApprovalCheck> CheckAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return TeacherApprovalCheck.NoTeacherProfile;

        var status = await _context.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => (TeacherApprovalStatus?)t.ApprovalStatus)
            .FirstOrDefaultAsync(ct);

        return status switch
        {
            null => TeacherApprovalCheck.NoTeacherProfile,
            TeacherApprovalStatus.Approved => TeacherApprovalCheck.Approved,
            _ => TeacherApprovalCheck.NotApproved,
        };
    }
}
