using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.DirectMessages;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.AskTeacher;

public class AskTeacherOptions
{
    public const string Section = "AskTeacher";

    /// <summary>Öğretmenin cevap süresi (saat); dolunca soru kapanır, hak düşmez.</summary>
    public int AnswerHours { get; set; } = 48;

    /// <summary>Süresi dolan soruları kapatan işin zamanı.</summary>
    public string Cron { get; set; } = "*/15 * * * *";
}

public enum AskTeacherError
{
    None,
    StudentNotFound,
    NotInPlan,
    QuotaReached,
    /// <summary>Öğretmen öğrencinin ilişkili / randevulu öğretmenlerinden değil (nötr 404).</summary>
    TeacherNotAvailable,
    InvalidSubject,
    InvalidImage,
    ImageTooLarge,
    NoteTooLong,
    NotFound,
    /// <summary>Soru bu işlem için uygun durumda değil (cevaplanmış, süresi dolmuş …).</summary>
    InvalidState,
    AnswerRequired,
    AnswerTooLong,
    InvalidRating,
}

public sealed record AskTeacherImage(long Length, Stream Content);

public sealed record AskTeacherSendRequest(int TeacherId, int? SubjectId, string? Note, AskTeacherImage Image);

public sealed record AskTeacherResult(AskTeacherQuestionDto? Question, AskTeacherError Error, EntitlementCheck? Quota = null);

public interface IAskTeacherService
{
    Task<IReadOnlyList<AskTeacherTeacherDto>?> GetTeachersAsync(int studentUserId, int? subjectId, CancellationToken ct);
    Task<AskTeacherQuotaDto> GetQuotaAsync(EntitlementSubject subject, CancellationToken ct);
    Task<AskTeacherResult> SendAsync(EntitlementSubject subject, AskTeacherSendRequest request, CancellationToken ct);
    Task<IReadOnlyList<AskTeacherQuestionDto>> ListForStudentAsync(int studentUserId, CancellationToken ct);
    Task<AskTeacherQuestionDto?> GetForStudentAsync(int studentUserId, int id, CancellationToken ct);
    Task<AskTeacherError> CancelAsync(int studentUserId, int id, CancellationToken ct);
    Task<AskTeacherError> RateAsync(int studentUserId, int id, int rating, CancellationToken ct);

    Task<IReadOnlyList<AskTeacherQuestionDto>> QueueAsync(int teacherUserId, AskTeacherStatus? status, CancellationToken ct);
    Task<AskTeacherQuestionDto?> GetForTeacherAsync(int teacherUserId, int id, CancellationToken ct);
    Task<AskTeacherResult> AnswerAsync(int teacherUserId, int id, string? text, AskTeacherImage? image, CancellationToken ct);

    /// <summary>Soru ya da cevap görseli; yalnız sorunun öğrencisi ve öğretmeni (başkasına null).</summary>
    Task<Stream?> GetImageAsync(int userId, int id, bool answer, CancellationToken ct);

    /// <summary>Süresi dolan açık soruları kapatır (hak düşmez); kapatılan sayısı.</summary>
    Task<int> ExpireAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1111: öğretmene soru sor. Öğrenci soruyu mesajlaşabildiği öğretmenlerden (<see cref="IDirectMessagePolicy.RelatedTeachers"/>:
/// atama ya da okul, onaylı, engellememiş) ya da onaylı randevusu olan bir öğretmene gönderir. Hak yalnız cevapta düşer
/// (<c>ask-teacher:{id}</c>); gönderimde kalan hak, cevap bekleyen sorulardan fazla olmalı. Görseller yönü düzeltilip küçültülür.
/// </summary>
public sealed class AskTeacherService(
    AppDbContext db,
    IDirectMessagePolicy policy,
    IEntitlementService entitlements,
    IMinIoService minio,
    IAuthApiClient authApi,
    IOptions<AskTeacherOptions> options,
    TimeProvider clock) : IAskTeacherService
{
    public const long MaxImageBytes = 15 * 1024 * 1024;
    public const int ImageMaxEdge = 2400;

    public async Task<IReadOnlyList<AskTeacherTeacherDto>?> GetTeachersAsync(int studentUserId, int? subjectId, CancellationToken ct)
    {
        var student = await policy.ResolveStudentAsync(studentUserId, ct);
        if (student is null)
            return null;
        var teachers = await EligibleTeachers(student).Select(t => new { t.Id, t.UserId }).ToListAsync(ct);
        if (teachers.Count == 0)
            return [];
        var teaching = subjectId is { } sid
            ? (await db.TeacherSubjects.Where(ts => ts.SubjectId == sid).Select(ts => ts.TeacherId).ToListAsync(ct)).ToHashSet()
            : [];
        var names = await NamesAsync(teachers.Select(t => t.UserId), ct);
        return teachers
            .Select(t => new AskTeacherTeacherDto
            {
                TeacherId = t.Id, FullName = names.GetValueOrDefault(t.UserId, string.Empty), TeachesSubject = teaching.Contains(t.Id),
            })
            .OrderByDescending(t => t.TeachesSubject).ThenBy(t => t.FullName, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<AskTeacherQuotaDto> GetQuotaAsync(EntitlementSubject subject, CancellationToken ct)
    {
        var check = await entitlements.CheckAsync(subject, EntitlementFeatures.AskTeacher, ct);
        return new AskTeacherQuotaDto
        {
            Limit = check.Limit, Used = check.Used, Pending = await PendingAsync(subject.UserId, ct), ResetsAtUtc = check.ResetsAtUtc,
            PlanKey = check.PlanKey,
        };
    }

    public async Task<AskTeacherResult> SendAsync(EntitlementSubject subject, AskTeacherSendRequest request, CancellationToken ct)
    {
        var student = await policy.ResolveStudentAsync(subject.UserId, ct);
        if (student is null)
            return new(null, AskTeacherError.StudentNotFound);
        if (request.Note is { Length: > AskTeacherQuestion.MaxNoteLength })
            return new(null, AskTeacherError.NoteTooLong);
        if (request.Image.Length is <= 0 or > MaxImageBytes)
            return new(null, AskTeacherError.ImageTooLarge);
        if (request.SubjectId is { } subjectId && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return new(null, AskTeacherError.InvalidSubject);

        var check = await entitlements.CheckAsync(subject, EntitlementFeatures.AskTeacher, ct);
        if (check.Limit == 0)
            return new(null, AskTeacherError.NotInPlan, check);
        if (!check.Allowed || check.Remaining is { } remaining && remaining <= await PendingAsync(subject.UserId, ct))
            return new(null, AskTeacherError.QuotaReached, check);

        var teacher = await EligibleTeachers(student).Where(t => t.Id == request.TeacherId)
            .Select(t => new { t.Id, t.UserId }).FirstOrDefaultAsync(ct);
        if (teacher is null)
            return new(null, AskTeacherError.TeacherNotAvailable);

        var jpeg = await EncodeAsync(request.Image, ct);
        if (jpeg is null)
            return new(null, AskTeacherError.InvalidImage);

        var now = clock.GetUtcNow().UtcDateTime;
        var question = new AskTeacherQuestion
        {
            StudentId = student.StudentId, StudentUserId = subject.UserId, TeacherId = teacher.Id, TeacherUserId = teacher.UserId,
            SubjectId = request.SubjectId, Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = now, DueAt = now.AddHours(options.Value.AnswerHours),
        };
        question.ImageUrl = await UploadAsync(jpeg, $"ask-teacher/{Guid.NewGuid():N}/question.jpg");
        db.SetCurrentUser(subject.UserId);
        db.AskTeacherQuestions.Add(question);
        await db.SaveChangesAsync(ct);
        return new((await ToDtosAsync([question], forTeacher: false, ct))[0], AskTeacherError.None);
    }

    public async Task<IReadOnlyList<AskTeacherQuestionDto>> ListForStudentAsync(int studentUserId, CancellationToken ct)
    {
        var rows = await db.AskTeacherQuestions.AsNoTracking().Where(q => q.StudentUserId == studentUserId)
            .OrderByDescending(q => q.CreatedAt).Take(200).ToListAsync(ct);
        return await ToDtosAsync(rows, forTeacher: false, ct);
    }

    public async Task<AskTeacherQuestionDto?> GetForStudentAsync(int studentUserId, int id, CancellationToken ct)
    {
        var row = await db.AskTeacherQuestions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id && q.StudentUserId == studentUserId, ct);
        return row is null ? null : (await ToDtosAsync([row], forTeacher: false, ct))[0];
    }

    public async Task<AskTeacherError> CancelAsync(int studentUserId, int id, CancellationToken ct)
    {
        if (!await db.AskTeacherQuestions.AnyAsync(q => q.Id == id && q.StudentUserId == studentUserId, ct))
            return AskTeacherError.NotFound;
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.AskTeacherQuestions.Where(q => q.Id == id && q.Status == AskTeacherStatus.Open)
            .ExecuteUpdateAsync(u => u.SetProperty(q => q.Status, AskTeacherStatus.Cancelled).SetProperty(q => q.ClosedAt, now), ct);
        return rows == 0 ? AskTeacherError.InvalidState : AskTeacherError.None;
    }

    public async Task<AskTeacherError> RateAsync(int studentUserId, int id, int rating, CancellationToken ct)
    {
        if (rating is < 1 or > 5)
            return AskTeacherError.InvalidRating;
        var row = await db.AskTeacherQuestions.AsTracking().FirstOrDefaultAsync(q => q.Id == id && q.StudentUserId == studentUserId, ct);
        if (row is null)
            return AskTeacherError.NotFound;
        if (row.Status != AskTeacherStatus.Answered)
            return AskTeacherError.InvalidState;
        row.Rating = rating;
        await db.SaveChangesAsync(ct);
        return AskTeacherError.None;
    }

    public async Task<IReadOnlyList<AskTeacherQuestionDto>> QueueAsync(int teacherUserId, AskTeacherStatus? status, CancellationToken ct)
    {
        var query = db.AskTeacherQuestions.AsNoTracking().Where(q => q.TeacherUserId == teacherUserId);
        if (status is { } s)
            query = query.Where(q => q.Status == s);
        // Açıklar süresi en yakın olandan, diğerleri en yeniden.
        var rows = status == AskTeacherStatus.Open
            ? await query.OrderBy(q => q.DueAt).Take(200).ToListAsync(ct)
            : await query.OrderByDescending(q => q.CreatedAt).Take(200).ToListAsync(ct);
        return await ToDtosAsync(rows, forTeacher: true, ct);
    }

    public async Task<AskTeacherQuestionDto?> GetForTeacherAsync(int teacherUserId, int id, CancellationToken ct)
    {
        var row = await db.AskTeacherQuestions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id && q.TeacherUserId == teacherUserId, ct);
        return row is null ? null : (await ToDtosAsync([row], forTeacher: true, ct))[0];
    }

    public async Task<AskTeacherResult> AnswerAsync(int teacherUserId, int id, string? text, AskTeacherImage? image, CancellationToken ct)
    {
        var row = await db.AskTeacherQuestions.AsTracking().FirstOrDefaultAsync(q => q.Id == id && q.TeacherUserId == teacherUserId, ct);
        if (row is null)
            return new(null, AskTeacherError.NotFound);
        var now = clock.GetUtcNow().UtcDateTime;
        if (row.Status != AskTeacherStatus.Open || row.DueAt <= now)
            return new(null, AskTeacherError.InvalidState);
        var answer = text?.Trim();
        if (string.IsNullOrEmpty(answer) && image is null)
            return new(null, AskTeacherError.AnswerRequired);
        if (answer is { Length: > AskTeacherQuestion.MaxAnswerLength })
            return new(null, AskTeacherError.AnswerTooLong);

        string? imageUrl = null;
        if (image is not null)
        {
            if (image.Length is <= 0 or > MaxImageBytes)
                return new(null, AskTeacherError.ImageTooLarge);
            var jpeg = await EncodeAsync(image, ct);
            if (jpeg is null)
                return new(null, AskTeacherError.InvalidImage);
            imageUrl = await UploadAsync(jpeg, $"ask-teacher/{Guid.NewGuid():N}/answer.jpg");
        }

        // Eşzamanlı ikinci cevap ya da süre dolumu ile yarış: yalnız hâlâ açık satır cevaplanır.
        var updated = await db.AskTeacherQuestions
            .Where(q => q.Id == id && q.Status == AskTeacherStatus.Open && q.DueAt > now)
            .ExecuteUpdateAsync(u => u
                .SetProperty(q => q.Status, AskTeacherStatus.Answered)
                .SetProperty(q => q.AnsweredAt, now)
                .SetProperty(q => q.AnswerText, string.IsNullOrEmpty(answer) ? null : answer)
                .SetProperty(q => q.AnswerImageUrl, imageUrl), ct);
        if (updated == 0)
        {
            if (imageUrl is not null)
                await minio.DeleteFileByUrlAsync(imageUrl);
            return new(null, AskTeacherError.InvalidState);
        }
        // Hak cevapta düşer; referans soru başına tek (tekrar sayılmaz).
        await entitlements.RecordUsageAsync(row.StudentUserId, EntitlementFeatures.AskTeacher, $"ask-teacher:{row.Id}", ct);

        await db.Entry(row).ReloadAsync(ct);
        return new((await ToDtosAsync([row], forTeacher: true, ct))[0], AskTeacherError.None);
    }

    public async Task<Stream?> GetImageAsync(int userId, int id, bool answer, CancellationToken ct)
    {
        var url = await db.AskTeacherQuestions.AsNoTracking()
            .Where(q => q.Id == id && (q.StudentUserId == userId || q.TeacherUserId == userId))
            .Select(q => answer ? q.AnswerImageUrl : q.ImageUrl)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrEmpty(url) ? null : await minio.GetFileStreamAsync(url);
    }

    public async Task<int> ExpireAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.AskTeacherQuestions.Where(q => q.Status == AskTeacherStatus.Open && q.DueAt <= now)
            .ExecuteUpdateAsync(u => u.SetProperty(q => q.Status, AskTeacherStatus.Expired).SetProperty(q => q.ClosedAt, now), ct);
    }

    /// <summary>İlişkili öğretmenler (mesajlaşma kuralı, engel dahil) ∪ öğrenciyle onaylı randevusu olan onaylı öğretmenler.</summary>
    private IQueryable<Teacher> EligibleTeachers(StudentMessagingContext student)
    {
        var related = policy.RelatedTeachers(student, clock.GetUtcNow().UtcDateTime).Select(r => r.TeacherId);
        var booked = db.Bookings.Where(b => b.StudentId == student.StudentId && b.Status == BookingStatus.Approved).Select(b => b.TeacherId);
        return db.Teachers.Where(t => (related.Contains(t.Id) || booked.Contains(t.Id))
                                      && t.AccountApprovedAt != null && t.AccountSuspendedAt == null);
    }

    private Task<int> PendingAsync(int studentUserId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.AskTeacherQuestions.CountAsync(q => q.StudentUserId == studentUserId && q.Status == AskTeacherStatus.Open && q.DueAt > now, ct);
    }

    private static async Task<byte[]?> EncodeAsync(AskTeacherImage image, CancellationToken ct)
    {
        try
        {
            using var buffer = new MemoryStream();
            await image.Content.CopyToAsync(buffer, ct);
            using var decoded = LabelingImage.Decode(buffer.ToArray());
            return decoded.Encode(ImageMaxEdge).Jpeg;
        }
        catch (LabelingValidationException)
        {
            return null;
        }
    }

    private async Task<string> UploadAsync(byte[] jpeg, string objectName)
    {
        using var stream = new MemoryStream(jpeg, writable: false);
        return await minio.UploadFileAsync(stream, objectName, contentType: "image/jpeg");
    }

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        var users = await authApi.GetUsersByIdsAsync(ids, ct);
        return users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty);
    }

    /// <summary>"Ayşe Kaya" → "Ayşe K." (öğretmene öğrencinin tam soyadı gösterilmez).</summary>
    internal static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => $"{string.Join(' ', parts[..^1])} {char.ToUpper(parts[^1][0], System.Globalization.CultureInfo.CurrentCulture)}.",
        };
    }

    private async Task<List<AskTeacherQuestionDto>> ToDtosAsync(IReadOnlyList<AskTeacherQuestion> rows, bool forTeacher,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var subjectIds = rows.Where(r => r.SubjectId != null).Select(r => r.SubjectId!.Value).Distinct().ToList();
        var subjects = subjectIds.Count == 0
            ? []
            : await db.Subjects.Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var names = await NamesAsync(rows.Select(r => r.TeacherUserId).Concat(forTeacher ? rows.Select(r => r.StudentUserId) : []), ct);
        return rows.Select(r => new AskTeacherQuestionDto
        {
            Id = r.Id,
            // Süresi geçmiş ama iş henüz kapatmamış açık soru Expired görünür.
            Status = (r.Status == AskTeacherStatus.Open && r.DueAt <= now ? AskTeacherStatus.Expired : r.Status).ToString(),
            TeacherId = r.TeacherId,
            TeacherName = names.GetValueOrDefault(r.TeacherUserId, string.Empty),
            StudentName = forTeacher ? ShortName(names.GetValueOrDefault(r.StudentUserId, string.Empty)) : string.Empty,
            SubjectId = r.SubjectId,
            SubjectName = r.SubjectId is { } sid ? subjects.GetValueOrDefault(sid) : null,
            Note = r.Note,
            CreatedAt = r.CreatedAt,
            DueAt = r.DueAt,
            AnsweredAt = r.AnsweredAt,
            AnswerText = r.AnswerText,
            HasAnswerImage = !string.IsNullOrEmpty(r.AnswerImageUrl),
            Rating = r.Rating,
        }).ToList();
    }
}
