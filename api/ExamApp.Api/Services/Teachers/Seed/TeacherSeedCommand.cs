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
/// <c>dotnet run -- seed-teachers [...]</c> komut modu (issue #217) — <c>seed-schools</c> (#216) ile aynı desen;
/// Program.cs <see cref="ISeedCommand"/> üzerinden dispatch eder. Ortam guard'ı üç katmanlı: Program.cs
/// (host kurulmadan), burada ve serviste; Production'da servis DI'a hiç kaydedilmez.
/// </summary>
public sealed record TeacherSeedCommand(TeacherSeedOptions Options, string? ConnectionString, bool ShowHelp, bool NoMigrate = false)
    : ISeedCommand
{
    public const string Name = "seed-teachers";

    public string CommandName => Name;
    public string UsageText => Usage;

    public static string Usage => """
        Kullanım: dotnet run -- seed-teachers [seçenekler]

          --provinces <il,il,...>              Hangi illerin seed okulları (varsayılan: issue #216'daki 10 il)
          --limit-schools-per-province <N>     İl başına en fazla N okul — ada göre sıralı ilk N (varsayılan: sınırsız)
          --dry-run                            auth-api'yi çağırma, hiçbir şey yazma; planı raporla
          --no-events                          UserPreferredLocaleChangedEvent outbox satırlarını yazma (hacim için)
          --keycloak-mode <admin-api|partial-import>
                                               Keycloak'a yazma yolu (varsayılan: admin-api — kullanıcı başına 2 istek;
                                               partial-import: parti başına tek istek, önceden hash'lenmiş parola)
          --batch-size <N>                     auth-api'ye istek başına hesap (varsayılan 100, en fazla 500)
          --connection <conn-str>              ConnectionStrings:DefaultConnection yerine kullanılacak bağlantı
                                               (tercih edilen yol: ConnectionStrings__DefaultConnection ortam değişkeni)
          --no-migrate                         Bekleyen migration'ları ve il/ilçe referans seed'ini atla
          --help                               Bu metin

        Yalnızca Development/Staging ortamında çalışır. Ortak parola koda yazılmaz; SeedData:Password config
        anahtarı zorunludur ve YALNIZCA şu iki yolla verilir (.env dosyası bu komut için OKUNMAZ):
          ortam değişkeni:  SeedData__Password=<parola> dotnet run -- seed-teachers ...
          user-secrets:     dotnet user-secrets set "SeedData:Password" "<parola>"   (api/ExamApp.Api dizininde)
        Hesaplar
        seed.t.<kurumKodu>.<brans>.<n>@seed.examapp.local deseniyle üretilir; Keycloak kullanıcısı + identity
        User + exam Teacher (SchoolId, Approved, IsSeedData=true). Tekrar koşu kopya açmaz.
        Ortaokul: 2 Türkçe, 2 Matematik, 2 Fen, 2 Sosyal, 1 İngilizce, 1 Din Kültürü. İlkokul: 1'er Türkçe/
        Matematik/Fen/Sosyal/İngilizce. auth-api'nin ayakta olması gerekir (AuthApiBaseUrl).
        """;

    public static bool IsRequested(string[] args)
        => args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    public static TeacherSeedCommand Parse(string[] args)
    {
        if (!IsRequested(args))
            throw new ArgumentException($"İlk argüman '{Name}' olmalı.");

        var provinces = Schools.Seed.SchoolSeedOptions.DefaultProvinces;
        int? limit = null;
        var dryRun = false;
        var emitEvents = true;
        var mode = TeacherSeedOptions.KeycloakModeAdminApi;
        var batchSize = TeacherSeedOptions.DefaultBatchSize;
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
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--no-events":
                    emitEvents = false;
                    break;
                case "--no-migrate":
                    noMigrate = true;
                    break;
                case "--connection":
                    connection = TakeValue(args, ref i, arg);
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

        var options = new TeacherSeedOptions
        {
            Provinces = provinces,
            LimitSchoolsPerProvince = limit,
            DryRun = dryRun,
            EmitEvents = emitEvents,
            KeycloakMode = mode,
            BatchSize = batchSize
        };
        return new TeacherSeedCommand(options, connection, help, noMigrate);
    }

    public Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment) => RunAsync(services, environment, this);

    /// <summary>Host kurulduktan sonra çağrılır; süreç çıkış kodunu döner (Ctrl+C: Console.CancelKeyPress ile iptal).</summary>
    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, TeacherSeedCommand command)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(Name);

        if (command.ShowHelp)
        {
            Console.WriteLine(Usage);
            return SeedCommands.ExitOk;
        }

        if (!SeedCommands.IsAllowedEnvironment(environment))
        {
            logger.LogError("seed-teachers reddedildi: ortam '{Environment}' (yalnızca Development/Staging).", environment.EnvironmentName);
            Console.Error.WriteLine(SeedCommands.RefusalMessage(Name, environment));
            return SeedCommands.ExitEnvironmentRefused;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            using var scope = services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITeacherSeedService>();
            var result = await service.RunAsync(command.Options, cts.Token);
            Console.WriteLine(Format(result, command.Options));
            // Kısmi başarı da hata çıkış koduyla biter: CI/betik "0 = her şey tamam" diyebilsin. Tamamlanan
            // partiler kalıcıdır; tekrar koşu yalnızca eksikleri dener.
            if (result.Failed > 0)
            {
                Console.Error.WriteLine($"seed-teachers: {result.Failed} hesap başarısız (ayrıntı yukarıda). Tekrar koşu eksikleri tamamlar.");
                return SeedCommands.ExitFailed;
            }
            return SeedCommands.ExitOk;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("seed-teachers iptal edildi (Ctrl+C). Tamamlanan partiler kalıcıdır; tekrar koşu kaldığı yerden tamamlar.");
            return SeedCommands.ExitFailed;
        }
        catch (TaskCanceledException ex) when (!cts.IsCancellationRequested)
        {
            // HttpClient zaman aşımı istemci katmanında TeacherSeedAuthApiException'a çevrilir; burası
            // başka bir zaman aşımı kaynağı (ör. DB komutu) için son sigorta.
            logger.LogError(ex, "seed-teachers: zaman aşımı.");
            Console.Error.WriteLine($"seed-teachers zaman aşımı: {ex.Message}. auth-api/Keycloak yavaş olabilir; --batch-size küçültün.");
            return SeedCommands.ExitFailed;
        }
        catch (TeacherSeedAuthApiException ex)
        {
            logger.LogError(ex, "seed-teachers: auth-api çağrısı başarısız.");
            Console.Error.WriteLine(ex.Message);
            return SeedCommands.ExitFailed;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "seed-teachers: veritabanına yazma başarısız.");
            Console.Error.WriteLine($"Veritabanına yazma başarısız: {ex.InnerException?.Message ?? ex.Message}");
            return SeedCommands.ExitFailed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.LogError(ex, "seed-teachers başarısız.");
            Console.Error.WriteLine(ex.Message);
            return SeedCommands.ExitFailed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "seed-teachers beklenmeyen hata.");
            Console.Error.WriteLine($"seed-teachers başarısız: {ex.GetBaseException().Message}");
            return SeedCommands.ExitFailed;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Konsol için özet tablo. Parola hiçbir zaman yazılmaz.</summary>
    public static string Format(TeacherSeedResult r, TeacherSeedOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.DryRun ? "== seed-teachers DRY-RUN (hiçbir şey yazılmadı) ==" : "== seed-teachers ==");
        sb.AppendLine($"Parametreler: iller={string.Join(",", options.Provinces)} limit={options.LimitSchoolsPerProvince?.ToString(CultureInfo.InvariantCulture) ?? "sınırsız"} " +
                      $"keycloak={r.KeycloakMode} parti={options.BatchSize} events={(options.EmitEvents ? "açık" : "kapalı")}");
        sb.AppendLine($"Okullar: seçilen={r.SchoolsSelected} (ilkokul={r.SchoolsIlkokul} ortaokul={r.SchoolsOrtaokul}) türüBilinmeyen={r.SchoolsUnknownKind} limitDışı={r.SchoolsSkippedByLimit}");
        sb.AppendLine();
        sb.AppendLine($"{"İl",-12} {"Eşleşti",-8} {"İlk",5} {"Orta",5} {"Plan",6} {"Yeni",6} {"Mevcut",7} {"Hata",5} {"Limit",6}");
        foreach (var p in r.Provinces)
        {
            sb.AppendLine($"{p.Province,-12} {(p.ProvinceMatched ? "evet" : "HAYIR"),-8} {p.SchoolsIlkokul,5} {p.SchoolsOrtaokul,5} {p.Planned,6} {p.Created,6} {p.Existing,7} {p.Failed,5} {p.SkippedByLimit,6}");
        }
        sb.AppendLine();
        sb.AppendLine($"{"Branş",-30} {"Plan",6} {"Yeni",6} {"Mevcut",7} {"Hata",5}");
        foreach (var b in r.Branches)
            sb.AppendLine($"{b.SubjectName,-30} {b.Planned,6} {b.Created,6} {b.Existing,7} {b.Failed,5}");
        sb.AppendLine();
        sb.AppendLine($"Toplam: plan={r.Planned} {(r.DryRun ? "(dry-run)" : $"teacherYeni={r.TeachersCreated} teacherMevcut={r.TeachersExisting} hata={r.Failed}")}");
        if (!r.DryRun)
        {
            sb.AppendLine($"Keycloak: yeni={r.KeycloakCreated} mevcut={r.KeycloakExisting}  Identity: yeni={r.IdentityCreated} mevcut={r.IdentityExisting}");
            sb.AppendLine($"Süre: keycloak={r.KeycloakElapsedMs} ms, identity={r.IdentityDbElapsedMs} ms, exam={r.ExamDbElapsedMs} ms, toplam={r.TotalElapsedMs} ms, parti={r.Batches}");
            if (r.KeycloakCreated > 0)
                sb.AppendLine($"Keycloak ortalama: {(double)r.KeycloakElapsedMs / Math.Max(1, r.KeycloakCreated + r.KeycloakExisting):F1} ms/hesap");
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
                sb.AppendLine($"  [{a.Status,-8}] {a.Email,-48} {a.FullName,-22} {a.Branch,-15} {a.School} (#{a.SchoolId}, {a.Province})");
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
