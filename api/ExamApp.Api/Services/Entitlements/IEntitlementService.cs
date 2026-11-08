using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>Plan sınırı uygulanan özelliklerin anahtarları (#1019).</summary>
public static class EntitlementFeatures
{
    /// <summary>Kitaptan aktarım (epic #1017): kurulumu onaylanan iş bir kullanım sayılır.</summary>
    public const string BookImport = "book-import";
}

/// <param name="Allowed">Kullanıcı özelliği şimdi kullanabilir mi.</param>
/// <param name="Limit">Aylık sınır; null = sınırsız.</param>
/// <param name="Used">Bu takvim ayındaki (UTC) kullanım.</param>
/// <param name="ResetsAtUtc">Sayacın sıfırlanacağı an (sınırsızda null).</param>
public sealed record EntitlementCheck(bool Allowed, int? Limit, int Used, DateTime? ResetsAtUtc)
{
    public static readonly EntitlementCheck Unlimited = new(true, null, 0, null);
}

/// <summary>
/// Issue #1018: plan / hak kontrolünün tek giriş noktası. Gerçek uygulama (planlar, aylık sınır, kullanım sayımı) #1019'da gelir;
/// o zamana kadar <see cref="UnlimitedEntitlementService"/> her şeye izin verir.
/// </summary>
public interface IEntitlementService
{
    Task<EntitlementCheck> CheckAsync(int userId, bool isAdmin, string featureKey, CancellationToken ct = default);
}

/// <summary>#1019'a kadar geçici: her kullanıcıya sınırsız.</summary>
public sealed class UnlimitedEntitlementService : IEntitlementService
{
    public Task<EntitlementCheck> CheckAsync(int userId, bool isAdmin, string featureKey, CancellationToken ct = default) =>
        Task.FromResult(EntitlementCheck.Unlimited);
}
