namespace ExamApp.Api.Data;

/// <summary>
/// issue #105: ilgili (sorumlu) öğretmenin hangi kuraldan geldiği (öncelik sırasıyla). issue #326: yorumda
/// <see cref="WorksheetComment.ResponsibleTeacherSource"/> olarak da saklanır (DB'de string — üye sırası değişse de geçmiş
/// satırların anlamı kaymasın).
/// </summary>
public enum ResponsibleTeacherSource
{
    /// <summary>Öğrencinin ilgili aktif ataması var → atamayı yapan (<c>WorksheetAssignment.CreateUserId</c>). Her zaman öncelikli.</summary>
    Assignment = 0,

    /// <summary>
    /// Aktif atama yok, worksheet kopya (<c>SourceWorksheetId</c> dolu) → kopyalayan (kopyanın <c>CreateUserId</c>'si) —
    /// issue #326: YALNIZ kopyalayan öğrenciyle aynı okuldaysa.
    /// </summary>
    CopyOwner = 1,

    /// <summary>
    /// Aktif atama yok, worksheet kopya değil → worksheet'in <c>CreateUserId</c>'si (orijinal yaratıcı) — issue #326: YALNIZ
    /// sahip öğrenciyle aynı okuldaysa.
    /// </summary>
    Owner = 2
}
