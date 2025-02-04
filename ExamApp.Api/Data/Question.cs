using System;

namespace ExamApp.Api.Data;

public class Question
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;  // Soru metni
    public string? ImageUrl { get; set; }  // Eğer soru resimli ise

    public int CategoryId { get; set; }  // Soru hangi derse ait
    public Category Category { get; set; }

    public int CorrectAnswer { get;set;}

    public int Point { get; set; }  // Sorunun puan değeri (1, 5, 10 vb.)

    // Bir soru birçok teste ait olabilir
    public ICollection<TestQuestion> TestQuestions { get; set; } = new List<TestQuestion>();

    public ICollection<Answer> Answers { get; set; } = new List<Answer>();  // Şıklar
}

