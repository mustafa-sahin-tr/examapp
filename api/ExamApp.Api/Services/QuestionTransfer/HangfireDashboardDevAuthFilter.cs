using Hangfire.Dashboard;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>
/// Development-only Hangfire dashboard access. Issue #365: same Admin/SuperAdmin rule as production
/// (<see cref="HangfireDashboardAuthFilter"/>), only synchronous; the dashboard shows every job's arguments.
/// </summary>
public class HangfireDashboardDevAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        HangfireDashboardAuthFilter.IsAllowed(context.GetHttpContext().User);
}
