using System;

namespace ExamApp.Api.Data;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Bir kategori birçok soruya sahip olabilir
    public ICollection<Question> Questions { get; set; } = new List<Question>();
}
