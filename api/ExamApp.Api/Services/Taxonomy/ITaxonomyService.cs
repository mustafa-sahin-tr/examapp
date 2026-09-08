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

    Task<ResponseBaseDto> CreateSubjectAsync(UpsertSubjectDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateSubjectAsync(int id, UpsertSubjectDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteSubjectAsync(int id, int userId, CancellationToken ct = default);

    /// <summary>Links a subject to a grade (idempotent — already linked is a success no-op).</summary>
    Task<ResponseBaseDto> AddSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default);

    /// <summary>Removes the subject↔grade link only; topics/subtopics/questions are untouched. Idempotent.</summary>
    Task<ResponseBaseDto> RemoveSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default);

    Task<ResponseBaseDto> CreateTopicAsync(UpsertTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateTopicAsync(int id, UpsertTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteTopicAsync(int id, int userId, CancellationToken ct = default);

    Task<ResponseBaseDto> CreateSubTopicAsync(UpsertSubTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateSubTopicAsync(int id, UpsertSubTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteSubTopicAsync(int id, int userId, CancellationToken ct = default);
}
