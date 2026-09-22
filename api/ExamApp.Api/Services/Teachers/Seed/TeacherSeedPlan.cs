using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ExamApp.Api.Services.Schools.Seed;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>Issue #217'deki branşlar. Sıra raporlama ve e-posta üretimi için sabittir.</summary>
public enum TeacherSeedBranch
{
    Turkce = 1,
    Matematik = 2,
    FenBilimleri = 3,
    SosyalBilgiler = 4,
    Ingilizce = 5,
    DinKulturu = 6
}

/// <summary>
/// Okul türü → branş dağılımı, deterministik e-posta/ad üretimi (issue #217).
/// Ortaokul: 2 Türkçe, 2 Matematik, 2 Fen Bilimleri, 2 Sosyal Bilgiler, 1 İngilizce, 1 Din Kültürü = 10.
/// İlkokul: 1'er Türkçe/Matematik/Fen/Sosyal/İngilizce = 5.
/// </summary>
public static class TeacherSeedPlan
{
    public sealed record BranchInfo(TeacherSeedBranch Branch, string SubjectName, string Slug);

    /// <summary>Branş → Subject.Name (curriculum tablosundaki ad) ve e-posta slug'ı.</summary>
    public static readonly IReadOnlyList<BranchInfo> Branches =
    [
        new(TeacherSeedBranch.Turkce, "Türkçe", "turkce"),
        new(TeacherSeedBranch.Matematik, "Matematik", "matematik"),
        new(TeacherSeedBranch.FenBilimleri, "Fen Bilimleri", "fen"),
        new(TeacherSeedBranch.SosyalBilgiler, "Sosyal Bilgiler", "sosyal"),
        new(TeacherSeedBranch.Ingilizce, "İngilizce", "ingilizce"),
        new(TeacherSeedBranch.DinKulturu, "Din Kültürü ve Ahlak Bilgisi", "din")
    ];

    /// <summary>
    /// E-posta deseni: <c>seed.t.&lt;kurumKodu&gt;.&lt;brans&gt;.&lt;n&gt;@seed.examapp.local</c>.
    /// Issue #218 temizliği bu alan adını (ve identity/exam <c>IsSeedData</c> bayrağını) anahtar olarak kullanır.
    /// </summary>
    public const string EmailLocalPrefix = ExamApp.Foundation.Security.SeedDataConventions.EmailLocalPrefix + "t.";
    public const string EmailDomain = ExamApp.Foundation.Security.SeedDataConventions.EmailDomain;

    public static int CountFor(SchoolSeedKind kind, TeacherSeedBranch branch) => kind switch
    {
        SchoolSeedKind.Ortaokul => branch switch
        {
            TeacherSeedBranch.Turkce or TeacherSeedBranch.Matematik or TeacherSeedBranch.FenBilimleri or TeacherSeedBranch.SosyalBilgiler => 2,
            _ => 1
        },
        SchoolSeedKind.Ilkokul => branch == TeacherSeedBranch.DinKulturu ? 0 : 1,
        _ => 0
    };

    public static int TotalFor(SchoolSeedKind kind) => Branches.Sum(b => CountFor(kind, b.Branch));

    private static readonly string KeyOrtaokul = SchoolSeedService.FoldKey("ortaokul");
    private static readonly string KeyIlkokul = SchoolSeedService.FoldKey("ilkokul");

    /// <summary>
    /// School tablosunda tür kolonu yok; ad üzerinden: "…Ortaokulu" → Ortaokul (İmam Hatip Ortaokulu dahil),
    /// "…İlkokulu" → İlkokul, ikisi de yoksa null (kapsam dışı, raporlanır). Ad ikisini de içeriyorsa Ortaokul.
    /// </summary>
    public static SchoolSeedKind? ClassifyBySchoolName(string name)
    {
        var key = SchoolSeedService.FoldKey(name);
        if (key.Contains(KeyOrtaokul, StringComparison.Ordinal)) return SchoolSeedKind.Ortaokul;
        if (key.Contains(KeyIlkokul, StringComparison.Ordinal)) return SchoolSeedKind.Ilkokul;
        return null;
    }

    /// <summary>Kurum kodu (MEB) e-posta içinde güvenli hale getirilir; yoksa <c>s&lt;Id&gt;</c>.</summary>
    public static string SchoolCode(string? externalCode, int schoolId)
    {
        if (!string.IsNullOrWhiteSpace(externalCode))
        {
            var safe = new string(externalCode.Trim().ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c)).ToArray());
            if (safe.Length > 0) return safe;
        }
        return "s" + schoolId.ToString(CultureInfo.InvariantCulture);
    }

    public static string Email(string schoolCode, BranchInfo branch, int ordinal)
        => $"{EmailLocalPrefix}{schoolCode}.{branch.Slug}.{ordinal.ToString(CultureInfo.InvariantCulture)}@{EmailDomain}";

    /// <summary>
    /// Deterministik ad/soyad: e-postanın SHA-256'sından iki indeks. Aynı okul+branş+sıra her koşuda aynı adı verir;
    /// string.GetHashCode süreçler arası kararlı olmadığı için kullanılmaz.
    /// </summary>
    public static (string FirstName, string LastName) PickName(string email)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(email));
        var i1 = (int)(BitConverter.ToUInt32(hash, 0) % (uint)TeacherSeedNamePool.FirstNames.Count);
        var i2 = (int)(BitConverter.ToUInt32(hash, 4) % (uint)TeacherSeedNamePool.LastNames.Count);
        return (TeacherSeedNamePool.FirstNames[i1], TeacherSeedNamePool.LastNames[i2]);
    }
}
