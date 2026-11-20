using System;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Kitap aktarımında testin başlığında süre yazmıyorsa varsayılan süre: soru sayısı × soru başına süre (yukarı yuvarlanır).
/// Kurulum ekranında değiştirilebilir. Ölçüt merkezi sınavlar (2026-11-20 araştırması, docs/book-import.md § Varsayılan test süresi):
/// LGS sözel 50 soru 75 dk (1,5), sayısal 40 soru 80 dk (2); TYT 120 soru 165 dk (~1,4). İlkokulda merkezi sınav yok: okuma
/// hızı düşük olduğundan 2 dk.
/// </summary>
public static class BookTestDurations
{
    /// <summary>Sayısal dersler (LGS sayısal oturumu ölçüsü): Subject.Code.</summary>
    private static readonly string[] NumericSubjects = ["matematik", "fen-bilimleri", "fizik", "kimya", "biyoloji", "geometri"];

    /// <summary>İlkokul ve okul öncesi: Grade.Code.</summary>
    private static readonly string[] PrimaryGrades = ["okul-oncesi", "1-sinif", "2-sinif", "3-sinif", "4-sinif"];

    public const double PrimarySecondsPerQuestion = 120;
    public const double VerbalSecondsPerQuestion = 90;
    public const double NumericSecondsPerQuestion = 120;

    /// <summary>Soru başına saniye; sınıf ya da ders bilinmiyorsa sözel ölçü.</summary>
    public static double SecondsPerQuestion(string? gradeCode, string? subjectCode) =>
        gradeCode is not null && Array.IndexOf(PrimaryGrades, gradeCode) >= 0 ? PrimarySecondsPerQuestion
        : subjectCode is not null && Array.IndexOf(NumericSubjects, subjectCode) >= 0 ? NumericSecondsPerQuestion
        : VerbalSecondsPerQuestion;

    /// <summary>Varsayılan dakika; soru sayısı bilinmiyorsa null (süresiz kalır).</summary>
    public static int? DefaultMinutes(string? gradeCode, string? subjectCode, int expectedQuestions) =>
        expectedQuestions <= 0
            ? null
            : Math.Clamp((int)Math.Ceiling(expectedQuestions * SecondsPerQuestion(gradeCode, subjectCode) / 60), 1, 600);
}
