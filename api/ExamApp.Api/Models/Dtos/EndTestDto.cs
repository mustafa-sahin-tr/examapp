using ExamApp.Api.Services.Storage;
using System;

namespace ExamApp.Api.Models.Dtos;

public class EndTestInstanceDto
{
    public int Id { get; set; }

    public int DurationSeconds { get; set; }

}

/// <summary>
/// issue #572: <c>PUT end-test/{testInstanceId}</c> isteğe bağlı gövdesi. Gövdesiz çağrı (test bitirme, eski istemci) geçerlidir.
/// </summary>
public class EndTestRequestDto
{
    /// <summary>
    /// Yalnız etkinlikte okunur: oynatıcının ölçtüğü etkin süre (sn; 2 dk'dan uzun hareketsizlik hariç). Sunucu güvenmez,
    /// <c>[0, min(ActivityOptions.MaxActiveSecondsPerActivity, oturum başlangıcından beri geçen süre)]</c> aralığına kırpar;
    /// gönderilmezse maddelerin <c>TimeTaken</c> toplamı aynı kuralla kullanılır. Testte yok sayılır.
    /// </summary>
    public int? ActiveSeconds { get; set; }
}

public class InstanceSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [StorageUrl(StorageArea.WorksheetCover)]
    public string? ImageUrl { get; set; }
    public DateTime CompletedDate { get; set; }
    public int Score { get; set; }
    public int DurationMinutes { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int TotalQuestions { get; set; }

    public int Status { get; set; } // 0: In Progress, 1: Completed, 2: Canceled

    /// <summary>
    /// issue #573: yalnız etkinlikte dolu — cevaplanmış (boş olmayan) madde sayısı (liste kartındaki mini ilerleme). Etkinlikte
    /// <see cref="Score"/>/<see cref="CorrectAnswers"/>/<see cref="WrongAnswers"/> hesaplanmaz (0). Testte null.
    /// </summary>
    public int? AnsweredQuestions { get; set; }

    /// <summary>issue #573: yalnız etkinlikte dolu — "Cevapları kontrol et" sayısı. Testte null.</summary>
    public int? CheckCount { get; set; }

    /// <summary>issue #573: yalnız etkinlikte — ilk kontroldeki doğru satır sayısı; kontrol yoksa / testte null.</summary>
    public int? FirstCheckCorrectCount { get; set; }

    /// <summary>issue #573: yalnız etkinlikte — ilk kontroldeki toplam satır sayısı; kontrol yoksa / testte null.</summary>
    public int? FirstCheckTotalCount { get; set; }
}
