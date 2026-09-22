using ExamApp.Api.Services.Schools.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Tests.Services;

/// <summary>seed-schools komut çalıştırıcısı (issue #216): çıkış kodları ve konsol çıktısı.</summary>
[Collection("Console")] // Console.Out/Error yönlendirmesi süreç geneli — paralel çalışmasın
public class SchoolSeedCommandRunTests
{
    private static (IServiceProvider Services, ISchoolSeedService Service) Host()
    {
        var service = Substitute.For<ISchoolSeedService>();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(service)
            .BuildServiceProvider();
        return (services, service);
    }

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static SchoolSeedCommand Cmd(params string[] extra) => SchoolSeedCommand.Parse(["seed-schools", .. extra]);

    private static async Task<(int Exit, string Out, string Err)> RunCapturedAsync(
        IServiceProvider services, IHostEnvironment env, SchoolSeedCommand cmd)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var exit = await SchoolSeedCommand.RunAsync(services, env, cmd);
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

        exit.ShouldBe(SchoolSeedCommand.ExitEnvironmentRefused);
        err.ShouldContain("Production");
        await service.DidNotReceive().RunAsync(Arg.Any<SchoolSeedOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Help_prints_usage_and_returns_0()
    {
        var (services, service) = Host();

        var (exit, output, _) = await RunCapturedAsync(services, Env("Development"), Cmd("--help"));

        exit.ShouldBe(SchoolSeedCommand.ExitOk);
        output.ShouldContain("seed-schools");
        output.ShouldContain("--no-migrate");
        await service.DidNotReceive().RunAsync(Arg.Any<SchoolSeedOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Source_failure_returns_exit_3_with_message()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<SchoolSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns<SchoolSeedResult>(_ => throw new SchoolSeedSourceException("Okul listesi indirilemedi: HTTP 404"));

        var (exit, _, err) = await RunCapturedAsync(services, Env("Development"), Cmd());

        exit.ShouldBe(SchoolSeedCommand.ExitFailed);
        err.ShouldContain("HTTP 404");
    }

    [Fact]
    public async Task Db_failure_returns_exit_3_with_inner_message()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<SchoolSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns<SchoolSeedResult>(_ => throw new DbUpdateException("save failed", new InvalidOperationException("duplicate key IX_Schools_ExternalCode")));

        var (exit, _, err) = await RunCapturedAsync(services, Env("Staging"), Cmd());

        exit.ShouldBe(SchoolSeedCommand.ExitFailed);
        err.ShouldContain("IX_Schools_ExternalCode");
    }

    [Fact]
    public async Task Success_prints_summary_table_and_returns_0()
    {
        var (services, service) = Host();
        service.RunAsync(Arg.Any<SchoolSeedOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SchoolSeedResult
            {
                DryRun = true,
                Source = "fixture",
                SourceSha256 = "abc",
                TotalRowsInSource = 3,
                CandidateRows = 2,
                Added = 1,
                SkippedSoftDeleted = 1,
                Provinces = [new SchoolSeedProvinceSummary { Province = "Kars", ProvinceMatched = true, IlkokulAdded = 1, SkippedSoftDeleted = 1 }],
                UnmatchedDistricts = [new SchoolSeedUnmatchedDistrict { Province = "Kars", District = "Hayalilçe", Rows = 2 }],
                AddedSample = [new SchoolSeedAddedSchool { ExternalCode = "1", Name = "Test İlkokulu", Province = "Kars", District = "Merkez" }]
            });

        var (exit, output, _) = await RunCapturedAsync(services, Env("Development"), Cmd("--provinces", "Kars", "--dry-run"));

        exit.ShouldBe(SchoolSeedCommand.ExitOk);
        output.ShouldContain("DRY-RUN");
        output.ShouldContain("Kars");
        output.ShouldContain("softDeleted=1");
        output.ShouldContain("EŞLEŞMEYEN İLÇE: Kars/Hayalilçe (2 satır)");
        output.ShouldContain("[1] Test İlkokulu — Kars/Merkez");
        await service.Received(1).RunAsync(Arg.Is<SchoolSeedOptions>(o => o.DryRun && o.Provinces.Single() == "Kars"), Arg.Any<CancellationToken>());
    }
}
