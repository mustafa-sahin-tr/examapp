using System;
using System.Linq;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Worksheet yetki modeli: "sahibi VEYA admin".
/// Legacy kayıtlar (CreateUserId null veya 0) owner sayılmaz.
/// <para>
/// issue #191: <see cref="WorksheetTeacherSharing.SchoolOnly"/> için "istek sahibinin okulu" ve
/// "sahibin okulu" bu sınıfa girdi olarak verilir (<c>requesterSchoolId</c> / <c>ownerSchoolId</c>);
/// kural tek yerde: iki okul da dolu ve eşitse aynı-okul öğretmeni PublicAssignable ile aynı hakları
/// alır, aksi halde (farklı okul, okulsuz istekçi, okulsuz sahip) worksheet Private gibi görünmez.
/// Bu sınıf saf/DB'siz; okul değerleri <see cref="WorksheetSchoolContext"/> ile çözülür.
/// </para>
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
    /// issue #191: TeacherSharing=SchoolOnly ve istekçi sahibiyle aynı okuldaysa PublicAssignable ile aynı.
    /// </summary>
    public static bool CanAssign(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null, bool hasApprovedGrant = false,
        int? requesterSchoolId = null, int? ownerSchoolId = null)
    {
        if (isAdmin || (createUserId.HasValue && createUserId.Value > 0 && createUserId.Value == userId))
            return true;

        var hasOwner = createUserId.HasValue && createUserId.Value > 0;
        return hasOwner
            && (sharing == WorksheetTeacherSharing.PublicAssignable
                || hasApprovedGrant
                || IsSchoolOnlyMatch(sharing, requesterSchoolId, ownerSchoolId));
    }

    /// <summary>
    /// Öğretmen bir worksheet'i görüntüleyebilir mi (liste/detay/popüler).
    /// Sahibi veya admin her zaman görebilir; ayrıca TeacherSharing PublicView/PublicAssignable
    /// ise herhangi bir kimliği doğrulanmış öğretmen de görüntüleyebilir (issue #11).
    /// issue #191: TeacherSharing=SchoolOnly ise yalnızca sahibiyle aynı okuldaki öğretmen görebilir.
    /// Düzenleme yetkisi bundan ayrıdır — bkz. <see cref="CanModify"/>.
    /// </summary>
    public static bool CanView(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null,
        WorksheetStudentVisibility? studentVisibility = null,
        int? requesterSchoolId = null, int? ownerSchoolId = null)
    {
        // TODO(#13): studentVisibility == Restricted dalı burada ele alınacak.
        if (isAdmin || (createUserId.HasValue && createUserId.Value > 0 && createUserId.Value == userId))
            return true;

        // Legacy (owner'sız) worksheet'ler PublicView/PublicAssignable işaretlenmiş olsa bile
        // görünür sayılmaz — sadece admin erişebilir. Aksi halde varlığı 403 ile sızdırılır.
        var hasOwner = createUserId.HasValue && createUserId.Value > 0;
        return hasOwner &&
            (sharing == WorksheetTeacherSharing.PublicView
                || sharing == WorksheetTeacherSharing.PublicAssignable
                || IsSchoolOnlyMatch(sharing, requesterSchoolId, ownerSchoolId));
    }

    /// <summary>
    /// Öğretmen bir public sınavı kendi hesabına kopyalayabilir mi (issue #16).
    /// Kaynak PublicView/PublicAssignable (veya aynı okul için SchoolOnly, #191) olmalı; sahibi/admin her durumda.
    /// Düzenleme yetkisi (<see cref="CanModify"/>) bundan ayrı — kopya yeni bir worksheet olur,
    /// kopyalayan onun sahibidir.
    /// </summary>
    public static bool CanCopy(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null,
        WorksheetStudentVisibility? studentVisibility = null,
        int? requesterSchoolId = null, int? ownerSchoolId = null)
    {
        return CanView(createUserId, userId, isAdmin, sharing, studentVisibility, requesterSchoolId, ownerSchoolId);
    }

    /// <summary>
    /// SchoolOnly eşleşmesi (issue #191): iki taraf da okula bağlı VE aynı okul. Okulsuz istekçi
    /// (requesterSchoolId null) veya okulsuz/legacy sahip (ownerSchoolId null) → false; "null == null"
    /// bilinçli olarak eşleşme SAYILMAZ. Bu, <c>SchoolAccessPolicy.CanAccess</c>'in (#190) "null == null → true"
    /// kuralından farklıdır ve bilinçlidir: #190'da null "okulsuz kayıtları gör" izolasyonu (okulsuz→okulsuz
    /// izinli) iken burada null "bir okul YOK" demektir — "Sadece okulum" paylaşımı bir okul gerektirir;
    /// okulsuz öğretmenler ortak bir "okul" oluşturmaz ve bu değeri seçmeleri zaten API'de 400 ile reddedilir.
    /// </summary>
    public static bool IsSchoolOnlyMatch(WorksheetTeacherSharing? sharing, int? requesterSchoolId, int? ownerSchoolId)
    {
        return sharing == WorksheetTeacherSharing.SchoolOnly
            && requesterSchoolId.HasValue
            && ownerSchoolId.HasValue
            && requesterSchoolId.Value == ownerSchoolId.Value;
    }

    /// <summary>
    /// Liste sorguları için (issue #11 + #191) admin olmayan öğretmenin görebileceği worksheet koşulu —
    /// <see cref="CanView"/>'ın SQL karşılığı, sonradan süzme değil: kendi (sahibi olduğu) satırlar VEYA
    /// sahipli PublicView/PublicAssignable satırlar VEYA sahibinin <c>Teachers.SchoolId</c>'si istekçinin
    /// okuluyla eşit olan SchoolOnly satırlar (korelasyonlu alt sorgu, N+1 yok). İstekçi okulsuzsa
    /// SchoolOnly dalı hiç üretilmez. Legacy (owner'sız) satırlar hiçbir dala girmez.
    /// </summary>
    public static Expression<Func<Worksheet, bool>> VisibleToTeacherPredicate(AppDbContext context, int userId, int? requesterSchoolId)
    {
        if (!requesterSchoolId.HasValue)
        {
            return t => t.CreateUserId != null && t.CreateUserId > 0
                && (t.CreateUserId == userId
                    || t.TeacherSharing == WorksheetTeacherSharing.PublicView
                    || t.TeacherSharing == WorksheetTeacherSharing.PublicAssignable);
        }

        var schoolId = requesterSchoolId.Value;
        return t => t.CreateUserId != null && t.CreateUserId > 0
            && (t.CreateUserId == userId
                || t.TeacherSharing == WorksheetTeacherSharing.PublicView
                || t.TeacherSharing == WorksheetTeacherSharing.PublicAssignable
                || (t.TeacherSharing == WorksheetTeacherSharing.SchoolOnly
                    && context.Teachers.Any(te => te.UserId == t.CreateUserId && te.SchoolId == schoolId)));
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
