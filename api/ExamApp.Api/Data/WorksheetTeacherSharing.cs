namespace ExamApp.Api.Data;

/// <summary>
/// Öğretmenler arası paylaşım ekseni. Bu issue'da (#9) yalnızca taşınır;
/// PublicView / PublicAssignable yetki dalları #11/#12/#13'te aktifleşir.
/// DB'de int saklanır (Program.cs'te string enum converter yok → API sözleşmesi de int).
/// </summary>
public enum WorksheetTeacherSharing
{
    Private = 0,
    PublicView = 1,
    PublicAssignable = 2,

    /// <summary>
    /// issue #191: yalnızca sahibin okulundaki öğretmenlere açık; onlar için davranış
    /// <see cref="PublicAssignable"/> ile birebir aynı (görür/atar/kopyalar). Farklı okul veya
    /// okulsuz öğretmen için Private gibi görünmez. Okulsuz sahip bu değeri seçemez (API 400).
    /// </summary>
    SchoolOnly = 3
}
