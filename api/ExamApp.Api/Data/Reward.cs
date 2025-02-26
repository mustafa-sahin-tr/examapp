using System;

namespace ExamApp.Api.Data;


/// <summary>
/// Öğrencilerin puanlarını kullanarak alabilecekleri ödüller.
/// </summary>
public class Reward
{
    public int Id { get; set; }
    public string Name { get; set; } // Ödül Adı (Örn: "30 Dakika Tablet", "Oyuncak")
    public int PointsRequired { get; set; } // Ödül için gereken puan
    public int Stock { get; set; } // Stok durumu

    public ICollection<StudentReward> StudentRewards { get; set; }
}

