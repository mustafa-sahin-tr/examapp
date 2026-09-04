namespace ExamApp.Api.Data;

/// <summary>
/// Öğrenciye görünürlük ekseni. Bu issue'da (#9) yalnızca taşınır;
/// Restricted davranışı #11/#12/#13'te aktifleşir.
/// </summary>
public enum WorksheetStudentVisibility
{
    Normal = 0,
    Restricted = 1
}
