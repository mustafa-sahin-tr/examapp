using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #422 (veli paneli V4): BadgeService'te kazanılan rozetlerin exam DB'deki salt-okunur projeksiyonu —
/// <c>StudentBadgeEarnedEvent</c> → <c>StudentBadgeEarnedConsumer</c> yazar (#225 deseni; servisler arası HTTP/DB yok).
/// (StudentId, BadgeDefinitionId) UNIQUE: tekrar teslim no-op. Geriye dönük doldurma YOK — bu özellikten önce kazanılmış
/// rozetler yeniden kazanılana (ör. sıfırlama sonrası) kadar görünmez (ürün kararı; prod yok). Öğrenci sıfırlamasında
/// (<c>StudentResetJob</c>) satırlar silinir ki yeniden kazanılan rozet tekrar yazılabilsin.
/// </summary>
public class StudentBadgeProjection
{
    public long Id { get; set; }

    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = default!;

    /// <summary>BadgeService <c>BadgeDefinition.Id</c>.</summary>
    public Guid BadgeDefinitionId { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Allowlist'ten geçmiş Material Symbols adı; yoksa null.</summary>
    [MaxLength(64)]
    public string? Icon { get; set; }

    /// <summary>Kazanma anı (UTC, BadgeService saati).</summary>
    public DateTime EarnedAtUtc { get; set; }

    /// <summary>Projeksiyona yazıldığı an (UTC).</summary>
    public DateTime ReceivedAtUtc { get; set; }
}

/// <summary>
/// Issue #422: öğrencinin YEREL (Europe/Istanbul) gün başına kazandığı puan defteri — <c>StudentPointsChangedEvent</c>
/// mutlak toplam taşır; exam API senkronu (<c>StudentPointsSyncService</c>) önceki toplamla farkı, event zamanının yerel
/// gününe ekler. Veli panelinin "bu hafta kazanılan" değeri buradan toplanır. (StudentId, Day) UNIQUE.
/// İlk senkron (önceki satır yok / hiç senkronlanmamış) geçmiş toplamı taşıdığı için deftere YAZILMAZ (taban çizgisi).
/// </summary>
public class StudentDailyXp
{
    public long Id { get; set; }

    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = default!;

    /// <summary>Yerel takvim günü (Europe/Istanbul).</summary>
    public DateOnly Day { get; set; }

    /// <summary>O gün kazanılan net puan.</summary>
    public int Xp { get; set; }
}
