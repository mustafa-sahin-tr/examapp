using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

/// <summary>Issue #692: MEB kazanımları, resmî listeden içe aktarım ve soruya bağlama. Yetki çağıranın (controller) sorumluluğundadır.</summary>
public interface ILearningOutcomeService
{
    Task<List<LearningOutcomeDto>> SearchAsync(int gradeId, int subjectId, CurriculumVersion? version, string? search, CancellationToken ct = default);

    Task<List<LearningOutcomeDto>> GetForQuestionAsync(int questionId, CancellationToken ct = default);

    /// <summary>Sorunun bağlarını verilen listeyle değiştirir. Listede olmayan bir kazanım varsa hiçbir şey yazılmaz ve <c>false</c> döner.</summary>
    Task<bool> SetForQuestionAsync(int questionId, IReadOnlyCollection<int> learningOutcomeIds, int userId, CancellationToken ct = default);

    Task<LearningOutcomeImportResultDto> ImportAsync(LearningOutcomeImportDto request, CurriculumVersion version, int userId, CancellationToken ct = default);
}

public sealed class LearningOutcomeService(AppDbContext context) : ILearningOutcomeService
{
    private const int MaxSearchResults = 200;

    public async Task<List<LearningOutcomeDto>> SearchAsync(int gradeId, int subjectId, CurriculumVersion? version, string? search, CancellationToken ct = default)
    {
        var query = context.LearningOutcomes.AsNoTracking().Where(lo => lo.GradeId == gradeId && lo.SubjectId == subjectId);
        if (version.HasValue)
            query = query.Where(lo => lo.CurriculumVersion == version.Value);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term.ToLower().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(lo => EF.Functions.Like(lo.Code.ToLower(), pattern, "\\")
                || EF.Functions.Like(lo.Description.ToLower(), pattern, "\\"));
        }

        var rows = await query
            .OrderBy(lo => lo.CurriculumVersion).ThenBy(lo => lo.Code)
            .Take(MaxSearchResults)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<List<LearningOutcomeDto>> GetForQuestionAsync(int questionId, CancellationToken ct = default)
    {
        var rows = await context.QuestionLearningOutcomes.AsNoTracking()
            .Where(link => link.QuestionId == questionId)
            .Select(link => link.LearningOutcome)
            .OrderBy(lo => lo.CurriculumVersion).ThenBy(lo => lo.Code)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<bool> SetForQuestionAsync(int questionId, IReadOnlyCollection<int> learningOutcomeIds, int userId, CancellationToken ct = default)
    {
        var wanted = learningOutcomeIds.Distinct().ToHashSet();
        var existingCount = await context.LearningOutcomes.CountAsync(lo => wanted.Contains(lo.Id), ct);
        if (existingCount != wanted.Count)
            return false;

        var links = await context.QuestionLearningOutcomes.Where(link => link.QuestionId == questionId).ToListAsync(ct);
        context.QuestionLearningOutcomes.RemoveRange(links.Where(link => !wanted.Contains(link.LearningOutcomeId)));
        var linked = links.Select(link => link.LearningOutcomeId).ToHashSet();
        context.QuestionLearningOutcomes.AddRange(wanted.Where(id => !linked.Contains(id))
            .Select(id => new QuestionLearningOutcome { QuestionId = questionId, LearningOutcomeId = id }));

        context.SetCurrentUser(userId);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<LearningOutcomeImportResultDto> ImportAsync(LearningOutcomeImportDto request, CurriculumVersion version, int userId, CancellationToken ct = default)
    {
        var grades = await NameLookupAsync(context.Grades.AsNoTracking().Select(g => new IdName(g.Id, g.Name)), ct);
        var subjects = await NameLookupAsync(context.Subjects.AsNoTracking().Select(s => new IdName(s.Id, s.Name)), ct);

        var result = new LearningOutcomeImportResultDto();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<(string Code, string Description, int GradeId, int SubjectId)>();
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            var line = i + 1;
            var code = item.Code?.Trim() ?? string.Empty;
            var description = item.Description?.Trim() ?? string.Empty;
            if (code.Length == 0 || description.Length == 0)
                result.Errors.Add($"{line}: code and description are required");
            else if (!seenCodes.Add(code))
                result.Errors.Add($"{line}: duplicate code {code}");

            var hasGrade = grades.TryGetValue(item.GradeName?.Trim() ?? string.Empty, out var gradeId);
            if (!hasGrade)
                result.Errors.Add($"{line}: unknown grade {item.GradeName}");
            var hasSubject = subjects.TryGetValue(item.SubjectName?.Trim() ?? string.Empty, out var subjectId);
            if (!hasSubject)
                result.Errors.Add($"{line}: unknown subject {item.SubjectName}");

            rows.Add((code, description, gradeId, subjectId));
        }

        if (result.Errors.Count > 0)
            return result;

        var codes = rows.Select(r => r.Code).ToList();
        var existing = await context.LearningOutcomes
            .Where(lo => lo.CurriculumVersion == version && codes.Contains(lo.Code))
            .ToDictionaryAsync(lo => lo.Code, ct);

        foreach (var row in rows)
        {
            if (!existing.TryGetValue(row.Code, out var outcome))
            {
                context.LearningOutcomes.Add(new LearningOutcome
                {
                    Code = row.Code,
                    Description = row.Description,
                    GradeId = row.GradeId,
                    SubjectId = row.SubjectId,
                    CurriculumVersion = version,
                    SourceReference = request.SourceReference,
                });
                result.Created++;
            }
            else if (outcome.Description != row.Description || outcome.GradeId != row.GradeId || outcome.SubjectId != row.SubjectId
                || outcome.SourceReference != request.SourceReference)
            {
                outcome.Description = row.Description;
                outcome.GradeId = row.GradeId;
                outcome.SubjectId = row.SubjectId;
                outcome.SourceReference = request.SourceReference;
                result.Updated++;
            }
            else
            {
                result.Unchanged++;
            }
        }

        context.SetCurrentUser(userId);
        await context.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<string, int>> NameLookupAsync(IQueryable<IdName> source, CancellationToken ct)
    {
        var rows = await source.ToListAsync(ct);
        // Aynı adı taşıyan iki kayıt varsa hangisine yazılacağı belirsizdir; ad bilinmiyor sayılır.
        return rows.GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.OrdinalIgnoreCase);
    }

    private static LearningOutcomeDto ToDto(LearningOutcome lo) => new()
    {
        Id = lo.Id,
        Code = lo.Code,
        Description = lo.Description,
        CurriculumVersion = lo.CurriculumVersion.ToString(),
        GradeId = lo.GradeId,
        SubjectId = lo.SubjectId,
        SubTopicId = lo.SubTopicId,
    };

    private sealed record IdName(int Id, string Name);
}
