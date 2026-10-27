namespace ExamApp.Api.Data;

/// <summary>
/// Sorunun ders/konu/alt konu bilgisinin kaynağı. DB'de int saklanır (sıra değişmez; yeni üye sona eklenir).
/// </summary>
public enum ClassificationSource
{
    Human,
    AI,

    /// <summary>
    /// Issue #577: kitabın içindekiler eşlemesinden (<see cref="BookTocEntry"/>) geldi. Sınıflandırıcı alt konu varsa
    /// atlar; yalnız konu varsa (Hybrid) yalnız alt konu + zorluğu o konuyla sınırlı belirler, kaynak Book kalır.
    /// </summary>
    Book
}
