using System;

namespace ExamApp.Api.Data;

public class Passage
{
    public int Id { get; set; }
    public string? Title { get; set; } // Kapsam başlığı (isteğe bağlı)
    public string? Text { get; set; } // Kapsam metni
    public string? ImageUrl { get; set; } // Eğer tablo, grafik vs. varsa resim URL'si
    public ICollection<Question> Questions { get; set; } = new List<Question>(); // Bu kapsama bağlı sorular
}
