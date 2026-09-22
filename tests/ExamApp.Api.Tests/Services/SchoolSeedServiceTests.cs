using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// seed-schools (issue #216): filtre, normalizasyon, il/ilçe eşleştirme, idempotency, ortam guard'ı,
/// dry-run ve deterministik limit. Kaynak dosya gömülü fixture — ağ yok.
/// </summary>
public class SchoolSeedServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    /// <summary>
    /// Gerçek dosyanın biçimini taklit eder: UTF-8 BOM, CRLF, tırnaklı alan (virgül içeren ad),
    /// Türkçe karakterler, sütun sırası kaynaktaki gibi.
    /// </summary>
    private const string FixtureCsv =
        "il,ilce,okul_adi,kurum_kodu,okul_turu,web_sitesi,adres,telefon,harita,cekim_tarihi\r\n" +
        // Kars — 3 ilkokul, 2 ortaokul, 1 İHO, 1 lise (kapsam dışı)
        "Kars,Merkez,KARATAŞ İLKOKULU,100001,İlkokul,,,,,2026-08-31\r\n" +
        "Kars,Merkez,Atatürk İlkokulu,100002,İlkokul,,,,,2026-08-31\r\n" +
        "Kars,Sarıkamış,Şehit Öğretmen Ali Yıldırım ilkokulu,100003,İlkokul,,,,,2026-08-31\r\n" +
        "Kars,Merkez,\"Fevzi Çakmak, Cumhuriyet Ortaokulu\",100004,Ortaokul,,,,,2026-08-31\r\n" +
        "Kars,Kağızman,Kağızman Yatılı Bölge Ortaokulu,100005,Ortaokul,,,,,2026-08-31\r\n" +
        "Kars,Merkez,Kars İmam Hatip Ortaokulu,100006,İmam Hatip Ortaokulu,,,,,2026-08-31\r\n" +
        "Kars,Merkez,Kars Fen Lisesi,100007,Fen Lisesi,,,,,2026-08-31\r\n" +
        // Kars — ilçesi referans tabloda olmayan satır
        "Kars,Hayalilçe,Hayalilçe İlkokulu,100008,İlkokul,,,,,2026-08-31\r\n" +
        // Antalya — Büyükşehir sahte ilçe + normal
        "Antalya,Büyükşehir,Antalya Merkez İlkokulu,200001,İlkokul,,,,,2026-08-31\r\n" +
        "Antalya,Muratpaşa,Muratpaşa Ortaokulu,200002,Ortaokul,,,,,2026-08-31\r\n" +
        // Ankara — yanlış sınıflanmış özel eğitim kayıtları + normal
        "Ankara,Çankaya,Millî Eğitim Vakfı Gökkuşağı Özel Eğitim İlkokulu,300001,İlkokul,,,,,2026-08-31\r\n" +
        "Ankara,Çankaya,Çankaya Uygulama Merkezi Ortaokulu,300002,Ortaokul,,,,,2026-08-31\r\n" +
        "Ankara,Çankaya,Çankaya İlkokulu,300003,İlkokul,,,,,2026-08-31\r\n" +
        // Yalova — istenirse Province tablosunda yok
        "Yalova,Merkez,Yalova İlkokulu,400001,İlkokul,,,,,2026-08-31\r\n" +
        // Kapsam dışı il — hiç dokunulmamalı
        "Bursa,Nilüfer,Nilüfer İlkokulu,500001,İlkokul,,,,,2026-08-31\r\n";

    private async Task SeedReferenceAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.Provinces.AddRange(
            Prov("Kars", "Merkez", "Sarıkamış", "Kağızman"),
            Prov("Antalya", "Muratpaşa"),
            Prov("Ankara", "Çankaya"));
        await ctx.SaveChangesAsync();

        static Province Prov(string name, params string[] districts)
        {
            var p = new Province { Name = name };
            foreach (var d in districts) p.Districts.Add(new District { Name = d, Province = p });
            return p;
        }
    }

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static ISchoolSeedSourceProvider Fixture(string csv = FixtureCsv)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        var provider = Substitute.For<ISchoolSeedSourceProvider>();
        provider.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new SchoolSeedSource("fixture", "deadbeef", FromCache: true, () => new MemoryStream(bytes)));
        return provider;
    }

    private SchoolSeedService NewService(AppDbContext ctx, string environment = "Development", ISchoolSeedSourceProvider? source = null)
        => new(ctx, source ?? Fixture(), Env(environment), NullLogger<SchoolSeedService>.Instance);

    private static SchoolSeedOptions Opts(params string[] provinces) => new() { Provinces = provinces };

    // ---- ortam guard'ı ----

    [Fact]
    public async Task Production_refuses_before_touching_the_source()
    {
        await SeedReferenceAsync();
        var source = Fixture();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx, "Production", source).RunAsync(Opts("Kars")));

        ex.Message.ShouldContain("Production");
        await source.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
        (await _db.NewContext().Schools.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task Development_and_staging_are_allowed(string environment)
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, environment).RunAsync(Opts("Kars"));

        result.Added.ShouldBe(5);
    }

    // ---- filtre / sayım ----

    [Fact]
    public async Task Imports_ilkokul_and_ortaokul_only_and_reports_per_province()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).RunAsync(Opts("Kars", "Antalya", "Ankara"));

        result.TotalRowsInSource.ShouldBe(15);
        result.CandidateRows.ShouldBe(11); // İHO, lise, Yalova, Bursa dışarıda
        result.Added.ShouldBe(7);
        result.SourceSha256.ShouldBe("deadbeef");

        var kars = result.Provinces.Single(p => p.Province == "Kars");
        kars.ProvinceMatched.ShouldBeTrue();
        kars.IlkokulAdded.ShouldBe(3);
        kars.OrtaokulAdded.ShouldBe(2);
        kars.UnmatchedDistrictRows.ShouldBe(1);

        var antalya = result.Provinces.Single(p => p.Province == "Antalya");
        antalya.SkippedBuyuksehir.ShouldBe(1);
        antalya.IlkokulAdded.ShouldBe(0);
        antalya.OrtaokulAdded.ShouldBe(1);

        var ankara = result.Provinces.Single(p => p.Province == "Ankara");
        ankara.SkippedSpecialEducation.ShouldBe(2);
        ankara.IlkokulAdded.ShouldBe(1);

        var saved = await _db.NewContext().Schools.Include(s => s.District).ToListAsync();
        saved.Count.ShouldBe(7);
        saved.ShouldAllBe(s => s.IsSeedData && s.ExternalCode != null && s.ProvinceId != null && s.DistrictId != null);
        saved.ShouldNotContain(s => s.Name.Contains("Lisesi") || s.Name.Contains("İmam Hatip") || s.Name.Contains("Nilüfer"));
        saved.Single(s => s.ExternalCode == "100004").Name.ShouldBe("Fevzi Çakmak, Cumhuriyet Ortaokulu"); // tırnaklı alan
        saved.Single(s => s.ExternalCode == "100005").District!.Name.ShouldBe("Kağızman");
    }

    [Fact]
    public async Task Kinds_filter_selects_only_requested_type()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).RunAsync(new SchoolSeedOptions
        {
            Provinces = ["Kars"],
            Kinds = new HashSet<SchoolSeedKind> { SchoolSeedKind.Ortaokul }
        });

        result.Added.ShouldBe(2);
        result.Provinces.Single().IlkokulAdded.ShouldBe(0);
        (await _db.NewContext().Schools.CountAsync(s => s.Name.EndsWith("Ortaokulu"))).ShouldBe(2);
    }

    [Fact]
    public async Task Imam_hatip_included_only_with_flag()
    {
        await SeedReferenceAsync();

        await using (var ctx = _db.NewContext())
        {
            var without = await NewService(ctx).RunAsync(Opts("Kars"));
            without.Provinces.Single().OrtaokulAdded.ShouldBe(2);
        }

        await using (var ctx = _db.NewContext())
        {
            var with = await NewService(ctx).RunAsync(new SchoolSeedOptions { Provinces = ["Kars"], IncludeImamHatip = true });
            with.Added.ShouldBe(1);
            with.Provinces.Single().OrtaokulAdded.ShouldBe(1);
            with.Provinces.Single().OrtaokulExisting.ShouldBe(2);
        }

        (await _db.NewContext().Schools.SingleAsync(s => s.ExternalCode == "100006")).Name.ShouldBe("Kars İmam Hatip Ortaokulu");
    }

    // ---- normalizasyon ----

    [Fact]
    public async Task Names_are_normalized_on_import()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        await NewService(ctx).RunAsync(Opts("Kars"));

        var byCode = await _db.NewContext().Schools.ToDictionaryAsync(s => s.ExternalCode!, s => s.Name);
        byCode["100001"].ShouldBe("Karataş İlkokulu");
        byCode["100003"].ShouldBe("Şehit Öğretmen Ali Yıldırım İlkokulu");
        byCode["100002"].ShouldBe("Atatürk İlkokulu");
    }

    // ---- il / ilçe eşleşmesi ----

    [Fact]
    public async Task Unmatched_province_and_district_are_reported_not_silently_dropped()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).RunAsync(Opts("Kars", "Yalova", "Hakkari"));

        result.UnmatchedProvinces.ShouldBe(new[] { "Yalova", "Hakkari" });
        result.ProvincesNotInSource.ShouldBe(new[] { "Hakkari" });
        result.Provinces.Single(p => p.Province == "Yalova").ProvinceMatched.ShouldBeFalse();

        result.UnmatchedDistrictRows.ShouldBe(1);
        var unmatched = result.UnmatchedDistricts.ShouldHaveSingleItem();
        unmatched.Province.ShouldBe("Kars");
        unmatched.District.ShouldBe("Hayalilçe");
        unmatched.Rows.ShouldBe(1);

        (await _db.NewContext().Schools.AnyAsync(s => s.ExternalCode == "100008" || s.ExternalCode == "400001")).ShouldBeFalse();
    }

    [Fact]
    public async Task Province_and_district_matching_is_turkish_case_and_dotted_i_insensitive()
    {
        await using (var ctx = _db.NewContext())
        {
            var p = new Province { Name = "İstanbul" };
            p.Districts.Add(new District { Name = "Şişli", Province = p });
            ctx.Provinces.Add(p);
            await ctx.SaveChangesAsync();
        }

        const string csv =
            "il,ilce,okul_adi,kurum_kodu,okul_turu\n" +
            "ISTANBUL,ŞİŞLİ,Şişli İlkokulu,900001,İlkokul\n";

        await using var run = _db.NewContext();
        var result = await NewService(run, source: Fixture(csv)).RunAsync(Opts("istanbul"));

        result.UnmatchedProvinces.ShouldBeEmpty();
        result.UnmatchedDistricts.ShouldBeEmpty();
        result.Added.ShouldBe(1);
        var saved = await _db.NewContext().Schools.Include(s => s.Province).Include(s => s.District).SingleAsync();
        saved.Province!.Name.ShouldBe("İstanbul");
        saved.District!.Name.ShouldBe("Şişli");
    }

    // ---- idempotency ----

    [Fact]
    public async Task Second_run_adds_zero_and_counts_existing()
    {
        await SeedReferenceAsync();

        await using (var ctx = _db.NewContext())
        {
            var first = await NewService(ctx).RunAsync(Opts("Kars", "Antalya", "Ankara"));
            first.Added.ShouldBe(7);
            first.SkippedExisting.ShouldBe(0);
        }

        await using (var ctx = _db.NewContext())
        {
            var second = await NewService(ctx).RunAsync(Opts("Kars", "Antalya", "Ankara"));
            second.Added.ShouldBe(0);
            second.SkippedExisting.ShouldBe(7);
            var kars = second.Provinces.Single(p => p.Province == "Kars");
            kars.IlkokulExisting.ShouldBe(3);
            kars.OrtaokulExisting.ShouldBe(2);
        }

        (await _db.NewContext().Schools.CountAsync()).ShouldBe(7);
    }

    [Fact]
    public async Task Soft_deleted_seed_row_still_counts_as_existing()
    {
        await SeedReferenceAsync();
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).RunAsync(Opts("Kars"));
            var karatas = await ctx.Schools.SingleAsync(s => s.ExternalCode == "100001");
            ctx.Schools.Remove(karatas); // soft delete
            await ctx.SaveChangesAsync();
        }

        await using var run = _db.NewContext();
        var result = await NewService(run).RunAsync(Opts("Kars"));

        result.Added.ShouldBe(0);
        result.SkippedSoftDeleted.ShouldBe(1);
        result.SkippedExisting.ShouldBe(4);
        result.Provinces.Single().SkippedSoftDeleted.ShouldBe(1);
        (await _db.NewContext().Schools.IgnoreQueryFilters().CountAsync(s => s.ExternalCode == "100001")).ShouldBe(1);
    }

    [Fact]
    public async Task Rows_exceeding_column_limits_are_skipped_and_reported()
    {
        await SeedReferenceAsync();
        var longName = new string('A', 201) + " İlkokulu";
        var longCode = new string('9', 33);
        var csv =
            "il,ilce,okul_adi,kurum_kodu,okul_turu\n" +
            $"Kars,Merkez,{longName},100001,İlkokul\n" +
            $"Kars,Merkez,Kısa İlkokulu,{longCode},İlkokul\n" +
            "Kars,Merkez,Normal İlkokulu,100003,İlkokul\n";

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx, source: Fixture(csv)).RunAsync(Opts("Kars"));

        result.SkippedTooLong.ShouldBe(2);
        result.Provinces.Single().SkippedTooLong.ShouldBe(2);
        result.Added.ShouldBe(1);
        (await _db.NewContext().Schools.SingleAsync()).Name.ShouldBe("Normal İlkokulu");
    }

    [Fact]
    public async Task Manually_created_school_with_same_name_and_district_is_treated_as_existing()
    {
        await SeedReferenceAsync();
        await using (var ctx = _db.NewContext())
        {
            var merkez = await ctx.Districts.SingleAsync(d => d.Name == "Merkez" && d.Province.Name == "Kars");
            ctx.Schools.Add(new School { Name = "ATATÜRK İLKOKULU", ProvinceId = merkez.ProvinceId, DistrictId = merkez.Id });
            await ctx.SaveChangesAsync();
        }

        await using var run = _db.NewContext();
        var result = await NewService(run).RunAsync(Opts("Kars"));

        result.Added.ShouldBe(4);
        result.SkippedExisting.ShouldBe(1);
        (await _db.NewContext().Schools.CountAsync(s => s.ExternalCode == "100002")).ShouldBe(0);
    }

    [Fact]
    public async Task ExternalCode_is_unique_in_the_schema()
    {
        await using var ctx = _db.NewContext();
        ctx.Schools.Add(new School { Name = "A", ExternalCode = "777" });
        ctx.Schools.Add(new School { Name = "B", ExternalCode = null });
        ctx.Schools.Add(new School { Name = "C", ExternalCode = null }); // null'lar serbest
        await ctx.SaveChangesAsync();

        ctx.Schools.Add(new School { Name = "D", ExternalCode = "777" });
        await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ---- dry-run ----

    [Fact]
    public async Task DryRun_reports_but_writes_nothing()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).RunAsync(new SchoolSeedOptions { Provinces = ["Kars"], DryRun = true });

        result.DryRun.ShouldBeTrue();
        result.Added.ShouldBe(5);
        result.AddedSample.Count.ShouldBe(5);
        (await _db.NewContext().Schools.CountAsync()).ShouldBe(0);
    }

    // ---- limit ----

    [Fact]
    public async Task Limit_takes_first_n_by_normalized_name_deterministically()
    {
        await SeedReferenceAsync();

        await using (var ctx = _db.NewContext())
        {
            var dry = await NewService(ctx).RunAsync(new SchoolSeedOptions { Provinces = ["Kars"], LimitPerProvince = 2, DryRun = true });
            dry.Added.ShouldBe(2);
            dry.SkippedByLimit.ShouldBe(3);
            dry.AddedSample.Select(s => s.Name).ShouldBe(new[] { "Atatürk İlkokulu", "Fevzi Çakmak, Cumhuriyet Ortaokulu" });
            // limit, eşleşmeyen ilçe raporunu gizlemez
            dry.UnmatchedDistrictRows.ShouldBe(1);
        }

        await using (var ctx = _db.NewContext())
        {
            var first = await NewService(ctx).RunAsync(new SchoolSeedOptions { Provinces = ["Kars"], LimitPerProvince = 2 });
            first.Added.ShouldBe(2);
        }

        await using (var ctx = _db.NewContext())
        {
            var second = await NewService(ctx).RunAsync(new SchoolSeedOptions { Provinces = ["Kars"], LimitPerProvince = 2 });
            second.Added.ShouldBe(0);
            second.SkippedExisting.ShouldBe(2);
        }

        var names = await _db.NewContext().Schools.OrderBy(s => s.ExternalCode).Select(s => s.Name).ToListAsync();
        names.ShouldBe(new[] { "Atatürk İlkokulu", "Fevzi Çakmak, Cumhuriyet Ortaokulu" });
    }

    [Fact]
    public async Task Duplicate_province_names_are_processed_once()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).RunAsync(Opts("Kars", "kars", "KARS"));

        result.Provinces.ShouldHaveSingleItem().Province.ShouldBe("Kars");
        result.Added.ShouldBe(5);
    }

    [Fact]
    public async Task Invalid_options_are_rejected()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        await Should.ThrowAsync<ArgumentException>(() => svc.RunAsync(new SchoolSeedOptions { Provinces = [] }));
        await Should.ThrowAsync<ArgumentException>(() => svc.RunAsync(new SchoolSeedOptions { LimitPerProvince = 0 }));
        await Should.ThrowAsync<ArgumentException>(() => svc.RunAsync(new SchoolSeedOptions { Kinds = new HashSet<SchoolSeedKind>() }));
    }

    public void Dispose() => _db.Dispose();
}
