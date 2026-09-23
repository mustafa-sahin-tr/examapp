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
/// issue #234 (security): Teacher VEYA Student rolünde önce Teachers satırına bakılır; kullanıcının öğretmen
/// kaydı varsa okul kapsamı HER ZAMAN Teachers.SchoolId'den gelir — profil/önbellekteki rol Student olsa bile.
/// Aksi halde öğretmen, student/register ile kendine Students.SchoolId=X yazıp (rol önbellekte Student'a döner)
/// X okulunun kapsamına girebiliyordu. Kayıt uçları artık iki kaydı birbirini dışlayacak şekilde korur; bu kural
/// halihazırda iki kaydı olan (eski) kullanıcılar için de öğretmen kaydını esas alır. Teachers.UserId unique
/// değil — deterministik seçim için OrderBy(Id) (TeacherService.Save ile aynı).
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

        if (user.Role is not (nameof(UserRole.Teacher) or nameof(UserRole.Student)))
            return null;

        var teacherRow = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .OrderBy(t => t.Id)
            .Select(t => new { t.SchoolId })
            .FirstOrDefaultAsync(ct);

        if (teacherRow != null)
            return teacherRow.SchoolId;

        if (user.Role != nameof(UserRole.Student))
            return null;

        return await _context.Students
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderBy(s => s.Id)
            .Select(s => (int?)s.SchoolId)
            .FirstOrDefaultAsync(ct);
    }
}
