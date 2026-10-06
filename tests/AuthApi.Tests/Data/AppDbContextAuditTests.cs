using AuthApi.Tests.Support;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AuthApi.Tests.Data;

/// <summary>
/// issue #342: auth-api <see cref="AppDbContext"/> audit/soft-delete hook'u da bool overload'larda —
/// <c>SaveChangesAsync(acceptAllChangesOnSuccess: false)</c> ile kaydedilen satır da damgalanır.
/// </summary>
public class AppDbContextAuditTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveChangesAsync_without_accepting_changes_stamps_and_soft_deletes()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        int deletedId;
        await using (var seed = _db.NewContext())
        {
            var d = new User { Email = "d@x.local", FullName = "D", Role = "Student", KeycloakId = "kc-d" };
            seed.Users.Add(d);
            await seed.SaveChangesAsync();
            deletedId = d.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(77);
            var a = new User { Email = "a@x.local", FullName = "A", Role = "Student", KeycloakId = "kc-a" };
            ctx.Users.Add(a);
            ctx.Users.Remove(await ctx.Users.SingleAsync(u => u.Id == deletedId));
            await ctx.SaveChangesAsync(acceptAllChangesOnSuccess: false);
            // Not: acceptAllChangesOnSuccess:false iken üretilen Id entity'ye AcceptAllChanges'e kadar yazılmaz.
        }

        await using var check = _db.NewContext();
        var all = await check.Users.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(u => u.Id);
        var added = all.Values.Single(u => u.Email == "a@x.local");
        added.CreateTime.ShouldBeGreaterThan(before);
        added.CreateUserId.ShouldBe(77);
        all[deletedId].IsDeleted.ShouldBeTrue();
        all[deletedId].DeleteUserId.ShouldBe(77);
    }
}
