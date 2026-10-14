using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #438 (epic #435): veli panelinden çocuk hesabı. Başarılı akış (Student ParentManaged + bekleyen okul, ParentCreated/Active/
/// birincil bağlantı, rıza kaydı, ParentLinkedEvent), kullanıcı adı çakışması, kimlik deposu hatası (yerel satır yok), DB hatasında
/// kompanzasyon (auth-api hesabı silinir), tavanlar (10 açılmış hesap / açık çocuk), rıza doğrulaması, rol kilidi (yansıma) ve şifre
/// sıfırlama (IDOR 404, ParentManaged değil 403, denetim izi). Parola hiçbir yanıtta/logda yer almaz. SQLite; advisory lock ve
/// HTTP uçları ParentChildAccountEndpointsTests'te (Postgres).
/// </summary>
public class ParentChildAccountServiceTests : IDisposable
{
    private const int ParentUser = 8101;
    private const int OtherParentUser = 8102;
    private const int NewChildUserId = 9001;
    private static readonly string ChildPw = "Kal" + "em" + "2026x"; // test-only example, runtime-joined for the secret scanner
    private static readonly string NewPw = "Def" + "ter" + "77yz"; // test-only example, runtime-joined for the secret scanner

    private readonly TestDb _db = TestDb.Create();
    private readonly IChildAccountDirectory _directory = Substitute.For<IChildAccountDirectory>();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));
    private readonly ChildAccountTestLogger<ParentChildAccountService> _logger = new();
    private int _gradeId;
    private int _schoolId;

    public ParentChildAccountServiceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id, KeycloakId = $"kc-{id}", FullName = "Zeynep Yılmaz", Email = $"veli{id}@gmail.com"
            }).ToList());
        _directory.CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Success,
                Created: new CreateChildAccountResponse { UserId = NewChildUserId, KeycloakId = "kc-child-1" }));
        _directory.DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Success));
        _directory.DeleteByRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.NotFound, ChildAccountErrorCodes.NotFound));
        _directory.ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Success));
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk İlkokulu" };
        var grade = new Grade { Name = "3. Sınıf" };
        ctx.AddRange(school, grade);
        ctx.Parents.AddRange(new Parent { UserId = ParentUser }, new Parent { UserId = OtherParentUser });
        await ctx.SaveChangesAsync();
        _gradeId = grade.Id;
        _schoolId = school.Id;
    }

    private ParentChildAccountService Service(AppDbContext ctx)
        => new(ctx, _directory, new ParentChildAccess(ctx), _authApi, _time, _logger);

    private CreateChildAccountRequestDto Request(string username = "Ali.Kaya_9", string? pw = null, bool consent = true,
        string? version = ParentConsentVersions.Current, int? schoolId = -1)
    {
        var dto = new CreateChildAccountRequestDto
        {
            FirstName = " Ali ", LastName = "Kaya", Username = username, GradeId = _gradeId,
            SchoolId = schoolId == -1 ? _schoolId : schoolId, StudentNumber = "  42 ", ConsentAccepted = consent, ConsentVersion = version
        };
        dto.Password = pw ?? ChildPw;
        return dto;
    }

    private async Task<ParentChildAccountResultDto> CreateAsync(CreateChildAccountRequestDto? request = null, int parentUser = ParentUser,
        AppDbContext? ctx = null)
    {
        if (ctx != null)
            return await Service(ctx).CreateAsync(parentUser, request ?? Request(), "203.0.113.7");
        await using var c = _db.NewContext();
        return await Service(c).CreateAsync(parentUser, request ?? Request(), "203.0.113.7");
    }

    private Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> work) => InDb(work);

    private async Task<T> InDb<T>(Func<AppDbContext, Task<T>> work)
    {
        await using var ctx = _db.NewContext();
        return await work(ctx);
    }

    private void AssertNoPasswordLeak(object? response)
    {
        var json = JsonSerializer.Serialize(response);
        json.ShouldNotContain(ChildPw);
        json.ShouldNotContain(NewPw);
        foreach (var line in _logger.Lines)
        {
            line.ShouldNotContain(ChildPw);
            line.ShouldNotContain(NewPw);
        }
    }

    // ---- açma: başarılı akış ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_success_writes_student_primary_active_link_consent_and_linked_event_in_one_go()
    {
        var result = await CreateAsync();

        result.Success.ShouldBeTrue(result.Message);
        result.Child.ShouldNotBeNull();
        result.Child!.Username.ShouldBe("ali.kaya_9"); // kırpılıp küçük harfe
        result.Child.StudentName.ShouldBe("Ali Kaya");

        await _directory.Received(1).CreateAsync(
            Arg.Is<CreateChildAccountRequest>(r => r.Username == "ali.kaya_9" && r.FirstName == "Ali" && r.LastName == "Kaya"
                && ChildPw.Equals(r.Password) && r.RequestId != Guid.Empty),
            Arg.Any<CancellationToken>());
        await _directory.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _directory.DidNotReceive().DeleteByRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        var student = await InDbAsync(db => db.Students.SingleAsync(s => s.UserId == NewChildUserId));
        student.ParentManaged.ShouldBeTrue();
        student.GradeId.ShouldBe(_gradeId);
        student.SchoolId.ShouldBe(_schoolId);
        student.SchoolVerifiedAt.ShouldBeNull(); // #361: velinin seçtiği okul bekleyen üyelik
        student.StudentNumber.ShouldBe("42");
        result.Child.StudentId.ShouldBe(student.Id);

        var link = await InDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.StudentId == student.Id));
        link.Origin.ShouldBe(ParentStudentLinkOrigin.ParentCreated);
        link.Status.ShouldBe(ParentStudentLinkStatus.Active);
        link.IsPrimary.ShouldBeTrue();
        link.ActivatedAt.ShouldBe(_time.Now.UtcDateTime);
        result.Child.LinkId.ShouldBe(link.Id);

        var parentId = await InDbAsync(db => db.Parents.Where(p => p.UserId == ParentUser).Select(p => p.Id).SingleAsync());
        var consent = await InDbAsync(db => db.ParentConsents.SingleAsync());
        consent.ParentId.ShouldBe(parentId);
        consent.StudentId.ShouldBe(student.Id);
        consent.Version.ShouldBe(ParentConsentVersions.Current);
        consent.AcceptedAt.ShouldBe(_time.Now.UtcDateTime);
        consent.IpAddress.ShouldBe("203.0.113.7");

        var outbox = await InDbAsync(db => db.OutboxMessages.ToListAsync());
        var linked = outbox.ShouldHaveSingleItem();
        linked.Type.ShouldBe(OutboxEventRegistry.NameFor<ParentLinkedEvent>());
        var evt = JsonSerializer.Deserialize<ParentLinkedEvent>(linked.Content)!;
        evt.LinkId.ShouldBe(link.Id);
        evt.StudentUserId.ShouldBe(NewChildUserId);
        evt.ParentUserId.ShouldBe(ParentUser);
        evt.StudentKeycloakId.ShouldBe("kc-child-1");
        evt.ParentKeycloakId.ShouldBe($"kc-{ParentUser}");

        AssertNoPasswordLeak(result);
    }

    [Fact]
    public async Task Create_without_school_leaves_the_student_school_empty()
    {
        var result = await CreateAsync(Request(schoolId: null));

        result.Success.ShouldBeTrue(result.Message);
        (await InDbAsync(db => db.Students.SingleAsync(s => s.UserId == NewChildUserId))).SchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task Created_child_appears_in_my_children_as_parent_managed()
    {
        await CreateAsync();

        await using var ctx = _db.NewContext();
        var children = await new ParentLinkService(ctx, ParentInviteCodeHasherTests.Create(), _authApi,
            Substitute.For<IParentRedeemAttemptGuard>(), _time).GetParentChildrenAsync(ParentUser);
        var child = children.ShouldNotBeNull().ShouldHaveSingleItem();
        child.Status.ShouldBe("Active");
        child.IsPrimary.ShouldBeTrue();
        child.ParentManaged.ShouldBeTrue();
        child.CanResetPassword.ShouldBeTrue();
    }

    // ---- açma: hata yolları -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_username_is_409_and_writes_nothing()
    {
        _directory.CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Conflict, ChildAccountErrorCodes.UsernameTaken));

        var result = await CreateAsync();

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.UsernameTaken);
        (await InDbAsync(db => db.Students.CountAsync())).ShouldBe(0);
        (await InDbAsync(db => db.ParentConsents.CountAsync())).ShouldBe(0);
        await _directory.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _directory.DidNotReceive().DeleteByRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        AssertNoPasswordLeak(result);
    }

    [Fact]
    public async Task Keycloak_failure_is_502_leaves_no_local_rows_and_compensates_by_request_id()
    {
        var sent = Guid.Empty;
        _directory.CreateAsync(Arg.Do<CreateChildAccountRequest>(r => sent = r.RequestId), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Unavailable, ChildAccountErrorCodes.Unavailable));

        var result = await CreateAsync();

        // Yanıt alınamadı: auth-api hesabı açmış olabilir; aynı requestId ile geri alma istenir (userId bilinmiyor).
        sent.ShouldNotBe(Guid.Empty);
        await _directory.Received(1).DeleteByRequestIdAsync(sent, Arg.Any<CancellationToken>());
        await _directory.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

        result.Success.ShouldBeFalse();
        result.Upstream.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.Unavailable);
        (await InDbAsync(db => db.Students.CountAsync())).ShouldBe(0);
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(0);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
        AssertNoPasswordLeak(result);
    }

    [Theory]
    [InlineData(ChildAccountErrorCodes.RequestInProgress)]
    [InlineData(ChildAccountErrorCodes.RequestCancelled)]
    public async Task Request_state_conflicts_are_not_reported_as_username_taken(string code)
    {
        _directory.CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Conflict, code));

        var result = await CreateAsync();

        result.Upstream.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.Unavailable);
        await _directory.Received(1).DeleteByRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_create_request_gets_its_own_request_id()
    {
        var ids = new List<Guid>();
        _directory.CreateAsync(Arg.Do<CreateChildAccountRequest>(r => ids.Add(r.RequestId)), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Conflict, ChildAccountErrorCodes.UsernameTaken));

        await CreateAsync();
        await CreateAsync();

        ids.Count.ShouldBe(2);
        ids.ShouldAllBe(id => id != Guid.Empty);
        ids.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Client_cancellation_during_create_compensates_by_request_id_and_rethrows()
    {
        using var cts = new CancellationTokenSource();
        var sent = Guid.Empty;
        _directory.CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>())
            .Returns<ChildAccountCallResult>(ci =>
            {
                sent = ci.Arg<CreateChildAccountRequest>().RequestId;
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            Service(ctx).CreateAsync(ParentUser, Request(), "203.0.113.7", cts.Token));

        await _directory.Received(1).DeleteByRequestIdAsync(sent, CancellationToken.None);
        (await InDbAsync(db => db.Students.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Failed_request_id_compensation_is_logged_as_error()
    {
        _directory.CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Unavailable, ChildAccountErrorCodes.Unavailable));
        _directory.DeleteByRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Conflict, ChildAccountErrorCodes.NotParentManaged));

        (await CreateAsync()).Upstream.ShouldBeTrue();

        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("Kompanzasyon BAŞARISIZ"));
    }

    [Fact]
    public async Task Keycloak_password_policy_rejection_maps_to_PasswordRejected()
    {
        _directory.CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Invalid, ChildAccountErrorCodes.PasswordRejected));

        var result = await CreateAsync();

        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        result.Conflict.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
    }

    private sealed class FailOnConsentInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ParentConsent>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated DB failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Db_failure_rolls_back_and_deletes_the_auth_api_account()
    {
        await using var ctx = _db.NewContext(new FailOnConsentInsert());

        var result = await CreateAsync(ctx: ctx);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.Busy);
        await _directory.Received(1).DeleteAsync(NewChildUserId, Arg.Any<CancellationToken>());
        (await InDbAsync(db => db.Students.CountAsync())).ShouldBe(0); // transaction geri alındı
        (await InDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(0);
        (await InDbAsync(db => db.ParentConsents.CountAsync())).ShouldBe(0);
        (await InDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
        _logger.Lines.ShouldContain(l => l.Contains("geri alınıyor"));
        AssertNoPasswordLeak(result);
    }

    [Fact]
    public async Task Failed_compensation_is_logged_as_error()
    {
        _directory.DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Unavailable));
        await using var ctx = _db.NewContext(new FailOnConsentInsert());

        (await CreateAsync(ctx: ctx)).ErrorCode.ShouldBe(ParentLinkErrorCodes.Busy);

        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("Kompanzasyon BAŞARISIZ"));
    }

    [Theory]
    [InlineData("abc")] // kısa
    [InlineData("bu.kullanici.adi.otuz.karakterden.uzun")]
    [InlineData("ali-kaya")]
    [InlineData("ali kaya")]
    [InlineData("ayşe")]
    [InlineData("ali@kaya.com")]
    public async Task Invalid_username_is_400_before_any_external_call(string username)
    {
        var result = await CreateAsync(Request(username));

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.InvalidUsername);
        await _directory.DidNotReceive().CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("kisa1")] // < 8
    [InlineData("sadeceharf")] // rakam yok
    [InlineData("12345678")] // harf yok
    [InlineData("xali.kaya_9x1")] // kullanıcı adını içeriyor
    public async Task Weak_password_is_400_and_never_echoed(string pw)
    {
        var result = await CreateAsync(Request("ali.kaya_9", pw));

        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        JsonSerializer.Serialize(result).ShouldNotContain(pw);
        await _directory.DidNotReceive().CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consent_is_required_and_version_must_be_current()
    {
        (await CreateAsync(Request(consent: false))).ErrorCode.ShouldBe(ParentChildAccountErrorCodes.ConsentRequired);
        (await CreateAsync(Request(version: "kvkk-2020-01"))).ErrorCode.ShouldBe(ParentChildAccountErrorCodes.ConsentVersionMismatch);
        (await CreateAsync(Request(version: null))).ErrorCode.ShouldBe(ParentChildAccountErrorCodes.ConsentVersionMismatch);
        await _directory.DidNotReceive().CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_grade_or_school_is_400()
    {
        var badGrade = Request();
        badGrade.GradeId = 999_999;
        (await CreateAsync(badGrade)).ErrorCode.ShouldBe(ParentChildAccountErrorCodes.InvalidGrade);
        (await CreateAsync(Request(schoolId: 999_999))).ErrorCode.ShouldBe(ParentChildAccountErrorCodes.SchoolNotFound);
    }

    [Fact]
    public async Task Caller_without_parent_record_is_404()
    {
        var result = await CreateAsync(parentUser: 4242);

        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.ProfileNotFound);
    }

    // ---- tavanlar -----------------------------------------------------------------------------------------------------

    private async Task SeedCreatedChildrenAsync(int count, ParentStudentLinkStatus status = ParentStudentLinkStatus.Revoked)
    {
        await using var ctx = _db.NewContext();
        var parentId = await ctx.Parents.Where(p => p.UserId == ParentUser).Select(p => p.Id).SingleAsync();
        for (var i = 0; i < count; i++)
        {
            var s = new Student { UserId = 20_000 + i, StudentNumber = $"c{i}", ParentManaged = true };
            ctx.Students.Add(s);
            await ctx.SaveChangesAsync();
            ctx.ParentStudentLinks.Add(new ParentStudentLink
            {
                ParentId = parentId, StudentId = s.Id, Status = status, Origin = ParentStudentLinkOrigin.ParentCreated,
                IsPrimary = status == ParentStudentLinkStatus.Active, CreatedAt = _time.Now.UtcDateTime,
                ActivatedAt = _time.Now.UtcDateTime, RevokedAt = status == ParentStudentLinkStatus.Revoked ? _time.Now.UtcDateTime : null
            });
        }
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Tenth_account_is_allowed_eleventh_is_409_even_if_earlier_links_were_revoked()
    {
        await SeedCreatedChildrenAsync(9); // koparılmış ama hesaplar duruyor — tavan "açılmış hesap" sayar

        (await CreateAsync()).Success.ShouldBeTrue();

        _directory.ClearReceivedCalls();
        var eleventh = await CreateAsync(Request("ikinci.cocuk"));
        eleventh.Conflict.ShouldBeTrue();
        eleventh.ErrorCode.ShouldBe(ParentChildAccountErrorCodes.ChildAccountLimitReached);
        eleventh.Message.ShouldContain("10");
        await _directory.DidNotReceive().CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleted_child_frees_the_slot()
    {
        await SeedCreatedChildrenAsync(10);
        await using (var ctx = _db.NewContext())
        {
            var first = await ctx.Students.OrderBy(s => s.Id).FirstAsync();
            ctx.Students.Remove(first); // soft-delete
            await ctx.SaveChangesAsync();
        }

        (await CreateAsync()).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Open_children_cap_is_enforced_too()
    {
        // Açık (Active) 10 çocuk — kodla bağlanmış olsalar bile velinin açık çocuk tavanı dolu.
        await using (var ctx = _db.NewContext())
        {
            var parentId = await ctx.Parents.Where(p => p.UserId == ParentUser).Select(p => p.Id).SingleAsync();
            for (var i = 0; i < 10; i++)
            {
                var s = new Student { UserId = 30_000 + i, StudentNumber = $"o{i}" };
                ctx.Students.Add(s);
                await ctx.SaveChangesAsync();
                ctx.ParentStudentLinks.Add(new ParentStudentLink
                {
                    ParentId = parentId, StudentId = s.Id, Status = ParentStudentLinkStatus.Active, Origin = ParentStudentLinkOrigin.InviteCode,
                    CreatedAt = _time.Now.UtcDateTime, ActivatedAt = _time.Now.UtcDateTime
                });
            }
            await ctx.SaveChangesAsync();
        }

        var result = await CreateAsync();

        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.ParentLimitReached);
        await _directory.DidNotReceive().CreateAsync(Arg.Any<CreateChildAccountRequest>(), Arg.Any<CancellationToken>());
    }

    // ---- rol kilidi ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Endpoints_are_parent_only()
    {
        // BaseController'ın rolsüz [Authorize]'ı da miras gelir; rol taşıyan tek attribute Parent olmalı.
        var authorize = typeof(ParentChildAccountsController).GetCustomAttributes<AuthorizeAttribute>()
            .Where(a => !string.IsNullOrEmpty(a.Roles)).ShouldHaveSingleItem();
        authorize.Roles.ShouldBe("Parent"); // Student / Teacher / Admin → 403
        typeof(ParentChildAccountsController).GetMethods()
            .Where(m => m.DeclaringType == typeof(ParentChildAccountsController))
            .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>())
            .ShouldBeEmpty(); // metot seviyesinde rolü genişleten attribute yok
        typeof(ParentChildAccountsController).GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
    }

    // ---- şifre sıfırlama ----------------------------------------------------------------------------------------------

    private async Task<int> CreatedStudentIdAsync()
    {
        var created = await CreateAsync();
        created.Success.ShouldBeTrue(created.Message);
        return created.Child!.StudentId;
    }

    /// <summary>Varsayılan = <see cref="NewPw"/> (çalışma anında birleştirildiği için default parametre olamaz).</summary>
    private const string DefaultPw = "<default>";

    private async Task<ParentLinkResponseDto> ResetAsync(int studentId, string? pw = DefaultPw, int parentUser = ParentUser)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).ResetPasswordAsync(parentUser, studentId, pw == DefaultPw ? NewPw : pw);
    }

    /// <summary>İkinci veli: oluşturulan çocuğa Active ama birincil OLMAYAN bağlantı.</summary>
    private async Task LinkSecondParentAsync(int studentId)
    {
        await using var ctx = _db.NewContext();
        var otherParentId = await ctx.Parents.Where(p => p.UserId == OtherParentUser).Select(p => p.Id).SingleAsync();
        ctx.ParentStudentLinks.Add(new ParentStudentLink
        {
            ParentId = otherParentId, StudentId = studentId, Status = ParentStudentLinkStatus.Active,
            Origin = ParentStudentLinkOrigin.InviteCode, IsPrimary = false,
            CreatedAt = _time.Now.UtcDateTime.AddMinutes(5), ActivatedAt = _time.Now.UtcDateTime.AddMinutes(5)
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Reset_password_by_a_secondary_parent_is_403_OnlyPrimaryParent()
    {
        var studentId = await CreatedStudentIdAsync();
        await LinkSecondParentAsync(studentId);

        var result = await ResetAsync(studentId, parentUser: OtherParentUser);

        result.Forbidden.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentChildAccountErrorCodes.OnlyPrimaryParent);
        result.Message.ShouldNotBeNullOrWhiteSpace();
        await _directory.DidNotReceive().ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await InDbAsync(db => db.ParentAccessAudits.CountAsync())).ShouldBe(0);

        // Birincil veli hâlâ sıfırlayabilir.
        (await ResetAsync(studentId)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task My_children_tells_only_the_primary_parent_that_it_can_reset_the_password()
    {
        var studentId = await CreatedStudentIdAsync();
        await LinkSecondParentAsync(studentId);

        await using var ctx = _db.NewContext();
        var links = new ParentLinkService(ctx, ParentInviteCodeHasherTests.Create(), _authApi,
            Substitute.For<IParentRedeemAttemptGuard>(), _time);
        var primary = (await links.GetParentChildrenAsync(ParentUser)).ShouldNotBeNull().ShouldHaveSingleItem();
        var secondary = (await links.GetParentChildrenAsync(OtherParentUser)).ShouldNotBeNull().ShouldHaveSingleItem();

        primary.CanResetPassword.ShouldBeTrue();
        secondary.ParentManaged.ShouldBeTrue();
        secondary.IsPrimary.ShouldBeFalse();
        secondary.CanResetPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_password_calls_auth_api_for_the_child_and_writes_an_audit_row()
    {
        var studentId = await CreatedStudentIdAsync();

        var result = await ResetAsync(studentId);

        result.Success.ShouldBeTrue(result.Message);
        await _directory.Received(1).ResetPasswordAsync(NewChildUserId, NewPw, Arg.Any<CancellationToken>());
        var audit = await InDbAsync(db => db.ParentAccessAudits.SingleAsync());
        audit.StudentId.ShouldBe(studentId);
        audit.Endpoint.ShouldBe(ParentAccessEndpoints.ChildPasswordReset);
        audit.At.ShouldBe(_time.Now.UtcDateTime);

        // İkinci sıfırlama da ayrı satır (tekilleştirme yok).
        (await ResetAsync(studentId)).Success.ShouldBeTrue();
        (await InDbAsync(db => db.ParentAccessAudits.CountAsync())).ShouldBe(2);
        AssertNoPasswordLeak(result);
    }

    [Fact]
    public async Task Reset_password_of_another_parents_child_is_404_without_calling_auth_api()
    {
        var studentId = await CreatedStudentIdAsync();

        var result = await ResetAsync(studentId, parentUser: OtherParentUser);

        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentLinkErrorCodes.NotFound);
        (await ResetAsync(987_654)).NotFound.ShouldBeTrue(); // olmayan öğrenci: aynı yanıt
        await _directory.DidNotReceive().ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await InDbAsync(db => db.ParentAccessAudits.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Reset_password_after_the_link_is_revoked_is_404()
    {
        var studentId = await CreatedStudentIdAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.ParentStudentLinks.ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                .SetProperty(l => l.RevokedAt, _time.Now.UtcDateTime));
        }

        (await ResetAsync(studentId)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Reset_password_of_a_self_registered_child_is_403()
    {
        int studentId;
        await using (var ctx = _db.NewContext())
        {
            var parentId = await ctx.Parents.Where(p => p.UserId == ParentUser).Select(p => p.Id).SingleAsync();
            var s = new Student { UserId = 40_000, StudentNumber = "self", ParentManaged = false };
            ctx.Students.Add(s);
            await ctx.SaveChangesAsync();
            ctx.ParentStudentLinks.Add(new ParentStudentLink
            {
                ParentId = parentId, StudentId = s.Id, Status = ParentStudentLinkStatus.Active, Origin = ParentStudentLinkOrigin.InviteCode,
                IsPrimary = true, CreatedAt = _time.Now.UtcDateTime, ActivatedAt = _time.Now.UtcDateTime
            });
            await ctx.SaveChangesAsync();
            studentId = s.Id;
        }

        var result = await ResetAsync(studentId);

        result.Forbidden.ShouldBeTrue();
        result.ErrorCode.ShouldBe(ParentChildAccountErrorCodes.NotParentManaged);
        await _directory.DidNotReceive().ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kisa1")]
    [InlineData("sadeceharf")]
    public async Task Reset_password_validates_the_new_password(string? pw)
    {
        var studentId = await CreatedStudentIdAsync();

        var result = await ResetAsync(studentId, pw);

        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        await _directory.DidNotReceive().ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_password_maps_auth_api_failures_without_writing_an_audit_row()
    {
        var studentId = await CreatedStudentIdAsync();

        _directory.ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.NotFound, ChildAccountErrorCodes.NotFound));
        (await ResetAsync(studentId)).NotFound.ShouldBeTrue();

        _directory.ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Invalid, ChildAccountErrorCodes.PasswordRejected));
        (await ResetAsync(studentId)).ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);

        _directory.ResetPasswordAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChildAccountCallResult(ChildAccountCallStatus.Unavailable));
        var upstream = await ResetAsync(studentId);
        upstream.ShouldBeOfType<ParentChildAccountResultDto>().Upstream.ShouldBeTrue();

        (await InDbAsync(db => db.ParentAccessAudits.CountAsync())).ShouldBe(0);
        AssertNoPasswordLeak(upstream);
    }
}

/// <summary>Testte log satırlarını toplayan basit logger (parola sızıntısı kontrolü).</summary>
internal sealed class ChildAccountTestLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message);

    public ConcurrentQueue<Entry> Entries { get; } = new();

    public IEnumerable<string> Lines => Entries.Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Enqueue(new Entry(logLevel, formatter(state, exception) + (exception == null ? string.Empty : " | " + exception)));
}
