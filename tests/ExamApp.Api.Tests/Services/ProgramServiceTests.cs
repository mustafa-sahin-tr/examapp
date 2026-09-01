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

    [Fact]
    public async Task GetProgramSteps_returns_steps_ordered_with_options_and_actions()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.ProgramSteps.Add(new ProgramStep
            {
                Title = "İkinci", Description = "d", Order = 2, Multiple = true,
                Options = { new ProgramStepOption { Label = "A", Value = "a", Selected = true, Icon = "star" } },
            });
            ctx.ProgramSteps.Add(new ProgramStep
            {
                Title = "Birinci", Description = "d", Order = 1,
                Actions = { new ProgramStepAction { Label = "İleri", Value = "next" } },
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var steps = await NewService(ctx2).GetProgramStepsAsync();

        steps.Select(s => s.Title).ShouldBe(new[] { "Birinci", "İkinci" });
        steps[1].Options.ShouldHaveSingleItem().Selected.ShouldBeTrue();
        steps[0].Actions.ShouldHaveSingleItem().Value.ShouldBe("next");
    }

    [Fact]
    public async Task GetUserPrograms_is_scoped_to_the_user()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserPrograms.Add(Program("kc-1", "Benim Programım"));
            ctx.UserPrograms.Add(Program("kc-2", "Başkasının"));
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var mine = await NewService(ctx2).GetUserProgramsAsync("kc-1");
        mine.ShouldHaveSingleItem().ProgramName.ShouldBe("Benim Programım");
    }

    [Fact]
    public async Task GetUserProgramById_enforces_ownership()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var p = Program("kc-1", "P");
            ctx.UserPrograms.Add(p);
            await ctx.SaveChangesAsync();
            id = p.Id;
        }

        await using var ctx2 = _db.NewContext();
        var svc = NewService(ctx2);
        (await svc.GetUserProgramByIdAsync("kc-1", id)).ShouldNotBeNull();
        (await svc.GetUserProgramByIdAsync("kc-2", id)).ShouldBeNull();
        (await svc.GetUserProgramByIdAsync("kc-1", 9999)).ShouldBeNull();
    }

    [Fact]
    public async Task CreateUserProgram_persists_a_program_and_returns_its_dto()
    {
        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).CreateUserProgramAsync("kc-9", new CreateProgramRequestDto
        {
            ProgramName = "30 Günlük Plan",
            Description = "açıklama",
            StartDate = "2026-04-01",
            EndDate = "2026-04-30",
            UserSelections = new(),
        });

        dto.ProgramName.ShouldBe("30 Günlük Plan");
        (await _db.NewContext().UserPrograms.CountAsync(p => p.UserId == "kc-9")).ShouldBe(1);
    }

    private static UserProgram Program(string userId, string name) => new()
    {
        UserId = userId, ProgramName = name, Description = "d",
        StudyType = "time", StudyDuration = "25-5", SubjectsPerDay = 2, RestDays = "6,7", DifficultSubjects = "",
        StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(30),
    };

    public void Dispose() => _db.Dispose();
}
