using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.UserRoles;

/// <summary>
/// issue #419 review (D4 + re-review): veli rolü ile öğrenci/öğretmen rolleri AYNI hesapta bulunamaz — tek kural noktası.
/// Öğrenci/öğretmen kendini veliye çeviremez (başka çocuklara bağlanma); veli de kendini öğrenci/öğretmene çeviremez (kendi
/// davet kodunu üretip kendi veli hesabına bağlanma, veli olarak kazandığı erişimi taşıma). Öğrenci ↔ öğretmen dışlaması
/// ayrıca kayıt servislerinde (#234/#277) kilit altındadır; burada tekrarlanmaz.
/// </summary>
public interface IUserRoleExclusivity
{
    /// <summary>
    /// <paramref name="targetRole"/>'e kayıt bu kullanıcının mevcut rolüyle çakışıyor mu. <paramref name="claimedRoles"/>:
    /// JWT realm rolleri + auth-api profil rolü (biri gecikmeli/eksik olsa da diğeri yakalasın). DB satırı da bakılır.
    /// </summary>
    Task<bool> ConflictsAsync(int userId, UserRole targetRole, IReadOnlyCollection<string> claimedRoles, CancellationToken ct = default);
}

/// <inheritdoc cref="IUserRoleExclusivity"/>
public sealed class UserRoleExclusivity(AppDbContext context) : IUserRoleExclusivity
{
    private static readonly string[] StudentOrTeacher = [nameof(UserRole.Student), nameof(UserRole.Teacher)];
    private static readonly string[] ParentOnly = [nameof(UserRole.Parent)];

    public async Task<bool> ConflictsAsync(
        int userId, UserRole targetRole, IReadOnlyCollection<string> claimedRoles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claimedRoles);

        if (targetRole == UserRole.Parent)
        {
            return claimedRoles.Any(r => StudentOrTeacher.Contains(r, StringComparer.OrdinalIgnoreCase))
                   || await context.Students.AsNoTracking().AnyAsync(s => s.UserId == userId, ct)
                   || await context.Teachers.AsNoTracking().AnyAsync(t => t.UserId == userId, ct);
        }

        return claimedRoles.Any(r => ParentOnly.Contains(r, StringComparer.OrdinalIgnoreCase))
               || await context.Parents.AsNoTracking().AnyAsync(p => p.UserId == userId, ct);
    }
}
