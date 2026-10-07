using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.ParentDashboard;

/// <summary>
/// Issue #422 (epic #407 V4): <c>GET api/parent/children/{studentId}/schedule?from=&amp;to=</c> — çocuğun "Planım" planları ve
/// ders randevuları, salt okunur. Aralık yerel takvim günüdür (Europe/Istanbul), iki uç dahil, en fazla
/// <see cref="MaxDays"/> gün, bugünden en fazla bir yıl geri / ileri. Alan listesi testlerde kilitli: ders bağlantısı (Jitsi),
/// ücret, not/ret gerekçesi YOK.
/// </summary>
public sealed class ParentChildScheduleDto
{
    /// <summary>İstenebilecek en uzun aralık (gün, iki uç dahil).</summary>
    public const int MaxDays = 31;

    public int StudentId { get; set; }

    /// <summary>Aralığın ilk günü, "yyyy-MM-dd".</summary>
    public DateOnly From { get; set; }

    /// <summary>Aralığın son günü (dahil), "yyyy-MM-dd".</summary>
    public DateOnly To { get; set; }

    /// <summary>Çocuğun worksheet planları (planlanan güne göre sıralı).</summary>
    public List<ParentPlanItemDto> Plans { get; set; } = new();

    /// <summary>Ders randevuları (başlangıca göre sıralı).</summary>
    public List<ParentLessonItemDto> Lessons { get; set; } = new();
}

/// <summary>"Planla &amp; Hatırlat" planı: worksheet adı, dersi ve planlanan gün (saat ve hatırlatma ayarı yok).</summary>
public sealed class ParentPlanItemDto
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Ders adı; worksheet'e ders atanmamışsa null.</summary>
    public string? Subject { get; set; }

    /// <summary>Planlanan gün (Europe/Istanbul), "yyyy-MM-dd".</summary>
    public DateOnly PlannedOn { get; set; }
}

/// <summary>Randevu durumu değerleri.</summary>
/// <remarks>Reddedilen talepler veliye gösterilmez (review, #422) — yalnızca bekleyen ve onaylanan randevular.</remarks>
public static class ParentLessonStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
}

/// <summary>
/// Ders randevusu: öğretmenin görünen adı, yerel gün, başlangıç/bitiş ve durum. Görüşme bağlantısı, ücret, not ya da ret gerekçesi
/// bilinçli olarak yok — yeni alan eklerken bu kural korunmalı.
/// </summary>
public sealed class ParentLessonItemDto
{
    /// <summary>Öğretmenin görünen adı (auth-api'den, en iyi çaba); çözülemezse null.</summary>
    public string? TeacherName { get; set; }

    /// <summary>
    /// Listede gösterileceği yerel gün (Europe/Istanbul), "yyyy-MM-dd" — başlangıcın yerel günü; aralığın başından önce başlayıp
    /// aralığa taşan randevuda aralığın ilk günü. İstemci gruplamayı tarayıcının saat dilimine göre değil buna göre yapar.
    /// </summary>
    public DateOnly StartsOn { get; set; }

    /// <summary>Başlangıç (UTC).</summary>
    public DateTime StartAt { get; set; }

    /// <summary>Bitiş (UTC).</summary>
    public DateTime EndAt { get; set; }

    /// <summary><see cref="ParentLessonStatuses"/>.</summary>
    public string Status { get; set; } = ParentLessonStatuses.Pending;
}
