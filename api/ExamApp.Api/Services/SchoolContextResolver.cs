using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

/// <summary>
/// issue #189: tenant (okul) bağlamını DB'den doğrulayarak çözer.
///
/// Davranış tablosu (Rol × kayıt durumu → sonuç):
///   Teacher, Teachers'da UserId eşleşen satır var, SchoolId dolu   -> o SchoolId
///   Teacher, satır var, SchoolId null (bağımsız öğretmen)          -> null
///   Teacher, Teachers'da satır yok                                 -> null
///   Student, Students'da UserId eşleşen satır var, SchoolId dolu   -> o SchoolId
///   Student, satır var, SchoolId null                              -> null
///   Student, Students'da satır yok                                 -> null
///   Admin / Service / Parent / diğer roller                        -> null (sorgu atılmaz)
///
/// #194 notu: kullanıcının okulu değiştiğinde (ör. transfer), bu resolver'ın sonucu
/// UserProfileCacheService üzerinden cache'lenir — okul değişikliğinde ilgili keycloakId için
/// UserProfileCacheService.RemoveAsync çağrılmalı (bkz. Teacher/StudentController Save akışı).
/// </summary>
public class SchoolContextResolver : ISchoolContextResolver
{
    private readonly AppDbContext _context;

    public SchoolContextResolver(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int?> ResolveSchoolIdAsync(UserProfileDto user, CancellationToken ct = default)
    {
        if (user is null)
            return null;

        return user.Role switch
        {
            nameof(UserRole.Teacher) => await _context.Teachers
                .AsNoTracking()
                .Where(t => t.UserId == user.Id)
                .Select(t => (int?)t.SchoolId)
                .FirstOrDefaultAsync(ct),

            nameof(UserRole.Student) => await _context.Students
                .AsNoTracking()
                .Where(s => s.UserId == user.Id)
                .Select(s => (int?)s.SchoolId)
                .FirstOrDefaultAsync(ct),

            _ => null
        };
    }
}
