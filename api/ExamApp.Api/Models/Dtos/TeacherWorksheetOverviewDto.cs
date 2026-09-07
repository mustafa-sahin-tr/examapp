using System;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Issue #54: öğretmen dashboard "Sınavlarım" tablosu satırı.
/// Sadece giriş yapan öğretmenin sahip olduğu (CreateUserId == teacherId) worksheet'ler döner;
/// paylaşılan (shared) worksheet'ler hariçtir.
/// </summary>
public class TeacherWorksheetOverviewDto
{
    public int WorksheetId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Bu worksheet için hedeflenen benzersiz öğrenci sayısı
    /// (direkt StudentId + GradeId/SchoolId bazlı atamalar genişletilip distinct alınır).
    /// </summary>
    public int AssignedStudentCount { get; set; }

    /// <summary>
    /// 0-100 arası. Tamamlayan öğrenci / AssignedStudentCount * 100. Atanan öğrenci yoksa 0.
    /// </summary>
    public double CompletionPercentage { get; set; }
}
