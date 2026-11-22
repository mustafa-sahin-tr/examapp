using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.Tenancy;

public enum SchoolAnnouncementStatus
{
    Success,

    /// <summary>Gönderen okulun onaylı öğretmeni değil ya da hedef yalnız okul yöneticisine açık.</summary>
    Forbidden,

    /// <summary>Şube / öğrenci bu okulda değil (varlık sızdırılmaz).</summary>
    NotFound,

    Invalid,

    /// <summary>Hedefte gönderen dışında alıcı yok.</summary>
    NoRecipients,

    /// <summary>Gönderenin son 24 saatteki duyuru sınırı doldu.</summary>
    RateLimited
}

public sealed record SchoolAnnouncementResult(SchoolAnnouncementStatus Status, SchoolAnnouncementDto? Announcement = null);

/// <summary>
/// issue #1281 (#1071 S6): okul duyurusu. Okul DB'den çözülür (<see cref="ISchoolSectionService.ResolveTeacherSchoolIdAsync"/>:
/// okulun onaylı, askısız, bağımsız olmayan öğretmeni); okul geneli ve sınıf hedefleri yalnız okul yöneticisine
/// (<see cref="ISchoolAdminResolver"/>), şube ve seçili öğrenci hedefleri okulun her öğretmenine açık (S4'te şubeleri ve
/// öğrencilerini zaten görüyorlar). Alıcılar: hedeflenen doğrulanmış öğrenciler, okul genelinde okulun öğretmenleri, hedeflenen
/// öğrencilerin Active velileri; gönderen hariç, kullanıcı başına tek. Duyuru satırı ve parçalı
/// <see cref="AnnouncementPublishedEvent"/>'ler aynı transaction'da yazılır.
/// </summary>
public interface ISchoolAnnouncementService
{
    Task<SchoolAnnouncementOptionsDto?> GetOptionsAsync(int senderUserId, CancellationToken ct = default);

    Task<SchoolAnnouncementResult> SendAsync(int senderUserId, SchoolAnnouncementSendRequestDto request, CancellationToken ct = default);

    /// <summary>Gönderenin kendi duyuruları (okul değişse de), yeniden eskiye.</summary>
    Task<Paged<SchoolAnnouncementDto>> ListSentAsync(int senderUserId, int page, int pageSize, CancellationToken ct = default);
}

public class SchoolAnnouncementService : ISchoolAnnouncementService
{
    public const int MaxStudentIds = 200;
    public const int DailyLimit = 20;

    /// <summary>Binlerce alıcıda auth-api toplu çözümüne verilen süre (yetişmezse sub'lar BadgeService'te çözülür).</summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(10);

    private readonly AppDbContext _context;
    private readonly ISchoolSectionService _sections;
    private readonly ISchoolAdminResolver _admins;
    private readonly IAuthApiClient? _authApi;
    private readonly ILogger<SchoolAnnouncementService> _logger;
    private readonly TimeProvider _time;

    public SchoolAnnouncementService(
        AppDbContext context,
        ISchoolSectionService sections,
        ISchoolAdminResolver admins,
        IAuthApiClient? authApi = null,
        ILogger<SchoolAnnouncementService>? logger = null,
        TimeProvider? time = null)
    {
        _context = context;
        _sections = sections;
        _admins = admins;
        _authApi = authApi;
        _logger = logger ?? NullLogger<SchoolAnnouncementService>.Instance;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SchoolAnnouncementOptionsDto?> GetOptionsAsync(int senderUserId, CancellationToken ct = default)
    {
        if (await _sections.ResolveTeacherSchoolIdAsync(senderUserId, ct) is not int schoolId)
            return null;
        var name = await _context.Schools.AsNoTracking().Where(s => s.Id == schoolId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        return new SchoolAnnouncementOptionsDto
        {
            SchoolName = name ?? string.Empty,
            CanTargetSchoolAndGrade = await _admins.IsSchoolAdminAsync(senderUserId, schoolId, ct)
        };
    }

    public async Task<SchoolAnnouncementResult> SendAsync(int senderUserId, SchoolAnnouncementSendRequestDto request,
        CancellationToken ct = default)
    {
        if (await _sections.ResolveTeacherSchoolIdAsync(senderUserId, ct) is not int schoolId)
            return new(SchoolAnnouncementStatus.Forbidden);

        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;
        if (title.Length == 0 || title.Length > SchoolAnnouncement.TitleMaxLength
            || body.Length == 0 || body.Length > SchoolAnnouncement.BodyMaxLength
            || !Enum.TryParse<SchoolAnnouncementTarget>(request.Target, ignoreCase: false, out var target)
            || !Enum.IsDefined(target))
            return new(SchoolAnnouncementStatus.Invalid);

        if (target is SchoolAnnouncementTarget.School or SchoolAnnouncementTarget.Grade
            && !await _admins.IsSchoolAdminAsync(senderUserId, schoolId, ct))
            return new(SchoolAnnouncementStatus.Forbidden);

        // Okulun doğrulanmış öğrencileri (silinmişler global filtreyle düşer).
        var verified = _context.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SchoolVerifiedAt != null);
        IQueryable<Student> students;
        string? label = null;
        int? gradeId = null, sectionId = null;
        switch (target)
        {
            case SchoolAnnouncementTarget.School:
                students = verified;
                break;
            case SchoolAnnouncementTarget.Grade:
                if (request.GradeId is not int g)
                    return new(SchoolAnnouncementStatus.Invalid);
                label = await _context.Grades.AsNoTracking().Where(x => x.Id == g).Select(x => x.Name).FirstOrDefaultAsync(ct);
                if (label == null)
                    return new(SchoolAnnouncementStatus.Invalid);
                gradeId = g;
                students = verified.Where(s => s.GradeId == g);
                break;
            case SchoolAnnouncementTarget.Section:
                if (request.SectionId is not int sid)
                    return new(SchoolAnnouncementStatus.Invalid);
                var section = await _context.SchoolSections.AsNoTracking()
                    .Where(s => s.Id == sid && s.SchoolId == schoolId)
                    .Select(s => new { s.Id, s.GradeId, s.Name, GradeName = s.Grade.Name })
                    .FirstOrDefaultAsync(ct);
                if (section == null)
                    return new(SchoolAnnouncementStatus.NotFound);
                (gradeId, sectionId, label) = (section.GradeId, section.Id, $"{section.GradeName} / {section.Name}");
                var memberIds = _context.SchoolSectionMemberships
                    .Where(m => m.SectionId == section.Id && m.EndedAt == null)
                    .Select(m => m.StudentId);
                students = verified.Where(s => memberIds.Contains(s.Id));
                break;
            default: // Students
                var ids = request.StudentIds?.Distinct().ToList() ?? [];
                if (ids.Count == 0 || ids.Count > MaxStudentIds || ids.Any(id => id <= 0))
                    return new(SchoolAnnouncementStatus.Invalid);
                if (await verified.CountAsync(s => ids.Contains(s.Id), ct) != ids.Count)
                    return new(SchoolAnnouncementStatus.NotFound); // hiçbiri yazılmaz
                students = verified.Where(s => ids.Contains(s.Id));
                break;
        }

        var since = _time.GetUtcNow().UtcDateTime.AddDays(-1);
        if (await _context.SchoolAnnouncements.CountAsync(a => a.SenderUserId == senderUserId && a.CreatedAt > since, ct) >= DailyLimit)
            return new(SchoolAnnouncementStatus.RateLimited);

        var plan = await PlanRecipientsAsync(schoolId, senderUserId, students, target == SchoolAnnouncementTarget.School, ct);
        if (plan.Recipients.Count == 0)
            return new(SchoolAnnouncementStatus.NoRecipients);

        var now = _time.GetUtcNow().UtcDateTime;
        var users = await ParentNotificationSupport.LookupAsync(_authApi,
            plan.Recipients.Select(r => r.UserId).Concat(plan.StudentUserIds.Values).Append(senderUserId), _logger, ct, LookupTimeout);
        foreach (var r in plan.Recipients)
        {
            var user = ParentNotificationSupport.Of(users, r.UserId);
            r.KeycloakId = user.KeycloakId;
            if (r.Audience == AnnouncementPublishedEvent.ParentAudience && plan.ParentChildCount[r.UserId] == 1
                && r.StudentId is int child && plan.StudentUserIds.TryGetValue(child, out var childUserId))
                r.StudentDisplayName = ParentNotificationSupport.Of(users, childUserId).DisplayName;
        }

        var schoolName = await _context.Schools.AsNoTracking().Where(s => s.Id == schoolId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var announcement = new SchoolAnnouncement
        {
            SchoolId = schoolId,
            SenderUserId = senderUserId,
            Target = target,
            TargetGradeId = gradeId,
            TargetSectionId = sectionId,
            TargetLabel = label is { Length: > SchoolAnnouncement.TargetLabelMaxLength }
                ? label[..SchoolAnnouncement.TargetLabelMaxLength]
                : label,
            Title = title,
            Body = body,
            StudentCount = plan.Recipients.Count(r => r.Audience == AnnouncementPublishedEvent.StudentAudience),
            TeacherCount = plan.Recipients.Count(r => r.Audience == AnnouncementPublishedEvent.TeacherAudience),
            ParentCount = plan.Recipients.Count(r => r.Audience == AnnouncementPublishedEvent.ParentAudience),
            CreatedAt = now
        };

        var chunks = plan.Recipients.Chunk(AnnouncementPublishedEvent.MaxRecipientsPerEvent).ToList();
        var sender = ParentNotificationSupport.Of(users, senderUserId).DisplayName;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);
            _context.SchoolAnnouncements.Add(announcement);
            await _context.SaveChangesAsync(CancellationToken.None);
            for (var i = 0; i < chunks.Count; i++)
            {
                _context.OutboxMessages.Add(new OutboxMessage
                {
                    Type = OutboxEventRegistry.NameFor<AnnouncementPublishedEvent>(),
                    Content = JsonSerializer.Serialize(new AnnouncementPublishedEvent
                    {
                        EventId = Guid.NewGuid(),
                        AnnouncementId = announcement.Id,
                        SchoolId = schoolId,
                        SchoolName = schoolName ?? string.Empty,
                        SenderDisplayName = sender,
                        Title = title,
                        Body = body,
                        ChunkIndex = i,
                        ChunkCount = chunks.Count,
                        PublishedAtUtc = now,
                        Recipients = chunks[i].ToList()
                    })
                });
            }

            await _context.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        });

        _logger.LogInformation(
            "Duyuru gönderildi. AnnouncementId={Id}, SchoolId={SchoolId}, Target={Target}, Students={Students}, Teachers={Teachers}, Parents={Parents}, Chunks={Chunks}",
            announcement.Id, schoolId, target, announcement.StudentCount, announcement.TeacherCount, announcement.ParentCount, chunks.Count);
        return new(SchoolAnnouncementStatus.Success, ToDto(announcement));
    }

    public async Task<Paged<SchoolAnnouncementDto>> ListSentAsync(int senderUserId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);
        var query = _context.SchoolAnnouncements.AsNoTracking().Where(a => a.SenderUserId == senderUserId);
        var totalCount = await query.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<SchoolAnnouncementDto>(page, pageSize, totalCount);
        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Skip(offset).Take(pageSize).ToListAsync(ct);
        return new Paged<SchoolAnnouncementDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(ToDto).ToList()
        };
    }

    private sealed record RecipientPlan(
        List<AnnouncementRecipient> Recipients,
        Dictionary<int, int> StudentUserIds,
        Dictionary<int, int> ParentChildCount);

    /// <summary>Öğrenciler → (okul genelinde) öğretmenler → Active veliler; gönderen hariç, kullanıcı başına ilk rol.</summary>
    private async Task<RecipientPlan> PlanRecipientsAsync(int schoolId, int senderUserId, IQueryable<Student> students,
        bool includeTeachers, CancellationToken ct)
    {
        var studentRows = await students.Select(s => new { s.Id, s.UserId }).ToListAsync(ct);
        var studentIds = studentRows.Select(s => s.Id).ToList();
        var teacherUserIds = includeTeachers
            ? await _context.Teachers.AsNoTracking()
                .Where(t => t.SchoolId == schoolId && t.AccountApprovedAt != null && t.AccountSuspendedAt == null
                            && !t.IsIndependentTutor)
                .Select(t => t.UserId)
                .ToListAsync(ct)
            : [];
        // ParentNotificationSupport.ActiveParentsAsync ile aynı koşul, toplu.
        var parentRows = studentIds.Count == 0
            ? []
            : await _context.ParentStudentLinks.AsNoTracking()
                .Where(l => studentIds.Contains(l.StudentId) && l.Status == ParentStudentLinkStatus.Active && !l.Parent.IsDeleted)
                .OrderBy(l => l.StudentId)
                .Select(l => new { l.StudentId, ParentUserId = l.Parent.UserId })
                .ToListAsync(ct);

        var seen = new HashSet<int> { senderUserId };
        var recipients = new List<AnnouncementRecipient>();
        void Add(string audience, int userId, int? studentId = null)
        {
            if (userId > 0 && seen.Add(userId))
                recipients.Add(new AnnouncementRecipient
                {
                    NotificationId = Guid.NewGuid(),
                    Audience = audience,
                    UserId = userId,
                    StudentId = studentId
                });
        }

        foreach (var s in studentRows)
            Add(AnnouncementPublishedEvent.StudentAudience, s.UserId);
        foreach (var t in teacherUserIds)
            Add(AnnouncementPublishedEvent.TeacherAudience, t);
        foreach (var p in parentRows)
            Add(AnnouncementPublishedEvent.ParentAudience, p.ParentUserId, p.StudentId);

        return new RecipientPlan(
            recipients,
            studentRows.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().UserId),
            parentRows.GroupBy(p => p.ParentUserId).ToDictionary(g => g.Key, g => g.Select(p => p.StudentId).Distinct().Count()));
    }

    private static SchoolAnnouncementDto ToDto(SchoolAnnouncement a) => new()
    {
        Id = a.Id,
        Target = a.Target.ToString(),
        TargetLabel = a.TargetLabel,
        Title = a.Title,
        Body = a.Body,
        StudentCount = a.StudentCount,
        TeacherCount = a.TeacherCount,
        ParentCount = a.ParentCount,
        CreatedAt = a.CreatedAt
    };
}
