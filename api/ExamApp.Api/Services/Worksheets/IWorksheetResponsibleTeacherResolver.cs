using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Bir öğrencinin bir worksheet'teki yorum thread'leri için ilgili öğretmen. <see cref="AssignmentId"/> yalnızca
/// <see cref="ResponsibleTeacherSource.Assignment"/> için dolu.
/// </summary>
public sealed record ResponsibleTeacher(int TeacherUserId, ResponsibleTeacherSource Source, int? AssignmentId);

/// <summary>
/// issue #105: öğrencinin worksheet'teki "ilgili aktif ataması" + ilgili (sorumlu) öğretmen tespiti — TEK kaynak. Yorum yazma
/// (sorumlu öğretmen sabitleme), öğretmen cevabı, okul kapsamı ve bildirim hedefi aynı sonucu kullanır.
/// <para>
/// İlgili aktif atama: öğrencinin <see cref="ExamApp.Api.Helpers.WorksheetAccess.ActiveAssignmentsFor"/> kümesindeki bu
/// worksheet'e ait atamalardan öğrenci hedefli olan sınıf hedefliden önce, sonra en yeni <c>StartAt</c>, sonra en büyük Id.
/// </para>
/// <para>
/// İlgili öğretmen önceliği (issue #326 O2, PO kararı c) — kural <see cref="ResponsibleTeacherRule"/>'da:
/// (1) ilgili aktif atamanın <c>CreateUserId</c>'si (legacy 0/null ise atlanır) — issue #334: YALNIZ atayanın güncel okulu
/// öğrencinin okuluyla aynıysa ya da ikisi de okulsuzsa (<see cref="WorksheetCommentPinRule"/>); değilse (2)'ye geçilir;
/// (2) atama yoksa worksheet'in <c>CreateUserId</c>'si (kopyada kopyalayan, değilse orijinal yaratıcı) — YALNIZ bu öğretmenin
/// okulu öğrencinin okuluyla aynıysa (<see cref="UserSchoolResolver.SameSchool"/>; okulsuz taraf için <c>null == null</c> aynı
/// okul SAYILMAZ). Aksi halde sorumlu öğretmen YOKTUR (null): yorum yalnız okul içinde görünür, sahibe gösterilmez ve bildirim
/// gitmez. Kaynak worksheet'in sahibi hiçbir zaman seçilmez; legacy sahipsiz worksheet → null. Retire (soft-delete) edilmiş
/// worksheet için de çözülür. Okullar <see cref="UserSchoolResolver"/>'dan (D3, tek kaynak).
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
    /// <see cref="ResolveResponsibleTeacherAsync(int,int,CancellationToken)"/>'in toplu hali (thread listesi): worksheet ve aktif
    /// atamaları TEK sefer, öğrenciler ve okullar toplu çekilir. Sözlükte her istenen user id için bir kayıt vardır (öğretmen
    /// yoksa değer null).
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

/// <summary>
/// issue #326 (O2): sorumlu öğretmen kuralının saf (sorgusuz) hali — resolver ve yorum servisinin erişim bağlamı (atamayı ve
/// okulları zaten çözmüş olan) AYNI kararı buradan alır.
/// </summary>
public static class ResponsibleTeacherRule
{
    /// <param name="worksheet">Worksheet (kopyada sahip = kopyalayan).</param>
    /// <param name="relevantAssignment">Öğrencinin ilgili aktif ataması; yoksa null.</param>
    /// <param name="ownerSchoolId">Worksheet sahibinin <see cref="UserSchoolResolver"/> okulu.</param>
    /// <param name="studentSchoolId">Öğrencinin <see cref="UserSchoolResolver"/> okulu.</param>
    /// <param name="assignmentTeacherSchoolId">
    /// issue #334: ilgili aktif atamayı yapan öğretmenin GÜNCEL <see cref="UserSchoolResolver"/> okulu (atama yoksa yok sayılır).
    /// </param>
    /// <remarks>
    /// issue #334: atamayı yapan da okul koşuluna (<see cref="WorksheetCommentPinRule.SchoolAllows"/>) tabidir — aynı okul ya
    /// da ikisi de okulsuz (bağımsız istisnası). Sağlanmazsa (öğrenci başka okula geçti / öğretmen taşındı) atayan sorumlu
    /// OLMAZ ve kural atama yokmuş gibi sahip fallback'ine geçer (sahip de yalnız aynı okuldaysa). Atama yine etkin yorum
    /// ayarını (<see cref="RelevantAssignment.CommentsEnabledOverride"/>) belirler — bu karar yalnız sorumlu öğretmen içindir.
    /// </remarks>
    public static ResponsibleTeacher? Decide(ResponsibleTeacherWorksheet worksheet, RelevantAssignment? relevantAssignment,
        int? ownerSchoolId, int? studentSchoolId, int? assignmentTeacherSchoolId)
    {
        if (relevantAssignment is { CreateUserId: > 0 } assignment
            && WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, assignmentTeacherSchoolId, studentSchoolId))
            return new ResponsibleTeacher(assignment.CreateUserId!.Value, ResponsibleTeacherSource.Assignment, assignment.Id);

        if (worksheet.CreateUserId is not > 0)
            return null;

        var source = worksheet.SourceWorksheetId.HasValue ? ResponsibleTeacherSource.CopyOwner : ResponsibleTeacherSource.Owner;
        return WorksheetCommentPinRule.SchoolAllows(source, ownerSchoolId, studentSchoolId)
            ? new ResponsibleTeacher(worksheet.CreateUserId.Value, source, null)
            : null;
    }
}
