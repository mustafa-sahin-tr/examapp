using System;

namespace ExamApp.Api.Models.Dtos;

public class BulkQuestionCreateDto
{
    public string ImageData { get; set; } // MinIO'ya yüklenecek, EF'e ImageUrl olarak gidecek
    public List<BulkPassageDto> Passages { get; set; }
    public List<BulkQuestionDto> Questions { get; set; }
    public HeaderInfo Header { get; set; }
}

public class BulkPassageDto
{
    public string Id { get; set; } // JSON'dan geliyor, ancak EF’de kullanılmayacak
    public double X { get; set; }
    public double Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class BulkQuestionDto
{
    public string Name { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsExample { get; set; } // EF'de `IsExample`
    public string? ExampleAnswer { get; set; } // EF'de `PracticeCorrectAnswer`
    public List<BulkAnswerDto> Answers { get; set; } = new List<BulkAnswerDto>();
}

public class BulkAnswerDto
{
    public string Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsCorrect { get; set; }
}


public class HeaderInfo {
    public int? TestId { get; set; }
    public int? TopicId { get; set; }

    public int? SubjectId { get; set; }
    public int? SubtopicId { get; set; }

}