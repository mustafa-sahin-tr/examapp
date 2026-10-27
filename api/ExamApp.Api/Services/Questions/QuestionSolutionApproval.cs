using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

/// <summary>
/// Issue #580: soru kaydını yazan çağıran — çözüm onayı için. <see cref="CanApprove"/> = Admin ya da QuestionTrainer realm rolü.
/// </summary>
public sealed record SolutionActor(string? KeycloakId, string? Name, bool CanApprove);

/// <summary>
/// Issue #580: işaretleme ekranındaki taslak çözümden (<c>LabelingActivitySolutions</c>) sunucunun türettiği kaynak/onay — istemcinin
/// bildirdiği kaynak yok sayılır.
/// </summary>
public sealed record SolutionDraftInfo(
    SolutionSource Source, int? PromptVersion, bool Approved, string? ApprovedByKeycloakId, string? ApprovedByName, DateTime? ApprovedAt);

/// <summary>
/// Issue #580: soru çözümünün kaynak/onay yazma kuralları (soru kaydı <c>CreateOrUpdateQuestion</c> ve toplu kayıt
/// <c>SaveBulkQuestion</c> ortak; saf mantık). Önceki durum <paramref name="question"/>'ın mevcut alanlarıdır.
/// <list type="bullet">
/// <item>Taslak verildi (<c>draftSolutionId</c>) → kaynak/sürüm taslaktan; taslak onaylıysa onaylı (onaylayan taslağınki), değilse
/// Pending (Admin/QuestionTrainer <c>solutionApproved=true</c> ile hemen onaylayabilir).</item>
/// <item>Çözüm yok → None/Approved; ancak onaylanmamış Key/AI çözümü onaylayamayan biri sildiyse Pending kalır (tip değiştirip geri
/// alarak "aklama" kapanır; onaylayan onay ucuyla temizler).</item>
/// <item>Çözüm (kanonik JSON) değişmedi — yalnız plan/kutu değişse de → kaynak ve durum korunur; kaynağı bilinmeyen (None) eski
/// çözüm Human/Approved olur. Bekleyen çözümü yalnız Admin/QuestionTrainer <c>solutionApproved=true</c> ile onaylar.</item>
/// <item>Çözüm değişti, bildirilen kaynak Key/AI → Pending (onaylayan + <c>solutionApproved=true</c> → Approved).</item>
/// <item>Çözüm değişti, kaynak Human/boş → Human/Approved (yazan onaylar); ANCAK önceki çözüm onay bekleyen Key/AI ise ve yazan
/// onaylayamıyorsa kaynak korunur ve Pending kalır (onaylanmamış AI çözümü insan düzeltmesiyle onaysız yayına çıkamaz).</item>
/// </list>
/// </summary>
public static class QuestionSolutionApproval
{
    /// <summary>İstek değeri: boş → null (Human sayılır); <c>Key</c> | <c>AI</c> | <c>Human</c> (büyük/küçük harf duyarsız); başkası geçersiz.</summary>
    public static bool TryParseSource(string? value, out SolutionSource? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var trimmed = value.Trim();
        foreach (var candidate in new[] { SolutionSource.Key, SolutionSource.AI, SolutionSource.Human })
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                source = candidate;
                return true;
            }
        }
        return false;
    }

    public static void Apply(
        Question question, bool hasSolution, bool solutionChanged, SolutionSource? requestedSource, bool requestedApproved,
        int? promptVersion, SolutionActor? actor, DateTime now, SolutionDraftInfo? draft = null)
    {
        var canApprove = actor?.CanApprove == true;
        var approveNow = requestedApproved && canApprove;
        var wasUnreviewed = question.SolutionStatus == SolutionStatus.Pending
                            && question.SolutionSource is SolutionSource.Key or SolutionSource.AI;

        if (!hasSolution)
        {
            if (wasUnreviewed && !canApprove)
                return;
            question.SolutionSource = SolutionSource.None;
            question.SolutionStatus = SolutionStatus.Approved;
            question.SolutionPromptVersion = null;
            ClearApproval(question);
            return;
        }

        if (draft is not null)
        {
            question.SolutionSource = draft.Source;
            question.SolutionPromptVersion = draft.PromptVersion;
            if (draft.Approved)
            {
                question.SolutionStatus = SolutionStatus.Approved;
                question.SolutionApprovedByKeycloakId = Truncate(draft.ApprovedByKeycloakId, 64);
                question.SolutionApprovedByName = Truncate(draft.ApprovedByName, 200);
                question.SolutionApprovedAt = draft.ApprovedAt ?? now;
            }
            else if (approveNow)
            {
                MarkApproved(question, actor, now);
            }
            else
            {
                MarkPending(question);
            }
            return;
        }

        if (!solutionChanged)
        {
            if (question.SolutionSource == SolutionSource.None)
            {
                question.SolutionSource = SolutionSource.Human;
                MarkApproved(question, actor, now);
            }
            else if (question.SolutionStatus == SolutionStatus.Pending && approveNow)
            {
                MarkApproved(question, actor, now);
            }
            return;
        }

        var source = requestedSource ?? SolutionSource.Human;
        if (source is SolutionSource.Key or SolutionSource.AI)
        {
            question.SolutionSource = source;
            question.SolutionPromptVersion = promptVersion;
            if (approveNow)
                MarkApproved(question, actor, now);
            else
                MarkPending(question);
            return;
        }

        if (wasUnreviewed && !canApprove)
        {
            // Onaylanmamış Key/AI çözümünü onaylayamayan biri düzeltti: kaynak korunur, onay beklemeye devam eder.
            MarkPending(question);
            return;
        }

        question.SolutionSource = SolutionSource.Human;
        question.SolutionPromptVersion = null;
        MarkApproved(question, actor, now);
    }

    private static void MarkPending(Question question)
    {
        question.SolutionStatus = SolutionStatus.Pending;
        ClearApproval(question);
    }

    public static void MarkApproved(Question question, SolutionActor? actor, DateTime now)
    {
        question.SolutionStatus = SolutionStatus.Approved;
        question.SolutionApprovedByKeycloakId = Truncate(actor?.KeycloakId, 64);
        question.SolutionApprovedByName = Truncate(actor?.Name, 200);
        question.SolutionApprovedAt = now;
    }

    private static void ClearApproval(Question question)
    {
        question.SolutionApprovedByKeycloakId = null;
        question.SolutionApprovedByName = null;
        question.SolutionApprovedAt = null;
    }

    public static string ToStatus(SolutionStatus status) => status == SolutionStatus.Approved ? "approved" : "pending";

    /// <summary>Kaynak adı (sorguda çevrilebilir ifade yerine sonuçta kullanılır).</summary>
    public static string ToSourceName(SolutionSource source) => source.ToString();

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

/// <summary>Issue #580: <c>POST api/questions/{id}/solution/approve</c> sonucu.</summary>
public enum QuestionSolutionApproveOutcome
{
    Approved,
    NotFound,
    NoSolution,
}

public interface IQuestionSolutionApprovalService
{
    /// <summary>
    /// Bekleyen (Key/AI) çözümü onaylar: onaylayan + zaman kaydedilir; zaten onaylıysa dokunulmaz. Yetki (Admin/QuestionTrainer + soru
    /// sahibi ya da admin) controller'da.
    /// </summary>
    Task<(QuestionSolutionApproveOutcome Outcome, QuestionSolutionStateDto? State)> ApproveAsync(
        int questionId, SolutionActor actor, CancellationToken ct = default);
}

public sealed class QuestionSolutionApprovalService(AppDbContext db, TimeProvider timeProvider) : IQuestionSolutionApprovalService
{
    public async Task<(QuestionSolutionApproveOutcome Outcome, QuestionSolutionStateDto? State)> ApproveAsync(
        int questionId, SolutionActor actor, CancellationToken ct = default)
    {
        var question = await db.Questions.AsTracking().FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null)
            return (QuestionSolutionApproveOutcome.NotFound, null);
        if (string.IsNullOrWhiteSpace(question.InteractionSolution) || question.SolutionSource == SolutionSource.None)
        {
            // Çözümü silinmiş ama Pending kalmış soru (onaylanmamış çözüm silindi): onaylayan durumu temizler.
            if (question.SolutionStatus == SolutionStatus.Pending)
            {
                question.SolutionSource = SolutionSource.None;
                question.SolutionStatus = SolutionStatus.Approved;
                question.SolutionPromptVersion = null;
                await db.SaveChangesAsync(ct);
                return (QuestionSolutionApproveOutcome.Approved, ToState(question));
            }
            return (QuestionSolutionApproveOutcome.NoSolution, ToState(question));
        }

        if (question.SolutionStatus == SolutionStatus.Pending)
        {
            QuestionSolutionApproval.MarkApproved(question, actor, timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }
        return (QuestionSolutionApproveOutcome.Approved, ToState(question));
    }

    internal static QuestionSolutionStateDto ToState(Question q) => new()
    {
        QuestionId = q.Id,
        SolutionSource = q.SolutionSource.ToString(),
        SolutionStatus = QuestionSolutionApproval.ToStatus(q.SolutionStatus),
        SolutionPromptVersion = q.SolutionPromptVersion,
        SolutionApprovedByName = q.SolutionApprovedByName,
        SolutionApprovedAt = q.SolutionApprovedAt,
    };
}
