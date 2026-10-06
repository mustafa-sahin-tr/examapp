using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Data;

/// <summary>
/// issue #342: <see cref="AppDbContext"/> audit/soft-delete hook'u (ApplyAuditInfo) EF'in TÜM SaveChanges
/// overload'larında çalışır — özellikle retry-güvenli transaction tarifindeki
/// <c>SaveChangesAsync(acceptAllChangesOnSuccess: false)</c> (StudentService.Save, #277) eskiden hook'u atlıyordu:
/// yeni öğrenci satırında CreateTime default, CreateUserId null kalıyordu.
/// </summary>
public class AppDbContextAuditTests : IDisposable
{
    private const int CurrentUser = 4242;
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedGradeAsync()
    {
        await using var ctx = _db.NewContext();
        var g = new Grade { Name = "7" };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    [Fact]
    public async Task New_student_registration_stamps_CreateTime_and_CreateUserId()
    {
        var gradeId = await SeedGradeAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        ResponseBaseDto r;
        await using (var ctx = _db.NewContext())
        {
            // SetCurrentUser çağrılmaz: kendi kendine kayıtta servis kaydolan kullanıcıyı audit kullanıcısı yapar (#342 review).
            r = await new StudentService(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx))
                .Save(910, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId });
        }

        r.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        var student = await check.Students.AsNoTracking().SingleAsync(s => s.UserId == 910);
        student.CreateTime.ShouldBeGreaterThan(before);
        student.CreateUserId.ShouldBe(910);
    }

    [Fact]
    public async Task New_student_registration_after_a_transient_commit_retry_is_still_stamped()
    {
        var gradeId = await SeedGradeAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);
        var interceptor = new FailFirstCommitInterceptor();

        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
        {
            (await new StudentService(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx))
                .Save(911, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId })).Success.ShouldBeTrue();
        }

        interceptor.Failures.ShouldBe(1);
        await using var check = _db.NewContext();
        var student = await check.Students.AsNoTracking().SingleAsync(s => s.UserId == 911);
        student.CreateTime.ShouldBeGreaterThan(before);
        student.CreateUserId.ShouldBe(911);
    }

    [Fact]
    public async Task SaveChangesAsync_without_accepting_changes_stamps_added_modified_and_soft_deletes()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        int addedId, modifiedId, deletedId;
        await using (var seed = _db.NewContext())
        {
            var m = new Grade { Name = "m" };
            var d = new Grade { Name = "d" };
            seed.Grades.AddRange(m, d);
            await seed.SaveChangesAsync();
            modifiedId = m.Id;
            deletedId = d.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(CurrentUser);
            var a = new Grade { Name = "a" };
            ctx.Grades.Add(a);
            (await ctx.Grades.SingleAsync(g => g.Id == modifiedId)).Name = "m2";
            ctx.Grades.Remove(await ctx.Grades.SingleAsync(g => g.Id == deletedId));

            await ctx.SaveChangesAsync(acceptAllChangesOnSuccess: false);
            ctx.ChangeTracker.AcceptAllChanges();
            addedId = a.Id;
        }

        await using var check = _db.NewContext();
        var all = await check.Grades.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(g => g.Id);

        all[addedId].CreateTime.ShouldBeGreaterThan(before);
        all[addedId].CreateUserId.ShouldBe(CurrentUser);

        all[modifiedId].UpdateTime.ShouldNotBeNull();
        all[modifiedId].UpdateUserId.ShouldBe(CurrentUser);

        // Remove() gerçek DELETE'e değil soft-delete'e dönüşür.
        all[deletedId].IsDeleted.ShouldBeTrue();
        all[deletedId].DeleteTime.ShouldNotBeNull();
        all[deletedId].DeleteUserId.ShouldBe(CurrentUser);
    }

    [Fact]
    public async Task Sync_SaveChanges_without_accepting_changes_stamps_added_rows()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(CurrentUser);
            ctx.Grades.Add(new Grade { Name = "sync-no-accept" });
            ctx.SaveChanges(acceptAllChangesOnSuccess: false); // üretilen Id AcceptAllChanges'e kadar entity'ye yazılmaz
        }

        await using var check = _db.NewContext();
        var stored = await check.Grades.AsNoTracking().SingleAsync(g => g.Name == "sync-no-accept");
        stored.CreateTime.ShouldBeGreaterThan(before);
        stored.CreateUserId.ShouldBe(CurrentUser);
    }

    [Fact]
    public async Task Default_overloads_run_the_hook_once_soft_delete_does_not_also_stamp_update()
    {
        // Hook bool overload'a taşındı; parametresiz overload'lar ona delege eder. Çift çalışsaydı soft-delete'e
        // dönüşen (Modified) satıra ikinci geçişte UpdateTime/UpdateUserId da yazılırdı.
        int id;
        await using (var seed = _db.NewContext())
        {
            var g = new Grade { Name = "x" };
            seed.Grades.Add(g);
            await seed.SaveChangesAsync();
            id = g.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(CurrentUser);
            ctx.Grades.Remove(await ctx.Grades.SingleAsync(g => g.Id == id));
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var stored = await check.Grades.IgnoreQueryFilters().AsNoTracking().SingleAsync(g => g.Id == id);
        stored.IsDeleted.ShouldBeTrue();
        stored.DeleteUserId.ShouldBe(CurrentUser);
        stored.UpdateTime.ShouldBeNull();
        stored.UpdateUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Retried_save_does_not_restamp_a_soft_deleted_row_as_an_update()
    {
        // Retry-güvenli tarif: SaveChanges(false) değişiklikleri kabul etmez → ikinci deneme hook'u yeniden çalıştırır;
        // ilk geçişte Deleted→Modified olmuş satıra UpdateTime/UpdateUserId yazılmamalı (#342 review).
        int id;
        await using (var seed = _db.NewContext())
        {
            var g = new Grade { Name = "retry-del" };
            seed.Grades.Add(g);
            await seed.SaveChangesAsync();
            id = g.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(CurrentUser);
            ctx.Grades.Remove(await ctx.Grades.SingleAsync(g => g.Id == id));
            await ctx.SaveChangesAsync(acceptAllChangesOnSuccess: false);
            await ctx.SaveChangesAsync(acceptAllChangesOnSuccess: false); // retry
        }

        await using var check = _db.NewContext();
        var stored = await check.Grades.IgnoreQueryFilters().AsNoTracking().SingleAsync(g => g.Id == id);
        stored.IsDeleted.ShouldBeTrue();
        stored.DeleteUserId.ShouldBe(CurrentUser);
        stored.UpdateTime.ShouldBeNull();
        stored.UpdateUserId.ShouldBeNull();
    }
}
