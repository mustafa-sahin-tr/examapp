using System.Globalization;
using System.Text.RegularExpressions;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.TaxonomySearch;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TargetGoals;

public sealed record TargetProgramQuery(string? Exam, string? ScoreType, int? Year, string? City, string? Search, int Skip, int Take);

public sealed record TargetProgramImport(string Exam, int Year, string Source, byte[] Content);

/// <summary>Issue #695: hedef kataloğu ve CSV aktarımı. Yetki çağıranın (controller) sorumluluğundadır.</summary>
public interface ITargetProgramService
{
    /// <summary>Arama boşken en çok seçilenler önce (#891); aramada ad sırası.</summary>
    Task<TargetProgramPageDto<TargetProgramDto>> SearchAsync(TargetProgramQuery query, CancellationToken ct = default);

    /// <summary>Issue #891: katalogdaki iller (sınava göre), Türkçe alfabe sırasıyla.</summary>
    Task<List<string>> CitiesAsync(string? exam, CancellationToken ct = default);
    Task<TargetProgramPageDto<AdminTargetProgramDto>> SearchForAdminAsync(TargetProgramQuery query, CancellationToken ct = default);
    Task<AdminTargetProgramDto> CreateAsync(SaveTargetProgramDto request, int userId, CancellationToken ct = default);
    Task<AdminTargetProgramDto?> UpdateAsync(int id, SaveTargetProgramDto request, int userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, int userId, CancellationToken ct = default);
    Task<TargetProgramImportPreviewDto> PreviewImportAsync(TargetProgramImport import, CancellationToken ct = default);
    Task<TargetProgramImportResultDto> ConfirmImportAsync(TargetProgramImport import, int userId, CancellationToken ct = default);
    Task<List<TargetCatalogSummaryDto>> SummaryAsync(CancellationToken ct = default);
}

public sealed partial class TargetProgramService(AppDbContext db) : ITargetProgramService
{
    internal const int PreviewLimit = 200;
    internal const int MaxImportRows = 50_000;

    private sealed record Row(int Line, string ScoreType, string Name, string Institution, string City, string District, decimal MinimumScore, decimal? Percentile, string Key);

    private sealed record Analysis(List<Row> New, List<(TargetProgram Existing, Row Row)> Changed, int Unchanged, List<TargetProgramImportErrorDto> Invalid);

    public async Task<TargetProgramPageDto<TargetProgramDto>> SearchAsync(TargetProgramQuery query, CancellationToken ct = default)
    {
        var rows = Filter(query);
        // #891: boş aramada öneri listesi — öğrencilerin en çok seçtikleri, sonra yüksek taban.
        var ordered = string.IsNullOrWhiteSpace(query.Search)
            ? rows.OrderByDescending(p => db.StudentTargetGoals.Count(g => g.TargetProgramId == p.Id))
                .ThenByDescending(p => p.Year).ThenByDescending(p => p.MinimumScore).ThenBy(p => p.Id)
            : Ordered(rows);
        return new()
        {
            Total = await rows.CountAsync(ct),
            Items = await ordered.Skip(query.Skip).Take(query.Take).Select(p => new TargetProgramDto
            {
                Id = p.Id, ScoreType = p.ScoreType, Name = p.Name, Institution = p.Institution, City = p.City, District = p.District,
                Year = p.Year, MinimumScore = p.MinimumScore, Percentile = p.Percentile, SourceReference = p.SourceReference,
            }).ToListAsync(ct),
        };
    }

    public async Task<List<string>> CitiesAsync(string? exam, CancellationToken ct = default)
    {
        var cities = await Filter(new TargetProgramQuery(exam, null, null, null, null, 0, 0))
            .Select(p => p.City)
            .Distinct()
            .ToListAsync(ct);
        return cities.Where(c => !string.IsNullOrWhiteSpace(c)).Order(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), false)).ToList();
    }

    public async Task<TargetProgramPageDto<AdminTargetProgramDto>> SearchForAdminAsync(TargetProgramQuery query, CancellationToken ct = default)
    {
        var rows = Filter(query);
        return new()
        {
            Total = await rows.CountAsync(ct),
            Items = await Ordered(rows).Skip(query.Skip).Take(query.Take).Select(p => new AdminTargetProgramDto
            {
                Id = p.Id, ScoreType = p.ScoreType, Name = p.Name, Institution = p.Institution, City = p.City, District = p.District,
                Year = p.Year, MinimumScore = p.MinimumScore, Percentile = p.Percentile, SourceReference = p.SourceReference,
                InUse = db.StudentTargetGoals.Any(g => g.TargetProgramId == p.Id),
            }).ToListAsync(ct),
        };
    }

    public async Task<List<TargetCatalogSummaryDto>> SummaryAsync(CancellationToken ct = default)
    {
        var byTypeAndYear = await db.TargetPrograms.AsNoTracking()
            .GroupBy(p => new { p.ScoreType, p.Year })
            .Select(g => new { g.Key.ScoreType, g.Key.Year, Count = g.Count() })
            .ToListAsync(ct);

        return new[] { TargetScoreTypes.LgsExam, TargetScoreTypes.YksExam }.Select(exam =>
        {
            var types = TargetScoreTypes.OfExam(exam);
            var rows = byTypeAndYear.Where(r => types.Contains(r.ScoreType)).ToList();
            int? latest = rows.Count == 0 ? null : rows.Max(r => r.Year);
            return new TargetCatalogSummaryDto
            {
                Exam = exam,
                Total = rows.Sum(r => r.Count),
                LatestYear = latest,
                LatestYearCount = rows.Where(r => r.Year == latest).Sum(r => r.Count),
            };
        }).ToList();
    }

    private IQueryable<TargetProgram> Filter(TargetProgramQuery query)
    {
        var rows = db.TargetPrograms.AsNoTracking();
        if (TargetScoreTypes.IsExam(query.Exam))
        {
            var types = TargetScoreTypes.OfExam(query.Exam!);
            rows = rows.Where(p => types.Contains(p.ScoreType));
        }
        if (!string.IsNullOrWhiteSpace(query.ScoreType))
            rows = rows.Where(p => p.ScoreType == query.ScoreType);
        if (query.Year.HasValue)
            rows = rows.Where(p => p.Year == query.Year.Value);
        if (!string.IsNullOrWhiteSpace(query.City))
            rows = rows.Where(p => p.City == query.City.Trim());

        foreach (var word in TaxonomySearchService.Normalize(query.Search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var pattern = $"%{word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            rows = rows.Where(p => EF.Functions.Like(p.NormalizedKey, pattern, "\\"));
        }
        return rows;
    }

    private static IQueryable<TargetProgram> Ordered(IQueryable<TargetProgram> rows) =>
        rows.OrderByDescending(p => p.Year).ThenBy(p => p.Name).ThenBy(p => p.Institution).ThenBy(p => p.Id);

    public async Task<AdminTargetProgramDto> CreateAsync(SaveTargetProgramDto request, int userId, CancellationToken ct = default)
    {
        var entity = new TargetProgram();
        Apply(entity, request);
        await EnsureNoDuplicateAsync(entity, ct);
        db.TargetPrograms.Add(entity);
        await SaveAsync(userId, ct);
        return ToAdminDto(entity, inUse: false);
    }

    public async Task<AdminTargetProgramDto?> UpdateAsync(int id, SaveTargetProgramDto request, int userId, CancellationToken ct = default)
    {
        var entity = await db.TargetPrograms.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
            return null;

        Apply(entity, request);
        await EnsureNoDuplicateAsync(entity, ct);
        await SaveAsync(userId, ct);
        return ToAdminDto(entity, await db.StudentTargetGoals.AnyAsync(g => g.TargetProgramId == id, ct));
    }

    public async Task<bool> DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        var entity = await db.TargetPrograms.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
            return false;
        if (await db.StudentTargetGoals.AnyAsync(g => g.TargetProgramId == id, ct))
            throw new TargetGoalRuleException("InUse");

        db.TargetPrograms.Remove(entity);
        await SaveAsync(userId, ct);
        return true;
    }

    private static void Apply(TargetProgram entity, SaveTargetProgramDto request)
    {
        var name = Clean(request.Name);
        var institution = Clean(request.Institution);
        var city = Clean(request.City);
        var district = Clean(request.District);
        var error = ValidateFields(request.ScoreType, name, institution, city, district);
        if (error is not null)
            throw new TargetGoalRuleException(error);

        entity.ScoreType = request.ScoreType;
        entity.Name = name;
        entity.Institution = institution;
        entity.City = city;
        entity.District = district;
        entity.Year = request.Year;
        entity.MinimumScore = request.MinimumScore;
        entity.Percentile = request.Percentile;
        entity.SourceReference = request.SourceReference.Trim();
        entity.NormalizedKey = KeyOf(name, institution, city, district);
    }

    private async Task EnsureNoDuplicateAsync(TargetProgram entity, CancellationToken ct)
    {
        if (await db.TargetPrograms.AnyAsync(p => p.Id != entity.Id && p.ScoreType == entity.ScoreType && p.Year == entity.Year && p.NormalizedKey == entity.NormalizedKey, ct))
            throw new TargetGoalRuleException("Duplicate");
    }

    private async Task SaveAsync(int userId, CancellationToken ct)
    {
        db.SetCurrentUser(userId);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            throw new TargetGoalRuleException("Duplicate");
        }
    }

    public async Task<TargetProgramImportPreviewDto> PreviewImportAsync(TargetProgramImport import, CancellationToken ct = default)
    {
        var analysis = await AnalyzeAsync(import, ct);
        return new()
        {
            NewCount = analysis.New.Count,
            ChangedCount = analysis.Changed.Count,
            UnchangedCount = analysis.Unchanged,
            InvalidCount = analysis.Invalid.Count,
            New = analysis.New.Take(PreviewLimit).Select(row => Fill(new TargetProgramImportRowDto(), row)).ToList(),
            Changed = analysis.Changed.Take(PreviewLimit).Select(change =>
            {
                var dto = Fill(new TargetProgramImportChangeDto(), change.Row);
                dto.PreviousMinimumScore = change.Existing.MinimumScore;
                dto.PreviousPercentile = change.Existing.Percentile;
                return dto;
            }).ToList(),
            Invalid = analysis.Invalid.Take(PreviewLimit).ToList(),
        };
    }

    /// <summary>Hatalı satırlar atlanır: resmî dosyada puanı olmayan satır (dolmayan kontenjan) olağandır, admin önizlemede görmüştür.</summary>
    public async Task<TargetProgramImportResultDto> ConfirmImportAsync(TargetProgramImport import, int userId, CancellationToken ct = default)
    {
        var analysis = await AnalyzeAsync(import, ct);
        var source = import.Source.Trim();
        db.TargetPrograms.AddRange(analysis.New.Select(row => new TargetProgram
        {
            ScoreType = row.ScoreType, Name = row.Name, Institution = row.Institution, City = row.City, District = row.District,
            Year = import.Year, MinimumScore = row.MinimumScore, Percentile = row.Percentile, SourceReference = source, NormalizedKey = row.Key,
        }));
        foreach (var (existing, row) in analysis.Changed)
        {
            existing.MinimumScore = row.MinimumScore;
            existing.Percentile = row.Percentile;
            existing.SourceReference = source;
        }
        await SaveAsync(userId, ct);
        return new()
        {
            Created = analysis.New.Count,
            Updated = analysis.Changed.Count,
            Unchanged = analysis.Unchanged,
            Skipped = analysis.Invalid.Count,
        };
    }

    private async Task<Analysis> AnalyzeAsync(TargetProgramImport import, CancellationToken ct)
    {
        if (!TargetScoreTypes.IsExam(import.Exam))
            throw new TargetGoalRuleException("ExamInvalid");

        var lines = TargetProgramCsv.Parse(TargetProgramCsv.Decode(import.Content));
        if (lines.Count > MaxImportRows)
            throw new TargetGoalRuleException("CsvTooManyRows");

        var types = TargetScoreTypes.OfExam(import.Exam);
        var existing = await db.TargetPrograms
            .Where(p => p.Year == import.Year && types.Contains(p.ScoreType))
            .ToDictionaryAsync(p => (p.ScoreType, p.NormalizedKey), ct);

        var added = new List<Row>();
        var changed = new List<(TargetProgram, Row)>();
        var invalid = new List<TargetProgramImportErrorDto>();
        var unchanged = 0;
        var seen = new HashSet<(string, string)>();
        foreach (var (line, values) in lines)
        {
            var name = Clean(values.GetValueOrDefault(TargetProgramCsv.Name));
            var error = TryReadRow(import.Exam, line, values, name, out var row);
            if (error is null && !seen.Add((row!.ScoreType, row.Key)))
                error = "Duplicate";
            if (error is not null)
            {
                invalid.Add(new() { Line = line, Code = error, Name = name.Length > TargetProgram.NameMaxLength ? name[..TargetProgram.NameMaxLength] : name });
                continue;
            }

            if (!existing.TryGetValue((row!.ScoreType, row.Key), out var current))
                added.Add(row);
            else if (current.MinimumScore != row.MinimumScore || current.Percentile != row.Percentile)
                changed.Add((current, row));
            else
                unchanged++;
        }
        return new(added, changed, unchanged, invalid);
    }

    private static string? TryReadRow(string exam, int line, Dictionary<string, string> values, string name, out Row? row)
    {
        row = null;
        var institution = Clean(values.GetValueOrDefault(TargetProgramCsv.Institution));
        var city = Clean(values.GetValueOrDefault(TargetProgramCsv.City));
        var district = Clean(values.GetValueOrDefault(TargetProgramCsv.District));
        // Resmî dosyalar "SÖZ" yazar: kimlik "soz".
        var rawType = TaxonomySearchService.Normalize(Clean(values.GetValueOrDefault(TargetProgramCsv.ScoreType)));
        var scoreType = exam == TargetScoreTypes.LgsExam && rawType.Length == 0 ? TargetScoreTypes.Lgs : rawType;
        if (!TargetScoreTypes.OfExam(exam).Contains(scoreType))
            return "ScoreTypeInvalid";

        var error = ValidateFields(scoreType, name, institution, city, district);
        if (error is not null)
            return error;

        if (!TryParseDecimal(values.GetValueOrDefault(TargetProgramCsv.MinimumScore), out var minimum) || minimum is < 100 or > 500)
            return "MinimumScoreInvalid";

        decimal? percentile = null;
        var rawPercentile = Clean(values.GetValueOrDefault(TargetProgramCsv.Percentile));
        if (rawPercentile.Length > 0)
        {
            if (!TryParseDecimal(rawPercentile, out var parsed) || parsed is < 0 or > 100)
                return "PercentileInvalid";
            percentile = parsed;
        }

        // Kolon ölçeğine yuvarlanır: yoksa aynı dosyanın ikinci yüklemesi her satırı "değişti" gösterirdi.
        row = new Row(line, scoreType, name, institution, city, district, Math.Round(minimum, 5), percentile is { } p ? Math.Round(p, 5) : null, KeyOf(name, institution, city, district));
        return null;
    }

    private static string? ValidateFields(string scoreType, string name, string institution, string city, string district)
    {
        if (!TargetScoreTypes.IsValid(scoreType))
            return "ScoreTypeInvalid";
        if (name.Length == 0)
            return "NameRequired";
        if (city.Length == 0)
            return "CityRequired";
        if (scoreType != TargetScoreTypes.Lgs && institution.Length == 0)
            return "InstitutionRequired";
        if (name.Length > TargetProgram.NameMaxLength || institution.Length > TargetProgram.NameMaxLength
            || city.Length > TargetProgram.PlaceMaxLength || district.Length > TargetProgram.PlaceMaxLength)
            return "TooLong";
        if (new[] { name, institution, city, district }.Any(PlainTextAttribute.ContainsControlCharacters))
            return "InvalidText";
        return null;
    }

    /// <summary>Virgüllü ("412,345") ve noktalı ("412.345") ondalık kabul edilir; binlik ayraç bu ölçekte yoktur.</summary>
    private static bool TryParseDecimal(string? value, out decimal parsed) =>
        decimal.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed);

    private static string Clean(string? value) => ExtraSpaces().Replace(value?.Trim() ?? string.Empty, " ");

    private static string KeyOf(string name, string institution, string city, string district) =>
        string.Join('|', new[] { name, institution, city, district }.Select(TaxonomySearchService.Normalize));

    private static T Fill<T>(T dto, Row row) where T : TargetProgramImportRowDto
    {
        dto.Line = row.Line;
        dto.ScoreType = row.ScoreType;
        dto.Name = row.Name;
        dto.Institution = row.Institution;
        dto.City = row.City;
        dto.District = row.District;
        dto.MinimumScore = row.MinimumScore;
        dto.Percentile = row.Percentile;
        return dto;
    }

    private static AdminTargetProgramDto ToAdminDto(TargetProgram p, bool inUse) => new()
    {
        Id = p.Id, ScoreType = p.ScoreType, Name = p.Name, Institution = p.Institution, City = p.City, District = p.District,
        Year = p.Year, MinimumScore = p.MinimumScore, Percentile = p.Percentile, SourceReference = p.SourceReference, InUse = inUse,
    };

    [GeneratedRegex(@"[ \t\r\n]+")]
    private static partial Regex ExtraSpaces();
}
