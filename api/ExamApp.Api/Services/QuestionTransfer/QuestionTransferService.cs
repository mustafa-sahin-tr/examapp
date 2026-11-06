using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>
/// issue #289 (security D2): aktarım işini başlatan kullanıcı. <see cref="UserId"/> işin <c>CreateUserId</c>'sine yazılır;
/// <see cref="IsAdmin"/> Hangfire iş argümanı olarak saklanır (sunucu tarafı). Runner iş başında sahibin öğretmen
/// onayını yeniden kontrol eder — admin sahipli iş ApprovedTeacher policy'sindeki admin muafiyetiyle aynı şekilde muaftır.
/// </summary>
public sealed record QuestionTransferOwner(int UserId, bool IsAdmin);

public interface IQuestionTransferService
{
    Task<QuestionTransferJobDto> StartExportAsync(StartQuestionExportDto request, QuestionTransferOwner owner, CancellationToken ct);
    Task<QuestionTransferJobDto> StartImportAsync(string sourceKey, string uploadedFileUrl, QuestionTransferOwner owner, QuestionImportWorksheetOptions options, CancellationToken ct);
    Task<QuestionTransferJobDto?> GetJobAsync(Guid id, CancellationToken ct);
    Task<List<QuestionTransferJobDto>> ListJobsAsync(int take, CancellationToken ct);
    Task<Stream?> GetJobFileStreamAsync(Guid jobId, CancellationToken ct);

    Task<List<string>> ListSourceKeysAsync(CancellationToken ct);

    Task<List<QuestionTransferExportBundleDto>> ListExportBundlesAsync(string sourceKey, CancellationToken ct);

    /// <summary>
    /// Paketteki sorulara kaynak, lisans ve yayın durumunun bugünkü değerini yazar. Dosyalar soru ilk çıkarıldığında üretilir:
    /// sonradan yayından kaldırılan soru paketle yayında görünmesin.
    /// </summary>
    Task<List<JsonNode>> WithCurrentRightsAsync(IReadOnlyList<JsonElement> questions, CancellationToken ct);

    /// <summary>Bu ortamın kalıcı kimliği: birleştirilen paketin manifestine yazılır.</summary>
    Task<string> GetOriginKeyAsync(CancellationToken ct);

    /// <summary>Dışa aktarma için seçilebilecek test ve etkinlikler, en yeniden eskiye.</summary>
    Task<List<QuestionTransferWorksheetListItemDto>> ListWorksheetsAsync(WorksheetKind? kind, int? gradeId, int? subjectId, string? search, int take, CancellationToken ct);

    /// <summary>İndirilecek paketi yazar.</summary>
    Task WritePackageAsync(string sourceKey, Stream output, CancellationToken ct);
    Task<Stream?> GetExportBundleStreamAsync(string sourceKey, int bundleNo, CancellationToken ct);
    Task<Stream?> GetExportBundleMapStreamAsync(string sourceKey, int bundleNo, CancellationToken ct);
    Task<Stream?> GetExportSourceIndexStreamAsync(string sourceKey, CancellationToken ct);

    Task<QuestionTransferImportPreviewDto> PreviewImportAsync(IFormFile file, string? sourceOverride, CancellationToken ct);
}

public class QuestionTransferService : IQuestionTransferService
{
    private readonly AppDbContext _db;
    private readonly IMinIoService _minio;
    private readonly IBackgroundJobClient _jobs;
    private readonly string _minioBucket;

    public QuestionTransferService(
        AppDbContext db,
        IMinIoService minio,
        IBackgroundJobClient jobs,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _db = db;
        _minio = minio;
        _jobs = jobs;
        _minioBucket = configuration.GetSection("MinioConfig")["BucketName"] ?? "exam-questions";
    }

    public async Task<QuestionTransferJobDto> StartExportAsync(StartQuestionExportDto request, QuestionTransferOwner owner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        // QuestionIds may be empty to indicate "export all".

        var hasWorksheets = request.WorksheetIds is { Count: > 0 };

        // Ad verilmezse her dışa aktarım kendi kaynağıdır: paket kaynağın bütün dosyalarını birleştirir, başka iş karışmasın.
        // Test ve etkinlik seçimi kaynakta tek dosyadır: aynı adla ikinci aktarım öncekini ezeceği için ad her zaman üretilir.
        request.SourceKey = hasWorksheets || string.IsNullOrWhiteSpace(request.SourceKey)
            ? $"transfer-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"
            : request.SourceKey.Trim();

        // Kapsam boş sonuç verirse işe başlanmaz: boş kimlik listesi "hepsi" demektir, yanlışlıkla tüm sorular çıkar.
        if (hasWorksheets && request.WorksheetIds.Distinct().Count() > QuestionTransferWorksheetImporter.MaxWorksheetsPerPackage)
            return await SaveUnstartedExportAsync(request, owner, request.SourceKey, QuestionTransferJobStatus.Failed, $"Select at most {QuestionTransferWorksheetImporter.MaxWorksheetsPerPackage} tests and activities.", ct);

        if (hasWorksheets || request.HasScope)
        {
            var scopeIds = hasWorksheets ? await ResolveWorksheetQuestionsAsync(request.WorksheetIds, ct) : await ResolveScopeAsync(request, ct);
            request.QuestionIds = scopeIds;
            var scopeSourceKey = request.SourceKey;
            if (scopeIds.Count == 0)
                return await SaveUnstartedExportAsync(request, owner, scopeSourceKey, QuestionTransferJobStatus.Completed, "No questions match the selected scope.", ct);

            // Paket kaynak adının bütün dosyalarını birleştirir: kapsam dışı soru varsa indirilen paket seçilenden fazlasını, çözümleriyle taşır.
            var hasOutsideQuestions = await _db.Set<QuestionTransferExportMap>()
                .AnyAsync(m => m.SourceKey == scopeSourceKey && !scopeIds.Contains(m.QuestionId), ct);
            if (hasOutsideQuestions)
                return await SaveUnstartedExportAsync(request, owner, scopeSourceKey, QuestionTransferJobStatus.Failed, "This source name already holds questions outside the selected scope. Use a new source name.", ct);
        }

        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(),
            Kind = QuestionTransferJobKind.Export,
            Status = QuestionTransferJobStatus.Queued,
            SourceKey = request.SourceKey,
            RequestJson = JsonSerializer.Serialize(request),
            TotalItems = request.QuestionIds?.Count ?? 0,
            ProcessedItems = 0,
            Message = "Queued"
        };

        // issue #289 (security D2): CreateUserId = işin sahibi (runner onu guard'a sorar).
        _db.SetCurrentUser(owner.UserId);
        _db.Add(job);
        await _db.SaveChangesAsync(ct);

        var ownerIsAdmin = owner.IsAdmin;
        _jobs.Enqueue<QuestionTransferJobRunner>(r => r.RunExportAsync(job.Id, ownerIsAdmin));

        return ToDto(job);
    }

    /// <summary>
    /// Kaldırılmış madde (soft-delete) pakete girmez: bundan sonra başlayan oturum da görmez. Silinen kaydın maddeleri silinmez:
    /// kayıt filtrelenmezse soruları çözümleriyle, kayıt olmadan pakete girer.
    /// </summary>
    private Task<List<int>> ResolveWorksheetQuestionsAsync(List<int> worksheetIds, CancellationToken ct) =>
        _db.TestQuestions.AsNoTracking()
            .Where(i => worksheetIds.Contains(i.TestId) && _db.Worksheets.Any(w => w.Id == i.TestId))
            .Select(i => i.QuestionId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);

    public async Task<List<QuestionTransferWorksheetListItemDto>> ListWorksheetsAsync(WorksheetKind? kind, int? gradeId, int? subjectId, string? search, int take, CancellationToken ct)
    {
        var query = _db.Set<Worksheet>().AsNoTracking();
        if (kind is not null)
            query = query.Where(w => w.Kind == kind);
        if (gradeId is not null)
            query = query.Where(w => w.GradeId == gradeId);
        if (subjectId is not null)
            query = query.Where(w => w.SubjectId == subjectId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            var lowered = text.ToLower();
            query = query.Where(w => w.Name.Contains(text) || w.Name.ToLower().Contains(lowered));
        }

        return await query
            .OrderByDescending(w => w.Id)
            .Take(Math.Clamp(take, 1, 200))
            .Select(w => new QuestionTransferWorksheetListItemDto
            {
                Id = w.Id,
                Name = w.Name,
                Kind = w.Kind.ToString(),
                GradeName = w.Grade.Name,
                SubjectName = w.Subject.Name,
                QuestionCount = w.WorksheetQuestions.Count,
            })
            .ToListAsync(ct);
    }

    internal static string WorksheetsObjectName(string sourceKey) => $"question-transfer/exports/{sourceKey}/worksheets.zip";

    /// <summary>
    /// İçe aktarılabilir tek zip: kök manifest ve dosyalar. Bu kaynağın bütün soru dosyalarını ve varsa test / etkinlik dosyasını birleştirir.
    /// ZipArchive kapanırken zaman uyumlu yazar: <paramref name="output"/> yanıt gövdesi değil, geçici dosya olmalı.
    /// </summary>
    public async Task WritePackageAsync(string sourceKey, Stream output, CancellationToken ct)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var questions = new List<JsonElement>();
        var seenExternalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in (await ListExportBundlesAsync(sourceKey, ct)).OrderBy(b => b.BundleNo))
        {
            var bundleStream = await GetExportBundleStreamAsync(sourceKey, bundle.BundleNo, ct);
            if (bundleStream is null)
                continue;

            await CopyZipAsync(bundleStream, zip, seenEntries, async manifestEntry =>
            {
                await using var manifestStream = manifestEntry.Open();
                using var doc = await JsonDocument.ParseAsync(manifestStream, cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("questions", out var items) || items.ValueKind != JsonValueKind.Array)
                    return;
                foreach (var question in items.EnumerateArray())
                {
                    var key = question.TryGetProperty("externalKey", out var externalKey) ? externalKey.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(key) && seenExternalKeys.Add(key))
                        questions.Add(question.Clone());
                }
            }, "manifest.json", ct);
        }

        var worksheets = new List<JsonElement>();
        var worksheetsUrl = $"/img/{_minioBucket}/{WorksheetsObjectName(sourceKey)}";
        if (await _minio.FileExistsAsync(worksheetsUrl, ct))
        {
            // Okunamayan dosya sessizce atlanırsa testsiz paket inerdi ve önizleme de bunu göstermezdi.
            var worksheetsStream = await _minio.GetFileStreamAsync(worksheetsUrl)
                ?? throw new InvalidOperationException("The tests and activities of this export could not be read. Try again.");
            await CopyZipAsync(worksheetsStream, zip, seenEntries, async entry =>
            {
                await using var stream = entry.Open();
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                worksheets.AddRange(doc.RootElement.EnumerateArray().Select(w => w.Clone()));
            }, QuestionTransferWorksheetImporter.WorksheetsFileName, ct);
        }

        var manifest = new
        {
            version = 4,
            sourceKey,
            originKey = await GetOriginKeyAsync(ct),
            bundleNo = 0,
            exportedAtUtc = DateTime.UtcNow,
            questions = await WithCurrentRightsAsync(questions, ct),
            worksheets,
        };

        var manifestOut = zip.CreateEntry("manifest.json", CompressionLevel.Fastest);
        await using var manifestOutStream = manifestOut.Open();
        await JsonSerializer.SerializeAsync(manifestOutStream, manifest, cancellationToken: ct);
    }

    /// <summary>Kaynak zip'i geçici dosyadan okur; <paramref name="dataEntry"/> dışındaki girdileri kopyalar, onu <paramref name="readData"/>'ya verir.</summary>
    private static async Task CopyZipAsync(Stream source, ZipArchive target, HashSet<string> seenEntries, Func<ZipArchiveEntry, Task> readData, string dataEntry, CancellationToken ct)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"question-transfer-part-{Guid.NewGuid():N}.zip");
        try
        {
            await using (source)
            await using (var write = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(write, ct);

            await using var read = new FileStream(tmpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(read, ZipArchiveMode.Read, leaveOpen: false);
            if (zip.GetEntry(dataEntry) is { } data)
                await readData(data);

            foreach (var entry in zip.Entries)
            {
                // Dizin girdisi, veri dosyası ve zaten eklenen ortak dosya (paylaşılan paragraf) atlanır.
                if (string.IsNullOrWhiteSpace(entry.Name)
                    || entry.FullName.Equals(dataEntry, StringComparison.OrdinalIgnoreCase)
                    || !seenEntries.Add(entry.FullName))
                    continue;

                var outEntry = target.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                await using var outStream = outEntry.Open();
                await using var inStream = entry.Open();
                await inStream.CopyToAsync(outStream, ct);
            }
        }
        finally
        {
            try
            {
                File.Delete(tmpPath);
            }
            catch (IOException)
            {
                // Temp temizliği indirmeyi düşürmez.
            }
        }
    }

    private async Task<List<int>> ResolveScopeAsync(StartQuestionExportDto request, CancellationToken ct)
    {
        var query = _db.Questions.AsNoTracking();
        if (request.SubjectId is int subjectId)
            query = query.Where(q => q.SubjectId == subjectId);
        if (request.TopicId is int topicId)
            query = query.Where(q => q.TopicId == topicId);
        if (request.GradeId is int gradeId)
            query = query.Where(q => (q.TopicId != null && q.Topic.GradeId == gradeId)
                || q.WorksheetQuestions.Any(wq => wq.Worksheet.GradeId == gradeId));
        if (request.BookId is int bookId)
            query = query.Where(q => _db.QuestionSources.Any(s => s.QuestionId == q.Id && s.BookId == bookId));
        return await query.OrderBy(q => q.Id).Select(q => q.Id).ToListAsync(ct);
    }

    public async Task<QuestionTransferJobDto> StartImportAsync(string sourceKey, string uploadedFileUrl, QuestionTransferOwner owner, QuestionImportWorksheetOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);
        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(),
            Kind = QuestionTransferJobKind.Import,
            Status = QuestionTransferJobStatus.Queued,
            SourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim(),
            FileUrl = uploadedFileUrl,
            RequestJson = JsonSerializer.Serialize(options),
            Message = "Queued"
        };

        _db.SetCurrentUser(owner.UserId); // issue #289 (security D2)
        _db.Add(job);
        await _db.SaveChangesAsync(ct);

        var ownerIsAdmin = owner.IsAdmin;
        _jobs.Enqueue<QuestionTransferJobRunner>(r => r.RunImportAsync(job.Id, ownerIsAdmin));

        return ToDto(job);
    }

    public async Task<QuestionTransferJobDto?> GetJobAsync(Guid id, CancellationToken ct)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstOrDefaultAsync(j => j.Id == id, ct);
        return job == null ? null : ToDto(job);
    }

    public async Task<List<QuestionTransferJobDto>> ListJobsAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        var jobs = await _db.Set<QuestionTransferJob>()
            .OrderByDescending(j => j.CreateTime)
            .Take(take)
            .ToListAsync(ct);

        return jobs.Select(ToDto).ToList();
    }

    public async Task<List<string>> ListSourceKeysAsync(CancellationToken ct)
    {
        // Sources may exist via bundles or jobs; merge both.
        var bundleKeys = await _db.Set<QuestionTransferExportBundle>()
            .Select(b => b.SourceKey)
            .Distinct()
            .ToListAsync(ct);

        var jobKeys = await _db.Set<QuestionTransferJob>()
            .Select(j => j.SourceKey)
            .Distinct()
            .ToListAsync(ct);

        return bundleKeys
            .Concat(jobKeys)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k)
            .Take(200)
            .ToList();
    }

    public async Task<Stream?> GetJobFileStreamAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || string.IsNullOrWhiteSpace(job.FileUrl))
        {
            return null;
        }

        return await _minio.GetFileStreamAsync(job.FileUrl);
    }

    public async Task<List<QuestionTransferExportBundleDto>> ListExportBundlesAsync(string sourceKey, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();
        var bundles = await _db.Set<QuestionTransferExportBundle>()
            .Where(b => b.SourceKey == sourceKey)
            .OrderBy(b => b.BundleNo)
            .ToListAsync(ct);

        return bundles.Select(b => new QuestionTransferExportBundleDto
        {
            SourceKey = b.SourceKey,
            BundleNo = b.BundleNo,
            QuestionCount = b.QuestionCount,
            FileUrl = b.FileUrl,
        }).ToList();
    }

    public async Task<Stream?> GetExportBundleStreamAsync(string sourceKey, int bundleNo, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();
        if (bundleNo <= 0) return null;

        var bundle = await _db.Set<QuestionTransferExportBundle>()
            .FirstOrDefaultAsync(b => b.SourceKey == sourceKey && b.BundleNo == bundleNo, ct);

        if (bundle == null || string.IsNullOrWhiteSpace(bundle.FileUrl))
        {
            return null;
        }

        return await _minio.GetFileStreamAsync(bundle.FileUrl);
    }

    public async Task<Stream?> GetExportBundleMapStreamAsync(string sourceKey, int bundleNo, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();
        if (bundleNo <= 0) return null;

        var objectName = $"question-transfer/exports/{sourceKey}/bundle-{bundleNo:D4}.map.json";
        var url = $"/img/{_minioBucket}/{objectName}";
        return await _minio.GetFileStreamAsync(url);
    }

    public async Task<Stream?> GetExportSourceIndexStreamAsync(string sourceKey, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();

        var objectName = $"question-transfer/exports/{sourceKey}/index.json";
        var url = $"/img/{_minioBucket}/{objectName}";
        return await _minio.GetFileStreamAsync(url);
    }

    /// <summary>Henüz hedefte olmayan soru: durumu paketten okunur, kimliği içe alınınca belli olur.</summary>
    private static QuestionTransferWorksheetImporter.ItemState NewQuestionState(JsonElement question)
    {
        var licenseEndsAt = ReadPackageLicenseEndsAt(question);
        var type = question.TryGetProperty("interactionType", out var interactionType) && interactionType.ValueKind == JsonValueKind.String ? interactionType.GetString() : null;
        return new QuestionTransferWorksheetImporter.ItemState(null, null, QuestionTransferJobRunner.IsUnpublishedInPackage(question), licenseEndsAt, type);
    }

    private static DateTime? ReadPackageLicenseEndsAt(JsonElement question) =>
        question.TryGetProperty("licenseEndsAt", out var license) && license.ValueKind == JsonValueKind.String && license.TryGetDateTime(out var ends) ? ends : null;

    private async Task<bool> WouldUnpublishAsync(JsonElement question, int targetQuestionId, CancellationToken ct) =>
        QuestionTransferJobRunner.IsUnpublishedInPackage(question)
        && await _db.Questions.AsNoTracking().AnyAsync(q => q.Id == targetQuestionId && !q.IsUnpublished, ct);

    public Task<string> GetOriginKeyAsync(CancellationToken ct) => QuestionTransferOriginStore.GetOrCreateAsync(_db, ct);

    private async Task<QuestionTransferJobDto> SaveUnstartedExportAsync(StartQuestionExportDto request, QuestionTransferOwner owner, string sourceKey,
        QuestionTransferJobStatus status, string message, CancellationToken ct)
    {
        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(),
            Kind = QuestionTransferJobKind.Export,
            Status = status,
            SourceKey = sourceKey,
            RequestJson = JsonSerializer.Serialize(request),
            Message = message,
        };
        _db.SetCurrentUser(owner.UserId);
        _db.Add(job);
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    public async Task<List<JsonNode>> WithCurrentRightsAsync(IReadOnlyList<JsonElement> questions, CancellationToken ct)
    {
        var ids = questions
            .Where(q => q.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
            .Select(q => q.GetProperty("id").GetInt32())
            .ToList();
        var rights = await _db.Questions.AsNoTracking()
            .Where(q => ids.Contains(q.Id))
            .Select(q => new { q.Id, q.SourceType, q.RightsHolder, q.LicenseReference, q.LicenseEndsAt, q.IsUnpublished })
            .ToDictionaryAsync(q => q.Id, ct);

        var result = new List<JsonNode>(questions.Count);
        foreach (var question in questions)
        {
            var node = JsonNode.Parse(question.GetRawText())!;
            if (question.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && rights.TryGetValue(id.GetInt32(), out var current))
            {
                node["sourceType"] = current.SourceType.ToString();
                node["rightsHolder"] = current.RightsHolder;
                node["licenseReference"] = current.LicenseReference;
                node["licenseEndsAt"] = JsonSerializer.SerializeToNode(current.LicenseEndsAt);
                node["isUnpublished"] = current.IsUnpublished;
            }
            result.Add(node);
        }
        return result;
    }

    public async Task<QuestionTransferImportPreviewDto> PreviewImportAsync(IFormFile file, string? sourceOverride, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is required.");
        }

        await using var input = file.OpenReadStream();
        using var zip = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("manifest.json not found in zip.");

        using var doc = JsonDocument.Parse(await QuestionContentHash.ReadPackageEntryAsync(manifestEntry, QuestionContentHash.MaxPackageManifestBytes, ct));

        var sourceKeyFromManifest = QuestionContentHash.ReadPackageSourceKey(doc.RootElement) ?? string.Empty;

        var sourceKey = !string.IsNullOrWhiteSpace(sourceOverride)
            ? sourceOverride.Trim()
            : (string.IsNullOrWhiteSpace(sourceKeyFromManifest) ? "default" : sourceKeyFromManifest.Trim());

        var questions = doc.RootElement.GetProperty("questions").EnumerateArray().ToList();
        var questionCount = questions.Count;

        // Görseller zipten okunup hash'lenir; hedefte hash'i eksik soru varsa arka planda tamamlanır ve sayısı gösterilir.
        var unhashed = await _db.Questions.AsNoTracking().CountAsync(q => q.ContentFingerprint == null, ct);
        if (unhashed > 0)
            _jobs.Enqueue<QuestionContentHashService>(s => s.RunAsync());

        var taxonomy = new QuestionImportTaxonomy(_db);
        var newCount = 0;
        var matchedCount = 0;
        var conflictCount = 0;
        var missingTaxonomyCount = 0;
        var missingImageCount = 0;
        var unpublishCount = 0;
        var worksheetImporter = new QuestionTransferWorksheetImporter(_db, _minio);
        var itemStates = new Dictionary<string, Func<Task<QuestionTransferWorksheetImporter.ItemState>>>(StringComparer.Ordinal);
        // Aynı hedefe düşen iki paket sorusundan biri kaldırılmışsa içe aktarma hedefi kaldırır: ikisini de kullanan kayıt durur.
        var unpublishedTargets = new HashSet<int>();
        var unpublishedHashes = new HashSet<string>(StringComparer.Ordinal);
        var alreadyImportedInFile = 0;
        var conflictSamples = new List<string>();
        var seenInFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            var key = question.TryGetProperty("externalKey", out var keyEl) ? keyEl.GetString() ?? string.Empty : string.Empty;
            var alreadyImportedAs = await _db.Set<QuestionTransferImportMap>()
                .Where(m => m.SourceKey == sourceKey && m.ExternalQuestionKey == key)
                .Select(m => (int?)m.TargetQuestionId)
                .FirstOrDefaultAsync(ct);
            if (alreadyImportedAs is not null)
            {
                alreadyImportedInFile++;
                if (await WouldUnpublishAsync(question, alreadyImportedAs.Value, ct))
                    unpublishCount++;
                var alreadyTargetId = alreadyImportedAs.Value;
                if (QuestionTransferJobRunner.IsUnpublishedInPackage(question))
                    unpublishedTargets.Add(alreadyTargetId);
                itemStates[key] = () => worksheetImporter.ReadTargetAsync(alreadyTargetId, unpublishedTargets.Contains(alreadyTargetId), ct);
                continue;
            }

            byte[]? image = null;
            if (question.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                && zip.GetEntry($"questions/{idEl.GetInt32()}/question.jpg") is { } imageEntry)
            {
                image = await QuestionContentHash.ReadPackageEntryAsync(imageEntry, QuestionContentHash.MaxPackageImageBytes, ct);
            }

            if (QuestionContentHash.IsImageMissing(question, image))
            {
                missingImageCount++;
                itemStates[key] = () => Task.FromResult(new QuestionTransferWorksheetImporter.ItemState(null, "a question could not be imported", false, null, null));
                continue;
            }

            var hashes = QuestionContentHash.ForManifest(question, image);
            var match = await QuestionContentHash.FindMatchAsync(_db, hashes, ct);
            // Aynı dosyada ikinci kez geçen içerik: import sırasında ilki yazılmış olacağı için onunla karşılaştırılır.
            if (match.Kind == QuestionMatchKind.New && hashes.ContentHash is not null)
            {
                if (seenInFile.TryGetValue(hashes.ContentHash, out var fingerprint))
                    match = new QuestionMatch(fingerprint == hashes.Fingerprint ? QuestionMatchKind.Exact : QuestionMatchKind.Conflict, null);
                else
                    seenInFile[hashes.ContentHash] = hashes.Fingerprint;
            }

            var contentHash = hashes.ContentHash;
            if (match.Kind != QuestionMatchKind.Conflict && contentHash is not null && QuestionTransferJobRunner.IsUnpublishedInPackage(question))
                unpublishedHashes.Add(contentHash);
            bool UnpublishedWithSameContent() => contentHash is not null && unpublishedHashes.Contains(contentHash);
            QuestionTransferWorksheetImporter.ItemState NewState()
            {
                var state = NewQuestionState(question);
                return state with { WillBeUnpublished = state.WillBeUnpublished || UnpublishedWithSameContent() };
            }

            switch (match.Kind)
            {
                case QuestionMatchKind.New:
                    newCount++;
                    itemStates[key] = () => Task.FromResult(NewState());
                    break;
                case QuestionMatchKind.Exact:
                    matchedCount++;
                    if (match.TargetQuestionId is int targetId)
                    {
                        if (await WouldUnpublishAsync(question, targetId, ct))
                            unpublishCount++;
                        var packageLicenseEndsAt = ReadPackageLicenseEndsAt(question);
                        itemStates[key] = () => worksheetImporter.ReadTargetAsync(targetId, UnpublishedWithSameContent() || unpublishedTargets.Contains(targetId), ct, packageLicenseEndsAt);
                    }
                    else
                    {
                        // Aynı dosyada önce gelen yeni soruyla aynı içerik: henüz hedefte yok.
                        itemStates[key] = () => Task.FromResult(NewState());
                    }
                    break;
                default:
                    conflictCount++;
                    if (conflictSamples.Count < 10)
                        conflictSamples.Add(key);
                    itemStates[key] = () => Task.FromResult(new QuestionTransferWorksheetImporter.ItemState(null, "a question conflicts with one here", false, null, null));
                    continue;
            }

            if ((await taxonomy.ResolveAsync(question, ct)).Missing)
                missingTaxonomyCount++;
        }

        var externalKeys = questions
            .Select(q => q.TryGetProperty("externalKey", out var ek) ? (ek.GetString() ?? string.Empty) : string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var alreadyImported = 0;
        const int chunkSize = 500;
        for (var i = 0; i < externalKeys.Count; i += chunkSize)
        {
            var chunk = externalKeys.Skip(i).Take(chunkSize).ToList();
            alreadyImported += await _db.Set<QuestionTransferImportMap>()
                .CountAsync(m => m.SourceKey == sourceKey && chunk.Contains(m.ExternalQuestionKey), ct);
        }

        var worksheetPreview = await worksheetImporter.PreviewAsync(zip, doc.RootElement, sourceKey,
            itemKey => itemStates.TryGetValue(itemKey, out var state)
                ? state()
                : Task.FromResult(new QuestionTransferWorksheetImporter.ItemState(null, "a question is not in the package", false, null, null)),
            ct);

        return new QuestionTransferImportPreviewDto
        {
            Worksheets = worksheetPreview,
            SourceKey = sourceKey,
            QuestionCount = questionCount,
            AlreadyImportedCount = alreadyImported,
            NewCount = newCount,
            MatchedCount = matchedCount,
            ConflictCount = conflictCount,
            MissingTaxonomyCount = missingTaxonomyCount,
            MissingImageCount = missingImageCount,
            UnpublishCount = unpublishCount,
            TargetUnhashedCount = unhashed,
            ConflictSamples = conflictSamples,
        };
    }

    private static QuestionTransferJobDto ToDto(QuestionTransferJob job)
    {
        return new QuestionTransferJobDto
        {
            Id = job.Id,
            Kind = job.Kind.ToString(),
            Status = job.Status.ToString(),
            SourceKey = job.SourceKey,
            TotalItems = job.TotalItems,
            ProcessedItems = job.ProcessedItems,
            FileUrl = job.FileUrl,
            Message = job.Message,
            Result = string.IsNullOrWhiteSpace(job.ResultJson)
                ? null
                : JsonSerializer.Deserialize<QuestionTransferImportResultDto>(job.ResultJson),
        };
    }

    // Execution logic lives in QuestionTransferJobRunner (Hangfire job)
}
