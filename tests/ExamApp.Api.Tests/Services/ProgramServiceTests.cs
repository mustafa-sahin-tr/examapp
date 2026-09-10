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

    // ---- AddStudyItemSchedulesAsync ----

    [Fact]
    public async Task Adding_study_page_schedules_returns_null_for_an_unknown_program()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).AddStudyItemSchedulesAsync(User, 404, new ProgramStudyItemScheduleRequestDto());
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Adding_study_page_schedules_skips_pages_that_do_not_exist()
    {
        await SeedSubjectsAsync("Mat");
        int programId, realPageId;
        await using (var ctx = _db.NewContext())
        {
            var page = new StudyItem { Title = "P", Description = "d", CreatedByUserId = 1 };
            ctx.StudyItems.Add(page);
            await ctx.SaveChangesAsync();
            realPageId = page.Id;

            programId = (await NewService(ctx).CreateUserProgramAsync(User, new CreateProgramRequestDto
            {
                ProgramName = "P", Description = "d",
                StartDate = "2026-03-02", EndDate = "2026-03-02",
                UserSelections = new() { Sel(1, "time"), Sel(2, "25-5") },
            })).Id;
        }

        ProgramStudyItemScheduleRequestDto request = new()
        {
            Items =
            {
                new ProgramStudyItemScheduleItemDto { StudyItemId = realPageId, StartDate = new DateTime(2026, 3, 2), EndDate = new DateTime(2026, 3, 5) },
                new ProgramStudyItemScheduleItemDto { StudyItemId = 99999, StartDate = new DateTime(2026, 3, 2), EndDate = new DateTime(2026, 3, 5) },
            },
        };

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).AddStudyItemSchedulesAsync(User, programId, request);
            result!.StudyItemSchedules.ShouldHaveSingleItem().StudyItemId.ShouldBe(realPageId);
        }

        await using var check = _db.NewContext();
        (await check.UserProgramStudyPageSchedules.CountAsync()).ShouldBe(1);
    }

    // ---- CompleteStudyItemAsync / UncompleteStudyItemAsync / DeleteUserProgramAsync (issue #109) ----

    private async Task<(int programId, int scheduleId)> SeedProgramWithScheduleAsync(string userId, bool isCompleted = false, DateTime? completedDate = null)
    {
        await using var ctx = _db.NewContext();
        var page = new StudyItem { Title = "P", Description = "d", CreatedByUserId = 1 };
        ctx.StudyItems.Add(page);
        await ctx.SaveChangesAsync();

        var program = new UserProgram
        {
            UserId = userId,
            ProgramName = "Plan",
            Description = "d",
            StudyType = "time",
            StudyDuration = "25-5",
            SubjectsPerDay = 1,
            RestDays = "",
            DifficultSubjects = "",
        };
        ctx.UserPrograms.Add(program);
        await ctx.SaveChangesAsync();

        var schedule = new UserProgramStudyPageSchedule
        {
            UserProgramId = program.Id,
            StudyItemId = page.Id,
            StartDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
            IsCompleted = isCompleted,
            CompletedDate = completedDate,
        };
        ctx.UserProgramStudyPageSchedules.Add(schedule);
        await ctx.SaveChangesAsync();

        return (program.Id, schedule.Id);
    }

    [Fact]
    public async Task CompleteStudyItemAsync_Owner_MarksCompletedAndSetsCompletedDate()
    {
        var (programId, scheduleId) = await SeedProgramWithScheduleAsync(User);

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CompleteStudyItemAsync(User, programId, scheduleId);

        result.ShouldBeTrue();

        await using var check = _db.NewContext();
        var schedule = await check.UserProgramStudyPageSchedules.SingleAsync(s => s.Id == scheduleId);
        schedule.IsCompleted.ShouldBeTrue();
        schedule.CompletedDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task CompleteStudyItemAsync_AlreadyCompleted_KeepsOriginalCompletedDate()
    {
        var originalDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var (programId, scheduleId) = await SeedProgramWithScheduleAsync(User, isCompleted: true, completedDate: originalDate);

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CompleteStudyItemAsync(User, programId, scheduleId);

        result.ShouldBeTrue();

        await using var check = _db.NewContext();
        var schedule = await check.UserProgramStudyPageSchedules.SingleAsync(s => s.Id == scheduleId);
        schedule.IsCompleted.ShouldBeTrue();
        schedule.CompletedDate.ShouldBe(originalDate);
    }

    [Fact]
    public async Task UncompleteStudyItemAsync_CompletedSchedule_ClearsCompletionState()
    {
        var (programId, scheduleId) = await SeedProgramWithScheduleAsync(User, isCompleted: true, completedDate: DateTime.UtcNow);

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).UncompleteStudyItemAsync(User, programId, scheduleId);

        result.ShouldBeTrue();

        await using var check = _db.NewContext();
        var schedule = await check.UserProgramStudyPageSchedules.SingleAsync(s => s.Id == scheduleId);
        schedule.IsCompleted.ShouldBeFalse();
        schedule.CompletedDate.ShouldBeNull();
    }

    [Fact]
    public async Task UncompleteStudyItemAsync_AlreadyNotCompleted_IsIdempotentAndReturnsTrue()
    {
        var (programId, scheduleId) = await SeedProgramWithScheduleAsync(User, isCompleted: false, completedDate: null);

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).UncompleteStudyItemAsync(User, programId, scheduleId);

        result.ShouldBeTrue();

        await using var check = _db.NewContext();
        var schedule = await check.UserProgramStudyPageSchedules.SingleAsync(s => s.Id == scheduleId);
        schedule.IsCompleted.ShouldBeFalse();
        schedule.CompletedDate.ShouldBeNull();
    }

    [Fact]
    public async Task CompleteStudyItemAsync_ScheduleBelongsToAnotherUsersProgram_ReturnsFalse()
    {
        var (programId, scheduleId) = await SeedProgramWithScheduleAsync("owner-user");

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CompleteStudyItemAsync("attacker-user", programId, scheduleId);

        result.ShouldBeFalse();

        await using var check = _db.NewContext();
        var schedule = await check.UserProgramStudyPageSchedules.SingleAsync(s => s.Id == scheduleId);
        schedule.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task UncompleteStudyItemAsync_ScheduleBelongsToAnotherUsersProgram_ReturnsFalse()
    {
        var (programId, scheduleId) = await SeedProgramWithScheduleAsync("owner-user", isCompleted: true, completedDate: DateTime.UtcNow);

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).UncompleteStudyItemAsync("attacker-user", programId, scheduleId);

        result.ShouldBeFalse();

        await using var check = _db.NewContext();
        var schedule = await check.UserProgramStudyPageSchedules.SingleAsync(s => s.Id == scheduleId);
        schedule.IsCompleted.ShouldBeTrue(); // unchanged
    }

    [Fact]
    public async Task CompleteStudyItemAsync_UnknownScheduleId_ReturnsFalse()
    {
        var (programId, _) = await SeedProgramWithScheduleAsync(User);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CompleteStudyItemAsync(User, programId, 999999);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteUserProgramAsync_Owner_SoftDeletesProgramWithoutRemovingRow()
    {
        int programId;
        await using (var ctx = _db.NewContext())
        {
            var program = new UserProgram { UserId = User, ProgramName = "Plan", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "" };
            ctx.UserPrograms.Add(program);
            await ctx.SaveChangesAsync();
            programId = program.Id;
        }

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).DeleteUserProgramAsync(User, programId);

        result.ShouldBeTrue();

        await using var check = _db.NewContext();
        var program2 = await check.UserPrograms.SingleAsync(p => p.Id == programId);
        program2.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteUserProgramAsync_AlreadyDeleted_IsIdempotentAndReturnsTrue()
    {
        int programId;
        await using (var ctx = _db.NewContext())
        {
            var program = new UserProgram { UserId = User, ProgramName = "Plan", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "", IsActive = false };
            ctx.UserPrograms.Add(program);
            await ctx.SaveChangesAsync();
            programId = program.Id;
        }

        await using var ctx2 = _db.NewContext();
        var result = await NewService(ctx2).DeleteUserProgramAsync(User, programId);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteUserProgramAsync_AnotherUsersProgram_ReturnsFalseAndLeavesItActive()
    {
        int programId;
        await using (var ctx = _db.NewContext())
        {
            var program = new UserProgram { UserId = "owner-user", ProgramName = "Plan", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "" };
            ctx.UserPrograms.Add(program);
            await ctx.SaveChangesAsync();
            programId = program.Id;
        }

        bool result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).DeleteUserProgramAsync("attacker-user", programId);

        result.ShouldBeFalse();

        await using var check = _db.NewContext();
        var program2 = await check.UserPrograms.SingleAsync(p => p.Id == programId);
        program2.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task GetUserProgramsAsync_SoftDeletedProgram_IsExcludedFromList()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserPrograms.Add(new UserProgram { UserId = User, ProgramName = "Active", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "", IsActive = true });
            ctx.UserPrograms.Add(new UserProgram { UserId = User, ProgramName = "Deleted", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "", IsActive = false });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var programs = await NewService(read).GetUserProgramsAsync(User);

        programs.ShouldHaveSingleItem().ProgramName.ShouldBe("Active");
    }

    [Fact]
    public async Task GetUserProgramByIdAsync_SoftDeletedProgram_ReturnsNullForOwner()
    {
        var (programId, _) = await SeedProgramWithScheduleAsync(User);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteUserProgramAsync(User, programId)).ShouldBeTrue();

        await using var read = _db.NewContext();
        var program = await NewService(read).GetUserProgramByIdAsync(User, programId);

        program.ShouldBeNull();
    }

    [Fact]
    public async Task CompleteAndUncompleteStudyItemAsync_SoftDeletedProgram_ReturnFalseAndLeaveScheduleUntouched()
    {
        var (programId, scheduleId) = await SeedProgramWithScheduleAsync(User);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteUserProgramAsync(User, programId)).ShouldBeTrue();

        bool completeResult, uncompleteResult;
        await using (var ctx = _db.NewContext())
        {
            var service = NewService(ctx);
            completeResult = await service.CompleteStudyItemAsync(User, programId, scheduleId);
            uncompleteResult = await service.UncompleteStudyItemAsync(User, programId, scheduleId);
        }

        completeResult.ShouldBeFalse();
        uncompleteResult.ShouldBeFalse();

        await using var check = _db.NewContext();
        var schedule = await check.UserProgramStudyPageSchedules.SingleAsync(s => s.Id == scheduleId);
        schedule.IsCompleted.ShouldBeFalse();
        schedule.CompletedDate.ShouldBeNull();
    }

    [Fact]
    public async Task AddStudyItemSchedulesAsync_SoftDeletedProgram_ReturnsNullAndAddsNothing()
    {
        var (programId, _) = await SeedProgramWithScheduleAsync(User);
        int pageId;
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).DeleteUserProgramAsync(User, programId)).ShouldBeTrue();
            pageId = await ctx.StudyItems.Select(p => p.Id).FirstAsync();
        }

        var request = new ProgramStudyItemScheduleRequestDto
        {
            Items = new List<ProgramStudyItemScheduleItemDto>
            {
                new() { StudyItemId = pageId, StartDate = new DateTime(2026, 3, 10), EndDate = new DateTime(2026, 3, 12) }
            }
        };

        UserProgramDto? result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).AddStudyItemSchedulesAsync(User, programId, request);

        result.ShouldBeNull();

        await using var check = _db.NewContext();
        (await check.UserProgramStudyPageSchedules.CountAsync(s => s.UserProgramId == programId)).ShouldBe(1);
    }

    [Fact]
    public async Task MapToUserProgramDto_ZeroStudyItems_ProgressPercentageIsZero()
    {
        int programId;
        await using (var ctx = _db.NewContext())
        {
            var program = new UserProgram { UserId = User, ProgramName = "Plan", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "" };
            ctx.UserPrograms.Add(program);
            await ctx.SaveChangesAsync();
            programId = program.Id;
        }

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetUserProgramByIdAsync(User, programId);

        dto!.TotalPageCount.ShouldBe(0);
        dto.CompletedPageCount.ShouldBe(0);
        dto.ProgressPercentage.ShouldBe(0);
    }

    [Fact]
    public async Task MapToUserProgramDto_PartiallyCompletedPages_ComputesCorrectPercentage()
    {
        int programId;
        await using (var ctx = _db.NewContext())
        {
            var program = new UserProgram { UserId = User, ProgramName = "Plan", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "" };
            ctx.UserPrograms.Add(program);
            await ctx.SaveChangesAsync();
            programId = program.Id;

            var page = new StudyItem { Title = "P", Description = "d", CreatedByUserId = 1 };
            ctx.StudyItems.Add(page);
            await ctx.SaveChangesAsync();

            for (var i = 0; i < 5; i++)
            {
                ctx.UserProgramStudyPageSchedules.Add(new UserProgramStudyPageSchedule
                {
                    UserProgramId = programId,
                    StudyItemId = page.Id,
                    StartDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                    IsCompleted = i < 2, // 2 of 5 completed
                });
            }
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetUserProgramByIdAsync(User, programId);

        dto!.TotalPageCount.ShouldBe(5);
        dto.CompletedPageCount.ShouldBe(2);
        dto.ProgressPercentage.ShouldBe(40);
    }

    [Fact]
    public async Task MapToUserProgramDto_AllStudyItemsCompleted_ProgressPercentageIsHundred()
    {
        int programId;
        await using (var ctx = _db.NewContext())
        {
            var program = new UserProgram { UserId = User, ProgramName = "Plan", Description = "d", StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 1, RestDays = "", DifficultSubjects = "" };
            ctx.UserPrograms.Add(program);
            await ctx.SaveChangesAsync();
            programId = program.Id;

            var page = new StudyItem { Title = "P", Description = "d", CreatedByUserId = 1 };
            ctx.StudyItems.Add(page);
            await ctx.SaveChangesAsync();

            for (var i = 0; i < 5; i++)
            {
                ctx.UserProgramStudyPageSchedules.Add(new UserProgramStudyPageSchedule
                {
                    UserProgramId = programId,
                    StudyItemId = page.Id,
                    StartDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                    IsCompleted = true,
                });
            }
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetUserProgramByIdAsync(User, programId);

        dto!.TotalPageCount.ShouldBe(5);
        dto.CompletedPageCount.ShouldBe(5);
        dto.ProgressPercentage.ShouldBe(100);
    }

    public void Dispose() => _db.Dispose();
}
