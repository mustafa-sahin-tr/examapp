using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

/// <summary>
/// Yatay ölçekleme retry yardımcısı (issue #279, item 1): birden fazla BadgeService instance'ı aynı
/// aggregate satırını (StudentQuestionAggregate/StudentSubjectAggregate/StudentDailyActivity — bkz.
/// <see cref="BadgeDbContext"/>'teki <c>xmin</c> concurrency token'ları) eşzamanlı güncellerse
/// <see cref="DbUpdateConcurrencyException"/> fırlar; bu yardımcı işlemi sınırlı sayıda yeniden dener.
///
/// Sadece PROSES İÇİ tek-kullanıcı partitioner'ının (<c>AnswerSubmittedConsumerDefinition</c>) kapsamadığı
/// çapraz-instance çakışmasını hedefler — aynı instance içinde zaten sıralı işlenir, bu yol pratikte nadiren
/// tetiklenir. Test edilebilirlik için provider'dan bağımsız, saf bir retry döngüsü olarak ayrıldı (DB'ye
/// dokunmaz) — gerçek concurrency çakışmasını sqlite ile üretmek yerine <see cref="DbUpdateConcurrencyException"/>
/// fırlatan sahte bir action ile birim testi yapılabilir.
/// </summary>
public static class ConcurrencyRetry
{
    public const int DefaultMaxAttempts = 3;

    /// <summary>
    /// <paramref name="action"/>'ı en fazla <paramref name="maxAttempts"/> kez dener. Her başarısız denemeden
    /// ÖNCE <paramref name="onRetry"/> çağrılır (çağıran burada DbContext.ChangeTracker.Clear() gibi
    /// bir sonraki denemenin taze durum okumasını garanti eden temizliği yapmalı). Son denemede de başarısız
    /// olursa istisna olduğu gibi yukarı fırlatılır (çağıranın retry'ı bilerek burada sınırlı tutulur — sonsuz
    /// döngü/canlı kilit riski yok, tükenince MassTransit'in kendi retry/dead-letter'ına devredilir).
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        Action<int, DbUpdateConcurrencyException>? onRetry = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                onRetry?.Invoke(attempt, ex);
            }
        }
    }
}
