using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #1276 (#1071 S1): kullanıcının okul yöneticisi olduğu okul. JWT claim'ine değil DB'ye bakar
/// (<c>UserSchoolResolver</c> gibi). Bayrak tek başına yetmez: öğretmen okula bağlı, hesabı onaylı, askısız ve
/// bağımsız değilse geçerlidir.
/// </summary>
public interface ISchoolAdminResolver
{
    /// <summary>Kullanıcının yöneticisi olduğu okul; değilse null.</summary>
    Task<int?> GetAdministeredSchoolIdAsync(int userId, CancellationToken ct = default);

    /// <summary>Kullanıcı <paramref name="schoolId"/> okulunun yöneticisi mi.</summary>
    Task<bool> IsSchoolAdminAsync(int userId, int schoolId, CancellationToken ct = default);
}

public class SchoolAdminResolver : ISchoolAdminResolver
{
    private readonly AppDbContext _context;

    public SchoolAdminResolver(AppDbContext context) => _context = context;

    public Task<int?> GetAdministeredSchoolIdAsync(int userId, CancellationToken ct = default) =>
        ActiveAdmins(userId).Select(t => t.SchoolId).FirstOrDefaultAsync(ct);

    public Task<bool> IsSchoolAdminAsync(int userId, int schoolId, CancellationToken ct = default) =>
        ActiveAdmins(userId).AnyAsync(t => t.SchoolId == schoolId, ct);

    private IQueryable<Teacher> ActiveAdmins(int userId) =>
        _context.Teachers.AsNoTracking().Where(t => t.UserId == userId
            && t.IsSchoolAdmin
            && t.SchoolId != null
            && !t.IsIndependentTutor
            && t.AccountApprovedAt != null
            && t.AccountSuspendedAt == null);
}
