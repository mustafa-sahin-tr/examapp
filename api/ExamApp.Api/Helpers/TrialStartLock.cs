using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #1090: deneme planı kullanıcı anahtarında transaction-kapsamlı PostgreSQL advisory lock'u (<c>pg_advisory_xact_lock</c>)
/// altında başlar. Plan kilidi denemeyi ilk istekte kendiliğinden başlatır; ilk sayfanın eş zamanlı istekleri kilitsiz "deneme
/// kullanıldı mı" denetimini birlikte geçip aynı kullanıcıya birden çok açık deneme yazıyordu. Kilit alındıktan sonraki sorgu
/// READ COMMITTED'da öncekinin commit'ini görür. Postgres dışı sağlayıcılarda (SQLite birim testleri) no-op.
/// </summary>
public static class TrialStartLock
{
    /// <summary><c>pg_advisory_xact_lock(int4, int4)</c> sınıf anahtarı (ikinci anahtar exam kullanıcı id'si).</summary>
    internal const int LockClass = 1_090_001;

    public static async Task AcquireTrialStartLockAsync(this DatabaseFacade database, int userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Trial start lock must be acquired inside a transaction (pg_advisory_xact_lock is transaction-scoped).");

        if (!database.IsNpgsql())
            return;

        await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockClass}, {userId})", ct);
    }
}
