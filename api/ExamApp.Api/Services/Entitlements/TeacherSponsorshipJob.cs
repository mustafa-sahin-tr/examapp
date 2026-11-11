using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Entitlements;

public class TeacherSponsorshipOptions
{
    public const string SectionName = "TeacherSponsorship";

    /// <summary>Uygun öğrenci kalmayınca planın süreceği gün sayısı.</summary>
    [Range(0, 90)]
    public int GraceDays { get; set; } = 14;

    /// <summary>Günlük değerlendirme (UTC).</summary>
    [Required]
    public string Cron { get; set; } = "30 2 * * *";
}

public sealed record TeacherSponsorshipRunResult(int Started, int Renewed, int GraceStarted, int Ended);

public interface ITeacherSponsorshipJob
{
    Task<TeacherSponsorshipRunResult> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1115: öğrencilerinden en az birinin ücretli öğrenci planı olan öğretmen Teacher Essential'ı ücretsiz kullanır. Günlük:
/// uygun öğretmene sponsorluk açar ya da sürdürür (grace'i temizler); uygunluğu düşene <see cref="TeacherSponsorshipOptions.GraceDays"/>
/// gün grace yazar; grace'i biteni kapatır. Plan atanmaz; çözüm <see cref="SchoolLicensePlans"/>'ta (ücretli Pro kişisel plan kazanır).
///
/// "Öğrencisi": öğretmenin okulunun doğrulanmış üyesi, onaylı randevusu olan ya da öğretmenin doğrudan (öğrenciye) aktif ataması
/// olan öğrenci. "Ücretli plan": şu an geçerli, deneme / ücretsiz olmayan öğrenci planı ataması (admin ataması dahil; okul lisansı
/// sayılmaz — okul lisansının öğretmeni zaten lisanstan plan alır). Öğretmen onaylı ve askıda değil olmalı.
/// </summary>
public sealed class TeacherSponsorshipJob(
    AppDbContext db,
    TimeProvider clock,
    IOptions<TeacherSponsorshipOptions> options,
    ILogger<TeacherSponsorshipJob> logger) : ITeacherSponsorshipJob
{
    public const string RecurringJobId = "teacher-sponsorship";

    public async Task<TeacherSponsorshipRunResult> RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var qualified = (await QualifiedTeacherUserIdsAsync(now, ct)).ToHashSet();
        var open = await db.TeacherSponsorships.AsTracking().Where(x => x.EndedAtUtc == null).ToListAsync(ct);

        int started = 0, renewed = 0, grace = 0, ended = 0;
        foreach (var row in open)
        {
            if (qualified.Remove(row.TeacherUserId))
            {
                row.LastQualifiedAtUtc = now;
                if (row.GraceEndsAtUtc is not null)
                {
                    row.GraceEndsAtUtc = null;
                    renewed++;
                }
            }
            else if (row.GraceEndsAtUtc is null)
            {
                row.GraceEndsAtUtc = now.AddDays(options.Value.GraceDays);
                grace++;
            }
            else if (row.GraceEndsAtUtc <= now)
            {
                row.EndedAtUtc = now;
                ended++;
            }
        }
        foreach (var teacherUserId in qualified)
        {
            db.TeacherSponsorships.Add(new TeacherSponsorship
            {
                TeacherUserId = teacherUserId, StartedAtUtc = now, LastQualifiedAtUtc = now,
            });
            started++;
        }
        await db.SaveChangesAsync(ct);

        var result = new TeacherSponsorshipRunResult(started, renewed, grace, ended);
        logger.LogInformation(
            "Teacher sponsorship: {Started} started, {Renewed} renewed from grace, {Grace} entered grace, {Ended} ended",
            started, renewed, grace, ended);
        return result;
    }

    private async Task<List<int>> QualifiedTeacherUserIdsAsync(DateTime now, CancellationToken ct)
    {
        var paidStudents = db.Students.Where(s => db.UserSubscriptionPlans.Any(a =>
            a.UserId == s.UserId && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now)
            && a.Plan.IsActive && a.Plan.Audience == SubscriptionPlanAudience.Student
            && a.Plan.Key != SubscriptionPlan.FreeKey && a.Plan.Key != SubscriptionPlan.TrialKey));

        var viaSchool = db.Teachers
            .Where(t => t.SchoolId != null && paidStudents.Any(s => s.SchoolVerifiedAt != null && s.SchoolId == t.SchoolId))
            .Select(t => t.UserId);
        var viaBooking = db.Bookings
            .Where(b => b.Status == BookingStatus.Approved && paidStudents.Any(s => s.Id == b.StudentId))
            .Join(db.Teachers, b => b.TeacherId, t => t.Id, (b, t) => t.UserId);
        var viaAssignment = db.WorksheetAssignments
            .Where(WorksheetAccess.ActiveAt(now))
            .Where(a => a.StudentId != null && a.CreateUserId != null && paidStudents.Any(s => s.Id == a.StudentId))
            .Select(a => a.CreateUserId!.Value);

        var candidates = viaSchool.Union(viaBooking).Union(viaAssignment);
        return await db.Teachers
            .Where(t => t.AccountApprovedAt != null && t.AccountSuspendedAt == null && candidates.Contains(t.UserId))
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(ct);
    }
}
