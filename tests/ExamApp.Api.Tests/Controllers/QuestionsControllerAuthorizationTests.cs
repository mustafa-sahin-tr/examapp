using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #287 security review H1: <c>api/questions</c> — gerçek MVC + yetkilendirme pipeline'ı (TestServer), gerçek
/// sahiplik guard'ı (SQLite), servisler stub. Öğrenci/veli her uçta 403; öğretmen yalnızca kendi sorusu/testi;
/// admin her şey; BadgeService servis hesabı image + classification (+ classifier-cache) uçlarında geçer.
/// </summary>
public class QuestionsControllerAuthorizationTests : IDisposable
{
    private const int OwnerTeacher = 1;     // W (orijinal) sahibi → eski soru Q1'in sahibi
    private const int CopierTeacher = 2;    // W'nin kopyası W2'nin sahibi; Q2'yi kendisi oluşturdu
    private const int AdminUser = 9;

    private readonly TestDb _db = TestDb.Create();
    private readonly int _worksheet;
    private readonly int _copyWorksheet;
    private readonly int _legacyQuestion;   // CreateUserId=0, W + W2'de
    private readonly int _copierQuestion;   // CreateUserId=CopierTeacher

    public QuestionsControllerAuthorizationTests()
    {
        using var ctx = _db.NewContext();
        ctx.Teachers.AddRange(
            new Teacher { UserId = OwnerTeacher, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = CopierTeacher, AccountApprovedAt = DateTime.UtcNow });
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();

        var legacy = new Question { Text = "eski" };
        ctx.Questions.Add(legacy);
        ctx.SaveChanges(); // CreateUserId = 0 (eski davranış)

        ctx.SetCurrentUser(OwnerTeacher);
        var w = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(w);
        ctx.SaveChanges();

        ctx.SetCurrentUser(CopierTeacher);
        var w2 = new Worksheet { Name = "W2", Description = "", GradeId = grade.Id, SourceWorksheetId = w.Id };
        var own = new Question { Text = "kopyalayanın" };
        ctx.AddRange(w2, own);
        ctx.SaveChanges();

        ctx.TestQuestions.AddRange(
            new WorksheetQuestion { TestId = w.Id, QuestionId = legacy.Id, Order = 1 },
            new WorksheetQuestion { TestId = w2.Id, QuestionId = legacy.Id, Order = 1 });
        ctx.SaveChanges();

        (_worksheet, _copyWorksheet, _legacyQuestion, _copierQuestion) = (w.Id, w2.Id, legacy.Id, own.Id);
    }

    public void Dispose() => _db.Dispose();

    // ---------------- Pipeline ----------------

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Sub", out var sub))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, sub.ToString()) };
            foreach (var role in Request.Headers["X-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
            if (Request.Headers.TryGetValue("X-Azp", out var azp))
                claims.Add(new Claim("azp", azp.ToString()));
            var identity = new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class FakeProfiles : IUserProfileProvider
    {
        public Task<UserProfileDto> GetAsync(string keycloakId, CancellationToken ct = default) =>
            Task.FromResult(new UserProfileDto
            {
                Id = int.TryParse(keycloakId.TrimStart('u'), out var id) ? id : 0, KeycloakId = keycloakId, FullName = "U",
                Email = "", Role = ""
            });
    }

    /// <summary>Yalnızca QuestionsController'ı keşfeder (diğer controller'ların bağımlılıkları gerekmez).</summary>
    private sealed class OnlyQuestionsController : ControllerFeatureProvider
    {
        protected override bool IsController(System.Reflection.TypeInfo typeInfo) =>
            typeInfo.AsType() == typeof(QuestionsController);
    }

    private async Task<IHost> StartHostAsync()
    {
        var questionService = Substitute.For<IQuestionService>();
        questionService.CreateOrUpdateQuestion(Arg.Any<QuestionDto>(), Arg.Any<int>())
            .Returns(new QuestionSavedDto { Success = true });
        questionService.SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), Arg.Any<int>())
            .Returns(new ResponseBaseDto { Success = true });
        questionService.AttachImageToStudyPage(Arg.Any<StudyPageAttachImageDto>())
            .Returns(new StudyPageAttachImageResponseDto { Success = true });
        questionService.ResizeQuestionImage(Arg.Any<int>(), Arg.Any<double>())
            .Returns(new ResponseBaseDto { Success = true });

        var classification = Substitute.For<IQuestionClassificationService>();
        classification.UpdateCorrectAnswer(Arg.Any<int>(), Arg.Any<int>()).Returns(new ResponseBaseDto { Success = true });
        classification.UpdateQuestionClassification(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int[]?>(), Arg.Any<string?>(), Arg.Any<int?>())
            .Returns(new ResponseBaseDto { Success = true });
        classification.RemoveQuestionFromTest(Arg.Any<int>(), Arg.Any<int>()).Returns(new ResponseBaseDto { Success = true });

        var query = Substitute.For<IQuestionQueryService>();
        query.GetQuestionById(Arg.Any<int>()).Returns(new QuestionDto { Id = 1, ImageUrl = "q/question.jpg" });
        query.GetQuestionByTestId(Arg.Any<int>()).Returns(new List<QuestionDto>());
        query.GetLastTenPassages().Returns(new List<PassageDto>());

        var minio = Substitute.For<IMinIoService>();
        minio.GetFileStreamAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1 })));

        var classifierCache = Substitute.For<IClassifierCacheService>();
        classifierCache.GetActivePointerAsync(Arg.Any<CancellationToken>()).Returns((null, "m"));

        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                    services.AddLogging();
                    services.AddControllers()
                        .ConfigureApplicationPartManager(m =>
                        {
                            m.ApplicationParts.Add(new AssemblyPart(typeof(QuestionsController).Assembly));
                            m.FeatureProviders.Clear();
                            m.FeatureProviders.Add(new OnlyQuestionsController());
                        });
                    services.AddJsonLocalization(o =>
                    {
                        o.ResourcesPath = "Resources";
                        o.FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory);
                    });
                    services.Configure<RequestLocalizationOptions>(options =>
                    {
                        var cultures = SupportedLocales.AllCultureNames.Select(n => new CultureInfo(n)).ToList();
                        options.DefaultRequestCulture = new RequestCulture(SupportedLocales.DefaultCultureName);
                        options.SupportedCultures = cultures;
                        options.SupportedUICultures = cultures;
                    });
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", _ => { });
                    services.AddAuthorization(o => QuestionAccessPolicies.AddTo(o, serviceClients: null));
                    services.AddScoped(_ => _db.NewContext());
                    services.AddScoped<IApprovedTeacherGuard, ApprovedTeacherGuard>();
                    services.AddScoped<IQuestionOwnershipGuard, QuestionOwnershipGuard>();
                    services.AddSingleton<IUserProfileProvider, FakeProfiles>();
                    services.AddApprovedTeacherAuthorization();
                    services.AddSingleton(questionService);
                    services.AddSingleton(classification);
                    services.AddSingleton(query);
                    services.AddSingleton(minio);
                    services.AddSingleton(classifierCache);
                    services.AddSingleton<ImageHelper>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapControllers());
                });
            })
            .StartAsync();
    }

    private sealed record Caller(int UserId, string Roles, string? Azp = null);

    private static readonly Caller Student = new(50, "Student");
    private static readonly Caller Parent = new(51, "Parent");
    private static readonly Caller Owner = new(OwnerTeacher, "Teacher");
    private static readonly Caller Copier = new(CopierTeacher, "Teacher");
    private static readonly Caller Admin = new(AdminUser, "Admin");
    private static readonly Caller Service = new(0, "", Azp: "exam-admin");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, Caller caller, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Sub", caller.Azp != null ? "service-account-exam-admin" : $"u{caller.UserId}");
        request.Headers.Add("X-Roles", caller.Roles);
        if (caller.Azp != null)
            request.Headers.Add("X-Azp", caller.Azp);
        if (body != null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    /// <summary>Tüm uçlar; sahiplik gerektirenler Q1 (eski, W'nin sahibi Owner) / W üzerinden.</summary>
    private IEnumerable<(HttpMethod Method, string Path, object? Body)> AllEndpoints() => new (HttpMethod, string, object?)[]
    {
        (HttpMethod.Get, $"/api/questions/{_legacyQuestion}", null),
        (HttpMethod.Get, "/api/questions/passages", null),
        (HttpMethod.Get, $"/api/questions/bytest/{_worksheet}", null),
        (HttpMethod.Get, $"/api/questions/{_legacyQuestion}/image", null),
        (HttpMethod.Get, "/api/questions/classifier-cache", null),
        (HttpMethod.Post, "/api/questions", new { id = _legacyQuestion, text = "t", categoryName = "c", answers = Array.Empty<object>() }),
        (HttpMethod.Post, "/api/questions", new { id = 0, testId = _worksheet, text = "t", categoryName = "c", answers = Array.Empty<object>() }),
        (HttpMethod.Post, "/api/questions/save", new { imageData = "x", passages = Array.Empty<object>(), header = new { testId = _worksheet }, questions = Array.Empty<object>() }),
        (HttpMethod.Post, "/api/questions/attach-study-page", new { imageData = "x" }),
        (HttpMethod.Put, $"/api/questions/{_legacyQuestion}/correct-answer", new { correctAnswerId = 1, scale = 1 }),
        (HttpMethod.Put, $"/api/questions/{_legacyQuestion}/classification", new { subjectId = 1 }),
        (HttpMethod.Delete, $"/api/questions/test/{_worksheet}/question/{_legacyQuestion}", null),
    };

    [Fact]
    public async Task Student_and_parent_are_forbidden_on_every_endpoint()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        foreach (var caller in new[] { Student, Parent })
        foreach (var (method, path, body) in AllEndpoints())
            (await SendAsync(client, caller, method, path, body)).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden, $"{caller.Roles} {method} {path}");
    }

    [Fact]
    public async Task Admin_can_call_every_endpoint()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        foreach (var (method, path, body) in AllEndpoints())
            (await SendAsync(client, Admin, method, path, body)).StatusCode
                .ShouldBe(HttpStatusCode.OK, $"{method} {path}");
    }

    [Fact]
    public async Task Owner_teacher_can_modify_own_question_and_test()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        foreach (var (method, path, body) in AllEndpoints().Where(e => !e.Path.EndsWith("classifier-cache")))
            (await SendAsync(client, Owner, method, path, body)).StatusCode
                .ShouldBe(HttpStatusCode.OK, $"{method} {path}");
    }

    [Fact]
    public async Task Classifier_cache_pointer_is_admin_or_service_only()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await SendAsync(client, Owner, HttpMethod.Get, "/api/questions/classifier-cache")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(client, Service, HttpMethod.Get, "/api/questions/classifier-cache")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Non_owner_teacher_gets_403_on_every_write_including_the_copier_of_a_shared_question()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        var writes = new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, "/api/questions", new { id = _legacyQuestion, text = "t", categoryName = "c", answers = Array.Empty<object>() }),
            (HttpMethod.Post, "/api/questions", new { id = 0, testId = _worksheet, text = "t", categoryName = "c", answers = Array.Empty<object>() }),
            (HttpMethod.Post, "/api/questions/save", new { imageData = "x", passages = Array.Empty<object>(), header = new { testId = _worksheet }, questions = Array.Empty<object>() }),
            (HttpMethod.Put, $"/api/questions/{_legacyQuestion}/correct-answer", new { correctAnswerId = 1, scale = 1 }),
            (HttpMethod.Put, $"/api/questions/{_legacyQuestion}/classification", new { subjectId = 1 }),
            (HttpMethod.Delete, $"/api/questions/test/{_worksheet}/question/{_legacyQuestion}", null),
            // Owner, Copier'ın kendi sorusunu da değiştiremez.
        };
        foreach (var (method, path, body) in writes)
        {
            var response = await SendAsync(client, Copier, method, path, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
            (await response.Content.ReadAsStringAsync()).ShouldContain("\"success\":false");
        }

        (await SendAsync(client, Owner, HttpMethod.Put, $"/api/questions/{_copierQuestion}/correct-answer",
            new { correctAnswerId = 1, scale = 1 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stamped_creator_owns_the_question_and_copier_owns_the_copy_worksheet()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await SendAsync(client, Copier, HttpMethod.Put, $"/api/questions/{_copierQuestion}/correct-answer",
            new { correctAnswerId = 1, scale = 1 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Kopya testin sahibi, kendi testinden soru çıkarabilir (soru satırı değişmez, yalnızca üyelik).
        (await SendAsync(client, Copier, HttpMethod.Delete, $"/api/questions/test/{_copyWorksheet}/question/{_legacyQuestion}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Service_account_can_read_image_and_classify_any_question_but_not_author()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await SendAsync(client, Service, HttpMethod.Get, $"/api/questions/{_copierQuestion}/image")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(client, Service, HttpMethod.Put, $"/api/questions/{_copierQuestion}/classification",
            new { subjectId = 1 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(client, Service, HttpMethod.Put, $"/api/questions/{_copierQuestion}/correct-answer",
            new { correctAnswerId = 1, scale = 1 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_question_or_test_is_404_for_a_teacher()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await SendAsync(client, Owner, HttpMethod.Put, "/api/questions/987654/correct-answer",
            new { correctAnswerId = 1, scale = 1 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(client, Owner, HttpMethod.Delete, $"/api/questions/test/987654/question/{_legacyQuestion}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pending_teacher_is_denied_even_on_own_resources()
    {
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.UserId == OwnerTeacher)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountApprovedAt, (DateTime?)null));
        }
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await SendAsync(client, Owner, HttpMethod.Put, $"/api/questions/{_legacyQuestion}/correct-answer",
            new { correctAnswerId = 1, scale = 1 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
