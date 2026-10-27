namespace ExamApp.Api.Data;

/// <summary>
/// Issue #580: <c>AddAnswerKeyAndSolutionApproval</c> migration'ının veri adımı. Yeni kolonlar <c>SolutionSource = 'None'</c>,
/// <c>SolutionStatus = 'Approved'</c> varsayılanıyla eklenir; çözümü olan etkileşimli (MCQ dışı) mevcut sorular öğretmen yazdığı için
/// <c>Human</c> yapılır (onay beklemez). SQL, testlerin SQLite'ında da koşabilsin diye taşınabilir (<c>lower</c>, <c>trim</c>,
/// <c>COALESCE</c>). Migration aynı metni kendi içinde taşır (<c>AddAnswerKeyAndSolutionApproval.BackfillSql</c>); eşitlik testle
/// denetlenir. Değiştirmeyin; yeni bir veri adımı gerekirse yeni migration açın.
/// </summary>
public static class QuestionSolutionBackfill
{
    public const string Sql =
        "UPDATE \"Questions\" SET \"SolutionSource\" = 'Human', \"SolutionStatus\" = 'Approved' " +
        "WHERE \"InteractionSolution\" IS NOT NULL AND trim(\"InteractionSolution\") <> '' " +
        "AND lower(COALESCE(\"InteractionType\", 'mcq')) <> 'mcq' AND trim(COALESCE(\"InteractionType\", '')) <> '';";
}
