using System;

namespace ExamApp.Api.Data;

public class Badge : BaseEntity
{
    public int Id { get; set; }
    public string Name { get; set; } // Rozet Adı (Örn: "100 Soru Çözdü")
    public string Description { get; set; } // Açıklama
    public string ImageUrl { get; set; } // Rozet Görseli
    public int RequiredXP { get; set; } // Kazanılması için gereken XP
    public int RequiredQuestionsSolved { get; set; } // Kazanılması için gereken soru sayısı

    public ICollection<StudentBadge> StudentBadges { get; set; }
}

