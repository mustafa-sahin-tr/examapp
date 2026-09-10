using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Locations;

/// <summary>Salt-okunur il/ilçe referans verisi (cascading dropdown için, issue #91).</summary>
public interface ILocationService
{
    /// <summary>Tüm iller, Türkçe alfabetik sıralı.</summary>
    Task<List<ProvinceDto>> GetProvincesAsync(CancellationToken ct = default);

    /// <summary>Verilen ile ait ilçeler, Türkçe alfabetik sıralı. Bilinmeyen il için boş liste.</summary>
    Task<List<DistrictDto>> GetDistrictsAsync(int provinceId, CancellationToken ct = default);
}
