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
/// Bkz. <see cref="ITeacherSeedService"/>. Akış: ortam guard'ı → parola config kontrolü → il eşleştirme →
/// <c>IsSeedData</c> okullar (ada göre sıralı, il başına limit) → ad üzerinden tür → branş planı
/// (deterministik e-posta/ad) → auth-api'ye parti parti (Keycloak + identity, idempotent) →
/// exam <c>Teacher</c> + <c>TeacherSubject</c> upsert (parti başına SaveChanges).
///
/// <para>İdempotency: e-posta deterministik; auth-api mevcut Keycloak/identity kaydını döner, burada
/// <c>Teacher.UserId</c> ile varlık kontrolü yapılır. Kısmi durum (Keycloak'ta var, identity/exam'de yok)
/// her koşuda tamamlanır: auth-api plandaki e-postayı <c>Adopted</c> ile sahiplenir (roller/school_id onarılır,
/// identity satırı açılır), burada Teacher açılır. Parola yalnızca <c>--reset-password</c> ile sıfırlanır.
/// Bir hesabın hatası diğerlerini durdurmaz; rapora yazılır.</para>
/// </summary>
public sealed class TeacherSeedService : ITeacherSeedService
{
    /// <summary>
    /// Ortak parolanın okunduğu config anahtarı. Değer koda/commit'e girmez: <c>.env</c> /
    /// user-secrets / ortam değişkeni <c>SeedData__Password</c>.
    /// </summary>
    public const string PasswordConfigKey = "SeedData:Password";

    private const string RoleTeacher = "Teacher";

    private readonly AppDbContext _context;
    private readonly IAuthApiSeedClient _authApi;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<TeacherSeedService> _logger;

    public TeacherSeedService(
        AppDbContext context,
        IAuthApiSeedClient authApi,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<TeacherSeedService> logger)
    {
        _context = context;
        _authApi = authApi;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public static bool IsAllowedEnvironment(IHostEnvironment environment) => SeedCommands.IsAllowedEnvironment(environment);

    /// <summary>Config'ten parolayı okur; yoksa nasıl verileceğini anlatan hata.</summary>
    public static string RequirePassword(IConfiguration configuration)
    {
        var value = configuration[PasswordConfigKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"'{PasswordConfigKey}' yapılandırılmamış. Ortak seed parolasını koda yazmadan verin (.env dosyası okunmaz): " +
                $"ortam değişkeni SeedData__Password=<parola> ya da api/ExamApp.Api dizininde `dotnet user-secrets set \"{PasswordConfigKey}\" \"<parola>\"`.");
        }
        if (value.Length < 6)
            throw new InvalidOperationException($"'{PasswordConfigKey}' en az 6 karakter olmalı (Keycloak parola politikası).");
        return value;
    }

    private sealed record PlannedTeacher(
        School School, string ProvinceName, SchoolSeedKind Kind, TeacherSeedPlan.BranchInfo Branch, int Ordinal,
        string Email, string FirstName, string LastName, int SubjectId);

    public async Task<TeacherSeedResult> RunAsync(TeacherSeedOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsAllowedEnvironment(_environment))
        {
            throw new InvalidOperationException(
                $"seed-teachers yalnızca Development/Staging ortamında çalışır; mevcut ortam: '{_environment.EnvironmentName}'.");
        }

        if (options.Provinces.Count == 0)
            throw new ArgumentException("En az bir il verilmeli.", nameof(options));
        if (options.LimitSchoolsPerProvince is <= 0)
            throw new ArgumentException("İl başına okul limiti pozitif olmalı.", nameof(options));
        if (options.BatchSize is <= 0 or > TeacherSeedOptions.MaxBatchSize)
            throw new ArgumentException($"Parti boyutu 1..{TeacherSeedOptions.MaxBatchSize} olmalı.", nameof(options));
        if (options.KeycloakMode != TeacherSeedOptions.KeycloakModeAdminApi && options.KeycloakMode != TeacherSeedOptions.KeycloakModePartialImport)
            throw new ArgumentException($"Keycloak modu '{TeacherSeedOptions.KeycloakModeAdminApi}' ya da '{TeacherSeedOptions.KeycloakModePartialImport}' olmalı.", nameof(options));

        // Parola: yazma koşusunda auth-api'ye gitmeden ÖNCE doğrulanır (dry-run'da gerekmez).
        var pwd = options.DryRun ? null : RequirePassword(_configuration);

        var total = Stopwatch.StartNew();
        var result = new TeacherSeedResult { DryRun = options.DryRun, KeycloakMode = options.KeycloakMode, ResetPassword = options.ResetPassword };

        // ---- Referans: iller, dersler ----
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

        foreach (var b in TeacherSeedPlan.Branches)
            result.Branches.Add(new TeacherSeedBranchSummary { Branch = b.Branch, SubjectName = b.SubjectName });
        var branchSummary = result.Branches.ToDictionary(b => b.Branch);

        // ---- İstenen iller ----
        var requested = options.Provinces
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .DistinctBy(SchoolSeedService.FoldKey, StringComparer.Ordinal)
            .ToList();

        var provinceIdByRequested = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in requested)
        {
            var summary = new TeacherSeedProvinceSummary { Province = name };
            result.Provinces.Add(summary);
            if (provinceByKey.TryGetValue(SchoolSeedService.FoldKey(name), out var p))
            {
                summary.ProvinceMatched = true;
                provinceIdByRequested[name] = p.Id;
            }
            else
            {
                result.UnmatchedProvinces.Add(name);
                _logger.LogWarning("seed-teachers: '{Province}' ili Province tablosunda bulunamadı.", name);
            }
        }

        var provinceIds = provinceIdByRequested.Values.ToList();
        var seedSchools = provinceIds.Count == 0
            ? new List<School>()
            : await _context.Schools
                .AsNoTracking()
                .Where(s => s.IsSeedData && s.ProvinceId != null && provinceIds.Contains(s.ProvinceId.Value))
                .ToListAsync(ct);
        var schoolsByProvince = seedSchools.GroupBy(s => s.ProvinceId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        // ---- Plan ----
        var plan = new List<PlannedTeacher>();
        // E-posta tekilliği: kurum kodu güvenli hale getirilirken ("77-58" → "7758") ya da s<Id> fallback'ında
        // iki okul aynı koda düşebilir; ikincisi sessizce "Existing" sayılmasın — Failed olarak raporlanır.
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collisions = new List<PlannedTeacher>();
        foreach (var summary in result.Provinces.Where(p => p.ProvinceMatched))
        {
            var provinceId = provinceIdByRequested[summary.Province];
            schoolsByProvince.TryGetValue(provinceId, out var schools);
            schools ??= new List<School>();

            // Deterministik sıra (TeacherSeedPlan.OrderSchoolsDeterministic); limit ilk N.
            var ordered = TeacherSeedPlan.OrderSchoolsDeterministic(schools);

            if (options.LimitSchoolsPerProvince is { } limit && ordered.Count > limit)
            {
                summary.SkippedByLimit = ordered.Count - limit;
                result.SchoolsSkippedByLimit += summary.SkippedByLimit;
                ordered = ordered.Take(limit).ToList();
            }

            foreach (var school in ordered)
            {
                var kind = TeacherSeedPlan.ClassifyBySchoolName(school.Name);
                if (kind is null)
                {
                    summary.SchoolsUnknownKind++;
                    result.SchoolsUnknownKind++;
                    _logger.LogWarning("seed-teachers: '{School}' (id {Id}) adından tür çıkarılamadı — atlandı.", school.Name, school.Id);
                    continue;
                }

                result.SchoolsSelected++;
                if (kind == SchoolSeedKind.Ilkokul) { summary.SchoolsIlkokul++; result.SchoolsIlkokul++; }
                else { summary.SchoolsOrtaokul++; result.SchoolsOrtaokul++; }

                var code = TeacherSeedPlan.SchoolCode(school.ExternalCode, school.Id);
                foreach (var branch in TeacherSeedPlan.Branches)
                {
                    var count = TeacherSeedPlan.CountFor(kind.Value, branch.Branch);
                    for (var n = 1; n <= count; n++)
                    {
                        var email = TeacherSeedPlan.Email(code, branch, n);
                        var (first, last) = TeacherSeedPlan.PickName(email);
                        var planned = new PlannedTeacher(school, summary.Province, kind.Value, branch, n, email, first, last,
                            subjectIdByKey[SchoolSeedService.FoldKey(branch.SubjectName)]);
                        summary.Planned++;
                        branchSummary[branch.Branch].Planned++;
                        if (!seenEmails.Add(email))
                        {
                            collisions.Add(planned);
                            continue;
                        }
                        plan.Add(planned);
                    }
                }
            }
        }
        result.Planned = plan.Count + collisions.Count;

        var provinceSummary = result.Provinces.ToDictionary(p => p.Province, StringComparer.Ordinal);
        foreach (var c in collisions)
        {
            _logger.LogWarning("seed-teachers: e-posta çakışması {Email} — okul '{School}' (id {Id}) başka bir okulla aynı koda düşüyor; atlandı.",
                c.Email, c.School.Name, c.School.Id);
            MarkFailed(result, provinceSummary, branchSummary, c, $"e-posta çakışması: '{c.School.Name}' (#{c.School.Id}) başka bir okulla aynı kurum koduna düşüyor");
        }

        if (options.DryRun)
        {
            foreach (var p in plan)
                AddAccount(result, p, "Planned");
            result.TotalElapsedMs = total.ElapsedMilliseconds;
            LogSummary(result);
            return result;
        }

        // ---- Yazma: parti parti ----
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
                    Email = p.Email,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    SchoolId = p.School.Id
                }).ToList()
            };

            var response = await _authApi.SeedUsersAsync(request, ct);
            result.KeycloakElapsedMs += response.KeycloakElapsedMs;
            result.IdentityDbElapsedMs += response.IdentityDbElapsedMs;

            var byEmail = response.Results
                .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var ready = new List<(PlannedTeacher Planned, int UserId, bool Adopted)>();
            foreach (var p in batch)
            {
                if (!byEmail.TryGetValue(p.Email, out var r) || r.UserId is null)
                {
                    var reason = r?.Error ?? (r is null ? "auth-api yanıtında yok" : $"keycloak={r.KeycloakStatus} identity={r.IdentityStatus}");
                    MarkFailed(result, provinceSummary, branchSummary, p, reason);
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
            var existingTeachers = await _context.Teachers
                .Include(t => t.TeacherSubjects)
                .Where(t => userIds.Contains(t.UserId))
                .ToListAsync(ct);
            var existingByUserId = existingTeachers
                .GroupBy(t => t.UserId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (p, userId, adopted) in ready)
            {
                if (existingByUserId.TryGetValue(userId, out var teacher))
                {
                    // Kısmi onarım: ders eşlemesi eksikse tamamla; okul/onay dokunulmaz (elle değişmiş olabilir).
                    if (teacher.TeacherSubjects.All(ts => ts.SubjectId != p.SubjectId))
                        teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = p.SubjectId });

                    result.TeachersExisting++;
                    provinceSummary[p.ProvinceName].Existing++;
                    branchSummary[p.Branch.Branch].Existing++;
                    AddAccount(result, p, DevSeedUsersResponse.StatusExisting);
                    continue;
                }

                var created = new Teacher
                {
                    UserId = userId,
                    SchoolId = p.School.Id,
                    IsIndependentTutor = false,
                    ApprovalStatus = TeacherApprovalStatus.Approved,
                    AccountApprovedAt = DateTime.UtcNow, // issue #287: seed öğretmenler onaylı hesapla başlar
                    IsSeedData = true
                };
                created.TeacherSubjects.Add(new TeacherSubject { SubjectId = p.SubjectId });
                _context.Teachers.Add(created);
                existingByUserId[userId] = created;

                result.TeachersCreated++;
                provinceSummary[p.ProvinceName].Created++;
                branchSummary[p.Branch.Branch].Created++;
                AddAccount(result, p, adopted ? DevSeedUsersResponse.StatusAdopted : DevSeedUsersResponse.StatusCreated);
            }

            await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.Clear();
            result.ExamDbElapsedMs += sw.ElapsedMilliseconds;

            _logger.LogInformation("seed-teachers: parti {Batch} tamam — {Count} hesap, Keycloak {KcMs} ms, identity {IdMs} ms, exam {ExMs} ms",
                result.Batches, batch.Length, response.KeycloakElapsedMs, response.IdentityDbElapsedMs, sw.ElapsedMilliseconds);
        }

        result.TotalElapsedMs = total.ElapsedMilliseconds;
        LogSummary(result);
        return result;
    }

    private static void MarkFailed(
        TeacherSeedResult result,
        Dictionary<string, TeacherSeedProvinceSummary> provinceSummary,
        Dictionary<TeacherSeedBranch, TeacherSeedBranchSummary> branchSummary,
        PlannedTeacher p, string reason)
    {
        result.Failed++;
        provinceSummary[p.ProvinceName].Failed++;
        branchSummary[p.Branch.Branch].Failed++;
        if (result.Errors.Count < TeacherSeedResult.ErrorSampleLimit)
            result.Errors.Add($"{p.Email}: {reason}");
        AddAccount(result, p, DevSeedUsersResponse.StatusFailed);
    }

    private static void AddAccount(TeacherSeedResult result, PlannedTeacher p, string status)
    {
        if (result.Accounts.Count >= TeacherSeedResult.AccountSampleLimit) return;
        result.Accounts.Add(new TeacherSeedAccount
        {
            Email = p.Email,
            FullName = $"{p.FirstName} {p.LastName}",
            School = p.School.Name,
            SchoolId = p.School.Id,
            Province = p.ProvinceName,
            Branch = p.Branch.Branch,
            Status = status
        });
    }

    private void LogSummary(TeacherSeedResult r)
    {
        _logger.LogInformation(
            "seed-teachers {Mode}: okul={Schools} (ilk={Ilk} orta={Orta} bilinmeyen={Unknown} limitDışı={Limit}) plan={Planned} " +
            "teacher+={Created} teacher={Existing} hata={Failed} kc+={KcCreated} kc={KcExisting} kcAdopt={KcAdopted} pwReset={PwReset} id+={IdCreated} id={IdExisting} " +
            "süre: keycloak={KcMs}ms identity={IdMs}ms exam={ExMs}ms toplam={TotalMs}ms parti={Batches}",
            r.DryRun ? "DRY-RUN" : "WRITE", r.SchoolsSelected, r.SchoolsIlkokul, r.SchoolsOrtaokul, r.SchoolsUnknownKind, r.SchoolsSkippedByLimit,
            r.Planned, r.TeachersCreated, r.TeachersExisting, r.Failed, r.KeycloakCreated, r.KeycloakExisting, r.KeycloakAdopted, r.PasswordsReset,
            r.IdentityCreated, r.IdentityExisting, r.KeycloakElapsedMs, r.IdentityDbElapsedMs, r.ExamDbElapsedMs, r.TotalElapsedMs, r.Batches);
    }
}
