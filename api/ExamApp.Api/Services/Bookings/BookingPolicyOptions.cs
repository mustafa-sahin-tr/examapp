using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// issue #315: "BookingPolicy" config bölümü — iptal / erteleme / no-show kuralları. Tüm değerlerin varsayılanı vardır;
/// appsettings'te tanımlamak zorunlu değildir.
/// </summary>
public sealed class BookingPolicyOptions
{
    public const string SectionName = "BookingPolicy";

    /// <summary>
    /// Onaylı randevu ders başlangıcına en az bu kadar saat kala iptal edilebilir (en az 1: görüşme katılım penceresi
    /// açılmadan; verilmiş oda token'ı iptalde geri alınamaz); sonrasında iptal 409
    /// <c>CancellationWindowClosed</c>. Henüz onaylanmamış (Pending/AwaitingParent) talebin geri çekilmesi bu kurala tabi değildir.
    /// </summary>
    [Range(1, 168)]
    public int CancellationMinNoticeHours { get; set; } = 2;

    /// <summary>Erteleme önerisi ve kabulü, MEVCUT ders başlangıcına en az bu kadar saat kala yapılabilir.</summary>
    [Range(1, 168)]
    public int RescheduleMinNoticeHours { get; set; } = 2;

    /// <summary>
    /// Bir randevuda en fazla bu kadar erteleme ÖNERİSİ yapılabilir (kabul, ret ve geri çekilenler dahil) — öner/geri çek
    /// döngüsüyle bildirim seli engellenir (security review O2).
    /// </summary>
    [Range(1, 50)]
    public int MaxProposalsPerBooking { get; set; } = 4;

    /// <summary>Bir randevu en fazla bu kadar kez ertelenebilir (kabul edilen öneriler).</summary>
    [Range(0, 20)]
    public int MaxReschedulesPerBooking { get; set; } = 2;

    /// <summary>Ders bitişinden sonra no-show en geç bu kadar gün içinde bildirilebilir.</summary>
    [Range(1, 90)]
    public int NoShowReportWindowDays { get; set; } = 7;
}
