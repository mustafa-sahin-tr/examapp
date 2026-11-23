namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #1310: öğrencinin oturumdaki çizimleri (soru başına; gönderilmeyen soru değişmez).</summary>
public sealed class SaveSessionDrawingsDto
{
    public List<SessionDrawingItemDto> Items { get; set; } = new();
}

public sealed class SessionDrawingItemDto
{
    /// <summary><c>WorksheetInstanceQuestion.Id</c> (istemcide <c>TestInstanceQuestion.id</c>).</summary>
    public int InstanceQuestionId { get; set; }

    /// <summary>Soru görseli üstündeki çizgiler; <c>[renk:]M… L…</c> biçimi, genişlik 1000 birim.</summary>
    public List<string>? Ink { get; set; }

    /// <summary>Karalama alanındaki çizgiler.</summary>
    public List<string>? Strokes { get; set; }

    public string? Text { get; set; }
}

/// <summary>Issue #1310: öğretmen / veli için oturumun soru soru görünümü (salt okunur).</summary>
public sealed class SessionReviewDto
{
    public int TestInstanceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public List<SessionReviewQuestionDto> Questions { get; set; } = new();
}

public sealed class SessionReviewQuestionDto
{
    public int InstanceQuestionId { get; set; }
    /// <summary>Testteki sıra (1'den).</summary>
    public int Number { get; set; }
    [Services.Storage.StorageUrl(Services.Storage.StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }
    /// <summary>Görselsiz soruda (#590) soru metni; görselli soruda null.</summary>
    public string? QuestionText { get; set; }
    public string? InteractionType { get; set; }
    /// <summary>Öğrencinin şıkkı (A, B …); boş ya da etkileşim cevabında null.</summary>
    public string? SelectedLetter { get; set; }
    public string? CorrectLetter { get; set; }
    /// <summary><c>correct</c> | <c>wrong</c> | <c>blank</c>.</summary>
    public string Outcome { get; set; } = "blank";
    public int TimeTakenSeconds { get; set; }
    public List<string> Ink { get; set; } = new();
    public List<string> Strokes { get; set; } = new();
    public string? Text { get; set; }
}
