using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;

namespace ExamApp.Api.Services;

/// <summary>
/// issue #189: tenant (okul) bağlamını DB'den doğrulayarak çözer.
///
/// Davranış tablosu (Rol × kayıt durumu → sonuç):
///   Teacher, Teachers'da UserId eşleşen satır var, SchoolId dolu   -> o SchoolId
///   Teacher, satır var, SchoolId null (bağımsız öğretmen)          -> null
///   Teacher, Teachers'da satır yok                                 -> Students satırının SchoolId'si (yoksa null; #277 review)
///   Student, Students'da UserId eşleşen satır var, SchoolId dolu   -> o SchoolId
///   Student, satır var, SchoolId null                              -> null
///   Student, Students'da satır yok                                 -> null
///   Admin / Service / Parent / diğer roller                        -> null (sorgu atılmaz)
///
/// issue #234 (security): Teacher VEYA Student rolünde önce Teachers satırına bakılır; kullanıcının öğretmen
/// kaydı varsa okul kapsamı HER ZAMAN Teachers.SchoolId'den gelir — profil/önbellekteki rol Student olsa bile.
/// Aksi halde öğretmen, student/register ile kendine Students.SchoolId=X yazıp (rol önbellekte Student'a döner)
/// X okulunun kapsamına girebiliyordu. Kayıt uçları artık iki kaydı birbirini dışlayacak şekilde korur; bu kural
/// halihazırda iki kaydı olan (eski) kullanıcılar için de öğretmen kaydını esas alır. Kural ve çoklu canlı satır
/// davranışı <see cref="ExamApp.Api.Helpers.UserSchoolResolver"/>'da (issue #326 D3, tek kaynak).
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

        // issue #326 (D3): kural tek kaynakta (UserSchoolResolver) — canlı öğretmen satırı önce, yoksa canlı öğrenci satırı.
        // #259 unique index canlı satırı tekil kılar; index'siz ortamda çoklu canlı satır → null (güvenli taraf, tahmin yok).
        // issue #277 review (security HIGH): profil rolü (auth-api) JWT'den geri kalabilir — Teacher ↔ Student ayrımı burada
        // karar vermez. Öğretmen kaydı yoksa öğrenci kaydının okulu esas alınır (iki kayıt birbirini dışlar, #234/#277 madde 9);
        // dal kararı controller'da JWT'yle doğrulanmış etkin rolle verilir (EffectiveRole).
        return await UserSchoolResolver.ResolveAsync(_context, user.Id, ct);
    }
}
