using System.Net.Http;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #305 (dilim B review): Y1 — kök yazarı öğretmenin uygunluğu GÜNCEL okuluna göre; O2 — profilsiz/silinmiş alıcı yalnız
/// Keycloak'ta Admin olan worksheet sahibiyse; atlanan bildirimin logunda kayıtlı yorum Id'si. Yardımcılar
/// <c>WorksheetCommentServiceTests.cs</c> / <c>WorksheetCommentModerationServiceTests.cs</c> / <c>WorksheetCommentNotificationRevalidationTests.cs</c>'te.
/// </summary>
public partial class WorksheetCommentServiceTests
{
    private async Task AnnouncementThenStudentReplyAsync(World w, Func<Task> mutate)
    {
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru")); // sahip, X okulu: AuthorSchoolId = X
        await mutate();
        await ClearOutboxAsync();
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru", parentId: announcement));
    }

    [Fact]
    public async Task Root_author_teacher_who_became_independent_after_the_announcement_gets_no_notification()
    {
        var w = await SeedAsync();

        await AnnouncementThenStudentReplyAsync(w, () => SetTeacherSchoolAsync(Owner, null));

        (await CreatedRecipientsAsync()).ShouldBe(new[] { Assigner }); // yalnız öğrencinin sorumlusu
    }

    [Fact]
    public async Task Root_author_teacher_moved_to_another_school_by_admin_gets_no_notification()
    {
        var w = await SeedAsync();

        await AnnouncementThenStudentReplyAsync(w, () => SetTeacherSchoolAsync(Owner, w.OtherSchoolId));

        (await CreatedRecipientsAsync()).ShouldBe(new[] { Assigner });
    }

    [Fact]
    public async Task Root_author_teacher_still_in_the_same_school_keeps_getting_notified()
    {
        var w = await SeedAsync();

        await AnnouncementThenStudentReplyAsync(w, () => Task.CompletedTask);

        (await CreatedRecipientsAsync()).OrderBy(x => x).ShouldBe(new[] { Owner, Assigner });
    }

    /// <summary>
    /// issue #334: okulsuz (bağımsız) atayan yalnız okulsuz öğrencisinde sorumlu ve bildirim alıcısı; okullu öğrencide sorumlu
    /// aynı okuldaki sahibe düşer (#326 fallback'i).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Independent_assigner_is_notified_only_for_a_schoolless_student(bool studentSchoolless)
    {
        var w = await SeedAsync();
        await SetTeacherSchoolAsync(Assigner, null);
        if (studentSchoolless)
        {
            await using var ctx = _db.NewContext();
            await ctx.Students.Where(s => s.UserId == StudentAUser).ExecuteUpdateAsync(s => s.SetProperty(x => x.SchoolId, (int?)null));
        }

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        (await CreatedRecipientsAsync()).ShouldBe(new[] { studentSchoolless ? Assigner : Owner });
    }

    [Fact]
    public async Task Soft_deleted_pinned_teacher_gets_no_notification()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.UserId == Assigner).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        await ClearOutboxAsync();

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: root));

        (await OutboxAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// Öğrencinin aktif atamasını sahip yapmış → sorumlu sahip (atamadan); sahibin Teacher profili silinmiş; öğrenci yeni kök
    /// yazar. issue #326: profilsiz sahip okulsuz sayılır → sahip FALLBACK'i artık sorumlu vermez; admin sahibin bildirim yolu
    /// yalnız atamadan gelen sabitte anlamlı. issue #334: profilsiz atayan okulsuz sayılır → atama sabiti yalnız okulsuz
    /// öğrencide geçerli (bağımsız istisnası), bu yüzden A okulsuz yapılır.
    /// </summary>
    private async Task<int> WriteRootPinnedToProfilelessOwnerAsync(World w, IKeycloakService? keycloak, bool studentSchoolless = true)
    {
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru")); // sahibin sub'ı exam DB'ye yazılır
        if (studentSchoolless)
        {
            await using var ctx = _db.NewContext();
            await ctx.Students.Where(s => s.UserId == StudentAUser).ExecuteUpdateAsync(s => s.SetProperty(x => x.SchoolId, (int?)null));
        }
        await using (var ctx = _db.NewContext())
            await ctx.WorksheetAssignments.Where(a => a.Id == w.AssignmentId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreateUserId, (int?)Owner));
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.UserId == Owner).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        await ClearOutboxAsync();

        await using var svcCtx = _db.NewContext();
        var service = new WorksheetCommentService(svcCtx, new WorksheetResponsibleTeacherResolver(svcCtx), _authApi,
            teacherGuard: new ApprovedTeacherGuard(svcCtx), keycloak: keycloak);
        return Created(await service.CreateAsync(w.WorksheetId, new CreateWorksheetCommentDto { Body = "soru" }, Student(StudentAUser)));
    }

    /// <summary>
    /// CR-2 (kabul): profilsiz admin atayan + OKULLU öğrenci → atayan okulsuz sayılır, okul koşulu sağlanmaz; sahip fallback'i
    /// de profilsiz (okulsuz) → sabit yok, bildirim yok (admin okul öğretmeni değil, #326 sahip kuralıyla tutarlı).
    /// </summary>
    [Fact]
    public async Task Profileless_admin_assigner_gets_no_pin_and_no_notification_for_a_school_student()
    {
        var w = await SeedAsync();

        var root = await WriteRootPinnedToProfilelessOwnerAsync(w, KeycloakWithRoles("Admin"), studentSchoolless: false);

        (await PinOfAsync(root)).ShouldBeNull();
        (await OutboxAsync()).ShouldBeEmpty();
    }

    private static IKeycloakService KeycloakWithRoles(params string[] realmRoles)
    {
        var kc = Substitute.For<IKeycloakService>();
        kc.GetUserRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KeycloakUserRolesDto(realmRoles, Array.Empty<string>()));
        return kc;
    }

    [Fact]
    public async Task Profileless_owner_is_notified_only_when_keycloak_says_admin()
    {
        var w = await SeedAsync();
        await WriteRootPinnedToProfilelessOwnerAsync(w, KeycloakWithRoles("Admin"));

        (await CreatedRecipientsAsync()).ShouldBe(new[] { Owner });
    }

    [Fact]
    public async Task Profileless_owner_who_is_not_admin_gets_no_notification()
    {
        var w = await SeedAsync();
        await WriteRootPinnedToProfilelessOwnerAsync(w, KeycloakWithRoles("Teacher"));

        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Profileless_owner_gets_no_notification_when_the_admin_role_cannot_be_verified()
    {
        var w = await SeedAsync();
        var kc = Substitute.For<IKeycloakService>();
        kc.GetUserRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<KeycloakUserRolesDto>>(_ => throw new HttpRequestException("keycloak down"));

        await WriteRootPinnedToProfilelessOwnerAsync(w, kc);

        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Profileless_owner_gets_no_notification_without_a_keycloak_service()
    {
        var w = await SeedAsync();

        await WriteRootPinnedToProfilelessOwnerAsync(w, keycloak: null); // fail-closed

        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Skipped_teacher_notification_is_logged_with_the_saved_comment_id()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        await EndAssignmentAsync(w.AssignmentId);
        var logger = new ListLogger<WorksheetCommentService>();

        int replyId;
        await using (var ctx = _db.NewContext())
        {
            var r = await new WorksheetCommentService(ctx, new WorksheetResponsibleTeacherResolver(ctx), _authApi, logger)
                .CreateAsync(w.WorksheetId, new CreateWorksheetCommentDto { Body = "ek", ParentCommentId = root }, Student(StudentAUser));
            replyId = Created(r);
        }

        var message = logger.AllMessages.Single(m => m.Contains("atlandı"));
        message.ShouldContain($"CommentId={replyId}");
        message.ShouldNotContain("CommentId=0");
    }
}
