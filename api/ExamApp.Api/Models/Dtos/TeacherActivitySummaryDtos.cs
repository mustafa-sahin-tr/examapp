using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Issue #56: öğretmen dashboard "Benim Aktivitem" kartı.
/// Pencere = bugün dahil son <c>days</c> takvim günü; gün sınırları UTC'dir (başlangıç = UTC bugün 00:00 − (days−1) gün).
/// Türkiye saatiyle (UTC+3) 00:00–03:00 arasındaki kayıtlar önceki güne düşer.
/// </summary>
public class TeacherOwnActivitySummaryDto
{
    /// <summary>Öğretmenin pencerede oluşturduğu (CreateUserId == öğretmen) worksheet sayısı; silinenler hariç.</summary>
    public int WorksheetsCreated { get; set; }

    /// <summary>
    /// Öğretmenin pencerede oluşturduğu atama satırı sayısı (CreateUserId == öğretmen; silinenler hariç).
    /// Ürün kararı: öğretmenin yaptığı TÜM atamalar sayılır — kendi worksheet'lerine ve başka öğretmenin
    /// PublicAssignable worksheet'lerine yapılanlar dahil. Bir sınıf ataması tek atama sayılır.
    /// </summary>
    public int AssignmentsCreated { get; set; }

    /// <summary>
    /// Etkin öğrenci: öğretmenin kendi worksheet'lerine atanmış (dashboard-summary ile aynı kapsam) ve pencerede
    /// o atandığı worksheet'lerde en az bir soru cevaplamış öğrenci sayısı. students-activity-summary'de
    /// QuestionsSolved &gt; 0 olan öğrenci sayısına eşittir.
    /// </summary>
    public int ActiveStudents { get; set; }
}

/// <summary>
/// Issue #56: öğretmen dashboard "Öğrenci Aktivitesi" kartı + "En Aktif Öğrenciler" tablosu.
/// Yalnızca öğretmenin kendi worksheet'lerine atanan öğrencilerin, atandıkları bu worksheet'lerde pencerede
/// cevapladıkları sorular sayılır.
/// Ürün kararı: (worksheet, öğrenci) çifti bir atamayla hedefleniyorsa cevap, atamanın StartAt/EndAt penceresinden
/// BAĞIMSIZ sayılır (erken/geç çözülen sorular da dahil); belirleyici olan cevap anının (UpdateTime) rapor penceresinde
/// olmasıdır. Pencere gün sınırları UTC'dir (bkz. <see cref="TeacherOwnActivitySummaryDto"/>).
/// </summary>
public class TeacherStudentsActivitySummaryDto
{
    /// <summary>Pencerede cevaplanan soru sayısı (tüm kapsamdaki öğrenciler, yalnız TopStudents değil).</summary>
    public int TotalQuestionsSolved { get; set; }

    public int TotalCorrectCount { get; set; }

    /// <summary>Cevaplanan soruların soru bazlı çözüm süreleri (TimeTaken) toplamı, saniye; 0 veya negatif süre 0 sayılır.</summary>
    public int TotalTimeSeconds { get; set; }

    /// <summary>
    /// En fazla 10 öğrenci; QuestionsSolved azalan (eşitlikte CorrectCount azalan, TimeSeconds artan, StudentId artan).
    /// Hiç soru çözmemiş öğrenciler listelenmez; veri yoksa [].
    /// </summary>
    public List<TeacherActiveStudentDto> TopStudents { get; set; } = new();
}

public class TeacherActiveStudentDto
{
    public int StudentId { get; set; }

    /// <summary>Auth-api'den çözümlenen ad-soyad; erişilemezse "Öğrenci #{StudentNumber}" fallback'i.</summary>
    public string StudentName { get; set; } = string.Empty;

    public int QuestionsSolved { get; set; }

    public int CorrectCount { get; set; }

    public int TimeSeconds { get; set; }
}
