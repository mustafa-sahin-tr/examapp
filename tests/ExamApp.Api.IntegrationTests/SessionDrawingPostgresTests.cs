using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #1310 — the student's drawings per question (ink on the question image + scratch pad) are stored on the server
/// and shown read-only, question by question, once the session is finished.
/// </summary>
public class SessionDrawingPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private sealed record Seed(int UserId, int InstanceId, int Tiq1, int Tiq2);

    private Task<Seed> SeedAsync(WorksheetInstanceStatus status) => WithDbAsync(async db =>
    {
        var userId = NewUserId();
        var grade = new Grade { Name = "5" };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var q1 = new Question { Text = "1+1?", Point = 10, DifficultyLevel = 1 };
        var q2 = new Question { Text = "2+2?", Point = 10, DifficultyLevel = 1 };
        var worksheet = new Worksheet { Name = "Karalama", Description = "", GradeId = grade.Id };
        var student = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "Okul", GradeId = grade.Id };
        db.AddRange(q1, q2, worksheet, student);
        await db.SaveChangesAsync();

        var a = new Answer { QuestionId = q1.Id, Text = "2", Tag = "A" };
        var b = new Answer { QuestionId = q1.Id, Text = "3", Tag = "B" };
        db.Answers.AddRange(a, b);
        await db.SaveChangesAsync();
        q1.CorrectAnswerId = a.Id;

        var wq1 = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q1.Id, Order = 1 };
        var wq2 = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q2.Id, Order = 2 };
        db.TestQuestions.AddRange(wq1, wq2);
        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = DateTime.UtcNow.AddMinutes(-20),
            EndTime = status == WorksheetInstanceStatus.Completed ? DateTime.UtcNow.AddMinutes(-1) : null,
            Status = status
        };
        db.TestInstances.Add(instance);
        await db.SaveChangesAsync();

        var tiq1 = new WorksheetInstanceQuestion
        {
            WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq1.Id, Order = 1, SelectedAnswerId = b.Id, TimeTaken = 40
        };
        var tiq2 = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq2.Id, Order = 2 };
        db.TestInstanceQuestions.AddRange(tiq1, tiq2);
        await db.SaveChangesAsync();
        return new Seed(userId, instance.Id, tiq1.Id, tiq2.Id);
    });

    private async Task<T> WithServiceAsync<T>(Func<ISessionDrawingService, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISessionDrawingService>());
    }

    private static SaveSessionDrawingsDto Dto(int tiq, params string[] ink) => new()
    {
        Items = [new SessionDrawingItemDto { InstanceQuestionId = tiq, Ink = [.. ink], Strokes = ["red:M1 2 L3 4"], Text = "kara gün?" }]
    };

    [Fact]
    public async Task Save_WhileSolving_ThenReviewAfterFinish_ShowsDrawingsAndOutcome()
    {
        var seed = await SeedAsync(WorksheetInstanceStatus.Started);

        var saved = await WithServiceAsync(s => s.SaveAsync(seed.InstanceId, seed.UserId, Dto(seed.Tiq1, "M10 10 L20 20", "<script>")));
        Assert.Equal(SaveSessionDrawingsResult.Saved, saved);

        // Öğrenci çözerken görünmez.
        Assert.Null(await WithServiceAsync(s => s.BuildReviewAsync(seed.InstanceId)));

        await WithDbAsync(async db =>
        {
            await db.TestInstances.Where(t => t.Id == seed.InstanceId).ExecuteUpdateAsync(u => u
                .SetProperty(t => t.Status, WorksheetInstanceStatus.Completed)
                .SetProperty(t => t.EndTime, DateTime.UtcNow));
            return 0;
        });

        var review = await WithServiceAsync(s => s.BuildReviewAsync(seed.InstanceId));
        Assert.NotNull(review);
        Assert.Equal(2, review!.Questions.Count);
        var first = review.Questions[0];
        Assert.Equal(1, first.Number);
        Assert.Equal("wrong", first.Outcome);
        Assert.Equal("B", first.SelectedLetter);
        Assert.Equal("A", first.CorrectLetter);
        Assert.Equal(["M10 10 L20 20"], first.Ink); // biçime uymayan çizgi atıldı
        Assert.Equal(["red:M1 2 L3 4"], first.Strokes);
        Assert.Equal("kara gün?", first.Text);
        Assert.Equal("blank", review.Questions[1].Outcome);
        Assert.Empty(review.Questions[1].Ink);
    }

    [Fact]
    public async Task Save_OtherStudentsSession_IsNotFound_AndEmptyItemRemovesTheRow()
    {
        var seed = await SeedAsync(WorksheetInstanceStatus.Started);
        Assert.Equal(SaveSessionDrawingsResult.NotFound,
            await WithServiceAsync(s => s.SaveAsync(seed.InstanceId, seed.UserId + 1, Dto(seed.Tiq1, "M1 1 L2 2"))));

        await WithServiceAsync(s => s.SaveAsync(seed.InstanceId, seed.UserId, Dto(seed.Tiq1, "M1 1 L2 2")));
        await WithServiceAsync(s => s.SaveAsync(seed.InstanceId, seed.UserId, new SaveSessionDrawingsDto
        {
            Items = [new SessionDrawingItemDto { InstanceQuestionId = seed.Tiq1 }]
        }));
        var count = await WithDbAsync(db => db.TestInstanceQuestionDrawings.CountAsync(d => d.WorksheetInstanceQuestionId == seed.Tiq1));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Save_LongAfterFinish_IsClosed()
    {
        var seed = await SeedAsync(WorksheetInstanceStatus.Completed);
        await WithDbAsync(async db =>
        {
            await db.TestInstances.Where(t => t.Id == seed.InstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.EndTime, DateTime.UtcNow.AddHours(-1)));
            return 0;
        });
        Assert.Equal(SaveSessionDrawingsResult.Closed,
            await WithServiceAsync(s => s.SaveAsync(seed.InstanceId, seed.UserId, Dto(seed.Tiq1, "M1 1 L2 2"))));
    }

    [Fact]
    public async Task TeacherReview_OnlyTheAssigningTeacher_OnlyFinishedSessions()
    {
        var studentUser = NewUserId();
        var assigner = NewUserId();
        var other = NewUserId();
        var ids = await WithDbAsync(async db =>
        {
            var school = new School { Name = $"Karalama Okul {studentUser}" };
            var grade = new Grade { Name = "5" };
            db.AddRange(school, grade);
            await db.SaveChangesAsync();
            var now = DateTime.UtcNow;
            db.Teachers.AddRange(
                new Teacher { UserId = assigner, SchoolId = school.Id, AccountApprovedAt = now.AddDays(-5) },
                new Teacher { UserId = other, SchoolId = school.Id, AccountApprovedAt = now.AddDays(-5) });
            var student = new Student
            {
                UserId = studentUser, StudentNumber = $"S{studentUser}", SchoolId = school.Id, SchoolVerifiedAt = now, GradeId = grade.Id
            };
            var question = new Question { Text = "1+1?", Point = 10, DifficultyLevel = 1 };
            var ws = new Worksheet { Name = "Karalama", Description = "", GradeId = grade.Id };
            db.AddRange(student, question, ws);
            await db.SaveChangesAsync();
            db.SetCurrentUser(assigner);
            db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws.Id, StudentId = student.Id, StartAt = now.AddDays(-1) });
            var wq = new WorksheetQuestion { TestId = ws.Id, QuestionId = question.Id, Order = 1 };
            db.TestQuestions.Add(wq);
            var instance = new WorksheetInstance
            {
                StudentId = student.Id, WorksheetId = ws.Id, StartTime = now.AddMinutes(-10), Status = WorksheetInstanceStatus.Started
            };
            db.TestInstances.Add(instance);
            await db.SaveChangesAsync();
            var tiq = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id, Order = 1 };
            db.TestInstanceQuestions.Add(tiq);
            await db.SaveChangesAsync();
            db.TestInstanceQuestionDrawings.Add(new WorksheetInstanceQuestionDrawing
            {
                WorksheetInstanceQuestionId = tiq.Id, Ink = "[\"M1 1 L2 2\"]", ScratchStrokes = "[]", UpdatedAt = now
            });
            await db.SaveChangesAsync();
            return (SchoolId: school.Id, WorksheetId: ws.Id, InstanceId: instance.Id);
        });
        var url = $"/api/worksheet/{ids.WorksheetId}/assignments/sessions/{ids.InstanceId}/review";
        var teacher = await ClientAsWithSchoolAsync(assigner, "Teacher", $"kc-{assigner}", ids.SchoolId, "Teacher");
        var stranger = await ClientAsWithSchoolAsync(other, "Teacher", $"kc-{other}", ids.SchoolId, "Teacher");

        // Öğrenci çözerken öğretmen izlemez.
        (await teacher.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await WithDbAsync(db => db.TestInstances.Where(t => t.Id == ids.InstanceId).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.Status, WorksheetInstanceStatus.Completed)
            .SetProperty(t => t.EndTime, DateTime.UtcNow)));

        var response = await teacher.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var review = await response.Content.ReadFromJsonAsync<SessionReviewDto>(Json);
        review!.Questions.Single().Ink.ShouldBe(["M1 1 L2 2"]);

        // Aynı okuldan, atamayı yapmamış öğretmen göremez.
        (await stranger.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
