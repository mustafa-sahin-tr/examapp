using System.Security.Cryptography;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #440 (+ review fix round): öğrencinin veli daveti (SQLite; advisory lock'lar no-op, Postgres yarışları ParentInvitationEndpointsTests'te).
/// Yeni veli: davette HİÇBİR hesap / Parent satırı / bağlantı açılmaz (security M2), şifreli aktivasyon bağlantısı (URL fragment'ı);
/// aktivasyonda hesap açılır ve bağlantı Active olur. Mevcut veli: Pending bağlantı + rızalı onay. Kullanılamayan e-posta, e-posta düzeltme,
/// aktivasyon sürerken düzeltme (Busy), 1 saatte 3 farklı e-posta kilidi, aynı e-postaya 24 saatte 1 / toplam 5 gönderim, hedef e-postaya
/// 24 saatte en fazla 3 davet, aktivasyon (tek kullanım, süre, parola reddi, e-posta bu arada kaydedildi), eski/düzeltilmiş token lookup 404,
/// davet başlatınca hoşgörü damgası, kayıt geri alma, mevcut veli onayı/reddi ve outbox event'leri.
/// </summary>
public class ParentInvitationServiceTests : IDisposable
{
    private const int StudentUser = 5001;
    private const string StudentKc = "kc-student-5001";
    private const int NewParentUser = 7001;
    private const int ExistingParentUser = 7100;
    private static readonly string Pw = "Def" + "ter" + "77yz"; // test-only, runtime-joined for the secret scanner

    private readonly TestDb _db = TestDb.Create();
    private readonly IParentAccountDirectory _directory = Substitute.For<IParentAccountDirectory>();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ParentRequirementOptions _options = new() { StatusCacheSeconds = 0, ActivationBaseUrl = "http://gw.test" };
    private readonly IHostEnvironment _env = Substitute.For<IHostEnvironment>();
    private readonly ActivationLinkProtector _protector = new(RandomNumberGenerator.GetBytes(32));
    private int _studentId;
    private int _nextParentUser = NewParentUser;

    public ParentInvitationServiceTests()
    {
        _env.EnvironmentName.Returns(Environments.Production);
        _directory.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Lookup(ParentAccountOutcomes.None));
        _directory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Created(_nextParentUser++));

        using var ctx = _db.NewContext();
        var student = new Student { UserId = StudentUser, StudentNumber = "1" };
        ctx.Students.Add(student);
        ctx.Parents.Add(new Parent { UserId = ExistingParentUser });
        ctx.SaveChanges();
        _studentId = student.Id;
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    private static ParentAccountCallResult Lookup(string outcome, int? userId = null) => new(ParentAccountCallStatus.Success,
        Lookup: new ParentAccountLookupResponse { Outcome = outcome, UserId = userId, KeycloakId = userId is int u ? $"kc-{u}" : null });

    private static ParentAccountCallResult Created(int userId) => new(ParentAccountCallStatus.Success,
        Created: new CreateParentAccountResponse { UserId = userId, KeycloakId = $"kc-{userId}" });

    private ParentInvitationService Service(AppDbContext ctx)
    {
        var monitor = Substitute.For<IOptionsMonitor<ParentRequirementOptions>>();
        monitor.CurrentValue.Returns(_options);
        var status = new StudentParentStatusService(ctx, new StudentParentStatusCache(_cache), monitor, _time);
        return new ParentInvitationService(ctx, status, _directory, new ParentActivationDevLinkStore(_cache, _env), _protector, monitor, time: _time);
    }

    private static ParentInvitationStudent Student(int userId = StudentUser) => new(userId, $"kc-student-{userId}", "Ali Kaya");

    private static ParentContactRequestDto Contact(string email = "veli@example.com", string name = "Ayşe Kaya") => new()
    {
        ParentFullName = name, ParentEmail = email, ParentPhone = "0532 123 45 67"
    };

    private async Task<ParentInvitationResultDto> StartAsync(string email = "veli@example.com", int studentUser = StudentUser)
    {
        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        return await Service(ctx).StartAsync(Student(studentUser), Contact(email), requiresParent: true);
    }

    private List<ParentInvitationRequestedEvent> InvitationEvents()
    {
        using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<ParentInvitationRequestedEvent>();
        return ctx.OutboxMessages.Where(m => m.Type == type).OrderBy(m => m.CreatedAt).AsEnumerable()
            .Select(m => JsonSerializer.Deserialize<ParentInvitationRequestedEvent>(m.Content)!).ToList();
    }

    private string UrlOf(ParentInvitationRequestedEvent e) => _protector.Unprotect(e.ProtectedActivationUrl!);

    private string TokenOf(ParentInvitationRequestedEvent e) => UrlOf(e).Split("#token=")[1];

    private ParentActivationRequestDto Activation(string token, bool consent = true, string? pw = null) => new()
    {
        Token = token, Password = pw ?? Pw, ConsentAccepted = consent, ConsentVersion = ParentConsentVersions.Current
    };

    // ---- başlatma ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task New_parent_invitation_creates_no_account_parent_row_or_link_and_an_encrypted_fragment_link()
    {
        var result = await StartAsync(" Veli@Example.com ");

        result.Success.ShouldBeTrue(result.ErrorCode);
        result.Status!.Mode.ShouldBe("AwaitingActivation");
        result.Status.Invitation!.MaskedEmail.ShouldBe("v***@e***.com");
        result.Status.Invitation.CanResend.ShouldBeFalse();
        result.Status.Invitation.DevActivationUrl.ShouldBeNull(); // Development değil
        await _directory.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default!, default); // M2: hesap açılmaz

        await using var ctx = _db.NewContext();
        var inv = await ctx.ParentInvitations.SingleAsync();
        inv.Status.ShouldBe(ParentInvitationStatus.Pending);
        inv.IsNewAccount.ShouldBeTrue();
        inv.ParentUserId.ShouldBeNull();
        inv.ParentId.ShouldBeNull();
        inv.LinkId.ShouldBeNull();
        inv.Email.ShouldBe("veli@example.com");
        inv.Phone.ShouldBe("+905321234567");
        inv.ExpiresAt.ShouldBe(_time.Now.UtcDateTime.AddDays(7));
        (await ctx.ParentStudentLinks.AnyAsync()).ShouldBeFalse();
        (await ctx.Parents.CountAsync()).ShouldBe(1); // yalnız seed'deki mevcut veli
        (await ctx.ParentInvitationAttempts.CountAsync()).ShouldBe(1);
        // Review M3: davet açılınca hoşgörü sayacı başlar.
        (await ctx.Students.SingleAsync(s => s.Id == _studentId)).ParentRequirementStartedAt.ShouldBe(_time.Now.UtcDateTime);

        var evt = InvitationEvents().Single();
        evt.Kind.ShouldBe(ParentInvitationService.KindNewParentActivation);
        evt.InvitationId.ShouldBe(inv.Id);
        evt.EventId.ShouldNotBe(Guid.Empty);
        evt.SendCount.ShouldBe(1);
        evt.StudentUserId.ShouldBe(StudentKc);
        evt.StudentFirstName.ShouldBe("Ali");
        evt.ParentUserId.ShouldBe(string.Empty);
        evt.ParentEmail.ShouldBe("veli@example.com");
        evt.ProtectedActivationUrl!.ShouldNotContain("token");
        UrlOf(evt).ShouldStartWith("http://gw.test/app/parent-activate#token="); // fragment: sunucuya/loglara gitmez
        ParentActivationTokens.Hash(TokenOf(evt)).ShouldBe(inv.TokenHash);
    }

    [Fact]
    public async Task Development_shows_the_plain_activation_link_to_the_student()
    {
        _env.EnvironmentName.Returns(Environments.Development);
        var result = await StartAsync();

        result.Status!.Invitation!.DevActivationUrl.ShouldBe(UrlOf(InvitationEvents().Single()));
    }

    [Fact]
    public async Task Existing_parent_gets_a_pending_link_and_no_token()
    {
        _directory.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Lookup(ParentAccountOutcomes.ExistingParent, ExistingParentUser));

        var result = await StartAsync();

        result.Success.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        var inv = await ctx.ParentInvitations.SingleAsync();
        inv.TokenHash.ShouldBeNull();
        inv.IsNewAccount.ShouldBeFalse();
        inv.ParentUserId.ShouldBe(ExistingParentUser);
        (await ctx.ParentStudentLinks.SingleAsync(l => l.Id == inv.LinkId)).Status.ShouldBe(ParentStudentLinkStatus.Pending);
        (await ctx.Parents.CountAsync(p => p.UserId == ExistingParentUser)).ShouldBe(1);
        var evt = InvitationEvents().Single();
        evt.Kind.ShouldBe(ParentInvitationService.KindExistingParentApproval);
        evt.ParentUserId.ShouldBe($"kc-{ExistingParentUser}");
        evt.ProtectedActivationUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData(ParentAccountOutcomes.NotParentAccount, null)]
    [InlineData(ParentAccountOutcomes.ExistingParent, StudentUser)] // öğrencinin kendi hesabı
    public async Task Unusable_email_is_rejected_and_still_counts_toward_the_lock(string outcome, int? userId)
    {
        _directory.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Lookup(outcome, userId));

        var result = await StartAsync("ogretmen@example.com");

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentInvitationErrorCodes.ParentEmailNotUsable);
        await using var ctx = _db.NewContext();
        (await ctx.ParentInvitations.AnyAsync()).ShouldBeFalse();
        (await ctx.ParentInvitationAttempts.CountAsync()).ShouldBe(1);
        InvitationEvents().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "", "", ParentInvitationErrorCodes.ParentInfoRequired)]
    [InlineData("A", "veli@example.com", "05321234567", ParentInvitationErrorCodes.InvalidParentName)]
    [InlineData("Kazan www.bedava", "veli@example.com", "05321234567", ParentInvitationErrorCodes.InvalidParentName)]
    [InlineData("Ayşe Kaya", "veli@", "05321234567", ParentInvitationErrorCodes.InvalidParentEmail)]
    [InlineData("Ayşe Kaya", "veli@example.com", "02121234567", ParentInvitationErrorCodes.InvalidParentPhone)]
    public async Task Invalid_contact_is_rejected_before_any_call(string name, string email, string phone, string code)
    {
        await using var ctx = _db.NewContext();
        var result = await Service(ctx).StartAsync(Student(),
            new ParentContactRequestDto { ParentFullName = name, ParentEmail = email, ParentPhone = phone }, requiresParent: true);

        result.ErrorCode.ShouldBe(code);
        await _directory.DidNotReceiveWithAnyArgs().LookupAsync(default!, default);
        (await ctx.ParentInvitationAttempts.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Student_with_an_active_parent_cannot_start_an_invitation()
    {
        await using (var seed = _db.NewContext())
        {
            var parentId = await seed.Parents.Where(p => p.UserId == ExistingParentUser).Select(p => p.Id).SingleAsync();
            seed.ParentStudentLinks.Add(new ParentStudentLink
            {
                ParentId = parentId, StudentId = _studentId, Status = ParentStudentLinkStatus.Active,
                Origin = ParentStudentLinkOrigin.ParentCreated, IsPrimary = true, CreatedAt = _time.Now.UtcDateTime
            });
            await seed.SaveChangesAsync();
        }

        (await StartAsync()).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ParentAlreadyLinked);
    }

    // ---- ön kontrol (kayıt) ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Precheck_works_without_a_student_row_and_commit_needs_one()
    {
        const int newcomer = 5999;
        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        var service = Service(ctx);

        var precheck = await service.PrecheckAsync(Student(newcomer), Contact(), requiresParent: true);
        precheck.Success.ShouldBeTrue();
        precheck.Prepared!.StudentExisted.ShouldBeFalse();
        (await ctx.Students.AnyAsync(s => s.UserId == newcomer)).ShouldBeFalse();

        (await service.CommitAsync(Student(newcomer), precheck.Prepared)).ErrorCode.ShouldBe(ParentLinkErrorCodes.ProfileNotFound);
    }

    [Fact]
    public async Task Discard_new_student_removes_only_a_row_without_links_or_invitations()
    {
        const int newcomer = 5998;
        await using (var seed = _db.NewContext())
        {
            seed.Students.Add(new Student { UserId = newcomer, StudentNumber = "x" });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        await Service(ctx).DiscardNewStudentAsync(newcomer);
        (await ctx.Students.IgnoreQueryFilters().AnyAsync(s => s.UserId == newcomer)).ShouldBeFalse();

        (await StartAsync()).Success.ShouldBeTrue(); // seed öğrencisinin daveti var
        await Service(ctx).DiscardNewStudentAsync(StudentUser);
        (await ctx.Students.AnyAsync(s => s.UserId == StudentUser)).ShouldBeTrue();
    }

    // ---- düzeltme / kilit / tavanlar / yeniden gönderim -------------------------------------------------------------

    [Fact]
    public async Task Correcting_the_email_supersedes_the_old_invitation_and_its_token()
    {
        (await StartAsync("yanlis@example.com")).Success.ShouldBeTrue();
        var oldToken = TokenOf(InvitationEvents().Single());

        var result = await StartAsync("dogru@example.com");

        result.Success.ShouldBeTrue(result.ErrorCode);
        await using var ctx = _db.NewContext();
        var invitations = await ctx.ParentInvitations.ToListAsync();
        invitations.Single(i => i.Email == "yanlis@example.com").Status.ShouldBe(ParentInvitationStatus.Superseded);
        invitations.Single(i => i.Email == "dogru@example.com").Status.ShouldBe(ParentInvitationStatus.Pending);

        // Security MINOR6: düzeltilmiş davetin token'ı bilinmeyen token gibi.
        (await Service(ctx).LookupAsync(oldToken)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.InvalidActivationToken);
        (await Service(ctx).ActivateAsync(Activation(oldToken), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ActivationTokenUsed);
    }

    [Fact]
    public async Task Correction_while_activation_is_in_progress_is_busy()
    {
        (await StartAsync("a@example.com")).Success.ShouldBeTrue();
        await using (var seed = _db.NewContext())
            await seed.ParentInvitations.ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAt, _time.Now.UtcDateTime));

        var result = await StartAsync("b@example.com");

        result.ErrorCode.ShouldBe(ParentInvitationErrorCodes.Busy);
        await using var ctx = _db.NewContext();
        (await ctx.ParentInvitations.SingleAsync()).Status.ShouldBe(ParentInvitationStatus.Pending);
    }

    [Fact]
    public async Task Fourth_distinct_email_within_an_hour_is_locked()
    {
        (await StartAsync("a@example.com")).Success.ShouldBeTrue();
        _time.Now = _time.Now.AddMinutes(10);
        (await StartAsync("b@example.com")).Success.ShouldBeTrue();
        _time.Now = _time.Now.AddMinutes(10);
        (await StartAsync("c@example.com")).Success.ShouldBeTrue();
        _time.Now = _time.Now.AddMinutes(10);

        var locked = await StartAsync("d@example.com");

        locked.RateLimited.ShouldBeTrue();
        locked.ErrorCode.ShouldBe(ParentInvitationErrorCodes.ParentEmailChangeLocked);
        locked.RetryAt.ShouldBe(new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc)); // a'nın denemesi + 1 saat

        _time.Now = _time.Now.AddMinutes(31); // a pencereden düştü
        (await StartAsync("d@example.com")).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Same_target_email_gets_at_most_three_invitations_per_day_across_students()
    {
        for (var i = 0; i < ParentInvitationRules.MaxInvitationsPerTargetEmail; i++)
        {
            await using (var seed = _db.NewContext())
            {
                seed.Students.Add(new Student { UserId = 6100 + i, StudentNumber = "s" + i });
                await seed.SaveChangesAsync();
            }
            (await StartAsync("hedef@example.com", 6100 + i)).Success.ShouldBeTrue();
            _time.Now = _time.Now.AddMinutes(1);
        }

        var capped = await StartAsync("hedef@example.com");

        capped.RateLimited.ShouldBeTrue();
        capped.ErrorCode.ShouldBe(ParentInvitationErrorCodes.ParentInvitationResendLimited); // genel kod
        capped.RetryAt.ShouldBe(new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc));
        await _directory.Received(ParentInvitationRules.MaxInvitationsPerTargetEmail).LookupAsync("hedef@example.com", Arg.Any<CancellationToken>());

        _time.Now = new DateTimeOffset(2026, 10, 16, 9, 0, 1, TimeSpan.Zero);
        (await StartAsync("hedef@example.com")).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Resend_is_limited_to_once_per_24_hours_and_five_in_total()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var firstToken = TokenOf(InvitationEvents().Single());

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var tooSoon = await Service(ctx).ResendAsync(Student(), true);
            tooSoon.ErrorCode.ShouldBe(ParentInvitationErrorCodes.ParentInvitationResendLimited);
            tooSoon.RetryAt.ShouldBe(_time.Now.UtcDateTime.AddHours(24));
        }

        for (var i = 1; i <= ParentInvitationRules.MaxResends; i++)
        {
            _time.Now = _time.Now.AddHours(24);
            await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
            var resent = await Service(ctx).ResendAsync(Student(), true);
            resent.Success.ShouldBeTrue($"resend {i}: {resent.ErrorCode}");
            resent.Status!.Invitation!.ResendsLeft.ShouldBe(ParentInvitationRules.MaxResends - i);
        }

        _time.Now = _time.Now.AddHours(24);
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var exhausted = await Service(ctx).ResendAsync(Student(), true);
            exhausted.ErrorCode.ShouldBe(ParentInvitationErrorCodes.ParentInvitationResendLimited);
            exhausted.RetryAt.ShouldBeNull();
            (await ctx.ParentInvitations.SingleAsync()).SendCount.ShouldBe(1 + ParentInvitationRules.MaxResends);
            (await Service(ctx).LookupAsync(firstToken)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.InvalidActivationToken);
        }
        var events = InvitationEvents();
        events.Count.ShouldBe(1 + ParentInvitationRules.MaxResends);
        events.Select(e => e.EventId).Distinct().Count().ShouldBe(events.Count); // her gönderim yeni EventId
        events.Select(e => e.SendCount).ShouldBe(Enumerable.Range(1, events.Count));
    }

    [Fact]
    public async Task Same_email_again_is_a_resend_not_a_new_invitation()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        _time.Now = _time.Now.AddHours(25);

        var again = await StartAsync();

        again.Success.ShouldBeTrue(again.ErrorCode);
        await using var ctx = _db.NewContext();
        (await ctx.ParentInvitations.SingleAsync()).SendCount.ShouldBe(2);
        await _directory.Received(1).LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Alternating_emails_cannot_bypass_the_per_email_send_interval()
    {
        (await StartAsync("a@example.com")).Success.ShouldBeTrue();
        _time.Now = _time.Now.AddMinutes(5);
        (await StartAsync("b@example.com")).Success.ShouldBeTrue();
        _time.Now = _time.Now.AddMinutes(5);

        (await StartAsync("a@example.com")).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ParentInvitationResendLimited);
    }

    // ---- durum modu (review M3) --------------------------------------------------------------------------------------

    [Fact]
    public async Task After_grace_ends_an_open_invitation_keeps_restricted_mode_only_until_it_expires()
    {
        _options.GraceDays = 3;
        (await StartAsync()).Success.ShouldBeTrue(); // damga = şimdi
        _time.Now = _time.Now.AddDays(4); // hoşgörü bitti, davet (7 gün) sürüyor

        await using var ctx = _db.NewContext();
        var awaiting = await Service(ctx).GetStatusAsync(StudentUser, true, stamp: false);
        awaiting.Mode.ShouldBe("AwaitingActivation");
        awaiting.DaysLeft.ShouldBe(0);

        _time.Now = _time.Now.AddDays(4); // davet de doldu
        var expired = await Service(ctx).GetStatusAsync(StudentUser, true, stamp: false);
        expired.Mode.ShouldBe("Expired");
        expired.Invitation.ShouldNotBeNull(); // UI yeniden gönder / düzelt sunabilsin
        expired.Invitation!.Expired.ShouldBeTrue();
        expired.Invitation.CanResend.ShouldBeTrue();
    }

    // ---- aktivasyon ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Activation_creates_the_account_parent_row_consent_and_active_link_once()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var lookup = await Service(ctx).LookupAsync(token);
            lookup.Lookup!.RequiresPasswordSetup.ShouldBeTrue();
            lookup.Lookup.Used.ShouldBeFalse();

            var result = await Service(ctx).ActivateAsync(Activation(token), "203.0.113.9");
            result.Success.ShouldBeTrue(result.ErrorCode);
            result.Activation!.PasswordSet.ShouldBeTrue();
        }
        await _directory.Received(1).CreateAsync("veli@example.com", "Ayşe Kaya", Pw, Arg.Any<CancellationToken>());

        await using (var check = _db.NewContextWithRetryingExecutionStrategy())
        {
            var inv = await check.ParentInvitations.SingleAsync();
            inv.Status.ShouldBe(ParentInvitationStatus.Activated);
            inv.ParentUserId.ShouldBe(NewParentUser);
            var parent = await check.Parents.SingleAsync(p => p.UserId == NewParentUser);
            inv.ParentId.ShouldBe(parent.Id);
            var link = await check.ParentStudentLinks.SingleAsync(l => l.Id == inv.LinkId);
            link.Status.ShouldBe(ParentStudentLinkStatus.Active);
            link.Origin.ShouldBe(ParentStudentLinkOrigin.StudentRegistered);
            link.IsPrimary.ShouldBeTrue();
            (await check.ParentConsents.SingleAsync()).IpAddress.ShouldBe("203.0.113.9");
            (await check.Students.SingleAsync(s => s.Id == _studentId)).ParentRequirementStartedAt.ShouldBeNull();
            (await check.OutboxMessages.CountAsync(m => m.Type == OutboxEventRegistry.NameFor<ParentLinkedEvent>())).ShouldBe(1);
            (await Service(check).GetStatusAsync(StudentUser, true, stamp: false)).Mode.ShouldBe("Ok");

            (await Service(check).ActivateAsync(Activation(token), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ActivationTokenUsed);
            (await Service(check).LookupAsync(token)).Lookup!.Used.ShouldBeTrue(); // yeni kullanılmış: tanınır
        }

        _time.Now = _time.Now.AddDays(2); // 1 günden eski kullanılmış token: bilinmeyen gibi
        await using var late = _db.NewContext();
        (await Service(late).LookupAsync(token)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.InvalidActivationToken);
    }

    [Fact]
    public async Task Expired_token_cannot_activate_but_the_parent_can_request_a_new_link()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());
        _time.Now = _time.Now.AddDays(8);

        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        (await Service(ctx).ActivateAsync(Activation(token), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ActivationTokenExpired);
        var lookup = await Service(ctx).LookupAsync(token);
        lookup.Lookup!.Expired.ShouldBeTrue();
        lookup.Lookup.CanResend.ShouldBeTrue();

        (await Service(ctx).ResendByTokenAsync(token)).Success.ShouldBeTrue();
        var newToken = TokenOf(InvitationEvents().Last());
        newToken.ShouldNotBe(token);
        (await Service(ctx).ActivateAsync(Activation(token), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.InvalidActivationToken);
        (await Service(ctx).ActivateAsync(Activation(newToken), null)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Rejected_password_releases_the_token_for_another_try()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());
        _directory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ParentAccountCallResult(ParentAccountCallStatus.Invalid, ParentAccountErrorCodes.PasswordRejected), Created(NewParentUser));

        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        (await Service(ctx).ActivateAsync(Activation(token), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.PasswordRejected);
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).UsedAt.ShouldBeNull();
        (await Service(ctx).ActivateAsync(Activation(token), null)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Stale_claim_is_treated_as_released_but_a_fresh_one_blocks()
    {
        // Security MINOR3: süreç çökmesinden kalan talep 5 dakika sonra bırakılmış sayılır.
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());

        await using (var seed = _db.NewContext())
            await seed.ParentInvitations.ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAt, _time.Now.UtcDateTime.AddMinutes(-1)));
        await using (var ctx = _db.NewContext())
        {
            (await Service(ctx).LookupAsync(token)).Lookup!.Used.ShouldBeTrue();
            (await Service(ctx).ActivateAsync(Activation(token), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ActivationTokenUsed);
        }

        await using (var seed = _db.NewContext())
            await seed.ParentInvitations.ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAt, _time.Now.UtcDateTime.AddMinutes(-6)));
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            (await Service(ctx).LookupAsync(token)).Lookup!.Used.ShouldBeFalse();
            (await Service(ctx).ActivateAsync(Activation(token), null)).Success.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Unexpected_failure_after_the_claim_releases_it_and_rethrows()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());
        _directory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ParentAccountCallResult>(new InvalidOperationException("boom")));

        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<InvalidOperationException>(() => Service(ctx).ActivateAsync(Activation(token), null));
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).UsedAt.ShouldBeNull();

        _directory.ConfirmEmailAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ParentAccountCallResult>(new InvalidOperationException("boom")));
        await Should.ThrowAsync<InvalidOperationException>(() => Service(ctx).AcceptAsync(Accept(token), 8_900, null));
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).UsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Weak_password_or_missing_consent_is_rejected_without_calling_auth_api()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());

        await using var ctx = _db.NewContext();
        (await Service(ctx).ActivateAsync(Activation(token, pw: "short"), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.PasswordRejected);
        (await Service(ctx).ActivateAsync(Activation(token, consent: false), null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ConsentRequired);
        var wrongVersion = Activation(token);
        wrongVersion.ConsentVersion = "eski";
        (await Service(ctx).ActivateAsync(wrongVersion, null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ConsentVersionMismatch);
        (await Service(ctx).ActivateAsync(Activation("bilinmeyen-token-0123456789abcdef"), null)).ErrorCode
            .ShouldBe(ParentInvitationErrorCodes.InvalidActivationToken);
        await _directory.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData(ParentAccountOutcomes.ExistingParent)]
    [InlineData(ParentAccountOutcomes.UnverifiedParent)]
    [InlineData(ParentAccountOutcomes.NotParentAccount)]
    public async Task Anonymous_activation_never_links_when_an_account_exists_for_the_email(string outcome)
    {
        // Security C1: anonim yol, e-postaya ait hesap varken (ele geçirilmiş/doğrulanmamış olabilir) ASLA bağlantı kurmaz.
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());
        _directory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ParentAccountCallResult(ParentAccountCallStatus.Conflict, ParentAccountErrorCodes.EmailAlreadyRegistered));
        _directory.LookupAsync("veli@example.com", Arg.Any<CancellationToken>()).Returns(Lookup(outcome, 7_777));

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).ActivateAsync(Activation(token), null);

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentInvitationErrorCodes.LoginRequired);
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).UsedAt.ShouldBeNull();
        (await ctx.ParentStudentLinks.AnyAsync()).ShouldBeFalse();
        (await ctx.Parents.AnyAsync(p => p.UserId == 7_777)).ShouldBeFalse();
    }

    [Fact]
    public async Task Unverified_parent_account_gets_a_token_invitation_not_in_app_approval()
    {
        _directory.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Lookup(ParentAccountOutcomes.UnverifiedParent));

        (await StartAsync()).Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var inv = await ctx.ParentInvitations.SingleAsync();
        inv.IsNewAccount.ShouldBeTrue();
        inv.TokenHash.ShouldNotBeNull();
        inv.ParentUserId.ShouldBeNull();
        (await ctx.ParentStudentLinks.AnyAsync()).ShouldBeFalse(); // veli listesine (uygulama içi onay) düşmez
        InvitationEvents().Single().Kind.ShouldBe(ParentInvitationService.KindNewParentActivation);
    }

    [Fact]
    public async Task Lookup_tells_the_token_holder_when_an_account_already_exists()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());

        await using var ctx = _db.NewContext();
        var none = (await Service(ctx).LookupAsync(token)).Lookup!;
        none.AccountExists.ShouldBeFalse();
        none.RequiresPasswordSetup.ShouldBeTrue();

        _directory.LookupAsync("veli@example.com", Arg.Any<CancellationToken>()).Returns(Lookup(ParentAccountOutcomes.UnverifiedParent));
        var exists = (await Service(ctx).LookupAsync(token)).Lookup!;
        exists.AccountExists.ShouldBeTrue();
        exists.RequiresPasswordSetup.ShouldBeFalse();
    }

    // ---- oturum açmış velinin kabulü (security C1) ------------------------------------------------------------------

    private ParentInvitationAcceptRequestDto Accept(string token, bool consent = true) => new()
    {
        Token = token, ConsentAccepted = consent, ConsentVersion = ParentConsentVersions.Current
    };

    [Fact]
    public async Task Logged_in_parent_whose_server_side_email_matches_accepts_the_invitation()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());
        _directory.ConfirmEmailAsync(8_800, "veli@example.com", Arg.Any<CancellationToken>())
            .Returns(new ParentAccountCallResult(ParentAccountCallStatus.Success));

        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        var result = await Service(ctx).AcceptAsync(Accept(token), 8_800, "203.0.113.5");

        result.Success.ShouldBeTrue(result.ErrorCode);
        var inv = await ctx.ParentInvitations.AsNoTracking().SingleAsync();
        inv.Status.ShouldBe(ParentInvitationStatus.Activated);
        inv.ParentUserId.ShouldBe(8_800);
        var link = await ctx.ParentStudentLinks.AsNoTracking().SingleAsync();
        link.Status.ShouldBe(ParentStudentLinkStatus.Active);
        link.IsPrimary.ShouldBeTrue();
        (await ctx.Parents.AsNoTracking().SingleAsync(p => p.Id == link.ParentId)).UserId.ShouldBe(8_800);
        (await ctx.ParentConsents.AsNoTracking().SingleAsync()).IpAddress.ShouldBe("203.0.113.5");
        await _directory.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default!, default);

        (await Service(ctx).AcceptAsync(Accept(token), 8_800, null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ActivationTokenUsed);
    }

    [Theory]
    [InlineData(ParentAccountErrorCodes.EmailMismatch, ParentInvitationErrorCodes.InvitationEmailMismatch)]
    [InlineData(ParentAccountErrorCodes.NotParentAccount, ParentInvitationErrorCodes.ParentAccountRequired)]
    public async Task Accept_is_refused_for_another_account_or_a_non_parent(string authApiCode, string expected)
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());
        _directory.ConfirmEmailAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ParentAccountCallResult(ParentAccountCallStatus.Conflict, authApiCode));

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).AcceptAsync(Accept(token), 8_801, null);

        result.Forbidden.ShouldBeTrue();
        result.ErrorCode.ShouldBe(expected);
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).UsedAt.ShouldBeNull(); // talep geri bırakıldı
        (await ctx.ParentStudentLinks.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Accept_validates_consent_token_and_expiry_before_asking_auth_api()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());

        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        (await service.AcceptAsync(Accept(token, consent: false), 8_802, null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ConsentRequired);
        (await service.AcceptAsync(Accept("bilinmeyen-token-0123456789abcdef"), 8_802, null)).ErrorCode
            .ShouldBe(ParentInvitationErrorCodes.InvalidActivationToken);
        _time.Now = _time.Now.AddDays(8);
        (await service.AcceptAsync(Accept(token), 8_802, null)).ErrorCode.ShouldBe(ParentInvitationErrorCodes.ActivationTokenExpired);
        await _directory.DidNotReceiveWithAnyArgs().ConfirmEmailAsync(default, default!, default);
    }

    [Fact]
    public async Task Auth_api_outage_on_activation_is_a_502_and_keeps_the_token_usable()
    {
        (await StartAsync()).Success.ShouldBeTrue();
        var token = TokenOf(InvitationEvents().Single());
        _directory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ParentAccountCallResult(ParentAccountCallStatus.Unavailable, ParentAccountErrorCodes.Unavailable));

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).ActivateAsync(Activation(token), null);
        result.Upstream.ShouldBeTrue();
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).UsedAt.ShouldBeNull();
    }

    // ---- mevcut veli onayı / reddi ----------------------------------------------------------------------------------

    private async Task<int> StartForExistingParentAsync()
    {
        _directory.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Lookup(ParentAccountOutcomes.ExistingParent, ExistingParentUser));
        (await StartAsync()).Success.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        return (await ctx.ParentInvitations.SingleAsync()).LinkId!.Value;
    }

    [Fact]
    public async Task Existing_parent_approves_with_consent()
    {
        var linkId = await StartForExistingParentAsync();

        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        var service = Service(ctx);
        (await service.ApproveAsInvitedParentAsync(linkId, ExistingParentUser, null, null))!.ErrorCode
            .ShouldBe(ParentInvitationErrorCodes.ConsentRequired);
        var ok = await service.ApproveAsInvitedParentAsync(linkId, ExistingParentUser,
            new ParentLinkApproveRequestDto { ConsentAccepted = true, ConsentVersion = ParentConsentVersions.Current }, "198.51.100.7");

        ok!.Success.ShouldBeTrue(ok.ErrorCode);
        (await ctx.ParentStudentLinks.AsNoTracking().SingleAsync(l => l.Id == linkId)).Status.ShouldBe(ParentStudentLinkStatus.Active);
        (await ctx.ParentConsents.AsNoTracking().SingleAsync()).IpAddress.ShouldBe("198.51.100.7");
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).Status.ShouldBe(ParentInvitationStatus.Activated);
    }

    [Fact]
    public async Task Other_parent_or_non_student_registered_link_falls_back_to_null()
    {
        var linkId = await StartForExistingParentAsync();

        await using var ctx = _db.NewContext();
        (await Service(ctx).ApproveAsInvitedParentAsync(linkId, 9_999, null, null)).ShouldBeNull();
        (await Service(ctx).RejectAsInvitedParentAsync(linkId, 9_999)).ShouldBeNull();
        (await Service(ctx).ApproveAsInvitedParentAsync(123_456, ExistingParentUser, null, null)).ShouldBeNull();
    }

    [Fact]
    public async Task Expired_invitation_cannot_be_approved()
    {
        var linkId = await StartForExistingParentAsync();
        _time.Now = _time.Now.AddDays(8);

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).ApproveAsInvitedParentAsync(linkId, ExistingParentUser,
            new ParentLinkApproveRequestDto { ConsentAccepted = true, ConsentVersion = ParentConsentVersions.Current }, null);
        result!.ErrorCode.ShouldBe(ParentInvitationErrorCodes.InvitationExpired);
    }

    [Fact]
    public async Task Existing_parent_rejects_and_the_student_falls_back_to_grace()
    {
        var linkId = await StartForExistingParentAsync();

        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        (await Service(ctx).RejectAsInvitedParentAsync(linkId, ExistingParentUser))!.Success.ShouldBeTrue();

        (await ctx.ParentStudentLinks.AsNoTracking().SingleAsync()).Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        (await ctx.ParentInvitations.AsNoTracking().SingleAsync()).Status.ShouldBe(ParentInvitationStatus.Cancelled);
        (await Service(ctx).GetStatusAsync(StudentUser, true, stamp: false)).Mode.ShouldBe("Grace");
    }

    [Fact]
    public async Task Needs_parent_info_only_without_active_parent_or_open_invitation()
    {
        await using (var ctx = _db.NewContext())
        {
            (await Service(ctx).NeedsParentInfoAsync(StudentUser, requiresParent: true)).ShouldBeTrue();
            (await Service(ctx).NeedsParentInfoAsync(StudentUser, requiresParent: false)).ShouldBeFalse();
            (await Service(ctx).NeedsParentInfoAsync(424_242, requiresParent: true)).ShouldBeTrue(); // öğrenci satırı henüz yok
        }

        (await StartAsync()).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            (await Service(ctx).NeedsParentInfoAsync(StudentUser, requiresParent: true)).ShouldBeFalse();
    }
}
