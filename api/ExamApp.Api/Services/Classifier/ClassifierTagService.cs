using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Classifier;

/// <summary>
/// Issue #1047: learning outcome (#692) and skill (#703) links written by the question classifier. The classifier only
/// picks from <see cref="GetOptionsAsync"/>; <see cref="SetAsync"/> checks the pick against the same lists, so links a human
/// set are never replaced (a kind that already has links offers an empty list).
/// </summary>
public interface IClassifierTagService
{
    /// <summary><see langword="null"/> = question not found. <paramref name="gradeId"/> is used when the topic has none.</summary>
    Task<ClassifierTagOptionsDto?> GetOptionsAsync(int questionId, int? gradeId, CancellationToken ct = default);

    /// <summary><see langword="null"/> = question not found; <see langword="false"/> = an id outside the offered lists.</summary>
    Task<bool?> SetAsync(int questionId, int? gradeId, SetClassifierTagsDto request, CancellationToken ct = default);
}

public sealed class ClassifierTagService(AppDbContext context) : IClassifierTagService
{
    public const int MaxLearningOutcomes = 3;
    public const int MaxSkills = 2;

    public async Task<ClassifierTagOptionsDto?> GetOptionsAsync(int questionId, int? gradeId, CancellationToken ct = default)
    {
        // Book import drafts are classified before approval (issue #1022).
        var question = await context.Questions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => new
            {
                q.SubjectId,
                TopicSubjectId = q.Topic != null ? (int?)q.Topic.SubjectId : null,
                TopicGradeId = q.Topic != null ? (int?)q.Topic.GradeId : null,
                HasOutcomes = q.QuestionLearningOutcomes.Any(),
                HasSkills = q.QuestionSkills.Any(),
            })
            .FirstOrDefaultAsync(ct);
        if (question is null)
            return null;

        var options = new ClassifierTagOptionsDto();

        var subjectId = question.SubjectId ?? question.TopicSubjectId;
        var grade = question.TopicGradeId ?? (gradeId is > 0 ? gradeId : null);
        if (!question.HasOutcomes && subjectId.HasValue && grade.HasValue)
        {
            var outcomes = context.LearningOutcomes.AsNoTracking()
                .Where(lo => lo.GradeId == grade.Value && lo.SubjectId == subjectId.Value);
            // Both curricula live side by side during the transition (#692): offer the Maarif list when the pair has one.
            var version = await outcomes.AnyAsync(lo => lo.CurriculumVersion == CurriculumVersion.Maarif, ct)
                ? CurriculumVersion.Maarif
                : CurriculumVersion.Previous;
            options.LearningOutcomes = await outcomes
                .Where(lo => lo.CurriculumVersion == version)
                .OrderBy(lo => lo.Code)
                .Select(lo => new ClassifierOutcomeOptionDto { Id = lo.Id, Code = lo.Code, Description = lo.Description })
                .ToListAsync(ct);
        }

        if (!question.HasSkills)
        {
            options.Skills = await context.Skills.AsNoTracking()
                .OrderBy(s => s.SortOrder)
                .Select(s => new ClassifierSkillOptionDto { Id = s.Id, Code = s.Code, Name = s.Name })
                .ToListAsync(ct);
        }

        return options;
    }

    public async Task<bool?> SetAsync(int questionId, int? gradeId, SetClassifierTagsDto request, CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(questionId, gradeId, ct);
        if (options is null)
            return null;

        var outcomeIds = request.LearningOutcomeIds.Distinct().ToList();
        var skillIds = request.SkillIds.Distinct().ToList();
        var offeredOutcomes = options.LearningOutcomes.Select(o => o.Id).ToHashSet();
        var offeredSkills = options.Skills.Select(s => s.Id).ToHashSet();
        if (outcomeIds.Count > MaxLearningOutcomes || skillIds.Count > MaxSkills
            || !outcomeIds.All(offeredOutcomes.Contains) || !skillIds.All(offeredSkills.Contains))
            return false;

        context.QuestionLearningOutcomes.AddRange(outcomeIds
            .Select(id => new QuestionLearningOutcome { QuestionId = questionId, LearningOutcomeId = id }));
        context.QuestionSkills.AddRange(skillIds.Select(id => new QuestionSkill { QuestionId = questionId, SkillId = id }));

        await context.SaveChangesAsync(ct);
        return true;
    }
}
