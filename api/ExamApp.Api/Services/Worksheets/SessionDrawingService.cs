using System.Text.Json;
using System.Text.RegularExpressions;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>Issue #1310: çizim sınırları. İstemcinin kalem biçimi ve not uzunluğu (`test-solve-scratch-store.ts`) ile aynı.</summary>
public static partial class SessionDrawingRules
{
    public const int TextMaxLength = 2000;
    /// <summary>Soru başına iki alanın çizgileri toplamı (karakter). Aşan çizgiler sondan düşer, istek reddedilmez.</summary>
    public const int MaxCharsPerQuestion = 64_000;
    public const int MaxItemsPerRequest = 200;
    /// <summary>Test bittikten sonra son yüklemenin kabul edildiği süre (bitirme ile son yükleme yarışı, süre dolması).</summary>
    public static readonly TimeSpan GraceAfterFinish = TimeSpan.FromMinutes(10);

    [GeneratedRegex(@"^(?:(?:red|green|blue|orange):)?M[\d L]+$")]
    private static partial Regex StrokePattern();

    /// <summary>Biçime uymayan çizgiyi atar; toplam sınırı aşan çizgileri (önce karalama alanından, sondan) düşürür.</summary>
    public static (List<string> Ink, List<string> Strokes) Sanitize(IEnumerable<string>? ink, IEnumerable<string>? strokes)
    {
        var cleanInk = (ink ?? []).Where(s => s is { Length: > 0 } && StrokePattern().IsMatch(s)).ToList();
        var cleanStrokes = (strokes ?? []).Where(s => s is { Length: > 0 } && StrokePattern().IsMatch(s)).ToList();
        var total = cleanInk.Sum(s => s.Length) + cleanStrokes.Sum(s => s.Length);
        while (total > MaxCharsPerQuestion && cleanStrokes.Count > 0)
        {
            total -= cleanStrokes[^1].Length;
            cleanStrokes.RemoveAt(cleanStrokes.Count - 1);
        }
        while (total > MaxCharsPerQuestion && cleanInk.Count > 0)
        {
            total -= cleanInk[^1].Length;
            cleanInk.RemoveAt(cleanInk.Count - 1);
        }
        return (cleanInk, cleanStrokes);
    }

    public static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length > TextMaxLength ? text[..TextMaxLength] : text;
    }

    /// <summary>
    /// Çözülürken her zaman; bittikten sonra <see cref="GraceAfterFinish"/> kadar. Süresi dolup henüz kapatılmamış oturumun
    /// bitişi süre sınırının sonu sayılır.
    /// </summary>
    public static bool AcceptsUpload(WorksheetInstanceStatus effectiveStatus, DateTime startTime, DateTime? endTime,
        int? maxDurationSeconds, DateTime nowUtc)
    {
        if (effectiveStatus == WorksheetInstanceStatus.Started) return true;
        var end = endTime ?? (maxDurationSeconds is { } limit ? startTime.AddSeconds(limit) : (DateTime?)null);
        return end is { } finished && nowUtc - finished <= GraceAfterFinish;
    }
}

public enum SaveSessionDrawingsResult
{
    Saved,
    NotFound,
    Closed,
}

public interface ISessionDrawingService
{
    Task<SaveSessionDrawingsResult> SaveAsync(int testInstanceId, int studentUserId, SaveSessionDrawingsDto dto, CancellationToken ct = default);

    /// <summary>Bitmiş oturumun soru soru görünümü. Yetki çağıranda (öğretmen kapsamı / veli kapsamı) denetlenir.</summary>
    Task<SessionReviewDto?> BuildReviewAsync(int testInstanceId, CancellationToken ct = default);
}

public sealed class SessionDrawingService(AppDbContext context, TimeProvider time) : ISessionDrawingService
{
    public async Task<SaveSessionDrawingsResult> SaveAsync(int testInstanceId, int studentUserId, SaveSessionDrawingsDto dto,
        CancellationToken ct = default)
    {
        var instance = await context.TestInstances.AsNoTracking()
            .Where(ti => ti.Id == testInstanceId && ti.Student.UserId == studentUserId)
            .Select(ti => new { ti.Id, ti.Status, ti.StartTime, ti.EndTime, ti.MaxDurationSeconds })
            .FirstOrDefaultAsync(ct);
        if (instance == null)
            return SaveSessionDrawingsResult.NotFound;

        var now = time.GetUtcNow().UtcDateTime;
        var status = TestTimeLimit.EffectiveStatus(instance.Status, instance.StartTime, instance.MaxDurationSeconds, now);
        if (!SessionDrawingRules.AcceptsUpload(status, instance.StartTime, instance.EndTime, instance.MaxDurationSeconds, now))
            return SaveSessionDrawingsResult.Closed;

        var items = (dto.Items ?? []).Take(SessionDrawingRules.MaxItemsPerRequest)
            .GroupBy(i => i.InstanceQuestionId).Select(g => g.Last()).ToList();
        if (items.Count == 0)
            return SaveSessionDrawingsResult.Saved;

        var ids = items.Select(i => i.InstanceQuestionId).ToList();
        // Yalnız bu oturumun satırları; başka oturumun id'si sessizce atlanır.
        var ownIds = await context.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.WorksheetInstanceId == instance.Id && ids.Contains(q.Id))
            .Select(q => q.Id)
            .ToListAsync(ct);
        var existing = await context.TestInstanceQuestionDrawings
            .Where(d => ownIds.Contains(d.WorksheetInstanceQuestionId))
            .ToDictionaryAsync(d => d.WorksheetInstanceQuestionId, ct);

        foreach (var item in items.Where(i => ownIds.Contains(i.InstanceQuestionId)))
        {
            var (ink, strokes) = SessionDrawingRules.Sanitize(item.Ink, item.Strokes);
            var text = SessionDrawingRules.CleanText(item.Text);
            existing.TryGetValue(item.InstanceQuestionId, out var row);
            if (ink.Count == 0 && strokes.Count == 0 && text == null)
            {
                if (row != null) context.TestInstanceQuestionDrawings.Remove(row);
                continue;
            }
            if (row == null)
            {
                row = new WorksheetInstanceQuestionDrawing { WorksheetInstanceQuestionId = item.InstanceQuestionId };
                context.TestInstanceQuestionDrawings.Add(row);
            }
            row.Ink = JsonSerializer.Serialize(ink);
            row.ScratchStrokes = JsonSerializer.Serialize(strokes);
            row.ScratchText = text;
            row.UpdatedAt = now;
        }

        await context.SaveChangesAsync(ct);
        return SaveSessionDrawingsResult.Saved;
    }

    public async Task<SessionReviewDto?> BuildReviewAsync(int testInstanceId, CancellationToken ct = default)
    {
        var instance = await context.TestInstances.AsNoTracking()
            .Where(ti => ti.Id == testInstanceId)
            .Select(ti => new { ti.Id, ti.Status, ti.StartTime, ti.EndTime, ti.MaxDurationSeconds, Title = ti.Worksheet.Name })
            .FirstOrDefaultAsync(ct);
        if (instance == null)
            return null;
        var now = time.GetUtcNow().UtcDateTime;
        // Öğrenci çözerken izlenmez: yalnız bitmiş oturum.
        if (!WorksheetInstanceStatusRules.IsFinished(
                TestTimeLimit.EffectiveStatus(instance.Status, instance.StartTime, instance.MaxDurationSeconds, now)))
            return null;

        var rows = await context.TestInstanceQuestions.AsNoTracking()
            .WithRemovedWorksheetItems()
            .Where(q => q.WorksheetInstanceId == testInstanceId)
            .OrderBy(q => q.Order).ThenBy(q => q.Id)
            .Select(q => new
            {
                q.Id,
                q.SelectedAnswerId,
                SelectedTag = q.SelectedAnswer != null ? q.SelectedAnswer.Tag : null,
                HasPayload = q.AnswerPayload != null,
                q.IsCorrect,
                q.TimeTaken,
                ImageUrl = q.WorksheetQuestion.Question.ImageUrl,
                q.WorksheetQuestion.Question.Text,
                q.WorksheetQuestion.Question.InteractionType,
                q.WorksheetQuestion.Question.CorrectAnswerId,
                CorrectTag = q.WorksheetQuestion.Question.CorrectAnswer != null ? q.WorksheetQuestion.Question.CorrectAnswer.Tag : null,
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var drawings = await context.TestInstanceQuestionDrawings.AsNoTracking()
            .Where(d => ids.Contains(d.WorksheetInstanceQuestionId))
            .ToDictionaryAsync(d => d.WorksheetInstanceQuestionId, ct);

        return new SessionReviewDto
        {
            TestInstanceId = instance.Id,
            Title = instance.Title,
            StartedAt = instance.StartTime,
            FinishedAt = instance.EndTime,
            Questions = rows.Select((r, index) =>
            {
                drawings.TryGetValue(r.Id, out var d);
                return new SessionReviewQuestionDto
                {
                    InstanceQuestionId = r.Id,
                    Number = index + 1,
                    ImageUrl = r.ImageUrl,
                    QuestionText = string.IsNullOrEmpty(r.ImageUrl) ? r.Text : null,
                    InteractionType = r.InteractionType,
                    SelectedLetter = r.SelectedTag,
                    CorrectLetter = r.CorrectTag,
                    Outcome = WorksheetScoring.IsBlank(r.SelectedAnswerId, r.HasPayload) ? "blank"
                        : WorksheetScoring.IsCorrect(r.SelectedAnswerId, r.CorrectAnswerId, r.HasPayload, r.IsCorrect) ? "correct"
                        : "wrong",
                    TimeTakenSeconds = r.TimeTaken,
                    Ink = Parse(d?.Ink),
                    Strokes = Parse(d?.ScratchStrokes),
                    Text = d?.ScratchText,
                };
            }).ToList(),
        };
    }

    private static List<string> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
