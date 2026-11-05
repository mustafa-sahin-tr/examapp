using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>Issue #703: TYMM kavramsal beceri katmanı. KB1 temel becerilerin kapalı listesi yok, etiket olarak alınmadı.</summary>
public enum SkillCategory
{
    /// <summary>KB2 bütünleşik beceriler.</summary>
    Integrated = 2,

    /// <summary>KB3 üst düzey düşünme becerileri.</summary>
    HigherOrder = 3,
}

/// <summary>
/// Issue #703: soruya etiketlenen beceri. Liste <see cref="SkillSeed"/> içinde MEB bağlam temelli soru yazım kılavuzundan alınmıştır
/// (bkz. docs/context-based-questions.md). Kod kalıcıdır; soru aktarımı ve ekran çevirisi kodla eşleşir.
/// </summary>
public class Skill : BaseEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 100;

    public int Id { get; set; }

    /// <summary>TYMM kodu, ör. <c>KB2.8</c>.</summary>
    [MaxLength(CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    public SkillCategory Category { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

/// <summary>Issue #703: soru ile beceri bağı (çoka çok). Etiket öğretmenin beyanıdır.</summary>
public class QuestionSkill : BaseEntity
{
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public Question Question { get; set; } = default!;

    public int SkillId { get; set; }
    public Skill Skill { get; set; } = default!;
}

/// <summary>
/// Issue #703: TYMM Bağlam Temelli Çoktan Seçmeli Soru Yazım Kılavuzu (MEB, Mart 2026, ISBN 978-975-11-9562-3), bölüm 2.2.1.
/// Kodlar kılavuzdaki sıraya göredir; örnek sorulardaki KB2.3, KB2.8, KB2.14 ve KB3.2 ile doğrulandı.
/// </summary>
public static class SkillSeed
{
    private static readonly DateTime SeedTime = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Integrated =
    [
        "Çelişki giderme", "Gözlemleme", "Özetleme", "Çözümleme", "Sınıflandırma", "Bilgi toplama", "Karşılaştırma", "Sorgulama",
        "Genelleme", "Çıkarım yapma", "Gözleme dayalı tahmin etme", "Mevcut bilgiye/veriye dayalı tahmin etme", "Yapılandırma",
        "Yorumlama", "Yansıtma", "Muhakeme (akıl yürütme)", "Değerlendirme", "Tartışma", "Mantıksal denetleme", "Sentezleme",
    ];

    private static readonly string[] HigherOrder = ["Karar verme", "Problem çözme", "Eleştirel düşünme"];

    /// <summary>Kimlikler sabittir: KB2.n → n, KB3.n → 100 + n.</summary>
    public static IEnumerable<Skill> All =>
        Integrated.Select((name, i) => new Skill
            {
                Id = i + 1, Code = $"KB2.{i + 1}", Category = SkillCategory.Integrated, Name = name, SortOrder = i + 1, CreateTime = SeedTime,
            })
            .Concat(HigherOrder.Select((name, i) => new Skill
            {
                Id = 100 + i + 1, Code = $"KB3.{i + 1}", Category = SkillCategory.HigherOrder, Name = name, SortOrder = 100 + i + 1, CreateTime = SeedTime,
            }));

    public static void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Skill>().HasData(All);
}
