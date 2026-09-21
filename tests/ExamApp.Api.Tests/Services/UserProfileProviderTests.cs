using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #189: UserProfileProvider cache ve DB lookup davranışı.
///
/// - Cache miss'te auth-api'den profili alır ve SchoolContextResolver ile SchoolId'yi doldurur.
/// - Cache hit'te tekrar çağrılmaz.
/// - RemoveAsync sonrası yeni istekte yeniden yüklenir (invalidation).
/// </summary>
public class UserProfileProviderTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedSchoolAsync(string name = "Test Okulu")
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    private UserProfileProvider NewProvider(
        IDistributedCache cache,
        UserProfileCacheService cacheService,
        IAuthApiClient authApiClient,
        ISchoolContextResolver schoolContextResolver)
        => new(cacheService, authApiClient, schoolContextResolver);

    // ---- Cache Miss Tests ----

    [Fact]
    public async Task GetAsync_CacheMiss_LoadsFromAuthApiAndResolver()
    {
        var schoolId = await SeedSchoolAsync("Öğretmen Okulu");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 101, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var authApiClient = Substitute.For<IAuthApiClient>();
        var profileFromAuthApi = new UserProfileDto
        {
            Id = 101,
            KeycloakId = "kc-101",
            FullName = "Test Teacher",
            Email = "teacher@test.com",
            Role = "Teacher",
            SchoolId = null  // auth-api tarafından null dönebilir
        };
        authApiClient.GetUserProfileAsync().Returns(profileFromAuthApi);

        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();
        schoolContextResolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>())
            .Returns(async (call) =>
            {
                var user = (UserProfileDto)call[0];
                await using var ctx = _db.NewContext();
                return await ctx.Teachers
                    .Where(t => t.UserId == user.Id)
                    .Select(t => (int?)t.SchoolId)
                    .FirstOrDefaultAsync(CancellationToken.None);
            });

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cacheService = new UserProfileCacheService(cache, Substitute.For<ILogger<UserProfileCacheService>>());
        var provider = NewProvider(cache, cacheService, authApiClient, schoolContextResolver);

        var result = await provider.GetAsync("kc-101");

        result.ShouldNotBeNull();
        result.Id.ShouldBe(101);
        result.KeycloakId.ShouldBe("kc-101");
        result.Role.ShouldBe("Teacher");
        result.SchoolId.ShouldBe(schoolId);  // Resolver tarafından doldurulmuş

        // auth-api exactly once çağrılmış
        _ = authApiClient.Received(1).GetUserProfileAsync();
        // resolver exactly once çağrılmış
        await schoolContextResolver.Received(1).ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_StudentWithSchoolId_ResolverFillsSchoolId()
    {
        var schoolId = await SeedSchoolAsync("Öğrenci Okulu");
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;
            ctx.Students.Add(new Student { UserId = 201, StudentNumber = "S201", SchoolId = schoolId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        var authApiClient = Substitute.For<IAuthApiClient>();
        var profileFromAuthApi = new UserProfileDto
        {
            Id = 201,
            KeycloakId = "kc-201",
            FullName = "Test Student",
            Email = "student@test.com",
            Role = "Student",
            SchoolId = null  // auth-api tarafından null
        };
        authApiClient.GetUserProfileAsync().Returns(profileFromAuthApi);

        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();
        schoolContextResolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>())
            .Returns(async (call) =>
            {
                var user = (UserProfileDto)call[0];
                await using var ctx = _db.NewContext();
                return await ctx.Students
                    .Where(s => s.UserId == user.Id)
                    .Select(s => (int?)s.SchoolId)
                    .FirstOrDefaultAsync(CancellationToken.None);
            });

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cacheService = new UserProfileCacheService(cache, Substitute.For<ILogger<UserProfileCacheService>>());
        var provider = NewProvider(cache, cacheService, authApiClient, schoolContextResolver);

        var result = await provider.GetAsync("kc-201");

        result.ShouldNotBeNull();
        result.SchoolId.ShouldBe(schoolId);
    }

    // ---- Cache Hit Tests ----

    [Fact]
    public async Task GetAsync_CacheHit_DoesNotCallAuthApiOrResolver()
    {
        var authApiClient = Substitute.For<IAuthApiClient>();
        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cacheService = new UserProfileCacheService(cache, Substitute.For<ILogger<UserProfileCacheService>>());

        // Cache'e manuel olarak profile yazıyoruz
        var cachedProfile = new UserProfileDto
        {
            Id = 301,
            KeycloakId = "kc-301",
            FullName = "Cached Teacher",
            Email = "cached@test.com",
            Role = "Teacher",
            SchoolId = 42
        };
        await cacheService.SetAsync("kc-301", cachedProfile);

        var provider = NewProvider(cache, cacheService, authApiClient, schoolContextResolver);

        // İkinci çağrı — cache'ten okuyacak
        var result = await provider.GetAsync("kc-301");

        result.ShouldNotBeNull();
        result.Id.ShouldBe(301);
        result.SchoolId.ShouldBe(42);

        // auth-api hiç çağrılmamış
        _ = authApiClient.DidNotReceive().GetUserProfileAsync();
        // resolver hiç çağrılmamış
        await schoolContextResolver.DidNotReceive().ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>());
    }

    // ---- Invalidation (RemoveAsync) Tests ----

    [Fact]
    public async Task RemoveAsync_InvalidatesCache_NextCallReloads()
    {
        var schoolId = await SeedSchoolAsync("Transfer Okulu");
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher { UserId = 401, SchoolId = schoolId };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        var authApiClient = Substitute.For<IAuthApiClient>();
        var profileFromAuthApi = new UserProfileDto
        {
            Id = 401,
            KeycloakId = "kc-401",
            FullName = "Transfer Teacher",
            Email = "transfer@test.com",
            Role = "Teacher",
            SchoolId = null
        };
        authApiClient.GetUserProfileAsync().Returns(profileFromAuthApi);

        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();
        // İlk defa: schoolId döner; ikinci defa: farklı bir schoolId döner (transfer sonrası)
        int callCount = 0;
        schoolContextResolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                callCount++;
                var user = (UserProfileDto)call[0];
                await using var ctx = _db.NewContext();
                var resolved = await ctx.Teachers
                    .Where(t => t.UserId == user.Id)
                    .Select(t => (int?)t.SchoolId)
                    .FirstOrDefaultAsync(CancellationToken.None);
                return resolved;
            });

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cacheService = new UserProfileCacheService(cache, Substitute.For<ILogger<UserProfileCacheService>>());
        var provider = NewProvider(cache, cacheService, authApiClient, schoolContextResolver);

        // İlk çağrı
        var result1 = await provider.GetAsync("kc-401");
        result1.SchoolId.ShouldBe(schoolId);

        // Cache'i düşür (transfer sonrası)
        await cacheService.RemoveAsync("kc-401");

        // DB'deki değeri değiştir (transfer)
        var newSchoolId = await SeedSchoolAsync("Yeni Okul");
        await using (var ctx = _db.NewContext())
        {
            var teacher = await ctx.Teachers.FirstAsync(t => t.UserId == 401, CancellationToken.None);
            teacher.SchoolId = newSchoolId;
            await ctx.SaveChangesAsync();
        }

        // İkinci çağrı — cache invalidation sonrası yeniden yüklenir
        var result2 = await provider.GetAsync("kc-401");
        result2.SchoolId.ShouldBe(newSchoolId);  // Yeni okul döner

        // resolver 2 kez çağrılmış (invalidation sonrası reload)
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task RemoveAsync_NonexistentKey_DoesNotThrow()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cacheService = new UserProfileCacheService(cache, Substitute.For<ILogger<UserProfileCacheService>>());

        // Cache'te olmayan key'i remove etme — hata atılmamalı
        await cacheService.RemoveAsync("nonexistent-key");

        // Başarılı
    }

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_CallsLoaderAndCaches()
    {
        var expectedProfile = new UserProfileDto
        {
            Id = 501,
            KeycloakId = "kc-501",
            FullName = "Loader Profile",
            Email = "loader@test.com",
            Role = "Teacher",
            SchoolId = 50
        };

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cacheService = new UserProfileCacheService(cache, Substitute.For<ILogger<UserProfileCacheService>>());

        var loaderCallCount = 0;
        var result = await cacheService.GetOrSetAsync("kc-501", async () =>
        {
            loaderCallCount++;
            return await Task.FromResult(expectedProfile);
        });

        result.ShouldNotBeNull();
        result.Id.ShouldBe(501);
        loaderCallCount.ShouldBe(1);

        // İkinci çağrı cache'ten okuyacak
        var cached = await cacheService.GetAsync("kc-501");
        cached.ShouldNotBeNull();
        cached.Id.ShouldBe(501);
    }
}
