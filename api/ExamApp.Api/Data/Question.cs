using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class Question : BaseEntity
{
    public int Id { get; set; }
    public string? Text { get; set; } = string.Empty;  // Soru metni
    public string? SubText { get; set; } = string.Empty;  // Soru metni
    public string? ImageUrl { get; set; }  // Eğer soru resimli ise

    public int SubjectId { get; set; }  // Soru hangi derse ait
    public Subject Subject { get; set; }


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
    public ICollection<WorksheetQuestion> WorksheetQuestions { get; set; } = new List<WorksheetQuestion>();

    public ICollection<Answer> Answers { get; set; } = new List<Answer>();  // Şıklar

    public int? CorrectAnswerId { get; set; }

    [ForeignKey("CorrectAnswerId")]
    public Answer CorrectAnswer { get; set; }   

    public int? PassageId { get; set; } // 🟢 Kapsam ID (Opsiyonel)

    [ForeignKey("PassageId")]
    public Passage Passage { get; set; } // 🟢 Navigation Property

    public string? PracticeCorrectAnswer { get; set; } // Eğer çalışma testi ise bu alan kullanılacak
    public bool IsExample { get; set; } = false;// Eğer true ise bu soru örnek sorudur ve cevabı otomatik gösterilir.
    public int AnswerColCount {get; set;} = 3; 
    // Şıkların kaç sütun olacağı. 
    // 3 demek yan yana 3 şık olacak demektir.
    // 1 demek cevaplar alt alta sıralanacak demektir.

    public double? X { get; set; }

    public double? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public bool IsCanvasQuestion { get; set; } = false;

}

