using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>
/// Kaynak CSV'nin nereden geldiği ve içeriğine erişim. <paramref name="OpenRead"/> her çağrıda
/// yeni bir akış döner; çağıran kapatır.
/// </summary>
/// <param name="Description">Rapora yazılacak kaynak tanımı (URL / önbellek yolu / "fixture").</param>
/// <param name="Sha256">Dosyanın SHA-256'sı (küçük harf hex).</param>
/// <param name="FromCache">Ağdan indirilmeden yerel önbellekten mi alındı?</param>
public sealed record SchoolSeedSource(
    string Description,
    string Sha256,
    bool FromCache,
    Func<Stream> OpenRead);

/// <summary>
/// Okul listesi kaynağını sağlar (issue #216). Üretim implementasyonu sabit commit'ten indirip
/// önbellekler (<see cref="GitHubSchoolSeedSourceProvider"/>); testler gömülü fixture verir.
/// </summary>
public interface ISchoolSeedSourceProvider
{
    Task<SchoolSeedSource> GetAsync(CancellationToken ct = default);
}
