using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #693: resmî kılavuzlardan alınan sınav yapıları.
/// LGS: MEB, Merkezî Sınav Başvuru ve Uygulama Kılavuzu 2026 (s. 9 oturumlar, s. 13 ham puan). YKS: ÖSYM, 2026-YKS Kılavuzu (Tablo 1A, 1B; yanlışın dörtte biri).
/// Din muafiyeti seçenekleri (LGS 60 dk, TYT ve AYT ilave felsefe soruları) kapsam dışıdır.
/// </summary>
public static class ExamTemplateSeed
{
    public const int LgsId = 1;
    public const int TytId = 2;
    public const int AytId = 3;

    private const string LgsSource = "T.C. MEB, Sınavla Öğrenci Alacak Ortaöğretim Kurumlarına İlişkin Merkezî Sınav Başvuru ve Uygulama Kılavuzu, 2026";
    private const string YksSource = "T.C. ÖSYM, 2026 Yükseköğretim Kurumları Sınavı (YKS) Kılavuzu, Tablo 1A ve 1B";
    private static readonly DateTime SeedTime = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Section(string? Group, string Name, int Count);

    private sealed record Session(int Id, int TemplateId, int Order, string Name, int Minutes, Section[] Sections);

    private static readonly Session[] Sessions =
    [
        new(1, LgsId, 1, "Sözel Bölüm", 75,
        [
            new(null, "Türkçe", 20),
            new(null, "T.C. İnkılap Tarihi ve Atatürkçülük", 10),
            new(null, "Din Kültürü ve Ahlak Bilgisi", 10),
            new(null, "Yabancı Dil", 10),
        ]),
        new(2, LgsId, 2, "Sayısal Bölüm", 80,
        [
            new(null, "Matematik", 20),
            new(null, "Fen Bilimleri", 20),
        ]),
        new(3, TytId, 1, "Temel Yeterlilik Testi", 165,
        [
            new(null, "Türkçe", 40),
            new("Sosyal Bilimler", "Tarih", 5),
            new("Sosyal Bilimler", "Coğrafya", 5),
            new("Sosyal Bilimler", "Felsefe", 5),
            new("Sosyal Bilimler", "Din Kültürü ve Ahlak Bilgisi", 5),
            new(null, "Temel Matematik", 40),
            new("Fen Bilimleri", "Fizik", 7),
            new("Fen Bilimleri", "Kimya", 7),
            new("Fen Bilimleri", "Biyoloji", 6),
        ]),
        new(4, AytId, 1, "Alan Yeterlilik Testleri", 180,
        [
            new("Türk Dili ve Edebiyatı-Sosyal Bilimler-1", "Türk Dili ve Edebiyatı", 24),
            new("Türk Dili ve Edebiyatı-Sosyal Bilimler-1", "Tarih-1", 10),
            new("Türk Dili ve Edebiyatı-Sosyal Bilimler-1", "Coğrafya-1", 6),
            new("Sosyal Bilimler-2", "Tarih-2", 11),
            new("Sosyal Bilimler-2", "Coğrafya-2", 11),
            new("Sosyal Bilimler-2", "Felsefe Grubu", 12),
            new("Sosyal Bilimler-2", "Din Kültürü ve Ahlak Bilgisi", 6),
            new(null, "Matematik", 40),
            new("Fen Bilimleri", "Fizik", 14),
            new("Fen Bilimleri", "Kimya", 13),
            new("Fen Bilimleri", "Biyoloji", 13),
        ]),
    ];

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExamTemplate>().HasData(
            new ExamTemplate { Id = LgsId, Code = "lgs", Name = "LGS", WrongAnswersPerCorrect = 3, SourceReference = LgsSource, CreateTime = SeedTime },
            new ExamTemplate { Id = TytId, Code = "tyt", Name = "YKS TYT", WrongAnswersPerCorrect = 4, SourceReference = YksSource, CreateTime = SeedTime },
            new ExamTemplate { Id = AytId, Code = "ayt", Name = "YKS AYT", WrongAnswersPerCorrect = 4, SourceReference = YksSource, CreateTime = SeedTime });

        modelBuilder.Entity<ExamTemplateSession>().HasData(Sessions.Select(s => new ExamTemplateSession
        {
            Id = s.Id,
            ExamTemplateId = s.TemplateId,
            Order = s.Order,
            Name = s.Name,
            DurationSeconds = s.Minutes * 60,
            CreateTime = SeedTime,
        }).ToArray());

        var sectionId = 0;
        modelBuilder.Entity<ExamTemplateSection>().HasData(Sessions.SelectMany(session => session.Sections.Select((section, index) => new ExamTemplateSection
        {
            Id = ++sectionId,
            ExamTemplateSessionId = session.Id,
            Order = index + 1,
            GroupName = section.Group,
            Name = section.Name,
            QuestionCount = section.Count,
            CreateTime = SeedTime,
        })).ToArray());
    }
}
