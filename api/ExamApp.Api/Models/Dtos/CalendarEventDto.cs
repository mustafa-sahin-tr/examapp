using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Öğrenci takvimindeki tek bir etkinlik. <see cref="Kind"/> değerine göre bazı alanlar dolu,
/// diğerleri null olur (reminder vs. assignment-deadline).
/// Etkinlikler <c>[from, to)</c> aralığında döner — <c>to</c> hariç (exclusive).
/// </summary>
public class CalendarEventDto
{
    /// <summary>"reminder" | "assignment-deadline"</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Etkinliğin gerçekleştiği an (UTC). reminder için ScheduledFor, deadline için EndAt.</summary>
    public DateTime Date { get; set; }

    public int WorksheetId { get; set; }

    public string WorksheetTitle { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string? ImageUrl { get; set; }

    // --- reminder alanları ---

    /// <summary>"Pending" | "Sent" (yalnızca Kind == "reminder").</summary>
    public string? Status { get; set; }

    public int? RemindBeforeMinutes { get; set; }

    // --- assignment-deadline alanları ---

    public bool? IsCompleted { get; set; }

    public string? TeacherName { get; set; }
}

public class StudentCalendarResponseDto
{
    public List<CalendarEventDto> Events { get; set; } = new();
}
