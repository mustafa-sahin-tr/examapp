using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #277 (madde 9): öğretmen ve öğrenci kaydı birbirini dışlar (#234), ama "diğer tabloda satır var mı?" kontrolü ile
/// INSERT iki ayrı ifadedir ve iki AYRI tabloya yazılır — #259'un filtreli unique index'leri (tablo başına) eşzamanlı
/// <c>teacher/register</c> + <c>student/register</c> yarışını çözmez (ikisi de karşı tabloyu boş görüp kendi satırını
/// yazabilir). Çözüm: iki kayıt akışı da kontrol + yazmayı AYNI kullanıcı anahtarında transaction-kapsamlı PostgreSQL
/// advisory lock'u (<c>pg_advisory_xact_lock</c>) altında yapar; ikinci istek birincinin commit'ini bekler ve kilidi
/// aldıktan sonra karşı tablodaki satırı görür → 409. Kilit commit/rollback'te kendiliğinden bırakılır.
/// <para>
/// Çağıran bunu <c>Database.CreateExecutionStrategy().ExecuteAsync</c> İÇİNDE açılmış transaction'da çağırmalıdır
/// (Aspire AddNpgsqlDbContext retry-on-failure açık; QuestionService deseni). Kontrol kilidin ALTINDA, transaction içinde
/// tekrarlanmalıdır — kilitten önceki kontrol yalnızca hızlı yol/mesaj içindir.
/// </para>
/// <para>
/// Postgres dışı sağlayıcılarda (SQLite birim testleri) no-op: SQLite zaten tek yazar kilidiyle serileştirir.
/// </para>
/// </summary>
public static class UserRegistrationLock
{
    /// <summary>
    /// <c>pg_advisory_xact_lock(int4, int4)</c> sınıf anahtarı — kullanıcı kaydı kilitlerini başka advisory lock
    /// kullanımlarından ayırır (ikinci anahtar UserId).
    /// </summary>
    internal const int LockClass = 277_009;

    public static async Task AcquireUserRegistrationLockAsync(this DatabaseFacade database, int userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "User registration lock must be acquired inside a transaction (pg_advisory_xact_lock is transaction-scoped).");

        if (!database.IsNpgsql())
            return;

        await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockClass}, {userId})", ct);
    }
}
