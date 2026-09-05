using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Schools;

/// <summary>Admin-side CRUD + public read access for schools.</summary>
public interface ISchoolService
{
    Task<List<SchoolDto>> GetAllAsync(CancellationToken ct = default);

    Task<ResponseBaseDto> CreateAsync(UpsertSchoolDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateAsync(int id, UpsertSchoolDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteAsync(int id, int userId, CancellationToken ct = default);
}
