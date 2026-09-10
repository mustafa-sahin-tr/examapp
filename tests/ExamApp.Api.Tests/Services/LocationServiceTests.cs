using ExamApp.Api.Data;
using ExamApp.Api.Services.Locations;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Services;

public class LocationServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private async Task SeedAsync(params (string Province, string[] Districts)[] data)
    {
        await using var ctx = _db.NewContext();
        foreach (var (province, districts) in data)
        {
            var p = new Province { Name = province };
            foreach (var d in districts)
                p.Districts.Add(new District { Name = d, Province = p });
            ctx.Provinces.Add(p);
        }
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetProvincesAsync_orders_with_turkish_collation()
    {
        // DB sıralaması Ç/İ/Ş'yi sona atar; servis tr-TR ile bellekte sıralamalı.
        await SeedAsync(("Çorum", []), ("Adana", []), ("İzmir", []), ("Isparta", []), ("Şanlıurfa", []), ("Zonguldak", []));
        await using var ctx = _db.NewContext();

        var result = await new LocationService(ctx).GetProvincesAsync();

        result.Select(p => p.Name).ShouldBe(new[] { "Adana", "Çorum", "Isparta", "İzmir", "Şanlıurfa", "Zonguldak" });
    }

    [Fact]
    public async Task GetDistrictsAsync_returns_only_the_requested_provinces_districts_sorted()
    {
        await SeedAsync(("Ankara", ["Çankaya", "Altındağ", "Sincan"]), ("İzmir", ["Konak"]));
        await using var ctx = _db.NewContext();
        var ankaraId = (await ctx.Provinces.SingleAsync(p => p.Name == "Ankara")).Id;

        var result = await new LocationService(ctx).GetDistrictsAsync(ankaraId);

        result.Select(d => d.Name).ShouldBe(new[] { "Altındağ", "Çankaya", "Sincan" });
        result.ShouldAllBe(d => d.ProvinceId == ankaraId);
    }

    [Fact]
    public async Task GetDistrictsAsync_returns_an_empty_list_for_an_unknown_province()
    {
        await SeedAsync(("Ankara", ["Çankaya"]));
        await using var ctx = _db.NewContext();

        var result = await new LocationService(ctx).GetDistrictsAsync(9999);

        result.ShouldBeEmpty();
    }

    public void Dispose() => _db.Dispose();
}

public class ReferenceDataSeedTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private IServiceProvider Provider()
    {
        // ReferenceDataSeed CatalogSeed deseniyle DbContextOptions<AppDbContext>'i provider'dan çeker.
        using var probe = _db.NewContext();
        var options = ((IInfrastructure<IServiceProvider>)probe).Instance
            .GetRequiredService<IDbContextOptions>();
        return new ServiceCollection()
            .AddSingleton((DbContextOptions<AppDbContext>)options)
            .BuildServiceProvider();
    }

    [Fact]
    public void Initialize_seeds_81_provinces_with_unique_names_and_districts()
    {
        ReferenceDataSeed.Initialize(Provider());

        using var ctx = _db.NewContext();
        var provinces = ctx.Provinces.ToList();
        provinces.Count.ShouldBe(81);
        provinces.Select(p => p.Name).Distinct().Count().ShouldBe(81);
        ctx.Districts.Count().ShouldBeGreaterThan(900);
        // Her ilin en az bir ilçesi var; il içinde ilçe adı tekrar etmiyor.
        ctx.Provinces.Include(p => p.Districts).ToList()
            .ShouldAllBe(p => p.Districts.Count > 0 && p.Districts.Select(d => d.Name).Distinct().Count() == p.Districts.Count);
    }

    [Fact]
    public void Initialize_is_idempotent()
    {
        var provider = Provider();
        ReferenceDataSeed.Initialize(provider);
        ReferenceDataSeed.Initialize(provider);

        using var ctx = _db.NewContext();
        ctx.Provinces.Count().ShouldBe(81);
    }

    public void Dispose() => _db.Dispose();
}
