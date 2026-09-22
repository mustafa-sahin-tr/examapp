using System.Collections.Generic;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>Kaynak dosyadaki okul türlerinden içe aktarılanlar (issue #216).</summary>
public enum SchoolSeedKind
{
    Ilkokul = 1,
    Ortaokul = 2
}

/// <summary>
/// <c>seed-schools</c> aracının parametreleri. Varsayılanlar issue #216'daki tam kapsamı verir:
/// 10 il, İlkokul + Ortaokul, limitsiz, İmam Hatip Ortaokulları hariç.
/// </summary>
public sealed record SchoolSeedOptions
{
    /// <summary>Issue #216'daki 10 il. Parametre verilmezse bu liste kullanılır.</summary>
    public static readonly IReadOnlyList<string> DefaultProvinces =
    [
        "İstanbul", "Ankara", "İzmir", "Erzincan", "Trabzon",
        "Adana", "Mersin", "Antalya", "Mardin", "Kars"
    ];

    public IReadOnlyList<string> Provinces { get; init; } = DefaultProvinces;

    /// <summary>İl başına en fazla kaç okul (ada göre sıralı ilk N). null = sınırsız.</summary>
    public int? LimitPerProvince { get; init; }

    public IReadOnlySet<SchoolSeedKind> Kinds { get; init; } =
        new HashSet<SchoolSeedKind> { SchoolSeedKind.Ilkokul, SchoolSeedKind.Ortaokul };

    /// <summary>"İmam Hatip Ortaokulu" türündeki kayıtlar Ortaokul olarak dahil edilsin mi?</summary>
    public bool IncludeImamHatip { get; init; }

    /// <summary>true ise hiçbir şey yazılmaz; rapor "eklenecek" sayılarını gösterir.</summary>
    public bool DryRun { get; init; }
}
