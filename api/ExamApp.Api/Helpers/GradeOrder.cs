using ExamApp.Api.Services.Catalog;
using ExamApp.Api.Services.TargetGoals;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Issue #1159: sınıf listelerinin ortak sırası — okul öncesi, 1–8, hazırlık, 9–12, sonra tanınmayanlar. Id sırası tutmaz:
/// "Okul Öncesi" ve "Hazırlık" sonradan eklendi (Id 31–33) ve "12. Sınıf"tan sonra çıkıyordu.
/// </summary>
public static class GradeOrder
{
    private const int Unknown = 1000;

    /// <summary>Sıralama anahtarı; ad (kod yoksa) Türkçe harfler sadeleştirilerek okunur.</summary>
    public static int KeyOf(string? code, string? name)
    {
        var level = TargetGoalService.GradeLevelOf(code) ?? TargetGoalService.GradeLevelOf(name);
        if (level is { } n)
            return n * 10;

        var slug = CatalogCode.Slug($"{code} {name}");
        if (slug.Contains("okul-oncesi"))
            return 0;
        if (slug.Contains("hazirlik"))
            return 85;
        return Unknown;
    }

    public static IEnumerable<T> Sort<T>(IEnumerable<T> grades, Func<T, string?> code, Func<T, string?> name, Func<T, int> id) =>
        grades.OrderBy(g => KeyOf(code(g), name(g))).ThenBy(id);
}
