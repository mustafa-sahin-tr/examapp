using ExamApp.Api.Data;

public class ExamTest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Test içindeki sorular (Ara tablo ile ilişkilendirilecek)
    public ICollection<TestQuestion> TestQuestions { get; set; } = new List<TestQuestion>();
}
