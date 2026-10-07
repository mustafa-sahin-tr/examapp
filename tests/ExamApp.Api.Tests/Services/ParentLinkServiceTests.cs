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
/// issue #419 (+ review): veli–öğrenci bağlantısı — kod üretimi (hash'li, 12 karakter, 48 saat, öğrenci başına tek geçerli kod,
/// çakışmada yazmama), redeem → PENDING + öğrenci onayı/reddi, tek kullanım, genel hata (öğrenci tavanı dahil), velinin
/// kendi tavanı kod aranmadan önce, en fazla 4 açık veli (Pending dahil, süresi dolan Pending hariç), silinmiş taraf tavan
/// tüketmez, iki taraftan koparma, başkasının bağlantısı 404, outbox yalnız onay/aktif koparmada, başarısızlık sayacı.
/// SQLite (gerçek ilişkisel davranış); advisory lock ve Postgres eşzamanlılığı ParentLinkEndpointsTests'te.
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

    private async Task<string> NewCodeAsync(int studentUserId = StudentUser)
    {
        await using var ctx = _db.NewContext();
        var result = await Service(ctx).CreateInviteCodeAsync(studentUserId);
        result.Success.ShouldBeTrue(result.Message);
        return result.Code!;
    }

    private async Task<RedeemParentInviteCodeResultDto> RedeemAsync(int parentUserId, string? code, IParentRedeemAttemptGuard? guard = null)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx, guard: guard).RedeemAsync(parentUserId, code);
    }

    private async Task<int> RequestAsync(int parentUserId, int studentUserId = StudentUser)
    {
        var result = await RedeemAsync(parentUserId, await NewCodeAsync(studentUserId));
        result.Success.ShouldBeTrue(result.Message);
        return result.ObjectId;
    }

    private async Task<ParentLinkResponseDto> ApproveAsync(int linkId, int studentUserId = StudentUser)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).ApproveAsync(linkId, studentUserId);
    }

    /// <summary>Kod → redeem (Pending) → öğrenci onayı (Active).</summary>
    private async Task<int> LinkAsync(int parentUserId, int studentUserId = StudentUser)
    {
        var linkId = await RequestAsync(parentUserId, studentUserId);
        (await ApproveAsync(linkId, studentUserId)).Success.ShouldBeTrue();
        return linkId;
    }

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        await using var ctx = _db.NewContext();
        return await work(ctx);
    }

    // ---- kod üretimi ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Created_code_is_twelve_chars_stored_only_as_hash_and_expires_in_48_hours()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await Service(ctx).CreateInviteCodeAsync(StudentUser);

        result.Success.ShouldBeTrue();
        result.Code!.Length.ShouldBe(12);
        result.ExpiresAt.ShouldBe(_time.Now.UtcDateTime.AddHours(48));
        var row = await InDbAsync(db => db.ParentInviteCodes.SingleAsync());
        row.CodeHash.ShouldBe(_hasher.Hash(result.Code));
        row.CodeHash.ShouldNotContain(result.Code, Case.Insensitive);
        row.UsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task New_code_invalidates_the_previous_one()
    {
        await SeedAsync();
        var first = await NewCodeAsync();
        var second = await NewCodeAsync();

        (await RedeemAsync(ParentUser, first)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await RedeemAsync(ParentUser, second)).Success.ShouldBeTrue();

        var active = await InDbAsync(db => db.ParentInviteCodes.CountAsync(c => c.UsedAt == null && c.ExpiresAt > _time.Now.UtcDateTime));
        active.ShouldBe(0);
    }

    [Fact]
    public async Task Code_creation_without_student_record_is_not_found()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await Service(ctx).CreateInviteCodeAsync(StrangerUser);
        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.ProfileNotFound);
    }

    [Fact]
    public async Task Exhausted_collision_retries_fail_without_inserting_a_code()
    {
        await SeedAsync();
        var existing = await NewCodeAsync(OtherStudentUser);
        var colliding = Substitute.For<IParentInviteCodeHasher>();
        colliding.Generate().Returns(existing);
        colliding.Hash(Arg.Any<string>()).Returns(ci => _hasher.Hash(ci.Arg<string>()));

        await using var ctx = _db.NewContext();
        var result = await Service(ctx, hasher: colliding).CreateInviteCodeAsync(StudentUser);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.Busy);
        colliding.Received(ParentLinkService.MaxGenerateAttempts).Generate();
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync())).ShouldBe(1); // yalnız diğer öğrencinin kodu
    }

    // ---- redeem → Pending → onay ------------------------------------------------------------------------------------

    [Fact]
    public async Task Redeem_creates_pending_link_consumes_code_and_reveals_no_student_data_or_event()
    {
        await SeedAsync();
        var code = await NewCodeAsync();

        var result = await RedeemAsync(ParentUser, code.ToLowerInvariant().Insert(8, "-").Insert(4, "-"));

        result.Success.ShouldBeTrue();
        result.Child!.Status.ShouldBe("Pending");
        result.Child.StudentName.ShouldBeNull();
        result.Child.GradeName.ShouldBeNull();
        result.Child.SchoolName.ShouldBeNull();
        result.Child.PendingExpiresAt.ShouldBe(_time.Now.UtcDateTime.AddDays(7));

        var (link, invite, outboxCount) = await InDbAsync(async db => (
            await db.ParentStudentLinks.SingleAsync(),
            await db.ParentInviteCodes.SingleAsync(),
            await db.OutboxMessages.CountAsync()));
        link.Status.ShouldBe(ParentStudentLinkStatus.Pending);
        link.ActivatedAt.ShouldBeNull();
        invite.UsedAt.ShouldNotBeNull();
        outboxCount.ShouldBe(0); // event yalnız onayda

        await using var ctx = _db.NewContext();
        var children = await Service(ctx).GetParentChildrenAsync(ParentUser);
        children!.ShouldHaveSingleItem().StudentName.ShouldBeNull();
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);
    }

    [Fact]
    public async Task Student_approval_activates_link_and_writes_ParentLinkedEvent()
    {
        await SeedAsync();
        var linkId = await RequestAsync(ParentUser);

        (await ApproveAsync(linkId)).Success.ShouldBeTrue();
        (await ApproveAsync(linkId)).Success.ShouldBeTrue(); // idempotent

        var (link, outbox, studentId, parentId) = await InDbAsync(async db => (
            await db.ParentStudentLinks.SingleAsync(),
            await db.OutboxMessages.ToListAsync(),
            await db.Students.Where(s => s.UserId == StudentUser).Select(s => s.Id).SingleAsync(),
            await db.Parents.Where(p => p.UserId == ParentUser).Select(p => p.Id).SingleAsync()));
        link.Status.ShouldBe(ParentStudentLinkStatus.Active);
        link.ActivatedAt.ShouldBe(_time.Now.UtcDateTime);
        var evt = JsonSerializer.Deserialize<ParentLinkedEvent>(outbox.ShouldHaveSingleItem().Content)!;
        outbox[0].Type.ShouldBe(OutboxEventRegistry.NameFor<ParentLinkedEvent>());
        evt.LinkId.ShouldBe(linkId);
        evt.ParentUserId.ShouldBe(ParentUser);
        evt.StudentUserId.ShouldBe(StudentUser);
        evt.StudentId.ShouldBe(studentId);
        evt.ParentId.ShouldBe(parentId);
        // #423: bildirim hedefi (sub) + kısa adlar ("Ad S.") event'te taşınır; ham ad/e-posta taşınmaz.
        evt.ParentKeycloakId.ShouldBe($"kc-{ParentUser}");
        evt.StudentKeycloakId.ShouldBe($"kc-{StudentUser}");
        evt.StudentDisplayName.ShouldBe("Ayşe K.");
        evt.ParentDisplayName.ShouldBe("Veli 1.");
        outbox[0].Content.ShouldNotContain("veli7101.com");

        await using var ctx = _db.NewContext();
        var child = (await Service(ctx).GetParentChildrenAsync(ParentUser))!.ShouldHaveSingleItem();
        child.Status.ShouldBe("Active");
        child.StudentName.ShouldBe("Ayşe Kaya");
        child.GradeName.ShouldBe("7. Sınıf");
        child.SchoolName.ShouldBe("Atatürk Ortaokulu");
    }

    [Fact]
    public async Task Student_sees_pending_request_and_can_reject_it()
    {
        await SeedAsync();
        var linkId = await RequestAsync(ParentUser);

        await using (var ctx = _db.NewContext())
        {
            var dto = await Service(ctx).GetStudentParentsAsync(StudentUser);
            dto!.Items.ShouldBeEmpty();
            var pending = dto.PendingRequests.ShouldHaveSingleItem();
            pending.ParentName.ShouldBe("Veli 1");
            pending.ParentEmailMasked.ShouldBe("v***@g***.com");
            pending.RequestedAt.ShouldBe(_time.Now.UtcDateTime);
            pending.ExpiresAt.ShouldBe(_time.Now.UtcDateTime.AddDays(7));
        }

        await using (var ctx = _db.NewContext())
            (await Service(ctx).RejectAsync(linkId, StudentUser)).Success.ShouldBeTrue();

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync());
        link.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        link.RevokedByUserId.ShouldBe(StudentUser);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
        (await ApproveAsync(linkId)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Pending_request_expires_after_seven_days_and_cannot_be_approved()
    {
        await SeedAsync();
        var linkId = await RequestAsync(ParentUser);
        _time.Now = _time.Now.AddDays(7).AddSeconds(1);

        (await ApproveAsync(linkId)).NotFound.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        (await Service(ctx).GetStudentParentsAsync(StudentUser))!.PendingRequests.ShouldBeEmpty();
        (await Service(ctx).GetParentChildrenAsync(ParentUser))!.ShouldBeEmpty();

        // Süresi dolan istek çifti bloklamaz: aynı veli yeni kodla yeniden istek açabilir.
        var again = await RequestAsync(ParentUser);
        again.ShouldNotBe(linkId);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == linkId))).Status.ShouldBe(ParentStudentLinkStatus.Revoked);
    }

    [Fact]
    public async Task Approving_or_rejecting_someone_elses_request_is_not_found()
    {
        await SeedAsync();
        var linkId = await RequestAsync(ParentUser);

        (await ApproveAsync(linkId, OtherStudentUser)).NotFound.ShouldBeTrue();
        (await ApproveAsync(linkId, ParentUser)).NotFound.ShouldBeTrue(); // veli kendi isteğini onaylayamaz
        await using var ctx = _db.NewContext();
        (await Service(ctx).RejectAsync(linkId, OtherStudentUser)).NotFound.ShouldBeTrue();
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync())).Status.ShouldBe(ParentStudentLinkStatus.Pending);
    }

    [Fact]
    public async Task Code_is_single_use()
    {
        await SeedAsync();
        var code = await NewCodeAsync();
        (await RedeemAsync(ParentUser, code)).Success.ShouldBeTrue();

        var second = await RedeemAsync(ParentUser + 1, code);

        second.Success.ShouldBeFalse();
        second.ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        await SeedAsync();
        var code = await NewCodeAsync();
        _time.Now = _time.Now.AddHours(48).AddSeconds(1);

        (await RedeemAsync(ParentUser, code)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
    }

    [Fact]
    public async Task Every_invalid_reason_including_full_student_returns_the_same_generic_error()
    {
        await SeedAsync();
        var used = await NewCodeAsync();
        (await RedeemAsync(ParentUser, used)).Success.ShouldBeTrue();
        var expired = await NewCodeAsync(OtherStudentUser);
        _time.Now = _time.Now.AddHours(49);

        var results = new List<RedeemParentInviteCodeResultDto>
        {
            await RedeemAsync(ParentUser + 1, used),
            await RedeemAsync(ParentUser + 1, expired),
            await RedeemAsync(ParentUser + 1, "ZZZZZZZZZZZZ"),
            await RedeemAsync(ParentUser + 1, "bad!"),
            await RedeemAsync(ParentUser + 1, null),
            await RedeemAsync(ParentUser + 1, ""),
            await RedeemAsync(ParentUser + 1, new string('A', 40)),
        };

        // Öğrencinin 4 açık bağlantısı varken geçerli bir kod da genel hata döner (geçerlilik kâhini yok).
        await using (var ctx = _db.NewContext())
        {
            var sid = await ctx.Students.Where(s => s.UserId == StudentUser).Select(s => s.Id).SingleAsync();
            var pids = await ctx.Parents.Where(p => p.UserId >= ParentUser + 2 && p.UserId <= ParentUser + 4).Select(p => p.Id).ToListAsync();
            foreach (var pid in pids)
                ctx.ParentStudentLinks.Add(new ParentStudentLink
                {
                    ParentId = pid, StudentId = sid, Status = ParentStudentLinkStatus.Active, CreatedAt = _time.Now.UtcDateTime
                });
            ctx.ParentInviteCodes.Add(new ParentInviteCode
            {
                StudentId = sid, CodeHash = _hasher.Hash("ABCDEFGHJKMN"), CreatedAt = _time.Now.UtcDateTime,
                ExpiresAt = _time.Now.UtcDateTime.AddHours(1)
            });
            await ctx.SaveChangesAsync();
        }
        results.Add(await RedeemAsync(ParentUser + 5, "ABCDEFGHJKMN"));

        results.ShouldAllBe(r => !r.Success && !r.NotFound && !r.Conflict && r.ErrorCode == ParentLinkErrorCodes.InvalidCode);
        results.Select(r => r.Message).Distinct().ShouldHaveSingleItem().ShouldBe("Kod geçersiz veya süresi dolmuş.");
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync(c => c.UsedAt == null && c.CodeHash == _hasher.Hash("ABCDEFGHJKMN"))))
            .ShouldBe(1); // tavan dolu: kod tüketilmedi
    }

    [Fact]
    public async Task Student_can_have_at_most_four_open_parents_including_pending()
    {
        await SeedAsync();
        await LinkAsync(ParentUser);
        await LinkAsync(ParentUser + 1);
        await RequestAsync(ParentUser + 2);
        await RequestAsync(ParentUser + 3); // 2 Active + 2 Pending = 4

        await using var ctx = _db.NewContext();
        var create = await Service(ctx).CreateInviteCodeAsync(StudentUser);
        create.Conflict.ShouldBeTrue();
        create.ErrorCode.ShouldBe(ParentLinkErrorCodes.StudentLimitReached);

        // Bekleyen istek süresi dolunca yer açılır.
        _time.Now = _time.Now.AddDays(8);
        await using var later = _db.NewContext();
        (await Service(later).CreateInviteCodeAsync(StudentUser)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Soft_deleted_parent_does_not_consume_the_students_limit()
    {
        await SeedAsync();
        for (var i = 0; i < 4; i++)
            await LinkAsync(ParentUser + i);

        await using (var ctx = _db.NewContext())
        {
            var parent = await ctx.Parents.SingleAsync(p => p.UserId == ParentUser);
            ctx.Parents.Remove(parent); // soft-delete (audit hook)
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        (await Service(read).CreateInviteCodeAsync(StudentUser)).Success.ShouldBeTrue();
        (await Service(read).GetStudentParentsAsync(StudentUser))!.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Parent_limit_is_checked_before_code_lookup()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var pid = await ctx.Parents.Where(p => p.UserId == ParentUser).Select(p => p.Id).SingleAsync();
            for (var i = 0; i < ParentLinkRules.MaxActiveChildrenPerParent; i++)
            {
                var s = new Student { UserId = 9100 + i, StudentNumber = $"x{i}" };
                ctx.Students.Add(s);
                await ctx.SaveChangesAsync();
                ctx.ParentStudentLinks.Add(new ParentStudentLink
                {
                    ParentId = pid, StudentId = s.Id, Status = ParentStudentLinkStatus.Active, CreatedAt = _time.Now.UtcDateTime
                });
            }
            await ctx.SaveChangesAsync();
        }

        // Geçerli ve geçersiz kod aynı yanıtı alır — kod hiç aranmaz.
        var valid = await RedeemAsync(ParentUser, await NewCodeAsync());
        var bogus = await RedeemAsync(ParentUser, "ZZZZZZZZZZZZ");
        valid.ErrorCode.ShouldBe(ParentLinkErrorCodes.ParentLimitReached);
        bogus.ErrorCode.ShouldBe(ParentLinkErrorCodes.ParentLimitReached);
        valid.Conflict.ShouldBeTrue();
        (await InDbAsync(db => db.ParentInviteCodes.CountAsync(c => c.UsedAt == null))).ShouldBe(1);
    }

    [Fact]
    public async Task Parent_can_link_multiple_children_and_already_linked_or_pending_is_conflict_without_consuming_code()
    {
        await SeedAsync();
        await LinkAsync(ParentUser, StudentUser);
        await RequestAsync(ParentUser, OtherStudentUser); // bekliyor

        var code = await NewCodeAsync(StudentUser);
        var again = await RedeemAsync(ParentUser, code);
        again.Conflict.ShouldBeTrue();
        again.ErrorCode.ShouldBe(ParentLinkErrorCodes.AlreadyLinked);
        var pendingAgain = await RedeemAsync(ParentUser, await NewCodeAsync(OtherStudentUser));
        pendingAgain.ErrorCode.ShouldBe(ParentLinkErrorCodes.AlreadyLinked);
        (await RedeemAsync(ParentUser + 1, code)).Success.ShouldBeTrue(); // kod hâlâ geçerliydi

        await using var ctx = _db.NewContext();
        var children = await Service(ctx).GetParentChildrenAsync(ParentUser);
        children!.Select(c => c.Status).ShouldBe(new[] { "Active", "Pending" });
        children[0].StudentName.ShouldBe("Ayşe Kaya");
        children[1].StudentName.ShouldBeNull();
    }

    [Fact]
    public async Task Unverified_school_is_not_shown_to_the_parent()
    {
        await SeedAsync();
        await LinkAsync(ParentUser, OtherStudentUser);

        await using var ctx = _db.NewContext();
        var child = (await Service(ctx).GetParentChildrenAsync(ParentUser))!.ShouldHaveSingleItem();
        child.StudentName.ShouldBe("Mert Can");
        child.SchoolName.ShouldBeNull(); // doğrulanmamış okul (#361) veliye gösterilmez
        child.GradeName.ShouldBeNull();
    }

    [Fact]
    public async Task Account_with_both_rows_cannot_link_to_itself()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Parents.Add(new Parent { UserId = StudentUser }); // eski/bozuk veri: aynı hesapta iki rol
            await ctx.SaveChangesAsync();
        }

        var result = await RedeemAsync(StudentUser, await NewCodeAsync());

        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        result.Message.ShouldBe("Kod geçersiz veya süresi dolmuş.");
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Rejecting_an_active_link_is_not_found_use_revoke()
    {
        await SeedAsync();
        var linkId = await LinkAsync(ParentUser);

        await using var ctx = _db.NewContext();
        (await Service(ctx).RejectAsync(linkId, StudentUser)).NotFound.ShouldBeTrue();
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync())).Status.ShouldBe(ParentStudentLinkStatus.Active);
    }

    [Fact]
    public async Task Redeem_without_parent_record_is_not_found()
    {
        await SeedAsync();
        var code = await NewCodeAsync();
        var result = await RedeemAsync(StudentUser, code); // öğrencinin Parent satırı yok
        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.ProfileNotFound);
    }

    // ---- başarısızlık sayacı ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Failed_redeems_hit_the_per_account_cap_then_even_valid_codes_are_rate_limited()
    {
        await SeedAsync();
        var guard = NewGuard(new ParentRedeemGuardOptions { MaxFailuresPerAccount = 3 });
        for (var i = 0; i < 3; i++)
            (await RedeemAsync(ParentUser, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);

        var blocked = await RedeemAsync(ParentUser, await NewCodeAsync(), guard);
        blocked.RateLimited.ShouldBeTrue();
        blocked.ErrorCode.ShouldBe(ParentLinkErrorCodes.RateLimited);
        blocked.RetryAfterSeconds!.Value.ShouldBeInRange(1, 86_400);

        (await RedeemAsync(ParentUser + 1, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode); // başka hesap
        _time.Now = _time.Now.AddDays(1).AddSeconds(1);
        (await RedeemAsync(ParentUser, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode); // pencere bitti
    }

    [Fact]
    public async Task Global_failure_burst_blocks_accounts_with_failures_but_exempts_clean_accounts_until_cooldown()
    {
        await SeedAsync();
        var guard = NewGuard(new ParentRedeemGuardOptions
        {
            GlobalFailureThreshold = 3, GlobalWindowSeconds = 60, BreakerCooldownSeconds = 120
        });
        for (var i = 0; i < 4; i++)
            await RedeemAsync(ParentUser + (i % 3), "ZZZZZZZZZZZZ", guard);

        // Başarısızlığı olan hesap (ParentUser): devre açık → geçerli kodla bile 429.
        var failed = await RedeemAsync(ParentUser, await NewCodeAsync(), guard);
        failed.RateLimited.ShouldBeTrue();
        failed.RetryAfterSeconds!.Value.ShouldBeInRange(1, 120);

        // Hiç başarısızlığı olmayan hesap muaf (meşru veli kilitlenmez).
        (await RedeemAsync(ParentUser + 5, await NewCodeAsync(), guard)).Success.ShouldBeTrue();

        _time.Now = _time.Now.AddSeconds(121);
        (await RedeemAsync(ParentUser, await NewCodeAsync(OtherStudentUser), guard)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Successful_and_already_linked_outcomes_do_not_count_as_failures()
    {
        await SeedAsync();
        var guard = NewGuard(new ParentRedeemGuardOptions { MaxFailuresPerAccount = 1 });
        (await RedeemAsync(ParentUser, await NewCodeAsync(), guard)).Success.ShouldBeTrue();
        (await RedeemAsync(ParentUser, await NewCodeAsync(), guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.AlreadyLinked);
        (await RedeemAsync(ParentUser, "ZZZZZZZZZZZZ", guard)).ErrorCode.ShouldBe(ParentLinkErrorCodes.InvalidCode);
        (await RedeemAsync(ParentUser, "ZZZZZZZZZZZZ", guard)).RateLimited.ShouldBeTrue();
    }

    // ---- listeler ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Student_sees_parent_names_and_active_code_expiry_only()
    {
        await SeedAsync();
        await LinkAsync(ParentUser);
        await LinkAsync(ParentUser + 1);
        var pending = await NewCodeAsync();

        await using var ctx = _db.NewContext();
        var dto = await Service(ctx).GetStudentParentsAsync(StudentUser);

        dto!.Items.Select(i => i.ParentName).ShouldBe(new[] { "Veli 1", "Veli 2" });
        dto.ActiveInviteExpiresAt.ShouldBe(_time.Now.UtcDateTime.AddHours(48));
        dto.MaxActiveParents.ShouldBe(4);
        JsonSerializer.Serialize(dto).ShouldNotContain(pending);
        (await Service(ctx).GetStudentParentsAsync(StrangerUser)).ShouldBeNull();
    }

    [Fact]
    public async Task Names_fall_back_when_auth_api_is_unavailable()
    {
        await SeedAsync();
        await LinkAsync(ParentUser);
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<UserLookupResultDto>>>(_ => throw new HttpRequestException("down"));

        await using var ctx = _db.NewContext();
        (await Service(ctx).GetStudentParentsAsync(StudentUser))!.Items.Single().ParentName.ShouldBe("Veli");
        (await Service(ctx).GetParentChildrenAsync(ParentUser))!.Single().StudentName.ShouldBe("Öğrenci");
    }

    // ---- koparma ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StudentUser, "Student")]
    [InlineData(ParentUser, "Parent")]
    public async Task Either_side_can_revoke_and_access_ends_immediately(int actorUserId, string expectedRole)
    {
        await SeedAsync();
        var linkId = await LinkAsync(ParentUser);

        await using (var ctx = _db.NewContext())
            (await Service(ctx).RevokeAsync(linkId, actorUserId)).Success.ShouldBeTrue();

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync());
        link.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        link.RevokedAt.ShouldNotBeNull();
        link.RevokedByUserId.ShouldBe(actorUserId);

        var evt = await InDbAsync(async db => JsonSerializer.Deserialize<ParentUnlinkedEvent>((await db.OutboxMessages
            .SingleAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>())).Content)!);
        evt.LinkId.ShouldBe(linkId);
        evt.RevokedByRole.ShouldBe(expectedRole);
        evt.RevokedByUserId.ShouldBe(actorUserId);
        evt.ParentUserId.ShouldBe(ParentUser);
        evt.StudentUserId.ShouldBe(StudentUser);
        evt.ParentKeycloakId.ShouldBe($"kc-{ParentUser}");
        evt.StudentKeycloakId.ShouldBe($"kc-{StudentUser}");
        evt.StudentDisplayName.ShouldBe("Ayşe K.");

        await using var read = _db.NewContext();
        (await Service(read).GetParentChildrenAsync(ParentUser))!.ShouldBeEmpty();
        (await Service(read).GetStudentParentsAsync(StudentUser))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Approval_and_revoke_still_succeed_with_empty_targets_when_auth_api_is_unreachable()
    {
        await SeedAsync();
        var linkId = await RequestAsync(ParentUser);
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("auth-api down"));

        (await ApproveAsync(linkId)).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            (await Service(ctx).RevokeAsync(linkId, StudentUser)).Success.ShouldBeTrue();

        // #423: fail-soft — event yine yazılır (boş sub/ad); consumer BadgeService verisinden çözer ya da dead-letter'a düşürür.
        var contents = await InDbAsync(async db => (await db.OutboxMessages.ToListAsync()).Select(o => o.Content).ToList());
        contents.Count.ShouldBe(2);
        var linked = JsonSerializer.Deserialize<ParentLinkedEvent>(contents.Single(c => c.Contains("LinkedAtUtc")))!;
        linked.ParentKeycloakId.ShouldBeEmpty();
        linked.StudentDisplayName.ShouldBeEmpty();
        var unlinked = JsonSerializer.Deserialize<ParentUnlinkedEvent>(contents.Single(c => c.Contains("RevokedAtUtc")))!;
        unlinked.StudentKeycloakId.ShouldBeEmpty();
        unlinked.RevokedByRole.ShouldBe("Student");
    }

    [Fact]
    public async Task Parent_can_cancel_a_pending_request_without_an_unlinked_event()
    {
        await SeedAsync();
        var linkId = await RequestAsync(ParentUser);

        await using var ctx = _db.NewContext();
        (await Service(ctx).RevokeAsync(linkId, ParentUser)).Success.ShouldBeTrue();

        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync())).Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
        (await ApproveAsync(linkId)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Revoking_someone_elses_link_is_not_found_and_changes_nothing()
    {
        await SeedAsync();
        var linkId = await LinkAsync(ParentUser);

        await using var ctx = _db.NewContext();
        foreach (var intruder in new[] { ParentUser + 1, OtherStudentUser, StrangerUser })
        {
            var result = await Service(ctx).RevokeAsync(linkId, intruder);
            result.NotFound.ShouldBeTrue();
            result.ErrorCode.ShouldBe(ParentLinkErrorCodes.NotFound);
        }
        (await Service(ctx).RevokeAsync(linkId + 999, ParentUser)).NotFound.ShouldBeTrue();

        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync())).Status.ShouldBe(ParentStudentLinkStatus.Active);
        (await InDbAsync(db => db.OutboxMessages.CountAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>()))).ShouldBe(0);
    }

    [Fact]
    public async Task Repeated_revoke_is_idempotent_and_emits_a_single_event()
    {
        await SeedAsync();
        var linkId = await LinkAsync(ParentUser);

        await using var ctx = _db.NewContext();
        (await Service(ctx).RevokeAsync(linkId, StudentUser)).Success.ShouldBeTrue();
        (await Service(ctx).RevokeAsync(linkId, ParentUser)).Success.ShouldBeTrue();

        (await InDbAsync(db => db.OutboxMessages.CountAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>()))).ShouldBe(1);
        (await InDbAsync(db => db.ParentStudentLinks.SingleAsync())).RevokedByUserId.ShouldBe(StudentUser);
    }
}
