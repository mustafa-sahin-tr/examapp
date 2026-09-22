using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Services.Seed;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// Bkz. <see cref="ITutorSeedService"/>. Akış: ortam guard'ı → parola → il eşleştirme → il başına seed okullar
/// (<c>seed-teachers</c> ile aynı sıralama/limit) → tabandaki seed okul öğretmenleri il × branş sayımı →
/// <c>floor(n/2)</c> plan (deterministik e-posta/ad/profil) → auth-api parti parti (SchoolId=null) →
/// exam <c>Teacher</c> (tutor profili) + <c>TeacherSubject</c> upsert.
///
/// <para>İdempotency: e-posta deterministik; mevcut Teacher <c>UserId</c> ile bulunur, yalnızca eksik ders
/// eşlemesi tamamlanır — onay durumu/profil dokunulmaz (elle değişmiş olabilir).</para>
/// </summary>
public sealed class TutorSeedService : ITutorSeedService
{
    private const string RoleTeacher = "Teacher";

    private readonly AppDbContext _context;
    private readonly IAuthApiSeedClient _authApi;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<TutorSeedService> _logger;

    public TutorSeedService(
        AppDbContext context,
        IAuthApiSeedClient authApi,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<TutorSeedService> logger)
    {
        _context = context;
        _authApi = authApi;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    private sealed record PlannedTutor(
        string ProvinceName, TeacherSeedPlan.BranchInfo Branch, int Ordinal, string Email, string FirstName, string LastName,
        int SubjectId, bool Pending, TutorSeedPlan.TutorProfileDefaults Profile);

    public async Task<TutorSeedResult> RunAsync(TutorSeedOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!SeedCommands.IsAllowedEnvironment(_environment))
        {
            throw new InvalidOperationException(
                $"seed-tutors yalnızca Development/Staging ortamında çalışır; mevcut ortam: '{_environment.EnvironmentName}'.");
        }

        if (options.Provinces.Count == 0)
            throw new ArgumentException("En az bir il verilmeli.", nameof(options));
        if (options.LimitSchoolsPerProvince is <= 0)
            throw new ArgumentException("İl başına okul limiti pozitif olmalı.", nameof(options));
        if (options.PendingRatio is { } ratio && (ratio is < 0 or > 1 || double.IsNaN(ratio)))
            throw new ArgumentException("Pending oranı 0..1 aralığında olmalı.", nameof(options));
        var pendingRatio = options.PendingRatio ?? TutorSeedOptions.DefaultPendingRatioFor(_environment);
        if (options.BatchSize is <= 0 or > TeacherSeedOptions.MaxBatchSize)
            throw new ArgumentException($"Parti boyutu 1..{TeacherSeedOptions.MaxBatchSize} olmalı.", nameof(options));
        if (options.KeycloakMode != TeacherSeedOptions.KeycloakModeAdminApi && options.KeycloakMode != TeacherSeedOptions.KeycloakModePartialImport)
            throw new ArgumentException($"Keycloak modu '{TeacherSeedOptions.KeycloakModeAdminApi}' ya da '{TeacherSeedOptions.KeycloakModePartialImport}' olmalı.", nameof(options));

        var pwd = options.DryRun ? null : TeacherSeedService.RequirePassword(_configuration);

        var total = Stopwatch.StartNew();
        var result = new TutorSeedResult { DryRun = options.DryRun, KeycloakMode = options.KeycloakMode, PendingRatio = pendingRatio, ResetPassword = options.ResetPassword };

        // ---- Referans ----
        var dbProvinces = await _context.Provinces.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var provinceByKey = dbProvinces
            .GroupBy(p => SchoolSeedService.FoldKey(p.Name))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var subjects = await _context.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var subjectIdByKey = subjects
            .GroupBy(s => SchoolSeedService.FoldKey(s.Name))
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var missingSubjects = TeacherSeedPlan.Branches
            .Where(b => !subjectIdByKey.ContainsKey(SchoolSeedService.FoldKey(b.SubjectName)))
            .Select(b => b.SubjectName)
            .ToList();
        if (missingSubjects.Count > 0)
        {
            throw new InvalidOperationException(
                $"Subject tablosunda bulunmayan branşlar: {string.Join(", ", missingSubjects)}. Ders referans verisi (TopicSeed) yüklü değil.");
        }
        var branchBySubjectId = TeacherSeedPlan.Branches.ToDictionary(b => subjectIdByKey[SchoolSeedService.FoldKey(b.SubjectName)]);

        // ---- İller ----
        var requested = options.Provinces
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .DistinctBy(SchoolSeedService.FoldKey, StringComparer.Ordinal)
            .ToList();

        var matched = new List<(string Requested, int ProvinceId, string DbName)>();
        foreach (var name in requested)
        {
            if (provinceByKey.TryGetValue(SchoolSeedService.FoldKey(name), out var p))
                matched.Add((name, p.Id, p.Name));
            else
            {
                result.UnmatchedProvinces.Add(name);
                _logger.LogWarning("seed-tutors: '{Province}' ili Province tablosunda bulunamadı.", name);
            }
        }

        // ---- İl başına seed okullar (seed-teachers ile aynı sıralama/limit) ----
        var provinceIds = matched.Select(m => m.ProvinceId).ToList();
        var seedSchools = provinceIds.Count == 0
            ? new List<School>()
            : await _context.Schools
                .AsNoTracking()
                .Where(s => s.IsSeedData && s.ProvinceId != null && provinceIds.Contains(s.ProvinceId.Value))
                .ToListAsync(ct);

        var selectedSchoolIds = new List<int>();
        var provinceBySchoolId = new Dictionary<int, string>();
        foreach (var (_, provinceId, dbName) in matched)
        {
            var ordered = TeacherSeedPlan.OrderSchoolsDeterministic(seedSchools.Where(s => s.ProvinceId == provinceId));
            if (options.LimitSchoolsPerProvince is { } limit && ordered.Count > limit)
            {
                result.SchoolsSkippedByLimit += ordered.Count - limit;
                ordered = ordered.Take(limit).ToList();
            }
            foreach (var s in ordered)
            {
                selectedSchoolIds.Add(s.Id);
                provinceBySchoolId[s.Id] = dbName;
            }
        }
        result.SchoolsCounted = selectedSchoolIds.Count;

        // ---- Taban: seed okul öğretmenleri il × branş ----
        var baseRows = selectedSchoolIds.Count == 0
            ? new List<(int SchoolId, int SubjectId)>()
            : (await _context.TeacherSubjects
                .AsNoTracking()
                .Where(ts => ts.Teacher.IsSeedData && !ts.Teacher.IsIndependentTutor && ts.Teacher.SchoolId != null
                             && selectedSchoolIds.Contains(ts.Teacher.SchoolId.Value))
                .Select(ts => new { SchoolId = ts.Teacher.SchoolId!.Value, ts.SubjectId })
                .ToListAsync(ct))
                .Select(r => (r.SchoolId, r.SubjectId))
                .ToList();

        var baseCount = new Dictionary<(string Province, TeacherSeedBranch Branch), int>();
        foreach (var (schoolId, subjectId) in baseRows)
        {
            if (!branchBySubjectId.TryGetValue(subjectId, out var branch)) continue; // plan dışı ders (elle eklenmiş)
            var key = (provinceBySchoolId[schoolId], branch.Branch);
            baseCount[key] = baseCount.GetValueOrDefault(key) + 1;
            result.SchoolTeachersCounted++;
        }

        // ---- Plan ----
        var plan = new List<PlannedTutor>();
        var groupSummary = new Dictionary<(string, TeacherSeedBranch), TutorSeedGroupSummary>();
        // İl etiketi/slug/Bio için DB'deki Province adı kullanılır ("kars" istense de "Kars").
        foreach (var (_, _, dbName) in matched)
        {
            var slug = TutorSeedPlan.ProvinceSlug(dbName);
            foreach (var branch in TeacherSeedPlan.Branches)
            {
                var key = (dbName, branch.Branch);
                var n = baseCount.GetValueOrDefault(key);
                var count = TutorSeedPlan.HalfOf(n);
                var summary = new TutorSeedGroupSummary
                {
                    Province = dbName, Branch = branch.Branch, SubjectName = branch.SubjectName,
                    SchoolTeachers = n, Planned = count, PlannedPending = TutorSeedPlan.PendingCountFor(count, pendingRatio)
                };
                result.Groups.Add(summary);
                groupSummary[key] = summary;

                for (var ordinal = 1; ordinal <= count; ordinal++)
                {
                    var email = TutorSeedPlan.Email(slug, branch, ordinal);
                    var (first, last) = TeacherSeedPlan.PickName(email);
                    var pending = TutorSeedPlan.IsPending(ordinal, count, pendingRatio);
                    plan.Add(new PlannedTutor(dbName, branch, ordinal, email, first, last,
                        subjectIdByKey[SchoolSeedService.FoldKey(branch.SubjectName)], pending,
                        TutorSeedPlan.ProfileFor(email, dbName, branch)));
                }
            }
        }
        result.Planned = plan.Count;
        result.PlannedPending = plan.Count(p => p.Pending);

        if (options.DryRun)
        {
            foreach (var p in plan) AddAccount(result, p, "Planned");
            result.TotalElapsedMs = total.ElapsedMilliseconds;
            LogSummary(result);
            return result;
        }

        // ---- Yazma ----
        foreach (var batch in plan.Chunk(options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            result.Batches++;

            var request = new DevSeedUsersRequest
            {
                Password = pwd!,
                Role = RoleTeacher,
                EmitLocaleEvents = options.EmitEvents,
                Mode = options.KeycloakMode,
                ResetPassword = options.ResetPassword,
                AdoptUnmarked = options.AdoptUnmarked,
                Users = batch.Select(p => new DevSeedUserItem
                {
                    Email = p.Email, FirstName = p.FirstName, LastName = p.LastName, SchoolId = null
                }).ToList()
            };

            var response = await _authApi.SeedUsersAsync(request, ct);
            result.KeycloakElapsedMs += response.KeycloakElapsedMs;
            result.IdentityDbElapsedMs += response.IdentityDbElapsedMs;

            var byEmail = response.Results
                .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var ready = new List<(PlannedTutor Planned, int UserId, bool Adopted)>();
            foreach (var p in batch)
            {
                if (!byEmail.TryGetValue(p.Email, out var r) || r.UserId is null)
                {
                    var reason = r?.Error ?? (r is null ? "auth-api yanıtında yok" : $"keycloak={r.KeycloakStatus} identity={r.IdentityStatus}");
                    MarkFailed(result, groupSummary, p, reason);
                    continue;
                }

                var adopted = r.KeycloakStatus == DevSeedUsersResponse.StatusAdopted;
                if (r.KeycloakStatus == DevSeedUsersResponse.StatusCreated) result.KeycloakCreated++;
                else if (r.KeycloakStatus == DevSeedUsersResponse.StatusExisting) result.KeycloakExisting++;
                else if (adopted) result.KeycloakAdopted++;
                if (r.IdentityStatus == DevSeedUsersResponse.StatusCreated) result.IdentityCreated++;
                else if (r.IdentityStatus == DevSeedUsersResponse.StatusExisting) result.IdentityExisting++;
                if (r.PasswordReset) result.PasswordsReset++;

                ready.Add((p, r.UserId.Value, adopted));
            }

            if (ready.Count == 0) continue;

            var sw = Stopwatch.StartNew();
            var userIds = ready.Select(x => x.UserId).ToList();
            var existingByUserId = (await _context.Teachers
                    .Include(t => t.TeacherSubjects)
                    .Where(t => userIds.Contains(t.UserId))
                    .ToListAsync(ct))
                .GroupBy(t => t.UserId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (p, userId, adopted) in ready)
            {
                var key = (p.ProvinceName, p.Branch.Branch);
                if (existingByUserId.TryGetValue(userId, out var teacher))
                {
                    if (teacher.TeacherSubjects.All(ts => ts.SubjectId != p.SubjectId))
                        teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = p.SubjectId });

                    result.TutorsExisting++;
                    groupSummary[key].Existing++;
                    AddAccount(result, p, DevSeedUsersResponse.StatusExisting);
                    continue;
                }

                var created = new Teacher
                {
                    UserId = userId,
                    SchoolId = null,
                    IsIndependentTutor = true,
                    ApprovalStatus = p.Pending ? TeacherApprovalStatus.Pending : TeacherApprovalStatus.Approved,
                    HourlyRate = p.Profile.HourlyRate,
                    TeachesOnline = p.Profile.TeachesOnline,
                    TeachesInPerson = p.Profile.TeachesInPerson,
                    Bio = p.Profile.Bio,
                    IsSeedData = true
                };
                created.TeacherSubjects.Add(new TeacherSubject { SubjectId = p.SubjectId });
                _context.Teachers.Add(created);
                existingByUserId[userId] = created;

                result.TutorsCreated++;
                groupSummary[key].Created++;
                AddAccount(result, p, adopted ? DevSeedUsersResponse.StatusAdopted : DevSeedUsersResponse.StatusCreated);
            }

            await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.Clear();
            result.ExamDbElapsedMs += sw.ElapsedMilliseconds;

            _logger.LogInformation("seed-tutors: parti {Batch} tamam — {Count} hesap, Keycloak {KcMs} ms, identity {IdMs} ms, exam {ExMs} ms",
                result.Batches, batch.Length, response.KeycloakElapsedMs, response.IdentityDbElapsedMs, sw.ElapsedMilliseconds);
        }

        result.TotalElapsedMs = total.ElapsedMilliseconds;
        LogSummary(result);
        return result;
    }

    private static void MarkFailed(TutorSeedResult result, Dictionary<(string, TeacherSeedBranch), TutorSeedGroupSummary> groups, PlannedTutor p, string reason)
    {
        result.Failed++;
        groups[(p.ProvinceName, p.Branch.Branch)].Failed++;
        if (result.Errors.Count < TutorSeedResult.ErrorSampleLimit)
            result.Errors.Add($"{p.Email}: {reason}");
        AddAccount(result, p, DevSeedUsersResponse.StatusFailed);
    }

    private static void AddAccount(TutorSeedResult result, PlannedTutor p, string status)
    {
        if (result.Accounts.Count >= TutorSeedResult.AccountSampleLimit) return;
        result.Accounts.Add(new TutorSeedAccount
        {
            Email = p.Email,
            FullName = $"{p.FirstName} {p.LastName}",
            Province = p.ProvinceName,
            Branch = p.Branch.Branch,
            HourlyRate = p.Profile.HourlyRate,
            TeachesInPerson = p.Profile.TeachesInPerson,
            Pending = p.Pending,
            Status = status
        });
    }

    private void LogSummary(TutorSeedResult r)
    {
        _logger.LogInformation(
            "seed-tutors {Mode}: taban={Base} (okul={Schools}) plan={Planned} pending={Pending} tutor+={Created} tutor={Existing} hata={Failed} " +
            "kc+={KcCreated} kc={KcExisting} kcAdopt={KcAdopted} pwReset={PwReset} id+={IdCreated} id={IdExisting} süre: keycloak={KcMs}ms identity={IdMs}ms exam={ExMs}ms toplam={TotalMs}ms parti={Batches}",
            r.DryRun ? "DRY-RUN" : "WRITE", r.SchoolTeachersCounted, r.SchoolsCounted, r.Planned, r.PlannedPending, r.TutorsCreated, r.TutorsExisting, r.Failed,
            r.KeycloakCreated, r.KeycloakExisting, r.KeycloakAdopted, r.PasswordsReset, r.IdentityCreated, r.IdentityExisting,
            r.KeycloakElapsedMs, r.IdentityDbElapsedMs, r.ExamDbElapsedMs, r.TotalElapsedMs, r.Batches);
    }
}
