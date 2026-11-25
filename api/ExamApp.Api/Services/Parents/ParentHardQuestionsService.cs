using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Practice;

namespace ExamApp.Api.Services.Parents;

/// <summary>Issue #536: the questions a linked child struggled with most (#526 <c>mine</c>), for the parent.</summary>
public interface IParentHardQuestionsService
{
    /// <returns>Null when the parent has no Active link to the child (404).</returns>
    Task<HardQuestionsDto?> GetAsync(int parentUserId, int studentId, HardQuestionFilter filter, int take, CancellationToken ct = default);
}

/// <summary>
/// Issue #536: the #498 order — gate, access record, data. The child comes from the grant (verified school only, #361), the query is
/// #526 <c>mine</c> unchanged: finished tests and practice, a running or open assigned test never shows, no correct or chosen choice.
/// The last attempt is cut to the hour like the other parent endpoints (no minute-level trace of the child's activity).
/// </summary>
public sealed class ParentHardQuestionsService(
    AppDbContext context,
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    ILocalDayCalendar? calendar = null) : IParentHardQuestionsService
{
    public const int DefaultTake = 5;
    public const int MaxTake = 10;

    private readonly ILocalDayCalendar _calendar = calendar ?? LocalDayCalendar.Default;

    public async Task<HardQuestionsDto?> GetAsync(int parentUserId, int studentId, HardQuestionFilter filter, int take, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildHardQuestions, ct: ct);

        var child = new StudentProfileDto { Id = grant.StudentId, GradeId = grant.GradeId, SchoolId = grant.VerifiedSchoolId };
        var items = await HardQuestionQueries.LoadMineAsync(context, _calendar, child, filter, ct);
        return new HardQuestionsDto("mine", filter.Days, items.Count, items
            .Take(Math.Clamp(take, 1, MaxTake))
            .Select(q => q with { LastAttemptAt = q.LastAttemptAt is { } at ? new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0, DateTimeKind.Utc) : null })
            .ToList());
    }
}
