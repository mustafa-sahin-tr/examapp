using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #187: admin başvuru listesi — <c>status=pending|all</c> filtresi, sıralama (bekleyenler en eski önce, karar
/// verilmişler en yeni karar önce), sayfalama ve her durumdaki başvurunun detayı.
/// </summary>
public class TeacherApprovalServiceListTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public TeacherApprovalServiceListTests()
    {
        // Her kullanıcı "U{id}" adıyla ve u{id}@okul.k12.tr e-postasıyla çözülür.
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto { Id = id, FullName = $"U{id}", Email = $"u{id}@okul.k12.tr" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private TeacherApprovalService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private async Task<int> SchoolAsync(string name)
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    /// <summary>Öğretmeni ekler ve başvuru zamanını (CreateTime; SaveChanges'te "şimdi"ye ezilir) sabitler.</summary>
    private async Task<int> TeacherAsync(Teacher teacher, DateTime appliedAt)
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(teacher);
        await ctx.SaveChangesAsync();
        await ctx.Teachers.Where(t => t.Id == teacher.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreateTime, appliedAt));
        return teacher.Id;
    }

    private async Task DecisionLogAsync(int teacherId, AdminUserAction action, DateTime at,
        AdminUserActionOutcome outcome = AdminUserActionOutcome.Succeeded)
    {
        await using var ctx = _db.NewContext();
        ctx.AdminUserActionLogs.Add(new AdminUserActionLog
        {
            ActorKeycloakId = "kc-admin",
            Action = action,
            TargetType = AdminUserTargetType.Teacher,
            TargetId = teacherId,
            Outcome = outcome,
            OccurredAtUtc = at
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Karışık veri seti. Beklenen "all" sırası: pendingOld, pendingNew, approvedLate, rejectedMid, approvedSchool,
    /// rejectedNoLog (karar anı bilinmiyor → sonda).
    /// </summary>
    private async Task<Dictionary<string, int>> SeedMixedAsync()
    {
        var schoolA = await SchoolAsync("A Lisesi");
        var schoolB = await SchoolAsync("B Lisesi");
        var ids = new Dictionary<string, int>();

        ids["pendingNew"] = await TeacherAsync(new Teacher
        { UserId = 1, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending }, T0.AddDays(5));
        ids["pendingOld"] = await TeacherAsync(new Teacher
        { UserId = 2, IsIndependentTutor = false, RequestedSchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Pending }, T0.AddDays(1));

        ids["approvedLate"] = await TeacherAsync(new Teacher
        { UserId = 3, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved }, T0);
        await DecisionLogAsync(ids["approvedLate"], AdminUserAction.TeacherApproved, T0.AddDays(30));

        // Okul talebi reddedildi: RequestedSchoolId korunur, gerekçe dolu.
        ids["rejectedMid"] = await TeacherAsync(new Teacher
        {
            UserId = 4, IsIndependentTutor = false, RequestedSchoolId = schoolB,
            ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "Belge eksik"
        }, T0);
        await DecisionLogAsync(ids["rejectedMid"], AdminUserAction.TeacherRejected, T0.AddDays(20));

        // Onaylanmış okul talebi: onay RequestedSchoolId'yi temizleyip SchoolId'ye taşıdı → yalnızca audit'ten tanınır.
        // Önce reddedilmiş, sonra yeni talebi onaylanmış: karar anı ONAY log'udur (eşleşen en son karar).
        ids["approvedSchool"] = await TeacherAsync(new Teacher
        { UserId = 5, IsIndependentTutor = false, SchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Approved }, T0);
        await DecisionLogAsync(ids["approvedSchool"], AdminUserAction.TeacherRejected, T0.AddDays(12));
        await DecisionLogAsync(ids["approvedSchool"], AdminUserAction.TeacherApproved, T0.AddDays(10));

        // #157 öncesi red: audit yok → decidedAt null, karar verilmişlerin sonunda.
        ids["rejectedNoLog"] = await TeacherAsync(new Teacher
        {
            UserId = 6, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "Eski red"
        }, T0);

        // Başvuru OLMAYANLAR: sıradan okul öğretmeni, başka aksiyonun audit'i (şifre sıfırlama), başarısız karar kaydı.
        ids["ordinary"] = await TeacherAsync(new Teacher
        { UserId = 7, IsIndependentTutor = false, SchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Approved }, T0);
        ids["passwordReset"] = await TeacherAsync(new Teacher
        { UserId = 8, IsIndependentTutor = false, SchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Approved }, T0);
        await DecisionLogAsync(ids["passwordReset"], AdminUserAction.PasswordReset, T0.AddDays(3));
        await DecisionLogAsync(ids["passwordReset"], AdminUserAction.TeacherApproved, T0.AddDays(3), AdminUserActionOutcome.Requested);

        return ids;
    }

    [Fact]
    public async Task Pending_filter_returns_only_pending_applications_oldest_first()
    {
        var ids = await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 20);

        page.TotalCount.ShouldBe(2);
        page.PageNumber.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.Items.Select(i => i.TeacherId).ShouldBe([ids["pendingOld"], ids["pendingNew"]]);
        page.Items.ShouldAllBe(i => i.Status == "Pending" && i.DecidedAt == null && i.RejectionReason == null);
    }

    [Fact]
    public async Task All_filter_returns_every_application_pending_first_then_newest_decision_first()
    {
        var ids = await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).ListApplicationsAsync(TeacherApplicationStatusFilter.All, 1, 20);

        page.TotalCount.ShouldBe(6);
        page.Items.Select(i => i.TeacherId).ShouldBe(
        [
            ids["pendingOld"], ids["pendingNew"], ids["approvedLate"], ids["rejectedMid"], ids["approvedSchool"], ids["rejectedNoLog"]
        ]);
        page.Items.ShouldNotContain(i => i.TeacherId == ids["ordinary"] || i.TeacherId == ids["passwordReset"]);
    }

    [Fact]
    public async Task All_filter_maps_status_reason_decision_time_and_school()
    {
        var ids = await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var items = (await NewService(ctx).ListApplicationsAsync(TeacherApplicationStatusFilter.All, 1, 20))
            .Items.ToDictionary(i => i.TeacherId);

        var rejected = items[ids["rejectedMid"]];
        rejected.Status.ShouldBe("Rejected");
        rejected.RejectionReason.ShouldBe("Belge eksik");
        rejected.DecidedAt.ShouldBe(T0.AddDays(20));
        rejected.RequestedSchoolName.ShouldBe("B Lisesi");
        rejected.IsIndependentTutor.ShouldBeFalse();
        rejected.Email.ShouldBe("u***@okul.k12.tr"); // liste hâlâ maskeli (#262)

        var approvedSchool = items[ids["approvedSchool"]];
        approvedSchool.Status.ShouldBe("Approved");
        approvedSchool.RejectionReason.ShouldBeNull();
        approvedSchool.DecidedAt.ShouldBe(T0.AddDays(10)); // eşleşen (TeacherApproved) log; daha yeni red log'u değil
        approvedSchool.RequestedSchoolName.ShouldBe("A Lisesi"); // onayda SchoolId'ye taşınan talep

        var approvedIndependent = items[ids["approvedLate"]];
        approvedIndependent.Status.ShouldBe("Approved");
        approvedIndependent.RequestedSchoolId.ShouldBeNull();
        approvedIndependent.RequestedSchoolName.ShouldBeNull();

        var noLog = items[ids["rejectedNoLog"]];
        noLog.Status.ShouldBe("Rejected");
        noLog.DecidedAt.ShouldBeNull();
        noLog.RejectionReason.ShouldBe("Eski red");

        items[ids["pendingOld"]].DecidedAt.ShouldBeNull();
        items[ids["pendingOld"]].AppliedAt.ShouldBe(T0.AddDays(1));
    }

    [Fact]
    public async Task All_filter_is_paged_with_stable_order_and_total()
    {
        var ids = await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var second = await service.ListApplicationsAsync(TeacherApplicationStatusFilter.All, 2, 2);
        second.PageNumber.ShouldBe(2);
        second.PageSize.ShouldBe(2);
        second.TotalCount.ShouldBe(6);
        second.Items.Select(i => i.TeacherId).ShouldBe([ids["approvedLate"], ids["rejectedMid"]]);

        var third = await service.ListApplicationsAsync(TeacherApplicationStatusFilter.All, 3, 2);
        third.Items.Select(i => i.TeacherId).ShouldBe([ids["approvedSchool"], ids["rejectedNoLog"]]);
    }

    [Fact]
    public async Task Page_beyond_total_is_empty_and_paging_is_normalized_without_user_lookup()
    {
        await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var beyond = await NewService(ctx).ListApplicationsAsync(TeacherApplicationStatusFilter.All, int.MaxValue, 500);

        beyond.Items.ShouldBeEmpty();
        beyond.TotalCount.ShouldBe(6);
        beyond.PageNumber.ShouldBe(int.MaxValue);
        beyond.PageSize.ShouldBe(100); // AdminListPaging.MaxPageSize'a kırpıldı
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);

        var normalized = await NewService(ctx).ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 0, 0);
        normalized.PageNumber.ShouldBe(1);
        normalized.PageSize.ShouldBe(1);
        normalized.Items.Count.ShouldBe(1);
        normalized.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Detail_returns_a_rejected_application_with_masked_email_status_and_reason()
    {
        var ids = await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var detail = await NewService(ctx).GetApplicationAsync(ids["rejectedMid"]);

        detail.ShouldNotBeNull();
        detail.TeacherId.ShouldBe(ids["rejectedMid"]);
        detail.Email.ShouldBe("u***@okul.k12.tr"); // security review #187: karar verilmişte maskeli
        detail.Status.ShouldBe("Rejected");
        detail.RejectionReason.ShouldBe("Belge eksik");
        detail.DecidedAt.ShouldBe(T0.AddDays(20));
        detail.RequestedSchoolName.ShouldBe("B Lisesi");
    }

    [Fact]
    public async Task Detail_returns_approved_school_application_identified_by_audit_and_404s_non_applications()
    {
        var ids = await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var approved = await service.GetApplicationAsync(ids["approvedSchool"]);
        approved.ShouldNotBeNull();
        approved.Status.ShouldBe("Approved");
        approved.Email.ShouldBe("u***@okul.k12.tr"); // karar verilmiş → maskeli

        (await service.GetApplicationAsync(ids["ordinary"])).ShouldBeNull();
        (await service.GetApplicationAsync(ids["passwordReset"])).ShouldBeNull();
    }

    [Fact]
    public async Task Detail_returns_the_full_email_only_while_the_application_is_pending()
    {
        var ids = await SeedMixedAsync();

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.GetApplicationAsync(ids["pendingOld"]))!.Email.ShouldBe("u2@okul.k12.tr");
        (await service.GetApplicationAsync(ids["pendingNew"]))!.Email.ShouldBe("u1@okul.k12.tr");
        (await service.GetApplicationAsync(ids["approvedLate"]))!.Email.ShouldBe("u***@okul.k12.tr");
        (await service.GetApplicationAsync(ids["rejectedNoLog"]))!.Email.ShouldBe("u***@okul.k12.tr");
    }

    [Fact]
    public async Task Unknown_filter_value_is_rejected()
    {
        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            NewService(ctx).ListApplicationsAsync((TeacherApplicationStatusFilter)99, 1, 20));
    }
}
