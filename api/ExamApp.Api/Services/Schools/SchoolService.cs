using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Schools;

public class SchoolService : ISchoolService
{
    private const int AddressLineMaxLength = 500;

    private readonly AppDbContext _context;

    public SchoolService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<SchoolDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Schools
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SchoolDto
            {
                Id = s.Id,
                Name = s.Name,
                ProvinceId = s.ProvinceId,
                ProvinceName = s.Province != null ? s.Province.Name : null,
                DistrictId = s.DistrictId,
                DistrictName = s.District != null ? s.District.Name : null,
                AddressLine = s.AddressLine
            })
            .ToListAsync(ct);
    }

    public async Task<ResponseBaseDto> CreateAsync(UpsertSchoolDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Okul adı boş olamaz.");

        if (await _context.Schools.AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct))
            return Fail("Bu isimde bir okul zaten var.");

        var addressError = await ValidateAddressAsync(dto, ct);
        if (addressError != null)
            return Fail(addressError);

        _context.SetCurrentUser(userId);
        var school = new School
        {
            Name = name,
            ProvinceId = dto.ProvinceId,
            DistrictId = dto.DistrictId,
            AddressLine = NormalizeAddressLine(dto.AddressLine)
        };
        _context.Schools.Add(school);
        await _context.SaveChangesAsync(ct);
        return Ok("Okul eklendi.", school.Id);
    }

    public async Task<ResponseBaseDto> UpdateAsync(int id, UpsertSchoolDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Okul adı boş olamaz.");

        var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (school == null)
            return Fail("Okul bulunamadı.");

        if (await _context.Schools.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), ct))
            return Fail("Bu isimde başka bir okul zaten var.");

        var addressError = await ValidateAddressAsync(dto, ct);
        if (addressError != null)
            return Fail(addressError);

        _context.SetCurrentUser(userId);
        school.Name = name;
        school.ProvinceId = dto.ProvinceId;
        school.DistrictId = dto.DistrictId;
        school.AddressLine = NormalizeAddressLine(dto.AddressLine);
        await _context.SaveChangesAsync(ct);
        return Ok("Okul güncellendi.", school.Id);
    }

    public async Task<ResponseBaseDto> DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (school == null)
            return Fail("Okul bulunamadı.");

        // Teacher/Student do not have a SchoolId FK yet (out of scope for this issue) —
        // match against the free-text SchoolName field instead.
        var name = school.Name.ToLower();
        if (await _context.Teachers.AnyAsync(t => t.SchoolName != null && t.SchoolName.ToLower() == name, ct))
            return Fail("Bu okula bağlı öğretmen kayıtları var, silinemez.");

        if (await _context.Students.AnyAsync(s => s.SchoolName != null && s.SchoolName.ToLower() == name, ct))
            return Fail("Bu okula bağlı öğrenci kayıtları var, silinemez.");

        _context.SetCurrentUser(userId);
        _context.Schools.Remove(school); // soft delete via AppDbContext.ApplyAuditInfo
        await _context.SaveChangesAsync(ct);
        return Ok("Okul silindi.", id);
    }

    /// <summary>
    /// İl/ilçe FK'lerinin referans tablolarda var olduğunu ve ilçenin seçilen ile ait olduğunu doğrular.
    /// Hata yoksa null döner. İlçe verilmişse il zorunludur (ilçe tek başına anlamsız).
    /// </summary>
    private async Task<string?> ValidateAddressAsync(UpsertSchoolDto dto, CancellationToken ct)
    {
        if (dto.AddressLine != null && dto.AddressLine.Trim().Length > AddressLineMaxLength)
            return $"Açık adres en fazla {AddressLineMaxLength} karakter olabilir.";

        if (dto.DistrictId.HasValue && !dto.ProvinceId.HasValue)
            return "İlçe seçildiğinde il de seçilmelidir.";

        if (dto.ProvinceId.HasValue &&
            !await _context.Provinces.AnyAsync(p => p.Id == dto.ProvinceId.Value, ct))
            return "Geçersiz il.";

        if (dto.DistrictId.HasValue &&
            !await _context.Districts.AnyAsync(d => d.Id == dto.DistrictId.Value && d.ProvinceId == dto.ProvinceId!.Value, ct))
            return "Geçersiz ilçe veya ilçe seçilen ile ait değil.";

        return null;
    }

    private static string? NormalizeAddressLine(string? addressLine)
    {
        var trimmed = addressLine?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ResponseBaseDto Fail(string message) => new() { Success = false, Message = message };

    private static ResponseBaseDto Ok(string message, int id) =>
        new() { Success = true, Message = message, ObjectId = id };
}
