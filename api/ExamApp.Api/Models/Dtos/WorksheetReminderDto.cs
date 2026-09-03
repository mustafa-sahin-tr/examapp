using System;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetReminderDto
{
    public int WorksheetId { get; set; }

    /// <summary>Sınavın planlandığı an (UTC).</summary>
    public DateTime ScheduledFor { get; set; }

    public int RemindBeforeMinutes { get; set; }

    /// <summary>Pending | Sent | Cancelled</summary>
    public string Status { get; set; } = string.Empty;
}

public class UpsertWorksheetReminderRequestDto
{
    public DateTime ScheduledFor { get; set; }

    public int RemindBeforeMinutes { get; set; } = 60;
}
