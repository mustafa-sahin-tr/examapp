using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1092: kullanıcının okul lisansından gelen planı. Okul <c>UserSchoolResolver</c>'ın kuralıyla bulunur: canlı öğretmen satırı
/// varsa onun <c>SchoolId</c>'si (öğretmen planı), yoksa öğrenci satırının DOĞRULANMIŞ okulu (öğrenci planı); birden fazla canlı satır
/// belirsizdir → lisans yok. Lisans o an geçerli (<c>ValidFromUtc &lt;= now &lt; ValidToUtc</c>) ve planı etkin olmalı.
/// </summary>
public static class SchoolLicensePlans
{
    /// <summary>Lisans planı ve sıralaması (<see cref="SubscriptionPlan.SortOrder"/>: aynı kitlede büyük olan üst kademe).</summary>
    public sealed record LicensePlan(ResolvedPlan Plan, int SortOrder);

    public static async Task<Dictionary<int, LicensePlan>> ForUsersAsync(
        AppDbContext db, IReadOnlyCollection<int> userIds, DateTime now, CancellationToken ct = default)
    {
        var result = new Dictionary<int, LicensePlan>();
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return result;

        var teacherRows = await db.Teachers.AsNoTracking()
            .Where(t => ids.Contains(t.UserId))
            .Select(t => new { t.UserId, t.SchoolId })
            .ToListAsync(ct);
        await AddStudentSponsorshipsAsync(db, teacherRows.Select(t => t.UserId).Distinct().ToList(), now, result, ct);
        var withoutTeacherRow = ids.Except(teacherRows.Select(t => t.UserId)).ToList();
        var studentRows = withoutTeacherRow.Count == 0
            ? []
            : await db.Students.AsNoTracking()
                .Where(s => withoutTeacherRow.Contains(s.UserId))
                .Select(s => new { s.UserId, SchoolId = s.SchoolVerifiedAt != null ? s.SchoolId : null })
                .ToListAsync(ct);

        // user → (okul, öğretmen mi); belirsiz (birden fazla canlı satır) ya da okulsuz olan listeye girmez.
        var memberships = teacherRows.Select(t => (t.UserId, t.SchoolId, IsTeacher: true))
            .Concat(studentRows.Select(s => (s.UserId, s.SchoolId, IsTeacher: false)))
            .GroupBy(m => m.UserId)
            .Where(g => g.Count() == 1 && g.First().SchoolId is not null)
            .Select(g => g.First())
            .ToList();
        if (memberships.Count == 0)
            return result;

        var schoolIds = memberships.Select(m => m.SchoolId!.Value).Distinct().ToList();
        var licenses = await db.SchoolLicenses.AsNoTracking()
            .Where(l => schoolIds.Contains(l.SchoolId) && l.ValidFromUtc <= now && (l.ValidToUtc == null || l.ValidToUtc > now))
            .Select(l => new
            {
                l.SchoolId,
                l.ValidToUtc,
                Teacher = new { l.TeacherPlan.Id, l.TeacherPlan.Key, l.TeacherPlan.Name, l.TeacherPlan.SortOrder, l.TeacherPlan.IsActive },
                Student = new { l.StudentPlan.Id, l.StudentPlan.Key, l.StudentPlan.Name, l.StudentPlan.SortOrder, l.StudentPlan.IsActive },
            })
            .ToListAsync(ct);
        var bySchool = licenses.ToDictionary(l => l.SchoolId);

        foreach (var m in memberships)
        {
            if (!bySchool.TryGetValue(m.SchoolId!.Value, out var license))
                continue;
            var plan = m.IsTeacher ? license.Teacher : license.Student;
            if (!plan.IsActive)
                continue;
            // Issue #1115: öğrenci sponsorluğu lisanstan üst kademedeyse kalır.
            if (result.TryGetValue(m.UserId, out var sponsored) && sponsored.SortOrder > plan.SortOrder)
                continue;
            result[m.UserId] = new LicensePlan(
                new ResolvedPlan(plan.Id, plan.Key, plan.Name, license.ValidToUtc, FromSchoolLicense: true), plan.SortOrder);
        }
        return result;
    }

    /// <summary>
    /// Issue #1115: açık sponsorluğu olan öğretmene Teacher Essential (grace'teyse bitişi <see cref="ResolvedPlan.ValidToUtc"/>).
    /// Grace'i geçmiş ama iş henüz kapatmamış satır sayılmaz.
    /// </summary>
    private static async Task AddStudentSponsorshipsAsync(
        AppDbContext db, IReadOnlyCollection<int> teacherUserIds, DateTime now, Dictionary<int, LicensePlan> result, CancellationToken ct)
    {
        if (teacherUserIds.Count == 0)
            return;
        var sponsored = await db.TeacherSponsorships.AsNoTracking()
            .Where(x => teacherUserIds.Contains(x.TeacherUserId) && x.EndedAtUtc == null
                        && (x.GraceEndsAtUtc == null || x.GraceEndsAtUtc > now))
            .Select(x => new { x.TeacherUserId, x.GraceEndsAtUtc })
            .ToListAsync(ct);
        if (sponsored.Count == 0)
            return;
        var plan = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.Key == SubscriptionPlan.TeacherEssentialKey && p.IsActive)
            .Select(p => new { p.Id, p.Key, p.Name, p.SortOrder })
            .FirstOrDefaultAsync(ct);
        if (plan is null)
            return;
        foreach (var x in sponsored)
        {
            result[x.TeacherUserId] = new LicensePlan(
                new ResolvedPlan(plan.Id, plan.Key, plan.Name, x.GraceEndsAtUtc, FromStudentSponsorship: true), plan.SortOrder);
        }
    }

    /// <summary>
    /// Çözüm sırası: kişisel atama lisans planından üst kademedeyse (<see cref="SubscriptionPlan.SortOrder"/> büyük) kişisel, değilse
    /// lisans. Deneme ve Free her ücretli plandan aşağıdadır; lisanslı üyeye sonradan alınan daha düşük plan onu düşürmez.
    /// </summary>
    public static ResolvedPlan? Pick(ResolvedPlan? personal, int? personalSortOrder, LicensePlan? license)
    {
        if (license is null)
            return personal;
        if (personal is not null && personalSortOrder > license.SortOrder)
            return personal;
        return license.Plan;
    }
}
