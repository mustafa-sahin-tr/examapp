using System.Net;
using System.Text.Json.Nodes;
using AssistantService.Chat;
using AssistantService.Tools;
using Shouldly;
using static AssistantService.Tests.ToolHarness;

namespace AssistantService.Tests;

/// <summary>Issue #1336 (Phase 1b): parent tools over the parent dashboard endpoints, against a fake exam API.</summary>
public class ParentToolTests : IDisposable
{
    private readonly ToolHarness _h = new();
    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Children_are_the_active_links_with_first_name_and_grade_only()
    {
        _h.Api.On("GET", "api/parent-links/my-children", new object[]
        {
            new { linkId = 1, studentId = 7, status = "Active", studentName = "Ayşe Nur Yılmaz", gradeName = "5. Sınıf", schoolName = "Okul" },
            new { linkId = 2, studentId = (int?)null, status = "Pending", studentName = (string?)null, gradeName = (string?)null, schoolName = (string?)null },
        });

        var result = await new ListMyChildrenTool(_h.Downstream).ExecuteAsync(Parent(), Args());

        _h.Api.Calls.Single().Authorization.ShouldBe("Bearer token-parent-1");
        var child = result.Data!["children"]!.AsArray().Single()!;
        child["studentId"]!.GetValue<int>().ShouldBe(7);
        child["firstName"]!.GetValue<string>().ShouldBe("<untrusted>Ayşe</untrusted>");
        child["gradeName"]!.GetValue<string>().ShouldBe("5. Sınıf");
        child["schoolName"].ShouldBeNull();
        result.Data!.ToJsonString().ShouldNotContain("Yılmaz");
        result.Data!["pendingLinks"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task Weekly_summary_keeps_counts_and_the_weak_topic_and_computes_accuracy()
    {
        _h.Api.On("GET", "api/parent/children/7/summary", new
        {
            studentId = 7, weekStart = "2026-11-23", questionsSolvedThisWeek = 40, correctThisWeek = 30, activeDaysThisWeek = 3,
            assignments = new { completed = 2, overdue = 1, pending = 3, windowDays = 14 },
            attention = new { weakTopic = new { topicId = 5, topicName = "Kesirler", subjectName = "Matematik", accuracyPercent = 45, answered = 11 } },
        });

        var result = await new GetChildWeeklySummaryTool(_h.Downstream).ExecuteAsync(Parent(), Args(new { studentId = 7 }));

        result.Data!["accuracyPct"]!.GetValue<decimal>().ShouldBe(75m);
        result.Data!["assignments"]!["overdue"]!.GetValue<int>().ShouldBe(1);
        result.Data!["weakTopic"]!["topicName"]!.GetValue<string>().ShouldBe("Kesirler");
        result.Cards!.Single()["kind"]!.GetValue<string>().ShouldBe("kpi");
    }

    [Fact]
    public async Task Another_parents_child_is_not_found_and_a_missing_id_is_refused_without_a_call()
    {
        _h.Api.On("GET", "api/parent/children/99/summary", new { errorCode = "NotFound", message = "Çocuk bulunamadı." }, HttpStatusCode.NotFound);
        var tool = new GetChildWeeklySummaryTool(_h.Downstream);

        (await tool.ExecuteAsync(Parent(), Args(new { studentId = 99 }))).ErrorCode.ShouldBe("NotFound");
        (await tool.ExecuteAsync(Parent(), Args())).ErrorCode.ShouldBe("InvalidArguments");
        _h.Api.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Assignments_filter_by_status_mark_titles_untrusted_and_drop_teacher_names()
    {
        _h.Api.On("GET", "api/parent/children/7/assignments?page=1&status=overdue", new
        {
            studentId = 7, totalCount = 1, counts = new { completed = 0, overdue = 1, pending = 0, windowDays = 14 },
            items = new[] { new { worksheetId = 3, title = "Kesirler ödevi", subject = "Matematik", teacherName = "Öğretmen Adı",
                startAt = "2026-11-20T08:00:00Z", deadline = "2026-11-22T20:00:00Z", status = "overdue", testInstanceId = (int?)null } },
        });
        var tool = new ListChildAssignmentsTool(_h.Downstream);

        var result = await tool.ExecuteAsync(Parent(), Args(new { studentId = 7, status = "overdue" }));

        var row = result.Data!["assignments"]!.AsArray().Single()!;
        row["title"]!.GetValue<string>().ShouldBe("<untrusted>Kesirler ödevi</untrusted>");
        row["teacherName"].ShouldBeNull();
        result.Data!["counts"]!["overdue"]!.GetValue<int>().ShouldBe(1);
        (await tool.ExecuteAsync(Parent(), Args(new { studentId = 7, status = "late" }))).ErrorCode.ShouldBe("InvalidArguments");
    }

    [Fact]
    public async Task Test_result_returns_counts_and_topics_but_no_questions()
    {
        _h.Api.On("GET", "api/parent/children/7/test-results/12", new
        {
            studentId = 7, testInstanceId = 12, title = "Deneme 1", subject = "Fen", outcome = "completed",
            score = new { scorePercent = 80, correctCount = 8, wrongCount = 2, blankCount = 0, totalCount = 10, durationSeconds = 600 },
            topics = new[] { new { topicId = 1, name = "Kuvvet", correctCount = 4, wrongCount = 1, blankCount = 0, totalCount = 5 } },
            questions = new[] { new { order = 1, answerPayload = "{}" } },
        });

        var result = await new GetChildTestResultTool(_h.Downstream).ExecuteAsync(Parent(), Args(new { studentId = 7, testInstanceId = 12 }));

        result.Data!["score"]!["scorePercent"]!.GetValue<int>().ShouldBe(80);
        result.Data!["topics"]!.AsArray().Single()!["name"]!.GetValue<string>().ShouldBe("Kuvvet");
        result.Data!["questions"].ShouldBeNull();
        result.Data!["title"]!.GetValue<string>().ShouldStartWith("<untrusted>");
    }

    [Fact]
    public async Task Weak_topics_use_the_parent_minimum_and_a_plan_without_analysis_is_forbidden()
    {
        _h.Api.On("GET", "api/parent/children/7/topic-performance?days=30&groupBy=topic&minQuestions=5", new
        {
            items = new[]
            {
                new { subjectName = "Mat", topicId = 1, topicName = "Kesirler", total = 10, correct = 4, accuracyPct = 40m },
                new { subjectName = "Mat", topicId = 2, topicName = "Açılar", total = 10, correct = 9, accuracyPct = 90m },
            },
        });
        _h.Api.On("GET", "api/parent/children/8/topic-performance?days=30&groupBy=topic&minQuestions=5",
            new { errorCode = "FeatureNotInPlan", message = "m" }, HttpStatusCode.Forbidden);
        var tool = new GetChildWeakTopicsTool(_h.Downstream);

        var result = await tool.ExecuteAsync(Parent(), Args(new { studentId = 7 }));
        result.Data!["weakTopics"]!.AsArray().Select(t => t!["topicName"]!.GetValue<string>()).ShouldBe(["Kesirler"]);
        (await tool.ExecuteAsync(Parent(), Args(new { studentId = 8 }))).ErrorCode.ShouldBe("FeatureNotInPlan");
    }

    [Fact]
    public async Task Progress_and_schedule_keep_only_the_childs_own_safe_fields()
    {
        _h.Api.On("GET", "api/parent/children/7/progress", new
        {
            totalXp = 900, level = 4, weeklyXp = 120, weekStart = "2026-11-23",
            badges = new[] { new { name = "İlk test", icon = "x.png", earnedOn = "2026-11-01" } },
            ranks = new[] { new { scope = "school", rank = 3, totalCount = 40 } },
        });
        _h.Api.On("GET", "api/parent/children/7/schedule?from=2026-11-23&to=2026-11-29", new
        {
            from = "2026-11-23", to = "2026-11-29",
            plans = new[] { new { title = "Kesir tekrarı", subject = "Mat", plannedOn = "2026-11-24" } },
            lessons = new[] { new { teacherName = "Öğretmen Adı", startsOn = "2026-11-25", startAt = "2026-11-25T15:00:00Z", endAt = "2026-11-25T16:00:00Z", status = "approved" } },
        });

        var progress = await new GetChildProgressTool(_h.Downstream).ExecuteAsync(Parent(), Args(new { studentId = 7 }));
        progress.Data!["recentBadges"]!.AsArray().Single()!["icon"].ShouldBeNull();
        progress.Data!["ranks"]!.AsArray().Single()!["rank"]!.GetValue<int>().ShouldBe(3);

        var tool = new GetChildScheduleTool(_h.Downstream);
        var schedule = await tool.ExecuteAsync(Parent(), Args(new { studentId = 7, from = "2026-11-23", to = "2026-11-29" }));
        schedule.Data!["lessons"]!.AsArray().Single()!["teacherName"].ShouldBeNull();
        schedule.Data!["plans"]!.AsArray().Single()!["title"]!.GetValue<string>().ShouldStartWith("<untrusted>");
        (await tool.ExecuteAsync(Parent(), Args(new { studentId = 7, from = "2026-11-23" }))).ErrorCode.ShouldBe("InvalidArguments");
        (await tool.ExecuteAsync(Parent(), Args(new { studentId = 7, from = "x&y=1", to = "2026-11-29" }))).ErrorCode.ShouldBe("InvalidArguments");
    }

    [Fact]
    public async Task Parent_screens_open_only_for_parents()
    {
        var tool = new UiOpenScreenTool();

        (await tool.ExecuteAsync(Parent(), Args(new { screen = "childDashboard", id = 7 }))).UiActions!.Single()["route"]!
            .GetValue<string>().ShouldBe("/parent?child=7");
        (await tool.ExecuteAsync(Parent(), Args(new { screen = "practice" }))).ErrorCode.ShouldBe("ScreenNotAllowed");
        (await tool.ExecuteAsync(Student(), Args(new { screen = "myChildren" }))).ErrorCode.ShouldBe("ScreenNotAllowed");
    }

    [Fact]
    public void Parents_find_their_own_guide_articles()
    {
        Guide.Search("çocuğumun şifresini nasıl sıfırlarım", ["Parent"]).First().Article.Slug.ShouldBe("cocuk-sifre");
        Guide.Search("çocuğumun şifresini nasıl sıfırlarım", ["Student"]).ShouldNotContain(h => h.Article.Slug == "cocuk-sifre");
    }

    [Fact]
    public void Parent_prompt_and_crisis_text_are_their_own()
    {
        SystemPrompt.Stable("Parent").ShouldContain("list_my_children");
        SystemPrompt.Stable("Student").ShouldNotContain("list_my_children");
        SystemPrompt.CrisisResponseFor("Parent").ShouldBe(SystemPrompt.ParentCrisisResponse);
        SystemPrompt.CrisisResponseFor("Student").ShouldBe(SystemPrompt.CrisisResponse);
    }

    private static object Activity(int answered, int correct, int prevAnswered) => new
    {
        studentId = 7, days = 7, from = "2026-11-18", to = "2026-11-24",
        daily = new[] { new { day = "2026-11-24", answered, correct, points = 30 } },
        current = new { answered, correct, activeDays = 2, points = 30 },
        previous = new { answered = prevAnswered, correct = prevAnswered / 2, activeDays = 1, points = 20 },
    };

    [Fact]
    public async Task Activity_trend_compares_with_the_window_before_and_shows_a_kpi_card()
    {
        _h.Api.On("GET", "api/parent/children/7/activity?days=7", Activity(30, 24, 20));

        var result = await new GetChildActivityTrendTool(_h.Downstream).ExecuteAsync(Parent(), Args(new { studentId = 7, days = 3 }));

        _h.Api.Calls.Single().PathAndQuery.ShouldEndWith("api/parent/children/7/activity?days=7"); // clamped to the API's minimum
        result.Data!["current"]!["accuracyPct"]!.GetValue<decimal>().ShouldBe(80m);
        result.Data!["previous"]!["answered"]!.GetValue<int>().ShouldBe(20);
        result.Data!["answeredChangePct"]!.GetValue<decimal>().ShouldBe(50m);
        result.Data!["daily"]!.AsArray().Single()!["points"]!.GetValue<int>().ShouldBe(30);
        var kpis = result.Cards!.Single()["items"]!.AsArray().Select(i => i!["key"]!.GetValue<string>()).ToList();
        kpis.ShouldBe(["questionsSolved", "previousQuestionsSolved", "accuracyPct", "activeDays", "xp"]);
    }

    [Fact]
    public async Task Activity_trend_has_no_change_when_the_window_before_was_empty()
    {
        _h.Api.On("GET", "api/parent/children/7/activity?days=14", Activity(5, 5, 0));

        var result = await new GetChildActivityTrendTool(_h.Downstream).ExecuteAsync(Parent(), Args(new { studentId = 7, days = 14 }));

        result.Data!["answeredChangePct"].ShouldBeNull();
        result.Data!["previous"]!["accuracyPct"].ShouldBeNull();
    }

    [Fact]
    public async Task Compare_children_reads_each_active_child_and_keeps_the_others_when_one_fails()
    {
        _h.Api.On("GET", "api/parent-links/my-children", new object[]
        {
            new { studentId = 7, status = "Active", studentName = "Ayşe Yılmaz" },
            new { studentId = 8, status = "Active", studentName = "Can Yılmaz" },
            new { studentId = (int?)null, status = "Pending", studentName = (string?)null },
        });
        _h.Api.On("GET", "api/parent/children/7/activity?days=7", Activity(30, 24, 20));
        _h.Api.On("GET", "api/parent/children/8/activity?days=7", new { errorCode = "NotFound" }, HttpStatusCode.NotFound);

        var result = await new CompareChildrenTool(_h.Downstream).ExecuteAsync(Parent(), Args());

        var children = result.Data!["children"]!.AsArray();
        children.Count.ShouldBe(2);
        children[0]!["firstName"]!.GetValue<string>().ShouldBe("<untrusted>Ayşe</untrusted>");
        children[0]!["current"]!["answered"]!.GetValue<int>().ShouldBe(30);
        children[1]!["error"]!.GetValue<string>().ShouldBe("NotFound");
        result.Data!.ToJsonString().ShouldNotContain("Yılmaz");
    }

    private static object Report(string weekStart) => new
    {
        studentId = 7, weekStart, questionsSolved = 50, correct = 40, activeDays = 4, studyMinutes = 95, points = 120,
        subjects = new[] { new { subjectId = 1, subjectName = "Matematik", answered = 30, correct = 21, accuracyPercent = 70 } },
        droppedTopics = new[] { new { topicId = 5, topicName = "Kesirler", subjectName = "Matematik", accuracyPercent = 40, previousAccuracyPercent = 70 } },
        assignmentsCompleted = 2, assignmentsOverdue = 1,
        mockExams = new[] { new { name = "LGS Deneme 1", net = 42.5m, rank = 3, takers = 40, finishedAtUtc = "2026-11-21T10:00:00Z" } },
        badges = new[] { new { name = "Haftalık seri", icon = "fire" } },
        suggestion = "reviewTopic", suggestionTopic = "Kesirler",
    };

    [Fact]
    public async Task Weekly_report_keeps_totals_and_counts_older_weeks_back_from_the_api_week()
    {
        _h.Api.On("GET", "api/parent/children/7/weekly-report", Report("2026-11-16"));
        _h.Api.On("GET", "api/parent/children/7/weekly-report?weekStart=2026-11-02", Report("2026-11-02"));
        var tool = new GetChildWeeklyReportTool(_h.Downstream);

        var latest = await tool.ExecuteAsync(Parent(), Args(new { studentId = 7 }));
        latest.Data!["accuracyPct"]!.GetValue<decimal>().ShouldBe(80m);
        latest.Data!["droppedTopics"]!.AsArray().Single()!["topicName"]!.GetValue<string>().ShouldBe("Kesirler");
        latest.Data!["mockExams"]!.AsArray().Single()!["name"]!.GetValue<string>().ShouldBe("<untrusted>LGS Deneme 1</untrusted>");
        latest.Data!["badges"]!.AsArray().Single()!["icon"].ShouldBeNull();
        latest.Cards!.Single()["kind"]!.GetValue<string>().ShouldBe("kpi");

        var older = await tool.ExecuteAsync(Parent(), Args(new { studentId = 7, weeksAgo = 2 }));
        older.Data!["weekStart"]!.GetValue<string>().ShouldBe("2026-11-02");
    }

    [Fact]
    public async Task Weekly_report_outside_the_plan_is_forbidden()
    {
        // The real body (EntitlementLimitReachedDto) carries `code`, not `errorCode`: the status maps it.
        _h.Api.On("GET", "api/parent/children/7/weekly-report", new { code = "entitlement.notInPlan", feature = "weekly-report", limit = 0 }, HttpStatusCode.Forbidden);

        var result = await new GetChildWeeklyReportTool(_h.Downstream).ExecuteAsync(Parent(), Args(new { studentId = 7, weeksAgo = 1 }));

        result.ErrorCode.ShouldBe("Forbidden");
        _h.Api.Calls.Count.ShouldBe(1);
    }
}

public class AssistantRoleOptionTests
{
    [Fact]
    public void Roles_from_one_comma_separated_env_value_are_split()
    {
        AssistantOptions.SplitRoles(["Student, Parent", "Parent", " "]).ShouldBe(["Student", "Parent"]);
        AssistantOptions.SplitRoles(null).ShouldBeEmpty();
    }
}
