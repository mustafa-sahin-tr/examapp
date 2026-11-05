using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #703: TYMM beceri listesi, sorunun bağlam temelli etiketleri ve aktarımda kodla eşleşme.</summary>
public class SkillServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private async Task<int> SeedQuestionAsync()
    {
        await using var ctx = _db.NewContext();
        var question = new Question { Text = "?", Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        return question.Id;
    }

    private async Task<int> SkillIdAsync(string code)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Skills.Where(s => s.Code == code).Select(s => s.Id).SingleAsync();
    }

    [Fact]
    public async Task List_HasTwentyIntegratedAndThreeHigherOrderSkills_InGuideOrder()
    {
        await using var ctx = _db.NewContext();
        var skills = await new SkillService(ctx).ListAsync();

        skills.Count.ShouldBe(23);
        skills.Count(s => s.Category == SkillCategory.Integrated).ShouldBe(20);
        skills.Where(s => s.Category == SkillCategory.HigherOrder).Select(s => s.Code).ShouldBe(new[] { "KB3.1", "KB3.2", "KB3.3" });
        // Kılavuzun örnek sorularındaki kodlar.
        skills.Single(s => s.Code == "KB2.3").Name.ShouldBe("Özetleme");
        skills.Single(s => s.Code == "KB2.8").Name.ShouldBe("Sorgulama");
        skills.Single(s => s.Code == "KB2.14").Name.ShouldBe("Yorumlama");
        skills.Single(s => s.Code == "KB3.2").Name.ShouldBe("Problem çözme");
        skills.Select(s => s.Code).Take(3).ShouldBe(new[] { "KB2.1", "KB2.2", "KB2.3" });
    }

    [Fact]
    public async Task SetForQuestion_ReplacesSkillsAndFlag_AndCanRelinkRemovedSkill()
    {
        var questionId = await SeedQuestionAsync();
        var inquiry = await SkillIdAsync("KB2.8");
        var problemSolving = await SkillIdAsync("KB3.2");

        async Task<QuestionContextTagsDto> SetAsync(bool contextBased, params int[] ids)
        {
            await using var ctx = _db.NewContext();
            var service = new SkillService(ctx);
            (await service.SetForQuestionAsync(questionId, new SetQuestionContextTagsDto { IsContextBased = contextBased, SkillIds = ids.ToList() }, 1)).ShouldBeTrue();
            return await service.GetForQuestionAsync(questionId);
        }

        var tags = await SetAsync(true, problemSolving, inquiry, inquiry);
        tags.IsContextBased.ShouldBeTrue();
        tags.Skills.Select(s => s.Code).ShouldBe(new[] { "KB2.8", "KB3.2" });

        (await SetAsync(true, problemSolving)).Skills.Select(s => s.Code).ShouldBe(new[] { "KB3.2" });
        (await SetAsync(false, inquiry, problemSolving)).Skills.Select(s => s.Code).ShouldBe(new[] { "KB2.8", "KB3.2" });

        tags = await SetAsync(false);
        tags.IsContextBased.ShouldBeFalse();
        tags.Skills.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetForQuestion_UnknownSkill_WritesNothing()
    {
        var questionId = await SeedQuestionAsync();
        var inquiry = await SkillIdAsync("KB2.8");

        await using var ctx = _db.NewContext();
        var service = new SkillService(ctx);
        (await service.SetForQuestionAsync(questionId, new SetQuestionContextTagsDto { IsContextBased = true, SkillIds = [inquiry, 9999] }, 1)).ShouldBeFalse();

        var tags = await service.GetForQuestionAsync(questionId);
        tags.IsContextBased.ShouldBeFalse();
        tags.Skills.ShouldBeEmpty();
    }

    [Fact]
    public async Task Transfer_ResolvesSkillsByCode_SkipsUnknown()
    {
        var inquiry = await SkillIdAsync("KB2.8");
        var json = JsonDocument.Parse("""["KB2.8", "KB2.8", "KB9.9", 3]""");

        await using var ctx = _db.NewContext();
        (await QuestionTransferJobRunner.ResolveSkillsAsync(ctx, json.RootElement)).ShouldBe(new[] { inquiry });
        QuestionTransferJobRunner.IsContextBasedInPackage(JsonDocument.Parse("""{ "isContextBased": true }""").RootElement).ShouldBeTrue();
        QuestionTransferJobRunner.IsContextBasedInPackage(JsonDocument.Parse("""{ "isContextBased": "yes" }""").RootElement).ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
