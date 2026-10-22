using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #472: bankadan teste soru bağlama "testte zaten var mı?" kontrolü + sıra numarası (en büyük Order + 1) + INSERT'ten
/// oluşur; <c>WorksheetQuestions</c>'ta (TestId, QuestionId) unique index'i yok (eski veride çift satır olabilir). İki
/// eşzamanlı istek aynı soruyu iki kez ya da aynı sıra numarasıyla bağlamasın diye bağlama, test anahtarında
/// transaction-kapsamlı PostgreSQL advisory lock'u (<c>pg_advisory_xact_lock</c>) altında yapılır; kontrol kilidin altında
/// tekrarlanır. Kilit commit/rollback'te kendiliğinden bırakılır.
/// <para>
/// Çağıran bunu <c>Database.CreateExecutionStrategy().ExecuteAsync</c> içinde açılmış transaction'da çağırmalıdır.
/// Postgres dışı sağlayıcılarda (SQLite birim testleri) no-op.
/// </para>
/// </summary>
public static class WorksheetQuestionLinkLock
{
    /// <summary><c>pg_advisory_xact_lock(int4, int4)</c> sınıf anahtarı (ikinci anahtar TestId).</summary>
    internal const int LockClass = 472_001;

    public static async Task AcquireWorksheetQuestionLinkLockAsync(this DatabaseFacade database, int worksheetId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Worksheet question link lock must be acquired inside a transaction (pg_advisory_xact_lock is transaction-scoped).");

        if (!database.IsNpgsql())
            return;

        await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockClass}, {worksheetId})", ct);
    }
}
