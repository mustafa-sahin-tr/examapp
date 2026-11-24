using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.ParentDashboard;

/// <summary>
/// Issue #511: <c>GET api/parent/children/{studentId}/activity?days=</c>. The last <see cref="Days"/> local days (today
/// included) day by day and their totals beside the <see cref="Days"/> days before ("geçen haftaya göre"). Counts only, with
/// the panel summary's answer rule (#420 / #883) and points from <c>StudentDailyXps</c>. No question, answer or time of day.
/// </summary>
public sealed class ParentChildActivityDto
{
    public int StudentId { get; set; }

    public int Days { get; set; }

    /// <summary>First local day of the window ("yyyy-MM-dd").</summary>
    public DateOnly From { get; set; }

    /// <summary>Last local day of the window (today, inclusive).</summary>
    public DateOnly To { get; set; }

    /// <summary>One row per day of the window, oldest first (days without activity are zeros).</summary>
    public List<ParentChildActivityDayDto> Daily { get; set; } = [];

    public ParentChildActivityTotalsDto Current { get; set; } = new();

    /// <summary>The <see cref="Days"/> days before <see cref="From"/>.</summary>
    public ParentChildActivityTotalsDto Previous { get; set; } = new();
}

public sealed class ParentChildActivityDayDto
{
    public DateOnly Day { get; set; }

    public int Answered { get; set; }

    public int Correct { get; set; }

    /// <summary>Net points earned that day.</summary>
    public int Points { get; set; }
}

public sealed class ParentChildActivityTotalsDto
{
    public int Answered { get; set; }

    public int Correct { get; set; }

    /// <summary>Days with at least one answer.</summary>
    public int ActiveDays { get; set; }

    public int Points { get; set; }
}
