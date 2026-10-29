namespace ExamApp.Api.Data;

/// <summary>
/// Öğrenciye görünürlük ekseni (#9). <see cref="Restricted"/> sınav keşfedilemez: öğrenci yalnız atamayla başlatır
/// (<see cref="ExamApp.Api.Helpers.WorksheetAccess.CanStudentStartTest"/>, keşif listesi) ve soruları serbest pratiğe girmez
/// (<c>PracticeQuestionPool</c>). Öğretmen ekseni (görüntüleme / atama / düzenleme) bundan bağımsızdır.
/// </summary>
public enum WorksheetStudentVisibility
{
    Normal = 0,
    Restricted = 1
}
