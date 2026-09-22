using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Services.Seed;
using ExamApp.Api.Services.Teachers.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Tests.Services;

/// <summary>seed-teachers (issue #217): komut satırı çözümleme, dispatcher, çıkış kodları ve konsol çıktısı.</summary>
[Collection("Console")] // Console.Out/Error yönlendirmesi süreç geneli — paralel çalışmasın
public class TeacherSeedCommandTests
{
    private static TeacherSeedCommand Cmd(params string[] extra) => TeacherSeedCommand.Parse(["seed-teachers", .. extra]);

    // ---- parse ----

    [Fact]
    public void Defaults_are_full_scope_admin_api_events_on()
    {
        var cmd = Cmd();

        cmd.Options.Provinces.ShouldBe(SchoolSeedOptions.DefaultProvinces);
        cmd.Options.LimitSchoolsPerProvince.ShouldBeNull();
        cmd.Options.DryRun.ShouldBeFalse();
        cmd.Options.EmitEvents.ShouldBeTrue();
        cmd.Options.KeycloakMode.ShouldBe(TeacherSeedOptions.KeycloakModeAdminApi);
        cmd.Options.BatchSize.ShouldBe(TeacherSeedOptions.DefaultBatchSize);
        cmd.ConnectionString.ShouldBeNull();
        cmd.NoMigrate.ShouldBeFalse();
        cmd.ShowHelp.ShouldBeFalse();
        cmd.CommandName.ShouldBe("seed-teachers");
    }

    [Fact]
    public void All_options_parse()
    {
        var cmd = Cmd("--provinces", "Kars, Antalya", "--limit-schools-per-province", "2", "--dry-run", "--no-events",
            "--keycloak-mode", "partial-import", "--batch-size", "250", "--reset-password", "--adopt-unmarked", "--no-migrate", "--connection", "Host=x;Password='a{b}c'");

        cmd.Options.Provinces.ShouldBe(["Kars", "Antalya"]);
        cmd.Options.LimitSchoolsPerProvince.ShouldBe(2);
        cmd.Options.DryRun.ShouldBeTrue();
        cmd.Options.EmitEvents.ShouldBeFalse();
        cmd.Options.KeycloakMode.ShouldBe(TeacherSeedOptions.KeycloakModePartialImport);
        cmd.Options.BatchSize.ShouldBe(250);
        cmd.Options.ResetPassword.ShouldBeTrue();
        cmd.Options.AdoptUnmarked.ShouldBeTrue();
        cmd.NoMigrate.ShouldBeTrue();
        cmd.ConnectionString.ShouldBe("Host=x;Password='a{b}c'");
        Cmd().Options.ResetPassword.ShouldBeFalse(); // varsayılan: mevcut parolaya dokunma
        Cmd().Options.AdoptUnmarked.ShouldBeFalse(); // varsayılan: işaretsiz yetim dokunulmaz
    }

    [Fact]
    public void Format_warns_about_unreset_passwords_only_when_existing_or_adopted_and_flag_off()
    {
        var withExisting = new TeacherSeedResult { KeycloakExisting = 20, KeycloakAdopted = 5, ResetPassword = false };
        TeacherSeedCommand.Format(withExisting, new TeacherSeedOptions()).ShouldContain("25 hesabın parolası sıfırlanmadı");
        TeacherSeedCommand.Format(withExisting, new TeacherSeedOptions()).ShouldContain("--reset-password");

        var reset = new TeacherSeedResult { KeycloakExisting = 20, KeycloakAdopted = 5, ResetPassword = true, PasswordsReset = 25 };
        var text = TeacherSeedCommand.Format(reset, new TeacherSeedOptions { ResetPassword = true });
        text.ShouldNotContain("sıfırlanmadı");
        text.ShouldContain("adopt=5");
        text.ShouldContain("parolaSıfırlandı=25");

        TeacherSeedCommand.Format(new TeacherSeedResult { KeycloakCreated = 10 }, new TeacherSeedOptions()).ShouldNotContain("sıfırlanmadı");
    }

    [Theory]
    [InlineData("--limit-schools-per-province", "0")]
    [InlineData("--limit-schools-per-province", "abc")]
    [InlineData("--batch-size", "501")]
    [InlineData("--keycloak-mode", "ldap")]
    [InlineData("--bogus")]
    public void Invalid_usage_throws(params string[] extra)
    {
        Should.Throw<ArgumentException>(() => Cmd(extra));
    }

    [Fact]
    public void Option_without_value_throws_with_usage()
    {
        var ex = Should.Throw<ArgumentException>(() => Cmd("--provinces"));
        ex.Message.ShouldContain("--provinces bir değer bekliyor");
        ex.Message.ShouldContain("Kullanım:");
    }

    [Fact]
    public void Dispatcher_routes_both_commands_and_ignores_others()
    {
        SeedCommands.TryParse(["seed-teachers", "--dry-run"]).ShouldBeOfType<TeacherSeedCommand>();
        SeedCommands.TryParse(["seed-schools", "--dry-run"]).ShouldBeOfType<SchoolSeedCommand>();
        SeedCommands.TryParse([]).ShouldBeNull();
        SeedCommands.TryParse(["--urls", "http://x"]).ShouldBeNull();
        Should.Throw<ArgumentException>(() => SeedCommands.TryParse(["seed-teachers", "--nope"]));
    }

    [Fact]
    public void Usage_never_mentions_a_password_value_only_the_config_key()
    {
        TeacherSeedCommand.Usage.ShouldContain(TeacherSeedService.PasswordConfigKey);
        TeacherSeedCommand.Usage.ShouldContain("SeedData__Password");
        TeacherSeedCommand.Usage.ShouldNotContain("--password");
    }

    // ---- run ----

    private static (IServiceProvider Services, ITeacherSeedService Service) Host()
    {
        var service = Substitute.For<ITeacherSeedService>();
        var services = new ServiceCollection().AddLogging().AddSingleton(service).BuildServiceProvider();
        return (services, service);
    }

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

    [Fact]
    public async Task Production_returns_exit_2_without_calling_service()
    {
        var (services, service) = Host();

        var (exit, _, err) = await RunCapturedAsync(services, Env("Production"), Cmd("--dry-run"));

        exit.ShouldBe(SeedCommands.ExitEnvironmentRefused);
        err.ShouldContain("Production");
        await service.DidNotReceive().RunAsync(Arg.Any<TeacherSeedOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auth_api_failure_returns_exit_3_with_message()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<TeacherSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns<TeacherSeedResult>(_ => throw new TeacherSeedAuthApiException("auth-api dev ucu bulunamadı (404)"));

        var (exit, _, err) = await RunCapturedAsync(services, Env("Development"), Cmd());

        exit.ShouldBe(SeedCommands.ExitFailed);
        err.ShouldContain("404");
    }

    [Fact]
    public async Task Missing_password_returns_exit_3_with_instructions()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<TeacherSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns<TeacherSeedResult>(_ => throw new InvalidOperationException("'SeedData:Password' yapılandırılmamış."));

        var (exit, _, err) = await RunCapturedAsync(services, Env("Staging"), Cmd());

        exit.ShouldBe(SeedCommands.ExitFailed);
        err.ShouldContain("SeedData:Password");
    }

    [Fact]
    public async Task Success_prints_summary_and_account_list_and_returns_0()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<TeacherSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns(new TeacherSeedResult
            {
                KeycloakMode = "admin-api",
                SchoolsSelected = 1, SchoolsOrtaokul = 1, Planned = 10, TeachersCreated = 9, TeachersExisting = 1,
                KeycloakCreated = 9, KeycloakExisting = 1, IdentityCreated = 9, IdentityExisting = 1,
                KeycloakElapsedMs = 1200, IdentityDbElapsedMs = 30, ExamDbElapsedMs = 20, TotalElapsedMs = 1300, Batches = 1,
                Provinces = [new TeacherSeedProvinceSummary { Province = "Kars", ProvinceMatched = true, SchoolsOrtaokul = 1, Planned = 10, Created = 9, Existing = 1 }],
                Branches = [new TeacherSeedBranchSummary { Branch = TeacherSeedBranch.Matematik, SubjectName = "Matematik", Planned = 2, Created = 2 }],
                Accounts = [new TeacherSeedAccount { Email = "seed.t.1.matematik.1@seed.examapp.local", FullName = "Ayşe Kaya", School = "X Ortaokulu", SchoolId = 7, Province = "Kars", Branch = TeacherSeedBranch.Matematik, Status = "Created" }]
            });

        var (exit, output, _) = await RunCapturedAsync(services, Env("Development"), Cmd("--provinces", "Kars", "--limit-schools-per-province", "1"));

        exit.ShouldBe(SeedCommands.ExitOk);
        output.ShouldContain("== seed-teachers ==");
        output.ShouldContain("teacherYeni=9 teacherMevcut=1");
        output.ShouldContain("keycloak=1200 ms");
        output.ShouldContain("seed.t.1.matematik.1@seed.examapp.local");
        output.ShouldContain("Ayşe Kaya");
        output.ShouldContain("X Ortaokulu (#7, Kars)");
        await service.Received(1).RunAsync(
            Arg.Is<TeacherSeedOptions>(o => o.LimitSchoolsPerProvince == 1 && o.Provinces.Single() == "Kars"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Partial_failure_returns_exit_3_with_hint()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<TeacherSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns(new TeacherSeedResult { Planned = 10, TeachersCreated = 9, Failed = 1, Errors = ["seed.t.1.din.1@seed.examapp.local: boom"] });

        var (exit, output, err) = await RunCapturedAsync(services, Env("Development"), Cmd());

        exit.ShouldBe(SeedCommands.ExitFailed);
        output.ShouldContain("teacherYeni=9");
        err.ShouldContain("1 hesap başarısız");
    }

    [Fact]
    public async Task Timeout_not_caused_by_user_returns_exit_3_with_batch_hint()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<TeacherSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns<TeacherSeedResult>(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var (exit, _, err) = await RunCapturedAsync(services, Env("Development"), Cmd());

        exit.ShouldBe(SeedCommands.ExitFailed);
        err.ShouldContain("zaman aşımı");
        err.ShouldContain("--batch-size");
    }

    [Fact]
    public void Usage_documents_env_var_and_user_secrets_but_not_dotenv()
    {
        TeacherSeedCommand.Usage.ShouldContain("SeedData__Password=");
        TeacherSeedCommand.Usage.ShouldContain("dotnet user-secrets set");
        TeacherSeedCommand.Usage.ShouldContain("OKUNMAZ");
    }

    [Fact]
    public async Task All_failed_returns_exit_3()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<TeacherSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns(new TeacherSeedResult { Planned = 5, Failed = 5, Errors = ["a@x: boom"] });

        var (exit, output, _) = await RunCapturedAsync(services, Env("Development"), Cmd());

        exit.ShouldBe(SeedCommands.ExitFailed);
        output.ShouldContain("a@x: boom");
    }
}
