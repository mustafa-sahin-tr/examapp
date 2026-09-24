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

    // ---- issue #187 ----

    [Fact]
    public async Task Teacher_application_status_filter_is_stored_as_a_readable_string_and_null_elsewhere()
    {
        await using (var ctx = _db.NewContext())
        {
            var service = new AdminDataAccessAuditService(ctx);
            await service.RecordListAccessAsync(new AdminListAccessRecord(
                "kc", AdminDataAccessResource.TeacherApplicationList, null, false, 1, 20, 3, 3, TeacherApplicationStatusFilter.All));
            await service.RecordRateLimitedAsync(new AdminRateLimitedAccessRecord(
                "kc", AdminDataAccessResource.TeacherApplicationList, null, false, null, TeacherApplicationStatusFilter.Pending));
            await service.RecordListAccessAsync(new AdminListAccessRecord(
                "kc", AdminDataAccessResource.StudentList, null, false, 1, 20, 0, 0));
        }

        await using var read = _db.NewContext();
        var rows = await read.AdminDataAccessLogs.OrderBy(r => r.Id).ToListAsync();
        rows.Select(r => r.StatusFilter).ShouldBe(
            [TeacherApplicationStatusFilter.All, TeacherApplicationStatusFilter.Pending, null]);

        var raw = await read.Database
            .SqlQueryRaw<string>("SELECT \"StatusFilter\" AS \"Value\" FROM \"AdminDataAccessLogs\" WHERE \"StatusFilter\" IS NOT NULL ORDER BY \"Id\"")
            .ToListAsync();
        raw.ShouldBe(["All", "Pending"]);
    }

    [Fact]
    public async Task Detail_access_records_the_target_status_only_when_served()
    {
        await using (var ctx = _db.NewContext())
        {
            var service = new AdminDataAccessAuditService(ctx);
            await service.RecordDetailAccessAsync(new AdminDetailAccessRecord(
                "kc", AdminDataAccessResource.TeacherApplicationDetail, 5, AdminDataAccessOutcome.Served, "Rejected"));
            await service.RecordDetailAccessAsync(new AdminDetailAccessRecord(
                "kc", AdminDataAccessResource.TeacherApplicationDetail, 6, AdminDataAccessOutcome.NotFound, "Pending"));
            await service.RecordListAccessAsync(new AdminListAccessRecord(
                "kc", AdminDataAccessResource.TeacherApplicationList, null, false, 1, 20, 0, 0, TeacherApplicationStatusFilter.All));
        }

        await using var read = _db.NewContext();
        var rows = await read.AdminDataAccessLogs.OrderBy(r => r.Id).ToListAsync();
        rows.Select(r => r.TargetStatus).ShouldBe(["Rejected", null, null]);
    }

    // ---- issue #262 ----

    [Fact]
    public async Task List_access_is_stored_as_served_without_target()
    {
        await using (var ctx = _db.NewContext())
        {
            await new AdminDataAccessAuditService(ctx).RecordListAccessAsync(
                new AdminListAccessRecord("kc", AdminDataAccessResource.TeacherApplicationList, null, false, 1, 3, 3, 3));
        }

        await using var read = _db.NewContext();
        var row = await read.AdminDataAccessLogs.SingleAsync();
        row.Outcome.ShouldBe(AdminDataAccessOutcome.Served);
        row.TargetId.ShouldBeNull();
        row.Resource.ShouldBe(AdminDataAccessResource.TeacherApplicationList);
    }

    [Fact]
    public async Task Detail_access_writes_the_target_id()
    {
        await using (var ctx = _db.NewContext())
        {
            await new AdminDataAccessAuditService(ctx).RecordDetailAccessAsync(
                new AdminDetailAccessRecord(" kc-admin ", AdminDataAccessResource.TeacherApplicationDetail, 42));
        }

        await using var read = _db.NewContext();
        var row = await read.AdminDataAccessLogs.SingleAsync();
        row.ActorKeycloakId.ShouldBe("kc-admin");
        row.Resource.ShouldBe(AdminDataAccessResource.TeacherApplicationDetail);
        row.TargetId.ShouldBe(42);
        row.ReturnedCount.ShouldBe(1);
        row.Outcome.ShouldBe(AdminDataAccessOutcome.Served);
    }

    [Fact]
    public async Task Not_found_detail_is_persisted_with_target_and_zero_counts()
    {
        await using (var ctx = _db.NewContext())
        {
            await new AdminDataAccessAuditService(ctx).RecordDetailAccessAsync(new AdminDetailAccessRecord(
                "kc-admin", AdminDataAccessResource.TeacherApplicationDetail, 99, AdminDataAccessOutcome.NotFound));
        }

        await using var read = _db.NewContext();
        var row = await read.AdminDataAccessLogs.SingleAsync();
        row.Outcome.ShouldBe(AdminDataAccessOutcome.NotFound);
        row.TargetId.ShouldBe(99);
        row.ReturnedCount.ShouldBe(0);
        var raw = await read.Database.SqlQueryRaw<string>("SELECT \"Outcome\" AS \"Value\" FROM \"AdminDataAccessLogs\"").SingleAsync();
        raw.ShouldBe("NotFound");
    }

    [Fact]
    public async Task Detail_record_rejects_rate_limited_outcome()
    {
        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<ArgumentException>(() => new AdminDataAccessAuditService(ctx).RecordDetailAccessAsync(
            new AdminDetailAccessRecord("kc", AdminDataAccessResource.TeacherApplicationDetail, 1, AdminDataAccessOutcome.RateLimited)));
    }

    [Fact]
    public async Task Rate_limited_request_is_persisted_with_outcome_and_no_counts()
    {
        await using (var ctx = _db.NewContext())
        {
            await new AdminDataAccessAuditService(ctx).RecordRateLimitedAsync(
                new AdminRateLimitedAccessRecord("kc-admin", AdminDataAccessResource.StudentList, 5, false, null));
        }

        await using var read = _db.NewContext();
        var row = await read.AdminDataAccessLogs.SingleAsync();
        row.Outcome.ShouldBe(AdminDataAccessOutcome.RateLimited);
        row.SchoolIdFilter.ShouldBe(5);
        row.Page.ShouldBe(0);
        row.ReturnedCount.ShouldBe(0);
        row.TotalCount.ShouldBe(0);
        var raw = await read.Database.SqlQueryRaw<string>("SELECT \"Outcome\" AS \"Value\" FROM \"AdminDataAccessLogs\"").SingleAsync();
        raw.ShouldBe("RateLimited");
    }

    [Fact]
    public async Task Detail_and_rate_limited_records_also_require_an_actor()
    {
        await using var ctx = _db.NewContext();
        var service = new AdminDataAccessAuditService(ctx);

        await Should.ThrowAsync<InvalidOperationException>(() => service.RecordDetailAccessAsync(
            new AdminDetailAccessRecord(" ", AdminDataAccessResource.TeacherApplicationDetail, 1)));
        await Should.ThrowAsync<InvalidOperationException>(() => service.RecordRateLimitedAsync(
            new AdminRateLimitedAccessRecord("", AdminDataAccessResource.StudentList, null, false, null)));
        (await ctx.AdminDataAccessLogs.CountAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
