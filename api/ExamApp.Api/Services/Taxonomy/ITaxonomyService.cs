using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Taxonomy;

/// <summary>Admin-side write access to the Subject / Topic / SubTopic taxonomy.</summary>
public interface ITaxonomyService
{
    Task<TaxonomyTreeDto> GetTreeAsync(CancellationToken ct = default);

    Task<ResponseBaseDto> CreateSubjectAsync(UpsertSubjectDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateSubjectAsync(int id, UpsertSubjectDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteSubjectAsync(int id, int userId, CancellationToken ct = default);

    Task<ResponseBaseDto> CreateTopicAsync(UpsertTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateTopicAsync(int id, UpsertTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteTopicAsync(int id, int userId, CancellationToken ct = default);

    Task<ResponseBaseDto> CreateSubTopicAsync(UpsertSubTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateSubTopicAsync(int id, UpsertSubTopicDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteSubTopicAsync(int id, int userId, CancellationToken ct = default);
}
