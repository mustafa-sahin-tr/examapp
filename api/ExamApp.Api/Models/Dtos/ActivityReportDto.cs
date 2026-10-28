using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// issue #584: öğretmenin atanmış etkinlik sayfası analizi (<c>GET api/worksheet/{id}/activity-report</c>). Kapsam "Çalışma takibi"
/// (#573) ile aynı: istek sahibinin kendi atamaları, okul kapsamındaki öğrenciler, atama penceresinde sayılan oturum. Analize yalnız
/// tamamlanmış ve şu an yeniden çözülmeyen oturumlar girer (yeniden çözümde cevaplar silinmiştir, #609); cevaplar oturumun SON
/// hâlidir (ilk kontrol özeti değil). Sonuç sunucuda kontrolcüyle yeniden hesaplanır.
/// </summary>
public sealed class ActivityReportDto
{
    public int WorksheetId { get; set; }
    public string WorksheetName { get; set; } = string.Empty;
    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Atamaların hedeflediği öğrenci sayısı (tekil).</summary>
    public int StudentCount { get; set; }

    /// <summary>Analize giren öğrenci (tamamlamış, yeniden çözmüyor).</summary>
    public int AnalyzedCount { get; set; }

    /// <summary>Tamamlamış ama şu an yeniden çözdüğü için analiz dışı kalan öğrenci.</summary>
    public int RetakingCount { get; set; }

    /// <summary>Sayfanın maddeleri (sıra ile).</summary>
    public List<ActivityReportQuestionDto> Questions { get; set; } = new();

    /// <summary>Isı haritası satırları: analize giren öğrenciler (öğrenci numarasına göre).</summary>
    public List<ActivityReportStudentDto> Students { get; set; } = new();
}

public sealed class ActivityReportQuestionDto
{
    public int WorksheetQuestionId { get; set; }

    /// <summary>1'den başlayan madde numarası (sayfadaki sıra).</summary>
    public int Number { get; set; }

    /// <summary><c>Question.InteractionType</c> (null = MCQ).</summary>
    public string? InteractionType { get; set; }

    /// <summary>Otomatik kontrol edilebilir mi (openText ya da çözümsüz madde: false — oran gösterilmez).</summary>
    public bool Gradable { get; set; }

    /// <summary>Maddeyi tamamen doğru yapan öğrenci sayısı.</summary>
    public int FullyCorrectCount { get; set; }

    /// <summary>Maddeye en az bir cevap veren öğrenci sayısı.</summary>
    public int AnsweredCount { get; set; }

    /// <summary>Satır/öğe başına dağılım (kontrolcünün öğe sırası; çeldiriciler sonda).</summary>
    public List<ActivityReportItemDto> Items { get; set; } = new();
}

public sealed class ActivityReportItemDto
{
    public string ItemId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Çeldirici öğe (çözümde yeri olmayan kart / hedef dışı token): doğru = kullanılmamış.</summary>
    public bool Distractor { get; set; }

    /// <summary>Doğru yapan öğrenci sayısı (payda <see cref="ActivityReportDto.AnalyzedCount"/> değil, <see cref="StudentCount"/>).</summary>
    public int CorrectCount { get; set; }

    /// <summary>Boş bırakan öğrenci sayısı.</summary>
    public int EmptyCount { get; set; }

    /// <summary>Bu öğenin sayıldığı öğrenci sayısı (maddeyi analiz edilen öğrenciler).</summary>
    public int StudentCount { get; set; }

    /// <summary>En sık yanlış cevaplar (çoktan aza, en çok <c>ActivityReportService.MaxWrongAnswers</c>).</summary>
    public List<ActivityReportAnswerDto> WrongAnswers { get; set; } = new();
}

public sealed class ActivityReportAnswerDto
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class ActivityReportStudentDto
{
    public int StudentId { get; set; }
    public int UserId { get; set; }
    public string StudentNumber { get; set; } = string.Empty;
    public string? GradeName { get; set; }

    /// <summary>Madde başına hücre, <see cref="ActivityReportDto.Questions"/> sırasıyla.</summary>
    public List<ActivityReportCellDto> Cells { get; set; } = new();
}

/// <summary>Isı haritası hücresi: maddede doğru satır / toplam satır. <see cref="Answered"/> false = madde boş.</summary>
public sealed class ActivityReportCellDto
{
    public bool Answered { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
}
