using System.Net;
using AssistantService.Chat;
using AssistantService.Tools;
using Shouldly;
using static AssistantService.Tests.ToolHarness;

namespace AssistantService.Tests;

/// <summary>Issue #515: programs, study pages, tests and reminders: previews read only, commits send what the card showed.</summary>
public sealed class PlanningToolTests : IDisposable
{
    private readonly ToolHarness _h = new(); // clock: 2026-11-23 10:00 UTC = 13:00 Istanbul (Monday)
    public void Dispose() => _h.Dispose();

    private IAssistantTool Tool(string name) => _h.AllTools().Single(t => t.Name == name);

    [Fact]
    public async Task Programs_list_marks_names_untrusted_and_get_program_keeps_the_next_two_weeks()
    {
        _h.Api.On("GET", "api/program/my-programs", new[] { new { id = 4, programName = "Yaz <b>planı</b>", isActive = true, progressPercentage = 25, userId = "secret" } });
        _h.Api.On("GET", "api/program/4", new
        {
            id = 4, programName = "Yaz planı", isActive = true,
            schedules = new[]
            {
                new { scheduleDate = "2026-11-20T00:00:00", subjectName = "Matematik", isCompleted = true },
                new { scheduleDate = "2026-11-24T00:00:00", subjectName = "Türkçe", isCompleted = false },
                new { scheduleDate = "2026-12-10T00:00:00", subjectName = "Fen", isCompleted = false },
            },
            studyItemSchedules = new[] { new { id = 70, studyItemTitle = "Kesirler özeti", startDate = "2026-11-24", endDate = "2026-11-24", isCompleted = false } },
        });

        var list = await Tool("list_my_programs").ExecuteAsync(Student(), Args());
        var row = list.Data!["programs"]!.AsArray().Single()!;
        row["name"]!.GetValue<string>().ShouldBe("<untrusted>Yaz ‹b›planı‹/b›</untrusted>");
        row["userId"].ShouldBeNull();

        var program = await Tool("get_program").ExecuteAsync(Student(), Args(new { programId = 4 }));
        program.Data!["sessions"]!.AsArray().Select(s => s!["subjectName"]!.GetValue<string>()).ShouldBe(["Türkçe"]);
        program.Data!["studyPages"]![0]!["scheduleId"]!.GetValue<int>().ShouldBe(70);
    }

    [Fact]
    public async Task Create_program_previews_choices_and_commits_the_wizard_selections()
    {
        _h.Api.On("GET", "api/program/subjects", new[] { new { id = 3, name = "Matematik" }, new { id = 5, name = "Türkçe" } });
        _h.Api.On("GET", "api/program/my-programs", new[] { new { id = 4, isActive = true } });
        _h.Api.On("POST", "api/program/create", new { id = 9, schedules = new[] { new { id = 1 }, new { id = 2 } } });
        var tool = (IAssistantWriteTool)Tool("create_study_program");
        var args = Args(new { name = "Kis plani", studyType = "question", questionsPerDay = 12, subjectsPerDay = 2, restDays = new[] { 7 }, difficultSubjectIds = new[] { 3 } });

        (await tool.ExecuteAsync(Student(), Args(new { name = "x", studyType = "question", questionsPerDay = 10, subjectsPerDay = 2 }))).ErrorCode.ShouldBe("InvalidArguments");
        (await tool.ExecuteAsync(Student(), Args(new { name = "x", studyType = "time", subjectsPerDay = 1, difficultSubjectIds = new[] { 99 } }))).ErrorCode.ShouldBe("InvalidArguments");
        var preview = await tool.ExecuteAsync(Student(), args);
        preview.Data!["startDate"]!.GetValue<string>().ShouldBe("2026-11-23");
        preview.Data!["endDate"]!.GetValue<string>().ShouldBe("2026-12-23");
        preview.Data!["replacesActiveProgram"]!.GetValue<bool>().ShouldBeTrue();
        preview.Data!["difficultSubjects"]![0]!.GetValue<string>().ShouldBe("Matematik");
        _h.Api.Calls.ShouldAllBe(c => c.Method == "GET");

        var commit = await tool.CommitAsync(Student(), args);

        var body = _h.Api.Calls.Single(c => c.Method == "POST").Body!;
        body.ShouldContain("\"programName\":\"Kis plani\"");
        body.ShouldContain("{\"stepId\":1,\"selectedValues\":[\"question\"]}");
        body.ShouldContain("{\"stepId\":3,\"selectedValues\":[\"12\"]}");
        body.ShouldContain("{\"stepId\":5,\"selectedValues\":[\"2\"]}");
        body.ShouldContain("{\"stepId\":6,\"selectedValues\":[\"7\"]}");
        body.ShouldContain("\"difficultSubjectIds\":[3]");
        commit.UiActions.ShouldNotBeNull().Single()["route"]!.GetValue<string>().ShouldBe("/programs/9/detail");
    }

    [Fact]
    public async Task Study_pages_are_scheduled_at_istanbul_midnight_and_marked_done()
    {
        _h.Api.On("GET", "api/program/4", new { id = 4, programName = "Yaz planı", studyItemSchedules = new[] { new { id = 70, studyItemTitle = "Özet", isCompleted = false } } });
        _h.Api.On("POST", "api/program/4/study-pages", new { id = 4 });
        _h.Api.On("PUT", "api/program/4/study-pages/70/complete", null, HttpStatusCode.NoContent);
        var schedule = (IAssistantWriteTool)Tool("schedule_study_pages");
        var args = Args(new { programId = 4, items = new[] { new { studyItemId = 11, startDate = "2026-11-24", endDate = "2026-11-25" } } });

        (await schedule.ExecuteAsync(Student(), Args(new { programId = 4, items = new[] { new { studyItemId = 11, startDate = "2026-11-22" } } }))).ErrorCode.ShouldBe("InvalidArguments");
        (await schedule.ExecuteAsync(Student(), args)).Data!["items"]!.AsArray().Count.ShouldBe(1);
        await schedule.CommitAsync(Student(), args);
        _h.Api.Calls.Single(c => c.Method == "POST").Body
            .ShouldBe("""{"items":[{"studyItemId":11,"startDate":"2026-11-23T21:00:00Z","endDate":"2026-11-24T21:00:00Z"}]}""");

        var mark = (IAssistantWriteTool)Tool("mark_study_page_done");
        (await mark.ExecuteAsync(Student(), Args(new { programId = 4, scheduleId = 71 }))).ErrorCode.ShouldBe("NotFound");
        (await mark.ExecuteAsync(Student(), Args(new { programId = 4, scheduleId = 70 }))).Data!["done"]!.GetValue<bool>().ShouldBeTrue();
        (await mark.CommitAsync(Student(), Args(new { programId = 4, scheduleId = 70 }))).Ok.ShouldBeTrue();
        _h.Api.Calls.Last().Method.ShouldBe("PUT");
    }

    [Fact]
    public async Task Worksheets_search_filters_tests_and_start_test_opens_the_test_screen_after_confirm()
    {
        _h.Api.On("GET", "api/worksheet/list?pageNumber=1&pageSize=10&kind=0&search=kesir&statuses=-1",
            new { totalCount = 1, items = new[] { new { id = 301, name = "Kesirler", questionCount = 15, maxDurationSeconds = 1200, imageUrl = "x" } } });
        _h.Api.On("GET", "api/worksheet/301", new { id = 301, name = "Kesirler", kind = 0, questionCount = 15, maxDurationSeconds = 1200 });
        _h.Api.On("GET", "api/worksheet/302", new { id = 302, name = "Labirent", kind = 1 });
        _h.Api.On("POST", "api/worksheet/start-test/301", new { instanceId = 555 });

        var found = await Tool("search_worksheets").ExecuteAsync(Student(), Args(new { search = "kesir", status = "notStarted" }));
        var test = found.Data!["tests"]![0]!;
        test["worksheetId"]!.GetValue<int>().ShouldBe(301);
        test["durationMinutes"]!.GetValue<int>().ShouldBe(20);
        test["imageUrl"].ShouldBeNull();

        var start = (IAssistantWriteTool)Tool("start_test");
        (await start.ExecuteAsync(Student(), Args(new { worksheetId = 302 }))).ErrorCode.ShouldBe("NotATest");
        (await start.ExecuteAsync(Student(), Args(new { worksheetId = 301 }))).Data!["questionCount"]!.GetValue<int>().ShouldBe(15);
        _h.Api.Calls.ShouldAllBe(c => c.Method == "GET");
        var commit = await start.CommitAsync(Student(), Args(new { worksheetId = 301 }));
        commit.UiActions.ShouldNotBeNull().Single()["route"]!.GetValue<string>().ShouldBe("/testsolve/555");
        UiScreens.Route("testSolve", 555, ["Student"]).ShouldBeNull(); // never a screen the model can open itself
    }

    [Fact]
    public async Task Reminder_is_local_time_in_utc_and_rejects_the_past()
    {
        _h.Api.On("GET", "api/worksheet/list?id=301&pageNumber=1&pageSize=1", new { items = new[] { new { id = 301, name = "Kesirler", isAssigned = true } } });
        _h.Api.On("GET", "api/worksheet/list?id=302&pageNumber=1&pageSize=1", new { items = new[] { new { id = 302, name = "Serbest", isAssigned = false } } });
        _h.Api.On("GET", "api/worksheet/301", new { id = 301, name = "Kesirler" });
        _h.Api.On("GET", "api/worksheet/301/reminder", new { status = "Pending", scheduledFor = "2026-11-25T16:00:00Z" });
        _h.Api.On("PUT", "api/worksheet/301/reminder", new { status = "Pending" });
        _h.Api.On("DELETE", "api/worksheet/301/reminder", null, HttpStatusCode.NoContent);
        var set = (IAssistantWriteTool)Tool("set_worksheet_reminder");
        var args = Args(new { worksheetId = 301, date = "2026-11-27", time = "19:00", remindBeforeMinutes = 30 });

        (await set.ExecuteAsync(Student(), Args(new { worksheetId = 301, date = "2026-11-23", time = "12:59" }))).ErrorCode.ShouldBe("InvalidArguments");
        (await set.ExecuteAsync(Student(), Args(new { worksheetId = 302, date = "2026-11-27", time = "19:00" }))).ErrorCode.ShouldBe("NotAssigned");
        (await set.ExecuteAsync(Student(), args)).Data!["replacesReminder"]!.GetValue<bool>().ShouldBeTrue();
        await set.CommitAsync(Student(), args);
        _h.Api.Calls.Single(c => c.Method == "PUT").Body.ShouldBe("""{"scheduledFor":"2026-11-27T16:00:00Z","remindBeforeMinutes":30}""");

        var delete = (IAssistantWriteTool)Tool("delete_worksheet_reminder");
        (await delete.ExecuteAsync(Student(), Args(new { worksheetId = 301 }))).Data!["action"]!.GetValue<string>().ShouldBe("deleteWorksheetReminder");
        (await delete.CommitAsync(Student(), Args(new { worksheetId = 301 }))).Ok.ShouldBeTrue();
    }

    [Fact]
    public void Planning_tools_are_student_only_and_in_the_prompt()
    {
        var registry = new ToolRegistry(_h.AllTools());
        string[] names = ["list_my_programs", "get_program", "create_study_program", "search_study_pages", "schedule_study_pages",
            "mark_study_page_done", "search_worksheets", "start_test", "set_worksheet_reminder", "delete_worksheet_reminder"];
        registry.ForRoles(["Parent"]).Select(t => t.Name).ShouldNotContain("start_test");
        names.Except(registry.ForRoles(["Student"]).Select(t => t.Name)).ShouldBeEmpty();
        foreach (var write in names.Where(n => n is "create_study_program" or "schedule_study_pages" or "mark_study_page_done" or "start_test"
                     or "set_worksheet_reminder" or "delete_worksheet_reminder"))
            registry.Get(write).ShouldBeAssignableTo<IAssistantWriteTool>();
        SystemPrompt.Stable("Student").ShouldContain("create_study_program");
    }
}
