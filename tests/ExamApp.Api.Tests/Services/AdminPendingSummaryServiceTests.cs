using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Models.Dtos.OpenText;
using ExamApp.Api.Models.Dtos.QuestionReports;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.StudentSchoolMemberships;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>issue #861: admin "Bekleyenler" özeti her kuyruğun kendi bekleyen tanımını kullanır.</summary>
public class AdminPendingSummaryServiceTests
{
    [Fact]
    public async Task Summary_takes_each_count_from_its_queue_and_totals_them()
    {
        var admin = new StudentSchoolApprover(7, IsAdmin: true, "kc-admin");
        var teacherApprovals = Substitute.For<ITeacherApprovalService>();
        teacherApprovals.ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 1, Arg.Any<CancellationToken>())
            .Returns(new Paged<TeacherApplicationListItemDto> { TotalCount = 2, Items = [] });
        var memberships = Substitute.For<IStudentSchoolMembershipService>();
        memberships.CountPendingAsync(admin, Arg.Any<CancellationToken>()).Returns(1);
        var comments = Substitute.For<IWorksheetCommentService>();
        comments.CountOpenReportsAsync(Arg.Any<CancellationToken>()).Returns(3);
        var questionReports = Substitute.For<IQuestionReportService>();
        questionReports.ListAsync(7, true, QuestionReportStatus.Open, 1, 1, Arg.Any<CancellationToken>())
            .Returns(new QuestionReportPageDto { TotalCount = 4 });
        var openText = Substitute.For<IOpenTextReviewService>();
        openText.GetQueueAsync(
                Arg.Is<SchoolScope>(s => s.IsUnrestricted && s.UserId == 7),
                Arg.Is<OpenTextReviewQueueQuery>(q => q.Status == OpenTextReviewStatuses.Pending && q.PageSize == 1),
                Arg.Any<CancellationToken>())
            .Returns(new Paged<OpenTextReviewItemDto> { TotalCount = 5, Items = [] });

        var summary = await new AdminPendingSummaryService(teacherApprovals, memberships, comments, questionReports, openText)
            .GetAsync(admin);

        summary.TeacherApplications.ShouldBe(2);
        summary.SchoolApplications.ShouldBe(1);
        summary.CommentReports.ShouldBe(3);
        summary.QuestionReports.ShouldBe(4);
        summary.OpenTextAnswers.ShouldBe(5);
        summary.Total.ShouldBe(15);
    }
}
