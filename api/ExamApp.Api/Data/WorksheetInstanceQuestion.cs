using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;
using ExamApp.Foundation.Contracts;

public class WorksheetInstanceQuestion : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int WorksheetInstanceId { get; set; }

    [ForeignKey("WorksheetInstanceId")]
    public WorksheetInstance WorksheetInstance { get; set; }

    [Required]
    public int WorksheetQuestionId { get; set; }

    [ForeignKey("WorksheetQuestionId")]
    public WorksheetQuestion WorksheetQuestion { get; set; }

    /// <summary>
    /// issue #685: oturumdaki madde sırası (1 tabanlı) — oturum başlarken sayfanın o anki <c>(WorksheetQuestion.Order, Id)</c>
    /// sırasından kopyalanır. Oturum okuma yolları sırayı buradan alır (<c>(Order, Id)</c>): öğretmen sonradan maddeleri
    /// yeniden sıralasa da başlamış oturumun sırası değişmez. Mevcut oturumlar migration'da o anki sırayla dolduruldu.
    /// </summary>
    public int Order { get; set; }

    public int? SelectedAnswerId { get; set; }

    // Non-MCQ answers (e.g. drag-drop) stored as JSON payload
    public string? AnswerPayload { get; set; }

    [ForeignKey("SelectedAnswerId")]
    public Answer SelectedAnswer { get; set; }

    public bool IsCorrect { get; set; }
    public int TimeTaken { get; set; } // Kaç saniyede çözüldü
    public bool ShowCorrectAnswer { get; set; } // Kullanıcı "Sonucu Gör" yaptı mı?

    /// <summary>
    /// issue #279 review (blocker + security M1/L3): DB-generated monoton revizyon — her
    /// <c>TestSessionService.SaveAnswer</c> çağrısında atomik olarak artırılır (bkz. o metodun XML doc'u)
    /// ve <see cref="AnswerSubmittedEvent"/> ile taşınır. BadgeService'te
    /// <c>AnswerPointAward</c>'ın revizyon karşılaştırmasının birincil kaynağı budur — istemci saatine
    /// bağlı olmayan, DB tarafında UPDATE sırasına göre kesin sıralı bir sayaçtır (eski
    /// <c>AnswerSubmittedEvent.SubmittedAt</c> mikrosaniye altı çakışmalarda/istemci saat kaymasında
    /// sıra karıştırabiliyordu). Varsayılan 0 = henüz hiç cevaplanmamış satır.
    /// </summary>
    public int AnswerRevision { get; set; }

    /// <summary>
    /// issue #571: kontrolcünün (<c>IInteractionGrader</c>) satır bazlı sonucu — doğru satır sayısı. MCQ'da 1/0.
    /// <see cref="IsCorrect"/> = <c>CorrectCount == TotalCount &amp;&amp; TotalCount &gt; 0</c>. Kısmi puan yok: testte puan
    /// yalnız <see cref="IsCorrect"/> ile verilir.
    /// </summary>
    public int CorrectCount { get; set; }

    /// <summary>issue #571: kontrol edilen satır sayısı (MCQ'da 1; çözümü olmayan etkileşimli soruda 0 = puanlanamaz).</summary>
    public int TotalCount { get; set; }

    /// <summary>issue #571: sonucu kim verdi. Bugün yalnız <see cref="AnswerGradedBy.Auto"/> yazılır.</summary>
    public AnswerGradedBy GradedBy { get; set; } = AnswerGradedBy.Auto;

    /// <summary>
    /// issue #571: satır bazlı geri bildirim (JSON, jsonb), ör. <c>{"rows":[{"id":"z1","correct":false}]}</c>. Yalnız satır id'si
    /// ve doğru/yanlış taşır — beklenen cevabı (çözümü) ASLA içermez. MCQ'da null.
    /// </summary>
    public string? Feedback { get; set; }

    /// <summary>
    /// issue #573: etkinlikte bu satırın en son "Cevapları kontrol et" ile kontrol edildiği andaki <see cref="AnswerRevision"/>.
    /// Oynatıcı yeniden yüklenince satırın kontrol sonucu (doğru/yanlış, geri bildirim) yalnız
    /// <c>CheckedAnswerRevision == AnswerRevision</c> iken döner: kontrolden sonra save-answer ile değişen cevabın sonucu
    /// gösterilmez (aksi halde save-answer + yeniden yükleme sayılmayan, hız sınırsız bir "kontrol" olurdu). Hiç kontrol
    /// edilmediyse / testte null.
    /// </summary>
    public int? CheckedAnswerRevision { get; set; }
}

/// <summary>issue #571: cevabın sonucunu kim verdi.</summary>
public enum AnswerGradedBy
{
    Auto = 0,
    AI = 1,
    Teacher = 2
}
