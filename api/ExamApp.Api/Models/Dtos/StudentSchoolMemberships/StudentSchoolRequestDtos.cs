using System;

namespace ExamApp.Api.Models.Dtos.StudentSchoolMemberships;

/// <summary>
/// issue #361: onay bekleyen öğrenci okul üyeliği (öğrencinin kendi kaydında seçtiği okul). Liste yalnız onaylayıcının
/// görebileceği okul(lar)dan gelir — öğretmen yalnız kendi (doğrulanmış) okulunu, admin tümünü. Veri minimizasyonu: e-posta
/// ve kullanıcı id'si dönmez; öğrenci numarası kısmi (#262 <c>StudentNumberMask</c>, son 4 karakter); karar için ad, numara
/// sonu ve sınıf yeterli.
/// </summary>
public sealed class StudentSchoolRequestDto
{
    /// <summary>Student.Id — onay/ret uçlarının yol parametresi.</summary>
    public int StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string StudentNumber { get; init; } = string.Empty;
    public int? GradeId { get; init; }
    public string? GradeName { get; init; }
    public int SchoolId { get; init; }
    public string SchoolName { get; init; } = string.Empty;
    /// <summary>Öğrenci kaydının oluşturulma anı (UTC) — sıralama ve "ne zamandır bekliyor" bilgisi.</summary>
    public DateTime RegisteredAt { get; init; }
}

/// <summary>issue #361: <c>GET student-school-requests/count</c> yanıtı (menü rozeti).</summary>
public sealed class StudentSchoolRequestCountDto
{
    public int Count { get; init; }
}
