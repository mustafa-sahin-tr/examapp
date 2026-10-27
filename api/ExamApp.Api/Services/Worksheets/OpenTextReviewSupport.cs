using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.OpenText;
using ExamApp.Api.Services.Interactions;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// issue #583: öğrenci oturum yolları (<see cref="TestSessionService"/>) ve öğretmen kuyruğunun (<see cref="OpenTextReviewService"/>)
/// paylaştığı açık uçlu değerlendirme kuralları.
/// </summary>
internal static class OpenTextReviewSupport
{
    /// <summary>
    /// "Gönderildi" = cevap var VE (satır son yazımıyla kontrol edildi YA DA oturum artık Started değil). Kayıt sırasında (yazarken)
    /// kaydedilen taslak metin kuyruğa düşmez; "Cevapları kontrol et" ya da tamamlanma onu gönderir.
    /// </summary>
    public static bool IsSubmitted(bool hasAnswer, int? checkedAnswerRevision, int answerRevision, WorksheetInstanceStatus status)
        => hasAnswer && (checkedAnswerRevision == answerRevision || status != WorksheetInstanceStatus.Started);

    /// <summary>
    /// Satırın cevabı yazıldıktan sonra, AYNI transaction ve satır kilidi altında çağrılır: değerlendirme varsa güncelliği yeni metnin
    /// özetine göre (farklı / boş → eski; aynı metne dönüldüyse yeniden güncel). Değerlendirme yoksa hiçbir şey yapmaz.
    /// </summary>
    public static Task SyncStaleAsync(AppDbContext context, int instanceQuestionId, string? payload, CancellationToken ct)
    {
        var hash = OpenTextGrader.AnswerHash(payload);
        return context.OpenTextReviews
            .Where(r => r.WorksheetInstanceQuestionId == instanceQuestionId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Stale, r => hash == null || r.AnswerSha256 != hash), ct);
    }

    /// <summary>Oturumun değerlendirmeleri (satır id → kayıt).</summary>
    public static async Task<Dictionary<int, OpenTextReview>> LoadForInstanceAsync(AppDbContext context, int instanceId, CancellationToken ct)
        => await context.OpenTextReviews.AsNoTracking()
            .Where(r => context.TestInstanceQuestions.IgnoreQueryFilters()
                .Any(q => q.Id == r.WorksheetInstanceQuestionId && q.WorksheetInstanceId == instanceId))
            .ToDictionaryAsync(r => r.WorksheetInstanceQuestionId, ct);

    /// <summary>
    /// Öğrencinin kendi görünümü: cevap yoksa (boşaltılmış) null — eski değerlendirme gösterilmez (review #583/7). Değerlendirme
    /// güncelse <c>reviewed</c>, eskiyse <c>outdated</c> (eski değerlendirme + geri bildirim görünür), yoksa gönderilmiş cevapta
    /// <c>pending</c>; aksi halde null.
    /// </summary>
    public static OpenTextStudentReviewDto? StudentView(OpenTextReview? review, bool hasAnswer, bool submitted)
    {
        if (!hasAnswer)
            return null;
        if (review != null)
        {
            return new OpenTextStudentReviewDto
            {
                Status = review.Stale ? OpenTextReviewStatuses.Outdated : OpenTextReviewStatuses.Reviewed,
                Evaluation = OpenTextReviewStatuses.ToApi(review.Evaluation),
                Feedback = review.Feedback,
                ReviewedAt = review.ReviewedAt,
            };
        }
        return submitted ? new OpenTextStudentReviewDto { Status = OpenTextReviewStatuses.Pending } : null;
    }

    /// <summary>Satır geri bildirimi: güncel değerlendirmede değerlendirme adı, aksi halde <c>pending</c> (cevapsızda null).</summary>
    public static string? Feedback(OpenTextReview? review, string? storedFeedback)
        => review is { Stale: false }
            ? OpenTextGrader.FeedbackFor(OpenTextReviewStatuses.ToApi(review.Evaluation))
            : storedFeedback;

    /// <summary>Oturumun bütün değerlendirmelerini siler (#609 yeniden çözme; çağıranın transaction'ında).</summary>
    public static Task<int> DeleteForInstanceAsync(AppDbContext context, int instanceId, CancellationToken ct)
        => context.OpenTextReviews
            .Where(r => context.TestInstanceQuestions.IgnoreQueryFilters()
                .Any(q => q.Id == r.WorksheetInstanceQuestionId && q.WorksheetInstanceId == instanceId))
            .ExecuteDeleteAsync(ct);
}
