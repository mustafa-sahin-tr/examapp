namespace ExamApp.Api.Data;

/// <summary>
/// Öğretmenler arası paylaşım ekseni. Bu issue'da (#9) yalnızca taşınır;
/// PublicView / PublicAssignable yetki dalları #11/#12/#13'te aktifleşir.
/// </summary>
public enum WorksheetTeacherSharing
{
    Private = 0,
    PublicView = 1,
    PublicAssignable = 2
}
