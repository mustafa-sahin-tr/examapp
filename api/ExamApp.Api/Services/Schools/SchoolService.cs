using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Services.Schools;

public class SchoolService : ISchoolService
{
    private const int AddressLineMaxLength = 500;

    public const int MaxSearchLimit = 50;

    private readonly AppDbContext _context;

    // Client'a ulaşan ResponseBaseDto.Message metinleri buradan gelir (issue #184). DI her zaman
    // gerçek localizer'ı verir; parametre yalnızca DI'sız (birim test) senaryolar için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public SchoolService(AppDbContext context, IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<Paged<SchoolDto>> ListAsync(
        string? query, int? provinceId, int? districtId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);

        var schools = Matching(query);
        if (provinceId.HasValue) schools = schools.Where(s => s.ProvinceId == provinceId);
        if (districtId.HasValue) schools = schools.Where(s => s.DistrictId == districtId);

        var total = await schools.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, total, out var offset))
            return AdminListPaging.EmptyPage<SchoolDto>(page, pageSize, total);

        var items = await schools
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(s => new SchoolDto
            {
                Id = s.Id,
                Name = s.Name,
                ProvinceId = s.ProvinceId,
                ProvinceName = s.Province != null ? s.Province.Name : null,
                DistrictId = s.DistrictId,
                DistrictName = s.District != null ? s.District.Name : null,
                AddressLine = s.AddressLine,
                TeacherCount = _context.Teachers.Count(t => t.SchoolId == s.Id),
                StudentCount = _context.Students.Count(st => st.SchoolId == s.Id && st.SchoolVerifiedAt != null),
                PendingCount = _context.Students.Count(st => st.SchoolId == s.Id && st.SchoolVerifiedAt == null)
                    + _context.Teachers.Count(t => t.RequestedSchoolId == s.Id && t.ApprovalStatus == TeacherApprovalStatus.Pending),
            })
            .ToListAsync(ct);

        return new Paged<SchoolDto> { PageNumber = page, PageSize = pageSize, TotalCount = total, Items = items };
    }

    public async Task<List<SchoolOptionDto>> SearchAsync(string? query, int limit, CancellationToken ct = default)
    {
        return await ToOptions(Matching(query))
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Take(Math.Clamp(limit, 1, MaxSearchLimit))
            .ToListAsync(ct);
    }

    public async Task<SchoolOptionDto?> GetOptionAsync(int id, CancellationToken ct = default)
    {
        return await ToOptions(_context.Schools.AsNoTracking().Where(s => s.Id == id)).FirstOrDefaultAsync(ct);
    }

    private IQueryable<School> Matching(string? query)
    {
        var schools = _context.Schools.AsNoTracking();
        foreach (var token in SchoolSearchText.Tokens(query))
            schools = schools.Where(s => s.SearchText.Contains(token));
        return schools;
    }

    private static IQueryable<SchoolOptionDto> ToOptions(IQueryable<School> schools) => schools.Select(s => new SchoolOptionDto
    {
        Id = s.Id,
        Name = s.Name,
        ProvinceId = s.ProvinceId,
        ProvinceName = s.Province != null ? s.Province.Name : null,
        DistrictId = s.DistrictId,
        DistrictName = s.District != null ? s.District.Name : null,
    });

    /// <summary>İl / ilçe adları referans veridir; okul kaydında değişen il ve ilçenin adı buradan okunur.</summary>
    private async Task<string> BuildSearchTextAsync(string name, int? provinceId, int? districtId, CancellationToken ct)
    {
        var province = provinceId.HasValue
            ? await _context.Provinces.Where(p => p.Id == provinceId).Select(p => p.Name).FirstOrDefaultAsync(ct)
            : null;
        var district = districtId.HasValue
            ? await _context.Districts.Where(d => d.Id == districtId).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;
        return SchoolSearchText.Build(name, district, province);
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
            AddressLine = NormalizeAddressLine(dto.AddressLine),
            SearchText = await BuildSearchTextAsync(name, dto.ProvinceId, dto.DistrictId, ct)
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
        school.SearchText = await BuildSearchTextAsync(name, dto.ProvinceId, dto.DistrictId, ct);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["school.updated"], school.Id);
    }

    public async Task<ResponseBaseDto> DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (school == null)
            return Fail(_localizer["school.notFound"]);

        // Issue #898: üyelik SchoolId'de (bekleyen öğrenci ve okul isteyen öğretmen dahil); eski kayıtlar için serbest metin
        // SchoolName eşleşmesi de engeller.
        var name = school.Name.ToLower();
        if (await _context.Teachers.AnyAsync(t => t.SchoolId == id || t.RequestedSchoolId == id
                || (t.SchoolName != null && t.SchoolName.ToLower() == name), ct))
            return Fail(_localizer["school.hasTeachers"]);

        if (await _context.Students.AnyAsync(s => s.SchoolId == id || (s.SchoolName != null && s.SchoolName.ToLower() == name), ct))
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
