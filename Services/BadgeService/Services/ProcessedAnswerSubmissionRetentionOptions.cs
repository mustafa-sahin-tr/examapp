using System;
using System.ComponentModel.DataAnnotations;

namespace BadgeService.Services;

/// <summary>
/// <c>ProcessedAnswerSubmissionRetention</c> config bölümü (issue #279, item 3). BadgeService Hangfire
/// kullanmıyor (yalnızca exam API'de var, bkz. <c>AdminDataAccessLogRetentionJob</c>) — yeni bir
/// job scheduler/dashboard bağımlılığı eklemek yerine hafif bir <see cref="ProcessedAnswerSubmissionRetentionService"/>
/// (<c>BackgroundService</c> + <c>PeriodicTimer</c>) kullanılır; bkz. o sınıfın XML doc'u.
/// </summary>
public sealed class ProcessedAnswerSubmissionRetentionOptions
{
    public const string SectionName = "ProcessedAnswerSubmissionRetention";

    /// <summary>Bu kadar günden eski <c>ProcessedAnswerSubmissions</c> satırları silinir. Varsayılan: 30 gün.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;

    /// <summary>Tek DELETE ifadesinin sileceği en fazla satır (uzun kilit/WAL patlamasını önler).</summary>
    [Range(1, 100_000)]
    public int DeleteBatchSize { get; set; } = 5_000;

    /// <summary>
    /// Taramalar arası bekleme. Varsayılan: 6 saat. issue #279 review (NIT): alt sınır 1 dakika — daha
    /// küçük/sıfır bir değer <see cref="PeriodicTimer"/>'ı sıkı bir döngüye sokup DB'yi gereksiz yere
    /// yorabilir; üst sınır 7 gün (mantıksız derecede seyrek bir yapılandırmayı erkenden yakalar).
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
}
