using System;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Issue #55: öğretmen dashboard "Geride Kalan Öğrenciler" listesi satırı.
/// Granülarite öğrenci + worksheet çiftidir. Sadece giriş yapan öğretmenin sahip olduğu
/// (CreateUserId == teacherId) worksheet'lerdeki atamalar dikkate alınır ve yalnızca
/// en az bir bayrağı (IsLowCompletion / IsExpired) true olan satırlar döner.
/// </summary>
public class TeacherLaggingStudentDto
{
    public int StudentId { get; set; }

    /// <summary>
    /// Auth-api'den çözümlenen ad-soyad; erişilemezse "Öğrenci #{StudentNumber}" fallback'i.
    /// </summary>
    public string StudentName { get; set; } = string.Empty;

    public int WorksheetId { get; set; }

    public string WorksheetName { get; set; } = string.Empty;

    /// <summary>
    /// 0-100 arası. Öğrenci-worksheet ataması bazında: ilgili atama penceresinde
    /// tamamlanmışsa 100, değilse 0 (WorksheetInstance'ta soru bazlı ilerleme alanı yoktur;
    /// tamamlanma kuralı GetWorksheetsOverviewAsync ile aynıdır).
    /// </summary>
    public double CompletionPercentage { get; set; }

    /// <summary>CompletionPercentage &lt; 50.</summary>
    public bool IsLowCompletion { get; set; }

    /// <summary>
    /// İlgili atamanın EndAt'i dolu ve geçmiş VE öğrenci o atamada tamamlamamış.
    /// </summary>
    public bool IsExpired { get; set; }
}
