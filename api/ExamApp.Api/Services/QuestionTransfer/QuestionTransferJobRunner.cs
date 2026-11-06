using Microsoft.Extensions.Logging.Abstractions;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Teachers;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.QuestionTransfer;

public class QuestionTransferJobRunner
{
    private readonly AppDbContext _db;
    private readonly IMinIoService _minio;
    private readonly IApprovedTeacherGuard _teacherGuard;
    // review #574 (L1): içe alınan etkileşim tanımı kayıt yoluyla aynı kontrolcüyle doğrulanır.
    private readonly IInteractionGraderRegistry _graders;
    private readonly IQuestionContentHashService _hashes;
    private readonly ILogger<QuestionTransferJobRunner> _logger;

    private const int ExportBundleSize = 2000;
    private const int MaxResultSamples = 10;

    /// <summary>issue #289 (security D2): sahibi artık onaylı öğretmen olmayan işin sonlandırma mesajı.</summary>
    public const string OwnerNotApprovedMessage = "Job owner's teacher account is not approved (or is suspended); job was not run.";

    public QuestionTransferJobRunner(AppDbContext db, IMinIoService minio, IApprovedTeacherGuard teacherGuard,
        IInteractionGraderRegistry? graders = null, IQuestionContentHashService? hashes = null, ILogger<QuestionTransferJobRunner>? logger = null)
    {
        _db = db;
        _minio = minio;
        _teacherGuard = teacherGuard;
        _graders = graders ?? InteractionGraderRegistry.Default;
        _hashes = hashes ?? new QuestionContentHashService(db, minio, NullLogger<QuestionContentHashService>.Instance);
        _logger = logger ?? NullLogger<QuestionTransferJobRunner>.Instance;
    }

    /// <summary>
    /// Eski imza: #289 öncesi kuyruğa girmiş işler için (sahip bilgisi yok, <c>CreateUserId</c> 0 → kontrol atlanır).
    /// </summary>
    [Queue("question-transfer")]
    [AutomaticRetry(Attempts = 2)]
    public Task RunExportAsync(Guid jobId) => RunExportAsync(jobId, ownerIsAdmin: false);

    /// <summary>Eski imza — bkz. <see cref="RunExportAsync(Guid)"/>.</summary>
    [Queue("question-transfer")]
    [AutomaticRetry(Attempts = 0)]
    public Task RunImportAsync(Guid jobId) => RunImportAsync(jobId, ownerIsAdmin: false);

    /// <summary>
    /// issue #289 (security D2): iş kuyruğa girdikten sonra sahibi askıya alınmış / onayı kaldırılmış olabilir. Admin sahipli
    /// iş muaftır (ApprovedTeacher policy'sindeki Admin/SuperAdmin muafiyeti). Sahibi bilinmeyen eski işler (CreateUserId
    /// null/0) kontrol edilmez. Aksi halde yalnızca <see cref="TeacherApprovalCheck.Approved"/> geçer (fail-closed).
    /// Reddedilen iş soru yazmadan <see cref="QuestionTransferJobStatus.Failed"/> ile sonlanır (istisna fırlatılmaz →
    /// Hangfire yeniden denemez).
    /// </summary>
    private async Task<bool> RejectIfOwnerNotApprovedAsync(QuestionTransferJob job, bool ownerIsAdmin)
    {
        if (ownerIsAdmin || job.CreateUserId is not > 0)
            return false;

        if (await _teacherGuard.CheckAsync(job.CreateUserId.Value) == TeacherApprovalCheck.Approved)
            return false;

        job.Status = QuestionTransferJobStatus.Failed;
        job.Message = OwnerNotApprovedMessage;
        await _db.SaveChangesAsync();
        return true;
    }

    [Queue("question-transfer")]
    [AutomaticRetry(Attempts = 2)]
    public async Task RunExportAsync(Guid jobId, bool ownerIsAdmin)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstAsync(j => j.Id == jobId);
        if (await RejectIfOwnerNotApprovedAsync(job, ownerIsAdmin))
            return;

        job.Status = QuestionTransferJobStatus.Running;
        job.Message = "Running";
        await _db.SaveChangesAsync();

        try
        {
            var request = string.IsNullOrWhiteSpace(job.RequestJson)
                ? null
                : JsonSerializer.Deserialize<Models.Dtos.StartQuestionExportDto>(job.RequestJson);

            var sourceKey = string.IsNullOrWhiteSpace(request?.SourceKey) ? "default" : request!.SourceKey!.Trim();

            // Prevent concurrent exports within the same source (append/overwrite safety).
            using var connection = JobStorage.Current.GetConnection();
            using var sourceLock = connection.AcquireDistributedLock($"question-transfer-export:{sourceKey}", TimeSpan.FromHours(1));

            var requestedIds = request?.QuestionIds?.Distinct().ToList() ?? new List<int>();
            requestedIds = requestedIds.Where(x => x > 0).Distinct().ToList();

            if (request?.WorksheetIds is { Count: > 0 } worksheetIds)
                await WriteWorksheetsAsync(sourceKey, worksheetIds);

            async Task ProcessNewIdsAsync(List<int> ids, HashSet<int> touched)
            {
                var remaining = new Queue<int>(ids);
                while (remaining.Count > 0)
                {
                    var bundle = await GetOrCreateWritableBundleAsync(sourceKey);
                    var existingCount = await _db.Set<QuestionTransferExportMap>()
                        .CountAsync(m => m.SourceKey == sourceKey && m.BundleNo == bundle.BundleNo);

                    // Keep bundle count consistent even if it drifted.
                    if (bundle.QuestionCount != existingCount)
                    {
                        bundle.QuestionCount = existingCount;
                        bundle.UpdateTime = DateTime.UtcNow;
                        await _db.SaveChangesAsync();
                    }

                    var capacity = Math.Max(0, ExportBundleSize - existingCount);
                    if (capacity == 0)
                    {
                        // Bundle is full; create next.
                        bundle = await CreateNextBundleAsync(sourceKey);
                        existingCount = 0;
                        capacity = ExportBundleSize;
                    }

                    var batch = new List<int>(Math.Min(capacity, remaining.Count));
                    while (batch.Count < capacity && remaining.Count > 0)
                    {
                        batch.Add(remaining.Dequeue());
                    }

                    await AppendQuestionsToBundleAsync(sourceKey, bundle.BundleNo, batch);

                    foreach (var qid in batch)
                    {
                        _db.Set<QuestionTransferExportMap>().Add(new QuestionTransferExportMap
                        {
                            SourceKey = sourceKey,
                            QuestionId = qid,
                            BundleNo = bundle.BundleNo,
                            CreateTime = DateTime.UtcNow,
                        });
                    }

                    try
                    {
                        await _db.SaveChangesAsync();
                    }
                    catch (DbUpdateException)
                    {
                        foreach (var entry in _db.ChangeTracker.Entries<QuestionTransferExportMap>().ToList())
                        {
                            if (entry.State == EntityState.Added)
                            {
                                entry.State = EntityState.Detached;
                            }
                        }
                    }

                    var newCount = await _db.Set<QuestionTransferExportMap>()
                        .CountAsync(m => m.SourceKey == sourceKey && m.BundleNo == bundle.BundleNo);

                    bundle.QuestionCount = newCount;
                    bundle.UpdateTime = DateTime.UtcNow;
                    await _db.SaveChangesAsync();

                    touched.Add(bundle.BundleNo);

                    job.ProcessedItems = Math.Min(job.TotalItems, job.ProcessedItems + batch.Count);
                    job.Message = $"Export progress {job.ProcessedItems}/{job.TotalItems}";
                    await _db.SaveChangesAsync();
                }
            }

            var touchedBundles = new HashSet<int>();
            var exportAll = requestedIds.Count == 0;
            var skippedCount = 0;

            if (!exportAll)
            {
                // Skip if already exported for this source
                var alreadyExported = await _db.Set<QuestionTransferExportMap>()
                    .Where(m => m.SourceKey == sourceKey && requestedIds.Contains(m.QuestionId))
                    .Select(m => m.QuestionId)
                    .ToListAsync();

                var alreadySet = alreadyExported.ToHashSet();
                var newIds = requestedIds.Where(id => !alreadySet.Contains(id)).ToList();
                skippedCount = requestedIds.Count - newIds.Count;

                job.TotalItems = newIds.Count;
                job.ProcessedItems = 0;
                await _db.SaveChangesAsync();

                if (newIds.Count == 0)
                {
                    var indexUrl = await WriteSourceIndexAsync(sourceKey);
                    job.FileUrl = indexUrl;
                    job.Status = QuestionTransferJobStatus.Completed;
                    job.Message = $"Completed. Nothing new to export (Skipped {skippedCount} already exported).";
                    await _db.SaveChangesAsync();
                    return;
                }

                await ProcessNewIdsAsync(newIds, touchedBundles);
            }
            else
            {
                // Export ALL questions that were not yet exported for this source.
                var totalQuestions = await _db.Questions.CountAsync();
                var totalNew = await _db.Questions
                    .Where(q => !_db.Set<QuestionTransferExportMap>().Any(m => m.SourceKey == sourceKey && m.QuestionId == q.Id))
                    .CountAsync();

                skippedCount = Math.Max(0, totalQuestions - totalNew);
                job.TotalItems = totalNew;
                job.ProcessedItems = 0;
                job.Message = $"Export-all: {totalNew} new, {skippedCount} already exported";
                await _db.SaveChangesAsync();

                var lastId = 0;
                while (true)
                {
                    var page = await _db.Questions
                        .Where(q => q.Id > lastId)
                        .OrderBy(q => q.Id)
                        .Select(q => q.Id)
                        .Take(2000)
                        .ToListAsync();

                    if (page.Count == 0) break;
                    lastId = page[^1];

                    var exported = await _db.Set<QuestionTransferExportMap>()
                        .Where(m => m.SourceKey == sourceKey && page.Contains(m.QuestionId))
                        .Select(m => m.QuestionId)
                        .ToListAsync();

                    var exportedSet = exported.ToHashSet();
                    var newIds = page.Where(id => !exportedSet.Contains(id)).ToList();
                    if (newIds.Count == 0) continue;

                    await ProcessNewIdsAsync(newIds, touchedBundles);
                }
            }

            // Write per-source index + per-bundle map JSON for discoverability.
            foreach (var bundleNo in touchedBundles.OrderBy(x => x))
            {
                await WriteBundleMapAsync(sourceKey, bundleNo);
            }
            var finalIndexUrl = await WriteSourceIndexAsync(sourceKey);

            job.FileUrl = finalIndexUrl;
            job.Status = QuestionTransferJobStatus.Completed;
            job.Message = $"Completed. Exported {job.ProcessedItems}, Skipped {skippedCount}. Bundles updated: {string.Join(",", touchedBundles.OrderBy(x => x).Select(x => x.ToString()))}";
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            job.Status = QuestionTransferJobStatus.Failed;
            job.Message = ex.Message;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>Seçilen test ve etkinlikler ayrı dosyaya yazılır: soru dosyaları kapasiteye göre bölünür, paket indirilirken birleştirilir.</summary>
    private async Task WriteWorksheetsAsync(string sourceKey, List<int> worksheetIds)
    {
        var worksheets = await _db.Set<Worksheet>().AsNoTracking()
            .Include(w => w.Grade)
            .Include(w => w.Subject)
            .Include(w => w.Topic).ThenInclude(t => t!.Grade)
            .Include(w => w.SubTopic).ThenInclude(s => s!.Topic).ThenInclude(t => t.Grade)
            .Where(w => worksheetIds.Contains(w.Id))
            .OrderBy(w => w.Id)
            .ToListAsync();
        var items = (await _db.TestQuestions.AsNoTracking()
                .Where(i => worksheetIds.Contains(i.TestId))
                .OrderBy(i => i.Order).ThenBy(i => i.Id)
                .Select(i => new { i.TestId, i.QuestionId })
                .ToListAsync())
            .GroupBy(i => i.TestId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var tmpPath = Path.Combine(Path.GetTempPath(), $"export-worksheets-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var file = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var worksheet in worksheets)
                    await TryAddFromMinioAsync(zip, worksheet.ImageUrl, $"worksheets/{worksheet.Id}.jpg");

                var entry = zip.CreateEntry(QuestionTransferWorksheetImporter.WorksheetsFileName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await JsonSerializer.SerializeAsync(entryStream, worksheets.Select(w => new
                {
                    externalKey = $"worksheet:{w.Id}",
                    id = w.Id,
                    kind = w.Kind.ToString(),
                    name = w.Name,
                    description = w.Description,
                    subtitle = w.Subtitle,
                    badgeText = w.BadgeText,
                    maxDurationSeconds = w.MaxDurationSeconds,
                    isPracticeTest = w.IsPracticeTest,
                    commentsEnabled = w.CommentsEnabled,
                    grade = new { code = w.Grade?.Code, name = w.Grade?.Name },
                    subject = w.Subject == null ? null : new { code = w.Subject.Code, name = w.Subject.Name },
                    topic = w.Topic == null ? null : new { code = w.Topic.Code, name = w.Topic.Name, gradeName = w.Topic.Grade?.Name },
                    subTopic = w.SubTopic == null ? null : new { code = w.SubTopic.Code, name = w.SubTopic.Name, topicName = w.SubTopic.Topic?.Name, gradeName = w.SubTopic.Topic?.Grade?.Name },
                    items = (items.TryGetValue(w.Id, out var rows) ? rows : []).Select((row, index) => new { questionKey = $"question:{row.QuestionId}", order = index + 1 }).ToList(),
                }).ToList());
            }

            await using var upload = new FileStream(tmpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await _minio.UploadFileAsync(upload, QuestionTransferService.WorksheetsObjectName(sourceKey), contentType: "application/zip");
        }
        finally
        {
            DeleteTempFile(tmpPath);
        }
    }

    private static string BundleObjectName(string sourceKey, int bundleNo)
        => $"question-transfer/exports/{sourceKey}/bundle-{bundleNo:D4}.zip";

    private static string BundleMapObjectName(string sourceKey, int bundleNo)
        => $"question-transfer/exports/{sourceKey}/bundle-{bundleNo:D4}.map.json";

    private static string SourceIndexObjectName(string sourceKey)
        => $"question-transfer/exports/{sourceKey}/index.json";

    private async Task<QuestionTransferExportBundle> GetOrCreateWritableBundleAsync(string sourceKey)
    {
        var last = await _db.Set<QuestionTransferExportBundle>()
            .Where(b => b.SourceKey == sourceKey)
            .OrderByDescending(b => b.BundleNo)
            .FirstOrDefaultAsync();

        if (last == null)
        {
            return await CreateBundleAsync(sourceKey, 1);
        }

        if (last.QuestionCount >= ExportBundleSize)
        {
            return await CreateBundleAsync(sourceKey, last.BundleNo + 1);
        }

        return last;
    }

    private async Task<QuestionTransferExportBundle> CreateNextBundleAsync(string sourceKey)
    {
        var lastNo = await _db.Set<QuestionTransferExportBundle>()
            .Where(b => b.SourceKey == sourceKey)
            .MaxAsync(b => (int?)b.BundleNo) ?? 0;
        return await CreateBundleAsync(sourceKey, lastNo + 1);
    }

    private async Task<QuestionTransferExportBundle> CreateBundleAsync(string sourceKey, int bundleNo)
    {
        var bundle = new QuestionTransferExportBundle
        {
            SourceKey = sourceKey,
            BundleNo = bundleNo,
            QuestionCount = 0,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        };

        _db.Set<QuestionTransferExportBundle>().Add(bundle);
        await _db.SaveChangesAsync();
        return bundle;
    }

    private async Task AppendQuestionsToBundleAsync(string sourceKey, int bundleNo, List<int> questionIdsToAdd)
    {
        questionIdsToAdd = questionIdsToAdd.Where(x => x > 0).Distinct().ToList();
        if (questionIdsToAdd.Count == 0) return;

        var bundle = await _db.Set<QuestionTransferExportBundle>()
            .FirstOrDefaultAsync(b => b.SourceKey == sourceKey && b.BundleNo == bundleNo)
            ?? throw new InvalidOperationException("Bundle not found");

        // Ad işe özgü: aynı kaynak adıyla çalışan iki iş aynı dosyaya yazmasın. Kaynak adı kullanıcıdan gelir, yola girmez.
        var tmpPath = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.zip");
        try
        {
            await WriteBundleAsync(sourceKey, bundleNo, bundle, questionIdsToAdd, tmpPath);
        }
        finally
        {
            // Dosya doğru cevapları ve çözümleri taşır: temp'te kalmaz.
            DeleteTempFile(tmpPath);
        }
    }

    private async Task WriteBundleAsync(string sourceKey, int bundleNo, QuestionTransferExportBundle bundle, List<int> questionIdsToAdd, string tmpPath)
    {
        var objectName = BundleObjectName(sourceKey, bundleNo);

        // Download existing bundle if present
        if (!string.IsNullOrWhiteSpace(bundle.FileUrl))
        {
            await using var existing = await _minio.GetFileStreamAsync(bundle.FileUrl);
            if (existing != null)
            {
                await using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await existing.CopyToAsync(fs);
            }
        }
        else
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }

        var existingIds = await _db.Set<QuestionTransferExportMap>()
            .Where(m => m.SourceKey == sourceKey && m.BundleNo == bundleNo)
            .Select(m => m.QuestionId)
            .ToListAsync();

        var existingSet = existingIds.ToHashSet();
        var toAdd = questionIdsToAdd.Where(id => !existingSet.Contains(id)).ToList();
        if (toAdd.Count == 0) return;

        var questionsToAdd = await LoadQuestionsAsync(toAdd);

        // Open zip
        if (File.Exists(tmpPath))
        {
            await using var fs = new FileStream(tmpPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Update, leaveOpen: true);

            foreach (var q in questionsToAdd)
            {
                await TryAddFromMinioAsync(zip, q.ImageUrl, $"questions/{q.Id}/question.jpg");
                var qV2Url = TransformQuestionV2Url(q.ImageUrl);
                await TryAddFromMinioAsync(zip, qV2Url, $"questions/{q.Id}/question-v2.jpg");
                foreach (var a in q.Answers)
                {
                    await TryAddFromMinioAsync(zip, a.ImageUrl, $"questions/{q.Id}/answers/{a.Id}.jpg");
                }
                if (q.Passage != null)
                {
                    await TryAddFromMinioAsync(zip, q.Passage.ImageUrl, $"passages/{q.Passage.Id}.jpg");
                }
            }

            await RewriteBundleManifestAsync(zip, sourceKey, bundleNo, existingIds.Concat(toAdd).Distinct().ToList(), allowDeleteExisting: true);
        }
        else
        {
            await using var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true);

            foreach (var q in questionsToAdd)
            {
                await TryAddFromMinioAsync(zip, q.ImageUrl, $"questions/{q.Id}/question.jpg");
                var qV2Url = TransformQuestionV2Url(q.ImageUrl);
                await TryAddFromMinioAsync(zip, qV2Url, $"questions/{q.Id}/question-v2.jpg");
                foreach (var a in q.Answers)
                {
                    await TryAddFromMinioAsync(zip, a.ImageUrl, $"questions/{q.Id}/answers/{a.Id}.jpg");
                }
                if (q.Passage != null)
                {
                    await TryAddFromMinioAsync(zip, q.Passage.ImageUrl, $"passages/{q.Passage.Id}.jpg");
                }
            }

            await RewriteBundleManifestAsync(zip, sourceKey, bundleNo, toAdd, allowDeleteExisting: false);
        }

        // Upload (overwrite) and persist FileUrl
        await using var uploadStream = new FileStream(tmpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileUrl = await _minio.UploadFileAsync(uploadStream, objectName, contentType: "application/zip");
        bundle.FileUrl = fileUrl;
        bundle.UpdateTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    internal static bool IsContextBasedInPackage(JsonElement qEl) =>
        qEl.TryGetProperty("isContextBased", out var contextBased) && contextBased.ValueKind == JsonValueKind.True;

    internal static bool IsUnpublishedInPackage(JsonElement qEl) =>
        qEl.TryGetProperty("isUnpublished", out var unpublished) && unpublished.ValueKind == JsonValueKind.True;

    private static void DeleteTempFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Temp temizliği işi düşürmez.
        }
        catch (UnauthorizedAccessException)
        {
            // Temp temizliği işi düşürmez.
        }
    }

    private async Task RewriteBundleManifestAsync(ZipArchive zip, string sourceKey, int bundleNo, List<int> bundleQuestionIds, bool allowDeleteExisting)
    {
        if (allowDeleteExisting)
        {
            var existingManifest = zip.GetEntry("manifest.json");
            existingManifest?.Delete();
        }

        var questions = await LoadQuestionsAsync(bundleQuestionIds);
        var originKey = await QuestionTransferOriginStore.GetOrCreateAsync(_db);

        // issue #570: plan/çözüm soru başına bir kez ayrılır (taşınmamış eski satırın planındaki çözüm de ayrı alana gider).
        var interactionById = questions
            .GroupBy(q => q.Id)
            .ToDictionary(g => g.Key, g => InteractionPlanSolution.Split(g.First().InteractionPlan));

        var manifest = new
        {
            version = 3,
            sourceKey = sourceKey,
            originKey,
            bundleNo = bundleNo,
            exportedAtUtc = DateTime.UtcNow,
            questions = questions.Select(q => new
            {
                externalKey = $"question:{q.Id}",
                id = q.Id,
                text = q.Text,
                subText = q.SubText,
                imageUrl = q.ImageUrl,
                point = q.Point,
                bookName = q.BookName,
                // Issue #690: kaynak, lisans ve yayın durumu hedefte de geçerli olmalı (#701 baskı kuralı buna bakar).
                sourceType = q.SourceType.ToString(),
                rightsHolder = q.RightsHolder,
                licenseReference = q.LicenseReference,
                licenseEndsAt = q.LicenseEndsAt,
                isUnpublished = q.IsUnpublished,
                isContextBased = q.IsContextBased,
                difficultyLevel = q.DifficultyLevel,
                classificationSource = q.ClassificationSource?.ToString(),
                showPassageFirst = q.ShowPassageFirst,
                isExample = q.IsExample,
                practiceCorrectAnswer = q.PracticeCorrectAnswer,
                answerColCount = q.AnswerColCount,
                isCanvasQuestion = q.IsCanvasQuestion,
                interactionType = q.InteractionType,
                // issue #570: çözüm ayrı alan; plan çözümsüz.
                interactionPlan = interactionById[q.Id].Plan,
                interactionSolution = q.InteractionSolution ?? interactionById[q.Id].Solution,
                x = q.X,
                y = q.Y,
                width = q.Width,
                height = q.Height,
                sanitizedHeight = q.SanitizedHeight,
                sourcePageWidth = q.SourcePageWidth,
                layoutChoice = q.LayoutChoice,
                subject = q.SubjectId.HasValue && q.Subject != null
                    ? new { id = q.SubjectId, code = q.Subject.Code, name = q.Subject.Name }
                    : null,
                topic = q.TopicId.HasValue && q.Topic != null
                    ? new { id = q.TopicId, code = q.Topic.Code, name = q.Topic.Name, gradeId = q.Topic.GradeId, gradeName = q.Topic.Grade?.Name }
                    : null,
                subTopics = q.QuestionSubTopics
                    .Where(st => st.SubTopic != null)
                    .Select(st => new
                    {
                        id = st.SubTopicId,
                        code = st.SubTopic.Code,
                        name = st.SubTopic.Name,
                        topicName = st.SubTopic.Topic?.Name,
                        gradeName = st.SubTopic.Topic?.Grade?.Name,
                    })
                    .ToList(),
                // Issue #692: hedefte (müfredat, kod) ile eşleşir; hedefte olmayan kazanım atlanır.
                learningOutcomes = q.QuestionLearningOutcomes
                    .Where(link => link.LearningOutcome != null)
                    .Select(link => new
                    {
                        code = link.LearningOutcome.Code,
                        curriculumVersion = link.LearningOutcome.CurriculumVersion.ToString(),
                    })
                    .ToList(),
                // Issue #703: beceri kodla eşleşir; hedefte olmayan kod atlanır.
                skills = q.QuestionSkills
                    .Where(link => link.Skill != null)
                    .Select(link => link.Skill.Code)
                    .ToList(),
                passage = q.PassageId.HasValue && q.Passage != null
                    ? new
                    {
                        externalKey = $"passage:{q.Passage.Id}",
                        id = q.PassageId,
                        title = q.Passage.Title,
                        text = q.Passage.Text,
                        imageUrl = q.Passage.ImageUrl,
                        x = q.Passage.X,
                        y = q.Passage.Y,
                        width = q.Passage.Width,
                        height = q.Passage.Height,
                        kind = (int)q.Passage.Kind, // issue #573
                        instruction = q.Passage.Instruction,
                    }
                    : null,
                answers = q.Answers.Select(a => new
                {
                    id = a.Id,
                    text = a.Text,
                    tag = a.Tag,
                    order = a.Order,
                    imageUrl = a.ImageUrl,
                    isCorrect = q.CorrectAnswerId.HasValue && q.CorrectAnswerId.Value == a.Id,
                    x = a.X,
                    y = a.Y,
                    width = a.Width,
                    height = a.Height,
                }).ToList()
            }).ToList()
        };

        var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Fastest);
        await using var entryStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(entryStream, manifest);
    }

    /// <summary>
    /// issue #570: manifestten plan + çözüm. Ayrı <c>interactionSolution</c> (string) varsa o kullanılır; yoksa eski manifest
    /// planındaki <c>solution</c> ayrılır. Plan her durumda çözümsüz yazılır.
    /// </summary>
    internal static (string? Plan, string? Solution) ReadInteractionPlanAndSolution(JsonElement qEl)
    {
        var plan = qEl.TryGetProperty("interactionPlan", out var ip) && ip.ValueKind == JsonValueKind.String ? ip.GetString() : null;
        var explicitSolution = qEl.TryGetProperty("interactionSolution", out var isol) && isol.ValueKind == JsonValueKind.String
            ? isol.GetString()
            : null;
        // Geçersiz (nesne olmayan / yinelenen anahtarlı / sınır aşan) ayrı çözüm yok sayılır; plan içindeki çözüme düşülür.
        if (!InteractionPlanSolution.IsValidSolution(explicitSolution))
            explicitSolution = null;
        if (InteractionPlanSolution.TryResolveForWrite(plan, explicitSolution, existingSolution: null,
                out var storedPlan, out var storedSolution))
            return (storedPlan, storedSolution);

        // Katı kurala uymayan eski plan (geçersiz JSON, yinelenen anahtar, nesne olmayan gömülü çözüm): aktarım düşmez —
        // plan yine çözümsüz yazılır (ayrıştırılabiliyorsa), çözüm yalnız geçerliyse alınır.
        var (tolerantPlan, tolerantSolution) = InteractionPlanSolution.Split(plan);
        var solution = string.IsNullOrWhiteSpace(explicitSolution) ? tolerantSolution : explicitSolution.Trim();
        return (tolerantPlan, InteractionPlanSolution.IsValidSolution(solution) ? solution : null);
    }

    private async Task<List<Question>> LoadQuestionsAsync(List<int> ids)
    {
        ids = ids.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return new List<Question>();

        return await _db.Questions
            .Include(q => q.Answers)
            .Include(q => q.Subject)
            .Include(q => q.Topic)
                .ThenInclude(t => t.Grade)
            .Include(q => q.QuestionSubTopics)
                .ThenInclude(qst => qst.SubTopic)
                    .ThenInclude(st => st.Topic)
            .Include(q => q.QuestionLearningOutcomes)
                .ThenInclude(link => link.LearningOutcome)
            .Include(q => q.QuestionSkills)
                .ThenInclude(link => link.Skill)
            .Include(q => q.Passage)
            .Where(q => ids.Contains(q.Id))
            .ToListAsync();
    }

    private async Task WriteBundleMapAsync(string sourceKey, int bundleNo)
    {
        var ids = await _db.Set<QuestionTransferExportMap>()
            .Where(m => m.SourceKey == sourceKey && m.BundleNo == bundleNo)
            .OrderBy(m => m.QuestionId)
            .Select(m => m.QuestionId)
            .ToListAsync();

        var payload = new
        {
            sourceKey = sourceKey,
            bundleNo = bundleNo,
            questionIds = ids,
            updatedAtUtc = DateTime.UtcNow,
        };

        await using var ms = new MemoryStream();
        await JsonSerializer.SerializeAsync(ms, payload);
        ms.Position = 0;
        await _minio.UploadFileAsync(ms, BundleMapObjectName(sourceKey, bundleNo), contentType: "application/json");
    }

    private async Task<string> WriteSourceIndexAsync(string sourceKey)
    {
        var bundles = await _db.Set<QuestionTransferExportBundle>()
            .Where(b => b.SourceKey == sourceKey)
            .OrderBy(b => b.BundleNo)
            .Select(b => new { b.BundleNo, b.QuestionCount, b.FileUrl })
            .ToListAsync();

        var payload = new
        {
            sourceKey = sourceKey,
            bundleSize = ExportBundleSize,
            bundles = bundles,
            updatedAtUtc = DateTime.UtcNow,
        };

        await using var ms = new MemoryStream();
        await JsonSerializer.SerializeAsync(ms, payload);
        ms.Position = 0;
        return await _minio.UploadFileAsync(ms, SourceIndexObjectName(sourceKey), contentType: "application/json");
    }

    [Queue("question-transfer")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunImportAsync(Guid jobId, bool ownerIsAdmin)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstAsync(j => j.Id == jobId);
        if (await RejectIfOwnerNotApprovedAsync(job, ownerIsAdmin))
            return;

        job.Status = QuestionTransferJobStatus.Running;
        job.Message = "Running";
        await _db.SaveChangesAsync();

        try
        {
            if (string.IsNullOrWhiteSpace(job.FileUrl))
            {
                throw new InvalidOperationException("Import file URL is missing.");
            }

            // Eşleşme hedefteki hash'lere bakar: eksikleri tamamlanmadan başlanırsa kopya oluşabilir.
            job.Message = "Preparing question hashes";
            await _db.SaveChangesAsync();
            var hashFill = await _hashes.FillMissingAsync();
            if (hashFill.Unresolved > 0)
                throw new InvalidOperationException($"{hashFill.Unresolved} questions in this environment have no hash yet because storage could not be read. Try again later: importing now could create copies.");

            await using var zipStream = await _minio.GetFileStreamAsync(job.FileUrl)
                ?? throw new InvalidOperationException("Import file not found.");

            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
            var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidOperationException("manifest.json not found in zip.");

            using var doc = JsonDocument.Parse(await QuestionContentHash.ReadPackageEntryAsync(manifestEntry, QuestionContentHash.MaxPackageManifestBytes));

            var questions = doc.RootElement.GetProperty("questions").EnumerateArray().ToList();
            job.TotalItems = questions.Count;
            job.ProcessedItems = 0;
            await _db.SaveChangesAsync();

            var importedCount = 0;
            var skippedCount = 0;
            var failedCount = 0;
            var matchedCount = 0;
            var conflictCount = 0;
            var missingTaxonomyCount = 0;
            var failedSamples = new List<string>();
            var conflictSamples = new List<string>();
            // review #574 (L1): tanımı geçersiz etkileşim (soru yine içe alınır, çözüm/plan düşürülür). Anahtar = dış soru
            // anahtarı (yeniden denenen transaction aynı uyarıyı iki kez saymasın).
            var interactionWarnings = new Dictionary<string, string>(StringComparer.Ordinal);
            var unpublishedQuestionIds = new HashSet<int>();

            // In-memory maps for this import run
            var taxonomy = new QuestionImportTaxonomy(_db);
            var passageIdByExternalKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Reused across iterations for the per-question transaction below.
            var strategy = _db.Database.CreateExecutionStrategy();

            foreach (var qEl in questions)
            {
                var externalKey = qEl.TryGetProperty("externalKey", out var ek) ? (ek.GetString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(externalKey))
                {
                    failedCount++;
                    if (failedSamples.Count < 10)
                    {
                        failedSamples.Add("Missing externalKey");
                    }
                    job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Matched {matchedCount}, Skipped {skippedCount}, Conflicts {conflictCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                    continue;
                }

                // Fast-path skip when already imported (idempotent).
                var alreadyImportedAs = await _db.Set<QuestionTransferImportMap>()
                    .Where(m => m.SourceKey == job.SourceKey && m.ExternalQuestionKey == externalKey)
                    .Select(m => (int?)m.TargetQuestionId)
                    .FirstOrDefaultAsync();
                if (alreadyImportedAs is not null)
                {
                    // Daha önce alınmış soru kaynakta sonradan kaldırılmış olabilir: yayından kaldırma bu yolda da taşınır.
                    if (IsUnpublishedInPackage(qEl) && await _db.Questions.FirstOrDefaultAsync(q => q.Id == alreadyImportedAs && !q.IsUnpublished) is { } alreadyImported)
                    {
                        alreadyImported.IsUnpublished = true;
                        unpublishedQuestionIds.Add(alreadyImported.Id);
                    }
                    skippedCount++;
                    job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Matched {matchedCount}, Skipped {skippedCount}, Conflicts {conflictCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                    continue;
                }

                // Aynı içerik hedefte varsa kopya oluşturulmaz. Aynı görsel farklı cevapla varsa ikisine de dokunulmaz.
                QuestionMatch match;
                QuestionContentHashes contentHashes;
                try
                {
                    var image = await ReadQuestionImageAsync(zip, qEl);
                    if (QuestionContentHash.IsImageMissing(qEl, image))
                        throw new InvalidOperationException("the question image is missing from the package");
                    contentHashes = QuestionContentHash.ForManifest(qEl, image);
                    match = await QuestionContentHash.FindMatchAsync(_db, contentHashes);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    failedCount++;
                    if (failedSamples.Count < MaxResultSamples)
                        failedSamples.Add($"{externalKey}: {ex.Message}");
                    job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                    await _db.SaveChangesAsync();
                    continue;
                }

                if (match.Kind == QuestionMatchKind.Conflict)
                {
                    conflictCount++;
                    if (conflictSamples.Count < MaxResultSamples)
                        conflictSamples.Add($"{externalKey}: same image as question {match.TargetQuestionId} with different content");
                    job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                    await _db.SaveChangesAsync();
                    continue;
                }

                var resolvedTaxonomy = await taxonomy.ResolveAsync(qEl);
                if (resolvedTaxonomy.Missing)
                    missingTaxonomyCount++;
                var createdPassageKeys = new List<string>();

                // Acquire an idempotency lock row inside a transaction to prevent concurrent imports of the same key.
                // Wrapped in CreateExecutionStrategy so EF Core's retry-on-failure
                // can retry the transaction as one unit instead of rejecting it
                // outright. Note: a retried attempt would redo this iteration's
                // MinIO uploads too (they're not transactional) — acceptable
                // here since retries only fire for genuinely transient DB
                // errors, not the routine unique-constraint "already imported"
                // case, which is caught separately below and never retried.
                await strategy.ExecuteAsync(async () =>
                {
                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    var lockRow = new QuestionTransferImportMap
                    {
                        SourceKey = job.SourceKey,
                        ExternalQuestionKey = externalKey,
                        TargetQuestionId = 0
                    };

                    _db.Set<QuestionTransferImportMap>().Add(lockRow);
                    await _db.SaveChangesAsync();

                    if (match.Kind == QuestionMatchKind.Exact)
                    {
                        var unpublishedHere = await FillBlanksAsync(match.TargetQuestionId!.Value, resolvedTaxonomy, qEl);
                        lockRow.TargetQuestionId = match.TargetQuestionId.Value;
                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();
                        if (unpublishedHere)
                            unpublishedQuestionIds.Add(match.TargetQuestionId.Value);

                        matchedCount++;
                        job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                        job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Matched {matchedCount}, Skipped {skippedCount}, Conflicts {conflictCount}, Failed {failedCount})";
                        await _db.SaveChangesAsync();
                        return;
                    }

                    // issue #570: interactionSolution ayrı alan (manifest v2+); yoksa (eski manifest) plan içindeki solution ayrılır.
                    var (importedPlan, importedSolution) = ReadInteractionPlanAndSolution(qEl);
                    // review #574 (L1/L3): tip kanonik; tanım kontrolcüyle doğrulanır. Plan geçerli ama çözüm tutarsızsa çözüm,
                    // plan da geçersizse ikisi düşürülür (öğrenciye bilinmeyen alan taşıyan plan gitmesin) ve uyarı yazılır.
                    var importedType = _graders.CanonicalType(
                        qEl.TryGetProperty("interactionType", out var it) && it.ValueKind == JsonValueKind.String ? it.GetString() : null);
                    var interactionGrader = _graders.Resolve(importedType);
                    if (!interactionGrader.ValidateDefinition(importedPlan, importedSolution))
                    {
                        string dropped;
                        if (interactionGrader.ValidateDefinition(importedPlan, null))
                        {
                            importedSolution = null;
                            dropped = "solution";
                        }
                        else
                        {
                            importedPlan = null;
                            importedSolution = null;
                            dropped = "plan and solution";
                        }
                        interactionWarnings[externalKey] = $"{externalKey}: invalid {importedType} definition, {dropped} dropped";
                    }
                    // review #576 (H1/M1): saklanacak biçim (dragPlace / match: karışık sıra + opak id).
                    (importedPlan, importedSolution) = interactionGrader.PrepareForStorage(importedPlan, importedSolution);

                    var question = new Question
                    {
                        Text = qEl.GetProperty("text").GetString(),
                        SubText = qEl.TryGetProperty("subText", out var st) ? st.GetString() : null,
                        SubjectId = resolvedTaxonomy.SubjectId,
                        TopicId = resolvedTaxonomy.TopicId,
                        Point = qEl.TryGetProperty("point", out var pointEl) && pointEl.ValueKind == JsonValueKind.Number && pointEl.TryGetInt32(out var pointValue) ? Math.Clamp(pointValue, 0, 100) : 0,
                        BookName = qEl.TryGetProperty("bookName", out var bookNameEl) && bookNameEl.ValueKind == JsonValueKind.String ? bookNameEl.GetString() : null,
                        SourceType = ParseSourceType(qEl),
                        RightsHolder = qEl.TryGetProperty("rightsHolder", out var rightsEl) && rightsEl.ValueKind == JsonValueKind.String ? rightsEl.GetString() : null,
                        LicenseReference = qEl.TryGetProperty("licenseReference", out var licenseEl) && licenseEl.ValueKind == JsonValueKind.String ? licenseEl.GetString() : null,
                        LicenseEndsAt = qEl.TryGetProperty("licenseEndsAt", out var licenseEndsEl) && licenseEndsEl.ValueKind == JsonValueKind.String ? licenseEndsEl.GetDateTime() : null,
                        IsUnpublished = qEl.TryGetProperty("isUnpublished", out var unpublishedEl) && unpublishedEl.ValueKind == JsonValueKind.True,
                        IsContextBased = IsContextBasedInPackage(qEl),
                        DifficultyLevel = ParseDifficultyLevel(qEl),
                        ClassificationSource = ParseClassificationSource(qEl),
                        ShowPassageFirst = qEl.TryGetProperty("showPassageFirst", out var spf) && spf.ValueKind == JsonValueKind.True,
                        IsExample = qEl.TryGetProperty("isExample", out var ie) && ie.ValueKind == JsonValueKind.True,
                        PracticeCorrectAnswer = qEl.TryGetProperty("practiceCorrectAnswer", out var pca) ? pca.GetString() : null,
                        AnswerColCount = qEl.TryGetProperty("answerColCount", out var acc) && acc.ValueKind == JsonValueKind.Number ? acc.GetInt32() : 3,
                        InteractionType = importedType,
                        InteractionPlan = importedPlan,
                        InteractionSolution = importedSolution,
                        X = qEl.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : null,
                        Y = qEl.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetDouble() : null,
                        Width = qEl.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : null,
                        Height = qEl.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : null,
                        // Metin soru kanvas sayılırsa oynatıcıda boş görünür. Alan eski pakette yoktur: o paketler kanvas sorusu taşır.
                        IsCanvasQuestion = !qEl.TryGetProperty("isCanvasQuestion", out var isCanvas) || isCanvas.ValueKind != JsonValueKind.False,
                    };

                    // Passage (dedupe within this import)
                    if (qEl.TryGetProperty("passage", out var passageEl) && passageEl.ValueKind == JsonValueKind.Object)
                    {
                        var passageExternalKey = passageEl.TryGetProperty("externalKey", out var pek) ? pek.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(passageExternalKey))
                        {
                            if (!passageIdByExternalKey.TryGetValue(passageExternalKey!, out var targetPassageId))
                            {
                                var sourcePassageId = passageEl.TryGetProperty("id", out var pidEl) && pidEl.ValueKind != JsonValueKind.Null
                                    ? pidEl.GetInt32()
                                    : 0;

                                var passage = new Passage
                                {
                                    Title = passageEl.TryGetProperty("title", out var pt) ? pt.GetString() : null,
                                    Text = passageEl.TryGetProperty("text", out var ptxt) ? ptxt.GetString() : null,
                                    X = passageEl.TryGetProperty("x", out var px) && px.ValueKind == JsonValueKind.Number ? px.GetDouble() : null,
                                    Y = passageEl.TryGetProperty("y", out var py) && py.ValueKind == JsonValueKind.Number ? py.GetDouble() : null,
                                    Width = passageEl.TryGetProperty("width", out var pw) && pw.ValueKind != JsonValueKind.Null ? pw.GetInt32() : null,
                                    Height = passageEl.TryGetProperty("height", out var ph) && ph.ValueKind != JsonValueKind.Null ? ph.GetInt32() : null,
                                    IsCanvasQuestion = true,
                                    // issue #573: tür + yönerge taşınır; eski paketlerde alan yoksa Reading / null.
                                    Kind = ReadPassageKind(passageEl),
                                    Instruction = ReadPassageInstruction(passageEl),
                                };

                                // Upload passage image (best-effort)
                                if (sourcePassageId > 0)
                                {
                                    var passageImgEntry = zip.GetEntry($"passages/{sourcePassageId}.jpg");
                                    if (passageImgEntry != null)
                                    {
                                        await using var mem = new MemoryStream(await QuestionContentHash.ReadPackageEntryAsync(passageImgEntry, QuestionContentHash.MaxPackageImageBytes));
                                        passage.ImageUrl = await _minio.UploadFileAsync(mem, $"passages/{Guid.NewGuid()}.jpg");
                                    }
                                }

                                _db.Passage.Add(passage);
                                await _db.SaveChangesAsync();

                                targetPassageId = passage.Id;
                                passageIdByExternalKey[passageExternalKey!] = targetPassageId;
                                createdPassageKeys.Add(passageExternalKey!);
                            }

                            question.PassageId = targetPassageId;
                        }
                    }

                    _db.Questions.Add(question);
                    await _db.SaveChangesAsync();

                    var sourceQuestionId = qEl.GetProperty("id").GetInt32();
                    var folder = $"questions/{Guid.NewGuid()}";

                    var qImgEntry = zip.GetEntry($"questions/{sourceQuestionId}/question.jpg");
                    if (qImgEntry != null)
                    {
                        await using var mem = new MemoryStream(await QuestionContentHash.ReadPackageEntryAsync(qImgEntry, QuestionContentHash.MaxPackageImageBytes));
                        question.ImageUrl = await _minio.UploadFileAsync(mem, $"{folder}/question.jpg");
                    }

                    var qV2Entry = zip.GetEntry($"questions/{sourceQuestionId}/question-v2.jpg");
                    if (qV2Entry != null)
                    {
                        await using var mem = new MemoryStream(await QuestionContentHash.ReadPackageEntryAsync(qV2Entry, QuestionContentHash.MaxPackageImageBytes));
                        await _minio.UploadFileAsync(mem, $"{folder}/question-v2.jpg");
                    }

                    if (qEl.TryGetProperty("answers", out var answersEl) && answersEl.ValueKind == JsonValueKind.Array)
                    {
                        var answerEls = answersEl.EnumerateArray().ToList();
                        var answers = new List<Answer>();

                        foreach (var aEl in answerEls)
                        {
                            var a = new Answer
                            {
                                QuestionId = question.Id,
                                Text = aEl.GetProperty("text").GetString() ?? string.Empty,
                                Tag = aEl.TryGetProperty("tag", out var tag) ? tag.GetString() : null,
                                Order = aEl.TryGetProperty("order", out var order) && order.ValueKind != JsonValueKind.Null ? order.GetInt32() : null,
                                X = aEl.TryGetProperty("x", out var ax) && ax.ValueKind == JsonValueKind.Number ? ax.GetDouble() : null,
                                Y = aEl.TryGetProperty("y", out var ay) && ay.ValueKind == JsonValueKind.Number ? ay.GetDouble() : null,
                                Width = aEl.TryGetProperty("width", out var aw) && aw.ValueKind != JsonValueKind.Null ? aw.GetInt32() : null,
                                Height = aEl.TryGetProperty("height", out var ah) && ah.ValueKind != JsonValueKind.Null ? ah.GetInt32() : null,
                                IsCanvasQuestion = true,
                            };
                            answers.Add(a);
                        }

                        _db.Answers.AddRange(answers);
                        await _db.SaveChangesAsync();

                        for (var i = 0; i < answers.Count; i++)
                        {
                            var srcAnswerId = answerEls[i].GetProperty("id").GetInt32();
                            var entry = zip.GetEntry($"questions/{sourceQuestionId}/answers/{srcAnswerId}.jpg");
                            if (entry == null) continue;

                            await using var mem = new MemoryStream(await QuestionContentHash.ReadPackageEntryAsync(entry, QuestionContentHash.MaxPackageImageBytes));
                            answers[i].ImageUrl = await _minio.UploadFileAsync(mem, $"{folder}/{answers[i].Id}.jpg");
                        }

                        await _db.SaveChangesAsync();

                        for (var i = 0; i < answers.Count; i++)
                        {
                            var isCorrect = answerEls[i].TryGetProperty("isCorrect", out var ic) && ic.ValueKind == JsonValueKind.True;
                            if (isCorrect)
                            {
                                question.CorrectAnswerId = answers[i].Id;
                                break;
                            }
                        }

                        await _db.SaveChangesAsync();
                    }

                    foreach (var stId in resolvedTaxonomy.SubTopicIds)
                    {
                        _db.QuestionSubTopics.Add(new QuestionSubTopic
                        {
                            QuestionId = question.Id,
                            SubTopicId = stId
                        });
                    }
                    await _db.SaveChangesAsync();

                    if (qEl.TryGetProperty("learningOutcomes", out var outcomesEl) && outcomesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var outcomeId in await ResolveLearningOutcomesAsync(_db, outcomesEl))
                        {
                            _db.QuestionLearningOutcomes.Add(new QuestionLearningOutcome
                            {
                                QuestionId = question.Id,
                                LearningOutcomeId = outcomeId
                            });
                        }
                        await _db.SaveChangesAsync();
                    }

                    if (qEl.TryGetProperty("skills", out var skillsEl) && skillsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var skillId in await ResolveSkillsAsync(_db, skillsEl))
                            _db.QuestionSkills.Add(new QuestionSkill { QuestionId = question.Id, SkillId = skillId });
                        await _db.SaveChangesAsync();
                    }

                    lockRow.TargetQuestionId = question.Id;
                    // Kaynağın hash'i saklanır: plan kayıtta yeniden karıştırılsa da aynı paket tekrar eşleşir.
                    question.ContentHash = contentHashes.ContentHash;
                    question.ContentFingerprint = contentHashes.Fingerprint;
                    await _db.SaveChangesAsync();

                    await tx.CommitAsync();

                    importedCount++;
                    job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Matched {matchedCount}, Skipped {skippedCount}, Conflicts {conflictCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    await tx.RollbackAsync();
                    job = await ForgetFailedAttemptAsync(jobId, createdPassageKeys, passageIdByExternalKey);

                    // Yalnız başka bir iş aynı anahtarı yazmışsa "zaten aktarıldı" denir; diğer veritabanı hataları soruyu sessizce kaybettirmesin.
                    var importedElsewhere = await _db.Set<QuestionTransferImportMap>()
                        .AnyAsync(m => m.SourceKey == job.SourceKey && m.ExternalQuestionKey == externalKey);
                    if (importedElsewhere)
                    {
                        skippedCount++;
                    }
                    else
                    {
                        failedCount++;
                        if (failedSamples.Count < MaxResultSamples)
                            failedSamples.Add($"{externalKey}: {ex.InnerException?.Message ?? ex.Message}");
                    }
                    job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Matched {matchedCount}, Skipped {skippedCount}, Conflicts {conflictCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    job = await ForgetFailedAttemptAsync(jobId, createdPassageKeys, passageIdByExternalKey);
                    failedCount++;
                    if (failedSamples.Count < 10)
                    {
                        failedSamples.Add($"{externalKey}: {ex.Message}");
                    }
                    job.ProcessedItems = importedCount + matchedCount + skippedCount + conflictCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Matched {matchedCount}, Skipped {skippedCount}, Conflicts {conflictCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                }
                });
            }

            // Sorular işlendi: test ve etkinlikler bulunan kimliklere bağlanır. Hata alan kayıt izleyiciyi temizler:
            // işin durumu bundan sonra yazılır, yoksa iş "çalışıyor" olarak kalır.
            // Issue #819: işi başlatan isteğin paylaşım seçimi; eski işlerde (RequestJson yok) bugünkü varsayılan.
            var worksheetOptions = string.IsNullOrWhiteSpace(job.RequestJson)
                ? new Models.Dtos.QuestionImportWorksheetOptions()
                : JsonSerializer.Deserialize<Models.Dtos.QuestionImportWorksheetOptions>(job.RequestJson) ?? new Models.Dtos.QuestionImportWorksheetOptions();
            if (!worksheetOptions.IsValid)
                worksheetOptions = new Models.Dtos.QuestionImportWorksheetOptions();
            var worksheetResult = await new QuestionTransferWorksheetImporter(_db, _minio, _logger)
                .ImportAsync(zip, doc.RootElement, job.SourceKey, job.CreateUserId, worksheetOptions);
            job = await _db.Set<QuestionTransferJob>().FirstAsync(j => j.Id == jobId);

            job.Status = QuestionTransferJobStatus.Completed;
            var summary = $"Completed. Imported {importedCount}, Matched {matchedCount}, Skipped {skippedCount}, Conflicts {conflictCount}, Failed {failedCount}, Missing taxonomy {missingTaxonomyCount}.";
            if (failedSamples.Count > 0)
            {
                summary += " Samples: " + string.Join(" | ", failedSamples);
            }
            if (interactionWarnings.Count > 0)
            {
                summary += $" Warnings {interactionWarnings.Count}: " + string.Join(" | ", interactionWarnings.Values.Take(10));
            }
            job.Message = summary;

            // Normal yolda (#690) olduğu gibi yayından kaldırmanın izi kalır; hangi soruların kalktığı iş sonucundadır.
            if (unpublishedQuestionIds.Count > 0)
            {
                _db.QuestionSourceActionLogs.Add(new QuestionSourceActionLog
                {
                    RightsHolder = string.Empty,
                    Unpublished = true,
                    AffectedCount = unpublishedQuestionIds.Count,
                    Reason = $"Question transfer job {job.Id}, started by user {job.CreateUserId}",
                });
            }

            job.ResultJson = JsonSerializer.Serialize(new QuestionTransferImportResultDto
            {
                Worksheets = worksheetResult,
                Imported = importedCount,
                Matched = matchedCount,
                AlreadyImported = skippedCount,
                Conflicts = conflictCount,
                Failed = failedCount,
                MissingTaxonomy = missingTaxonomyCount,
                Unpublished = unpublishedQuestionIds.Count,
                UnpublishedQuestionIds = unpublishedQuestionIds.Order().ToList(),
                ConflictSamples = conflictSamples,
                FailedSamples = failedSamples,
                Warnings = interactionWarnings.Values.Take(MaxResultSamples).ToList(),
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // İzleyici temizlenmiş olabilir: eski nesneye yazılan "başarısız" kaydedilmez, iş çalışıyor görünür.
            _db.ChangeTracker.Clear();
            job = await _db.Set<QuestionTransferJob>().FirstAsync(j => j.Id == jobId);
            job.Status = QuestionTransferJobStatus.Failed;
            job.Message = ex.Message;
            await _db.SaveChangesAsync();
        }
    }


    /// <summary>issue #573: içe aktarılan kapsam türü — tanımsız/eksik değer Reading.</summary>
    private static PassageKind ReadPassageKind(JsonElement passageEl)
        => passageEl.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.Number
            && k.TryGetInt32(out var kv) && Enum.IsDefined(typeof(PassageKind), kv)
                ? (PassageKind)kv
                : PassageKind.Reading;

    /// <summary>issue #573: içe aktarılan yönerge — string değilse null, sınırdan uzunsa kırpılır.</summary>
    private static string? ReadPassageInstruction(JsonElement passageEl)
    {
        if (!passageEl.TryGetProperty("instruction", out var i) || i.ValueKind != JsonValueKind.String)
            return null;
        var value = i.GetString();
        return value is { Length: > Passage.MaxInstructionLength } ? value[..Passage.MaxInstructionLength] : value;
    }

    private static string? TransformQuestionV2Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        return System.Text.RegularExpressions.Regex.Replace(url, "question(\\.[^/?#]+)?$", m =>
        {
            var ext = m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty;
            return $"question-v2{ext}";
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static int ParseDifficultyLevel(JsonElement qEl)
    {
        if (!qEl.TryGetProperty("difficultyLevel", out var dl) || dl.ValueKind != JsonValueKind.Number)
        {
            return 1;
        }

        var value = dl.GetInt32();
        return Math.Clamp(value, 1, 10);
    }

    private static ClassificationSource? ParseClassificationSource(JsonElement qEl)
    {
        if (!qEl.TryGetProperty("classificationSource", out var cs))
        {
            return null;
        }

        var source = cs.ValueKind switch
        {
            JsonValueKind.String => cs.GetString(),
            JsonValueKind.Number => cs.TryGetInt32(out var numberValue) && Enum.IsDefined((ClassificationSource)numberValue)
                ? ((ClassificationSource)numberValue).ToString()
                : null,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        // Issue #577 (security L5): tanımsız sayısal değer ("99" ya da 99) enum'a dökülüp saklanmasın.
        return Enum.TryParse<ClassificationSource>(source, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }

    private async Task TryAddFromMinioAsync(ZipArchive zip, string? url, string entryPath)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        await using var stream = await _minio.GetFileStreamAsync(url);
        if (stream == null) return;

        var entry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await stream.CopyToAsync(entryStream);
    }

    private static QuestionSourceType ParseSourceType(JsonElement qEl) =>
        qEl.TryGetProperty("sourceType", out var el) && el.ValueKind == JsonValueKind.String
            && Enum.TryParse<QuestionSourceType>(el.GetString(), ignoreCase: true, out var value) && Enum.IsDefined(value) && !int.TryParse(el.GetString(), out _)
            ? value
            : QuestionSourceType.Unknown;

    private static async Task<byte[]?> ReadQuestionImageAsync(ZipArchive zip, JsonElement qEl)
    {
        var entry = zip.GetEntry($"questions/{qEl.GetProperty("id").GetInt32()}/question.jpg");
        return entry is null ? null : await QuestionContentHash.ReadPackageEntryAsync(entry, QuestionContentHash.MaxPackageImageBytes);
    }

    /// <summary>
    /// Hedefteki aynı soru değiştirilmez: yalnız boş sınıflandırma ve kaynağı bilinmeyen sorunun lisans bilgisi doldurulur, eksik alt konu ve
    /// kazanım bağları eklenir. Tek istisna yayından kaldırmadır: hak sahibi talebi hedefte de geçerli olmalı, soru kaldırılır ve true döner.
    /// Ters yön yoktur, paket hedefte kaldırılmış soruyu yeniden yayına almaz.
    /// </summary>
    private async Task<bool> FillBlanksAsync(int questionId, QuestionImportTaxonomy.Resolved taxonomy, JsonElement qEl)
    {
        var target = await _db.Questions
            .Include(q => q.QuestionSubTopics)
            .Include(q => q.QuestionLearningOutcomes)
            .Include(q => q.QuestionSkills)
            .FirstAsync(q => q.Id == questionId);

        target.SubjectId ??= taxonomy.SubjectId;
        target.TopicId ??= taxonomy.TopicId;

        if (target.SourceType == QuestionSourceType.Unknown)
        {
            target.SourceType = ParseSourceType(qEl);
            if (qEl.TryGetProperty("rightsHolder", out var rights) && rights.ValueKind == JsonValueKind.String)
                target.RightsHolder ??= rights.GetString();
            if (qEl.TryGetProperty("licenseReference", out var license) && license.ValueKind == JsonValueKind.String)
                target.LicenseReference ??= license.GetString();
            if (qEl.TryGetProperty("licenseEndsAt", out var licenseEnds) && licenseEnds.ValueKind == JsonValueKind.String)
                target.LicenseEndsAt ??= licenseEnds.GetDateTime();
        }

        var subTopicIds = target.QuestionSubTopics.Select(s => s.SubTopicId).ToHashSet();
        foreach (var id in taxonomy.SubTopicIds.Where(subTopicIds.Add))
            _db.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = questionId, SubTopicId = id });

        if (qEl.TryGetProperty("learningOutcomes", out var outcomesEl) && outcomesEl.ValueKind == JsonValueKind.Array)
        {
            var outcomeIds = target.QuestionLearningOutcomes.Select(l => l.LearningOutcomeId).ToHashSet();
            foreach (var id in (await ResolveLearningOutcomesAsync(_db, outcomesEl)).Where(outcomeIds.Add))
                _db.QuestionLearningOutcomes.Add(new QuestionLearningOutcome { QuestionId = questionId, LearningOutcomeId = id });
        }

        // Issue #703: etiket boşsa doldurulur; hedefte verilmiş etiket ezilmez.
        if (!target.IsContextBased && IsContextBasedInPackage(qEl))
            target.IsContextBased = true;
        if (qEl.TryGetProperty("skills", out var skillsEl) && skillsEl.ValueKind == JsonValueKind.Array)
        {
            var skillIds = target.QuestionSkills.Select(l => l.SkillId).ToHashSet();
            foreach (var id in (await ResolveSkillsAsync(_db, skillsEl)).Where(skillIds.Add))
                _db.QuestionSkills.Add(new QuestionSkill { QuestionId = questionId, SkillId = id });
        }

        var unpublishedHere = !target.IsUnpublished && IsUnpublishedInPackage(qEl);
        if (unpublishedHere)
            target.IsUnpublished = true;

        await _db.SaveChangesAsync();
        return unpublishedHere;
    }

    /// <summary>
    /// Geri alınan denemenin izleri temizlenir: takipte kalan satırlar sonraki kayıtta yeniden yazılmaya çalışılır,
    /// geri alınan paragrafın kimliği de sonraki soruda yabancı anahtar hatası verirdi.
    /// </summary>
    private async Task<QuestionTransferJob> ForgetFailedAttemptAsync(Guid jobId, List<string> createdPassageKeys, Dictionary<string, int> passageIdByExternalKey)
    {
        foreach (var key in createdPassageKeys)
            passageIdByExternalKey.Remove(key);
        _db.ChangeTracker.Clear();
        return await _db.Set<QuestionTransferJob>().FirstAsync(j => j.Id == jobId);
    }


    /// <summary>Issue #703: paketteki beceri kodlarının hedefteki kimlikleri; bilinmeyen kod atlanır.</summary>
    internal static async Task<List<int>> ResolveSkillsAsync(AppDbContext db, JsonElement skillsEl)
    {
        var codes = skillsEl.EnumerateArray()
            .Where(el => el.ValueKind == JsonValueKind.String)
            .Select(el => el.GetString()!)
            .Distinct()
            .ToList();
        if (codes.Count == 0)
            return [];
        return await db.Skills.Where(s => codes.Contains(s.Code)).Select(s => s.Id).ToListAsync();
    }

    /// <summary>Issue #692: paketteki kazanımların hedefteki kimlikleri; hedefte olmayan ya da tanımsız müfredatlı olan atlanır.</summary>
    internal static async Task<List<int>> ResolveLearningOutcomesAsync(AppDbContext db, JsonElement outcomesEl)
    {
        var ids = new List<int>();
        foreach (var el in outcomesEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object
                || !el.TryGetProperty("code", out var codeEl) || codeEl.ValueKind != JsonValueKind.String
                || !el.TryGetProperty("curriculumVersion", out var versionEl) || versionEl.ValueKind != JsonValueKind.String
                || !CurriculumVersionParser.TryParse(versionEl.GetString(), out var version))
            {
                continue;
            }

            var code = codeEl.GetString();
            var id = await db.LearningOutcomes
                .Where(lo => lo.CurriculumVersion == version && lo.Code == code)
                .Select(lo => (int?)lo.Id)
                .FirstOrDefaultAsync();
            if (id.HasValue && !ids.Contains(id.Value))
                ids.Add(id.Value);
        }

        return ids;
    }
}
