using System;

namespace ExamApp.Api.Data;

/// <summary>
/// Hangi öğrencinin hangi özel etkinlikten puan kazandığını takip eder.
/// </summary>
public class StudentSpecialEvent : BaseEntity
{
    public int Id { get; set; }
    public int StudentId { get; set; } // Öğrenci FK
    public int SpecialEventId { get; set; } // Etkinlik FK
    public bool Completed { get; set; } // Etkinliği tamamladı mı?
    public DateTime? CompletedAt { get; set; } // Tamamlandıysa zamanı

    public Student Student { get; set; }
    public SpecialEvent SpecialEvent { get; set; }
}
