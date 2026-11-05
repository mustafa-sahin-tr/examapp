using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.QuestionReports;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #781: yalnız kendi oturumundaki soru bildirilir, tekrar idempotent, liste soru sahibine özel ve anonim.</summary>
public sealed class QuestionReportServiceTests : IDisposable
{
    private const int StudentUserId = 501;
    private const int OtherStudentUserId = 502;
    private const int OwnerUserId = 601;
    private const int OtherTeacherUserId = 602;

    private readonly TestDb _db = TestDb.Create();
    private readonly int _questionId;
    private readonly int _otherQuestionId;
    private readonly int _instanceId;
    private readonly int _otherStudentInstanceId;

    public QuestionReportServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Add(grade);
        ctx.SaveChanges();
        var student = new Student { UserId = StudentUserId, StudentNumber = "S1", SchoolName = "s", GradeId = grade.Id };
        var other = new Student { UserId = OtherStudentUserId, StudentNumber = "S2", SchoolName = "s", GradeId = grade.Id };
        ctx.AddRange(student, other);
        ctx.SaveChanges();
        var question = new Question { Text = "Soru", Point = 5 };
        ctx.SetCurrentUser(OwnerUserId); // CreateUserId = soru sahibi
        ctx.Add(question);
        ctx.SaveChanges();
        var otherQuestion = new Question { Text = "Başka soru", Point = 5 };
        ctx.SetCurrentUser(OtherTeacherUserId);
        ctx.Add(otherQuestion);
        ctx.SaveChanges();
        var worksheet = new Worksheet { Name = "Test", Description = "d", GradeId = grade.Id };
        ctx.Add(worksheet);
        ctx.SaveChanges();
        var item = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.Add(item);
        ctx.SaveChanges();
        var instance = new WorksheetInstance { StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = DateTime.UtcNow, Status = WorksheetInstanceStatus.Started };
        var otherInstance = new WorksheetInstance { StudentId = other.Id, WorksheetId = worksheet.Id, StartTime = DateTime.UtcNow, Status = WorksheetInstanceStatus.Started };
        ctx.TestInstances.AddRange(instance, otherInstance);
        ctx.SaveChanges();
        ctx.TestInstanceQuestions.AddRange(
            new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = item.Id },
            new WorksheetInstanceQuestion { WorksheetInstanceId = otherInstance.Id, WorksheetQuestionId = item.Id });
        ctx.SaveChanges();
        (_questionId, _otherQuestionId, _instanceId, _otherStudentInstanceId) = (question.Id, otherQuestion.Id, instance.Id, otherInstance.Id);
    }

    public void Dispose() => _db.Dispose();

    private CreateQuestionReportDto Dto(string reason = "wrongAnswer", string? note = null, int? instanceId = null) =>
        new() { TestInstanceId = instanceId ?? _instanceId, Reason = reason, Note = note };

    private async Task<QuestionReportCreatedDto?> CreateAsync(CreateQuestionReportDto dto, int? questionId = null, int userId = StudentUserId)
    {
        await using var ctx = _db.NewContext();
        return await new QuestionReportService(ctx).CreateAsync(questionId ?? _questionId, dto, userId);
    }

    private async Task<QuestionReportPageDto> ListAsync(int userId, bool isAdmin = false, QuestionReportStatus status = QuestionReportStatus.Open)
    {
        await using var ctx = _db.NewContext();
        return await new QuestionReportService(ctx).ListAsync(userId, isAdmin, status, 1, 20);
    }

    [Fact]
    public async Task Student_reports_question_in_own_session_and_the_note_is_trimmed()
    {
        var result = await CreateAsync(Dto(reason: "unreadable", note: "  görsel bulanık  "));

        result.ShouldNotBeNull();
        result.AlreadyReported.ShouldBeFalse();
        await using var ctx = _db.NewContext();
        var saved = await ctx.QuestionReports.SingleAsync();
        saved.ShouldSatisfyAllConditions(
            () => saved.QuestionId.ShouldBe(_questionId),
            () => saved.ReporterUserId.ShouldBe(StudentUserId),
            () => saved.TestInstanceId.ShouldBe(_instanceId),
            () => saved.Reason.ShouldBe(QuestionReportReason.Unreadable),
            () => saved.Note.ShouldBe("görsel bulanık"),
            () => saved.Status.ShouldBe(QuestionReportStatus.Open));
    }

    [Fact]
    public async Task Another_students_session_looks_like_it_does_not_exist()
    {
        (await CreateAsync(Dto(instanceId: _otherStudentInstanceId))).ShouldBeNull();

        await using var ctx = _db.NewContext();
        (await ctx.QuestionReports.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Question_outside_the_session_looks_like_it_does_not_exist()
    {
        (await CreateAsync(Dto(), questionId: _otherQuestionId)).ShouldBeNull();
    }

    [Fact]
    public async Task Reporting_again_keeps_the_first_report()
    {
        await CreateAsync(Dto(reason: "wrongAnswer", note: "ilk"));

        var second = await CreateAsync(Dto(reason: "other", note: "ikinci"));

        second!.AlreadyReported.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        var saved = await ctx.QuestionReports.SingleAsync();
        saved.Reason.ShouldBe(QuestionReportReason.WrongAnswer);
        saved.Note.ShouldBe("ilk");
    }

    [Fact]
    public async Task Two_students_can_report_the_same_question()
    {
        await CreateAsync(Dto());
        var other = await CreateAsync(Dto(instanceId: _otherStudentInstanceId), userId: OtherStudentUserId);

        other!.AlreadyReported.ShouldBeFalse();
        (await ListAsync(OwnerUserId)).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Owner_and_admin_see_the_report_but_another_teacher_does_not()
    {
        await CreateAsync(Dto(reason: "wrongContent", note: "not"));

        var item = (await ListAsync(OwnerUserId)).Items.ShouldHaveSingleItem();
        item.Reason.ShouldBe("wrongContent");
        item.Note.ShouldBe("not");
        item.Status.ShouldBe("open");
        (await ListAsync(OtherTeacherUserId, isAdmin: true)).TotalCount.ShouldBe(1);
        (await ListAsync(OtherTeacherUserId)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public void List_item_does_not_carry_the_reporter()
    {
        typeof(QuestionReportItemDto).GetProperties().Select(p => p.Name)
            .ShouldNotContain(name => name.Contains("Reporter") || name.Contains("Student") || name.Contains("Instance"));
    }

    [Fact]
    public async Task Only_owner_or_admin_marks_reviewed_and_it_leaves_the_open_list()
    {
        await CreateAsync(Dto());
        await using (var ctx = _db.NewContext())
        {
            var reportId = (await ctx.QuestionReports.SingleAsync()).Id;
            var service = new QuestionReportService(ctx);
            (await service.MarkReviewedAsync(reportId, OtherTeacherUserId, isAdmin: false)).ShouldBeFalse();
            (await service.MarkReviewedAsync(reportId, OwnerUserId, isAdmin: false)).ShouldBeTrue();
        }

        (await ListAsync(OwnerUserId)).TotalCount.ShouldBe(0);
        (await ListAsync(OwnerUserId, status: QuestionReportStatus.Reviewed)).Items.ShouldHaveSingleItem().Status.ShouldBe("reviewed");
    }

    [Fact]
    public void Report_endpoint_is_rate_limited()
    {
        var attribute = typeof(QuestionReportsController).GetMethod(nameof(QuestionReportsController.Create))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        attribute.ShouldNotBeNull();
        attribute.PolicyName.ShouldBe(WorksheetCommentWriteRateLimiting.Policy);
    }
}
