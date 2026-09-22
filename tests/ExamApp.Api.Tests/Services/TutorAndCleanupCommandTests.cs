using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Services.Seed;
using ExamApp.Api.Services.Seed.Cleanup;
using ExamApp.Api.Services.Teachers.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Tests.Services;

/// <summary>seed-tutors ve seed-cleanup (issue #218): komut satırı çözümleme, dispatcher, çıkış kodları, konsol çıktısı, Production reddi.</summary>
[Collection("Console")]
public class TutorAndCleanupCommandTests
{
    private static TutorSeedCommand Tutors(params string[] extra) => TutorSeedCommand.Parse(["seed-tutors", .. extra]);
    private static SeedCleanupCommand Cleanup(params string[] extra) => SeedCleanupCommand.Parse(["seed-cleanup", .. extra]);

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static async Task<(int Exit, string Out, string Err)> RunCapturedAsync(IServiceProvider services, IHostEnvironment env, ISeedCommand cmd)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var exit = await cmd.RunAsync(services, env);
            return (exit, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    // ---- seed-tutors parse ----

    [Fact]
    public void Tutors_defaults()
    {
        var cmd = Tutors();
        cmd.Options.Provinces.ShouldBe(SchoolSeedOptions.DefaultProvinces);
        cmd.Options.LimitSchoolsPerProvince.ShouldBeNull();
        cmd.Options.PendingRatio.ShouldBeNull(); // ortam varsayılanı serviste çözülür (Dev 0, Staging 1)
        cmd.Options.DryRun.ShouldBeFalse();
        cmd.Options.EmitEvents.ShouldBeTrue();
        cmd.Options.KeycloakMode.ShouldBe(TeacherSeedOptions.KeycloakModeAdminApi);
        cmd.CommandName.ShouldBe("seed-tutors");
    }

    [Fact]
    public void Tutors_all_options_parse()
    {
        var cmd = Tutors("--provinces", "Kars,Antalya", "--limit-schools-per-province", "3", "--pending-ratio", "0.25", "--dry-run",
            "--no-events", "--keycloak-mode", "partial-import", "--batch-size", "50", "--no-migrate", "--connection", "Host=x");
        cmd.Options.Provinces.ShouldBe(["Kars", "Antalya"]);
        cmd.Options.LimitSchoolsPerProvince.ShouldBe(3);
        cmd.Options.PendingRatio.ShouldBe(0.25);
        cmd.Options.DryRun.ShouldBeTrue();
        cmd.Options.EmitEvents.ShouldBeFalse();
        cmd.Options.KeycloakMode.ShouldBe(TeacherSeedOptions.KeycloakModePartialImport);
        cmd.Options.BatchSize.ShouldBe(50);
        cmd.NoMigrate.ShouldBeTrue();
        cmd.ConnectionString.ShouldBe("Host=x");
    }

    [Theory]
    [InlineData("--pending-ratio", "1.5")]
    [InlineData("--pending-ratio", "-0.1")]
    [InlineData("--pending-ratio", "abc")]
    [InlineData("--limit", "0")]
    [InlineData("--batch-size", "501")]
    [InlineData("--bogus")]
    public void Tutors_invalid_usage_throws(params string[] extra) => Should.Throw<ArgumentException>(() => Tutors(extra));

    // ---- seed-cleanup parse ----

    [Fact]
    public void Cleanup_default_is_dry_run_without_force()
    {
        var cmd = Cleanup();
        cmd.Options.Apply.ShouldBeFalse();
        cmd.Options.Force.ShouldBeFalse();
        cmd.Options.SkipAuthApi.ShouldBeFalse();
        cmd.Options.Yes.ShouldBeFalse();
        cmd.CommandName.ShouldBe("seed-cleanup");
        Cleanup("--dry-run").Options.Apply.ShouldBeFalse();
    }

    [Fact]
    public void Cleanup_options_parse()
    {
        var cmd = Cleanup("--apply", "--yes", "--force", "--skip-auth-api", "--no-migrate");
        cmd.Options.Apply.ShouldBeTrue();
        cmd.Options.Yes.ShouldBeTrue();
        cmd.Options.Force.ShouldBeTrue();
        cmd.Options.SkipAuthApi.ShouldBeTrue();
        cmd.NoMigrate.ShouldBeTrue();
        Cleanup("--connection", "Host=y").ConnectionString.ShouldBe("Host=y"); // dry-run ile serbest
        Should.Throw<ArgumentException>(() => Cleanup("--delete-everything"));
        Should.Throw<ArgumentException>(() => Cleanup("--connection"));
    }

    [Fact]
    public void Cleanup_apply_with_connection_or_dry_run_is_a_usage_error()
    {
        var ex = Should.Throw<ArgumentException>(() => Cleanup("--apply", "--connection", "Host=prod"));
        ex.Message.ShouldContain("ConnectionStrings__DefaultConnection");
        ex.Message.ShouldContain("--yes");
        Should.Throw<ArgumentException>(() => Cleanup("--connection", "Host=prod", "--apply", "--yes"));
        Should.Throw<ArgumentException>(() => Cleanup("--apply", "--dry-run"));
        Should.Throw<ArgumentException>(() => Cleanup("--dry-run", "--apply"));
        // Dispatcher da aynı hatayı yüzeye çıkarır → Program.cs exit 1 (ExitUsage)
        Should.Throw<ArgumentException>(() => SeedCommands.TryParse(["seed-cleanup", "--apply", "--connection", "Host=prod"]));
    }

    [Fact]
    public void Dispatcher_routes_all_four_commands()
    {
        SeedCommands.TryParse(["seed-tutors", "--dry-run"]).ShouldBeOfType<TutorSeedCommand>();
        SeedCommands.TryParse(["seed-cleanup"]).ShouldBeOfType<SeedCleanupCommand>();
        SeedCommands.TryParse(["seed-teachers"]).ShouldBeOfType<TeacherSeedCommand>();
        SeedCommands.TryParse(["seed-schools"]).ShouldBeOfType<SchoolSeedCommand>();
        SeedCommands.TryParse(["seed-report"]).ShouldBeNull();
    }

    [Fact]
    public void Usages_never_mention_a_password_value()
    {
        TutorSeedCommand.Usage.ShouldContain(TeacherSeedService.PasswordConfigKey);
        TutorSeedCommand.Usage.ShouldNotContain("--password");
        TutorSeedCommand.Usage.ShouldContain("Staging 1");
        SeedCleanupCommand.Usage.ShouldNotContain("--password");
        SeedCleanupCommand.Usage.ShouldContain("--apply");
        SeedCleanupCommand.Usage.ShouldContain("--dry-run");
        SeedCleanupCommand.Usage.ShouldContain("--yes");
        SeedCleanupCommand.Usage.ShouldContain("2 ortam reddi");
        SeedCleanupCommand.Usage.ShouldContain("Booking");
    }

    // ---- run: Production reddi ----

    [Fact]
    public async Task Production_returns_exit_2_for_both_commands_without_calling_services()
    {
        var tutorService = Substitute.For<ITutorSeedService>();
        var cleanupService = Substitute.For<ISeedCleanupService>();
        var services = new ServiceCollection().AddLogging().AddSingleton(tutorService).AddSingleton(cleanupService).BuildServiceProvider();

        var (exit1, _, err1) = await RunCapturedAsync(services, Env("Production"), Tutors("--dry-run"));
        var (exit2, _, err2) = await RunCapturedAsync(services, Env("Production"), Cleanup("--apply", "--force"));

        exit1.ShouldBe(SeedCommands.ExitEnvironmentRefused);
        exit2.ShouldBe(SeedCommands.ExitEnvironmentRefused);
        err1.ShouldContain("Production");
        err2.ShouldContain("Production");
        await tutorService.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await cleanupService.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task Cleanup_apply_on_staging_without_yes_returns_exit_1_and_never_calls_service()
    {
        var service = Substitute.For<ISeedCleanupService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db.staging;Port=5433;Database=worksheet;Username=u;Password=secret-example",
            ["AuthApiBaseUrl"] = "http://auth-api.staging:5079"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton(service).AddSingleton<IConfiguration>(config).BuildServiceProvider();

        var (exit, output, err) = await RunCapturedAsync(services, Env("Staging"), Cleanup("--apply"));

        exit.ShouldBe(SeedCommands.ExitUsage);
        err.ShouldContain("--yes");
        output.ShouldContain("Ortam=Staging DB=db.staging:5433/worksheet auth-api=http://auth-api.staging:5079");
        output.ShouldNotContain("secret-example");
        await service.DidNotReceiveWithAnyArgs().RunAsync(default!, default);

        // --yes ile geçer
        service.RunAsync(Arg.Any<SeedCleanupOptions>(), Arg.Any<CancellationToken>()).Returns(new SeedCleanupResult { Applied = true });
        var (exit2, _, _) = await RunCapturedAsync(services, Env("Staging"), Cleanup("--apply", "--yes"));
        exit2.ShouldBe(SeedCommands.ExitOk);
        await service.Received(1).RunAsync(Arg.Is<SeedCleanupOptions>(o => o.Apply && o.Yes), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cleanup_apply_on_development_prints_target_banner_and_does_not_need_yes()
    {
        var service = Substitute.For<ISeedCleanupService>();
        service.RunAsync(Arg.Any<SeedCleanupOptions>(), Arg.Any<CancellationToken>()).Returns(new SeedCleanupResult { Applied = true });
        var services = new ServiceCollection().AddLogging().AddSingleton(service).BuildServiceProvider(); // IConfiguration yok → "?"

        var (exit, output, _) = await RunCapturedAsync(services, Env("Development"), Cleanup("--apply"));

        exit.ShouldBe(SeedCommands.ExitOk);
        output.ShouldContain("seed-cleanup --apply: Ortam=Development DB=? auth-api=?");
    }

    [Fact]
    public void DescribeDatabase_never_leaks_password()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=63756;Database=worksheet;Username=examuser;Password='p{w}d-example'"
        }).Build();
        SeedCleanupCommand.DescribeDatabase(config).ShouldBe("127.0.0.1:63756/worksheet");
        SeedCleanupCommand.DescribeDatabase(null).ShouldBe("?");
    }

    // ---- run: çıktı ----

    [Fact]
    public async Task Tutors_success_prints_groups_and_accounts()
    {
        var service = Substitute.For<ITutorSeedService>();
        service.RunAsync(Arg.Any<TutorSeedOptions>(), Arg.Any<CancellationToken>()).Returns(new TutorSeedResult
        {
            KeycloakMode = "admin-api", SchoolsCounted = 3, SchoolTeachersCounted = 20, Planned = 8, PlannedPending = 2, TutorsCreated = 8,
            Groups = [new TutorSeedGroupSummary { Province = "Kars", Branch = TeacherSeedBranch.Matematik, SubjectName = "Matematik", SchoolTeachers = 5, Planned = 2, Created = 2 }],
            Accounts = [new TutorSeedAccount { Email = "seed.i.kars.matematik.1@seed.examapp.local", FullName = "Ayşe Kaya", Province = "Kars", Branch = TeacherSeedBranch.Matematik, HourlyRate = 400, TeachesInPerson = true, Status = "Created" }]
        });
        var services = new ServiceCollection().AddLogging().AddSingleton(service).BuildServiceProvider();

        var (exit, output, _) = await RunCapturedAsync(services, Env("Development"), Tutors("--provinces", "Kars", "--pending-ratio", "0.25"));

        exit.ShouldBe(SeedCommands.ExitOk);
        output.ShouldContain("== seed-tutors ==");
        output.ShouldContain("tutorYeni=8");
        output.ShouldContain("seed.i.kars.matematik.1@seed.examapp.local");
        output.ShouldContain("online+yüzyüze");
        await service.Received(1).RunAsync(Arg.Is<TutorSeedOptions>(o => o.PendingRatio == 0.25 && o.Provinces.Single() == "Kars"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cleanup_dry_run_prints_report_and_hint_and_exit_0()
    {
        var service = Substitute.For<ISeedCleanupService>();
        service.RunAsync(Arg.Any<SeedCleanupOptions>(), Arg.Any<CancellationToken>()).Returns(new SeedCleanupResult
        {
            SeedSchools = 3, SeedSchoolTeachers = 20, SeedTutors = 8, TeachersDeleted = 27, TeachersSkippedScheduling = 1, TeachersSkippedRealStudentBooking = 2, RealStudentBookings = 5, SchoolsDeleted = 2, SchoolsSkippedStudent = 1,
            AuthApiCalled = true, AuthPlanned = 27, IdentityExcluded = 1,
            SchoolGroups = [new SeedCleanupSchoolGroup { Province = "Kars", Kind = "Ortaokul", Count = 3 }],
            TeacherGroups = [new SeedCleanupTeacherGroup { Province = "Kars", Subject = "Matematik", SchoolTeachers = 5, Tutors = 2 }],
            Skipped = ["Teacher #9 (user 1004, X Ortaokulu): randevu/müsaitlik=2 worksheet/soru=0 — atlandı (--force ile randevu verisi silinir)"]
        });
        var services = new ServiceCollection().AddLogging().AddSingleton(service).BuildServiceProvider();

        var (exit, output, _) = await RunCapturedAsync(services, Env("Development"), Cleanup());

        exit.ShouldBe(SeedCommands.ExitOk);
        output.ShouldContain("DRY-RUN");
        output.ShouldContain("Bağımsız öğretmen: 8");
        output.ShouldContain("Teacher silinecek=27 atlandı(gerçek öğrenci randevusu)=2 [randevu 5]");
        output.ShouldContain("silinecek=27 korunan(exam'de atlanan)=1");
        output.ShouldContain("seed-cleanup --apply");
        output.ShouldContain("Teacher #9");
        await service.Received(1).RunAsync(Arg.Is<SeedCleanupOptions>(o => !o.Apply && !o.Force), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cleanup_apply_with_auth_error_returns_exit_3()
    {
        var service = Substitute.For<ISeedCleanupService>();
        service.RunAsync(Arg.Any<SeedCleanupOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SeedCleanupResult { Applied = true, TeachersDeleted = 5, AuthApiError = "auth-api'ye ulaşılamadı", Errors = ["auth-api: auth-api'ye ulaşılamadı"] });
        var services = new ServiceCollection().AddLogging().AddSingleton(service).BuildServiceProvider();

        var (exit, output, err) = await RunCapturedAsync(services, Env("Development"), Cleanup("--apply"));

        exit.ShouldBe(SeedCommands.ExitFailed);
        output.ShouldContain("Teacher silindi=5");
        output.ShouldContain("ulaşılamadı");
        err.ShouldContain("Tekrar koşu");
        await service.Received(1).RunAsync(Arg.Is<SeedCleanupOptions>(o => o.Apply && !o.Force), Arg.Any<CancellationToken>());
    }
}
