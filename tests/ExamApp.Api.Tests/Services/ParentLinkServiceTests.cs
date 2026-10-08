using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #436 (epic #435, #419 üzerine): veli-öncelikli bağlantı modeli. Birincil veli ikinci veli kodu üretir (hash'li, 12
/// karakter, 7 gün, çocuk başına tek geçerli kod, çakışmada yazmama); ikinci veli kodu girince Pending (Origin=InviteCode),
/// birincil veli onaylayınca Active + ParentLinkedEvent. Birincil olmayan veli kod üretemez / onaylayamaz / koparamaz (403),
/// çocuğa bağlı olmayan 404. Öğrenci yeni isteği göremez/onaylayamaz; yalnız geçiş dönemindeki LegacyV1 isteği 30 gün içinde.
/// Birincil koparılınca ya da hesabı silinince birincillik en eski Active veliye geçer. #419'dan korunanlar: tek kullanım, genel
/// hata (kâhin yok), velinin kendi tavanı kod aranmadan önce, en fazla 4 açık veli, başarısızlık sayacı, silinmiş taraf tavan
/// tüketmez. Süpürücü job: süresi dolan Pending'i kapatır, birincili onarır. SQLite (gerçek ilişkisel davranış); advisory lock
/// ve Postgres eşzamanlılığı ParentLinkEndpointsTests'te, migration backfill'i MigrationParentLinkOriginAndPrimaryTests'te.
/// </summary>
public class ParentLinkServiceTests : IDisposable
{
    private const int StudentUser = 7001;
    private const int OtherStudentUser = 7002;
    private const int ParentUser = 7101; // 7101..7106
    private const int StrangerUser = 7999;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly ParentInviteCodeHasher _hasher = ParentInviteCodeHasherTests.Create();
    private readonly ParentRedeemAttemptGuard _guard;

    public ParentLinkServiceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id,
                KeycloakId = $"kc-{id}",
                FullName = id switch
                {
                    StudentUser => "Ayşe Kaya",
                    OtherStudentUser => "Mert Can",
                    >= ParentUser and < ParentUser + 10 => $"Veli {id - ParentUser + 1}",
                    _ => $"Kullanıcı {id}"
                },
                Email = $"veli{id}@gmail.com"
            }).ToList());
        _guard = NewGuard(new ParentRedeemGuardOptions());
    }

    public void Dispose() => _db.Dispose();

    private DateTime Now => _time.Now.UtcDateTime;

    private ParentRedeemAttemptGuard NewGuard(ParentRedeemGuardOptions options)
    {
        var monitor = Substitute.For<IOptionsMonitor<ParentRedeemGuardOptions>>();
        monitor.CurrentValue.Returns(options);
        return new ParentRedeemAttemptGuard(new InMemoryFixedWindowCounterStore(_time), monitor,
            new ConfigurationBuilder().Build(), NullLogger<ParentRedeemAttemptGuard>.Instance, _time);
    }

    private ParentLinkService Service(AppDbContext ctx, IParentInviteCodeHasher? hasher = null, IParentRedeemAttemptGuard? guard = null)
        => new(ctx, hasher ?? _hasher, _authApi, guard ?? _guard, _time);

    private async Task SeedAsync(int parents = 6)
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk Ortaokulu" };
        var grade = new Grade { Name = "7. Sınıf" };
        ctx.AddRange(school, grade);
        await ctx.SaveChangesAsync();

        ctx.Students.AddRange(
            new Student { UserId = StudentUser, StudentNumber = "s1", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id },
            new Student { UserId = OtherStudentUser, StudentNumber = "s2", SchoolId = school.Id, SchoolName = "Eski Okul" }); // bekleyen okul (#361)
        for (var i = 0; i < parents; i++)
            ctx.Parents.Add(new Parent { UserId = ParentUser + i });
        await ctx.SaveChangesAsync();
    }

    private Task<int> StudentIdAsync(int studentUserId = StudentUser)
        => InDbAsync(db => db.Students.Where(s => s.UserId == studentUserId).Select(s => s.Id).SingleAsync());

    private Task<int> ParentIdAsync(int parentUserId)
        => InDbAsync(db => db.Parents.Where(p => p.UserId == parentUserId).Select(p => p.Id).SingleAsync());

    /// <summary>
    /// Doğrudan DB'ye bağlantı satırı — veli-öncelikli hesap açma (#438) ya da migration'la gelen eski satırlar bu PR'da HTTP ile
    /// kurulamaz; birincil veli böyle tohumlanır.
    /// </summary>
    private async Task<int> SeedLinkAsync(
        int parentUserId, int studentUserId = StudentUser, ParentStudentLinkStatus status = ParentStudentLinkStatus.Active,
        ParentStudentLinkOrigin origin = ParentStudentLinkOrigin.ParentCreated, bool isPrimary = true, DateTime? createdAt = null)
    {
        var sid = await StudentIdAsync(studentUserId);
        var pid = await ParentIdAsync(parentUserId);
        var at = createdAt ?? Now;
        return await InDbAsync(async db =>
        {
            var link = new ParentStudentLink
            {
                ParentId = pid, StudentId = sid, Status = status, Origin = origin, IsPrimary = isPrimary, CreatedAt = at,
                ActivatedAt = status == ParentStudentLinkStatus.Active ? at : null
            };
            db.ParentStudentLinks.Add(link);
            await db.SaveChangesAsync();
            return link.Id;
        });
    }

    private async Task<ParentInviteCodeResultDto> CreateCodeAsync(int linkId, int parentUserId)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).CreateSecondParentCodeAsync(linkId, parentUserId);
    }

    private async Task<string> NewCodeAsync(int primaryLinkId, int primaryUser = ParentUser)
    {
        var result = await CreateCodeAsync(primaryLinkId, primaryUser);
        result.Success.ShouldBeTrue(result.Message);
        return result.Code!;
    }

    private async Task<RedeemParentInviteCodeResultDto> RedeemAsync(int parentUserId, string? code, IParentRedeemAttemptGuard? guard = null)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx, guard: guard).RedeemAsync(parentUserId, code);
    }

    /// <summary>Birincil velinin kodu → ikinci veli redeem (Pending). Bağlantı id'sini döner.</summary>
    private async Task<int> RequestAsync(int parentUserId, int primaryLinkId, int primaryUser = ParentUser)
    {
        var result = await RedeemAsync(parentUserId, await NewCodeAsync(primaryLinkId, primaryUser));
        result.Success.ShouldBeTrue(result.Message);
        return result.ObjectId;
    }

    private async Task<ParentLinkResponseDto> ApproveAsync(int linkId, int parentUserId = ParentUser)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).ApproveSecondParentAsync(linkId, parentUserId);
    }

    /// <summary>Kod → redeem (Pending) → birincil veli onayı (Active).</summary>
    private async Task<int> LinkSecondAsync(int parentUserId, int primaryLinkId, int primaryUser = ParentUser)
    {
        var linkId = await RequestAsync(parentUserId, primaryLinkId, primaryUser);
        (await ApproveAsync(linkId, primaryUser)).Success.ShouldBeTrue();
        return linkId;
    }

    private async Task<ParentLinkResponseDto> RevokeAsync(int linkId, int parentUserId)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).RevokeAsync(linkId, parentUserId);
    }

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        await using var ctx = _db.NewContext();
        return await work(ctx);
    }

    private Task<int> PrimaryLinkIdAsync(int studentUserId = StudentUser) => InDbAsync(db => db.ParentStudentLinks
        .Where(l => l.Student.UserId == studentUserId && l.IsPrimary && l.Status == ParentStudentLinkStatus.Active)
        .Select(l => l.Id).SingleAsync());

    private Task<int> EventCountAsync<T>() where T : class
        => InDbAsync(db => db.OutboxMessages.CountAsync(o => o.Type == OutboxEventRegistry.NameFor<T>()));

    // ---- ikinci veli kodu --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Second_parent_code_is_twelve_chars_stored_only_as_hash_valid_seven_days_and_records_the_primary()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);

        var result = await CreateCodeAsync(primary, ParentUser);

        result.Success.ShouldBeTrue();
        result.Code!.Length.ShouldBe(12);
        result.ExpiresAt.ShouldBe(Now.AddDays(7));
        var row = await InDbAsync(db => db.ParentInviteCodes.SingleAsync());
        row.CodeHash.ShouldBe(_hasher.Hash(result.Code));
        row.CodeHash.ShouldNotContain(result.Code, Case.Insensitive);
        row.UsedAt.ShouldBeNull();
        row.StudentId.ShouldBe(await StudentIdAsync());
        row.CreatedByParentId.ShouldBe(await ParentIdAsync(ParentUser));
    }

    [Fact]
    public async Task New_code_invalidates_the_previous_one()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var first = await NewCodeAsync(primary);
        var second = await NewCodeAsync(primary);

        (await RedeemAsync(ParentUser + 1, first)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await RedeemAsync(ParentUser + 1, second)).Success.ShouldBeTrue();
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync(c => c.UsedAt == null && c.ExpiresAt > Now))).ShouldBe(0);
    }

    [Fact]
    public async Task Code_needs_the_callers_own_active_link_otherwise_not_found()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var otherChild = await SeedLinkAsync(ParentUser + 1, OtherStudentUser);
        var pending = await SeedLinkAsync(ParentUser + 2, OtherStudentUser, ParentStudentLinkStatus.Pending,
            ParentStudentLinkOrigin.InviteCode, isPrimary: false);

        (await CreateCodeAsync(otherChild, ParentUser)).NotFound.ShouldBeTrue(); // başkasının bağlantısı (IDOR)
        (await CreateCodeAsync(pending, ParentUser + 2)).NotFound.ShouldBeTrue(); // kendi ama Pending
        (await CreateCodeAsync(primary + 999, ParentUser)).NotFound.ShouldBeTrue();
        var noProfile = await CreateCodeAsync(primary, StrangerUser);
        noProfile.NotFound.ShouldBeTrue();
        noProfile.ErrorCode.ShouldBe(ParentLinkErrorCodes.ProfileNotFound);
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Non_primary_parent_cannot_create_a_second_parent_code()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var second = await LinkSecondAsync(ParentUser + 1, primary);

        var result = await CreateCodeAsync(second, ParentUser + 1);

        result.Forbidden.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.NotPrimaryParent);
        result.Message.ShouldBe("Bu işlemi yalnızca çocuğun birincil velisi yapabilir.");
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync(c => c.UsedAt == null && c.ExpiresAt > Now))).ShouldBe(0);
    }

    [Fact]
    public async Task Exhausted_collision_retries_fail_without_inserting_a_code()
    {
        await SeedAsync();
        var otherPrimary = await SeedLinkAsync(ParentUser + 1, OtherStudentUser);
        var existing = await NewCodeAsync(otherPrimary, ParentUser + 1);
        var primary = await SeedLinkAsync(ParentUser);
        var colliding = Substitute.For<IParentInviteCodeHasher>();
        colliding.Generate().Returns(existing);
        colliding.Hash(Arg.Any<string>()).Returns(ci => _hasher.Hash(ci.Arg<string>()));

        await using var ctx = _db.NewContext();
        var result = await Service(ctx, hasher: colliding).CreateSecondParentCodeAsync(primary, ParentUser);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.Busy);
        colliding.Received(ParentLinkService.MaxGenerateAttempts).Generate();
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync())).ShouldBe(1); // yalnız diğer öğrencinin kodu
    }

    [Fact]
    public async Task Student_keeps_at_most_four_open_parents_including_pending_requests()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        await LinkSecondAsync(ParentUser + 1, primary);
        await RequestAsync(ParentUser + 2, primary);
        await RequestAsync(ParentUser + 3, primary); // 2 Active + 2 Pending = 4

        var full = await CreateCodeAsync(primary, ParentUser);
        full.Conflict.ShouldBeTrue();
        full.ErrorCode.ShouldBe(ParentLinkErrorCodes.StudentLimitReached);

        // Bekleyen istek süresi dolunca yer açılır.
        _time.Now = _time.Now.AddDays(8);
        (await CreateCodeAsync(primary, ParentUser)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Soft_deleted_parent_does_not_consume_the_students_limit()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        for (var i = 1; i < 4; i++)
            await LinkSecondAsync(ParentUser + i, primary);
        (await CreateCodeAsync(primary, ParentUser)).ErrorCode.ShouldBe(ParentLinkErrorCodes.StudentLimitReached);

        await using (var ctx = _db.NewContext())
        {
            ctx.Parents.Remove(await ctx.Parents.SingleAsync(p => p.UserId == ParentUser + 3)); // soft-delete (audit hook)
            await ctx.SaveChangesAsync();
        }

        (await CreateCodeAsync(primary, ParentUser)).Success.ShouldBeTrue();
        await using var read = _db.NewContext();
        (await Service(read).GetStudentParentsAsync(StudentUser))!.Items.Count.ShouldBe(3);
    }

    // ---- redeem → Pending → birincil veli onayı ------------------------------------------------------------------------

    [Fact]
    public async Task Redeem_creates_an_invite_code_pending_link_consumes_code_and_reveals_no_student_data_or_event()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var code = await NewCodeAsync(primary);
        _authApi.ClearReceivedCalls();

        var result = await RedeemAsync(ParentUser + 1, code.ToLowerInvariant().Insert(8, "-").Insert(4, "-"));

        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("İstek gönderildi. Bağlantı, çocuğun birincil velisi onayladığında etkinleşir.");
        result.Child!.Status.ShouldBe("Pending");
        result.Child.StudentId.ShouldBeNull();
        result.Child.StudentName.ShouldBeNull();
        result.Child.GradeName.ShouldBeNull();
        result.Child.SchoolName.ShouldBeNull();
        result.Child.PendingExpiresAt.ShouldBe(Now.AddDays(7));

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == result.ObjectId));
        link.Status.ShouldBe(ParentStudentLinkStatus.Pending);
        link.Origin.ShouldBe(ParentStudentLinkOrigin.InviteCode);
        link.IsPrimary.ShouldBeFalse();
        link.ActivatedAt.ShouldBeNull();
        var invite = await InDbAsync(db => db.ParentInviteCodes.SingleAsync());
        invite.UsedAt.ShouldNotBeNull();
        invite.UsedByParentId.ShouldBe(await ParentIdAsync(ParentUser + 1));
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0); // event yalnız Active'e geçişte

        await using var ctx = _db.NewContext();
        var children = await Service(ctx).GetParentChildrenAsync(ParentUser + 1);
        var pending = children!.ShouldHaveSingleItem();
        pending.StudentName.ShouldBeNull();
        pending.CoParents.ShouldBeEmpty();
        pending.IsPrimary.ShouldBeFalse();
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);
    }

    [Fact]
    public async Task Primary_parent_approval_activates_the_link_and_writes_ParentLinkedEvent()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var linkId = await RequestAsync(ParentUser + 1, primary);

        (await ApproveAsync(linkId)).Success.ShouldBeTrue();
        (await ApproveAsync(linkId)).Success.ShouldBeTrue(); // idempotent

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == linkId));
        link.Status.ShouldBe(ParentStudentLinkStatus.Active);
        link.ActivatedAt.ShouldBe(Now);
        link.IsPrimary.ShouldBeFalse(); // birincil değişmez
        (await PrimaryLinkIdAsync()).ShouldBe(primary);

        var outbox = await InDbAsync(db => db.OutboxMessages.ToListAsync());
        outbox.ShouldHaveSingleItem().Type.ShouldBe(OutboxEventRegistry.NameFor<ParentLinkedEvent>());
        var evt = JsonSerializer.Deserialize<ParentLinkedEvent>(outbox[0].Content)!;
        evt.LinkId.ShouldBe(linkId);
        evt.ParentUserId.ShouldBe(ParentUser + 1);
        evt.ParentId.ShouldBe(await ParentIdAsync(ParentUser + 1));
        evt.StudentUserId.ShouldBe(StudentUser);
        evt.StudentId.ShouldBe(await StudentIdAsync());
        // #423: bildirim hedefi (sub) + kısa adlar ("Ad S.") event'te taşınır; ham ad/e-posta taşınmaz.
        evt.ParentKeycloakId.ShouldBe($"kc-{ParentUser + 1}");
        evt.StudentKeycloakId.ShouldBe($"kc-{StudentUser}");
        evt.StudentDisplayName.ShouldBe("Ayşe K.");
        evt.ParentDisplayName.ShouldBe("Veli 2.");
        outbox[0].Content.ShouldNotContain("gmail.com");

        // Yeni veli çocuğu görür (birincil değil, diğer veliler ona açılmaz).
        await using var ctx = _db.NewContext();
        var child = (await Service(ctx).GetParentChildrenAsync(ParentUser + 1))!.ShouldHaveSingleItem();
        child.Status.ShouldBe("Active");
        child.StudentName.ShouldBe("Ayşe Kaya");
        child.GradeName.ShouldBe("7. Sınıf");
        child.SchoolName.ShouldBe("Atatürk Ortaokulu");
        child.IsPrimary.ShouldBeFalse();
        child.CoParents.ShouldBeEmpty();
        child.SecondParentCodeExpiresAt.ShouldBeNull();
        child.OpenParents.ShouldBeNull();
    }

    [Fact]
    public async Task Primary_parent_sees_co_parents_pending_requests_with_masked_email_and_code_expiry()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var second = await LinkSecondAsync(ParentUser + 1, primary);
        var pending = await RequestAsync(ParentUser + 2, primary);
        var codeForLater = await NewCodeAsync(primary);
        // Geçiş dönemindeki eski istek listede görünmez ama tavanı tüketir (m6: UI sunucuyla aynı sayımı kullanır).
        await SeedLinkAsync(ParentUser + 3, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.LegacyV1,
            isPrimary: false, createdAt: Now.AddDays(-3));

        await using var ctx = _db.NewContext();
        var child = (await Service(ctx).GetParentChildrenAsync(ParentUser))!.ShouldHaveSingleItem();

        child.IsPrimary.ShouldBeTrue();
        child.MaxParents.ShouldBe(4);
        child.OpenParents.ShouldBe(4);
        child.SecondParentCodeExpiresAt.ShouldBe(Now.AddDays(7));
        child.CoParents.Select(c => (c.LinkId, c.Status, c.ParentName)).ShouldBe(new[]
        {
            (second, "Active", "Veli 2"),
            (pending, "Pending", "Veli 3")
        });
        child.CoParents[0].ParentEmailMasked.ShouldBeEmpty(); // aktif veliye e-posta gerekmez
        child.CoParents[1].ParentEmailMasked.ShouldBe("v***@g***.com");
        child.CoParents[1].PendingExpiresAt.ShouldBe(Now.AddDays(7));
        JsonSerializer.Serialize(child).ShouldNotContain(codeForLater);
    }

    [Fact]
    public async Task Non_primary_parent_cannot_approve_or_reject_and_outsiders_get_not_found()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        await LinkSecondAsync(ParentUser + 1, primary);
        var pending = await RequestAsync(ParentUser + 2, primary);
        var otherChild = await SeedLinkAsync(ParentUser + 3, OtherStudentUser);

        var approve = await ApproveAsync(pending, ParentUser + 1);
        approve.Forbidden.ShouldBeTrue();
        approve.ErrorCode.ShouldBe(ParentLinkErrorCodes.NotPrimaryParent);
        await using (var ctx = _db.NewContext())
            (await Service(ctx).RejectSecondParentAsync(pending, ParentUser + 1)).Forbidden.ShouldBeTrue();

        // Kendi isteğini onaylayamaz; çocuğa bağlı olmayan veli / yabancı varlığı öğrenemez.
        (await ApproveAsync(pending, ParentUser + 2)).NotFound.ShouldBeTrue();
        (await ApproveAsync(pending, ParentUser + 3)).NotFound.ShouldBeTrue();
        (await ApproveAsync(pending, StrangerUser)).NotFound.ShouldBeTrue();
        (await ApproveAsync(pending + 999)).NotFound.ShouldBeTrue();
        (await ApproveAsync(otherChild)).NotFound.ShouldBeTrue();

        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == pending))).Status.ShouldBe(ParentStudentLinkStatus.Pending);
        (await EventCountAsync<ParentLinkedEvent>()).ShouldBe(1); // yalnız ikinci velinin onayı
    }

    [Fact]
    public async Task Primary_parent_rejects_a_pending_request_without_event()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var pending = await RequestAsync(ParentUser + 1, primary);

        await using (var ctx = _db.NewContext())
            (await Service(ctx).RejectSecondParentAsync(pending, ParentUser)).Success.ShouldBeTrue();

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == pending));
        link.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        link.RevokedByUserId.ShouldBe(ParentUser);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
        (await ApproveAsync(pending)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Pending_second_parent_request_expires_after_seven_days()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var pending = await RequestAsync(ParentUser + 1, primary);
        _time.Now = _time.Now.AddDays(7).AddSeconds(1);

        (await ApproveAsync(pending)).NotFound.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        (await Service(ctx).GetParentChildrenAsync(ParentUser + 1))!.ShouldBeEmpty();
        (await Service(ctx).GetParentChildrenAsync(ParentUser))!.Single().CoParents.ShouldBeEmpty();

        // Süresi dolan istek çifti bloklamaz: aynı veli yeni kodla yeniden istek açabilir.
        var again = await RequestAsync(ParentUser + 1, primary);
        again.ShouldNotBe(pending);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == pending))).Status.ShouldBe(ParentStudentLinkStatus.Revoked);
    }

    // ---- öğrenci: salt okunur + onay yolu yeni bağlantılara kapalı ------------------------------------------------------

    [Fact]
    public async Task Student_sees_linked_parents_read_only_with_primary_flag_and_never_new_requests()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        await LinkSecondAsync(ParentUser + 1, primary);
        await RequestAsync(ParentUser + 2, primary);

        await using var ctx = _db.NewContext();
        var dto = await Service(ctx).GetStudentParentsAsync(StudentUser);

        dto!.Items.Select(i => (i.ParentName, i.IsPrimary)).ShouldBe(new[] { ("Veli 1", true), ("Veli 2", false) });
        dto.PendingRequests.ShouldBeEmpty(); // yeni istek öğrenciye düşmez
        dto.MaxActiveParents.ShouldBe(4);
        dto.RequiresParent.ShouldBeTrue(); // #437
        JsonSerializer.Serialize(dto).ShouldNotContain("gmail");
        (await Service(ctx).GetStudentParentsAsync(StrangerUser)).ShouldBeNull();
    }

    [Fact]
    public async Task Student_approve_and_reject_are_closed_for_new_links()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var pending = await RequestAsync(ParentUser + 1, primary);

        await using var ctx = _db.NewContext();
        (await Service(ctx).ApproveLegacyAsync(pending, StudentUser)).NotFound.ShouldBeTrue();
        (await Service(ctx).RejectLegacyAsync(pending, StudentUser)).NotFound.ShouldBeTrue();
        (await Service(ctx).RejectLegacyAsync(primary, StudentUser)).NotFound.ShouldBeTrue(); // öğrenci koparamaz

        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == pending))).Status.ShouldBe(ParentStudentLinkStatus.Pending);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == primary))).Status.ShouldBe(ParentStudentLinkStatus.Active);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Legacy_pending_request_can_still_be_approved_by_the_student_within_thirty_days_and_becomes_primary()
    {
        await SeedAsync();
        var legacy = await SeedLinkAsync(ParentUser, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.LegacyV1,
            isPrimary: false, createdAt: Now.AddDays(-20));

        await using (var ctx = _db.NewContext())
        {
            var dto = await Service(ctx).GetStudentParentsAsync(StudentUser);
            var request = dto!.PendingRequests.ShouldHaveSingleItem();
            request.LinkId.ShouldBe(legacy);
            request.ParentEmailMasked.ShouldBe("v***@g***.com");
            request.ExpiresAt.ShouldBe(Now.AddDays(10)); // oluşturulmadan 30 gün
            (await Service(ctx).ApproveLegacyAsync(legacy, OtherStudentUser)).NotFound.ShouldBeTrue();
            (await Service(ctx).ApproveLegacyAsync(legacy, StudentUser)).Success.ShouldBeTrue();
        }

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync());
        link.Status.ShouldBe(ParentStudentLinkStatus.Active);
        link.IsPrimary.ShouldBeTrue(); // başka velisi yoktu → birincil
        var evt = JsonSerializer.Deserialize<ParentLinkedEvent>((await InDbAsync(db => db.OutboxMessages.SingleAsync())).Content)!;
        evt.LinkId.ShouldBe(legacy);
        evt.ParentUserId.ShouldBe(ParentUser);
    }

    [Fact]
    public async Task Legacy_approval_does_not_take_over_an_existing_primary()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser, createdAt: Now.AddDays(-40));
        var legacy = await SeedLinkAsync(ParentUser + 1, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.LegacyV1,
            isPrimary: false, createdAt: Now.AddDays(-2));

        await using (var ctx = _db.NewContext())
            (await Service(ctx).ApproveLegacyAsync(legacy, StudentUser)).Success.ShouldBeTrue();

        (await PrimaryLinkIdAsync()).ShouldBe(primary);
    }

    [Fact]
    public async Task Legacy_pending_request_past_thirty_days_cannot_be_approved_but_a_live_one_can_be_rejected()
    {
        await SeedAsync();
        var old = await SeedLinkAsync(ParentUser, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.LegacyV1,
            isPrimary: false, createdAt: Now.AddDays(-30).AddSeconds(-1));
        var live = await SeedLinkAsync(ParentUser + 1, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.LegacyV1,
            isPrimary: false, createdAt: Now.AddDays(-1));

        await using var ctx = _db.NewContext();
        (await Service(ctx).ApproveLegacyAsync(old, StudentUser)).NotFound.ShouldBeTrue();
        (await Service(ctx).RejectLegacyAsync(old, StudentUser)).NotFound.ShouldBeTrue();
        (await Service(ctx).GetStudentParentsAsync(StudentUser))!.PendingRequests.Select(p => p.LinkId).ShouldBe(new[] { live });

        (await Service(ctx).RejectLegacyAsync(live, StudentUser)).Success.ShouldBeTrue();
        var rejected = await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == live));
        rejected.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        rejected.RevokedByUserId.ShouldBe(StudentUser);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
    }

    // ---- redeem: kod kuralları (#419'dan korunanlar + #436) -----------------------------------------------------------

    [Fact]
    public async Task Old_student_generated_codes_cannot_be_redeemed()
    {
        await SeedAsync();
        await SeedLinkAsync(ParentUser);
        var sid = await StudentIdAsync();
        await InDbAsync(async db =>
        {
            db.ParentInviteCodes.Add(new ParentInviteCode
            {
                StudentId = sid, CodeHash = _hasher.Hash("ABCDEFGHJKMN"), CreatedAt = Now, ExpiresAt = Now.AddHours(10)
            }); // #419 öğrenci kodu: CreatedByParentId yok
            return await db.SaveChangesAsync();
        });

        var result = await RedeemAsync(ParentUser + 1, "ABCDEFGHJKMN");

        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync(l => l.Status == ParentStudentLinkStatus.Pending))).ShouldBe(0);
    }

    [Fact]
    public async Task Code_becomes_invalid_once_its_creator_is_no_longer_linked()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser, createdAt: Now.AddDays(-2));
        await SeedLinkAsync(ParentUser + 2, isPrimary: false, origin: ParentStudentLinkOrigin.InviteCode);
        var code = await NewCodeAsync(primary);
        (await RevokeAsync(primary, ParentUser)).Success.ShouldBeTrue(); // birincil ayrıldı; birincillik diğer veliye geçti

        (await RedeemAsync(ParentUser + 1, code)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await InDbAsync(db => db.ParentInviteCodes.SingleAsync())).UsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Code_is_single_use()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var code = await NewCodeAsync(primary);
        (await RedeemAsync(ParentUser + 1, code)).Success.ShouldBeTrue();

        var second = await RedeemAsync(ParentUser + 2, code);

        second.Success.ShouldBeFalse();
        second.ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(2);
    }

    [Fact]
    public async Task Expired_code_is_rejected_after_seven_days()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var code = await NewCodeAsync(primary);
        _time.Now = _time.Now.AddDays(7).AddSeconds(1);

        (await RedeemAsync(ParentUser + 1, code)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
    }

    [Fact]
    public async Task Every_invalid_reason_including_full_student_returns_the_same_generic_error()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var otherPrimary = await SeedLinkAsync(ParentUser + 4, OtherStudentUser);
        var used = await NewCodeAsync(primary);
        (await RedeemAsync(ParentUser + 1, used)).Success.ShouldBeTrue();
        var expired = await NewCodeAsync(otherPrimary, ParentUser + 4);
        _time.Now = _time.Now.AddDays(8);

        var results = new List<RedeemParentInviteCodeResultDto>
        {
            await RedeemAsync(ParentUser + 2, used),
            await RedeemAsync(ParentUser + 2, expired),
            await RedeemAsync(ParentUser + 2, "ZZZZZZZZZZZZ"),
            await RedeemAsync(ParentUser + 2, "bad!"),
            await RedeemAsync(ParentUser + 2, null),
            await RedeemAsync(ParentUser + 2, ""),
            await RedeemAsync(ParentUser + 2, new string('A', 40)),
        };

        // Öğrencinin 4 açık bağlantısı varken birincil velinin geçerli kodu da genel hata döner (geçerlilik kâhini yok).
        var sid = await StudentIdAsync();
        var pid = await ParentIdAsync(ParentUser);
        await InDbAsync(async db =>
        {
            var pids = await db.Parents.Where(p => p.UserId == ParentUser + 2 || p.UserId == ParentUser + 3).Select(p => p.Id).ToListAsync();
            foreach (var p in pids)
                db.ParentStudentLinks.Add(new ParentStudentLink
                {
                    ParentId = p, StudentId = sid, Status = ParentStudentLinkStatus.Active, Origin = ParentStudentLinkOrigin.InviteCode,
                    CreatedAt = Now, ActivatedAt = Now
                });
            db.ParentInviteCodes.Add(new ParentInviteCode
            {
                StudentId = sid, CodeHash = _hasher.Hash("ABCDEFGHJKMN"), CreatedAt = Now, ExpiresAt = Now.AddHours(1), CreatedByParentId = pid
            });
            return await db.SaveChangesAsync();
        });
        // 2. velinin isteği süresi dolmuştu; onu da Active yapıp tavanı 4'e tamamla.
        await InDbAsync(db => db.ParentStudentLinks.Where(l => l.StudentId == sid && l.Status == ParentStudentLinkStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ParentStudentLinkStatus.Active).SetProperty(l => l.CreatedAt, Now)));
        results.Add(await RedeemAsync(ParentUser + 5, "ABCDEFGHJKMN"));

        results.ShouldAllBe(r => !r.Success && !r.NotFound && !r.Conflict && !r.Forbidden && r.ErrorCode == ParentLinkErrorCodes.InvalidCode);
        results.Select(r => r.Message).Distinct().ShouldHaveSingleItem().ShouldBe("Kod geçersiz veya süresi dolmuş.");
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync(c => c.UsedAt == null && c.CodeHash == _hasher.Hash("ABCDEFGHJKMN"))))
            .ShouldBe(1); // tavan dolu: kod tüketilmedi
    }

    [Fact]
    public async Task Parent_limit_is_checked_before_code_lookup()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var pid = await ParentIdAsync(ParentUser + 1);
        await InDbAsync(async db =>
        {
            for (var i = 0; i < ParentLinkRules.MaxActiveChildrenPerParent; i++)
            {
                var s = new Student { UserId = 9100 + i, StudentNumber = $"x{i}" };
                db.Students.Add(s);
                await db.SaveChangesAsync();
                db.ParentStudentLinks.Add(new ParentStudentLink
                {
                    Origin = ParentStudentLinkOrigin.ParentCreated,
                    ParentId = pid, StudentId = s.Id, Status = ParentStudentLinkStatus.Active, IsPrimary = true, CreatedAt = Now
                });
            }
            return await db.SaveChangesAsync();
        });

        // Geçerli ve geçersiz kod aynı yanıtı alır — kod hiç aranmaz.
        var valid = await RedeemAsync(ParentUser + 1, await NewCodeAsync(primary));
        var bogus = await RedeemAsync(ParentUser + 1, "ZZZZZZZZZZZZ");
        valid.ErrorCode.ShouldBe(ParentLinkErrorCodes.ParentLimitReached);
        bogus.ErrorCode.ShouldBe(ParentLinkErrorCodes.ParentLimitReached);
        valid.Conflict.ShouldBeTrue();
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync(c => c.UsedAt == null))).ShouldBe(1);
    }

    [Fact]
    public async Task Already_linked_or_pending_parent_gets_conflict_without_consuming_the_code()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        await LinkSecondAsync(ParentUser + 1, primary);
        var otherPrimary = await SeedLinkAsync(ParentUser + 3, OtherStudentUser);
        await RequestAsync(ParentUser + 1, otherPrimary, ParentUser + 3); // diğer çocukta bekliyor

        var code = await NewCodeAsync(primary);
        var again = await RedeemAsync(ParentUser + 1, code);
        again.Conflict.ShouldBeTrue();
        again.ErrorCode.ShouldBe(ParentLinkErrorCodes.AlreadyLinked);
        (await RedeemAsync(ParentUser + 1, await NewCodeAsync(otherPrimary, ParentUser + 3))).ErrorCode.ShouldBe(ParentLinkErrorCodes.AlreadyLinked);
        (await RedeemAsync(ParentUser, code)).ErrorCode.ShouldBe(ParentLinkErrorCodes.AlreadyLinked); // birincil kendi kodunu
        (await RedeemAsync(ParentUser + 2, code)).Success.ShouldBeTrue(); // kod hâlâ geçerliydi

        await using var ctx = _db.NewContext();
        var children = await Service(ctx).GetParentChildrenAsync(ParentUser + 1);
        children!.Select(c => c.Status).ShouldBe(new[] { "Active", "Pending" });
        children[0].StudentName.ShouldBe("Ayşe Kaya");
        children[1].StudentName.ShouldBeNull();
    }

    [Fact]
    public async Task Unverified_school_is_not_shown_to_the_parent()
    {
        await SeedAsync();
        await SeedLinkAsync(ParentUser, OtherStudentUser);

        await using var ctx = _db.NewContext();
        var child = (await Service(ctx).GetParentChildrenAsync(ParentUser))!.ShouldHaveSingleItem();
        child.StudentName.ShouldBe("Mert Can");
        child.SchoolName.ShouldBeNull(); // doğrulanmamış okul (#361) veliye gösterilmez
        child.GradeName.ShouldBeNull();
        child.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public async Task Account_with_both_rows_cannot_link_to_itself()
    {
        await SeedAsync();
        await InDbAsync(async db =>
        {
            db.Parents.Add(new Parent { UserId = StudentUser }); // eski/bozuk veri: aynı hesapta iki rol
            return await db.SaveChangesAsync();
        });
        var primary = await SeedLinkAsync(ParentUser);

        var result = await RedeemAsync(StudentUser, await NewCodeAsync(primary));

        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        result.Message.ShouldBe("Kod geçersiz veya süresi dolmuş.");
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Redeem_without_parent_record_is_not_found()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var result = await RedeemAsync(StrangerUser, await NewCodeAsync(primary)); // Parent satırı yok
        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.ProfileNotFound);
    }

    // ---- başarısızlık sayacı ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Failed_redeems_hit_the_per_account_cap_then_even_valid_codes_are_rate_limited()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var guard = NewGuard(new ParentRedeemGuardOptions { MaxFailuresPerAccount = 3 });
        for (var i = 0; i < 3; i++)
            (await RedeemAsync(ParentUser + 1, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);

        var blocked = await RedeemAsync(ParentUser + 1, await NewCodeAsync(primary), guard);
        blocked.RateLimited.ShouldBeTrue();
        blocked.ErrorCode.ShouldBe(ParentLinkErrorCodes.RateLimited);
        blocked.RetryAfterSeconds!.Value.ShouldBeInRange(1, 86_400);

        (await RedeemAsync(ParentUser + 2, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode); // başka hesap
        _time.Now = _time.Now.AddDays(1).AddSeconds(1);
        (await RedeemAsync(ParentUser + 1, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode); // pencere bitti
    }

    [Fact]
    public async Task Global_failure_burst_blocks_accounts_with_failures_but_exempts_clean_accounts_until_cooldown()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var guard = NewGuard(new ParentRedeemGuardOptions
        {
            GlobalFailureThreshold = 3, GlobalWindowSeconds = 60, BreakerCooldownSeconds = 120
        });
        for (var i = 0; i < 4; i++)
            await RedeemAsync(ParentUser + 1 + (i % 3), "ZZZZZZZZZZZZ", guard);

        // Başarısızlığı olan hesap: devre açık → geçerli kodla bile 429.
        var failed = await RedeemAsync(ParentUser + 1, await NewCodeAsync(primary), guard);
        failed.RateLimited.ShouldBeTrue();
        failed.RetryAfterSeconds!.Value.ShouldBeInRange(1, 120);

        // Hiç başarısızlığı olmayan hesap muaf (meşru veli kilitlenmez).
        (await RedeemAsync(ParentUser + 5, await NewCodeAsync(primary), guard)).Success.ShouldBeTrue();

        _time.Now = _time.Now.AddSeconds(121);
        (await RedeemAsync(ParentUser + 1, await NewCodeAsync(primary), guard)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Successful_and_already_linked_outcomes_do_not_count_as_failures()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var guard = NewGuard(new ParentRedeemGuardOptions { MaxFailuresPerAccount = 1 });
        (await RedeemAsync(ParentUser + 1, await NewCodeAsync(primary), guard)).Success.ShouldBeTrue();
        (await RedeemAsync(ParentUser + 1, await NewCodeAsync(primary), guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.AlreadyLinked);
        (await RedeemAsync(ParentUser + 1, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await RedeemAsync(ParentUser + 1, "ZZZZZZZZZZZZ", guard)).RateLimited.ShouldBeTrue();
    }

    [Fact]
    public async Task Names_fall_back_when_auth_api_is_unavailable()
    {
        await SeedAsync();
        await SeedLinkAsync(ParentUser);
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<UserLookupResultDto>>>(_ => throw new HttpRequestException("down"));

        await using var ctx = _db.NewContext();
        (await Service(ctx).GetStudentParentsAsync(StudentUser))!.Items.Single().ParentName.ShouldBe("Veli");
        (await Service(ctx).GetParentChildrenAsync(ParentUser))!.Single().StudentName.ShouldBe("Öğrenci");
    }

    // ---- koparma: yalnız birincil veli / admin ------------------------------------------------------------------------

    [Fact]
    public async Task Primary_parent_removes_a_co_parent_and_access_ends_immediately()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var second = await LinkSecondAsync(ParentUser + 1, primary);

        (await RevokeAsync(second, ParentUser)).Success.ShouldBeTrue();

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == second));
        link.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        link.RevokedAt.ShouldNotBeNull();
        link.RevokedByUserId.ShouldBe(ParentUser);
        var evt = await InDbAsync(async db => JsonSerializer.Deserialize<ParentUnlinkedEvent>((await db.OutboxMessages
            .SingleAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>())).Content)!);
        evt.LinkId.ShouldBe(second);
        evt.RevokedByRole.ShouldBe("PrimaryParent");
        evt.RevokedByUserId.ShouldBe(ParentUser);
        evt.ParentUserId.ShouldBe(ParentUser + 1);
        evt.StudentUserId.ShouldBe(StudentUser);
        evt.ParentKeycloakId.ShouldBe($"kc-{ParentUser + 1}");
        evt.StudentDisplayName.ShouldBe("Ayşe K.");

        await using var read = _db.NewContext();
        (await Service(read).GetParentChildrenAsync(ParentUser + 1))!.ShouldBeEmpty();
        (await Service(read).GetStudentParentsAsync(StudentUser))!.Items.Select(i => i.ParentName).ShouldBe(new[] { "Veli 1" });
        (await PrimaryLinkIdAsync()).ShouldBe(primary);
    }

    [Fact]
    public async Task Non_primary_parent_cannot_remove_other_links_but_can_cancel_its_own_pending_request()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var second = await LinkSecondAsync(ParentUser + 1, primary);
        var third = await LinkSecondAsync(ParentUser + 2, primary);
        var pending = await RequestAsync(ParentUser + 3, primary);
        _authApi.ClearReceivedCalls();

        foreach (var target in new[] { primary, third, pending })
        {
            var result = await RevokeAsync(target, ParentUser + 1);
            result.Forbidden.ShouldBeTrue();
            result.ErrorCode.ShouldBe(ParentLinkErrorCodes.NotPrimaryParent);
        }
        // Security MINOR-3: yetkisiz koparma auth-api ad çözümüne hiç gitmez (ucuz ön kontrol).
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync(l => l.Status == ParentStudentLinkStatus.Active))).ShouldBe(3);
        (await EventCountAsync<ParentUnlinkedEvent>()).ShouldBe(0);
        second.ShouldBeGreaterThan(0);

        // Kendi bekleyen isteğini iptal: serbest, event yok.
        (await RevokeAsync(pending, ParentUser + 3)).Success.ShouldBeTrue();
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == pending))).Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        (await EventCountAsync<ParentUnlinkedEvent>()).ShouldBe(0);
        (await ApproveAsync(pending)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Revoking_someone_elses_link_is_not_found_and_changes_nothing()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        await SeedLinkAsync(ParentUser + 1, OtherStudentUser);

        foreach (var intruder in new[] { ParentUser + 1, StrangerUser, StudentUser })
        {
            var result = await RevokeAsync(primary, intruder);
            result.NotFound.ShouldBeTrue();
            result.ErrorCode.ShouldBe(ParentLinkErrorCodes.NotFound);
        }
        (await RevokeAsync(primary + 999, ParentUser)).NotFound.ShouldBeTrue();

        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == primary))).Status.ShouldBe(ParentStudentLinkStatus.Active);
        (await EventCountAsync<ParentUnlinkedEvent>()).ShouldBe(0);
    }

    [Fact]
    public async Task Primary_removing_its_own_link_hands_primary_over_to_the_oldest_remaining_parent()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser, createdAt: Now.AddDays(-10));
        var older = await SeedLinkAsync(ParentUser + 1, isPrimary: false, origin: ParentStudentLinkOrigin.InviteCode, createdAt: Now.AddDays(-5));
        var newer = await SeedLinkAsync(ParentUser + 2, isPrimary: false, origin: ParentStudentLinkOrigin.InviteCode, createdAt: Now.AddDays(-1));

        (await RevokeAsync(primary, ParentUser)).Success.ShouldBeTrue();

        (await PrimaryLinkIdAsync()).ShouldBe(older);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == primary))).IsPrimary.ShouldBeFalse();
        var evt = await InDbAsync(async db => JsonSerializer.Deserialize<ParentUnlinkedEvent>((await db.OutboxMessages.SingleAsync()).Content)!);
        evt.RevokedByRole.ShouldBe("Parent");
        evt.PrimaryParentUserId.ShouldBe(ParentUser + 1); // yeni birincil veli haberdar edilir
        evt.PrimaryParentKeycloakId.ShouldBe($"kc-{ParentUser + 1}");

        // Yeni birincil artık yönetir; diğer veli değil.
        (await CreateCodeAsync(older, ParentUser + 1)).Success.ShouldBeTrue();
        (await CreateCodeAsync(newer, ParentUser + 2)).Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task Deleted_primary_parent_is_replaced_by_the_oldest_remaining_active_parent()
    {
        await SeedAsync();
        await SeedLinkAsync(ParentUser, createdAt: Now.AddDays(-10));
        var older = await SeedLinkAsync(ParentUser + 1, isPrimary: false, origin: ParentStudentLinkOrigin.InviteCode, createdAt: Now.AddDays(-5));
        var newer = await SeedLinkAsync(ParentUser + 2, isPrimary: false, origin: ParentStudentLinkOrigin.InviteCode, createdAt: Now.AddDays(-1));
        await InDbAsync(async db =>
        {
            db.Parents.Remove(await db.Parents.SingleAsync(p => p.UserId == ParentUser)); // soft-delete; bağlantı satırı Active kalır
            return await db.SaveChangesAsync();
        });

        // Okuma anında devir (yazmadan).
        await using (var ctx = _db.NewContext())
        {
            var child = (await Service(ctx).GetParentChildrenAsync(ParentUser + 1))!.ShouldHaveSingleItem();
            child.IsPrimary.ShouldBeTrue();
            child.CoParents.Select(c => c.LinkId).ShouldBe(new[] { newer }); // silinmiş veli listede yok
            (await Service(ctx).GetStudentParentsAsync(StudentUser))!.Items.Single(i => i.IsPrimary).LinkId.ShouldBe(older);
        }

        // Yazım yolunda kalıcı devir: yeni birincil kod üretir, işaret taşınır.
        (await CreateCodeAsync(older, ParentUser + 1)).Success.ShouldBeTrue();
        (await PrimaryLinkIdAsync()).ShouldBe(older);
        (await RevokeAsync(older, ParentUser + 2)).Forbidden.ShouldBeTrue();
        (await RevokeAsync(newer, ParentUser + 1)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Admin_can_remove_any_link_with_audit_and_primary_handover()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser, createdAt: Now.AddDays(-3));
        var second = await LinkSecondAsync(ParentUser + 1, primary);

        await using (var ctx = _db.NewContext())
            (await Service(ctx).AdminRevokeAsync(primary, 1, "kc-admin")).Success.ShouldBeTrue();

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == primary));
        link.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        link.RevokedByUserId.ShouldBe(1);
        (await PrimaryLinkIdAsync()).ShouldBe(second);
        var evt = await InDbAsync(async db => JsonSerializer.Deserialize<ParentUnlinkedEvent>((await db.OutboxMessages
            .SingleAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>())).Content)!);
        evt.RevokedByRole.ShouldBe("Admin");
        var audit = await InDbAsync(db => db.AdminUserActionLogs.SingleAsync());
        audit.Action.ShouldBe(AdminUserAction.ParentLinkRevoked);
        audit.TargetType.ShouldBe(AdminUserTargetType.ParentLink);
        audit.TargetId.ShouldBe(primary);
        audit.ActorKeycloakId.ShouldBe("kc-admin");
        audit.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);

        // Security MINOR-3: yan etkisiz sonuçlar da denetlenir.
        await using (var missing = _db.NewContext())
            (await Service(missing).AdminRevokeAsync(primary + 999, 1, "kc-admin")).NotFound.ShouldBeTrue();
        await using (var again = _db.NewContext())
            (await Service(again).AdminRevokeAsync(primary, 1, "kc-admin")).Success.ShouldBeTrue();
        var audits = await InDbAsync(db => db.AdminUserActionLogs.OrderBy(a => a.Id).ToListAsync());
        audits.Select(a => (a.TargetId, a.Outcome)).ShouldBe(new[]
        {
            (primary, AdminUserActionOutcome.Succeeded),
            (primary + 999, AdminUserActionOutcome.NotFound),
            (primary, AdminUserActionOutcome.NoChange)
        });
        (await EventCountAsync<ParentUnlinkedEvent>()).ShouldBe(1);
    }

    [Fact]
    public async Task Only_parent_cannot_leave_but_admin_can_remove_the_last_link()
    {
        await SeedAsync();
        var only = await SeedLinkAsync(ParentUser);

        var leave = await RevokeAsync(only, ParentUser);

        leave.Conflict.ShouldBeTrue();
        leave.ErrorCode.ShouldBe(ParentLinkErrorCodes.LastParentCannotLeave);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync())).Status.ShouldBe(ParentStudentLinkStatus.Active);
        (await EventCountAsync<ParentUnlinkedEvent>()).ShouldBe(0);

        await using (var ctx = _db.NewContext())
            (await Service(ctx).AdminRevokeAsync(only, 1, "kc-admin")).Success.ShouldBeTrue();
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync())).Status.ShouldBe(ParentStudentLinkStatus.Revoked);
    }

    [Fact]
    public async Task Non_primary_parent_can_leave_its_own_active_link_and_the_primary_is_told()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var second = await LinkSecondAsync(ParentUser + 1, primary);

        (await RevokeAsync(second, ParentUser + 1)).Success.ShouldBeTrue();

        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == second))).Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        (await PrimaryLinkIdAsync()).ShouldBe(primary);
        var evt = await InDbAsync(async db => JsonSerializer.Deserialize<ParentUnlinkedEvent>((await db.OutboxMessages
            .SingleAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>())).Content)!);
        evt.RevokedByRole.ShouldBe("Parent");
        evt.ParentUserId.ShouldBe(ParentUser + 1);
        evt.PrimaryParentUserId.ShouldBe(ParentUser);
        evt.PrimaryParentKeycloakId.ShouldBe($"kc-{ParentUser}");
        await using var ctx = _db.NewContext();
        (await Service(ctx).GetParentChildrenAsync(ParentUser + 1))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeated_revoke_is_idempotent_and_emits_a_single_event()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser);
        var second = await LinkSecondAsync(ParentUser + 1, primary);

        (await RevokeAsync(second, ParentUser)).Success.ShouldBeTrue();
        (await RevokeAsync(second, ParentUser)).Success.ShouldBeTrue();
        (await RevokeAsync(second, ParentUser + 1)).Success.ShouldBeTrue(); // kendi (artık kopuk) bağlantısı

        (await EventCountAsync<ParentUnlinkedEvent>()).ShouldBe(1);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == second))).RevokedByUserId.ShouldBe(ParentUser);
    }

    // ---- geçiş / bakım süpürücüsü -------------------------------------------------------------------------------------

    private ParentLinkTransitionSweepJob Sweeper(AppDbContext ctx, int batch = 200)
    {
        var monitor = Substitute.For<IOptionsMonitor<ParentLinkTransitionSweepOptions>>();
        monitor.CurrentValue.Returns(new ParentLinkTransitionSweepOptions { BatchSize = batch });
        return new ParentLinkTransitionSweepJob(ctx, monitor, _time);
    }

    [Fact]
    public async Task Sweep_revokes_legacy_requests_after_thirty_days_and_new_requests_after_seven_without_events()
    {
        await SeedAsync();
        var primary = await SeedLinkAsync(ParentUser, createdAt: Now.AddDays(-60));
        var legacyOld = await SeedLinkAsync(ParentUser + 1, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.LegacyV1,
            isPrimary: false, createdAt: Now.AddDays(-31));
        var legacyLive = await SeedLinkAsync(ParentUser + 2, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.LegacyV1,
            isPrimary: false, createdAt: Now.AddDays(-20));
        var inviteOld = await SeedLinkAsync(ParentUser + 3, status: ParentStudentLinkStatus.Pending, origin: ParentStudentLinkOrigin.InviteCode,
            isPrimary: false, createdAt: Now.AddDays(-8));
        var inviteLive = await SeedLinkAsync(ParentUser + 4, OtherStudentUser, ParentStudentLinkStatus.Pending, ParentStudentLinkOrigin.InviteCode,
            isPrimary: false, createdAt: Now.AddDays(-6));

        ParentLinkTransitionSweepResult result;
        await using (var ctx = _db.NewContext())
            result = await Sweeper(ctx).SweepAsync();

        result.ExpiredPendingRevoked.ShouldBe(2);
        var rows = await InDbAsync(db => db.ParentStudentLinks.AsNoTracking().ToDictionaryAsync(l => l.Id));
        rows[legacyOld].Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        rows[legacyOld].RevokedAt.ShouldBe(Now);
        rows[legacyOld].RevokedByUserId.ShouldBeNull(); // süre doldu
        rows[inviteOld].Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        rows[legacyLive].Status.ShouldBe(ParentStudentLinkStatus.Pending);
        rows[inviteLive].Status.ShouldBe(ParentStudentLinkStatus.Pending);
        rows[primary].Status.ShouldBe(ParentStudentLinkStatus.Active);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);

        // Geçiş penceresi kapanan istek öğrenci tarafından onaylanamaz; ikinci tur no-op.
        await using (var read = _db.NewContext())
            (await Service(read).ApproveLegacyAsync(legacyOld, StudentUser)).NotFound.ShouldBeTrue();
        await using (var again = _db.NewContext())
            (await Sweeper(again).SweepAsync()).ExpiredPendingRevoked.ShouldBe(0);
    }

    [Fact]
    public async Task Sweep_persists_primary_handover_for_a_deleted_primary_and_backfills_students_without_one()
    {
        await SeedAsync();
        var deletedPrimary = await SeedLinkAsync(ParentUser, createdAt: Now.AddDays(-10));
        var heir = await SeedLinkAsync(ParentUser + 1, isPrimary: false, origin: ParentStudentLinkOrigin.InviteCode, createdAt: Now.AddDays(-5));
        // Diğer öğrencide hiç işaretli birincil yok (eski veri): en eski Active işaretlenir.
        var oldest = await SeedLinkAsync(ParentUser + 2, OtherStudentUser, isPrimary: false, origin: ParentStudentLinkOrigin.LegacyV1,
            createdAt: Now.AddDays(-9));
        await SeedLinkAsync(ParentUser + 3, OtherStudentUser, isPrimary: false, origin: ParentStudentLinkOrigin.LegacyV1, createdAt: Now.AddDays(-2));
        await InDbAsync(async db =>
        {
            db.Parents.Remove(await db.Parents.SingleAsync(p => p.UserId == ParentUser));
            return await db.SaveChangesAsync();
        });

        ParentLinkTransitionSweepResult result;
        await using (var ctx = _db.NewContext())
            result = await Sweeper(ctx).SweepAsync();

        result.PrimaryRepaired.ShouldBe(2);
        (await PrimaryLinkIdAsync()).ShouldBe(heir);
        (await PrimaryLinkIdAsync(OtherStudentUser)).ShouldBe(oldest);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == deletedPrimary))).IsPrimary.ShouldBeFalse();
        await using (var again = _db.NewContext())
            (await Sweeper(again).SweepAsync()).PrimaryRepaired.ShouldBe(0);
    }

    // ---- #437 veli kuralı ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(new[] { "Student" }, true)]
    [InlineData(new[] { "student" }, true)]
    [InlineData(new[] { "Teacher" }, false)]
    [InlineData(new[] { "Teacher", "Student" }, false)] // bağımsız öğretmen de Teacher rolüdür
    [InlineData(new[] { "Admin", "Student" }, false)]
    [InlineData(new[] { "Parent" }, false)]
    [InlineData(new string[0], false)]
    public void Every_student_and_only_students_require_a_parent(string[] roles, bool expected)
    {
        ParentRequirement.RequiresParent(roles).ShouldBe(expected);
        // m9: ClaimsPrincipal sürümü de büyük/küçük harfe duyarsız (IsInRole değil, rol claim değerleri).
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            roles.Select(r => new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, r)), "test"));
        ParentRequirement.RequiresParent(principal).ShouldBe(expected);
        ParentRequirement.RequiresParentForStudentProfile().ShouldBeTrue(); // yaş/admin muafiyeti yok
    }
}
