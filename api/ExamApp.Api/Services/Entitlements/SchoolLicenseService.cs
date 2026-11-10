using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1092: okul lisansını oluşturur, uzatır / değiştirir, bitirir; her değişiklik <see cref="SchoolLicenseAudit"/>'e yazılır.
/// Kimseye atama yazılmaz, plan çözümü lisansı okur (<see cref="SchoolLicensePlans"/>).
/// </summary>
public interface ISchoolLicenseService
{
    /// <summary><see langword="null"/> = okul yok.</summary>
    Task<SchoolLicenseDto?> GetAsync(int schoolId, CancellationToken ct = default);

    Task<SchoolLicenseError> SaveAsync(int schoolId, SaveSchoolLicenseDto request, int adminUserId, CancellationToken ct = default);

    Task<SchoolLicenseError> EndAsync(int schoolId, int adminUserId, CancellationToken ct = default);
}

public enum SchoolLicenseError
{
    None,
    SchoolNotFound,
    UnknownPlan,
    AudienceMismatch,
    InvalidRange,
    NoteTooLong,
    NoLicense,
}

public sealed class SchoolLicenseService(AppDbContext db, TimeProvider clock) : ISchoolLicenseService
{
    private const int HistoryLimit = 20;

    public async Task<SchoolLicenseDto?> GetAsync(int schoolId, CancellationToken ct = default)
    {
        if (!await db.Schools.AnyAsync(s => s.Id == schoolId, ct))
            return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var license = await db.SchoolLicenses.AsNoTracking()
            .Where(l => l.SchoolId == schoolId)
            .Select(l => new { TeacherPlanKey = l.TeacherPlan.Key, StudentPlanKey = l.StudentPlan.Key, l.ValidFromUtc, l.ValidToUtc, l.Note })
            .FirstOrDefaultAsync(ct);
        var history = await db.SchoolLicenseAudits.AsNoTracking()
            .Where(a => a.SchoolId == schoolId)
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Take(HistoryLimit)
            .Select(a => new SchoolLicenseHistoryDto
            {
                Action = a.Action.ToString(),
                ActorUserId = a.ActorUserId,
                TeacherPlanKey = a.TeacherPlanKey,
                StudentPlanKey = a.StudentPlanKey,
                ValidFromUtc = a.ValidFromUtc,
                ValidToUtc = a.ValidToUtc,
                Note = a.Note,
                OccurredAtUtc = a.OccurredAtUtc,
            })
            .ToListAsync(ct);

        return new SchoolLicenseDto
        {
            SchoolId = schoolId,
            Exists = license is not null,
            Active = license is not null && license.ValidFromUtc <= now && (license.ValidToUtc == null || license.ValidToUtc > now),
            TeacherPlanKey = license?.TeacherPlanKey,
            StudentPlanKey = license?.StudentPlanKey,
            ValidFromUtc = license?.ValidFromUtc,
            ValidToUtc = license?.ValidToUtc,
            Note = license?.Note,
            History = history,
        };
    }

    public async Task<SchoolLicenseError> SaveAsync(int schoolId, SaveSchoolLicenseDto request, int adminUserId, CancellationToken ct = default)
    {
        if (!await db.Schools.AnyAsync(s => s.Id == schoolId, ct))
            return SchoolLicenseError.SchoolNotFound;

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > SchoolLicense.NoteMaxLength })
            return SchoolLicenseError.NoteTooLong;

        var now = clock.GetUtcNow().UtcDateTime;
        var from = request.ValidFromUtc?.ToUniversalTime() ?? now;
        var to = request.ValidToUtc?.ToUniversalTime();
        if (to is { } end && (end <= from || end <= now))
            return SchoolLicenseError.InvalidRange;

        var teacherPlan = await PlanAsync(request.TeacherPlanKey ?? SubscriptionPlan.TeacherProKey, ct);
        var studentPlan = await PlanAsync(request.StudentPlanKey ?? SubscriptionPlan.StudentProKey, ct);
        if (teacherPlan is null || studentPlan is null)
            return SchoolLicenseError.UnknownPlan;
        if (teacherPlan.Audience == SubscriptionPlanAudience.Student || studentPlan.Audience == SubscriptionPlanAudience.Teacher)
            return SchoolLicenseError.AudienceMismatch;

        var license = await db.SchoolLicenses.FirstOrDefaultAsync(l => l.SchoolId == schoolId, ct);
        var action = license is null ? SchoolLicenseAuditAction.Created : SchoolLicenseAuditAction.Updated;
        if (license is null)
        {
            license = new SchoolLicense { SchoolId = schoolId, CreateTime = now, CreateUserId = adminUserId };
            db.SchoolLicenses.Add(license);
        }
        else
        {
            license.UpdateTime = now;
            license.UpdateUserId = adminUserId;
        }
        license.TeacherPlanId = teacherPlan.Id;
        license.StudentPlanId = studentPlan.Id;
        license.ValidFromUtc = from;
        license.ValidToUtc = to;
        license.Note = note;

        db.SchoolLicenseAudits.Add(new SchoolLicenseAudit
        {
            SchoolId = schoolId,
            ActorUserId = adminUserId,
            Action = action,
            TeacherPlanKey = teacherPlan.Key,
            StudentPlanKey = studentPlan.Key,
            ValidFromUtc = from,
            ValidToUtc = to,
            Note = note,
            OccurredAtUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return SchoolLicenseError.None;
    }

    public async Task<SchoolLicenseError> EndAsync(int schoolId, int adminUserId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var license = await db.SchoolLicenses
            .Include(l => l.TeacherPlan).Include(l => l.StudentPlan)
            .FirstOrDefaultAsync(l => l.SchoolId == schoolId, ct);
        if (license is null || license.ValidToUtc <= now)
            return SchoolLicenseError.NoLicense;

        // İleride başlayacak lisans da bitirilir: aralık boş kalır (ValidFrom = ValidTo = şimdi).
        if (license.ValidFromUtc > now)
            license.ValidFromUtc = now;
        license.ValidToUtc = now;
        license.UpdateTime = now;
        license.UpdateUserId = adminUserId;
        db.SchoolLicenseAudits.Add(new SchoolLicenseAudit
        {
            SchoolId = schoolId,
            ActorUserId = adminUserId,
            Action = SchoolLicenseAuditAction.Ended,
            TeacherPlanKey = license.TeacherPlan.Key,
            StudentPlanKey = license.StudentPlan.Key,
            ValidFromUtc = license.ValidFromUtc,
            ValidToUtc = now,
            Note = license.Note,
            OccurredAtUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return SchoolLicenseError.None;
    }

    // Lisans ücretli bir plan verir: Free ve deneme lisans planı olamaz.
    private Task<SubscriptionPlan?> PlanAsync(string key, CancellationToken ct) =>
        db.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key && p.IsActive && p.Key != SubscriptionPlan.FreeKey && p.TrialDays == null, ct);
}
