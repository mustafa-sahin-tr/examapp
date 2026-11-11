using System;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1110 (epic #1118): 1 Ağustos sınıf atlaması. Öğrenci + yıl başına tek satır: (StudentId, Year) tekil indeksi job'ı
/// idempotent yapar (aynı yıl ikinci tur hiçbir şey yapmaz). Admin "sınıfta kaldı" derse <see cref="RevertedAtUtc"/> dolar ve
/// öğrenci <see cref="FromGradeId"/>'ye döner; satır kalır, o yıl yeniden atlatılmaz. <see cref="BaseEntity"/>'den türemez, silinmez.
/// </summary>
public class StudentGradePromotion
{
    public long Id { get; set; }

    public int StudentId { get; set; }

    /// <summary>Atlamanın yılı (1 Ağustos'un yılı).</summary>
    public int Year { get; set; }

    public int FromGradeId { get; set; }
    public int ToGradeId { get; set; }
    public DateTime PromotedAtUtc { get; set; }

    public DateTime? RevertedAtUtc { get; set; }

    /// <summary>Geri alan admin'in exam/auth user id'si.</summary>
    public int? RevertedByUserId { get; set; }
}
