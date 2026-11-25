using System.Net;
using AssistantService.Tools;
using Shouldly;
using static AssistantService.Tests.ToolHarness;

namespace AssistantService.Tests;

/// <summary>Issue #512 (Phase 2, slice 1): teacher read tools over the teacher dashboard endpoints, against a fake exam API.</summary>
public class TeacherToolTests : IDisposable
{
    private readonly ToolHarness _h = new();
    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Overview_combines_summary_and_activity_with_the_teachers_token_and_marks_names_untrusted()
    {
        _h.Api.On("GET", "api/teacher/dashboard-summary", new { totalWorksheets = 6, totalUniqueStudents = 24 });
        _h.Api.On("GET", "api/teacher/students-activity-summary?days=14", new
        {
            totalQuestionsSolved = 200, totalCorrectCount = 150, totalTimeSeconds = 7260,
            topStudents = new[] { new { studentId = 3, studentName = "Ali Kaya", questionsSolved = 40, correctCount = 30, timeSeconds = 900 } },
        });

        var result = await new GetTeacherOverviewTool(_h.Downstream).ExecuteAsync(Teacher(), Args(new { days = 14 }));

        _h.Api.Calls.ShouldAllBe(c => c.Authorization == "Bearer token-teacher-1");
        result.Data!["worksheets"]!.GetValue<int>().ShouldBe(6);
        result.Data!["accuracyPct"]!.GetValue<decimal>().ShouldBe(75m);
        result.Data!["studyMinutes"]!.GetValue<int>().ShouldBe(121);
        var top = result.Data!["mostActiveStudents"]!.AsArray().Single()!;
        top["name"]!.GetValue<string>().ShouldBe("<untrusted>Ali Kaya</untrusted>");
        top["accuracyPct"]!.GetValue<decimal>().ShouldBe(75m);
        result.Cards!.Single()["kind"]!.GetValue<string>().ShouldBe("kpi");
    }

    [Fact]
    public async Task Overview_refuses_a_window_out_of_range_without_a_call_and_passes_TeacherNotApproved_through()
    {
        var tool = new GetTeacherOverviewTool(_h.Downstream);
        (await tool.ExecuteAsync(Teacher(), Args(new { days = 91 }))).ErrorCode.ShouldBe("InvalidArguments");
        _h.Api.Calls.ShouldBeEmpty();

        _h.Api.On("GET", "api/teacher/dashboard-summary",
            new { success = false, errorCode = "TeacherNotApproved", message = "Onay bekleniyor." }, HttpStatusCode.Forbidden);
        (await tool.ExecuteAsync(Teacher(), Args())).ErrorCode.ShouldBe("TeacherNotApproved");
    }

    [Fact]
    public async Task Worksheets_filter_by_name_ignoring_dotted_i_and_put_assigned_low_completion_first()
    {
        _h.Api.On("GET", "api/teacher/worksheets-overview", new object[]
        {
            new { worksheetId = 1, name = "Açılar Testi", assignedStudentCount = 10, completionPercentage = 80.0 },
            new { worksheetId = 2, name = "AÇILAR 2", assignedStudentCount = 0, completionPercentage = 0.0 },
            new { worksheetId = 3, name = "açılar tekrar", assignedStudentCount = 8, completionPercentage = 12.456 },
            new { worksheetId = 4, name = "Kesirler", assignedStudentCount = 5, completionPercentage = 0.0 },
        });

        var result = await new ListMyWorksheetsTool(_h.Downstream).ExecuteAsync(Teacher(), Args(new { query = "AÇILAR" }));

        result.Data!["total"]!.GetValue<int>().ShouldBe(3);
        result.Data!["assignedTotal"]!.GetValue<int>().ShouldBe(2);
        var ids = result.Data!["items"]!.AsArray().Select(i => i!["worksheetId"]!.GetValue<int>()).ToList();
        ids.ShouldBe([3, 1, 2]);
        result.Data!["items"]![0]!["completionPct"]!.GetValue<decimal>().ShouldBe(12.5m);
        result.Data!["items"]![0]!["name"]!.GetValue<string>().ShouldStartWith("<untrusted>");
    }

    [Fact]
    public async Task Lagging_students_pass_the_grade_filter_put_overdue_first_and_filter_one_test()
    {
        _h.Api.On("GET", "api/teacher/lagging-students?gradeId=22", new object[]
        {
            new { studentId = 1, studentName = "Can", worksheetId = 7, worksheetName = "Açılar", completionPercentage = 0.0, isLowCompletion = true, isExpired = false, gradeId = 22, gradeName = "5. Sınıf" },
            new { studentId = 2, studentName = "Ece", worksheetId = 7, worksheetName = "Açılar", completionPercentage = 0.0, isLowCompletion = true, isExpired = true, gradeId = 22, gradeName = "5. Sınıf" },
            new { studentId = 1, studentName = "Can", worksheetId = 8, worksheetName = "Kesirler", completionPercentage = 0.0, isLowCompletion = true, isExpired = true, gradeId = 22, gradeName = "5. Sınıf" },
        });
        var tool = new ListLaggingStudentsTool(_h.Downstream);

        var all = await tool.ExecuteAsync(Teacher(), Args(new { gradeId = 22 }));
        all.Data!["total"]!.GetValue<int>().ShouldBe(3);
        all.Data!["overdueTotal"]!.GetValue<int>().ShouldBe(2);
        all.Data!["distinctStudents"]!.GetValue<int>().ShouldBe(2);
        all.Data!["items"]![0]!["overdue"]!.GetValue<bool>().ShouldBeTrue();
        all.Data!["items"]![2]!["overdue"]!.GetValue<bool>().ShouldBeFalse();

        var one = await tool.ExecuteAsync(Teacher(), Args(new { gradeId = 22, worksheetId = 8 }));
        one.Data!["items"]!.AsArray().Single()!["name"]!.GetValue<string>().ShouldBe("<untrusted>Can</untrusted>");
    }

    [Fact]
    public void Teacher_screens_are_open_to_teachers_only()
    {
        UiScreens.Route("myTests", null, ["Teacher"]).ShouldBe("/tests");
        UiScreens.Route("worksheet", 5, ["Teacher"]).ShouldBe("/test/5");
        UiScreens.Route("myTests", null, ["Student"]).ShouldBeNull();
        UiScreens.Route("practice", null, ["Teacher"]).ShouldBeNull();
    }
}
