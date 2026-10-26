using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #573: worksheet türü (Test ⇄ Activity) yalnız hiç oturumu olmayan sayfada değişebilir. "Oturum var mı?" kontrolü ile
/// türün yazımı, ve start-test'in yeni oturum INSERT'i aynı worksheet anahtarında transaction-kapsamlı PostgreSQL advisory
/// lock'u (<c>pg_advisory_xact_lock</c>) altında yapılır: tür değişimi commit edildiyse yeni oturum yeni türü görür; oturum
/// commit edildiyse tür değişimi kilidin altında yeniden kontrol edip 409 döner. Kilit commit/rollback'te bırakılır.
/// <para>
/// Çağıran bunu <c>Database.CreateExecutionStrategy().ExecuteAsync</c> içinde açılmış transaction'da çağırmalıdır.
/// Postgres dışı sağlayıcılarda (SQLite birim testleri) no-op.
/// </para>
/// </summary>
public static class WorksheetKindLock
{
    /// <summary><c>pg_advisory_xact_lock(int4, int4)</c> sınıf anahtarı (ikinci anahtar WorksheetId).</summary>
    internal const int LockClass = 573_001;

    public static async Task AcquireWorksheetKindLockAsync(this DatabaseFacade database, int worksheetId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Worksheet kind lock must be acquired inside a transaction (pg_advisory_xact_lock is transaction-scoped).");

        if (!database.IsNpgsql())
            return;

        await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockClass}, {worksheetId})", ct);
    }
}
