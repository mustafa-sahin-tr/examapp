using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// Bağımsız öğretmen (tutor) seed planı (issue #218): il + branş bazında seed okul öğretmeni sayısının
/// YARISI (<c>floor</c>; tek sayıda aşağı yuvarlanır, 0 → 0) kadar hesap. E-posta
/// <c>seed.i.&lt;ilSlug&gt;.&lt;brans&gt;.&lt;n&gt;@seed.examapp.local</c>; ad <see cref="TeacherSeedPlan.PickName"/> ile,
/// tutor profili alanları e-postanın SHA-256'sından deterministik üretilir (öğrenci aramasında çıksın diye).
/// </summary>
public static class TutorSeedPlan
{
    /// <summary>Yerel kısım öneki: <c>seed.i.</c> (i = independent). Okul öğretmenleri <c>seed.t.</c>.</summary>
    public const string EmailLocalPrefix = ExamApp.Foundation.Security.SeedDataConventions.EmailLocalPrefix + "i.";
    public const string EmailDomain = ExamApp.Foundation.Security.SeedDataConventions.EmailDomain;

    public const int BioMaxLength = 500;

    /// <summary>Yarım formülü: <c>floor(n / 2)</c>. 400 → 200, 5 → 2, 1 → 0, 0 → 0.</summary>
    public static int HalfOf(int schoolTeacherCount) => Math.Max(0, schoolTeacherCount) / 2;

    /// <summary>
    /// Bir il+branş grubundaki <paramref name="count"/> tutor'dan kaçı Pending kalır: <c>floor(count × ratio)</c>.
    /// Sıra numarası en yüksek olanlar Pending'dir (deterministik).
    /// </summary>
    public static int PendingCountFor(int count, double pendingRatio)
    {
        if (count <= 0 || pendingRatio <= 0) return 0;
        if (pendingRatio >= 1) return count;
        return (int)Math.Floor(count * pendingRatio + 1e-9);
    }

    public static bool IsPending(int ordinal, int count, double pendingRatio)
        => ordinal > count - PendingCountFor(count, pendingRatio);

    /// <summary>
    /// İl adını e-posta yerel kısmına uygun ASCII slug'a çevirir (<c>SeedDataConventions</c> regex'i:
    /// yalnızca <c>[a-z0-9.-]</c>): "İstanbul" → "istanbul", "Şanlıurfa" → "sanliurfa", "Afyonkarahisar" → "afyonkarahisar".
    /// Boş kalırsa "il".
    /// </summary>
    public static string ProvinceSlug(string provinceName)
    {
        var sb = new StringBuilder(provinceName.Length);
        foreach (var raw in provinceName.Trim())
        {
            var c = raw switch
            {
                'İ' or 'I' or 'ı' or 'i' => 'i',
                'Ç' or 'ç' => 'c',
                'Ğ' or 'ğ' => 'g',
                'Ö' or 'ö' => 'o',
                'Ş' or 'ş' => 's',
                'Ü' or 'ü' => 'u',
                'Â' or 'â' or 'Á' or 'á' => 'a',
                'Û' or 'û' => 'u',
                'Î' or 'î' => 'i',
                ' ' or '-' or '_' => '-',
                _ => char.ToLowerInvariant(raw)
            };
            if (char.IsAsciiLetterOrDigit(c) || c == '-') sb.Append(c);
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "il" : slug;
    }

    public static string Email(string provinceSlug, TeacherSeedPlan.BranchInfo branch, int ordinal)
        => $"{EmailLocalPrefix}{provinceSlug}.{branch.Slug}.{ordinal.ToString(CultureInfo.InvariantCulture)}@{EmailDomain}";

    /// <summary>E-postadan il slug'ını geri çıkarır (rapor için): <c>seed.i.&lt;il&gt;.&lt;brans&gt;.&lt;n&gt;@…</c>; desen dışıysa null.</summary>
    public static string? ProvinceSlugFromEmail(string? email)
    {
        if (string.IsNullOrEmpty(email) || !email.StartsWith(EmailLocalPrefix, StringComparison.Ordinal)) return null;
        var at = email.IndexOf('@');
        if (at < 0) return null;
        var parts = email[EmailLocalPrefix.Length..at].Split('.');
        // <il>.<brans>.<n> — il slug'ı tire içerebilir ama nokta içermez.
        return parts.Length >= 3 ? parts[0] : null;
    }

    public static bool IsTutorSeedEmail(string? email)
        => !string.IsNullOrEmpty(email) && email.StartsWith(EmailLocalPrefix, StringComparison.Ordinal) && email.EndsWith("@" + EmailDomain, StringComparison.Ordinal);

    /// <summary>Öğrenci aramasında (teacher/search) görünmesi için makul, deterministik tutor profili.</summary>
    public sealed record TutorProfileDefaults(decimal HourlyRate, bool TeachesOnline, bool TeachesInPerson, string Bio);

    /// <summary>
    /// Saatlik ücret 250..900 (50 adım), online her zaman açık (arama filtresi <c>Online=true</c> herkesi bulsun),
    /// yüz yüze hash'e göre ~%50, kısa tanıtım metni. Aynı e-posta her koşuda aynı profili verir.
    /// </summary>
    public static TutorProfileDefaults ProfileFor(string email, string provinceName, TeacherSeedPlan.BranchInfo branch)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("tutor:" + email));
        var rate = 250 + (int)(BitConverter.ToUInt32(hash, 0) % 14) * 50; // 250, 300, …, 900
        var inPerson = (hash[4] & 1) == 1;
        var bio = $"{branch.SubjectName} dersi veren bağımsız öğretmen ({provinceName}). Seed test hesabı — gerçek bir kişi değildir.";
        if (bio.Length > BioMaxLength) bio = bio[..BioMaxLength];
        return new TutorProfileDefaults(rate, TeachesOnline: true, TeachesInPerson: inPerson, bio);
    }

    /// <summary>Branş slug'ından BranchInfo (rapor/e-posta çözümleme); yoksa null.</summary>
    public static TeacherSeedPlan.BranchInfo? BranchFromSlug(string slug)
        => TeacherSeedPlan.Branches.FirstOrDefault(b => b.Slug == slug);
}
