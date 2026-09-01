using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

public class MoreEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // ---- BooksController ----

    [Fact]
    public async Task Books_endpoints_list_books_and_their_tests()
    {
        var bookId = await WithDbAsync(async db =>
        {
            var book = new Book { Name = "Fen 6", BookTests = { new BookTest { Name = "Ünite 1" } } };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            return book.Id;
        });

        var client = await ClientAsAsync(1, "Teacher", "kc-t", "Teacher");

        var books = await client.GetFromJsonAsync<List<Book>>("/api/books", Json);
        books!.ShouldContain(b => b.Name == "Fen 6");

        var tests = await client.GetFromJsonAsync<List<BookTest>>($"/api/books/{bookId}/tests", Json);
        tests!.ShouldHaveSingleItem().Name.ShouldBe("Ünite 1");
    }

    // ---- TeacherController ----

    [Fact]
    public async Task Teacher_endpoints_reject_anonymous_callers()
    {
        (await Anonymous().PostAsJsonAsync("/api/teacher/register", new RegisterTeacherDto { SchoolName = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Anonymous().GetAsync("/api/teacher/check-teacher"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- ProgramController ----

    [Fact]
    public async Task Program_steps_and_my_programs_flow()
    {
        await WithDbAsync(async db =>
        {
            db.ProgramSteps.Add(new ProgramStep { Title = "Adım 1", Description = "d", Order = 1 });
            await db.SaveChangesAsync();
        });

        var client = await ClientAsAsync(60, "Student", "kc-60", "Student");

        var steps = await client.GetFromJsonAsync<List<ProgramStepDto>>("/api/program/steps", Json);
        steps!.ShouldContain(s => s.Title == "Adım 1");

        var create = await client.PostAsJsonAsync("/api/program/create", new CreateProgramRequestDto
        {
            ProgramName = "Plan", Description = "d", UserSelections = new(),
        });
        create.StatusCode.ShouldBe(HttpStatusCode.OK);

        var mine = await client.GetFromJsonAsync<List<UserProgramDto>>("/api/program/my-programs", Json);
        mine!.ShouldContain(p => p.ProgramName == "Plan");
    }

    [Fact]
    public async Task Program_steps_are_public_but_the_rest_needs_auth()
    {
        (await Anonymous().GetAsync("/api/program/steps")).StatusCode.ShouldBe(HttpStatusCode.OK); // [AllowAnonymous]
        (await Anonymous().GetAsync("/api/program/my-programs")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- SubjectController ----

    [Fact]
    public async Task Subject_endpoints_expose_the_taxonomy_tree()
    {
        var (subjectId, topicId, gradeId) = await WithDbAsync(async db =>
        {
            var subject = new Subject { Name = "Coğrafya" };
            var grade = new Grade { Name = "7" };
            db.AddRange(subject, grade);
            await db.SaveChangesAsync();
            db.GradeSubjects.Add(new GradeSubject { GradeId = grade.Id, SubjectId = subject.Id });
            var topic = new Topic { Name = "İklim", SubjectId = subject.Id, GradeId = grade.Id };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();
            db.SubTopics.Add(new SubTopic { Name = "Yağış", TopicId = topic.Id });
            await db.SaveChangesAsync();
            return (subject.Id, topic.Id, grade.Id);
        });

        var client = await ClientAsAsync(1, "Teacher", "kc-t", "Teacher");

        (await client.GetFromJsonAsync<List<Subject>>("/api/subject", Json))!
            .ShouldContain(s => s.Name == "Coğrafya");
        (await client.GetFromJsonAsync<List<Topic>>($"/api/subject/topics/{subjectId}", Json))!
            .ShouldContain(t => t.Id == topicId);
        (await client.GetFromJsonAsync<List<SubTopic>>($"/api/subject/subtopics/{topicId}", Json))!
            .ShouldContain(st => st.Name == "Yağış");
        (await client.GetFromJsonAsync<List<Subject>>($"/api/subject/by-grade/{gradeId}", Json))!
            .ShouldContain(s => s.Id == subjectId);
    }

    // ---- StudyPagesController ----

    [Fact]
    public async Task Study_pages_list_is_role_gated_and_students_see_only_published()
    {
        await WithDbAsync(async db =>
        {
            db.StudyPages.Add(new StudyPage { Title = "Yayında", Description = "d", IsPublished = true, CreatedByUserId = 1 });
            db.StudyPages.Add(new StudyPage { Title = "Taslak", Description = "d", IsPublished = false, CreatedByUserId = 1 });
            await db.SaveChangesAsync();
        });

        (await Anonymous().GetAsync("/api/study-pages")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var parent = await ClientAsAsync(2, "Parent", "kc-p", "Parent");
        (await parent.GetAsync("/api/study-pages")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(3, "Student", "kc-s", "Student");
        var page = await student.GetFromJsonAsync<Paged<StudyPageDto>>("/api/study-pages?pageNumber=1&pageSize=10", Json);
        page!.Items.Select(i => i.Title).ShouldBe(new[] { "Yayında" });
    }
}
