using System;

namespace ExamApp.Api.Models.Dtos;

public enum WorksheetSortBy
{
    Newest = 0,
    Popular = 1,
    Duration = 2,
    QuestionCount = 3,
    Alphabetical = 4,
    Recent = 5
}

public class ExamFilterDto : FilterBaseDto
{
    public int? id = 0;
    public string? search = null;
    public List<int>? subjectIds = null;
    public List<int>? gradeIds = null;
    public int bookTestId = 0;

    // --- Sorting ---
    public string? sortBy = null;
    public string? sortDir = null;

    // --- Richer filtering ---
    public List<int>? statuses = null; // STUDENT only: -1 not started, 0 in progress, 1 completed
    public int? minQuestionCount = null;
    public int? maxQuestionCount = null;
    public int? minDurationSeconds = null;
    public int? maxDurationSeconds = null;
    public bool? isPracticeTest = null;
    public List<int>? bookIds = null;

    /// <summary>
    /// True ise öğretmen kendi worksheet'lerine ek olarak başka öğretmenlerin PublicView/PublicAssignable
    /// worksheet'lerini de listede görür (issue #11). Admin ve öğrenci akışlarını etkilemez.
    /// Varsayılan false — mevcut sahiplik-scope'lu davranış aynen korunur.
    /// </summary>
    public bool includeShared = false;

    /// <summary>Safe-parse of <see cref="sortBy"/>; unknown/empty -> Newest.</summary>
    public WorksheetSortBy SortByParsed =>
        Enum.TryParse<WorksheetSortBy>(sortBy, ignoreCase: true, out var parsed) &&
        Enum.IsDefined(typeof(WorksheetSortBy), parsed)
            ? parsed
            : WorksheetSortBy.Newest;

    /// <summary>
    /// True = descending. Explicit <see cref="sortDir"/> wins; otherwise a sensible
    /// default per sort field (newest/popular/recent -> desc, others -> asc).
    /// </summary>
    public bool SortDescending
    {
        get
        {
            if (string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase)) return true;
            return SortByParsed switch
            {
                WorksheetSortBy.Newest => true,
                WorksheetSortBy.Popular => true,
                WorksheetSortBy.Recent => true,
                _ => false
            };
        }
    }
}
