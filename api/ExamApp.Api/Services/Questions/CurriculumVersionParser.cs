using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Questions;

/// <summary>Issue #692: sayısal değerler tanımsız müfredat üretmesin diye yalnız ad kabul edilir.</summary>
public static class CurriculumVersionParser
{
    public static bool TryParse(string? value, out CurriculumVersion version) =>
        Enum.TryParse(value, ignoreCase: true, out version)
        && !int.TryParse(value, out _)
        && Enum.IsDefined(version);
}
