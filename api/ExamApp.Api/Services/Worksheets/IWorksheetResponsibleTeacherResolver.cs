using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>issue #105: ilgili öğretmenin hangi kuraldan geldiği (öncelik sırasıyla).</summary>
public enum ResponsibleTeacherSource
{
    /// <summary>Öğrencinin ilgili aktif ataması var → atamayı yapan (<c>WorksheetAssignment.CreateUserId</c>). Her zaman öncelikli.</summary>
    Assignment = 0,

    /// <summary>Aktif atama yok, worksheet kopya (<c>SourceWorksheetId</c> dolu) → kopyalayan (kopyanın <c>CreateUserId</c>'si).</summary>
    CopyOwner = 1,

    /// <summary>Aktif atama yok, worksheet kopya değil → worksheet'in <c>CreateUserId</c>'si (orijinal yaratıcı).</summary>
    Owner = 2
}

/// <summary>
/// Bir öğrencinin bir worksheet'teki yorum thread'leri için ilgili öğretmen. <see cref="AssignmentId"/> yalnızca
/// <see cref="ResponsibleTeacherSource.Assignment"/> için dolu.
/// </summary>
public sealed record ResponsibleTeacher(int TeacherUserId, ResponsibleTeacherSource Source, int? AssignmentId);

/// <summary>
/// issue #105: öğrencinin worksheet'teki "ilgili aktif ataması" + ilgili öğretmen tespiti — TEK kaynak. Yorum yazma yetkisi
/// (öğretmen cevabı, etkin CommentsEnabled) ve dilim 2'deki bildirim hedefi aynı sonucu kullanır.
/// <para>
/// İlgili aktif atama: öğrencinin <see cref="ExamApp.Api.Helpers.WorksheetAccess.ActiveAssignmentsFor"/> kümesindeki bu
/// worksheet'e ait atamalardan öğrenci hedefli olan sınıf hedefliden önce, sonra en yeni <c>StartAt</c>, sonra en büyük Id.
/// </para>
/// <para>
/// İlgili öğretmen önceliği: (1) ilgili aktif atamanın <c>CreateUserId</c>'si (legacy 0/null ise atlanır),
/// (2) worksheet kopyaysa kopyanın <c>CreateUserId</c>'si, (3) worksheet'in <c>CreateUserId</c>'si. Kaynak worksheet'in
/// sahibi hiçbir zaman seçilmez. Hiçbiri yoksa (legacy sahipsiz worksheet) null. Retire (soft-delete) edilmiş worksheet
/// için de çözülür — thread'ler görünür kalır, cevap yetkisi sürer.
/// </para>
/// </summary>
public interface IWorksheetResponsibleTeacherResolver
{
    /// <param name="studentUserId">Öğrencinin user id'si (<c>Student.UserId</c>, yorumun <c>AuthorUserId</c>'si) — Student.Id değil.</param>
    Task<ResponsibleTeacher?> ResolveResponsibleTeacherAsync(int worksheetId, int studentUserId, CancellationToken ct = default);

    /// <summary>
    /// Çağıran worksheet'i zaten yüklediyse (yorum servisi, dilim 2 bildirim akışı) worksheet sorgusu atlanır.
    /// <paramref name="worksheet"/> retire (soft-delete) edilmiş olabilir.
    /// </summary>
    Task<ResponsibleTeacher?> ResolveResponsibleTeacherAsync(ResponsibleTeacherWorksheet worksheet, int studentUserId, CancellationToken ct = default);

    /// <summary>
    /// <see cref="ResolveResponsibleTeacherAsync"/>'in toplu hali (thread listesi): worksheet ve aktif atamaları TEK sefer,
    /// öğrenciler tek sorguda çekilir. Sözlükte her istenen user id için bir kayıt vardır (öğretmen yoksa değer null).
    /// </summary>
    Task<IReadOnlyDictionary<int, ResponsibleTeacher?>> ResolveResponsibleTeachersAsync(
        int worksheetId, IReadOnlyCollection<int> studentUserIds, CancellationToken ct = default);

    /// <summary>Toplu çözüm, worksheet bilgisi çağırandan.</summary>
    Task<IReadOnlyDictionary<int, ResponsibleTeacher?>> ResolveResponsibleTeachersAsync(
        ResponsibleTeacherWorksheet worksheet, IReadOnlyCollection<int> studentUserIds, CancellationToken ct = default);

    /// <summary>
    /// Öğrencinin bu worksheet'teki ilgili aktif ataması (yoksa null). Etkin yorum ayarı
    /// <c>CommentsEnabledOverride ?? Worksheet.CommentsEnabled</c> buradan hesaplanır.
    /// </summary>
    Task<RelevantAssignment?> FindRelevantActiveAssignmentAsync(
        int worksheetId, int studentId, int? gradeId, int? schoolId, CancellationToken ct = default);
}

/// <summary>İlgili öğretmen kararı için worksheet alanları (çağıranın yüklediği worksheet'ten).</summary>
public sealed record ResponsibleTeacherWorksheet(int Id, int? CreateUserId, int? SourceWorksheetId);

/// <summary>Öğrencinin ilgili aktif atamasının karar için gereken alanları.</summary>
public sealed record RelevantAssignment(int Id, int? CreateUserId, bool? CommentsEnabledOverride);
