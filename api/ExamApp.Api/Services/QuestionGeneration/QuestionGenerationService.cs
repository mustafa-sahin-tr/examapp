using System.Text.Json;
using System.Text.RegularExpressions;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Labeling;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.QuestionGeneration;

public interface IQuestionGenerationService
{
    Task<QuestionDraftsResponseDto?> GenerateAsync(GenerateQuestionDraftsDto request, LabelingActor actor, CancellationToken ct);

    Task<SavedQuestionDraftsDto?> SaveAsync(SaveQuestionDraftsDto request, LabelingActor actor, int userId, CancellationToken ct);
}

/// <summary>Taslak ya da kaydedilecek soru kurallara uymuyor.</summary>
public sealed class QuestionDraftInvalidException(string message) : Exception(message);

/// <summary>Claude yanıt veremedi ya da yanıtı kullanılamadı.</summary>
public sealed class QuestionDraftGenerationException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Kazanımdan görselsiz soru: üretim işaretleme ekranının Claude kotası ve bütçesini kullanır, taslak veritabanına yazılmaz.
/// Kayıt ayrı adımdır: incelenen sorular şıkları, kazanım bağı ve sınıflandırma olayıyla birlikte tek işlemde yazılır.
/// </summary>
public sealed class QuestionGenerationService(
    AppDbContext db,
    IQuestionDraftGenerator generator,
    IFixedWindowCounterStore rateLimitStore,
    IOptions<LabelingAiOptions> options) : IQuestionGenerationService
{
    internal const int MaxTextLength = 2000;
    internal const int MaxOptionLength = 500;
    private const int QuestionPoint = 5;
    private const int AnswerColumns = 1;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(2);

    // Görsel olmadan anlaşılmayan soru öğrenciye eksik görünür.
    private static readonly Regex ReferenceToVisual = new(
        // "Periyodik tabloda", "ölçekli haritada", "aşağıdaki şekilde" (= bu biçimde), "resmi kurumlar", "görsel sanatlar" atıf değildir.
        @"\b(yukarıdaki|aşağıdaki|yandaki|verilen)\s+(görsel(?!\s+sanat)|şek(il(?!de\b)|l)|tablo|grafi[kğ]|resim|resm(e|in)\b|harita)\w*|\b(görselde|resimde|grafikte)\b|\b(şekilde|tabloda|haritada)\s+(verilen|gösterilen|görülen)\b|\b(the\s+)?(picture|image|table|chart|graph|map)\s+(above|below)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InvisibleCharacters = new(@"[\u200B-\u200D\u2060\uFEFF]", RegexOptions.Compiled);

    private sealed record OutcomeInfo(int Id, string Code, string Description, int GradeId, string GradeName, int? SubjectId, string? SubjectName);

    public async Task<QuestionDraftsResponseDto?> GenerateAsync(GenerateQuestionDraftsDto request, LabelingActor actor, CancellationToken ct)
    {
        var outcome = await ReadOutcomeAsync(request.LearningOutcomeId, ct);
        if (outcome is null)
            return null;

        var model = options.Value.Models.FirstOrDefault(m => m.IsDefault) ?? options.Value.Models.FirstOrDefault();
        if (!generator.IsConfigured || model is null)
            throw new LabelingAiUnavailableException();

        var estimate = LabelingAiQuota.Cost(model, ClaudeQuestionDraftGenerator.WorstCaseInputTokens, ClaudeQuestionDraftGenerator.MaxOutputTokens);
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

        var optionCount = OptionCountFor(outcome.GradeName);
        var prompt = new QuestionDraftPrompt(outcome.Code, outcome.Description, outcome.GradeName, outcome.SubjectName, request.Count, optionCount, request.DifficultyLevel);

        // İstek iptali çağrıyı kesmez: token ödenir, bütçe gerçek maliyete düzeltilir.
        using var timeout = new CancellationTokenSource(CallTimeout);
        GeneratedQuestionSet generated;
        LabelingAiUsage usage;
        try
        {
            (generated, usage) = await generator.GenerateAsync(prompt, model, timeout.Token);
        }
        catch (Exception ex) when (ex is LabelingAiException or OperationCanceledException)
        {
            // Kullanım bilinmiyorsa (zaman aşımı, bağlantı) Claude yine ücretlendirmiş olabilir: ayrılan bütçe iade edilmez.
            var spent = (ex as LabelingAiException)?.Usage is { } paid ? LabelingAiQuota.Cost(model, paid.Input, paid.Output) : estimate;
            await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, spent);
            throw new QuestionDraftGenerationException("Question drafts could not be generated.", ex);
        }
        await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, LabelingAiQuota.Cost(model, usage.Input, usage.Output));

        var response = new QuestionDraftsResponseDto
        {
            LearningOutcomeId = outcome.Id,
            OutcomeCode = outcome.Code,
            OutcomeDescription = outcome.Description,
        };
        var questions = generated.Questions ?? [];
        foreach (var question in questions.Take(request.Count))
        {
            var draft = ToDraft(question);
            if (Problem(draft, optionCount, checkVisuals: true) is null)
                response.Drafts.Add(draft);
            else
                response.Rejected++;
        }
        response.Rejected += Math.Max(0, questions.Count - request.Count);
        return response;
    }

    public async Task<SavedQuestionDraftsDto?> SaveAsync(SaveQuestionDraftsDto request, LabelingActor actor, int userId, CancellationToken ct)
    {
        var outcome = await ReadOutcomeAsync(request.LearningOutcomeId, ct);
        if (outcome is null)
            return null;

        foreach (var question in request.Questions)
        {
            // İnceleyen şıkları düzenlemiş olabilir; görsele atıf kararı da onundur, kural yanlış alarm verebilir.
            if (Problem(question, optionCount: null, checkVisuals: false) is { } problem)
                throw new QuestionDraftInvalidException(problem);
        }

        // Her kayıt ücretli sınıflandırmayı tetikler: üretimle aynı saatlik kota.
        await LabelingAiQuota.AcquireCallsAsync(rateLimitStore, actor, options.Value, request.Questions.Count, ct);

        var saved = new List<int>();
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // Yeniden denemede önceki denemenin satırları izleyicide kalmasın.
                db.ChangeTracker.Clear();
                saved.Clear();
                db.SetCurrentUser(userId);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                foreach (var draft in request.Questions)
                    saved.Add(await WriteQuestionAsync(draft, outcome, request.DifficultyLevel, ct));
                await tx.CommitAsync(ct);
            });
        }
        catch
        {
            await LabelingAiQuota.RefundCallsAsync(rateLimitStore, actor, request.Questions.Count);
            throw;
        }
        return new SavedQuestionDraftsDto { QuestionIds = saved };
    }

    private async Task<int> WriteQuestionAsync(QuestionDraftDto draft, OutcomeInfo outcome, int difficultyLevel, CancellationToken ct)
    {
        var question = new Question
        {
            Text = ToStoredText(Clean(draft.Text)),
            Point = QuestionPoint,
            DifficultyLevel = difficultyLevel,
            SubjectId = outcome.SubjectId,
            AnswerColCount = AnswerColumns,
            InteractionType = McqGrader.Type,
            SourceType = QuestionSourceType.Generated,
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync(ct);

        var answers = draft.Options.Select((text, index) => new Answer { QuestionId = question.Id, Text = Clean(text), Order = index + 1 }).ToList();
        db.Answers.AddRange(answers);
        db.QuestionLearningOutcomes.Add(new QuestionLearningOutcome { QuestionId = question.Id, LearningOutcomeId = outcome.Id });
        await db.SaveChangesAsync(ct);

        question.CorrectAnswerId = answers[draft.CorrectIndex].Id;
        // Elle oluşturulan soruyla aynı yol: sınıflandırıcı konu ve alt konuyu metinden belirler.
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<QuestionCreatedEvent>(),
            Content = JsonSerializer.Serialize(new QuestionCreatedEvent
            {
                QuestionId = question.Id,
                SubjectId = question.SubjectId,
                GradeId = outcome.GradeId,
                CreatedAt = DateTime.UtcNow,
                Text = question.Text,
            }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return question.Id;
    }

    private Task<OutcomeInfo?> ReadOutcomeAsync(int id, CancellationToken ct) =>
        (from outcome in db.LearningOutcomes.AsNoTracking()
         join grade in db.Grades.AsNoTracking() on outcome.GradeId equals grade.Id
         where outcome.Id == id
         select new OutcomeInfo(outcome.Id, outcome.Code, outcome.Description, outcome.GradeId, grade.Name, outcome.SubjectId, outcome.Subject != null ? outcome.Subject.Name : null))
        .FirstOrDefaultAsync(ct);

    /// <summary>Oynatıcı soru metnini HTML olarak basar: düz metindeki "a&lt;b" etiket sanılıp metni yutmasın, satır sonu kaybolmasın.</summary>
    internal static string ToStoredText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

    /// <summary>Görünmez karakter boş ve yinelenen şık denetimini aşmasın.</summary>
    private static string Clean(string? value) => InvisibleCharacters.Replace(value ?? string.Empty, string.Empty).Trim();

    /// <summary>LGS'ye kadar 4, lisede 5 şık: sınav biçimiyle aynı.</summary>
    internal static int OptionCountFor(string gradeName)
    {
        var digits = new string(gradeName.TakeWhile(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var grade))
            return grade >= 9 ? 5 : 4;
        return gradeName.StartsWith("Hazırlık", StringComparison.OrdinalIgnoreCase) ? 5 : 4;
    }

    /// <summary>Modelin çoğunlukla aynı şıkkı doğru yazmasından kaçınmak için şıklar sunucuda karıştırılır.</summary>
    internal static QuestionDraftDto ToDraft(GeneratedQuestion question)
    {
        var options = question.Options ?? [];
        var correct = question.CorrectOptionIndex >= 0 && question.CorrectOptionIndex < options.Count ? options[question.CorrectOptionIndex] : null;
        var shuffled = options.OrderBy(_ => Random.Shared.Next()).ToList();
        return new QuestionDraftDto
        {
            Text = question.Text ?? string.Empty,
            Options = shuffled,
            CorrectIndex = correct is null ? -1 : shuffled.IndexOf(correct),
            Explanation = question.Explanation,
        };
    }

    /// <summary>Uymayan kuralın adı; uygunsa null.</summary>
    internal static string? Problem(QuestionDraftDto? draft, int? optionCount, bool checkVisuals)
    {
        if (draft is null)
            return "the question is empty";
        var text = Clean(draft.Text);
        if (text.Length == 0 || text.Length > MaxTextLength)
            return "the question text is empty or too long";
        var options = (draft.Options ?? []).Select(Clean).ToList();
        if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) || options.Any(o => o.Any(char.IsControl)))
            return "the text has control characters";
        if (optionCount is int expected ? options.Count != expected : options.Count is < 2 or > 6)
            return "the number of options is not valid";
        if (options.Any(o => o.Length == 0 || o.Length > MaxOptionLength))
            return "an option is empty or too long";
        if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
            return "two options are the same";
        if (draft.CorrectIndex < 0 || draft.CorrectIndex >= options.Count)
            return "the correct option is not valid";
        if (checkVisuals && (ReferenceToVisual.IsMatch(text) || options.Any(o => ReferenceToVisual.IsMatch(o))))
            return "the question refers to a visual";
        return null;
    }
}
