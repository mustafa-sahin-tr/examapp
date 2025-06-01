using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class LearningOutcomeDetail : BaseEntity
{
    public int Id { get; set; }

    public string DetailText { get; set; } // Örn: "Öğrenciler, temel matematik işlemlerini yapabilir."

    public int LearningOutcomeId { get; set; } // Hangi öğrenme çıktısına ait olduğu


    [ForeignKey("LearningOutcomeId")]
    public LearningOutcome LearningOutcome { get; set; } // İlişkili öğrenme çıktısı
}
