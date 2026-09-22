using System.Collections.Generic;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>
/// <c>seed-schools</c> aracının özet raporu (issue #216). Hem log'a yazılır hem çağırana döner.
/// HTTP yüzeyi olmadığı için Models/Dtos altında değil, servisin yanında.
/// </summary>
public sealed class SchoolSeedResult
{
    public bool DryRun { get; set; }

    /// <summary>Kaynak tanımı: URL ya da önbellek yolu.</summary>
    public string Source { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public bool SourceFromCache { get; set; }

    /// <summary>Kaynak dosyadaki toplam satır (tüm iller, tüm türler).</summary>
    public int TotalRowsInSource { get; set; }

    /// <summary>İl + tür filtresinden geçen satır (skip kuralları ve limit uygulanmadan önce).</summary>
    public int CandidateRows { get; set; }

    public int Added { get; set; }
    /// <summary>Kurum kodu ya da ad+il+ilçe ile zaten mevcut (aktif kayıt) — atlandı.</summary>
    public int SkippedExisting { get; set; }
    /// <summary>Kurum kodu soft-delete edilmiş bir kayıtta — unique index nedeniyle yeniden eklenmez, ayrıca raporlanır.</summary>
    public int SkippedSoftDeleted { get; set; }
    /// <summary>Ad 200 ya da kurum kodu 32 karakteri aşıyor — kolon sınırı, atlandı.</summary>
    public int SkippedTooLong { get; set; }
    public int SkippedBuyuksehir { get; set; }
    public int SkippedSpecialEducation { get; set; }
    /// <summary>İl başına limit nedeniyle dışarıda kalan.</summary>
    public int SkippedByLimit { get; set; }
    public int UnmatchedDistrictRows { get; set; }

    public List<SchoolSeedProvinceSummary> Provinces { get; set; } = new();

    /// <summary>İstenen ama Province tablosunda bulunmayan iller (ad eşleşmedi).</summary>
    public List<string> UnmatchedProvinces { get; set; } = new();

    /// <summary>İstenen ama kaynak dosyada hiç satırı olmayan iller (yazım farkı olabilir).</summary>
    public List<string> ProvincesNotInSource { get; set; } = new();

    /// <summary>District tablosunda karşılığı bulunamayan (il, ilçe) çiftleri ve etkilenen satır sayısı.</summary>
    public List<SchoolSeedUnmatchedDistrict> UnmatchedDistricts { get; set; } = new();

    /// <summary>Gerçekten eklenen (dry-run'da eklenecek) okullar — yalnızca ilk <see cref="AddedSampleLimit"/> tanesi.</summary>
    public List<SchoolSeedAddedSchool> AddedSample { get; set; } = new();

    public const int AddedSampleLimit = 50;
}

public sealed class SchoolSeedProvinceSummary
{
    public string Province { get; set; } = string.Empty;
    public bool ProvinceMatched { get; set; }

    public int IlkokulAdded { get; set; }
    public int IlkokulExisting { get; set; }
    public int OrtaokulAdded { get; set; }
    public int OrtaokulExisting { get; set; }

    public int SkippedSoftDeleted { get; set; }
    public int SkippedTooLong { get; set; }
    public int SkippedBuyuksehir { get; set; }
    public int SkippedSpecialEducation { get; set; }
    public int SkippedByLimit { get; set; }
    public int UnmatchedDistrictRows { get; set; }
}

public sealed class SchoolSeedUnmatchedDistrict
{
    public string Province { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public int Rows { get; set; }
}

public sealed class SchoolSeedAddedSchool
{
    public string ExternalCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
}
