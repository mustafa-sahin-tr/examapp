using System;
using System.Collections.Generic;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Öğrenci takvimindeki tek bir etkinlik. <see cref="Kind"/> değerine göre bazı alanlar dolu,
/// diğerleri null olur (reminder vs. assignment-deadline vs. ProgramStudyItem).
/// ProgramStudyItem etkinliği ayrıca StudyItem içerik tipi alanlarını taşır (ContentType, Url, Platform,
/// BookName, BookTestName, StartPage, EndPage); diğer kind'larda bunlar null'dır.
/// Etkinlikler <c>[from, to)</c> aralığında döner — <c>to</c> hariç (exclusive).
/// </summary>
public class CalendarEventDto
{
    /// <summary>"reminder" | "assignment-deadline" | "program-study-page" | "booking"</summary>
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

    // İçerik tipi (issue #141/#142): takvimde güne tıklanınca etkinlik detayı tipe göre render edilir.
    // Alan adları/tipleri UserProgramStudyPageScheduleDto ile birebir aynıdır. Yalnızca Kind == "program-study-page" için dolu.

    /// <summary>Etkinliğin içerik tipi (Image | Link | BookPageRange). Enum sayısal (int) serileşir.</summary>
    public StudyItemContentType? ContentType { get; set; }

    /// <summary>Link tipi — bağlantı adresi (yalnızca ContentType == Link iken dolu).</summary>
    public string? Url { get; set; }

    /// <summary>Link tipi — bağlantının platformu (yalnızca ContentType == Link iken dolu).</summary>
    public StudyItemLinkPlatform? Platform { get; set; }

    /// <summary>BookPageRange tipi — kitap adı (yalnızca ContentType == BookPageRange iken dolu).</summary>
    public string? BookName { get; set; }

    /// <summary>BookPageRange tipi — test adı (yalnızca ContentType == BookPageRange iken dolu).</summary>
    public string? BookTestName { get; set; }

    /// <summary>BookPageRange tipi — başlangıç sayfası.</summary>
    public int? StartPage { get; set; }

    /// <summary>BookPageRange tipi — bitiş sayfası.</summary>
    public int? EndPage { get; set; }

    // --- booking alanları (issue #96, Kind == "booking") ---

    /// <summary>Onaylanmış randevunun kimliği.</summary>
    public int? BookingId { get; set; }

    /// <summary>Randevunun dayandığı müsaitlik aralığı.</summary>
    public int? AvailabilitySlotId { get; set; }

    /// <summary>Randevudaki öğretmenin Teacher.Id'si.</summary>
    public int? TeacherId { get; set; }

    /// <summary>Randevudaki öğrencinin Student.Id'si.</summary>
    public int? StudentId { get; set; }

    /// <summary>Öğrencinin adı — öğretmenin takviminde karşı tarafı göstermek için (auth-api'den, best-effort).</summary>
    public string? StudentName { get; set; }
}

public class StudentCalendarResponseDto
{
    public List<CalendarEventDto> Events { get; set; } = new();
}
