using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

/// <summary>
/// issue #189: bir kullanıcının tenant (okul) bağlamını sunucu tarafında, DB'den doğrulayarak
/// çözer. Rol × kayıt durumu davranış tablosu için SchoolContextResolver'a bakın.
/// </summary>
public interface ISchoolContextResolver
{
    /// <summary>
    /// Kullanıcının rolüne göre Teacher/Student tablosundan SchoolId okur. Admin, servis hesabı,
    /// Parent veya tanınmayan roller için sorgu atmadan null döner.
    /// </summary>
    Task<int?> ResolveSchoolIdAsync(UserProfileDto user, CancellationToken ct = default);
}
