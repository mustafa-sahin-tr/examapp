using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Locations;

public class LocationService : ILocationService
{
    // DB collation Türkçe harfleri (Ç, İ, Ş, ...) doğru sıralamaz; listeler küçük (81 il / ~40 ilçe),
    // bu yüzden sıralama bellekte tr-TR kültürüyle yapılır.
    private static readonly StringComparer TurkishComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: false);

    private readonly AppDbContext _context;

    public LocationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<ProvinceDto>> GetProvincesAsync(CancellationToken ct = default)
    {
        var provinces = await _context.Provinces
            .AsNoTracking()
            .Select(p => new ProvinceDto { Id = p.Id, Name = p.Name })
            .ToListAsync(ct);

        return provinces.OrderBy(p => p.Name, TurkishComparer).ToList();
    }

    public async Task<List<DistrictDto>> GetDistrictsAsync(int provinceId, CancellationToken ct = default)
    {
        var districts = await _context.Districts
            .AsNoTracking()
            .Where(d => d.ProvinceId == provinceId)
            .Select(d => new DistrictDto { Id = d.Id, Name = d.Name, ProvinceId = d.ProvinceId })
            .ToListAsync(ct);

        return districts.OrderBy(d => d.Name, TurkishComparer).ToList();
    }
}
