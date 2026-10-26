using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;


public enum WorksheetInstanceStatus
{
    Started = 0,   // 🟢 Test başladı
    Completed = 1, // ✅ Test tamamlandı
    Expired = 2    // ⏳ Süre doldu
}


public class WorksheetInstance : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [ForeignKey("StudentId")]
    public Student Student { get; set; }

    [Required]
    public int WorksheetId { get; set; }

    [ForeignKey("WorksheetId")]
    public Worksheet Worksheet { get; set; }

    public DateTime StartTime { get; set; }

    /// <summary>
    /// issue #396: süre sınırının başlangıçtaki kopyası (<c>Worksheet.MaxDurationSeconds</c>); sunucu süre kontrolü buna
    /// bakar, öğretmen test süresini sonradan değiştirse de açık oturumun süresi değişmez. null/&lt;=0 = süre sınırı yok
    /// (mevcut satırlar migration ile worksheet değerinden doldurulur; yeni instance'ı start-test yazar).
    /// </summary>
    public int? MaxDurationSeconds { get; set; }
    public DateTime? EndTime { get; set; }

    public ICollection<WorksheetInstanceQuestion> WorksheetInstanceQuestions { get; set; }

    public WorksheetInstanceStatus Status { get; set; } // 🟢 Test durumu

    /// <summary>issue #571: etkinlikte "Cevapları kontrol et" sayısı (<c>POST test-instances/{id}/check</c>). Testte hep 0.</summary>
    public int CheckCount { get; set; }

    /// <summary>issue #571: ilk kontrol anı (UTC); hiç kontrol edilmediyse null.</summary>
    public DateTime? FirstCheckedAt { get; set; }

    /// <summary>issue #571: öğrencinin "doğru cevapları göster" ile çözümü ilk açtığı an (UTC); açmadıysa null.</summary>
    public DateTime? SolutionsRevealedAt { get; set; }

    /// <summary>
    /// issue #573: ilk kontroldeki doğru satır sayısı (öğretmen takibi "7 doğru · 3 yanlış"). İlk kontrol
    /// (<see cref="FirstCheckedAt"/> null iken) ile aynı transaction'da bir kez yazılır; sonraki kontroller değiştirmez. Testte null.
    /// </summary>
    public int? FirstCheckCorrectCount { get; set; }

    /// <summary>issue #573: ilk kontroldeki toplam satır sayısı (gönderilen maddelerin <c>TotalCount</c> toplamı). Testte null.</summary>
    public int? FirstCheckTotalCount { get; set; }

    /// <summary>
    /// issue #572: etkinliğin etkin çalışma süresi (sn) — tamamlanmada (<c>EndTest</c>, Started → Completed) bir kez yazılır:
    /// istemcinin bildirdiği değer (yoksa maddelerin <c>TimeTaken</c> toplamı), <c>[0, min(ActivityOptions.MaxActiveSecondsPerActivity,
    /// başlangıçtan beri geçen süre)]</c> aralığına kırpılmış. <c>ActivityCompletedEvent.ActiveSeconds</c> ile aynı değer. Testte ve
    /// tamamlanmamış etkinlikte null.
    /// </summary>
    public int? ActiveSeconds { get; set; }
}
