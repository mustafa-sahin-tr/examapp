using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Taxonomy;

/// <summary>Admin-side write access to the Subject / Topic / SubTopic taxonomy.</summary>
public interface ITaxonomyService
{
    /// <summary>
    /// Full taxonomy tree. <paramref name="gradeId"/> restricts subjects to those linked to that
    /// grade via GradeSubject; <paramref name="unassignedOnly"/> restricts to subjects with no
    /// GradeSubject link at all. Both null/false = every subject (unfiltered).
    /// </summary>
    Task<TaxonomyTreeDto> GetTreeAsync(int? gradeId = null, bool unassignedOnly = false, CancellationToken ct = default);

    Task<TaxonomyResponseDto> CreateSubjectAsync(UpsertSubjectDto dto, int userId, CancellationToken ct = default);
    Task<TaxonomyResponseDto> UpdateSubjectAsync(int id, UpsertSubjectDto dto, int userId, CancellationToken ct = default);
    Task<TaxonomyResponseDto> DeleteSubjectAsync(int id, int userId, CancellationToken ct = default);

    /// <summary>Links a subject to a grade (idempotent — already linked is a success no-op).</summary>
    Task<TaxonomyResponseDto> AddSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default);

    /// <summary>
    /// Removes the subject↔grade link only; subtopics/questions are untouched. Idempotent. Refused
    /// (<see cref="TaxonomyErrorCodes.SubjectGradeHasTopics"/>) while the subject still has topics in that grade, and
    /// (<see cref="TaxonomyErrorCodes.LastGradeLink"/>) when it is the subject's last grade link (issue #249).
    /// </summary>
    Task<TaxonomyResponseDto> RemoveSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default);

    Task<TaxonomyResponseDto> CreateTopicAsync(UpsertTopicDto dto, int userId, CancellationToken ct = default);
    Task<TaxonomyResponseDto> UpdateTopicAsync(int id, UpsertTopicDto dto, int userId, CancellationToken ct = default);
    Task<TaxonomyResponseDto> DeleteTopicAsync(int id, int userId, CancellationToken ct = default);

    Task<TaxonomyResponseDto> CreateSubTopicAsync(UpsertSubTopicDto dto, int userId, CancellationToken ct = default);
    Task<TaxonomyResponseDto> UpdateSubTopicAsync(int id, UpsertSubTopicDto dto, int userId, CancellationToken ct = default);
    Task<TaxonomyResponseDto> DeleteSubTopicAsync(int id, int userId, CancellationToken ct = default);
}
