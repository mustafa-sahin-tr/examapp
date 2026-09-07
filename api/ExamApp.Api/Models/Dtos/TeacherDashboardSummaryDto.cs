using System;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Issue #53: öğretmen dashboard özet kartları.
/// Sadece giriş yapan öğretmenin sahip olduğu (CreateUserId == teacherId) worksheet'ler sayılır;
/// paylaşılan (shared) worksheet'ler hariçtir. Aktif/pasif ayrımı yoktur.
/// </summary>
public class TeacherDashboardSummaryDto
{
    public int TotalWorksheets { get; set; }
    public int TotalUniqueStudents { get; set; }
}
