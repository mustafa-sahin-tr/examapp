using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Seed.Cleanup;

/// <summary>
/// Seed test verisini üç sistemden kaldırır (issue #218): exam <c>Teacher</c> (<c>IsSeedData</c>; bağlı
/// <c>TeacherSubject</c>, --force ile randevu/müsaitlik), exam <c>School</c> (<c>IsSeedData</c>; yalnızca bağlı
/// seed-dışı öğretmen/öğrenci/atama yoksa), identity <c>User</c> (<c>IsSeedData</c>) ve Keycloak kullanıcıları
/// (seed alanı) — son ikisi auth-api dev ucu üzerinden. Hard delete. Seed dışı hiçbir satıra dokunmaz.
/// Varsayılan dry-run = özet rapor. İdempotent: kısmi hatadan sonra ikinci koşu kalanı temizler.
/// YALNIZCA Development/Staging.
/// </summary>
public interface ISeedCleanupService
{
    Task<SeedCleanupResult> RunAsync(SeedCleanupOptions options, CancellationToken ct = default);
}
