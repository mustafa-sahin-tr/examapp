using System;
using System.Collections.Generic;
using System.Globalization;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.StudentReset;

/// <summary>
/// Kuyruğa alma sonucu: <paramref name="AlreadyPending"/> true ise yeni iş açılmadı, mevcut iş döndü. Kilit zaman aşımında
/// (eşzamanlı başka istek) <paramref name="JobId"/> boş olabilir.
/// </summary>
public sealed record StudentResetEnqueueResult(string JobId, bool AlreadyPending);

public interface IStudentResetScheduler
{
    /// <summary>
    /// Öğrencinin kendi verisini sıfırlama işini kuyruğa alır. Aynı kullanıcı için bekleyen
    /// (Enqueued/Scheduled/Processing/Awaiting) bir iş varsa YENİSİNİ açmaz, mevcut işin id'sini döner (issue #243).
    /// </summary>
    StudentResetEnqueueResult Enqueue(int userId, int studentId, string keycloakUserId);
}

/// <summary>
/// issue #243 (security): self-reset tekilleştirme. Tasarım — işaret Hangfire storage'ında tutulur, "bekliyor mu"
/// kararı ise işaretin kendisinden değil işin GERÇEK Hangfire durumundan verilir:
/// <list type="bullet">
/// <item>Kullanıcı başına hash <c>student-reset:{userId}</c> → <c>jobId</c> (son kuyruğa alınan iş).</item>
/// <item>Yeni istek: <c>student-reset-lock:{userId}</c> dağıtık kilidi altında hash okunur; kayıtlı işin durumu
/// (<see cref="IStorageConnection.GetStateData"/>) bekleyen bir durumsa mevcut id döner, değilse (Succeeded/Failed/
/// Deleted/iş silinmiş) yeni iş açılır ve hash güncellenir. Kilit, eşzamanlı iki isteğin ikisinin de iş açmasını önler.</item>
/// </list>
/// Neden DB'de "reset pending" kolonu değil: iş bitince/başarısız olunca işareti temizleyecek ayrı bir adım gerekir
/// (iş çökerse işaret takılı kalır → kullanıcı kalıcı kilitlenir). Hangfire durumu kendi kendini iyileştirir; migration
/// gerekmez; kilit + hash zaten aynı Postgres storage'ında (Hangfire şeması) atomik çalışır.
/// Hash 7 gün sonra Hangfire tarafından süpürülür (en uzun retry penceresinden uzun); süpürülse bile en kötü durum
/// bir mükerrer iş olur, rate limit (<c>RateLimiting:StudentSelfReset</c>) ayrıca sınırlar.
/// </summary>
public sealed class StudentResetScheduler : IStudentResetScheduler
{
    internal static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan MarkerTtl = TimeSpan.FromDays(7);
    internal const string JobIdField = "jobId";

    private static readonly HashSet<string> PendingStates = new(StringComparer.OrdinalIgnoreCase)
    {
        EnqueuedState.StateName,
        ScheduledState.StateName,
        ProcessingState.StateName,
        AwaitingState.StateName,
    };

    private readonly JobStorage _storage;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<StudentResetScheduler> _logger;

    public StudentResetScheduler(JobStorage storage, IBackgroundJobClient jobs, ILogger<StudentResetScheduler> logger)
    {
        _storage = storage;
        _jobs = jobs;
        _logger = logger;
    }

    internal static string MarkerKey(int userId) => "student-reset:" + userId.ToString(CultureInfo.InvariantCulture);

    internal static string LockResource(int userId) => "student-reset-lock:" + userId.ToString(CultureInfo.InvariantCulture);

    public StudentResetEnqueueResult Enqueue(int userId, int studentId, string keycloakUserId)
    {
        using var connection = _storage.GetConnection();
        var markerKey = MarkerKey(userId);

        IDisposable distributedLock;
        try
        {
            distributedLock = connection.AcquireDistributedLock(LockResource(userId), LockTimeout);
        }
        catch (DistributedLockTimeoutException)
        {
            // issue #243 review: kilit 10 sn'de alınamadıysa aynı kullanıcı için başka bir istek şu an kuyruğa alma
            // yapıyor demektir — 500 yerine "zaten bekleyen reset var" ile aynı sonuç. İş açılmaz. Kilitsiz, salt-okuma
            // en iyi çaba: bekleyen işin id'si okunabiliyorsa o döner, yoksa (diğer istek henüz yazmadı) boş id.
            var pendingJobId = FindPendingJobId(connection, markerKey, out _) ?? string.Empty;
            _logger.LogWarning(
                "[StudentReset] Sıfırlama kilidi {Timeout} içinde alınamadı; bekleyen iş varsayıldı: userId={UserId} jobId={JobId}",
                LockTimeout, userId, pendingJobId);
            return new StudentResetEnqueueResult(pendingJobId, AlreadyPending: true);
        }

        using var lockHandle = distributedLock;

        var existingJobId = FindPendingJobId(connection, markerKey, out var state);
        if (existingJobId != null)
        {
            _logger.LogInformation(
                "[StudentReset] Bekleyen sıfırlama işi var, yenisi kuyruğa alınmadı: userId={UserId} jobId={JobId} state={State}",
                userId, existingJobId, state);
            return new StudentResetEnqueueResult(existingJobId, AlreadyPending: true);
        }

        // Kullanıcının JWT'si saklanmaz; yalnızca id'ler job argümanıdır.
        var jobId = _jobs.Enqueue<StudentResetJob>(job => job.RunAsync(userId, studentId, keycloakUserId));

        using (var transaction = connection.CreateWriteTransaction())
        {
            transaction.SetRangeInHash(markerKey, new[] { new KeyValuePair<string, string>(JobIdField, jobId) });
            if (transaction is JobStorageTransaction storageTransaction)
            {
                storageTransaction.ExpireHash(markerKey, MarkerTtl);
            }
            transaction.Commit();
        }

        return new StudentResetEnqueueResult(jobId, AlreadyPending: false);
    }

    /// <summary>İşarette kayıtlı iş hâlâ bekleyen bir durumdaysa id'sini döner, değilse null.</summary>
    private static string? FindPendingJobId(IStorageConnection connection, string markerKey, out string? state)
    {
        state = null;
        var jobId = ReadJobId(connection, markerKey);
        if (jobId == null)
            return null;

        state = connection.GetStateData(jobId)?.Name;
        return state != null && PendingStates.Contains(state) ? jobId : null;
    }

    private static string? ReadJobId(IStorageConnection connection, string markerKey)
    {
        var entries = connection.GetAllEntriesFromHash(markerKey);
        return entries != null && entries.TryGetValue(JobIdField, out var jobId) && !string.IsNullOrWhiteSpace(jobId)
            ? jobId
            : null;
    }
}
