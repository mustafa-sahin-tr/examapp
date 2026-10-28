using System;

namespace ExamApp.Api.Models.Dtos.ParentDashboard;

/// <summary>
/// Issue #420 (epic #407 V2): velinin çocuk özeti — <c>GET api/parent/children/{studentId}/summary</c>. Yalnızca SAYILAR ve
/// zaman damgası: soru/worksheet içeriği, cevap anahtarı, mesaj/yorum, başka öğrenci ya da iletişim bilgisi (e-posta,
/// telefon) YOK. Yeni alan eklerken bu kural korunmalı (sözleşme ParentDashboardEndpointsTests'te alan listesiyle kilitli).
/// </summary>
public sealed class ParentChildSummaryDto
{
    /// <summary>Öğrencinin exam DB id'si (istekteki ile aynı).</summary>
    public int StudentId { get; set; }

    /// <summary>Bu haftanın ilk günü (Pazartesi, Europe/Istanbul yerel takvimi), "yyyy-MM-dd".</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>Bu hafta (Pazartesi 00:00'dan şimdiye, yerel) cevaplanan soru sayısı: worksheet cevapları + pratik (pas hariç).</summary>
    public int QuestionsSolvedThisWeek { get; set; }

    public ParentChildAssignmentCountsDto Assignments { get; set; } = new();

    /// <summary>Toplam puan (StudentPoints.XP).</summary>
    public int TotalPoints { get; set; }

    /// <summary>Son öğrenme aktivitesi (UTC, SAATE kesilmiş): son cevap, test başlatma ya da bitirme. Hiç aktivite yoksa null.</summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>issue #584: etkinlik özeti (tamamlama sayısı ve ilk kontrol ortalaması).</summary>
    public ParentChildActivitySummaryDto Activities { get; set; } = new();
}

/// <summary>
/// issue #584: öğrencinin etkileşimli etkinlikleri (<c>Worksheet.Kind = Activity</c>; atanmış ya da kendi açtığı). Tamamlama =
/// oturumun ilk tamamlanması (<c>FirstCompletedAt</c>; yeniden çözme ikinci kez saymaz, #609). Etkinlikler puansızdır; "skor"
/// ilk "Cevapları kontrol et"teki doğru satır oranıdır (öğretmen takibindeki ilk kontrol özetiyle aynı kaynak).
/// </summary>
public sealed class ParentChildActivitySummaryDto
{
    /// <summary>Bu hafta (<see cref="ParentChildSummaryDto.WeekStart"/>'tan) tamamlanan etkinlik.</summary>
    public int CompletedThisWeek { get; set; }

    /// <summary>Şimdiye dek tamamlanan etkinlik.</summary>
    public int CompletedTotal { get; set; }

    /// <summary>Tamamlanan etkinliklerin ilk kontrol doğru oranı ortalaması (0–100, yuvarlanmış); kontrol edilmiş etkinlik yoksa null.</summary>
    public int? AverageFirstCheckPercent { get; set; }
}

/// <summary>
/// Öğrenciye atanmış worksheet'lerin durum sayıları (worksheet başına tek sayım). Kapsam: başlamış atamalar — açık olanlar
/// ve son teslim tarihi son <see cref="WindowDays"/> gün içinde geçmiş olanlar.
/// </summary>
public sealed class ParentChildAssignmentCountsDto
{
    /// <summary>Tamamlandı.</summary>
    public int Completed { get; set; }

    /// <summary>Gecikti: son teslim tarihi geçti ve bitirilmedi, ya da oturumun süresi doldu (tekrar çözülemez).</summary>
    public int Overdue { get; set; }

    /// <summary>Bekliyor: süresi devam ediyor, başlanmadı ya da devam ediyor.</summary>
    public int Pending { get; set; }

    /// <summary>Teslim tarihi geçmiş atamaların geriye bakış penceresi (gün).</summary>
    public int WindowDays { get; set; }
}
