using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #419: veli bağlantısı yazımları (davet kodu üretme = "öğrenci başına tek geçerli kod", redeem = "kod tek
/// kullanımlık" + "öğrenci başına en fazla 4 açık (Active + Pending) veli" + "veli başına en fazla N çocuk"; onay = süresi
/// dolmamış Pending → Active) kontrol ile yazmayı AYNI anahtarda transaction-kapsamlı advisory lock altında yapar
/// (<see cref="TeacherAvailabilityLock"/> (#323) deseni). Filtreli tekil index yalnızca aynı çiftin iki AÇIK satırını
/// engeller; sayı tavanlarını ve kodun tek kullanımını kilit korur. Koparma/ret kilit almaz — koşullu UPDATE ile yarışı çözer.
/// Sıra her zaman önce öğrenci, sonra veli (kilitlenme yok). Postgres dışı sağlayıcılarda (SQLite birim testleri) no-op.
/// Bekleme üst sınırı 5 sn; aşılırsa <see cref="ParentLinkLockTimeoutException"/> (geçici sayılmaz → 409).
/// </summary>
public static class ParentLinkLock
{
    /// <summary>Öğrenci anahtarı sınıfı (ikinci anahtar Students.Id).</summary>
    internal const int StudentLockClass = 419_001;

    /// <summary>Veli anahtarı sınıfı (ikinci anahtar Parents.Id).</summary>
    internal const int ParentLockClass = 419_002;

    internal const string SetLockTimeoutSql = "SET LOCAL lock_timeout = '5s'";

    public static Task AcquireStudentParentLinkLockAsync(this DatabaseFacade database, int studentId, CancellationToken ct = default)
        => AcquireAsync(database, StudentLockClass, studentId, ct);

    public static Task AcquireParentChildLinkLockAsync(this DatabaseFacade database, int parentId, CancellationToken ct = default)
        => AcquireAsync(database, ParentLockClass, parentId, ct);

    private static async Task AcquireAsync(DatabaseFacade database, int lockClass, int key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Parent link lock must be acquired inside a transaction (pg_advisory_xact_lock is transaction-scoped).");

        if (!database.IsNpgsql())
            return;

        await database.ExecuteSqlRawAsync(SetLockTimeoutSql, ct);
        try
        {
            await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockClass}, {key})", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new ParentLinkLockTimeoutException(lockClass, key, ex);
        }
    }
}

/// <summary>issue #419: veli bağlantısı kilidi zamanında alınamadı. Bilinçli olarak geçici değil (execution strategy yeniden denemez).</summary>
public sealed class ParentLinkLockTimeoutException(int lockClass, int key, Exception inner)
    : Exception($"Parent link lock ({lockClass}, {key}) could not be acquired within 5s.", inner);
