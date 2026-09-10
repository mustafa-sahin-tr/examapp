using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Öğrenci takvimindeki tek bir etkinlik. <see cref="Kind"/> değerine göre bazı alanlar dolu,
/// diğerleri null olur (reminder vs. assignment-deadline vs. ProgramStudyItem).
/// Etkinlikler <c>[from, to)</c> aralığında döner — <c>to</c> hariç (exclusive).
/// </summary>
public class CalendarEventDto
{
    /// <summary>"reminder" | "assignment-deadline" | "program-study-page"</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Etkinliğin gerçekleştiği/başladığı an (UTC). reminder için ScheduledFor, deadline için EndAt,
    /// program-study-page için plan StartDate.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>Çok günlü etkinliklerde bitiş anı (UTC). Yalnızca Kind == "program-study-page" için dolu.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Worksheet tabanlı etkinliklerde dolu; ProgramStudyItem için 0.</summary>
    public int WorksheetId { get; set; }

    public string WorksheetTitle { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string? ImageUrl { get; set; }

    // --- reminder alanları ---

    /// <summary>"Pending" | "Sent" (yalnızca Kind == "reminder").</summary>
    public string? Status { get; set; }

    public int? RemindBeforeMinutes { get; set; }

    // --- assignment-deadline / ProgramStudyItem ortak alanı ---

    public bool? IsCompleted { get; set; }

    // --- assignment-deadline alanları ---

    public string? TeacherName { get; set; }

    // --- ProgramStudyItem alanları ---

    public int? ProgramId { get; set; }

    public string? ProgramName { get; set; }

    public int? StudyItemId { get; set; }

    public string? StudyItemTitle { get; set; }
}

public class StudentCalendarResponseDto
{
    public List<CalendarEventDto> Events { get; set; } = new();
}
