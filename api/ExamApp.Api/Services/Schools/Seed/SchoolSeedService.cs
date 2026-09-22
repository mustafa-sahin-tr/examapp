using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>
/// Bkz. <see cref="ISchoolSeedService"/>. Akış: ortam guard'ı → kaynak dosya → CSV parse → il/tür
/// filtresi → skip kuralları → il/ilçe eşleştirme → ad normalizasyonu → deterministik limit →
/// varlık kontrolü (kurum kodu, yoksa ad+il+ilçe) → tek SaveChanges.
///
/// <para>İl/ilçe ad eşleştirmesi Türkçe büyük/küçük harf ve İ/I/ı/i farkına duyarsızdır
/// (<see cref="FoldKey"/>). Eşleşmeyen il/ilçe raporda listelenir, sessizce atlanmaz.</para>
/// </summary>
public sealed class SchoolSeedService : ISchoolSeedService
{
    public const string TypeIlkokul = "İlkokul";
    public const string TypeOrtaokul = "Ortaokul";
    public const string TypeImamHatipOrtaokulu = "İmam Hatip Ortaokulu";

    /// <summary>School.Name / School.ExternalCode kolon sınırları (entity'deki MaxLength ile aynı).</summary>
    public const int NameMaxLength = 200;
    public const int ExternalCodeMaxLength = 32;

    private const string BuyuksehirDistrict = "Büyükşehir";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly StringComparer TrNameComparer = StringComparer.Create(Tr, ignoreCase: false);

    // FoldKey sabitleri — her satırda yeniden hesaplanmasın.
    private static readonly string KeyIlkokul = FoldKey(TypeIlkokul);
    private static readonly string KeyOrtaokul = FoldKey(TypeOrtaokul);
    private static readonly string KeyImamHatipOrtaokulu = FoldKey(TypeImamHatipOrtaokulu);
    private static readonly string[] SpecialEducationMarkerKeys = [FoldKey("Özel Eğitim"), FoldKey("Uygulama Merkezi")];

    private readonly AppDbContext _context;
    private readonly ISchoolSeedSourceProvider _source;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SchoolSeedService> _logger;

    public SchoolSeedService(
        AppDbContext context,
        ISchoolSeedSourceProvider source,
        IHostEnvironment environment,
        ILogger<SchoolSeedService> logger)
    {
        _context = context;
        _source = source;
        _environment = environment;
        _logger = logger;
    }

    public static bool IsAllowedEnvironment(IHostEnvironment environment)
        => ExamApp.Api.Services.Seed.SeedCommands.IsAllowedEnvironment(environment);

    public async Task<SchoolSeedResult> RunAsync(SchoolSeedOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsAllowedEnvironment(_environment))
        {
            throw new InvalidOperationException(
                $"seed-schools yalnızca Development/Staging ortamında çalışır; mevcut ortam: '{_environment.EnvironmentName}'.");
        }

        if (options.Provinces.Count == 0)
            throw new ArgumentException("En az bir il verilmeli.", nameof(options));
        if (options.Kinds.Count == 0)
            throw new ArgumentException("En az bir okul türü verilmeli.", nameof(options));
        if (options.LimitPerProvince is <= 0)
            throw new ArgumentException("İl başına limit pozitif olmalı.", nameof(options));

        var source = await _source.GetAsync(ct);
        IReadOnlyList<MebSchoolRow> rows;
        using (var stream = source.OpenRead())
        {
            rows = MebSchoolCsv.Parse(stream);
        }

        var result = new SchoolSeedResult
        {
            DryRun = options.DryRun,
            Source = source.Description,
            SourceSha256 = source.Sha256,
            SourceFromCache = source.FromCache,
            TotalRowsInSource = rows.Count
        };

        // Referans tablolar küçük (81 il, ~970 ilçe) — tek seferde belleğe.
        var dbProvinces = await _context.Provinces
            .AsNoTracking()
            .Include(p => p.Districts)
            .ToListAsync(ct);
        var provinceByKey = dbProvinces
            .GroupBy(p => FoldKey(p.Name))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var sourceByProvince = rows
            .GroupBy(r => FoldKey(r.Province))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // İstenen iller — sırayı koru, tekrarları at.
        var requested = options.Provinces
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .DistinctBy(FoldKey, StringComparer.Ordinal)
            .ToList();

        var matchedProvinceIds = requested
            .Select(FoldKey)
            .Where(provinceByKey.ContainsKey)
            .Select(k => provinceByKey[k].Id)
            .ToList();

        // Varlık kontrolü: soft-delete edilmişler de dahil (unique index onları da kapsar) — ayrı sayılır.
        var existingCodeRows = await _context.Schools
            .IgnoreQueryFilters()
            .Where(s => s.ExternalCode != null)
            .Select(s => new { Code = s.ExternalCode!, s.IsDeleted })
            .ToListAsync(ct);
        var existingCodes = existingCodeRows.Where(r => !r.IsDeleted).Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        var softDeletedCodes = existingCodeRows.Where(r => r.IsDeleted).Select(r => r.Code).ToHashSet(StringComparer.Ordinal);

        var existingNameKeys = (await _context.Schools
                .IgnoreQueryFilters()
                .Where(s => s.ProvinceId != null && matchedProvinceIds.Contains(s.ProvinceId.Value))
                .Select(s => new { s.Name, s.ProvinceId, s.DistrictId })
                .ToListAsync(ct))
            .Select(s => NameKey(s.Name, s.ProvinceId!.Value, s.DistrictId))
            .ToHashSet(StringComparer.Ordinal);

        var unmatchedDistricts = new Dictionary<(string Province, string District), int>();
        var toAdd = new List<School>();

        foreach (var provinceName in requested)
        {
            var key = FoldKey(provinceName);
            var summary = new SchoolSeedProvinceSummary { Province = provinceName };
            result.Provinces.Add(summary);

            if (!sourceByProvince.TryGetValue(key, out var provinceRows))
            {
                result.ProvincesNotInSource.Add(provinceName);
                provinceRows = new List<MebSchoolRow>();
            }

            if (!provinceByKey.TryGetValue(key, out var province))
            {
                result.UnmatchedProvinces.Add(provinceName);
                summary.ProvinceMatched = false;
                _logger.LogWarning("seed-schools: '{Province}' ili Province tablosunda bulunamadı; {Rows} satır atlandı.",
                    provinceName, provinceRows.Count);
                continue;
            }

            summary.ProvinceMatched = true;
            var districtByKey = province.Districts
                .GroupBy(d => FoldKey(d.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var candidates = new List<(MebSchoolRow Row, SchoolSeedKind Kind, District District, string Name)>();

            foreach (var row in provinceRows)
            {
                var kind = ClassifyKind(row.SchoolType, options.IncludeImamHatip);
                if (kind is null || !options.Kinds.Contains(kind.Value))
                    continue;

                result.CandidateRows++;

                if (string.Equals(row.District, BuyuksehirDistrict, StringComparison.Ordinal))
                {
                    summary.SkippedBuyuksehir++;
                    result.SkippedBuyuksehir++;
                    continue;
                }

                if (IsSpecialEducation(row.Name))
                {
                    summary.SkippedSpecialEducation++;
                    result.SkippedSpecialEducation++;
                    continue;
                }

                if (!districtByKey.TryGetValue(FoldKey(row.District), out var district))
                {
                    summary.UnmatchedDistrictRows++;
                    result.UnmatchedDistrictRows++;
                    var pair = (provinceName, row.District);
                    unmatchedDistricts[pair] = unmatchedDistricts.GetValueOrDefault(pair) + 1;
                    continue;
                }

                var normalized = SchoolNameNormalizer.Normalize(row.Name);
                if (normalized.Length > NameMaxLength || row.InstitutionCode.Length > ExternalCodeMaxLength)
                {
                    summary.SkippedTooLong++;
                    result.SkippedTooLong++;
                    _logger.LogWarning("seed-schools: '{Name}' (kod {Code}) kolon sınırını aşıyor — atlandı.", normalized, row.InstitutionCode);
                    continue;
                }

                candidates.Add((row, kind.Value, district, normalized));
            }

            // Deterministik örnekleme: ada göre (tr-TR) sıralı, eşitlikte kurum koduna göre; ilk N.
            var ordered = candidates
                .OrderBy(c => c.Name, TrNameComparer)
                .ThenBy(c => c.Row.InstitutionCode, StringComparer.Ordinal)
                .ToList();

            if (options.LimitPerProvince is { } limit && ordered.Count > limit)
            {
                summary.SkippedByLimit = ordered.Count - limit;
                result.SkippedByLimit += summary.SkippedByLimit;
                ordered = ordered.Take(limit).ToList();
            }

            foreach (var (row, kind, district, name) in ordered)
            {
                var code = row.InstitutionCode.Length > 0 ? row.InstitutionCode : null;
                var nameKey = NameKey(name, province.Id, district.Id);

                if (code != null && softDeletedCodes.Contains(code))
                {
                    summary.SkippedSoftDeleted++;
                    result.SkippedSoftDeleted++;
                    continue;
                }

                var exists = (code != null && existingCodes.Contains(code)) || existingNameKeys.Contains(nameKey);
                if (exists)
                {
                    if (kind == SchoolSeedKind.Ilkokul) summary.IlkokulExisting++; else summary.OrtaokulExisting++;
                    result.SkippedExisting++;
                    continue;
                }

                if (code != null) existingCodes.Add(code);
                existingNameKeys.Add(nameKey);

                toAdd.Add(new School
                {
                    Name = name,
                    ProvinceId = province.Id,
                    DistrictId = district.Id,
                    ExternalCode = code,
                    IsSeedData = true
                });

                if (kind == SchoolSeedKind.Ilkokul) summary.IlkokulAdded++; else summary.OrtaokulAdded++;
                result.Added++;

                if (result.AddedSample.Count < SchoolSeedResult.AddedSampleLimit)
                {
                    result.AddedSample.Add(new SchoolSeedAddedSchool
                    {
                        ExternalCode = code ?? string.Empty,
                        Name = name,
                        Province = province.Name,
                        District = district.Name
                    });
                }
            }
        }

        result.UnmatchedDistricts = unmatchedDistricts
            .OrderBy(kv => kv.Key.Province, TrNameComparer)
            .ThenBy(kv => kv.Key.District, TrNameComparer)
            .Select(kv => new SchoolSeedUnmatchedDistrict
            {
                Province = kv.Key.Province,
                District = kv.Key.District,
                Rows = kv.Value
            })
            .ToList();

        if (!options.DryRun && toAdd.Count > 0)
        {
            _context.Schools.AddRange(toAdd);
            await _context.SaveChangesAsync(ct);
        }

        LogSummary(result);
        return result;
    }

    /// <summary>
    /// Kaynak türünü içe aktarılan türe çevirir; kapsam dışı türler (lise, anaokulu…) için null.
    /// "İmam Hatip Ortaokulu" yalnızca <paramref name="includeImamHatip"/> ile Ortaokul sayılır.
    /// </summary>
    public static SchoolSeedKind? ClassifyKind(string schoolType, bool includeImamHatip)
    {
        var key = FoldKey(schoolType);
        if (key == KeyIlkokul) return SchoolSeedKind.Ilkokul;
        if (key == KeyOrtaokul) return SchoolSeedKind.Ortaokul;
        if (includeImamHatip && key == KeyImamHatipOrtaokulu) return SchoolSeedKind.Ortaokul;
        return null;
    }

    /// <summary>Adında "Özel Eğitim" / "Uygulama Merkezi" geçen ama türü İlkokul/Ortaokul görünen kayıtlar (yanlış sınıflanmış).</summary>
    public static bool IsSpecialEducation(string name)
    {
        var key = FoldKey(name);
        foreach (var markerKey in SpecialEducationMarkerKeys)
        {
            if (key.Contains(markerKey, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Türkçe büyük/küçük harf ve İ/I/ı/i farkına duyarsız karşılaştırma anahtarı.
    /// Önce dört "i" varyantı tek karaktere indirgenir, sonra invariant küçük harf.
    /// </summary>
    public static string FoldKey(string value)
    {
        var s = value.Trim();
        var chars = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            chars[i] = s[i] switch
            {
                'İ' or 'I' or 'ı' => 'i',
                var c => c
            };
        }
        return new string(chars).ToLower(Tr);
    }

    private static string NameKey(string name, int provinceId, int? districtId)
        => $"{provinceId}|{districtId?.ToString(CultureInfo.InvariantCulture) ?? "-"}|{FoldKey(name)}";

    private void LogSummary(SchoolSeedResult r)
    {
        _logger.LogInformation(
            "seed-schools {Mode}: kaynak={Source} sha256={Hash} toplam={Total} aday={Candidates} eklenen={Added} mevcut={Existing} softDeleted={SoftDeleted} " +
            "büyükşehir={Buyuksehir} özelEğitim={SpecialEd} uzun={TooLong} limitDışı={Limit} eşleşmeyenİlçeSatırı={UnmatchedRows} eşleşmeyenİl={UnmatchedProvinces}",
            r.DryRun ? "DRY-RUN" : "WRITE", r.Source, r.SourceSha256, r.TotalRowsInSource, r.CandidateRows, r.Added,
            r.SkippedExisting, r.SkippedSoftDeleted, r.SkippedBuyuksehir, r.SkippedSpecialEducation, r.SkippedTooLong, r.SkippedByLimit, r.UnmatchedDistrictRows,
            string.Join(",", r.UnmatchedProvinces));

        foreach (var p in r.Provinces)
        {
            _logger.LogInformation(
                "seed-schools  {Province,-10} eşleşti={Matched} İlkokul +{IlkAdd}/={IlkEx}  Ortaokul +{OrtAdd}/={OrtEx}  " +
                "büyükşehir={Buyuksehir} özelEğitim={SpecialEd} limitDışı={Limit} eşleşmeyenİlçe={Unmatched}",
                p.Province, p.ProvinceMatched, p.IlkokulAdded, p.IlkokulExisting, p.OrtaokulAdded, p.OrtaokulExisting,
                p.SkippedBuyuksehir, p.SkippedSpecialEducation, p.SkippedByLimit, p.UnmatchedDistrictRows);
        }

        foreach (var u in r.UnmatchedDistricts)
        {
            _logger.LogWarning("seed-schools: '{Province}/{District}' ilçesi District tablosunda yok — {Rows} satır atlandı.",
                u.Province, u.District, u.Rows);
        }
    }
}
