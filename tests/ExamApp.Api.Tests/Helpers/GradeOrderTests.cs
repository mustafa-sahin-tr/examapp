using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>Issue #1159: sınıflar Id değil okul sırasıyla listelenir (okul öncesi, 1–8, hazırlık, 9–12).</summary>
public class GradeOrderTests
{
    [Fact]
    public void Grades_follow_the_school_order_not_their_ids()
    {
        var grades = new (int Id, string? Code, string Name)[]
        {
            (18, "1-sinif", "1. Sınıf"),
            (22, "5-sinif", "5. Sınıf"),
            (26, "9-sinif", "9. Sınıf"),
            (29, "12-sinif", "12. Sınıf"),
            (31, null, "Hazırlık"),
            (32, "okul-oncesi", "Okul Öncesi"),
            (33, null, "Mezun"),
            (25, "8-sinif", "8. Sınıf"),
        };

        var ordered = GradeOrder.Sort(grades, g => g.Code, g => g.Name, g => g.Id).Select(g => g.Name);

        ordered.ShouldBe(["Okul Öncesi", "1. Sınıf", "5. Sınıf", "8. Sınıf", "Hazırlık", "9. Sınıf", "12. Sınıf", "Mezun"]);
    }

    [Fact]
    public void Same_level_keeps_id_order()
    {
        var grades = new (int Id, string Name)[] { (40, "5. Sınıf (yeni)"), (22, "5. Sınıf") };

        GradeOrder.Sort(grades, _ => null, g => g.Name, g => g.Id).Select(g => g.Id).ShouldBe([22, 40]);
    }
}
