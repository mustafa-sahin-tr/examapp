namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// issue #571: <c>POST api/test-instances/{instanceId}/check</c> (gateway: <c>/api/exam/test-instances/{id}/check</c>) gövdesi —
/// etkinliğin madde cevapları. Her madde en çok bir kez; en çok <see cref="MaxAnswers"/> madde. Sınırlar (madde sayısı, id,
/// payload boyutu) DataAnnotation ile DEĞİL serviste denetlenir (review #571): tüm 400'ler tek biçimde döner
/// (<c>{ success:false, invalidInput:true, errorCode, message }</c>).
/// </summary>
public class ActivityCheckRequestDto
{
    public const int MaxAnswers = 100;

    public List<ActivityCheckAnswerDto> Answers { get; set; } = new();
}

public class ActivityCheckAnswerDto
{
    /// <summary>Oturumdaki soru satırı (<c>WorksheetInstanceQuestion.Id</c>).</summary>
    public int InstanceQuestionId { get; set; }

    /// <summary>MCQ: seçilen şık; yoksa null/0.</summary>
    public int? SelectedAnswerId { get; set; }

    /// <summary>Etkileşim cevabı (JSON, ör. <c>dragDropLabeling</c> payload'u); yoksa null. En çok 16 KB (serviste).</summary>
    public string? AnswerPayload { get; set; }
}

/// <summary>
/// issue #571: kontrol yanıtı. Toplamlar gönderilen maddelerin satır sayılarının toplamıdır. Çözümü / doğru şıkkı İÇERMEZ.
/// </summary>
public class ActivityCheckResultDto : TestSessionResultDto
{
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public List<ActivityCheckQuestionResultDto> Questions { get; set; } = new();
}

public class ActivityCheckQuestionResultDto
{
    public int InstanceQuestionId { get; set; }
    public bool IsCorrect { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }

    /// <summary>Satır bazlı geri bildirim (JSON string, <c>{"rows":[{"id","correct"}]}</c>) ya da null (MCQ / puanlanamaz).</summary>
    public string? Feedback { get; set; }
}

/// <summary>issue #571: "doğru cevapları göster" yanıtı (<c>GET api/test-instances/{instanceId}/solutions</c>).</summary>
public class ActivitySolutionsResultDto : TestSessionResultDto
{
    public List<ActivitySolutionDto> Questions { get; set; } = new();
}

public class ActivitySolutionDto
{
    public int InstanceQuestionId { get; set; }
    public int QuestionId { get; set; }

    /// <summary>MCQ: doğru şık; etkileşimli soruda null.</summary>
    public int? CorrectAnswerId { get; set; }

    /// <summary>Etkileşimli soru: çözüm (JSON, ör. <c>{"placements":[...]}</c>); MCQ'da null.</summary>
    public string? InteractionSolution { get; set; }
}

/// <summary>
/// issue #609: "Yeniden çöz" yanıtı (<c>POST api/test-instances/{instanceId}/retake</c>). Aynı oturum sıfırlanır (yeni oturum
/// açılmaz): <see cref="InstanceId"/> istekteki oturumdur, oynatıcı onu yeniden açar.
/// </summary>
public class ActivityRetakeResultDto : TestSessionResultDto
{
    public int InstanceId { get; set; }

    /// <summary>
    /// true = oturum zaten yeniden çözülüyordu (bir önceki retake / çift tık); hiçbir şey sıfırlanmadı, mevcut cevaplar duruyor.
    /// </summary>
    public bool AlreadyRetaking { get; set; }
}
