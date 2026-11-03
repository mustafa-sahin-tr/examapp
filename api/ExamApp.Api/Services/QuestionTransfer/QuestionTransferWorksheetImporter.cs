using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>
/// Paketteki test ve etkinlikleri hedefe yazar. Sorular önceden işlenmiştir: her madde hedefteki sorunun kimliğine bağlanır.
/// Tek bir madde bile bağlanamıyorsa kayıt hiç oluşturulmaz, eksik maddeli test yanlış puan verir.
/// Yeni kayıt kapalı gelir (<see cref="WorksheetTeacherSharing.Private"/> + <see cref="WorksheetStudentVisibility.Restricted"/>): prod'a taşınan test
/// kendiliğinden yayına çıkmaz. Atama, oturum, sonuç ve yorum taşınmaz. İkinci kez gelen kayıt güncellenmez: hedefte oturum başlamış olabilir.
/// </summary>
public sealed class QuestionTransferWorksheetImporter(AppDbContext db, IMinIoService minio, ILogger? logger = null)
{
    public const string WorksheetsFileName = "worksheets.json";
    /// <summary>Paket başına kayıt sınırı. Paket güvenilmez içeriktir: her madde sorgu, her kapak depoya yazma demektir.</summary>
    public const int MaxWorksheetsPerPackage = 2000;
    private const int MaxItemsPerWorksheet = 1000;
    private const int MaxKeyLength = 200;
    private const int MaxTextLength = 4000;
    private const int MaxSampleNameLength = 200;
    private const int MaxSamples = 10;
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Maddenin hedefteki durumu. <paramref name="BlockedReason"/> doluysa kayıt alınamaz.</summary>
    public sealed record ItemState(int? TargetQuestionId, string? BlockedReason, bool WillBeUnpublished, DateTime? LicenseEndsAt, string? InteractionType);

    private sealed record Plan(string Name, string Kind, int GradeId, int? SubjectId, int? TopicId, int? SubTopicId, List<int> TargetQuestionIds, bool MissingTaxonomy, bool HadDuplicates);

    public async Task<QuestionTransferWorksheetResultDto> ImportAsync(ZipArchive zip, JsonElement manifest, string sourceKey, int? ownerUserId, CancellationToken ct = default)
    {
        var result = new QuestionTransferWorksheetResultDto();
        if (!TryReadWorksheets(manifest, out var worksheets))
            return result;
        if (worksheets.Count > MaxWorksheetsPerPackage)
            return TooManyWorksheets(worksheets.Count);

        // Sahibi içe alan admindir; soruların sahipliğini değiştirmemek için yalnız bu aşamada ayarlanır.
        db.SetCurrentUser(ownerUserId ?? 0);
        var taxonomy = new QuestionImportTaxonomy(db);
        var strategy = db.Database.CreateExecutionStrategy();
        var uploadedImages = new Dictionary<int, string>();

        foreach (var worksheet in worksheets)
        {
            var name = ReadName(worksheet);
            var key = ReadString(worksheet, "externalKey");
            if (KeyProblem(key) is { } keyProblem)
            {
                AddBlocked(result, name, keyProblem);
                continue;
            }
            if (await db.WorksheetTransferImportMaps.AsNoTracking().AnyAsync(m => m.SourceKey == sourceKey && m.ExternalWorksheetKey == key, ct))
            {
                result.AlreadyImported++;
                continue;
            }

            // Tek kaydın hatası diğerlerini ve iş sonucunu götürmez; ayrıntı yanıta değil günlüğe yazılır.
            try
            {
                var (plan, reason) = await BuildPlanAsync(worksheet, taxonomy, itemKey => ReadImportedItemAsync(sourceKey, itemKey, ct), ct);
                if (plan is null)
                {
                    AddBlocked(result, name, reason!);
                    continue;
                }

                var imageUrl = await UploadImageAsync(zip, worksheet, uploadedImages, ct);
                await strategy.ExecuteAsync(async () =>
                {
                    // Yeniden denemede önceki denemenin satırları izleyicide kalmasın: ikinci kez yazılır ya da geri alınmış kimliğe bağlanır.
                    db.ChangeTracker.Clear();
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var entity = NewWorksheet(worksheet, plan, imageUrl);
                    db.Add(entity);
                    await db.SaveChangesAsync(ct);

                    var order = 0;
                    foreach (var questionId in plan.TargetQuestionIds)
                        db.TestQuestions.Add(new WorksheetQuestion { TestId = entity.Id, QuestionId = questionId, Order = ++order });
                    db.WorksheetTransferImportMaps.Add(new WorksheetTransferImportMap { SourceKey = sourceKey, ExternalWorksheetKey = key!, TargetWorksheetId = entity.Id });
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                });

                result.Imported++;
                result.MissingTaxonomy += plan.MissingTaxonomy ? 1 : 0;
                if (plan.HadDuplicates && result.Warnings.Count < MaxSamples)
                    result.Warnings.Add($"{name}: two items are the same question here, it was kept once");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Worksheet {WorksheetKey} from {SourceKey} could not be imported", key, sourceKey);
                AddBlocked(result, name, "it could not be saved, see the server log");
            }
        }

        return result;
    }

    /// <summary>Hiçbir şey yazmaz. <paramref name="readItem"/> pakette sorunun ne olacağını söyler; hedefteki durumu çağıran bilir.</summary>
    public async Task<QuestionTransferWorksheetResultDto> PreviewAsync(ZipArchive zip, JsonElement manifest, string sourceKey, Func<string, Task<ItemState>> readItem, CancellationToken ct = default)
    {
        var result = new QuestionTransferWorksheetResultDto();
        if (!TryReadWorksheets(manifest, out var worksheets))
            return result;
        if (worksheets.Count > MaxWorksheetsPerPackage)
            return TooManyWorksheets(worksheets.Count);

        var taxonomy = new QuestionImportTaxonomy(db);
        foreach (var worksheet in worksheets)
        {
            var name = ReadName(worksheet);
            var key = ReadString(worksheet, "externalKey");
            if (KeyProblem(key) is null
                && await db.WorksheetTransferImportMaps.AsNoTracking().AnyAsync(m => m.SourceKey == sourceKey && m.ExternalWorksheetKey == key, ct))
            {
                result.AlreadyImported++;
                continue;
            }

            var (plan, reason) = await PreviewPlanAsync(zip, worksheet, key, taxonomy, readItem, ct);
            if (plan is null)
            {
                AddBlocked(result, name, reason!);
                continue;
            }

            result.Imported++;
            result.MissingTaxonomy += plan.MissingTaxonomy ? 1 : 0;
        }

        return result;
    }

    /// <summary>İçe aktarmada hata sayılıp kaydı durduran paket kusurları önizlemede de kaydı durdurur, önizlemeyi düşürmez.</summary>
    private async Task<(Plan? Plan, string? Reason)> PreviewPlanAsync(ZipArchive zip, JsonElement worksheet, string? key, QuestionImportTaxonomy taxonomy, Func<string, Task<ItemState>> readItem, CancellationToken ct)
    {
        if (KeyProblem(key) is { } keyProblem)
            return (null, keyProblem);
        try
        {
            var (plan, reason) = await BuildPlanAsync(worksheet, taxonomy, readItem, ct);
            return plan is not null && ImageEntry(zip, worksheet) is { Length: > QuestionContentHash.MaxPackageImageBytes }
                ? (null, "its image is too large")
                : (plan, reason);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (null, "it could not be saved, the package entry is not valid");
        }
    }

    /// <summary>
    /// Hedefte var olan sorunun, kayda bağlanmayı etkileyen durumu. <paramref name="packageLicenseEndsAt"/>: eşleşen soruda
    /// içe aktarma, kaynağı bilinmeyen hedefin boş lisans bitişini paketten doldurur; önizleme aynı sonucu görmeli.
    /// </summary>
    public async Task<ItemState> ReadTargetAsync(int questionId, bool unpublishedByPackage, CancellationToken ct = default, DateTime? packageLicenseEndsAt = null)
    {
        var target = await db.Questions.AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => new { q.IsUnpublished, q.LicenseEndsAt, q.InteractionType, q.SourceType })
            .FirstOrDefaultAsync(ct);
        return target is null
            ? new ItemState(null, "a question was deleted here", false, null, null)
            : new ItemState(questionId, null, target.IsUnpublished || unpublishedByPackage,
                target.LicenseEndsAt ?? (target.SourceType == QuestionSourceType.Unknown ? packageLicenseEndsAt : null), target.InteractionType);
    }

    private async Task<ItemState> ReadImportedItemAsync(string sourceKey, string itemKey, CancellationToken ct)
    {
        var targetId = await db.Set<QuestionTransferImportMap>().AsNoTracking()
            .Where(m => m.SourceKey == sourceKey && m.ExternalQuestionKey == itemKey)
            .Select(m => (int?)m.TargetQuestionId)
            .FirstOrDefaultAsync(ct);
        return targetId is null
            ? new ItemState(null, "a question could not be imported", false, null, null)
            : await ReadTargetAsync(targetId.Value, unpublishedByPackage: false, ct);
    }

    private async Task<(Plan? Plan, string? Reason)> BuildPlanAsync(JsonElement worksheet, QuestionImportTaxonomy taxonomy, Func<string, Task<ItemState>> readItem, CancellationToken ct)
    {
        var kind = ReadKind(worksheet);
        if (kind is null)
            return (null, "the kind is not known");

        var gradeId = worksheet.TryGetProperty("grade", out var gradeEl) && gradeEl.ValueKind == JsonValueKind.Object
            ? await taxonomy.ResolveGradeAsync(gradeEl, ct)
            : null;
        if (gradeId is null)
            return (null, "its grade is not here, move the catalog first");

        var items = worksheet.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array
            ? itemsEl.EnumerateArray().ToList()
            : [];
        if (items.Count == 0)
            return (null, "it has no questions");
        if (items.Count > MaxItemsPerWorksheet)
            return (null, "it has too many questions");
        if (new[] { "name", "description", "subtitle", "badgeText" }.Any(field => ReadString(worksheet, field)?.Length > MaxTextLength))
            return (null, "a text field is too long");

        var now = DateTime.UtcNow;
        var targets = new List<int>();
        var seenTargets = new HashSet<int>();
        var hadDuplicates = false;
        foreach (var item in items.OrderBy(i => i.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : int.MaxValue))
        {
            var itemKey = ReadString(item, "questionKey");
            if (string.IsNullOrWhiteSpace(itemKey))
                return (null, "an item has no question");

            var state = await readItem(itemKey);
            if (state.BlockedReason is not null)
                return (null, state.BlockedReason);
            // Normal test oluşturmadaki kural (#690): yayından kalkan ve lisansı biten soru teste girmez.
            if (state.WillBeUnpublished)
                return (null, "a question is unpublished");
            if (state.LicenseEndsAt <= now)
                return (null, "the licence of a question has ended");
            // Açık uçlu soru puanlanamaz, yalnız etkinlikte olur.
            if (kind == WorksheetKind.Test && string.Equals(state.InteractionType, OpenTextGrader.Type, StringComparison.OrdinalIgnoreCase))
                return (null, "a test cannot have an open text question");

            if (state.TargetQuestionId is not int targetId)
                continue;
            if (seenTargets.Add(targetId))
                targets.Add(targetId);
            else
                hadDuplicates = true;
        }

        var missing = false;
        var subjectId = await ResolveOptionalAsync(worksheet, "subject", el => taxonomy.ResolveSubjectAsync(el, ct), v => missing |= v);
        var topicId = await ResolveOptionalAsync(worksheet, "topic", el => taxonomy.ResolveTopicAsync(el, subjectId, ct), v => missing |= v);
        var subTopicId = await ResolveOptionalAsync(worksheet, "subTopic", el => taxonomy.ResolveSubTopicAsync(el, ct), v => missing |= v);

        return (new Plan(ReadName(worksheet), kind.Value.ToString(), gradeId.Value, subjectId, topicId, subTopicId, targets, missing, hadDuplicates), null);
    }

    private static async Task<int?> ResolveOptionalAsync(JsonElement worksheet, string name, Func<JsonElement, Task<int?>> resolve, Action<bool> reportMissing)
    {
        if (!worksheet.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;
        var id = await resolve(el);
        reportMissing(id is null);
        return id;
    }

    private static Worksheet NewWorksheet(JsonElement worksheet, Plan plan, string? imageUrl)
    {
        var kind = Enum.Parse<WorksheetKind>(plan.Kind);
        // Elle oluşturmadaki kurallar (#396, #573): süre 0-24 saat; etkinlikte süre ve çalışma testi yoktur.
        var isActivity = kind == WorksheetKind.Activity;
        var duration = worksheet.TryGetProperty("maxDurationSeconds", out var durationEl) && durationEl.ValueKind == JsonValueKind.Number && durationEl.TryGetInt32(out var seconds)
            ? Math.Clamp(seconds, 0, WorksheetDurationLimits.MaxSeconds)
            : 0;
        return new Worksheet
        {
            Name = plan.Name,
            Description = ReadString(worksheet, "description") ?? string.Empty,
            Subtitle = ReadString(worksheet, "subtitle"),
            BadgeText = ReadString(worksheet, "badgeText"),
            ImageUrl = imageUrl,
            Kind = kind,
            GradeId = plan.GradeId,
            SubjectId = plan.SubjectId,
            TopicId = plan.TopicId,
            SubTopicId = plan.SubTopicId,
            MaxDurationSeconds = isActivity ? 0 : duration,
            IsPracticeTest = !isActivity && worksheet.TryGetProperty("isPracticeTest", out var practice) && practice.ValueKind == JsonValueKind.True,
            CommentsEnabled = !worksheet.TryGetProperty("commentsEnabled", out var comments) || comments.ValueKind != JsonValueKind.False,
            TeacherSharing = WorksheetTeacherSharing.Private,
            StudentVisibility = WorksheetStudentVisibility.Restricted,
        };
    }

    private static ZipArchiveEntry? ImageEntry(ZipArchive zip, JsonElement worksheet) =>
        worksheet.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var worksheetId)
            ? zip.GetEntry($"worksheets/{worksheetId}.jpg")
            : null;

    /// <summary>Kapak, kapakların bucket'ına yazılır: başka yerdeki nesne imzalanmaz, görünmez. Aynı girdi iki kez yüklenmez.</summary>
    private async Task<string?> UploadImageAsync(ZipArchive zip, JsonElement worksheet, Dictionary<int, string> uploaded, CancellationToken ct)
    {
        if (ImageEntry(zip, worksheet) is not { } entry)
            return null;
        var id = worksheet.GetProperty("id").GetInt32();
        if (uploaded.TryGetValue(id, out var existing))
            return existing;

        await using var image = new MemoryStream(await QuestionContentHash.ReadPackageEntryAsync(entry, QuestionContentHash.MaxPackageImageBytes, ct));
        var url = await minio.UploadFileAsync(image, $"{Guid.NewGuid()}.jpg", MinioBucketPolicies.WorksheetsBucket, "image/jpeg");
        uploaded[id] = url;
        return url;
    }

    private static string? KeyProblem(string? key) =>
        string.IsNullOrWhiteSpace(key) ? "the package entry has no key"
        : key.Length > MaxKeyLength ? "the package entry has an invalid key"
        : null;

    private static QuestionTransferWorksheetResultDto TooManyWorksheets(int count) => new()
    {
        Blocked = count,
        BlockedSamples = [$"the package has {count} tests and activities, the limit is {MaxWorksheetsPerPackage}"],
    };

    private static void AddBlocked(QuestionTransferWorksheetResultDto result, string name, string reason)
    {
        result.Blocked++;
        if (result.BlockedSamples.Count < MaxSamples)
            result.BlockedSamples.Add($"{(name.Length > MaxSampleNameLength ? name[..MaxSampleNameLength] : name)}: {reason}");
    }

    private static bool TryReadWorksheets(JsonElement manifest, out List<JsonElement> worksheets)
    {
        worksheets = manifest.TryGetProperty("worksheets", out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray().Where(w => w.ValueKind == JsonValueKind.Object).ToList()
            : [];
        return worksheets.Count > 0;
    }

    private static WorksheetKind? ReadKind(JsonElement worksheet) =>
        ReadString(worksheet, "kind") is { } kind && Enum.TryParse<WorksheetKind>(kind, ignoreCase: true, out var value) && Enum.IsDefined(value) && !int.TryParse(kind, out _)
            ? value
            : null;

    private static string ReadName(JsonElement worksheet) => ReadString(worksheet, "name") ?? ReadString(worksheet, "externalKey") ?? "?";

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
