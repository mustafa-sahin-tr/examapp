using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class ProgramServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private ProgramService NewService(AppDbContext ctx) => new(ctx);

    private const string User = "kc-user-1";

    private async Task SeedSubjectsAsync(params string[] names)
    {
        await using var ctx = _db.NewContext();
        foreach (var n in names) ctx.Subjects.Add(new Subject { Name = n });
        await ctx.SaveChangesAsync();
    }

    private static UserSelectionDto Sel(int stepId, params string[] values) => new()
    {
        StepId = stepId,
        SelectedValues = values.ToList(),
    };

    // ---- GetProgramStepsAsync ----

    [Fact]
    public async Task Program_steps_are_ordered_and_project_options_and_actions()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.ProgramSteps.Add(new ProgramStep
            {
                Title = "Second", Description = "d", Order = 2,
                Options = { new ProgramStepOption { Label = "L", Value = "v", Icon = "i", Selected = true } },
                Actions = { new ProgramStepAction { Label = "Go", Value = "go" } },
            });
            ctx.ProgramSteps.Add(new ProgramStep { Title = "First", Description = "d", Order = 1 });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var steps = await NewService(read).GetProgramStepsAsync();

        steps.Select(s => s.Title).ShouldBe(new[] { "First", "Second" });
        var second = steps[1];
        second.Options.ShouldHaveSingleItem().Selected.ShouldBeTrue();
        second.Actions.ShouldHaveSingleItem().Value.ShouldBe("go");
    }

    // ---- CreateUserProgramAsync ----

    [Fact]
    public async Task Creating_a_time_based_program_generates_a_daily_schedule_skipping_rest_days()
    {
        await SeedSubjectsAsync("Mat", "Fen");

        var request = new CreateProgramRequestDto
        {
            ProgramName = "Plan",
            Description = "d",
            StartDate = "2026-03-02", // Monday
            EndDate = "2026-03-08",   // Sunday -> 7 days
            UserSelections = new()
            {
                Sel(1, "time"),
                Sel(2, "50-10"),
                Sel(5, "2"),        // 2 subjects/day
                Sel(6, "7", "8"),   // rest on Sunday(7); "8" is the "none" sentinel and is filtered
            },
        };

        UserProgramDto dto;
        await using (var ctx = _db.NewContext())
            dto = await NewService(ctx).CreateUserProgramAsync(User, request);

        dto.StudyType.ShouldBe("time");
        dto.SubjectsPerDay.ShouldBe(2);

        // 6 study days (Mon-Sat) * 2 subjects = 12 schedule rows, none on Sunday
        dto.Schedules.Count.ShouldBe(12);
        dto.Schedules.ShouldAllBe(s => s.ScheduleDate.DayOfWeek != DayOfWeek.Sunday);
        dto.Schedules.ShouldAllBe(s => s.StudyDurationMinutes == 50);
    }

    [Fact]
    public async Task Creating_a_question_based_program_splits_the_daily_question_count_across_subjects()
    {
        await SeedSubjectsAsync("Mat", "Fen", "Türkçe");

        var request = new CreateProgramRequestDto
        {
            ProgramName = "Q",
            Description = "d",
            StartDate = "2026-03-02",
            EndDate = "2026-03-03", // 2 days
            UserSelections = new()
            {
                Sel(1, "question"),
                Sel(3, "30"),  // questions per day
                Sel(5, "3"),   // 3 subjects
            },
        };

        UserProgramDto dto;
        await using (var ctx = _db.NewContext())
            dto = await NewService(ctx).CreateUserProgramAsync(User, request);

        dto.StudyType.ShouldBe("question");
        dto.StudyDuration.ShouldBe("question-based"); // placeholder swapped in for question mode
        dto.QuestionsPerDay.ShouldBe(30);
        dto.Schedules.Count.ShouldBe(6); // 2 days * 3 subjects
        dto.Schedules.ShouldAllBe(s => s.QuestionCount == 10); // 30 / 3
    }

    // ---- GetUserPrograms / GetUserProgramById ----

    [Fact]
    public async Task Programs_are_scoped_to_their_owner()
    {
        await SeedSubjectsAsync("Mat");
        int programId;
        await using (var ctx = _db.NewContext())
        {
            programId = (await NewService(ctx).CreateUserProgramAsync(User, new CreateProgramRequestDto
            {
                ProgramName = "Mine", Description = "d",
                StartDate = "2026-03-02", EndDate = "2026-03-02",
                UserSelections = new() { Sel(1, "time"), Sel(2, "25-5") },
            })).Id;
        }

        await using var read = _db.NewContext();
        var svc = NewService(read);

        (await svc.GetUserProgramsAsync(User)).ShouldHaveSingleItem().ProgramName.ShouldBe("Mine");
        (await svc.GetUserProgramsAsync("someone-else")).ShouldBeEmpty();
        (await svc.GetUserProgramByIdAsync(User, programId)).ShouldNotBeNull();
        (await svc.GetUserProgramByIdAsync("someone-else", programId)).ShouldBeNull();
    }

    // ---- AddStudyPageSchedulesAsync ----

    [Fact]
    public async Task Adding_study_page_schedules_returns_null_for_an_unknown_program()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).AddStudyPageSchedulesAsync(User, 404, new ProgramStudyPageScheduleRequestDto());
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Adding_study_page_schedules_skips_pages_that_do_not_exist()
    {
        await SeedSubjectsAsync("Mat");
        int programId, realPageId;
        await using (var ctx = _db.NewContext())
        {
            var page = new StudyPage { Title = "P", Description = "d", CreatedByUserId = 1 };
            ctx.StudyPages.Add(page);
            await ctx.SaveChangesAsync();
            realPageId = page.Id;

            programId = (await NewService(ctx).CreateUserProgramAsync(User, new CreateProgramRequestDto
            {
                ProgramName = "P", Description = "d",
                StartDate = "2026-03-02", EndDate = "2026-03-02",
                UserSelections = new() { Sel(1, "time"), Sel(2, "25-5") },
            })).Id;
        }

        ProgramStudyPageScheduleRequestDto request = new()
        {
            Items =
            {
                new ProgramStudyPageScheduleItemDto { StudyPageId = realPageId, StartDate = new DateTime(2026, 3, 2), EndDate = new DateTime(2026, 3, 5) },
                new ProgramStudyPageScheduleItemDto { StudyPageId = 99999, StartDate = new DateTime(2026, 3, 2), EndDate = new DateTime(2026, 3, 5) },
            },
        };

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).AddStudyPageSchedulesAsync(User, programId, request);
            result!.StudyPageSchedules.ShouldHaveSingleItem().StudyPageId.ShouldBe(realPageId);
        }

        await using var check = _db.NewContext();
        (await check.UserProgramStudyPageSchedules.CountAsync()).ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();
}
