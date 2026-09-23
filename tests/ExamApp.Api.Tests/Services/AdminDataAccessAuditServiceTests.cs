using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #246: admin liste erişim kaydı — satır olarak yazılır, yalnızca sub + filtre + sayfa + sayı içerir.
/// </summary>
public class AdminDataAccessAuditServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    [Fact]
    public async Task Writes_one_row_with_sub_filter_page_and_counts()
    {
        var before = DateTime.UtcNow;
        await using (var ctx = _db.NewContext())
        {
            await new AdminDataAccessAuditService(ctx).RecordListAccessAsync(
                new AdminListAccessRecord(" kc-admin-sub ", AdminDataAccessResource.StudentList, 5, false, 2, 100, 37, 137));
        }

        await using var read = _db.NewContext();
        var row = await read.AdminDataAccessLogs.SingleAsync();
        row.ActorKeycloakId.ShouldBe("kc-admin-sub");
        row.Resource.ShouldBe(AdminDataAccessResource.StudentList);
        row.SchoolIdFilter.ShouldBe(5);
        row.UnassignedFilter.ShouldBeFalse();
        row.Page.ShouldBe(2);
        row.PageSize.ShouldBe(100);
        row.ReturnedCount.ShouldBe(37);
        row.TotalCount.ShouldBe(137);
        row.OccurredAtUtc.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
    }

    [Fact]
    public async Task Resource_is_stored_as_a_readable_string()
    {
        await using (var ctx = _db.NewContext())
        {
            await new AdminDataAccessAuditService(ctx).RecordListAccessAsync(
                new AdminListAccessRecord("kc", AdminDataAccessResource.TeacherList, null, true, 1, 20, 0, 0));
        }

        await using var read = _db.NewContext();
        var raw = await read.Database.SqlQueryRaw<string>("SELECT \"Resource\" AS \"Value\" FROM \"AdminDataAccessLogs\"").SingleAsync();
        raw.ShouldBe("TeacherList");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_actor_is_rejected_so_the_list_is_not_served_unaudited(string actor)
    {
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<InvalidOperationException>(() => new AdminDataAccessAuditService(ctx).RecordListAccessAsync(
            new AdminListAccessRecord(actor, AdminDataAccessResource.StudentList, null, false, 1, 20, 0, 0)));

        (await ctx.AdminDataAccessLogs.CountAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
