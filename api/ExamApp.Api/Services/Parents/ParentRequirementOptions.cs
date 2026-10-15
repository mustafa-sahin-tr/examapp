using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Issue #440 / #441: veli zorunluluğunun yaptırım ayarları (<c>ParentRequirement</c> bölümü). Kural ("hangi hesap veli gerektirir")
/// <see cref="ParentRequirement"/>'tadır; burası yalnız süreler.
/// </summary>
public sealed class ParentRequirementOptions
{
    public const string SectionName = "ParentRequirement";

    /// <summary>
    /// #441 hoşgörü süresi (gün): velisiz öğrencinin ilk girişinden itibaren. 0 = süre yok, sayaç başlar başlamaz dolmuş sayılır
    /// (yerelde "süre bitti" ekranını simüle etmek için, bkz. local-dev.md).
    /// </summary>
    [Range(0, 3650)]
    public int GraceDays { get; set; } = 30;

    /// <summary>Son N gün: banner yerine kapatılamaz modal (<c>isFinalWarning</c>) ve ilk hatırlatma bildirimi. Varsayılan 7.</summary>
    [Range(1, 365)]
    public int FinalWarningDays { get; set; } = 7;

    /// <summary>Hatırlatma job'ının (#441: bitişten 7 ve 1 gün önce uygulama içi bildirim) Hangfire cron'u (UTC). Varsayılan her gün 07:00.</summary>
    [Required]
    public string ReminderCron { get; set; } = "0 7 * * *";

    /// <summary>Hatırlatma job'ının tek turda işleyeceği en fazla öğrenci.</summary>
    [Range(1, 5_000)]
    public int ReminderBatchSize { get; set; } = 500;

    /// <summary>
    /// #440 aktivasyon bağlantısının kökü (<c>{kök}/app/parent-activate?token=...</c>). Boşsa <c>Server:BaseUrl</c> (gateway'in genel
    /// adresi) kullanılır.
    /// </summary>
    public string? ActivationBaseUrl { get; set; }

    /// <summary>
    /// Durum önbelleği süresi (saniye; istek başına sorgu yükünü sınırlar, bu örnekteki yazımlar ayrıca geçersizler). 0 = önbellek yok.
    /// Önbellek SÜREÇ İÇİDİR (IMemoryCache): çok örnekli kurulumda başka örnekte yapılan değişiklik en fazla bu süre kadar geç görülür —
    /// ölçeklenince Redis'e taşınmalı.
    /// </summary>
    [Range(0, 600)]
    public int StatusCacheSeconds { get; set; } = 30;

    public TimeSpan GracePeriod => TimeSpan.FromDays(GraceDays);
}

/// <summary>Issue #440 davet kuralları (ürün kararları; yapılandırılmaz).</summary>
public static class ParentInvitationRules
{
    /// <summary>Aktivasyon token'ı / davet geçerliliği.</summary>
    public static readonly TimeSpan Validity = TimeSpan.FromDays(7);

    /// <summary>Aynı e-postaya iki gönderim arasındaki en kısa süre ("24 saatte en fazla 1").</summary>
    public static readonly TimeSpan ResendInterval = TimeSpan.FromHours(24);

    /// <summary>İlk gönderimden sonra aynı öğrenci + e-postaya en fazla yeniden gönderim.</summary>
    public const int MaxResends = 5;

    /// <summary>Kilit penceresi: bu süre içinde <see cref="MaxDistinctEmails"/> farklı e-posta denendiyse yeni bir e-posta kilitli.</summary>
    public static readonly TimeSpan EmailChangeWindow = TimeSpan.FromHours(1);

    public const int MaxDistinctEmails = 3;

    /// <summary>Anti-spam: aynı (normalize) e-postaya TÜM öğrencilerden 24 saatte en fazla bu kadar yeni davet.</summary>
    public const int MaxInvitationsPerTargetEmail = 3;

    public static readonly TimeSpan TargetEmailWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Security MINOR3: aktivasyon/kabul talebi (UsedAt dolu, davet henüz Activated değil) bu süreden eskiyse bırakılmış sayılır — süreç
    /// çöktüyse token kalıcı kilitlenmez.
    /// </summary>
    public static readonly TimeSpan ClaimStaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Kullanılmış token'ın lookup'ta "zaten kullanıldı" olarak tanındığı süre; sonra bilinmeyen token (404).</summary>
    public static readonly TimeSpan UsedTokenRecognition = TimeSpan.FromDays(1);

    /// <summary>Token entropisi (bayt) — base64url ile 43 karakter.</summary>
    public const int TokenBytes = 32;
}
