using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.OpenText;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>issue #583: açık uçlu cevapların öğretmen değerlendirme kuyruğu (puansız, AI'sız ilk sürüm).</summary>
public interface IOpenTextReviewService
{
    /// <summary>İstek sahibinin değerlendirebileceği gönderilmiş açık uçlu cevaplar (sayfalı). Sorgu doğrulanmış gelir.</summary>
    Task<Paged<OpenTextReviewItemDto>> GetQueueAsync(SchoolScope requester, OpenTextReviewQueueQuery query, CancellationToken ct = default);

    /// <summary>
    /// Değerlendir / düzelt. 400 InvalidInput · 403 Forbidden (başkasının değerlendirmesi) · 404 NotFound · 409 Conflict
    /// (bkz. <see cref="OpenTextReviewErrorCodes"/>).
    /// </summary>
    Task<OpenTextReviewResultDto> ReviewAsync(SchoolScope requester, int instanceQuestionId, OpenTextReviewRequestDto request,
        CancellationToken ct = default);
}

/// <summary>
/// issue #583. Yetki = #573 öğretmen takibinin kuralı (<see cref="WorksheetAssignmentService.GetWorksheetAssignmentsForTeacherAsync"/>):
/// öğretmen, etkinliği KENDİ oluşturduğu (<c>CreateUserId</c>) bir atamanın hedeflediği öğrencinin, o atamanın penceresinde sayılan
/// oturumunu (<see cref="AssignmentInstanceWindow.Counts"/>; yeniden çözülen oturum tamamlanmış sayılır,
/// <see cref="WorksheetInstanceStatusRules.TrackingStatus"/>) değerlendirir. Hedef: öğrenci hedefli atamada o öğrenci; sınıf hedefli
/// atamada (bağımsız öğretmende yok) o sınıftaki öğrenciler — atamanın okulu varsa öğrencinin DOĞRULANMIŞ okulu o olmalı
/// (takipteki <c>!assignment.SchoolId.HasValue || s.VerifiedSchoolId == assignment.SchoolId</c> koşulunun aynısı, #361). Öğrenci
/// ayrıca istek sahibinin okul kapsamında (<see cref="ISchoolAccessPolicy.ApplyScope{T}"/>; bağımsızda Approved booking). Admin
/// (unrestricted) tüm etkinliklerin cevaplarını görür. Yalnız <c>Worksheet.Kind = Activity</c> + <c>openText</c> maddesi; silinmiş
/// etkinlik dışarıda. Kapsam dışı satır "yok" ile aynı yanıtı alır (404). Başkasının verdiği değerlendirmeyi yalnız o kullanıcı ve
/// Admin düzeltir (403). Rubrik yalnız sorunun sahibine (<see cref="QuestionOwnershipGuard.OwnedBy"/>) ve Admin'e döner.
/// </summary>
public sealed class OpenTextReviewService : IOpenTextReviewService
{
    private readonly AppDbContext _context;
    private readonly ISchoolAccessPolicy _schoolAccessPolicy;
    private readonly IStringLocalizer<Messages> _localizer;

    public OpenTextReviewService(AppDbContext context, ISchoolAccessPolicy schoolAccessPolicy, IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _schoolAccessPolicy = schoolAccessPolicy;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<Paged<OpenTextReviewItemDto>> GetQueueAsync(SchoolScope requester, OpenTextReviewQueueQuery query,
        CancellationToken ct = default)
    {
        var page = Math.Clamp(query.Page, 1, OpenTextReviewQueueQuery.MaxPage);
        var pageSize = Math.Clamp(query.PageSize, 1, OpenTextReviewQueueQuery.MaxPageSize);
        var status = (query.Status ?? OpenTextReviewStatuses.Pending).Trim();

        var rows = Submitted(Reviewable(requester), DateTime.UtcNow);
        if (query.WorksheetId is { } worksheetId)
            rows = rows.Where(q => q.WorksheetInstance.WorksheetId == worksheetId);

        // "pending" = güncel değerlendirmesi yok (hiç yok ya da eskimiş); "reviewed" = güncel değerlendirmesi var.
        if (status == OpenTextReviewStatuses.Pending)
            rows = rows.Where(q => !_context.OpenTextReviews.Any(r => r.WorksheetInstanceQuestionId == q.Id && !r.Stale));
        else if (status == OpenTextReviewStatuses.Reviewed)
            rows = rows.Where(q => _context.OpenTextReviews.Any(r => r.WorksheetInstanceQuestionId == q.Id && !r.Stale));

        var total = await rows.CountAsync(ct);

        // Bekleyenler en eski cevaptan; diğerleri en yeniden.
        var ordered = status == OpenTextReviewStatuses.Pending
            ? rows.OrderBy(q => q.UpdateTime ?? q.CreateTime).ThenBy(q => q.Id)
            : rows.OrderByDescending(q => q.UpdateTime ?? q.CreateTime).ThenByDescending(q => q.Id);

        var items = await Project(ordered.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        var rubricQuestionIds = await RubricVisibleQuestionIdsAsync(requester, items.Select(i => i.QuestionId), ct);

        return new Paged<OpenTextReviewItemDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = total,
            Items = items.Select(i => ToDto(i, requester, rubricQuestionIds)).ToList()
        };
    }

    public async Task<OpenTextReviewResultDto> ReviewAsync(SchoolScope requester, int instanceQuestionId,
        OpenTextReviewRequestDto request, CancellationToken ct = default)
    {
        if (!TryReadRequest(request, out var evaluation, out var feedback, out var expectedHash))
            return Failure(OpenTextReviewErrorCodes.InvalidReview, "worksheets.openTextReview.invalidReview", invalidInput: true);

        if (instanceQuestionId <= 0 || !await Reviewable(requester).AnyAsync(q => q.Id == instanceQuestionId, ct))
            return Failure(OpenTextReviewErrorCodes.NotFound, "worksheets.openTextReview.notFound", notFound: true);

        var strategy = _context.Database.CreateExecutionStrategy();
        var failure = await strategy.ExecuteAsync(async _ =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            // Satır kilidi (no-op UPDATE): öğrencinin save-answer / kontrol / yeniden çözme yazımı aynı satırı günceller ve bu
            // kilitte serileşir — metin bu değerlendirmenin altında değişemez; değiştiyse aşağıdaki özet karşılaştırması yakalar.
            await _context.TestInstanceQuestions
                .Where(q => q.Id == instanceQuestionId)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.AnswerRevision, q => q.AnswerRevision), ct);

            var row = await _context.TestInstanceQuestions.AsNoTracking()
                .Where(q => q.Id == instanceQuestionId)
                .Select(q => new
                {
                    q.AnswerPayload, q.CheckedAnswerRevision, q.AnswerRevision, q.WorksheetInstance.Status,
                    q.WorksheetInstance.StartTime, q.WorksheetInstance.MaxDurationSeconds
                })
                .FirstOrDefaultAsync(ct);
            if (row == null)
            {
                await tx.RollbackAsync(ct);
                return Failure(OpenTextReviewErrorCodes.NotFound, "worksheets.openTextReview.notFound", notFound: true);
            }

            // Öğrenci görünümüyle aynı etkin durum (#396).
            var status = TestTimeLimit.EffectiveStatus(row.Status, row.StartTime, row.MaxDurationSeconds, DateTime.UtcNow);
            var currentHash = OpenTextGrader.AnswerHash(row.AnswerPayload);
            if (currentHash == null
                || !OpenTextReviewSupport.IsSubmitted(true, row.CheckedAnswerRevision, row.AnswerRevision, status))
            {
                await tx.RollbackAsync(ct);
                return Failure(OpenTextReviewErrorCodes.NotSubmitted, "worksheets.openTextReview.notSubmitted", conflict: true);
            }
            if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
            {
                await tx.RollbackAsync(ct);
                return Failure(OpenTextReviewErrorCodes.AnswerChanged, "worksheets.openTextReview.answerChanged", conflict: true);
            }

            var now = DateTime.UtcNow;
            var review = await _context.OpenTextReviews.AsTracking()
                .FirstOrDefaultAsync(r => r.WorksheetInstanceQuestionId == instanceQuestionId, ct);
            if (review != null && !CanEdit(review.ReviewedByUserId, requester))
            {
                await tx.RollbackAsync(ct);
                return Failure(OpenTextReviewErrorCodes.Forbidden, "worksheets.openTextReview.forbidden", forbidden: true);
            }
            if (review == null)
            {
                review = new OpenTextReview { WorksheetInstanceQuestionId = instanceQuestionId, CreatedAt = now };
                _context.OpenTextReviews.Add(review);
            }
            review.Evaluation = evaluation;
            review.Feedback = feedback;
            review.Source = AnswerGradedBy.Teacher;
            review.ReviewedByUserId = requester.UserId;
            review.ReviewedAt = now;
            review.AnswerSha256 = currentHash;
            review.Stale = false;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        }, ct);

        if (failure != null)
            return failure;

        var item = await Project(Reviewable(requester).Where(q => q.Id == instanceQuestionId)).FirstOrDefaultAsync(ct);
        var rubricQuestionIds = item == null
            ? new HashSet<int>()
            : await RubricVisibleQuestionIdsAsync(requester, [item.QuestionId], ct);
        return new OpenTextReviewResultDto
        {
            Success = true,
            Message = _localizer["worksheets.openTextReview.saved"],
            Item = item == null ? null : ToDto(item, requester, rubricQuestionIds)
        };
    }

    // ================================================================== kurallar

    /// <summary>Mevcut değerlendirmeyi düzeltebilir mi: değerlendirmeyi veren kullanıcı ya da Admin (unrestricted).</summary>
    private static bool CanEdit(int reviewedByUserId, SchoolScope requester)
        => requester.IsUnrestricted || reviewedByUserId == requester.UserId;

    /// <summary>Rubriği görebileceği sorular: Admin hepsi; diğerleri yalnız sahibi olduğu sorular (#287 sahiplik kuralı).</summary>
    private async Task<HashSet<int>> RubricVisibleQuestionIdsAsync(SchoolScope requester, IEnumerable<int> questionIds, CancellationToken ct)
    {
        var ids = questionIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<int>();
        if (requester.IsUnrestricted)
            return ids.ToHashSet();
        return (await _context.Questions.AsNoTracking()
                .Where(q => ids.Contains(q.Id))
                .Where(QuestionOwnershipGuard.OwnedBy(_context, requester.UserId))
                .Select(q => q.Id)
                .ToListAsync(ct))
            .ToHashSet();
    }

    // ================================================================== sorgular

    /// <summary>İstek sahibinin değerlendirebileceği openText satırları (etkinlik, kapsam içi öğrenci, takip kuralı + atama penceresi).</summary>
    private IQueryable<WorksheetInstanceQuestion> Reviewable(SchoolScope requester)
    {
        var students = _schoolAccessPolicy.ApplyScope(_context.Students.AsNoTracking(), requester);
        var rows = _context.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.WorksheetQuestion.Question.InteractionType == OpenTextGrader.Type
                && q.WorksheetInstance.Worksheet.Kind == WorksheetKind.Activity
                && students.Any(s => s.Id == q.WorksheetInstance.StudentId));
        if (requester.IsUnrestricted)
            return rows;

        var userId = requester.UserId;
        var independent = requester.IsIndependent;
        // Atama penceresi = AssignmentInstanceWindow.Counts(StartTime, TrackingStatus, StartAt, EndAt): EndAt'ten sonra başlamamış ve
        // (pencere içinde başlamış YA DA bitmiş — Completed/Expired ya da bir kez tamamlanmış/yeniden çözülen).
        return rows.Where(q => _context.WorksheetAssignments.Any(a =>
            a.CreateUserId == userId
            && a.WorksheetId == q.WorksheetInstance.WorksheetId
            && (a.EndAt == null || q.WorksheetInstance.StartTime <= a.EndAt)
            && (q.WorksheetInstance.StartTime >= a.StartAt
                || q.WorksheetInstance.Status == WorksheetInstanceStatus.Completed
                || q.WorksheetInstance.Status == WorksheetInstanceStatus.Expired
                || q.WorksheetInstance.FirstCompletedAt != null)
            && (a.StudentId == q.WorksheetInstance.StudentId
                || (!independent
                    && a.StudentId == null
                    && a.GradeId != null
                    && a.GradeId == q.WorksheetInstance.Student.GradeId
                    && (a.SchoolId == null
                        || (q.WorksheetInstance.Student.SchoolVerifiedAt != null
                            && q.WorksheetInstance.Student.SchoolId == a.SchoolId))))));
    }

    /// <summary>
    /// <see cref="OpenTextReviewSupport.IsSubmitted"/>'in SQL karşılığı; oturum durumu öğrenci görünümüyle aynı etkin durumdur
    /// (<see cref="TestTimeLimit.EffectiveStatus"/>: süresi + tolerans dolmuş Started oturum bitmiş sayılır).
    /// </summary>
    private static IQueryable<WorksheetInstanceQuestion> Submitted(IQueryable<WorksheetInstanceQuestion> rows, DateTime nowUtc)
    {
        var toleranceSeconds = (int)TestTimeLimit.Tolerance.TotalSeconds;
        return rows.Where(q => q.AnswerPayload != null
            && (q.CheckedAnswerRevision == q.AnswerRevision
                || q.WorksheetInstance.Status != WorksheetInstanceStatus.Started
                || (q.WorksheetInstance.MaxDurationSeconds > 0
                    && q.WorksheetInstance.StartTime.AddSeconds((double)(q.WorksheetInstance.MaxDurationSeconds!.Value + toleranceSeconds)) < nowUtc)));
    }

    private sealed record Row(
        int Id, int InstanceId, int WorksheetId, string WorksheetName, int QuestionId, int ItemNumber, string? QuestionText,
        string? QuestionImageUrl, string? InteractionPlan, string? InteractionSolution, int StudentId, int StudentUserId,
        string? StudentNumber, string? GradeName, string? AnswerPayload, DateTime? AnsweredAt, OpenTextReview? Review);

    /// <summary>
    /// <c>ItemNumber</c> = oynatıcının madde sırası (1 tabanlı): oturumun satırları <c>(WorksheetQuestion.Order, WorksheetQuestion.Id)</c>
    /// ile sıralanır (<c>TestSessionService.OrderedForDisplay</c>); oynatıcı satırları soru zinciriyle yüklediği için sorusu / bağı
    /// silinmiş satırlar sayılmaz (aynı filtreli JOIN).
    /// </summary>
    private IQueryable<Row> Project(IQueryable<WorksheetInstanceQuestion> rows)
        => rows.Select(q => new Row(
            q.Id,
            q.WorksheetInstanceId,
            q.WorksheetInstance.WorksheetId,
            q.WorksheetInstance.Worksheet.Name,
            q.WorksheetQuestion.QuestionId,
            _context.TestInstanceQuestions.Count(o => o.WorksheetInstanceId == q.WorksheetInstanceId
                && o.WorksheetQuestion.Question.Id > 0
                && (o.WorksheetQuestion.Order < q.WorksheetQuestion.Order
                    || (o.WorksheetQuestion.Order == q.WorksheetQuestion.Order && o.WorksheetQuestion.Id < q.WorksheetQuestion.Id))) + 1,
            q.WorksheetQuestion.Question.Text,
            q.WorksheetQuestion.Question.ImageUrl,
            q.WorksheetQuestion.Question.InteractionPlan,
            q.WorksheetQuestion.Question.InteractionSolution,
            q.WorksheetInstance.StudentId,
            q.WorksheetInstance.Student.UserId,
            q.WorksheetInstance.Student.StudentNumber,
            q.WorksheetInstance.Student.Grade != null ? q.WorksheetInstance.Student.Grade.Name : null,
            q.AnswerPayload,
            q.UpdateTime ?? q.CreateTime,
            _context.OpenTextReviews.FirstOrDefault(r => r.WorksheetInstanceQuestionId == q.Id)));

    private static OpenTextReviewItemDto ToDto(Row row, SchoolScope requester, IReadOnlySet<int> rubricQuestionIds)
    {
        var limits = OpenTextGrader.ReadLimits(InteractionPlanSolution.Split(row.InteractionPlan).Plan);
        var (_, solution) = InteractionPlanSolution.ForOutput(row.InteractionPlan, row.InteractionSolution, includeSolution: true);
        var (sample, rubric) = OpenTextGrader.ReadSolution(solution);
        var text = OpenTextGrader.ReadText(row.AnswerPayload) ?? string.Empty;
        return new OpenTextReviewItemDto
        {
            InstanceQuestionId = row.Id,
            InstanceId = row.InstanceId,
            WorksheetId = row.WorksheetId,
            WorksheetName = row.WorksheetName,
            QuestionId = row.QuestionId,
            ItemNumber = row.ItemNumber,
            QuestionText = row.QuestionText,
            QuestionImageUrl = row.QuestionImageUrl,
            StudentId = row.StudentId,
            StudentUserId = row.StudentUserId,
            StudentNumber = row.StudentNumber,
            GradeName = row.GradeName,
            Text = text,
            AnswerHash = OpenTextGrader.AnswerHash(row.AnswerPayload) ?? string.Empty,
            WordCount = OpenTextGrader.CountWords(text),
            MinWords = limits.MinWords,
            MaxWords = limits.MaxWords,
            SampleAnswer = sample,
            Rubric = rubricQuestionIds.Contains(row.QuestionId) ? rubric : null,
            AnsweredAt = row.AnsweredAt,
            Status = row.Review == null
                ? OpenTextReviewStatuses.Pending
                : row.Review.Stale ? OpenTextReviewStatuses.Outdated : OpenTextReviewStatuses.Reviewed,
            CanEdit = row.Review == null || CanEdit(row.Review.ReviewedByUserId, requester),
            Review = row.Review == null
                ? null
                : new OpenTextTeacherReviewDto
                {
                    Evaluation = OpenTextReviewStatuses.ToApi(row.Review.Evaluation),
                    Feedback = row.Review.Feedback,
                    Source = OpenTextReviewStatuses.SourceToApi(row.Review.Source),
                    ReviewedByUserId = row.Review.ReviewedByUserId,
                    ReviewedAt = row.Review.ReviewedAt,
                    Stale = row.Review.Stale
                }
        };
    }

    // ================================================================== doğrulama / yanıt

    private static bool TryReadRequest(OpenTextReviewRequestDto? request, out OpenTextEvaluation evaluation, out string? feedback,
        out string expectedHash)
    {
        evaluation = default;
        feedback = null;
        expectedHash = string.Empty;
        if (request == null || !OpenTextReviewStatuses.TryParse(request.Evaluation, out evaluation))
            return false;

        var hash = request.AnswerHash?.Trim() ?? string.Empty;
        if (hash.Length != 64 || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return false;
        expectedHash = hash;

        var text = request.Feedback?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrEmpty(text))
            return true;
        if (text.Length > OpenTextReview.MaxFeedbackLength || PlainTextAttribute.ContainsControlCharacters(text))
            return false;
        feedback = text;
        return true;
    }

    private OpenTextReviewResultDto Failure(string errorCode, string messageKey, bool invalidInput = false, bool notFound = false,
        bool conflict = false, bool forbidden = false) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        InvalidInput = invalidInput,
        NotFound = notFound,
        Conflict = conflict,
        Forbidden = forbidden,
        Message = _localizer[messageKey]
    };
}
