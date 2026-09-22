using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Services.Teachers.Seed;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Seed.Cleanup;

/// <summary>
/// Bkz. <see cref="ISeedCleanupService"/>. Akış: ortam guard'ı → envanter (seed okul/öğretmen/tutor, bağımlı veri
/// sayımları — tek tek değil, gruplu sorgular) → karar (atla / sil / --force) → exam yazma (tek transaction,
/// <c>ExecuteDelete</c>: SaveChanges soft-delete interceptor'ından geçmez, gerçek DELETE) → okullar → auth-api
/// (<c>ExcludeUserIds</c> = atlanan öğretmenlerin identity id'leri; auth-api içinde Keycloak → identity).
///
/// <para>Neden hard delete: seed satırları benzersiz indekslerde (School.ExternalCode, identity e-posta) yer tutar;
/// soft-delete kalıntısı yeniden koşuda "Existing" sayılırdı. Neden exam önce: exam tarafı hangi hesapların
/// KORUNACAĞINI (bağımlı veri) belirler; auth-api yalnızca daraltma listesini alır. Kısmi hatada ikinci koşu
/// kalanı bulur — her sistem kendi işaretinden (IsSeedData / seed alanı) okur, önceki koşuya bağımlı değildir.</para>
/// </summary>
public sealed class SeedCleanupService : ISeedCleanupService
{
    private readonly AppDbContext _context;
    private readonly IAuthApiSeedClient _authApi;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SeedCleanupService> _logger;

    public SeedCleanupService(AppDbContext context, IAuthApiSeedClient authApi, IHostEnvironment environment, ILogger<SeedCleanupService> logger)
    {
        _context = context;
        _authApi = authApi;
        _environment = environment;
        _logger = logger;
    }

    private sealed record SeedTeacherRow(
        int Id, int UserId, int? SchoolId, bool IsIndependentTutor, TeacherApprovalStatus ApprovalStatus,
        string? ProvinceName, string? SchoolName, List<string> Subjects);

    public async Task<SeedCleanupResult> RunAsync(SeedCleanupOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!SeedCommands.IsAllowedEnvironment(_environment))
        {
            throw new InvalidOperationException(
                $"seed-cleanup yalnızca Development/Staging ortamında çalışır; mevcut ortam: '{_environment.EnvironmentName}'.");
        }

        var total = Stopwatch.StartNew();
        var result = new SeedCleanupResult { Applied = options.Apply, Force = options.Force, IncludeOrphans = options.IncludeOrphans };

        // ================= 1) Envanter (yalnızca okuma; soft-delete kalıntıları dahil) =================
        var teachers = (await _context.Teachers
                .IgnoreQueryFilters()
                .Where(t => t.IsSeedData)
                .Select(t => new
                {
                    t.Id, t.UserId, t.SchoolId, t.IsIndependentTutor, t.ApprovalStatus,
                    ProvinceName = t.School != null && t.School.Province != null ? t.School.Province.Name : null,
                    SchoolName = t.School != null ? t.School.Name : null,
                    Subjects = t.TeacherSubjects.Select(ts => ts.Subject.Name).ToList()
                })
                .ToListAsync(ct))
            .Select(t => new SeedTeacherRow(t.Id, t.UserId, t.SchoolId, t.IsIndependentTutor, t.ApprovalStatus, t.ProvinceName, t.SchoolName, t.Subjects))
            .ToList();

        result.SeedSchoolTeachers = teachers.Count(t => !t.IsIndependentTutor);
        result.SeedTutors = teachers.Count(t => t.IsIndependentTutor);
        result.SeedTutorsPending = teachers.Count(t => t.IsIndependentTutor && t.ApprovalStatus == TeacherApprovalStatus.Pending);

        // Bağımlı veri — öğretmen başına ayrı sorgu değil, gruplu sayım (N+1 yok).
        // Booking'ler gerçek öğrencilere aittir (Student'ta IsSeedData yok; öğrenci seed'i gelirse burada
        // `b.Student.IsSeedData` ile ayrılabilir) → bu satırlar HİÇBİR modda silinmez, öğretmen atlanır.
        var bookings = await _context.Bookings.IgnoreQueryFilters()
            .Where(b => b.Teacher.IsSeedData)
            .GroupBy(b => b.TeacherId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var slots = await _context.TeacherAvailabilitySlots.IgnoreQueryFilters()
            .Where(s => s.Teacher.IsSeedData)
            .GroupBy(s => s.TeacherId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var rules = await _context.RecurringAvailabilityRules.IgnoreQueryFilters()
            .Where(r => r.Teacher.IsSeedData)
            .GroupBy(r => r.TeacherId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var worksheets = await _context.Worksheets.IgnoreQueryFilters()
            .Where(w => w.CreateUserId != null && _context.Teachers.Any(t => t.IsSeedData && t.UserId == w.CreateUserId))
            .GroupBy(w => w.CreateUserId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var questions = await _context.Questions.IgnoreQueryFilters()
            .Where(q => q.CreateUserId != null && _context.Teachers.Any(t => t.IsSeedData && t.UserId == q.CreateUserId))
            .GroupBy(q => q.CreateUserId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // ================= 2) Karar =================
        var skipIds = new List<int>();
        var skipUserIds = new List<int>();
        foreach (var t in teachers)
        {
            var realBookings = bookings.GetValueOrDefault(t.Id);
            var scheduling = slots.GetValueOrDefault(t.Id) + rules.GetValueOrDefault(t.Id);
            var content = worksheets.GetValueOrDefault(t.UserId) + questions.GetValueOrDefault(t.UserId);
            var label = $"Teacher #{t.Id} (user {t.UserId}, {(t.IsIndependentTutor ? "bağımsız" : t.SchoolName ?? "okul?")})";

            if (realBookings > 0)
            {
                // Seed-dışı satıra dokunulmaz: gerçek öğrencinin randevusu varsa --force bile öğretmeni silmez.
                skipIds.Add(t.Id);
                skipUserIds.Add(t.UserId);
                result.TeachersSkippedRealStudentBooking++;
                result.RealStudentBookings += realBookings;
                AddSample(result.Skipped, $"{label}: gerçek öğrenci randevusu={realBookings} — atlandı (--force ile de silinmez)");
                continue;
            }

            if (!options.Force && (scheduling > 0 || content > 0))
            {
                skipIds.Add(t.Id);
                skipUserIds.Add(t.UserId);
                if (scheduling > 0) result.TeachersSkippedScheduling++;
                else result.TeachersSkippedContent++;
                AddSample(result.Skipped, $"{label}: müsaitlik={scheduling} worksheet/soru={content} — atlandı (--force ile müsaitlik verisi silinir)");
                continue;
            }

            if (scheduling > 0) result.TeachersForceDeleted++;
            if (content > 0)
            {
                result.TeachersOrphanedContent++;
                result.WorksheetsOrphaned += worksheets.GetValueOrDefault(t.UserId);
                result.QuestionsOrphaned += questions.GetValueOrDefault(t.UserId);
                AddSample(result.Skipped, $"{label}: worksheet/soru={content} yerinde kalır (sahipsiz) — --force");
            }
        }
        var deletableCount = teachers.Count - skipIds.Count;

        // Okullar: seed okul; bağlı seed-dışı öğretmen / öğrenci / atama ya da korunan seed öğretmen varsa atlanır.
        var seedSchools = await _context.Schools.IgnoreQueryFilters()
            .Where(s => s.IsSeedData)
            .Select(s => new { s.Id, s.Name, Province = s.Province != null ? s.Province.Name : null })
            .ToListAsync(ct);
        result.SeedSchools = seedSchools.Count;

        var nonSeedTeacherSchools = (await _context.Teachers.IgnoreQueryFilters()
            .Where(t => t.SchoolId != null && t.School!.IsSeedData && !t.IsSeedData)
            .Select(t => t.SchoolId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var keptSeedTeacherSchools = skipIds.Count == 0
            ? new HashSet<int>()
            : (await _context.Teachers.IgnoreQueryFilters()
                .Where(t => t.SchoolId != null && skipIds.Contains(t.Id))
                .Select(t => t.SchoolId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var studentSchools = (await _context.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId != null && s.School!.IsSeedData)
            .Select(s => s.SchoolId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var assignmentSchools = (await _context.WorksheetAssignments.IgnoreQueryFilters()
            .Where(a => a.SchoolId != null && a.School!.IsSeedData)
            .Select(a => a.SchoolId!.Value).Distinct().ToListAsync(ct)).ToHashSet();

        var blockedSchoolIds = new List<int>();
        foreach (var s in seedSchools)
        {
            var reasons = new List<string>();
            if (nonSeedTeacherSchools.Contains(s.Id)) { reasons.Add("seed-dışı öğretmen"); result.SchoolsSkippedNonSeedTeacher++; }
            if (studentSchools.Contains(s.Id)) { reasons.Add("öğrenci"); result.SchoolsSkippedStudent++; }
            if (assignmentSchools.Contains(s.Id)) { reasons.Add("worksheet ataması"); result.SchoolsSkippedAssignment++; }
            if (keptSeedTeacherSchools.Contains(s.Id)) { reasons.Add("korunan seed öğretmen"); result.SchoolsSkippedSeedTeacherKept++; }
            if (reasons.Count == 0) continue;
            blockedSchoolIds.Add(s.Id);
            AddSample(result.Skipped, $"School #{s.Id} '{s.Name}' ({s.Province ?? "?"}): {string.Join(", ", reasons)} — atlandı");
        }

        // ================= 3) Exam yazma (tek transaction) =================
        var examSw = Stopwatch.StartNew();
        if (options.Apply)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                if (options.Force)
                {
                    // Booking silinmez; booking'li öğretmenler skipIds içinde olduğundan slot FK (Restrict) ihlali olmaz.
                    result.SlotsDeleted = await _context.TeacherAvailabilitySlots.IgnoreQueryFilters()
                        .Where(s => s.Teacher.IsSeedData && !skipIds.Contains(s.TeacherId)).ExecuteDeleteAsync(ct);
                    result.RulesDeleted = await _context.RecurringAvailabilityRules.IgnoreQueryFilters()
                        .Where(r => r.Teacher.IsSeedData && !skipIds.Contains(r.TeacherId)).ExecuteDeleteAsync(ct);
                }

                result.TeacherSubjectsDeleted = await _context.TeacherSubjects.IgnoreQueryFilters()
                    .Where(ts => ts.Teacher.IsSeedData && !skipIds.Contains(ts.TeacherId)).ExecuteDeleteAsync(ct);
                result.TeachersDeleted = await _context.Teachers.IgnoreQueryFilters()
                    .Where(t => t.IsSeedData && !skipIds.Contains(t.Id)).ExecuteDeleteAsync(ct);
                result.SchoolsDeleted = await _context.Schools.IgnoreQueryFilters()
                    .Where(s => s.IsSeedData && !blockedSchoolIds.Contains(s.Id)).ExecuteDeleteAsync(ct);

                await tx.CommitAsync(ct);
            });
        }
        else
        {
            result.TeachersDeleted = deletableCount;
            result.SchoolsDeleted = seedSchools.Count - blockedSchoolIds.Count;
        }
        result.ExamDbElapsedMs = examSw.ElapsedMilliseconds;

        // ================= 4) auth-api: Keycloak + identity =================
        var emailByUserId = new Dictionary<int, string>();
        if (!options.SkipAuthApi)
        {
            var authSw = Stopwatch.StartNew();
            try
            {
                var response = await _authApi.CleanupSeedUsersAsync(new DevSeedCleanupRequest
                {
                    DryRun = !options.Apply,
                    ExcludeUserIds = skipUserIds,
                    IncludeOrphans = options.IncludeOrphans
                }, ct);

                result.AuthApiCalled = true;
                result.KeycloakDeleted = response.KeycloakDeleted;
                result.KeycloakMissing = response.KeycloakMissing;
                result.KeycloakExcluded = response.KeycloakExcluded;
                result.KeycloakSkippedForeign = response.KeycloakSkippedForeign;
                result.KeycloakFailed = response.KeycloakFailed;
                result.KeycloakOrphans = response.KeycloakOrphans;
                result.IdentityDeleted = response.IdentityDeleted;
                result.IdentityExcluded = response.IdentityExcluded;
                result.IdentityFailed = response.IdentityFailed;
                result.AuthPlanned = response.Users.Count(u => u.IdentityStatus == DevSeedCleanupResponse.StatusPlanned);

                foreach (var u in response.Users)
                {
                    if (u.UserId is { } id) emailByUserId[id] = u.Email;
                    if (u.KeycloakStatus == DevSeedCleanupResponse.StatusFailed || u.IdentityStatus == DevSeedCleanupResponse.StatusFailed)
                        AddSample(result.Errors, $"{u.Email}: {u.Error}");
                }
            }
            catch (TeacherSeedAuthApiException ex)
            {
                result.AuthApiError = ex.Message;
                AddSample(result.Errors, "auth-api: " + ex.Message);
                _logger.LogError(ex, "seed-cleanup: auth-api çağrısı başarısız (exam tarafı tamamlandı; tekrar koşu Keycloak/identity'yi temizler).");
            }
            result.AuthApiElapsedMs = authSw.ElapsedMilliseconds;
        }

        // ================= 5) Özet rapor grupları =================
        var schoolGroups = new Dictionary<(string, string), int>();
        foreach (var s in seedSchools)
        {
            var kind = TeacherSeedPlan.ClassifyBySchoolName(s.Name) switch
            {
                SchoolSeedKind.Ilkokul => "İlkokul",
                SchoolSeedKind.Ortaokul => "Ortaokul",
                _ => "Diğer"
            };
            var key = (s.Province ?? "?", kind);
            schoolGroups[key] = schoolGroups.GetValueOrDefault(key) + 1;
        }
        result.SchoolGroups = schoolGroups
            .OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .Select(kv => new SeedCleanupSchoolGroup { Province = kv.Key.Item1, Kind = kv.Key.Item2, Count = kv.Value })
            .ToList();

        // Tutor'un ili exam DB'de yok (SchoolId=null): e-postadaki slug → Province adı.
        var provinceNameBySlug = (await _context.Provinces.AsNoTracking().Select(p => p.Name).ToListAsync(ct))
            .GroupBy(TutorSeedPlan.ProvinceSlug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var teacherGroups = new Dictionary<(string, string), SeedCleanupTeacherGroup>();
        foreach (var t in teachers)
        {
            string province;
            if (t.IsIndependentTutor)
            {
                var slug = emailByUserId.TryGetValue(t.UserId, out var email) ? TutorSeedPlan.ProvinceSlugFromEmail(email) : null;
                province = slug is null ? "?" : provinceNameBySlug.GetValueOrDefault(slug, slug);
            }
            else
                province = t.ProvinceName ?? "?";

            var subjects = t.Subjects.Count == 0 ? new List<string> { "(ders yok)" } : t.Subjects;
            foreach (var subject in subjects)
            {
                var key = (province, subject);
                if (!teacherGroups.TryGetValue(key, out var g))
                    teacherGroups[key] = g = new SeedCleanupTeacherGroup { Province = province, Subject = subject };
                if (t.IsIndependentTutor) g.Tutors++; else g.SchoolTeachers++;
            }
        }
        result.TeacherGroups = teacherGroups.Values
            .OrderBy(g => g.Province, StringComparer.Ordinal).ThenBy(g => g.Subject, StringComparer.Ordinal)
            .ToList();

        result.TotalElapsedMs = total.ElapsedMilliseconds;
        _logger.LogInformation(
            "seed-cleanup {Mode} force={Force}: teacher sil={TDel} atla(randevu)={TSched} atla(içerik)={TContent} force={TForce} " +
            "okul sil={SDel} atla={SSkip} kc sil={Kc} kcEksik={KcMissing} kcHata={KcFail} identity sil={Id} idHata={IdFail} authHata={AuthErr}",
            options.Apply ? "APPLY" : "DRY-RUN", options.Force, result.TeachersDeleted, result.TeachersSkippedScheduling, result.TeachersSkippedContent,
            result.TeachersForceDeleted, result.SchoolsDeleted, blockedSchoolIds.Count, result.KeycloakDeleted, result.KeycloakMissing, result.KeycloakFailed,
            result.IdentityDeleted, result.IdentityFailed, result.AuthApiError);
        return result;
    }

    private static void AddSample(List<string> list, string line)
    {
        if (list.Count < SeedCleanupResult.SampleLimit) list.Add(line);
    }
}
