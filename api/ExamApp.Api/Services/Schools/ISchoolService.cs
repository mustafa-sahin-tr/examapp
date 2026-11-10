using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Schools;

/// <summary>Admin-side CRUD + public read access for schools.</summary>
public interface ISchoolService
{
    /// <summary>Admin okul listesi: filtreli, sayfalı; üye sayıları yalnız dönen sayfa için hesaplanır.</summary>
    Task<Paged<SchoolDto>> ListAsync(string? query, int? provinceId, int? districtId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Okul seçici araması (ada göre sıralı, en fazla <see cref="SchoolService.MaxSearchLimit"/> satır); il / ilçe daraltır.</summary>
    Task<List<SchoolOptionDto>> SearchAsync(string? query, int? provinceId, int? districtId, int limit, CancellationToken ct = default);

    Task<SchoolOptionDto?> GetOptionAsync(int id, CancellationToken ct = default);

    Task<ResponseBaseDto> CreateAsync(UpsertSchoolDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> UpdateAsync(int id, UpsertSchoolDto dto, int userId, CancellationToken ct = default);
    Task<ResponseBaseDto> DeleteAsync(int id, int userId, CancellationToken ct = default);
}
