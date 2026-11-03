namespace ExamApp.Api.Services.Practice;

/// <summary>Issue #532: soru kaydetme sonucu.</summary>
public enum SaveQuestionResult
{
    Saved,
    /// <summary>Soru bitmiş test ya da pratikte görülmedi, ya da bitmemiş bir testte/atamada.</summary>
    NotAllowed,
    LimitReached
}
