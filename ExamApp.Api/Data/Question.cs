using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class Question
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;  // Soru metni
    public string? SubText { get; set; } = string.Empty;  // Soru metni
    public string? ImageUrl { get; set; }  // Eğer soru resimli ise

    public int SubjectId { get; set; }  // Soru hangi derse ait
    public Subject Subject { get; set; }

    public int CorrectAnswer { get;set;}

    public int Point { get; set; }  // Sorunun puan değeri (1, 5, 10 vb.)

    public int? TopicId { get; set; } // Ana Konu (Opsiyonel)

    [ForeignKey("TopicId")]
    public Topic Topic { get; set; }

    public int? SubTopicId { get; set; } // Alt Konu (Opsiyonel)

    [ForeignKey("SubTopicId")]
    public SubTopic SubTopic { get; set; }

    [Required]
    public int DifficultyLevel { get; set; } = 1; // 1-10 arasında zorluk seviyesi

    // Bir soru birçok teste ait olabilir
    public ICollection<TestQuestion> TestQuestions { get; set; } = new List<TestQuestion>();

    public ICollection<Answer> Answers { get; set; } = new List<Answer>();  // Şıklar

    public int? PassageId { get; set; } // 🟢 Kapsam ID (Opsiyonel)

    [ForeignKey("PassageId")]
    public Passage Passage { get; set; } // 🟢 Navigation Property
}

