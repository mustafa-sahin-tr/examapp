using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.Connections;

namespace ExamApp.Api.Services.Whiteboard;

/// <summary>
/// "Whiteboard" config bölümü (issue #98). Tüm değerlerin makul varsayılanı vardır; appsettings'te tanımlamak zorunlu
/// değildir. Katılım penceresi burada DEĞİL — görüşme odasıyla ortak <c>Video:JoinWindow*</c> ayarlarından gelir.
/// </summary>
public sealed class WhiteboardOptions
{
    public const string SectionName = "Whiteboard";

    /// <summary>Hub'ın tek SignalR mesajı üst sınırı (bayt). Varsayılan 128 KB (global varsayılan 32 KB).</summary>
    [Range(16 * 1024, 1024 * 1024)]
    public int MaxReceiveMessageBytes { get; set; } = 128 * 1024;

    /// <summary>Tek <c>SendElements</c> çağrısındaki azami eleman sayısı.</summary>
    [Range(1, 5000)]
    public int MaxElementsPerMessage { get; set; } = 500;

    /// <summary>Sahnedeki azami eleman sayısı (silinmiş/tombstone elemanlar dahil).</summary>
    [Range(1, 100_000)]
    public int MaxSceneElements { get; set; } = 5000;

    /// <summary>Sahnenin serileştirilmiş (UTF-8 JSON) azami boyutu.</summary>
    [Range(1024, 64 * 1024 * 1024)]
    public int MaxSceneBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Eleman <c>id</c> alanının azami uzunluğu.</summary>
    [Range(1, 256)]
    public int MaxElementIdLength { get; set; } = 64;

    /// <summary>Kullanıcı başına saniyedeki mesaj (SendElements) sayısı — token bucket dolum hızı ve kapasitesi.</summary>
    [Range(1, 1000)]
    public int MessagesPerSecond { get; set; } = 30;

    /// <summary>Kullanıcı başına saniyedeki imleç güncellemesi; aşan güncellemeler sessizce düşürülür.</summary>
    [Range(1, 1000)]
    public int PointerUpdatesPerSecond { get; set; } = 20;

    /// <summary>Kullanıcı başına saniyedeki JoinBoard (her biri DB'ye gider) dolum hızı.</summary>
    [Range(1, 100)]
    public int JoinsPerSecond { get; set; } = 2;

    /// <summary>JoinBoard kovasının kapasitesi (kısa patlama: yeniden bağlanma + sekme açma).</summary>
    [Range(1, 100)]
    public int JoinBurst { get; set; } = 4;

    /// <summary>
    /// Bir elemanın <c>version</c>'ı tek adımda sunucudaki mevcut değer + bu kadarı aşamaz; yeni elemanlarda bu değer
    /// mutlak üst sınırdır. Dev version ile elemanı karşı tarafa karşı "kilitleme" engellenir.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxVersionJump { get; set; } = 10_000;

    /// <summary>
    /// Bir bağlantının yetkisi (randevu durumu, öğretmen onayı/askısı) DB'den en geç bu kadar saniyede bir yeniden
    /// doğrulanır. Aradaki çağrılarda üyelik sunucunun bellekteki connection→booking eşlemesinden ve pencere
    /// kapanış anından doğrulanır.
    /// </summary>
    [Range(1, 3600)]
    public int RevalidateSeconds { get; set; } = 30;

    /// <summary>Temizlik servisinin (süresi dolan / iptal olan tahtaları kapatır) çalışma aralığı.</summary>
    [Range(1, 3600)]
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Temizlik servisi bu kadar saniyedir DB'den yeniden doğrulanmamış bağlantıların (yalnızca dinleyenler dahil)
    /// yetkisini yeniden kontrol eder; başarısızsa bağlantıya <c>BoardClosed("AccessRevoked")</c> gönderip gruptan çıkarır.
    /// </summary>
    [Range(1, 24 * 3600)]
    public int ListenerRevalidateSeconds { get; set; } = 300;

    /// <summary>
    /// Hub'ın kabul ettiği transport'lar. Varsayılan YALNIZCA WebSockets (gateway de yalnızca WebSocket upgrade'ini
    /// geçirir). Entegrasyon testleri TestServer üzerinden LongPolling kullandığı için test factory'si bunu
    /// <c>Whiteboard__AllowedTransports="WebSockets, LongPolling"</c> ile genişletir.
    /// </summary>
    public HttpTransportType AllowedTransports { get; set; } = HttpTransportType.WebSockets;
}
