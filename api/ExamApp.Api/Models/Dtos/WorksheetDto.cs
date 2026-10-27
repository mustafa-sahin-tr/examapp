using ExamApp.Api.Services.Storage;
using System;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }

    public int? TopicId { get; set; } // Konu ID'si (isteğe bağlı)
    public int? SubTopicId { get; set; } // Alt konu ID'si (isteğe bağlı)
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }

    /// <summary>issue #573: sayfa türü (0 = Test, 1 = Activity). Activity'de <see cref="Instance"/> skor alanları hesaplanmaz.</summary>
    public WorksheetKind Kind { get; set; } = WorksheetKind.Test;

    // issue #105: worksheet varsayılanı — öğrenciler yeni yorum-soru yazabilir mi (atama override'ı hariç).
    public bool CommentsEnabled { get; set; } = true;
    public string? Subtitle { get; set; }
    [StorageUrl(StorageArea.WorksheetCover)]
    public string? ImageUrl { get; set; }
    public string? BadgeText { get; set; }
    public int? BookTestId { get; set; }
    public int? BookId { get; set; }
    public int QuestionCount { get; set; } // ✅ Eklenen alan

    public InstanceSummaryDto? Instance { get; set; }

    public int InstanceCount { get; set; } = 0;// ✅ Eklenen alan = 0

    // Yetki modeli: "sahibi VEYA admin". CanEdit istek sahibine göre hesaplanır.
    public bool CanEdit { get; set; }

    // worksheet.CreateUserId — legacy kayıtlarda null olabilir.
    public int? CreatedByUserId { get; set; }

    // Sadece istek sahibi admin ise doldurulur; aksi halde null.
    public string? CreatedByName { get; set; }

    // --- Görünürlük eksenleri (issue #9). Davranış değişmez; UI için taşınır. ---

    // Öğretmenler arası paylaşım ekseni. Şu an her kayıt Private.
    public WorksheetTeacherSharing TeacherSharing { get; set; } = WorksheetTeacherSharing.Private;

    // Öğrenciye görünürlük ekseni. Şu an her kayıt Normal.
    public WorksheetStudentVisibility StudentVisibility { get; set; } = WorksheetStudentVisibility.Normal;

    // İstek sahibi bu worksheet'in sahibi mi (legacy CreateUserId null/0 => false).
    public bool IsOwner { get; set; }

    // Tekil detay (GetWorksheetByIdAsync) akışında admin VEYA sahip için dolu.
    // Liste akışlarında (N+1'den kaçınmak için) yalnız admin için dolu; aksi halde null.
    public string? OwnerName { get; set; }

    // İstek sahibi bu worksheet'i öğrenciye atayabilir mi (şimdilik CanEdit ile aynı).
    public bool CanAssign { get; set; }

    // Öğrenci akışı (issue #14): bu sınav istek sahibi öğrenciye/sınıfına aktif olarak
    // atanmış mı (true), yoksa yalnızca keşfet listesinden mi görünüyor (false).
    // Öğretmen akışlarında anlamsız, her zaman false döner.
    public bool IsAssigned { get; set; }
}

public class WorksheetWithInstanceDto
{
    public WorksheetDto Worksheet { get; set; } = default!;
    public WorksheetInstance? Instance { get; set; }
}

public class WorksheetInstanceDto
{
    public int Id { get; set; }
    public string TestName { get; set; } = default!;
    public WorksheetInstanceStatus Status { get; set; }
    public int MaxDurationSeconds { get; set; }

    /// <summary>
    /// issue #396: sunucunun hesapladığı kalan süre (saniye; süre sınırı yoksa null, oturum bitmişse 0). İstemci sayacı
    /// bundan başlar — yenileme/arka plan sekmesi süreyi sıfırlamaz.
    /// </summary>
    public int? RemainingSeconds { get; set; }
    public bool IsPracticeTest { get; set; }

    public List<WorksheetInstanceQuestionDto> TestInstanceQuestions { get; set; } = new();
}

public class WorksheetInstanceQuestionDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public QuestionDto Question { get; set; } = default!;
    public int? SelectedAnswerId { get; set; }
    public string? AnswerPayload { get; set; }
    public int? TimeTaken { get; set; }

    // issue #571: etkileşimli (MCQ dışı) sorunun kontrol sonucu — yalnız sonuç ekranında (bitmiş test, çözümle aynı kapı);
    // diğer yanıtlarda ve MCQ'da null. Feedback: {"rows":[{"id","correct"}]} (çözüm içermez).
    public bool? IsCorrect { get; set; }
    public int? CorrectCount { get; set; }
    public int? TotalCount { get; set; }
    public string? Feedback { get; set; }

    /// <summary>
    /// issue #583: açık uçlu (<c>openText</c>) maddede öğrencinin kendi değerlendirme durumu (pending / reviewed / outdated +
    /// değerlendirme ve geri bildirim); diğer tiplerde, cevapsız ya da gönderilmemiş ve değerlendirilmemiş maddede null.
    /// </summary>
    public ExamApp.Api.Models.Dtos.OpenText.OpenTextStudentReviewDto? OpenTextReview { get; set; }
}

public class WorksheetInstanceResultDto
{
    public int Id { get; set; }
    /// <summary>issue #383: instance'ın ait olduğu worksheet — UI tamamlanınca /test/{WorksheetId} sonucuna yönlenir.</summary>
    public int WorksheetId { get; set; }
    public string TestName { get; set; } = default!;
    public WorksheetInstanceStatus Status { get; set; }
    public int MaxDurationSeconds { get; set; }

    /// <summary>
    /// issue #396: sunucunun hesapladığı kalan süre (saniye; süre sınırı yoksa null, oturum bitmişse 0). İstemci sayacı
    /// bundan başlar — yenileme/arka plan sekmesi süreyi sıfırlamaz.
    /// </summary>
    public int? RemainingSeconds { get; set; }
    public bool IsPracticeTest { get; set; }

    /// <summary>issue #573: sayfa türü (0 = Test, 1 = Activity).</summary>
    public WorksheetKind Kind { get; set; } = WorksheetKind.Test;

    /// <summary>issue #573: worksheet açıklaması (etkinlikte yönerge yedeği); TestName ile aynı görünürlük.</summary>
    public string? WorksheetDescription { get; set; }

    /// <summary>issue #573: etkinlikte "Cevapları kontrol et" sayısı; testte null.</summary>
    public int? CheckCount { get; set; }

    /// <summary>issue #573: etkinlikte ilk kontrol anı (UTC); kontrol yoksa / testte null.</summary>
    public DateTime? FirstCheckedAt { get; set; }

    /// <summary>issue #573: etkinlikte çözümün ilk açıldığı an (UTC); açılmadıysa / testte null.</summary>
    public DateTime? SolutionsRevealedAt { get; set; }

    /// <summary>issue #573: etkinlikte ilk kontroldeki doğru satır sayısı; kontrol yoksa / testte null.</summary>
    public int? FirstCheckCorrectCount { get; set; }

    /// <summary>issue #573: etkinlikte ilk kontroldeki toplam satır sayısı; kontrol yoksa / testte null.</summary>
    public int? FirstCheckTotalCount { get; set; }

    /// <summary>
    /// issue #609: etkinliğin ilk tamamlanma anı (UTC); hiç tamamlanmadıysa / testte null. <c>Status = Started</c> iken doluysa
    /// oturum yeniden çözülüyordur.
    /// </summary>
    public DateTime? FirstCompletedAt { get; set; }

    public List<WorksheetInstanceQuestionDto> TestInstanceQuestions { get; set; } = new();
}
