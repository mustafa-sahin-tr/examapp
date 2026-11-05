using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

/// <summary>Issue #703: beceri listesi ve sorunun bağlam temelli etiketleri. Yetki (soru sahibi/admin) denetleyicide.</summary>
public interface ISkillService
{
    Task<List<SkillDto>> ListAsync(CancellationToken ct = default);

    Task<QuestionContextTagsDto> GetForQuestionAsync(int questionId, CancellationToken ct = default);

    /// <summary>Listede olmayan bir beceri varsa hiçbir şey yazılmaz ve <c>false</c> döner.</summary>
    Task<bool> SetForQuestionAsync(int questionId, SetQuestionContextTagsDto request, int userId, CancellationToken ct = default);
}

public sealed class SkillService(AppDbContext context) : ISkillService
{
    public Task<List<SkillDto>> ListAsync(CancellationToken ct = default) =>
        context.Skills.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .Select(s => new SkillDto { Id = s.Id, Code = s.Code, Category = s.Category, Name = s.Name })
            .ToListAsync(ct);

    public async Task<QuestionContextTagsDto> GetForQuestionAsync(int questionId, CancellationToken ct = default)
    {
        var isContextBased = await context.Questions.AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => q.IsContextBased)
            .FirstOrDefaultAsync(ct);
        var skills = await context.QuestionSkills.AsNoTracking()
            .Where(link => link.QuestionId == questionId)
            .Select(link => link.Skill)
            .OrderBy(s => s.SortOrder)
            .Select(s => new SkillDto { Id = s.Id, Code = s.Code, Category = s.Category, Name = s.Name })
            .ToListAsync(ct);
        return new QuestionContextTagsDto { IsContextBased = isContextBased, Skills = skills };
    }

    public async Task<bool> SetForQuestionAsync(int questionId, SetQuestionContextTagsDto request, int userId, CancellationToken ct = default)
    {
        var wanted = request.SkillIds.Distinct().ToHashSet();
        if (await context.Skills.CountAsync(s => wanted.Contains(s.Id), ct) != wanted.Count)
            return false;

        var question = await context.Questions.FirstAsync(q => q.Id == questionId, ct);
        question.IsContextBased = request.IsContextBased;

        var links = await context.QuestionSkills.Where(link => link.QuestionId == questionId).ToListAsync(ct);
        context.QuestionSkills.RemoveRange(links.Where(link => !wanted.Contains(link.SkillId)));
        var linked = links.Select(link => link.SkillId).ToHashSet();
        context.QuestionSkills.AddRange(wanted.Where(id => !linked.Contains(id))
            .Select(id => new QuestionSkill { QuestionId = questionId, SkillId = id }));

        context.SetCurrentUser(userId);
        await context.SaveChangesAsync(ct);
        return true;
    }
}
