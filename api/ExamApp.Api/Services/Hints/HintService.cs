using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Questions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Hints;

public enum HintError
{
    None,
    /// <summary>Öğrencinin bu soruda yanlış ya da boş cevabı yok (ya da soru yok) → 404.</summary>
    NotEligible,
    /// <summary>Çoktan seçmeli değil (doğru şık yok) → 400.</summary>
    NotSupported,
    NotInPlan,
    QuotaReached,
}

public sealed class HintStepDto
{
    public int Step { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Issue #1113: öğrencinin bir sorudaki ipucu durumu. <c>TotalSteps</c> ipucu üretilmeden null; <c>Unavailable</c> Claude bu soruda ipucu
/// veremedi. <c>Remaining</c> aylık haktan kalan (sınırsızda null).
/// </summary>
public sealed class HintStateDto
{
    public int QuestionId { get; set; }
    public List<HintStepDto> Steps { get; set; } = new();
    public int? TotalSteps { get; set; }
    public bool Unavailable { get; set; }
    public int? Remaining { get; set; }
}

public sealed record HintResult(HintError Error, HintStateDto? State = null, EntitlementCheck? Quota = null);

public interface IHintService
{
    Task<HintResult> GetAsync(EntitlementSubject subject, int questionId, CancellationToken ct);

    /// <summary>
    /// Sıradaki adımı açar: ipucu yoksa üretir (Claude kotası ve bütçesi), ilk kez açılan adım <c>step-hints</c> hakkından düşer.
    /// Adımlar bittiyse ya da ipucu yoksa durum değişmeden döner.
    /// </summary>
    Task<HintResult> RevealNextAsync(EntitlementSubject subject, LabelingActor actor, int questionId, CancellationToken ct);
}

/// <summary>Claude yanıt veremedi ya da yanıtı kullanılamadı → 502.</summary>
public sealed class HintGenerationException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Issue #1113 (epic #1118): yanlış ya da boş bırakılan soruda adım adım ipucu (Pro). İpuçları soru başına bir kez üretilir ve bütün
/// öğrenciler paylaşır (<see cref="QuestionHintSet"/>); öğrenci adımları sırayla açar.
/// </summary>
public sealed class HintService(
    AppDbContext db,
    IHintGenerator generator,
    IEntitlementService entitlements,
    IQuestionClassificationService classification,
    IMinIoService minio,
    IFixedWindowCounterStore rateLimitStore,
    IOptions<LabelingAiOptions> options,
    TimeProvider clock,
    ILogger<HintService> logger) : IHintService
{
    private const int MaxSteps = 3;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(1);

    private sealed record QuestionInfo(int Id, string? ImageUrl, int? CorrectAnswerId, List<(int Id, string? Text)> Answers, string? SubjectName);

    public async Task<HintResult> GetAsync(EntitlementSubject subject, int questionId, CancellationToken ct)
    {
        if (!await IsEligibleAsync(subject.UserId, questionId, ct))
            return new(HintError.NotEligible);
        var check = await entitlements.CheckAsync(subject, EntitlementFeatures.StepHints, ct);
        return new(HintError.None, await StateAsync(subject.UserId, questionId, check, ct), check);
    }

    public async Task<HintResult> RevealNextAsync(EntitlementSubject subject, LabelingActor actor, int questionId, CancellationToken ct)
    {
        if (!await IsEligibleAsync(subject.UserId, questionId, ct))
            return new(HintError.NotEligible);
        var question = await ReadQuestionAsync(questionId, ct);
        if (question?.CorrectAnswerId is null || question.Answers.All(a => a.Id != question.CorrectAnswerId))
            return new(HintError.NotSupported);

        var check = await entitlements.CheckAsync(subject, EntitlementFeatures.StepHints, ct);
        if (check.Limit == 0)
            return new(HintError.NotInPlan, Quota: check);

        var set = await ReadSetAsync(questionId, ct);
        var revealed = await RevealedStepsAsync(subject.UserId, questionId, ct);
        if (set is not null && (set.Unavailable || revealed >= set.Steps.Count))
            return new(HintError.None, await StateAsync(subject.UserId, questionId, check, ct), check);
        if (!check.Allowed)
            return new(HintError.QuotaReached, Quota: check);

        set ??= await GenerateAsync(subject.UserId, actor, question, ct);
        if (!set.Unavailable && revealed < set.Steps.Count)
        {
            var step = revealed + 1;
            db.StudentHintReveals.Add(new StudentHintReveal
            {
                StudentUserId = subject.UserId,
                QuestionId = questionId,
                Step = step,
                RevealedAtUtc = clock.GetUtcNow().UtcDateTime,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                await entitlements.RecordUsageAsync(subject.UserId, EntitlementFeatures.StepHints, $"hint:{subject.UserId}:{questionId}:{step}", ct);
            }
            catch (DbUpdateException)
            {
                // Hızlı çift tık: aynı adım ikinci kez açılmaz, ilk istek saymıştır.
                db.ChangeTracker.Clear();
            }
            check = await entitlements.CheckAsync(subject, EntitlementFeatures.StepHints, ct);
        }
        return new(HintError.None, await StateAsync(subject.UserId, questionId, check, ct), check);
    }

    /// <summary>Öğrencinin soruda yanlış ya da boş cevabı var mı: pratikte cevaplanmış yanlış, tamamlanmış testte doğru olmayan.</summary>
    private async Task<bool> IsEligibleAsync(int userId, int questionId, CancellationToken ct)
    {
        var studentId = await db.Students.AsNoTracking().Where(s => s.UserId == userId).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (studentId is null)
            return false;
        return await db.PracticeSessionQuestions.AsNoTracking()
                   .AnyAsync(p => p.PracticeSession.StudentId == studentId && p.QuestionId == questionId && p.AnsweredAt != null && !p.IsCorrect, ct)
               || await db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
                   .AnyAsync(q => q.WorksheetInstance.StudentId == studentId
                       && q.WorksheetInstance.Status == WorksheetInstanceStatus.Completed
                       && q.WorksheetQuestion.QuestionId == questionId
                       && !q.IsCorrect, ct);
    }

    private async Task<HintStateDto> StateAsync(int userId, int questionId, EntitlementCheck? check, CancellationToken ct)
    {
        var set = await ReadSetAsync(questionId, ct);
        var revealed = await RevealedStepsAsync(userId, questionId, ct);
        return new HintStateDto
        {
            QuestionId = questionId,
            TotalSteps = set is null || set.Unavailable ? null : set.Steps.Count,
            Unavailable = set?.Unavailable == true,
            Steps = set is null ? new() : set.Steps.Where(s => s.Step <= revealed).OrderBy(s => s.Step)
                .Select(s => new HintStepDto { Step = s.Step, Text = s.Text }).ToList(),
            Remaining = check?.Remaining,
        };
    }

    private Task<QuestionHintSet?> ReadSetAsync(int questionId, CancellationToken ct) =>
        db.QuestionHintSets.AsNoTracking().Include(h => h.Steps)
            .FirstOrDefaultAsync(h => h.QuestionId == questionId && h.SchemaRevision == ClaudeHintGenerator.SchemaRevision, ct);

    private Task<int> RevealedStepsAsync(int userId, int questionId, CancellationToken ct) =>
        db.StudentHintReveals.AsNoTracking().CountAsync(r => r.StudentUserId == userId && r.QuestionId == questionId, ct);

    private async Task<QuestionInfo?> ReadQuestionAsync(int questionId, CancellationToken ct)
    {
        var row = await db.Questions.AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => new
            {
                q.Id,
                q.ImageUrl,
                q.CorrectAnswerId,
                Answers = q.Answers.OrderBy(a => a.Order ?? int.MaxValue).ThenBy(a => a.Id).Select(a => new { a.Id, a.Text }).ToList(),
                SubjectName = q.Subject != null ? q.Subject.Name : null,
            })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new QuestionInfo(row.Id, row.ImageUrl, row.CorrectAnswerId,
            row.Answers.Select(a => (a.Id, a.Text)).ToList(), row.SubjectName);
    }

    private async Task<QuestionHintSet> GenerateAsync(int userId, LabelingActor actor, QuestionInfo question, CancellationToken ct)
    {
        var model = options.Value.Models.FirstOrDefault(m => m.IsDefault) ?? options.Value.Models.FirstOrDefault();
        if (!generator.IsConfigured || model is null)
            throw new LabelingAiUnavailableException();

        var correctIndex = question.Answers.FindIndex(a => a.Id == question.CorrectAnswerId);
        var correctLetter = ((char)('A' + correctIndex)).ToString();
        var correctText = QuestionClassificationText.ToPlainText(question.Answers[correctIndex].Text);
        var prompt = new HintPrompt(await ReadImageAsync(question.ImageUrl, ct), await classification.GetClassificationTextAsync(question.Id, ct),
            correctLetter, correctText, await GradeNameAsync(userId, ct), question.SubjectName);

        var estimate = LabelingAiQuota.Cost(model, ClaudeHintGenerator.WorstCaseInputTokens, ClaudeHintGenerator.MaxOutputTokens);
        var reserved = await LabelingAiQuota.ReserveBudgetAsync(rateLimitStore, actor, options.Value, estimate, ct);
        try
        {
            await LabelingAiQuota.AcquireCallsAsync(rateLimitStore, actor, options.Value, 1, ct);
        }
        catch
        {
            await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, 0m);
            throw;
        }

        using var timeout = new CancellationTokenSource(CallTimeout);
        GeneratedHints generated;
        LabelingAiUsage usage;
        try
        {
            (generated, usage) = await generator.GenerateAsync(prompt, model, timeout.Token);
        }
        catch (Exception ex) when (ex is LabelingAiException or OperationCanceledException)
        {
            var spent = (ex as LabelingAiException)?.Usage is { } paid ? LabelingAiQuota.Cost(model, paid.Input, paid.Output) : estimate;
            await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, spent);
            throw new HintGenerationException("Hints could not be generated.", ex);
        }
        await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, LabelingAiQuota.Cost(model, usage.Input, usage.Output));

        var steps = generated.CannotHelp ? new List<string>() : (generated.Steps ?? new()).Select(Clean)
            .Where(s => s.Length > 0)
            .Take(MaxSteps)
            .ToList();
        var leaked = steps.Count(s => HintLeakGuard.Leaks(s, correctLetter, correctText));
        if (leaked > 0)
        {
            // Cevabı açık eden adımdan sonrası da ona dayanır: hepsi düşer.
            logger.LogWarning("[Hints] {Count} hint step(s) for question {QuestionId} revealed the answer; the set is dropped", leaked, question.Id);
            steps.Clear();
        }

        var set = new QuestionHintSet
        {
            QuestionId = question.Id,
            SchemaRevision = ClaudeHintGenerator.SchemaRevision,
            Model = model.Id,
            Unavailable = steps.Count == 0,
            GeneratedAtUtc = clock.GetUtcNow().UtcDateTime,
            Steps = steps.Select((text, i) => new QuestionHintStep { Step = i + 1, Text = text }).ToList(),
        };
        db.QuestionHintSets.Add(set);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı soruyu eşzamanlı isteyen başka öğrenci önce yazdı: onunki kullanılır.
            db.ChangeTracker.Clear();
            return await ReadSetAsync(question.Id, ct) ?? throw new InvalidOperationException("Hint set vanished after a conflict.");
        }
        db.ChangeTracker.Clear();
        return set;
    }

    private async Task<EncodedRegion?> ReadImageAsync(string? imageUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;
        try
        {
            await using var stream = await minio.GetFileStreamAsync(imageUrl);
            if (stream is null)
                return null;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            using var image = LabelingImage.Decode(buffer.ToArray());
            return image.Encode(ClaudeHintGenerator.MaxImageEdge);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Görsel okunamazsa metinle denenir (#590 görselsiz soru gibi).
            logger.LogWarning(ex, "[Hints] Question image could not be read");
            return null;
        }
    }

    private Task<string?> GradeNameAsync(int userId, CancellationToken ct) =>
        db.Students.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.Grade != null ? s.Grade.Name : null).FirstOrDefaultAsync(ct);

    private static string Clean(string? value)
    {
        var text = new string((value ?? string.Empty).Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        return text.Length > QuestionHintStep.MaxTextLength ? text[..QuestionHintStep.MaxTextLength] : text;
    }
}
