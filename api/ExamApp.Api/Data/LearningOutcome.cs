using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class LearningOutcome : BaseEntity
{
    public int Id { get; set; }


    public string Description { get; set; } // Örn: "Öğrenciler, temel matematik işlemlerini yapabilir."

    public int GradeId { get; set; } // Hangi sınıfa ait olduğu

    public string Code { get; set; } // Örn: "M.3.1.1.1."

    public int SubTopicId { get; set; } // Hangi alt konuya ait olduğu

    [ForeignKey("SubTopicId")]
    public SubTopic SubTopic { get; set; } // İlişkili alt konu
    
    public ICollection<LearningOutcomeDetail> Details { get; set; } // İlişkili öğrenme çıktısı detayları

}
