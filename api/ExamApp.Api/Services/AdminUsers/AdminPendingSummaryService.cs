using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Models.Dtos.OpenText;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.StudentSchoolMemberships;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;

namespace ExamApp.Api.Services.AdminUsers;

public interface IAdminPendingSummaryService
{
    Task<AdminPendingSummaryDto> GetAsync(StudentSchoolApprover admin, CancellationToken ct = default);
}

/// <summary>
/// issue #861: beş onay / moderasyon kuyruğunun bekleyen sayıları. Her sayı kuyruğun kendi servisinden gelir (liste ucuyla
/// aynı "bekleyen" tanımı); burada yeni kural yok. Sayfalı listelerde toplam, tek satırlık sayfanın TotalCount'u.
/// </summary>
public sealed class AdminPendingSummaryService : IAdminPendingSummaryService
{
    private readonly ITeacherApprovalService _teacherApprovals;
    private readonly IStudentSchoolMembershipService _memberships;
    private readonly IWorksheetCommentService _comments;
    private readonly IQuestionReportService _questionReports;
    private readonly IOpenTextReviewService _openText;

    public AdminPendingSummaryService(
        ITeacherApprovalService teacherApprovals,
        IStudentSchoolMembershipService memberships,
        IWorksheetCommentService comments,
        IQuestionReportService questionReports,
        IOpenTextReviewService openText)
    {
        _teacherApprovals = teacherApprovals;
        _memberships = memberships;
        _comments = comments;
        _questionReports = questionReports;
        _openText = openText;
    }

    public async Task<AdminPendingSummaryDto> GetAsync(StudentSchoolApprover admin, CancellationToken ct = default)
    {
        // Aynı DbContext üzerinde sırayla (EF eşzamanlı sorguya izin vermez).
        var teacherApplications = await _teacherApprovals.ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 1, ct);
        var schoolApplications = await _memberships.CountPendingAsync(admin, ct);
        var commentReports = await _comments.CountOpenReportsAsync(ct);
        var questionReports = await _questionReports.ListAsync(admin.UserId, isAdmin: true, QuestionReportStatus.Open, 1, 1, ct);
        var openText = await _openText.GetQueueAsync(SchoolScope.Unrestricted(admin.UserId),
            new OpenTextReviewQueueQuery { Status = OpenTextReviewStatuses.Pending, Page = 1, PageSize = 1 }, ct);

        return new AdminPendingSummaryDto
        {
            TeacherApplications = teacherApplications.TotalCount,
            SchoolApplications = schoolApplications,
            CommentReports = commentReports,
            QuestionReports = questionReports.TotalCount,
            OpenTextAnswers = openText.TotalCount,
        };
    }
}
