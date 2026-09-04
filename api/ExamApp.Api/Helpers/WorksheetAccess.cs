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
}
