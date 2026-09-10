using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Schools;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class SchoolServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private SchoolService NewService(AppDbContext ctx) => new(ctx);

    private async Task<int> SeedSchoolAsync(string name = "Ankara Lisesi", string? addressLine = null)
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name, AddressLine = addressLine };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    /// <summary>Bir il ve ona bağlı bir ilçe ekler; (provinceId, districtId) döner.</summary>
    private async Task<(int ProvinceId, int DistrictId)> SeedProvinceWithDistrictAsync(
        string province = "Ankara", string district = "Çankaya")
    {
        await using var ctx = _db.NewContext();
        var p = new Province { Name = province };
        var d = new District { Name = district, Province = p };
        ctx.Provinces.Add(p);
        ctx.Districts.Add(d);
        await ctx.SaveChangesAsync();
        return (p.Id, d.Id);
    }

    // ---- Create ----

    [Fact]
    public async Task CreateAsync_persists_a_trimmed_school()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new UpsertSchoolDto { Name = "  Ankara Lisesi  ", AddressLine = " Kızılay Mah. No:1 " }, userId: 1);

        result.Success.ShouldBeTrue();
        var saved = await _db.NewContext().Schools.FindAsync(result.ObjectId);
        saved!.Name.ShouldBe("Ankara Lisesi");
        saved.AddressLine.ShouldBe("Kızılay Mah. No:1");
    }

    [Fact]
    public async Task CreateAsync_persists_province_and_district()
    {
        var (provinceId, districtId) = await SeedProvinceWithDistrictAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).CreateAsync(
            new UpsertSchoolDto { Name = "Ankara Lisesi", ProvinceId = provinceId, DistrictId = districtId }, userId: 1);

        result.Success.ShouldBeTrue();
        var saved = await _db.NewContext().Schools.FindAsync(result.ObjectId);
        saved!.ProvinceId.ShouldBe(provinceId);
        saved.DistrictId.ShouldBe(districtId);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_province()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new UpsertSchoolDto { Name = "Ankara Lisesi", ProvinceId = 9999 }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("il");
    }

    [Fact]
    public async Task CreateAsync_rejects_a_district_without_a_province()
    {
        var (_, districtId) = await SeedProvinceWithDistrictAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).CreateAsync(
            new UpsertSchoolDto { Name = "Ankara Lisesi", DistrictId = districtId }, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_rejects_a_district_that_belongs_to_another_province()
    {
        var (ankaraId, _) = await SeedProvinceWithDistrictAsync("Ankara", "Çankaya");
        var (_, konakId) = await SeedProvinceWithDistrictAsync("İzmir", "Konak");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).CreateAsync(
            new UpsertSchoolDto { Name = "Ankara Lisesi", ProvinceId = ankaraId, DistrictId = konakId }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("ilçe");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_rejects_a_blank_name(string name)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(new UpsertSchoolDto { Name = name }, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_name_case_insensitively()
    {
        await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).CreateAsync(new UpsertSchoolDto { Name = "ankara lisesi" }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("zaten var");
        (await _db.NewContext().Schools.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreateAsync_allows_distinct_names()
    {
        await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).CreateAsync(new UpsertSchoolDto { Name = "İzmir Lisesi" }, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.CountAsync()).ShouldBe(2);
    }

    // ---- Update ----

    [Fact]
    public async Task UpdateAsync_renames_an_existing_school()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).UpdateAsync(id, new UpsertSchoolDto { Name = "Ankara Fen Lisesi" }, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.FindAsync(id))!.Name.ShouldBe("Ankara Fen Lisesi");
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_missing_school()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateAsync(9999, new UpsertSchoolDto { Name = "X" }, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_rejects_renaming_to_another_schools_name_case_insensitively()
    {
        var idA = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Schools.Add(new School { Name = "Konya Lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var upd = _db.NewContext();
        var result = await NewService(upd).UpdateAsync(idA, new UpsertSchoolDto { Name = "konya lisesi" }, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_allows_keeping_its_own_name_unchanged()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi", addressLine: "Eski Adres");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).UpdateAsync(id, new UpsertSchoolDto { Name = "Ankara Lisesi", AddressLine = "Yeni Adres" }, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.FindAsync(id))!.AddressLine.ShouldBe("Yeni Adres");
    }

    [Fact]
    public async Task UpdateAsync_sets_province_and_district_and_can_clear_them()
    {
        var (provinceId, districtId) = await SeedProvinceWithDistrictAsync();
        var id = await SeedSchoolAsync("Ankara Lisesi");

        await using (var ctx = _db.NewContext())
        {
            var set = await NewService(ctx).UpdateAsync(id,
                new UpsertSchoolDto { Name = "Ankara Lisesi", ProvinceId = provinceId, DistrictId = districtId }, userId: 1);
            set.Success.ShouldBeTrue();
        }

        var afterSet = await _db.NewContext().Schools.FindAsync(id);
        afterSet!.ProvinceId.ShouldBe(provinceId);
        afterSet.DistrictId.ShouldBe(districtId);

        await using (var ctx = _db.NewContext())
        {
            var clear = await NewService(ctx).UpdateAsync(id, new UpsertSchoolDto { Name = "Ankara Lisesi" }, userId: 1);
            clear.Success.ShouldBeTrue();
        }

        var afterClear = await _db.NewContext().Schools.FindAsync(id);
        afterClear!.ProvinceId.ShouldBeNull();
        afterClear.DistrictId.ShouldBeNull();
    }

    // ---- Delete ----

    [Fact]
    public async Task DeleteAsync_rejects_a_missing_school()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).DeleteAsync(9999, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_succeeds_when_no_teacher_or_student_references_it()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.FindAsync(id)).ShouldBeNull(); // filtered by soft-delete query filter
    }

    [Fact]
    public async Task DeleteAsync_is_blocked_when_a_teacher_schoolname_matches_case_insensitively()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 1, SchoolName = "ankara lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("öğretmen");
        (await _db.NewContext().Schools.FindAsync(id)).ShouldNotBeNull(); // still there
    }

    [Fact]
    public async Task DeleteAsync_is_blocked_when_a_student_schoolname_matches_case_insensitively()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Students.Add(new Student { UserId = 1, StudentNumber = "123", SchoolName = "ANKARA LISESI" });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("öğrenci");
    }

    [Fact]
    public async Task DeleteAsync_succeeds_when_teacher_schoolname_does_not_match()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 1, SchoolName = "İzmir Lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeTrue();
    }

    // ---- Read ----

    [Fact]
    public async Task GetAllAsync_returns_schools_ordered_by_name()
    {
        await SeedSchoolAsync("Zeytinburnu Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Schools.Add(new School { Name = "Ankara Lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var result = await NewService(read).GetAllAsync();

        result.Select(s => s.Name).ShouldBe(new[] { "Ankara Lisesi", "Zeytinburnu Lisesi" });
    }

    [Fact]
    public async Task GetAllAsync_projects_province_and_district_names()
    {
        var (provinceId, districtId) = await SeedProvinceWithDistrictAsync("Ankara", "Çankaya");
        await using (var ctx = _db.NewContext())
        {
            ctx.Schools.Add(new School { Name = "Ankara Lisesi", ProvinceId = provinceId, DistrictId = districtId, AddressLine = "Adres" });
            ctx.Schools.Add(new School { Name = "Adressiz Lise" });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var result = await NewService(read).GetAllAsync();

        var withAddress = result.Single(s => s.Name == "Ankara Lisesi");
        withAddress.ProvinceName.ShouldBe("Ankara");
        withAddress.DistrictName.ShouldBe("Çankaya");
        withAddress.AddressLine.ShouldBe("Adres");

        var without = result.Single(s => s.Name == "Adressiz Lise");
        without.ProvinceId.ShouldBeNull();
        without.ProvinceName.ShouldBeNull();
        without.DistrictName.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllAsync_does_not_break_on_a_legacy_school_with_no_address_data_at_all()
    {
        // Migration öncesi kayıtları taklit eder: Province/District/AddressLine hepsi null.
        // Geriye dönük uyumluluk (kabul kriteri #4) — projection hata fırlatmamalı, hepsi null dönmeli.
        await SeedSchoolAsync("Eski Kayıt Lisesi", addressLine: null);

        await using var read = _db.NewContext();
        var result = await NewService(read).GetAllAsync();

        var legacy = result.Single(s => s.Name == "Eski Kayıt Lisesi");
        legacy.ProvinceId.ShouldBeNull();
        legacy.ProvinceName.ShouldBeNull();
        legacy.DistrictId.ShouldBeNull();
        legacy.DistrictName.ShouldBeNull();
        legacy.AddressLine.ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
