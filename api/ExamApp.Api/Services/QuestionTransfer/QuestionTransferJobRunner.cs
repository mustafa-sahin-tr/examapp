using ExamApp.Api.Data;
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

    private const int ExportBundleSize = 2000;

    public QuestionTransferJobRunner(AppDbContext db, IMinIoService minio)
    {
        _db = db;
        _minio = minio;
    }

    [Queue("question-transfer")]
    [AutomaticRetry(Attempts = 2)]
    public async Task RunExportAsync(Guid jobId)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstAsync(j => j.Id == jobId);
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

        var objectName = BundleObjectName(sourceKey, bundleNo);
        var tmpPath = Path.Combine(Path.GetTempPath(), $"export-{sourceKey}-bundle-{bundleNo:D4}.zip");

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

    private async Task RewriteBundleManifestAsync(ZipArchive zip, string sourceKey, int bundleNo, List<int> bundleQuestionIds, bool allowDeleteExisting)
    {
        if (allowDeleteExisting)
        {
            var existingManifest = zip.GetEntry("manifest.json");
            existingManifest?.Delete();
        }

        var questions = await LoadQuestionsAsync(bundleQuestionIds);

        var manifest = new
        {
            version = 2,
            sourceKey = sourceKey,
            bundleNo = bundleNo,
            exportedAtUtc = DateTime.UtcNow,
            questions = questions.Select(q => new
            {
                externalKey = $"question:{q.Id}",
                id = q.Id,
                text = q.Text,
                subText = q.SubText,
                imageUrl = q.ImageUrl,
                showPassageFirst = q.ShowPassageFirst,
                isExample = q.IsExample,
                practiceCorrectAnswer = q.PracticeCorrectAnswer,
                answerColCount = q.AnswerColCount,
                layoutPlan = q.LayoutPlan,
                interactionType = q.InteractionType,
                interactionPlan = q.InteractionPlan,
                x = q.X,
                y = q.Y,
                width = q.Width,
                height = q.Height,
                subject = q.SubjectId.HasValue && q.Subject != null
                    ? new { id = q.SubjectId, name = q.Subject.Name }
                    : null,
                topic = q.TopicId.HasValue && q.Topic != null
                    ? new { id = q.TopicId, name = q.Topic.Name, gradeId = q.Topic.GradeId, gradeName = q.Topic.Grade?.Name }
                    : null,
                subTopics = q.QuestionSubTopics
                    .Where(st => st.SubTopic != null)
                    .Select(st => new
                    {
                        id = st.SubTopicId,
                        name = st.SubTopic.Name,
                        topicName = st.SubTopic.Topic?.Name,
                        gradeName = st.SubTopic.Topic?.Grade?.Name,
                    })
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
    public async Task RunImportAsync(Guid jobId)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstAsync(j => j.Id == jobId);
        job.Status = QuestionTransferJobStatus.Running;
        job.Message = "Running";
        await _db.SaveChangesAsync();

        try
        {
            if (string.IsNullOrWhiteSpace(job.FileUrl))
            {
                throw new InvalidOperationException("Import file URL is missing.");
            }

            await using var zipStream = await _minio.GetFileStreamAsync(job.FileUrl)
                ?? throw new InvalidOperationException("Import file not found.");

            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
            var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidOperationException("manifest.json not found in zip.");

            using var manifestStream = manifestEntry.Open();
            using var doc = await JsonDocument.ParseAsync(manifestStream);

            var questions = doc.RootElement.GetProperty("questions").EnumerateArray().ToList();
            job.TotalItems = questions.Count;
            job.ProcessedItems = 0;
            await _db.SaveChangesAsync();

            var importedCount = 0;
            var skippedCount = 0;
            var failedCount = 0;
            var failedSamples = new List<string>();

            // In-memory maps for this import run
            var subjectIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var topicIdByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var subTopicIdByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var passageIdByExternalKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
                    job.ProcessedItems = importedCount + skippedCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Skipped {skippedCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                    continue;
                }

                // Fast-path skip when already imported (idempotent).
                var already = await _db.Set<QuestionTransferImportMap>()
                    .AnyAsync(m => m.SourceKey == job.SourceKey && m.ExternalQuestionKey == externalKey);
                if (already)
                {
                    skippedCount++;
                    job.ProcessedItems = importedCount + skippedCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Skipped {skippedCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                    continue;
                }

                // Acquire an idempotency lock row inside a transaction to prevent concurrent imports of the same key.
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

                    var (subjectId, topicId) = await ResolveTaxonomyAsync(qEl, subjectIdByName, topicIdByKey);

                    var question = new Question
                    {
                        Text = qEl.GetProperty("text").GetString(),
                        SubText = qEl.TryGetProperty("subText", out var st) ? st.GetString() : null,
                        SubjectId = subjectId,
                        TopicId = topicId,
                        ShowPassageFirst = qEl.TryGetProperty("showPassageFirst", out var spf) && spf.ValueKind == JsonValueKind.True,
                        IsExample = qEl.TryGetProperty("isExample", out var ie) && ie.ValueKind == JsonValueKind.True,
                        PracticeCorrectAnswer = qEl.TryGetProperty("practiceCorrectAnswer", out var pca) ? pca.GetString() : null,
                        AnswerColCount = qEl.TryGetProperty("answerColCount", out var acc) ? acc.GetInt32() : 3,
                        LayoutPlan = qEl.TryGetProperty("layoutPlan", out var lp) ? lp.GetString() : null,
                        InteractionType = qEl.TryGetProperty("interactionType", out var it) ? it.GetString() : null,
                        InteractionPlan = qEl.TryGetProperty("interactionPlan", out var ip) ? ip.GetString() : null,
                        X = qEl.TryGetProperty("x", out var x) ? x.GetDouble() : null,
                        Y = qEl.TryGetProperty("y", out var y) ? y.GetDouble() : null,
                        Width = qEl.TryGetProperty("width", out var w) ? w.GetInt32() : null,
                        Height = qEl.TryGetProperty("height", out var h) ? h.GetInt32() : null,
                        IsCanvasQuestion = true,
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
                                    X = passageEl.TryGetProperty("x", out var px) ? px.GetDouble() : null,
                                    Y = passageEl.TryGetProperty("y", out var py) ? py.GetDouble() : null,
                                    Width = passageEl.TryGetProperty("width", out var pw) && pw.ValueKind != JsonValueKind.Null ? pw.GetInt32() : null,
                                    Height = passageEl.TryGetProperty("height", out var ph) && ph.ValueKind != JsonValueKind.Null ? ph.GetInt32() : null,
                                    IsCanvasQuestion = true,
                                };

                                // Upload passage image (best-effort)
                                if (sourcePassageId > 0)
                                {
                                    var passageImgEntry = zip.GetEntry($"passages/{sourcePassageId}.jpg");
                                    if (passageImgEntry != null)
                                    {
                                        await using var imgStream = passageImgEntry.Open();
                                        await using var mem = new MemoryStream();
                                        await imgStream.CopyToAsync(mem);
                                        mem.Position = 0;
                                        passage.ImageUrl = await _minio.UploadFileAsync(mem, $"passages/{Guid.NewGuid()}.jpg");
                                    }
                                }

                                _db.Passage.Add(passage);
                                await _db.SaveChangesAsync();

                                targetPassageId = passage.Id;
                                passageIdByExternalKey[passageExternalKey!] = targetPassageId;
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
                        await using var imgStream = qImgEntry.Open();
                        await using var mem = new MemoryStream();
                        await imgStream.CopyToAsync(mem);
                        mem.Position = 0;
                        question.ImageUrl = await _minio.UploadFileAsync(mem, $"{folder}/question.jpg");
                    }

                    var qV2Entry = zip.GetEntry($"questions/{sourceQuestionId}/question-v2.jpg");
                    if (qV2Entry != null)
                    {
                        await using var imgStream = qV2Entry.Open();
                        await using var mem = new MemoryStream();
                        await imgStream.CopyToAsync(mem);
                        mem.Position = 0;
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
                                X = aEl.TryGetProperty("x", out var ax) ? ax.GetDouble() : null,
                                Y = aEl.TryGetProperty("y", out var ay) ? ay.GetDouble() : null,
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

                            await using var imgStream = entry.Open();
                            await using var mem = new MemoryStream();
                            await imgStream.CopyToAsync(mem);
                            mem.Position = 0;
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

                    // Subtopic join records (best-effort mapping by name)
                    if (qEl.TryGetProperty("subTopics", out var subTopicsEl) && subTopicsEl.ValueKind == JsonValueKind.Array)
                    {
                        var resolved = await ResolveSubTopicsAsync(subTopicsEl, subTopicIdByKey);
                        foreach (var stId in resolved.Distinct())
                        {
                            _db.QuestionSubTopics.Add(new QuestionSubTopic
                            {
                                QuestionId = question.Id,
                                SubTopicId = stId
                            });
                        }
                        await _db.SaveChangesAsync();
                    }

                    lockRow.TargetQuestionId = question.Id;
                    await _db.SaveChangesAsync();

                    await tx.CommitAsync();

                    importedCount++;
                    job.ProcessedItems = importedCount + skippedCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Skipped {skippedCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Most likely: another import job already inserted the same (SourceKey, ExternalQuestionKey).
                    await tx.RollbackAsync();
                    skippedCount++;
                    job.ProcessedItems = importedCount + skippedCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Skipped {skippedCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    failedCount++;
                    if (failedSamples.Count < 10)
                    {
                        failedSamples.Add($"{externalKey}: {ex.Message}");
                    }
                    job.ProcessedItems = importedCount + skippedCount + failedCount;
                    job.Message = $"Import progress {job.ProcessedItems}/{job.TotalItems} (Imported {importedCount}, Skipped {skippedCount}, Failed {failedCount})";
                    await _db.SaveChangesAsync();
                }
            }

            job.Status = QuestionTransferJobStatus.Completed;
            var summary = $"Completed. Imported {importedCount}, Skipped {skippedCount}, Failed {failedCount}.";
            if (failedSamples.Count > 0)
            {
                summary += " Samples: " + string.Join(" | ", failedSamples);
            }
            job.Message = summary;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            job.Status = QuestionTransferJobStatus.Failed;
            job.Message = ex.Message;
            await _db.SaveChangesAsync();
        }
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

    private async Task TryAddFromMinioAsync(ZipArchive zip, string? url, string entryPath)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        await using var stream = await _minio.GetFileStreamAsync(url);
        if (stream == null) return;

        var entry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await stream.CopyToAsync(entryStream);
    }

    private async Task<(int? subjectId, int? topicId)> ResolveTaxonomyAsync(
        JsonElement qEl,
        Dictionary<string, int> subjectIdByName,
        Dictionary<string, int> topicIdByKey)
    {
        int? subjectId = null;
        if (qEl.TryGetProperty("subject", out var subjEl) && subjEl.ValueKind == JsonValueKind.Object)
        {
            var subjectName = subjEl.TryGetProperty("name", out var sn) ? sn.GetString() : null;
            if (!string.IsNullOrWhiteSpace(subjectName))
            {
                if (!subjectIdByName.TryGetValue(subjectName!, out var foundSubjectId))
                {
                    foundSubjectId = await _db.Subjects
                        .Where(s => s.Name != null && s.Name.ToLower() == subjectName!.ToLower())
                        .Select(s => s.Id)
                        .FirstOrDefaultAsync();

                    if (foundSubjectId != 0)
                    {
                        subjectIdByName[subjectName!] = foundSubjectId;
                    }
                }

                if (subjectIdByName.TryGetValue(subjectName!, out var cachedSubj))
                {
                    subjectId = cachedSubj;
                }
            }
        }

        int? topicId = null;
        if (qEl.TryGetProperty("topic", out var topicEl) && topicEl.ValueKind == JsonValueKind.Object)
        {
            var topicName = topicEl.TryGetProperty("name", out var tn) ? tn.GetString() : null;
            var gradeName = topicEl.TryGetProperty("gradeName", out var gn) ? gn.GetString() : null;

            if (!string.IsNullOrWhiteSpace(topicName))
            {
                var key = string.IsNullOrWhiteSpace(gradeName) ? topicName! : $"{gradeName}|{topicName}";
                if (!topicIdByKey.TryGetValue(key, out var foundTopicId))
                {
                    var q = _db.Topics.AsQueryable();
                    if (!string.IsNullOrWhiteSpace(gradeName))
                    {
                        q = q.Include(t => t.Grade)
                            .Where(t => t.Grade != null && t.Grade.Name != null && t.Grade.Name.ToLower() == gradeName!.ToLower());
                    }

                    foundTopicId = await q
                        .Where(t => t.Name != null && t.Name.ToLower() == topicName!.ToLower())
                        .Select(t => t.Id)
                        .FirstOrDefaultAsync();

                    if (foundTopicId != 0)
                    {
                        topicIdByKey[key] = foundTopicId;
                    }
                }

                if (topicIdByKey.TryGetValue(key, out var cachedTopic))
                {
                    topicId = cachedTopic;
                }
            }
        }

        return (subjectId, topicId);
    }

    private async Task<List<int>> ResolveSubTopicsAsync(
        JsonElement subTopicsEl,
        Dictionary<string, int> subTopicIdByKey)
    {
        var result = new List<int>();

        foreach (var stEl in subTopicsEl.EnumerateArray())
        {
            if (stEl.ValueKind != JsonValueKind.Object) continue;

            var name = stEl.TryGetProperty("name", out var n) ? n.GetString() : null;
            var topicName = stEl.TryGetProperty("topicName", out var tn) ? tn.GetString() : null;
            var gradeName = stEl.TryGetProperty("gradeName", out var gn) ? gn.GetString() : null;

            if (string.IsNullOrWhiteSpace(name)) continue;

            var key = string.IsNullOrWhiteSpace(topicName)
                ? name!
                : string.IsNullOrWhiteSpace(gradeName)
                    ? $"{topicName}|{name}"
                    : $"{gradeName}|{topicName}|{name}";

            if (!subTopicIdByKey.TryGetValue(key, out var foundId))
            {
                var q = _db.SubTopics
                    .Include(st => st.Topic)
                        .ThenInclude(t => t.Grade)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(topicName))
                {
                    q = q.Where(st => st.Topic != null && st.Topic.Name != null && st.Topic.Name.ToLower() == topicName!.ToLower());
                }

                if (!string.IsNullOrWhiteSpace(gradeName))
                {
                    q = q.Where(st => st.Topic != null && st.Topic.Grade != null && st.Topic.Grade.Name != null && st.Topic.Grade.Name.ToLower() == gradeName!.ToLower());
                }

                foundId = await q
                    .Where(st => st.Name != null && st.Name.ToLower() == name!.ToLower())
                    .Select(st => st.Id)
                    .FirstOrDefaultAsync();

                if (foundId != 0)
                {
                    subTopicIdByKey[key] = foundId;
                }
            }

            if (subTopicIdByKey.TryGetValue(key, out var cachedId) && cachedId != 0)
            {
                result.Add(cachedId);
            }
        }

        return result;
    }
}
