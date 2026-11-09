using System.Security.Cryptography;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Catalog;

public sealed class CatalogPackageOptions
{
    public const string SectionName = "CatalogPackage";

    /// <summary>Açılışta paketi yükle. Entegrasyon testleri kapatır (boş katalogla başlarlar).</summary>
    public bool AutoImport { get; set; } = true;

    /// <summary>Boşsa API çıktısındaki <c>Data/Packages/maarif-catalog.json</c> (csproj deploy/data/meb'den bağlar).</summary>
    public string? Path { get; set; }

    public string ResolvePath() =>
        string.IsNullOrWhiteSpace(Path)
            ? System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "Packages", "maarif-catalog.json")
            : Path;
}

public enum CatalogPackageImportStatus
{
    Missing,
    Unchanged,
    Imported,
    Failed,
}

public sealed record CatalogPackageImportOutcome(CatalogPackageImportStatus Status, CatalogImportResultDto? Result = null);

/// <summary>
/// MEB katalog paketini (deploy/data/meb/maarif-catalog.json) her ortamda elle yükleme gerekmeden uygular: açılışta dosyanın
/// SHA-256'sı <see cref="AppliedDataPackage"/> ile karşılaştırılır, yalnız paket değiştiyse <see cref="ICatalogTransferService"/>
/// ile içe aktarılır. İçe aktarma koda göre ekler / günceller, hiçbir şey silmez; hatada hiçbir şey yazılmaz ve iz kaydedilmez,
/// bir sonraki açılış yeniden dener.
/// </summary>
public class CatalogPackageImporter(AppDbContext context, ICatalogTransferService transfer, ILogger<CatalogPackageImporter> logger)
{
    // Açılış işinin kimliği: AppDbContext'in kullanıcı yokken yazdığı varsayılan.
    private const int SystemUserId = 0;
    private const int LoggedErrorLimit = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CatalogPackageImportOutcome> ImportIfChangedAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("Catalog package {Path} not found; skipped.", path);
            return new CatalogPackageImportOutcome(CatalogPackageImportStatus.Missing);
        }

        var bytes = await File.ReadAllBytesAsync(path, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var name = System.IO.Path.GetFileNameWithoutExtension(path);

        var applied = await context.AppliedDataPackages.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, ct);
        if (applied?.Sha256 == hash)
            return new CatalogPackageImportOutcome(CatalogPackageImportStatus.Unchanged);

        var bundle = JsonSerializer.Deserialize<CatalogBundleDto>(bytes, JsonOptions)
                     ?? throw new InvalidDataException($"Catalog package {path} is empty.");
        var result = await transfer.ImportAsync(bundle, dryRun: false, SystemUserId, ct);
        if (result.Errors.Count > 0)
        {
            logger.LogError("Catalog package {Name} was not imported: {ErrorCount} errors. First: {Errors}",
                name, result.Errors.Count, string.Join("; ", result.Errors.Take(LoggedErrorLimit)));
            return new CatalogPackageImportOutcome(CatalogPackageImportStatus.Failed, result);
        }

        context.ChangeTracker.Clear();
        if (applied is null)
            context.AppliedDataPackages.Add(new AppliedDataPackage { Name = name, Sha256 = hash, AppliedAt = DateTime.UtcNow });
        else
            context.AppliedDataPackages.Update(new AppliedDataPackage { Name = name, Sha256 = hash, AppliedAt = DateTime.UtcNow });
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Catalog package {Name} imported. Created/updated: grades {GradesCreated}/{GradesUpdated}, subjects {SubjectsCreated}/{SubjectsUpdated}, " +
            "topics {TopicsCreated}/{TopicsUpdated}, subtopics {SubTopicsCreated}/{SubTopicsUpdated}, outcomes {OutcomesCreated}/{OutcomesUpdated}.",
            name, result.Grades.Created, result.Grades.Updated, result.Subjects.Created, result.Subjects.Updated,
            result.Topics.Created, result.Topics.Updated, result.SubTopics.Created, result.SubTopics.Updated,
            result.LearningOutcomes.Created, result.LearningOutcomes.Updated);
        return new CatalogPackageImportOutcome(CatalogPackageImportStatus.Imported, result);
    }
}
