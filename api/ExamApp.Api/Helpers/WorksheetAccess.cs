using System;
using System.Linq;
using ExamApp.Api.Data;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Worksheet yetki modeli: "sahibi VEYA admin".
/// Legacy kayıtlar (CreateUserId null veya 0) owner sayılmaz.
/// </summary>
public static class WorksheetAccess
{
    public static bool CanModify(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null)
    {
        // TODO(#11/#12/#13): sharing == PublicAssignable dalı burada ele alınacak.
        return isAdmin || (createUserId.HasValue && createUserId.Value > 0 && createUserId.Value == userId);
    }

    /// <summary>
    /// Bir worksheet atanabilir mi (issue #12/#13). Sahibi/admin her zaman atayabilir (CanModify ile aynı);
    /// ayrıca worksheet'in bir sahibi varsa ve TeacherSharing=PublicAssignable ise <em>veya</em> bu öğretmen
    /// için onaylı bir atama izni (aktif <c>WorksheetAccessGrant</c>) varsa, sahibi olmayan bir öğretmen de
    /// atayabilir. Düzenleme yetkisi (<see cref="CanModify"/>) bundan ayrıdır — PublicAssignable veya onaylı
    /// grant atama izni verir, düzenleme izni vermez.
    /// </summary>
    public static bool CanAssign(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null, bool hasApprovedGrant = false)
    {
        if (isAdmin || (createUserId.HasValue && createUserId.Value > 0 && createUserId.Value == userId))
            return true;

        var hasOwner = createUserId.HasValue && createUserId.Value > 0;
        return hasOwner && (sharing == WorksheetTeacherSharing.PublicAssignable || hasApprovedGrant);
    }

    /// <summary>
    /// Öğretmen bir worksheet'i görüntüleyebilir mi (liste/detay/popüler).
    /// Sahibi veya admin her zaman görebilir; ayrıca TeacherSharing PublicView/PublicAssignable
    /// ise herhangi bir kimliği doğrulanmış öğretmen de görüntüleyebilir (issue #11).
    /// Düzenleme yetkisi bundan ayrıdır — bkz. <see cref="CanModify"/>.
    /// </summary>
    public static bool CanView(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null,
        WorksheetStudentVisibility? studentVisibility = null)
    {
        // TODO(#13): studentVisibility == Restricted dalı burada ele alınacak.
        if (isAdmin || (createUserId.HasValue && createUserId.Value > 0 && createUserId.Value == userId))
            return true;

        // Legacy (owner'sız) worksheet'ler PublicView/PublicAssignable işaretlenmiş olsa bile
        // görünür sayılmaz — sadece admin erişebilir. Aksi halde varlığı 403 ile sızdırılır.
        var hasOwner = createUserId.HasValue && createUserId.Value > 0;
        return hasOwner &&
            (sharing == WorksheetTeacherSharing.PublicView
                || sharing == WorksheetTeacherSharing.PublicAssignable);
    }

    /// <summary>
    /// Öğretmen bir public sınavı kendi hesabına kopyalayabilir mi (issue #16).
    /// Kaynak PublicView/PublicAssignable olmalı; sahibi/admin her durumda.
    /// Düzenleme yetkisi (<see cref="CanModify"/>) bundan ayrı — kopya yeni bir worksheet olur,
    /// kopyalayan onun sahibidir.
    /// </summary>
    public static bool CanCopy(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null,
        WorksheetStudentVisibility? studentVisibility = null)
    {
        return CanView(createUserId, userId, isAdmin, sharing, studentVisibility);
    }

    /// <summary>
    /// Öğrenci bir testi başlatabilir mi (issue #14). Öğretmen sahiplik/paylaşım ekseniyle
    /// (<see cref="CanView"/>, <see cref="CanModify"/>) ilgisi yok — tamamen ayrı bir kural:
    /// ya öğrenciye/sınıfına aktif bir atama olmalı, ya da sınav "keşfedilebilir" olmalı
    /// (grade uyumlu + StudentVisibility=Normal). TeacherSharing bu kararı etkilemez.
    /// </summary>
    public static bool CanStudentStartTest(bool hasActiveAssignment, bool isGradeMatch,
        WorksheetStudentVisibility studentVisibility)
    {
        if (hasActiveAssignment)
            return true;

        return isGradeMatch && studentVisibility == WorksheetStudentVisibility.Normal;
    }

    /// <summary>
    /// Bir öğrenciye/sınıfına şu an aktif olan (StartAt/EndAt penceresi içindeki) atamalar.
    /// "Aktif atama" tanımı tek yerde tutulur — <see cref="CanStudentStartTest"/> ile kullanılan
    /// öğrencinin test başlatabilme koşulu, görünürlük filtresi (issue #14) ve "IsAssigned"
    /// hesaplaması hepsi buradan beslenir; tanım sadece burada değişir.
    /// IQueryable döner ki EF Core SQL'e çevirebilsin — bool döndüren
    /// <see cref="CanStudentStartTest"/> ile karıştırma, o bellek-içi bir karardır.
    /// </summary>
    public static IQueryable<WorksheetAssignment> ActiveAssignmentsFor(
        this AppDbContext context, int studentId, int? gradeId, DateTime now)
    {
        return context.WorksheetAssignments.Where(a =>
            (a.StudentId == studentId
                || (a.StudentId == null && a.GradeId != null && a.GradeId == gradeId))
            && a.StartAt <= now && (a.EndAt == null || a.EndAt > now));
    }
}
