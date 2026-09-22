using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.Teachers.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Seed.Cleanup;

/// <summary>
/// <c>dotnet run -- seed-cleanup [--apply] [--force]</c> (issue #218). Varsayılan dry-run = özet rapor
/// (ayrı <c>seed-report</c> komutu yok: aynı envanter, aynı çıktı). Aynı guard katmanları (#216/#217).
/// </summary>
public sealed record SeedCleanupCommand(SeedCleanupOptions Options, string? ConnectionString, bool ShowHelp, bool NoMigrate = false)
    : ISeedCommand
{
    public const string Name = "seed-cleanup";

    public string CommandName => Name;
    public string UsageText => Usage;

    public static string Usage => """
        Kullanım: dotnet run -- seed-cleanup [seçenekler]

          --dry-run            (varsayılan) Hiçbir şey silinmez; seed envanteri (okul il/tür, okul öğretmeni ve bağımsız
                               öğretmen il/branş) ve silinecek/atlanacak kayıtlar raporlanır (= özet rapor)
          --apply              Gerçekten sil (hard delete): exam Teacher (IsSeedData) + TeacherSubject, exam School
                               (IsSeedData; bağlı seed-dışı öğretmen/öğrenci/atama yoksa), identity User (IsSeedData),
                               Keycloak kullanıcıları (seed.*@seed.examapp.local) — auth-api dev ucu üzerinden.
                               --connection ile BİRLİKTE KULLANILAMAZ (farklı hedef için ConnectionStrings__DefaultConnection
                               ortam değişkeni + --yes); --dry-run ile çelişir.
          --yes                Staging'de --apply için zorunlu onay
          --force              Müsaitlik verisi (TeacherAvailabilitySlot, RecurringAvailabilityRule) ya da yazdığı
                               worksheet/soru olan seed öğretmenleri atlamak yerine müsaitlik satırlarıyla birlikte sil.
                               Worksheet/soru hiçbir zaman silinmez (sahipsiz kalır, raporlanır). Gerçek öğrenci randevusu
                               (Booking) olan öğretmen --force ile de silinmez.
          --skip-auth-api      auth-api'yi çağırma (yalnızca exam DB)
          --connection <str>   ConnectionStrings:DefaultConnection yerine kullanılacak bağlantı (yalnızca dry-run)
          --no-migrate         Bekleyen migration'ları ve referans seed'ini atla
          --help               Bu metin

        Yalnızca Development/Staging. Seed dışı hiçbir satıra dokunmaz: Teacher/School/User yalnızca IsSeedData=true,
        Keycloak yalnızca seed.*@seed.examapp.local VE identity'de seed kaydı olanlar; gerçek öğrenci randevuları ve
        worksheet/sorular hiçbir modda silinmez. Sıra: exam öğretmen → exam okul → auth-api (Keycloak → identity).
        Kısmi hatada tekrar koşu kalanı temizler. Çıkış: 0 tamam, 1 kullanım, 2 ortam reddi, 3 hata/kısmi.
        """;

    public static bool IsRequested(string[] args)
        => args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    public static SeedCleanupCommand Parse(string[] args)
    {
        if (!IsRequested(args))
            throw new ArgumentException($"İlk argüman '{Name}' olmalı.");

        var apply = false;
        var dryRun = false;
        var yes = false;
        var force = false;
        var skipAuth = false;
        string? connection = null;
        var help = false;
        var noMigrate = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--apply": apply = true; break;
                case "--dry-run": dryRun = true; break;
                case "--yes": yes = true; break;
                case "--force": force = true; break;
                case "--skip-auth-api": skipAuth = true; break;
                case "--no-migrate": noMigrate = true; break;
                case "--connection":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"{arg} bir değer bekliyor.\n{Usage}");
                    connection = args[++i];
                    break;
                case "--help":
                case "-h":
                case "-?":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"Bilinmeyen seçenek: '{arg}'.\n{Usage}");
            }
        }

        if (apply && dryRun)
            throw new ArgumentException("--apply ve --dry-run birlikte verilemez.");
        if (apply && connection is not null)
        {
            throw new ArgumentException(
                "Yıkıcı komut (--apply) yalnızca varsayılan/yerel bağlantıyla çalışır; --connection kabul edilmez. " +
                "Farklı bir hedef için ConnectionStrings__DefaultConnection ortam değişkenini verin ve --yes ekleyin.");
        }

        return new SeedCleanupCommand(new SeedCleanupOptions { Apply = apply, Force = force, SkipAuthApi = skipAuth, Yes = yes }, connection, help, noMigrate);
    }

    public Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment) => RunAsync(services, environment, this);

    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, SeedCleanupCommand command)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(Name);

        if (command.ShowHelp)
        {
            Console.WriteLine(Usage);
            return SeedCommands.ExitOk;
        }

        if (!SeedCommands.IsAllowedEnvironment(environment))
        {
            logger.LogError("seed-cleanup reddedildi: ortam '{Environment}' (yalnızca Development/Staging).", environment.EnvironmentName);
            Console.Error.WriteLine(SeedCommands.RefusalMessage(Name, environment));
            return SeedCommands.ExitEnvironmentRefused;
        }

        if (command.Options.Apply)
        {
            var configuration = services.GetService<IConfiguration>();
            Console.WriteLine($"seed-cleanup --apply: Ortam={environment.EnvironmentName} DB={DescribeDatabase(configuration)} " +
                              $"auth-api={(command.Options.SkipAuthApi ? "(atlandı)" : configuration?[AuthApiSeedClient.BaseUrlConfigKey] ?? "?")}");

            if (environment.IsStaging() && !command.Options.Yes)
            {
                Console.Error.WriteLine("seed-cleanup --apply Staging ortamında --yes onayı ister; hiçbir şey silinmedi.");
                return SeedCommands.ExitUsage;
            }
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;

        try
        {
            using var scope = services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ISeedCleanupService>();
            var result = await service.RunAsync(command.Options, cts.Token);
            Console.WriteLine(Format(result));
            if (result.TotalFailed > 0 || result.AuthApiError is not null)
            {
                Console.Error.WriteLine("seed-cleanup: hata var (ayrıntı yukarıda). Tekrar koşu kalanı temizler.");
                return SeedCommands.ExitFailed;
            }
            return SeedCommands.ExitOk;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("seed-cleanup iptal edildi (Ctrl+C). Tekrar koşu kalanı temizler.");
            return SeedCommands.ExitFailed;
        }
        catch (TeacherSeedAuthApiException ex)
        {
            logger.LogError(ex, "seed-cleanup: auth-api çağrısı başarısız.");
            Console.Error.WriteLine(ex.Message);
            return SeedCommands.ExitFailed;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "seed-cleanup: veritabanı yazma başarısız.");
            Console.Error.WriteLine($"Veritabanı yazma başarısız: {ex.InnerException?.Message ?? ex.Message}");
            return SeedCommands.ExitFailed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.LogError(ex, "seed-cleanup başarısız.");
            Console.Error.WriteLine(ex.Message);
            return SeedCommands.ExitFailed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "seed-cleanup beklenmeyen hata.");
            Console.Error.WriteLine($"seed-cleanup başarısız: {ex.GetBaseException().Message}");
            return SeedCommands.ExitFailed;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Bağlantı dizesinden yalnızca host/port/veritabanı (parola asla yazılmaz).</summary>
    public static string DescribeDatabase(IConfiguration? configuration)
    {
        var raw = configuration?.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(raw)) return "?";
        try
        {
            var b = new Npgsql.NpgsqlConnectionStringBuilder(raw);
            return $"{b.Host}:{b.Port}/{b.Database}";
        }
        catch (ArgumentException)
        {
            return "(çözümlenemedi)";
        }
    }

    public static string Format(SeedCleanupResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Applied ? $"== seed-cleanup APPLY{(r.Force ? " --force" : "")} ==" : "== seed-cleanup DRY-RUN (hiçbir şey silinmedi) ==");
        sb.AppendLine();
        sb.AppendLine("Seed envanteri");
        sb.AppendLine($"  Okul: {r.SeedSchools}   Okul öğretmeni: {r.SeedSchoolTeachers}   Bağımsız öğretmen: {r.SeedTutors} (pending {r.SeedTutorsPending})");
        if (r.SchoolGroups.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {"İl",-16} {"Tür",-10} {"Okul",6}");
            foreach (var g in r.SchoolGroups) sb.AppendLine($"  {g.Province,-16} {g.Kind,-10} {g.Count,6}");
        }
        if (r.TeacherGroups.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {"İl",-16} {"Branş",-30} {"Okul öğr.",9} {"Bağımsız",9}");
            foreach (var g in r.TeacherGroups) sb.AppendLine($"  {g.Province,-16} {g.Subject,-30} {g.SchoolTeachers,9} {g.Tutors,9}");
        }

        var verb = r.Applied ? "silindi" : "silinecek";
        sb.AppendLine();
        sb.AppendLine("Exam DB");
        sb.AppendLine($"  Teacher {verb}={r.TeachersDeleted} atlandı(gerçek öğrenci randevusu)={r.TeachersSkippedRealStudentBooking} [randevu {r.RealStudentBookings}] " +
                      $"atlandı(müsaitlik)={r.TeachersSkippedScheduling} atlandı(worksheet/soru)={r.TeachersSkippedContent}" +
                      (r.Force ? $" force={r.TeachersForceDeleted} sahipsizİçerik={r.TeachersOrphanedContent} (worksheet {r.WorksheetsOrphaned}, soru {r.QuestionsOrphaned})" : ""));
        if (r.Applied)
            sb.AppendLine($"  TeacherSubject={r.TeacherSubjectsDeleted} Slot={r.SlotsDeleted} Rule={r.RulesDeleted}");
        sb.AppendLine($"  School {verb}={r.SchoolsDeleted} atlandı: seed-dışı öğretmen={r.SchoolsSkippedNonSeedTeacher} öğrenci={r.SchoolsSkippedStudent} atama={r.SchoolsSkippedAssignment} korunan seed öğretmen={r.SchoolsSkippedSeedTeacherKept}");

        sb.AppendLine();
        sb.AppendLine("auth-api (Keycloak + identity)");
        if (!r.AuthApiCalled)
            sb.AppendLine($"  çağrılmadı{(r.AuthApiError is null ? " (--skip-auth-api)" : ": " + r.AuthApiError)}");
        else if (!r.Applied)
            sb.AppendLine($"  silinecek={r.AuthPlanned} korunan(exam'de atlanan)={r.IdentityExcluded} yabancı={r.KeycloakSkippedForeign} keycloakEksik={r.KeycloakMissing}");
        else
            sb.AppendLine($"  Keycloak silindi={r.KeycloakDeleted} eksik={r.KeycloakMissing} korunan={r.KeycloakExcluded} yabancı={r.KeycloakSkippedForeign} hata={r.KeycloakFailed}   " +
                          $"Identity silindi={r.IdentityDeleted} korunan={r.IdentityExcluded} hata={r.IdentityFailed}");

        sb.AppendLine();
        sb.AppendLine($"Süre: exam={r.ExamDbElapsedMs} ms auth-api={r.AuthApiElapsedMs} ms toplam={r.TotalElapsedMs} ms");

        if (r.Skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Atlanan / not (ilk {r.Skipped.Count}):");
            foreach (var s in r.Skipped) sb.AppendLine("  " + s);
        }
        if (r.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Hatalar (ilk {r.Errors.Count}):");
            foreach (var e in r.Errors) sb.AppendLine("  " + e);
        }
        if (!r.Applied)
            sb.AppendLine().Append("Silmek için: dotnet run -- seed-cleanup --apply [--force]");
        return sb.ToString();
    }
}
