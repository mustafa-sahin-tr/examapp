using System;

namespace ExamApp.Api.Data;

/// <summary>
/// Özel yarışmalar, günlük/haftalık görevler ve büyük ödüller için etkinlik yapısını sağlar.
/// </summary>
public class SpecialEvent
{
    public int Id { get; set; }
    public string Name { get; set; } // Etkinlik Adı
    public string Description { get; set; } // Açıklama
    public int RewardPoints { get; set; } // Kazanılacak puan miktarı
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
