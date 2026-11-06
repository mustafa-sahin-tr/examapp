namespace ExamApp.Api.Data;

/// <summary>
/// Issue #826: testten bağımsız (havuza kaydedilmiş) sorunun öğretmen havuzunda kime görüneceği. Testteki soru zaten testin
/// <see cref="WorksheetTeacherSharing"/>'iyle görünür; bu eksen ona ek bir yoldur, düzenleme yetkisi vermez.
/// DB'de ve API'de int.
/// </summary>
public enum QuestionBankSharing
{
    /// <summary>Yalnız sahibi (ve admin).</summary>
    Private = 0,

    /// <summary>Sahibin okulundaki öğretmenler (sahibin <c>Teachers.SchoolId</c>'si). Okulsuz sahip seçemez (400).</summary>
    School = 1,

    /// <summary>Bütün öğretmenler.</summary>
    Everyone = 2,
}
