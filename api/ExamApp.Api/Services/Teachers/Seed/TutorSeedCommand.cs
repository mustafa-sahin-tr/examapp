using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// <c>dotnet run -- seed-tutors [...]</c> komut modu (issue #218) — <c>seed-teachers</c> (#217) ile aynı desen ve
/// guard katmanları. Ayrı komut: taban veri kaynağı farklı (okul listesi değil, DB'deki seed okul öğretmenleri) ve
/// <c>seed-teachers</c> bittikten sonra koşması gerekir.
/// </summary>
public sealed record TutorSeedCommand(TutorSeedOptions Options, string? ConnectionString, bool ShowHelp, bool NoMigrate = false)
    : ISeedCommand
{
    public const string Name = "seed-tutors";

    public string CommandName => Name;
    public string UsageText => Usage;

    public static string Usage => """
        Kullanım: dotnet run -- seed-tutors [seçenekler]

          --provinces <il,il,...>              Hangi iller (varsayılan: issue #216'daki 10 il)
          --limit-schools-per-province <N>     Tabana il başına ada göre sıralı ilk N seed okulun öğretmenleri sayılır
                                               (seed-teachers ile aynı limit → tutarlı sayılar; varsayılan: tümü)
          --pending-ratio <0..1>               Her il+branş grubunda tutor'ların bu oranı (floor) Pending kalır
                                               (varsayılan: Development 0 = hepsi Approved, Staging 1 = hepsi Pending)
          --dry-run                            auth-api'yi çağırma, hiçbir şey yazma; planı raporla
          --no-events                          UserPreferredLocaleChangedEvent outbox satırlarını yazma
          --keycloak-mode <admin-api|partial-import>
          --batch-size <N>                     auth-api'ye istek başına hesap (varsayılan 100, en fazla 500)
          --connection <conn-str>              ConnectionStrings:DefaultConnection yerine kullanılacak bağlantı
          --no-migrate                         Bekleyen migration'ları ve referans seed'ini atla
          --help                               Bu metin

        Yalnızca Development/Staging ortamında çalışır. seed-teachers ÖNCE koşmuş olmalı: il + branş bazında seed okul
        öğretmeni sayısının YARISI (floor: 5 → 2, 0 → 0) kadar bağımsız öğretmen üretilir:
        IsIndependentTutor=true, SchoolId=null, ApprovalStatus=Approved (bilinçli sapma — aramada görünsün),
        deterministik saatlik ücret/online/yüz yüze/tanıtım. E-posta seed.i.<il>.<brans>.<n>@seed.examapp.local.
        Parola: SeedData:Password config anahtarı (SeedData__Password ortam değişkeni ya da user-secrets; .env okunmaz).
        Tekrar koşu kopya açmaz. auth-api'nin ayakta olması gerekir (AuthApiBaseUrl).
        """;

    public static bool IsRequested(string[] args)
        => args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    public static TutorSeedCommand Parse(string[] args)
    {
        if (!IsRequested(args))
            throw new ArgumentException($"İlk argüman '{Name}' olmalı.");

        var provinces = Schools.Seed.SchoolSeedOptions.DefaultProvinces;
        int? limit = null;
        var dryRun = false;
        var emitEvents = true;
        var mode = TeacherSeedOptions.KeycloakModeAdminApi;
        var batchSize = TeacherSeedOptions.DefaultBatchSize;
        double? pendingRatio = null;
        string? connection = null;
        var help = false;
        var noMigrate = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--provinces":
                case "--province":
                    provinces = SplitList(TakeValue(args, ref i, arg));
                    if (provinces.Count == 0) throw new ArgumentException("--provinces boş olamaz.");
                    break;
                case "--limit-schools-per-province":
                case "--limit":
                    limit = ParsePositive(TakeValue(args, ref i, arg), arg);
                    break;
                case "--pending-ratio":
                    var rawRatio = TakeValue(args, ref i, arg);
                    if (!double.TryParse(rawRatio, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRatio) || parsedRatio is < 0 or > 1)
                        throw new ArgumentException($"--pending-ratio 0..1 arasında ondalık olmalı (nokta ile): '{rawRatio}'.");
                    pendingRatio = parsedRatio;
                    break;
                case "--batch-size":
                    batchSize = ParsePositive(TakeValue(args, ref i, arg), arg);
                    if (batchSize > TeacherSeedOptions.MaxBatchSize)
                        throw new ArgumentException($"--batch-size en fazla {TeacherSeedOptions.MaxBatchSize} olabilir.");
                    break;
                case "--keycloak-mode":
                    var raw = TakeValue(args, ref i, arg).ToLowerInvariant();
                    mode = raw switch
                    {
                        TeacherSeedOptions.KeycloakModeAdminApi => TeacherSeedOptions.KeycloakModeAdminApi,
                        TeacherSeedOptions.KeycloakModePartialImport => TeacherSeedOptions.KeycloakModePartialImport,
                        _ => throw new ArgumentException($"--keycloak-mode admin-api ya da partial-import olmalı: '{raw}'.")
                    };
                    break;
                case "--dry-run": dryRun = true; break;
                case "--no-events": emitEvents = false; break;
                case "--no-migrate": noMigrate = true; break;
                case "--connection": connection = TakeValue(args, ref i, arg); break;
                case "--help":
                case "-h":
                case "-?":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"Bilinmeyen seçenek: '{arg}'.\n{Usage}");
            }
        }

        var options = new TutorSeedOptions
        {
            Provinces = provinces,
            LimitSchoolsPerProvince = limit,
            DryRun = dryRun,
            PendingRatio = pendingRatio,
            EmitEvents = emitEvents,
            KeycloakMode = mode,
            BatchSize = batchSize
        };
        return new TutorSeedCommand(options, connection, help, noMigrate);
    }

    public Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment) => RunAsync(services, environment, this);

    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, TutorSeedCommand command)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(Name);

        if (command.ShowHelp)
        {
            Console.WriteLine(Usage);
            return SeedCommands.ExitOk;
        }

        if (!SeedCommands.IsAllowedEnvironment(environment))
        {
            logger.LogError("seed-tutors reddedildi: ortam '{Environment}' (yalnızca Development/Staging).", environment.EnvironmentName);
            Console.Error.WriteLine(SeedCommands.RefusalMessage(Name, environment));
            return SeedCommands.ExitEnvironmentRefused;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;

        try
        {
            using var scope = services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITutorSeedService>();
            var result = await service.RunAsync(command.Options, cts.Token);
            Console.WriteLine(Format(result, command.Options));
            if (result.Failed > 0)
            {
                Console.Error.WriteLine($"seed-tutors: {result.Failed} hesap başarısız (ayrıntı yukarıda). Tekrar koşu eksikleri tamamlar.");
                return SeedCommands.ExitFailed;
            }
            return SeedCommands.ExitOk;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("seed-tutors iptal edildi (Ctrl+C). Tamamlanan partiler kalıcıdır; tekrar koşu kaldığı yerden tamamlar.");
            return SeedCommands.ExitFailed;
        }
        catch (TaskCanceledException ex) when (!cts.IsCancellationRequested)
        {
            logger.LogError(ex, "seed-tutors: zaman aşımı.");
            Console.Error.WriteLine($"seed-tutors zaman aşımı: {ex.Message}. auth-api/Keycloak yavaş olabilir; --batch-size küçültün.");
            return SeedCommands.ExitFailed;
        }
        catch (TeacherSeedAuthApiException ex)
        {
            logger.LogError(ex, "seed-tutors: auth-api çağrısı başarısız.");
            Console.Error.WriteLine(ex.Message);
            return SeedCommands.ExitFailed;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "seed-tutors: veritabanına yazma başarısız.");
            Console.Error.WriteLine($"Veritabanına yazma başarısız: {ex.InnerException?.Message ?? ex.Message}");
            return SeedCommands.ExitFailed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.LogError(ex, "seed-tutors başarısız.");
            Console.Error.WriteLine(ex.Message);
            return SeedCommands.ExitFailed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "seed-tutors beklenmeyen hata.");
            Console.Error.WriteLine($"seed-tutors başarısız: {ex.GetBaseException().Message}");
            return SeedCommands.ExitFailed;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Konsol özeti. Parola hiçbir zaman yazılmaz.</summary>
    public static string Format(TutorSeedResult r, TutorSeedOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.DryRun ? "== seed-tutors DRY-RUN (hiçbir şey yazılmadı) ==" : "== seed-tutors ==");
        sb.AppendLine($"Parametreler: iller={string.Join(",", options.Provinces)} limit={options.LimitSchoolsPerProvince?.ToString(CultureInfo.InvariantCulture) ?? "sınırsız"} " +
                      $"pendingRatio={r.PendingRatio.ToString(CultureInfo.InvariantCulture)}{(options.PendingRatio is null ? " (ortam varsayılanı)" : "")} keycloak={r.KeycloakMode} parti={options.BatchSize} events={(options.EmitEvents ? "açık" : "kapalı")}");
        sb.AppendLine($"Taban: seed okul={r.SchoolsCounted} (limitDışı={r.SchoolsSkippedByLimit}) seed okul öğretmeni={r.SchoolTeachersCounted}");
        sb.AppendLine();
        sb.AppendLine($"{"İl",-12} {"Branş",-30} {"Okul öğr.",9} {"Plan",6} {"Pend.",6} {"Yeni",6} {"Mevcut",7} {"Hata",5}");
        foreach (var g in r.Groups)
            sb.AppendLine($"{g.Province,-12} {g.SubjectName,-30} {g.SchoolTeachers,9} {g.Planned,6} {g.PlannedPending,6} {g.Created,6} {g.Existing,7} {g.Failed,5}");
        sb.AppendLine();
        sb.AppendLine($"Toplam: plan={r.Planned} pending={r.PlannedPending} {(r.DryRun ? "(dry-run)" : $"tutorYeni={r.TutorsCreated} tutorMevcut={r.TutorsExisting} hata={r.Failed}")}");
        if (!r.DryRun)
        {
            sb.AppendLine($"Keycloak: yeni={r.KeycloakCreated} mevcut={r.KeycloakExisting}  Identity: yeni={r.IdentityCreated} mevcut={r.IdentityExisting}");
            sb.AppendLine($"Süre: keycloak={r.KeycloakElapsedMs} ms, identity={r.IdentityDbElapsedMs} ms, exam={r.ExamDbElapsedMs} ms, toplam={r.TotalElapsedMs} ms, parti={r.Batches}");
        }
        if (r.UnmatchedProvinces.Count > 0)
            sb.AppendLine($"EŞLEŞMEYEN İL (Province tablosunda yok): {string.Join(", ", r.UnmatchedProvinces)}");
        if (r.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Hatalar (ilk {r.Errors.Count}/{r.Failed}):");
            foreach (var e in r.Errors) sb.AppendLine("  " + e);
        }
        if (r.Accounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Hesaplar (ilk {r.Accounts.Count}/{r.Planned}) — parola: {TeacherSeedService.PasswordConfigKey} config değeri:");
            foreach (var a in r.Accounts)
                sb.AppendLine($"  [{a.Status,-8}] {a.Email,-48} {a.FullName,-22} {a.Branch,-15} {a.Province,-12} {a.HourlyRate,5}₺ {(a.TeachesInPerson ? "online+yüzyüze" : "online"),-15}{(a.Pending ? " PENDING" : "")}");
        }
        return sb.ToString();
    }

    private static int ParsePositive(string raw, string option)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
            throw new ArgumentException($"{option} pozitif tam sayı olmalı: '{raw}'.");
        return n;
    }

    private static string TakeValue(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} bir değer bekliyor.\n{Usage}");
        return args[++i];
    }

    private static List<string> SplitList(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
