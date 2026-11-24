using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>Issue #511: a child's day-by-day activity for the parent (and the assistant's activity trend tool).</summary>
public interface IParentActivityTrendService
{
    /// <summary>Null when the parent has no Active link to the child (404). A successful read is audited.</summary>
    Task<ParentChildActivityDto?> GetAsync(int parentUserId, int studentId, int days, CancellationToken ct = default);
}

/// <inheritdoc cref="IParentActivityTrendService"/>
public sealed class ParentActivityTrendService(
    AppDbContext context,
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    ILocalDayCalendar? calendar = null) : IParentActivityTrendService
{
    public const int DefaultDays = 14;
    public const int MinDays = 7;
    public const int MaxDays = 28;

    private readonly ILocalDayCalendar _calendar = calendar ?? LocalDayCalendar.Default;

    public async Task<ParentChildActivityDto?> GetAsync(int parentUserId, int studentId, int days, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return null;
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildActivity, ct: ct);

        var to = _calendar.Today;
        var from = to.AddDays(1 - days);
        var previousFrom = from.AddDays(-days);

        // AppDbContext is not thread-safe: one query after the other.
        var answers = await ParentDashboardService.DailyAnswersAsync(context, _calendar.TimeZone, grant.StudentId,
            _calendar.StartOfDayUtc(previousFrom), _calendar.StartOfDayUtc(to.AddDays(1)), ct);
        var points = await context.StudentDailyXps.AsNoTracking()
            .Where(x => x.StudentId == grant.StudentId && x.Day >= previousFrom && x.Day <= to)
            .GroupBy(x => x.Day)
            .Select(g => new { Day = g.Key, Xp = g.Sum(x => x.Xp) })
            .ToDictionaryAsync(x => x.Day, x => x.Xp, ct);

        List<ParentChildActivityDayDto> Range(DateOnly first) => Enumerable.Range(0, days).Select(i =>
        {
            var day = first.AddDays(i);
            var a = answers.GetValueOrDefault(day);
            return new ParentChildActivityDayDto
            {
                Day = day,
                Answered = a?.Solved ?? 0,
                Correct = a?.Correct ?? 0,
                Points = points.GetValueOrDefault(day),
            };
        }).ToList();

        var current = Range(from);
        return new ParentChildActivityDto
        {
            StudentId = grant.StudentId,
            Days = days,
            From = from,
            To = to,
            Daily = current,
            Current = Totals(current),
            Previous = Totals(Range(previousFrom)),
        };
    }

    private static ParentChildActivityTotalsDto Totals(List<ParentChildActivityDayDto> days) => new()
    {
        Answered = days.Sum(d => d.Answered),
        Correct = days.Sum(d => d.Correct),
        ActiveDays = days.Count(d => d.Answered > 0),
        Points = days.Sum(d => d.Points),
    };
}
