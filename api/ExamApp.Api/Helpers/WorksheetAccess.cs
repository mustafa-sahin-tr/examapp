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
    /// Şu an CanModify ile aynı mantık; niyet ayrışsın diye ayrı isim.
    /// </summary>
    public static bool CanView(int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing? sharing = null,
        WorksheetStudentVisibility? studentVisibility = null)
    {
        // TODO(#11/#12/#13): sharing == PublicView/PublicAssignable ve studentVisibility == Restricted dalları burada.
        return isAdmin || (createUserId.HasValue && createUserId.Value > 0 && createUserId.Value == userId);
    }
}
