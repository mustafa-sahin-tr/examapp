using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #323 (security L2): bir öğretmenin müsaitlik takvimine yazan her yol (tekil slot oluşturma, tekrarlayan kural
/// oluşturma + slot üretimi, top-up, tüm seriyi silme) "kesişen slot var mı?" kontrolü ile yazmayı AYNI öğretmen
/// anahtarında transaction-kapsamlı PostgreSQL advisory lock'u (<c>pg_advisory_xact_lock</c>) altında yapar. Unique index
/// yalnızca birebir aynı <c>(Date, Start, End)</c> değerini yakalar; kilit olmadan paralel iki istek kesişen iki slot
/// açabilirdi. İkinci istek birincinin commit'ini bekler, kilidi aldıktan sonra yeni satırı görür → 409. Kilit
/// commit/rollback'te kendiliğinden bırakılır.
/// <para>
/// Neden exclusion constraint değil: slot <c>Date + time</c> olarak saklanıyor (gün aşımı <c>EndTime &lt; StartTime</c>),
/// soft-delete ediliyor ve tekrarlayan kuralların haftalık/geçerlilik-aralığı çakışması bir range tipiyle ifade
/// edilemiyor; kilit tüm bu kuralları tek yerde (servisteki <c>SlotTimeRange</c>) tutar.
/// </para>
/// <para>
/// Bekleme üst sınırı (security review Düşük-2): kilitten önce <c>SET LOCAL lock_timeout</c> = <see cref="LockTimeout"/>.
/// Süre dolarsa PostgreSQL 55P03 (<c>lock_not_available</c>) döner; Npgsql bunu geçici (transient) saydığı için
/// execution strategy'nin yeniden denemesine girmesin diye <see cref="TeacherAvailabilityLockTimeoutException"/>'a
/// çevrilir — çağıran 409 + <c>booking.slot.busy</c> döner. <c>SET LOCAL</c> transaction sonunda kendiliğinden geri döner.
/// </para>
/// <para>
/// Çağıran bunu <c>Database.CreateExecutionStrategy().ExecuteAsync</c> İÇİNDE açılmış transaction'da çağırmalı ve
/// çakışma kontrolünü kilidin ALTINDA yapmalıdır (<see cref="UserRegistrationLock"/> ile aynı desen). Anahtar
/// <c>Teachers.Id</c>'dir (UserId değil). Postgres dışı sağlayıcılarda (SQLite birim testleri) no-op.
/// </para>
/// </summary>
public static class TeacherAvailabilityLock
{
    /// <summary>
    /// <c>pg_advisory_xact_lock(int4, int4)</c> sınıf anahtarı — öğretmen müsaitlik kilitlerini diğer advisory lock
    /// kullanımlarından (<see cref="UserRegistrationLock.LockClass"/>) ayırır. İkinci anahtar <c>Teachers.Id</c>.
    /// </summary>
    internal const int LockClass = 323_001;

    /// <summary>Kilit için en fazla bekleme (saniye). Tek öğretmenin yazma işlemleri milisaniyeler sürer.</summary>
    internal const int LockTimeoutSeconds = 5;

    internal static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(LockTimeoutSeconds);

    /// <summary>SET parametre kabul etmez; sabit literal (<see cref="LockTimeoutSeconds"/> ile aynı tutulmalı).</summary>
    internal const string SetLockTimeoutSql = "SET LOCAL lock_timeout = '5s'";

    public static async Task AcquireTeacherAvailabilityLockAsync(this DatabaseFacade database, int teacherId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Teacher availability lock must be acquired inside a transaction (pg_advisory_xact_lock is transaction-scoped).");

        if (!database.IsNpgsql())
            return;

        await database.ExecuteSqlRawAsync(SetLockTimeoutSql, ct);
        try
        {
            await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockClass}, {teacherId})", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new TeacherAvailabilityLockTimeoutException(teacherId, ex);
        }
    }
}

/// <summary>
/// issue #323: öğretmen müsaitlik kilidi <see cref="TeacherAvailabilityLock.LockTimeout"/> içinde alınamadı. Bilinçli
/// olarak geçici (transient) DEĞİL: execution strategy yeniden denemez, çağıran 409 (<c>booking.slot.busy</c>) döner.
/// </summary>
public sealed class TeacherAvailabilityLockTimeoutException(int teacherId, Exception inner)
    : Exception($"Teacher availability lock for teacher {teacherId} could not be acquired within {TeacherAvailabilityLock.LockTimeoutSeconds}s.", inner)
{
    public int TeacherId { get; } = teacherId;
}
