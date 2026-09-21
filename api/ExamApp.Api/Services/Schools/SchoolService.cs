using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Services.Schools;

public class SchoolService : ISchoolService
{
    private const int AddressLineMaxLength = 500;

    private readonly AppDbContext _context;

    // Client'a ulaşan ResponseBaseDto.Message metinleri buradan gelir (issue #184). DI her zaman
    // gerçek localizer'ı verir; parametre yalnızca DI'sız (birim test) senaryolar için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public SchoolService(AppDbContext context, IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
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
            return Fail(_localizer["school.nameRequired"]);

        if (await _context.Schools.AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct))
            return Fail(_localizer["school.nameAlreadyExists"]);

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
        return Ok(_localizer["school.created"], school.Id);
    }

    public async Task<ResponseBaseDto> UpdateAsync(int id, UpsertSchoolDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail(_localizer["school.nameRequired"]);

        var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (school == null)
            return Fail(_localizer["school.notFound"]);

        if (await _context.Schools.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), ct))
            return Fail(_localizer["school.otherNameAlreadyExists"]);

        var addressError = await ValidateAddressAsync(dto, ct);
        if (addressError != null)
            return Fail(addressError);

        _context.SetCurrentUser(userId);
        school.Name = name;
        school.ProvinceId = dto.ProvinceId;
        school.DistrictId = dto.DistrictId;
        school.AddressLine = NormalizeAddressLine(dto.AddressLine);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["school.updated"], school.Id);
    }

    public async Task<ResponseBaseDto> DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (school == null)
            return Fail(_localizer["school.notFound"]);

        // Teacher/Student do not have a SchoolId FK yet (out of scope for this issue) —
        // match against the free-text SchoolName field instead.
        var name = school.Name.ToLower();
        if (await _context.Teachers.AnyAsync(t => t.SchoolName != null && t.SchoolName.ToLower() == name, ct))
            return Fail(_localizer["school.hasTeachers"]);

        if (await _context.Students.AnyAsync(s => s.SchoolName != null && s.SchoolName.ToLower() == name, ct))
            return Fail(_localizer["school.hasStudents"]);

        _context.SetCurrentUser(userId);
        _context.Schools.Remove(school); // soft delete via AppDbContext.ApplyAuditInfo
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["school.deleted"], id);
    }

    /// <summary>
    /// İl/ilçe FK'lerinin referans tablolarda var olduğunu ve ilçenin seçilen ile ait olduğunu doğrular.
    /// Hata yoksa null döner. İlçe verilmişse il zorunludur (ilçe tek başına anlamsız).
    /// </summary>
    private async Task<string?> ValidateAddressAsync(UpsertSchoolDto dto, CancellationToken ct)
    {
        if (dto.AddressLine != null && dto.AddressLine.Trim().Length > AddressLineMaxLength)
            return _localizer["school.address.tooLong", AddressLineMaxLength];

        if (dto.DistrictId.HasValue && !dto.ProvinceId.HasValue)
            return _localizer["school.address.provinceRequiredWithDistrict"];

        if (dto.ProvinceId.HasValue &&
            !await _context.Provinces.AnyAsync(p => p.Id == dto.ProvinceId.Value, ct))
            return _localizer["school.address.invalidProvince"];

        if (dto.DistrictId.HasValue &&
            !await _context.Districts.AnyAsync(d => d.Id == dto.DistrictId.Value && d.ProvinceId == dto.ProvinceId!.Value, ct))
            return _localizer["school.address.invalidDistrict"];

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
