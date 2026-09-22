using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>
/// <c>dotnet run -- seed-schools [...]</c> komut modu (issue #216). Program.cs host'u normal kurar
/// (migration + referans seed dahil) ama Kestrel'i AÇMADAN bu komutu çalıştırıp çıkar.
///
/// <para>Ortam guard'ı üç katmanlı: Program.cs'te host kurulmadan, burada (<see cref="RunAsync"/>) ve serviste. Production'da
/// servis DI'a hiç kaydedilmez (<see cref="SchoolSeedServiceCollectionExtensions"/>).</para>
/// </summary>
public sealed record SchoolSeedCommand(SchoolSeedOptions Options, string? ConnectionString, bool ShowHelp, bool NoMigrate = false)
{
    public const string Name = "seed-schools";

    public const int ExitOk = 0;
    public const int ExitUsage = 1;
    public const int ExitEnvironmentRefused = 2;
    public const int ExitFailed = 3;

    public static string Usage => """
        Kullanım: dotnet run -- seed-schools [seçenekler]

          --provinces <il,il,...>   İçe aktarılacak iller (varsayılan: issue #216'daki 10 il)
          --limit <N>               İl başına en fazla N okul — ada göre sıralı ilk N (varsayılan: sınırsız)
          --types <ilkokul,ortaokul> Tür seçimi (varsayılan: ikisi de)
          --include-imam-hatip      "İmam Hatip Ortaokulu" kayıtlarını Ortaokul olarak dahil et
          --dry-run                 Hiçbir şey yazma, yalnızca raporla
          --connection <conn-str>   ConnectionStrings:DefaultConnection yerine kullanılacak bağlantı
                                    (tercih edilen yol: ConnectionStrings__DefaultConnection ortam değişkeni)
          --no-migrate              Bekleyen migration'ları ve il/ilçe referans seed'ini atla
          --help                    Bu metin

        Yalnızca Development/Staging ortamında çalışır. Varsayılan olarak açılışta bekleyen migration'ları ve
        il/ilçe referans seed'ini uygular; --no-migrate ile atlanır. Kaynak: dalgali/MEB-okul-listesi (sabit
        commit), Data/SeedFixtures/ altına önbelleklenir. Ayrıntı: api/ExamApp.Api/Data/SeedFixtures/README.md
        """;

    /// <summary>İlk argüman <c>seed-schools</c> mu?</summary>
    public static bool IsRequested(string[] args)
        => args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Argümanları çözer; hatalı kullanımda <see cref="ArgumentException"/>.</summary>
    public static SchoolSeedCommand Parse(string[] args)
    {
        if (!IsRequested(args))
            throw new ArgumentException($"İlk argüman '{Name}' olmalı.");

        var provinces = SchoolSeedOptions.DefaultProvinces;
        int? limit = null;
        HashSet<SchoolSeedKind>? kinds = null;
        var includeImamHatip = false;
        var dryRun = false;
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
                case "--limit":
                    var raw = TakeValue(args, ref i, arg);
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
                        throw new ArgumentException($"--limit pozitif tam sayı olmalı: '{raw}'.");
                    limit = n;
                    break;
                case "--types":
                case "--type":
                    kinds = new HashSet<SchoolSeedKind>();
                    foreach (var t in SplitList(TakeValue(args, ref i, arg)))
                        kinds.Add(ParseKind(t));
                    if (kinds.Count == 0) throw new ArgumentException("--types boş olamaz.");
                    break;
                case "--include-imam-hatip":
                    includeImamHatip = true;
                    break;
                case "--dry-run":
                    dryRun = true;
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

        var options = new SchoolSeedOptions
        {
            Provinces = provinces,
            LimitPerProvince = limit,
            Kinds = kinds ?? new HashSet<SchoolSeedKind> { SchoolSeedKind.Ilkokul, SchoolSeedKind.Ortaokul },
            IncludeImamHatip = includeImamHatip,
            DryRun = dryRun
        };
        return new SchoolSeedCommand(options, connection, help, noMigrate);
    }

    public static SchoolSeedKind ParseKind(string value)
    {
        var key = SchoolSeedService.FoldKey(value);
        if (key == SchoolSeedService.FoldKey("ilkokul")) return SchoolSeedKind.Ilkokul;
        if (key == SchoolSeedService.FoldKey("ortaokul")) return SchoolSeedKind.Ortaokul;
        throw new ArgumentException($"Bilinmeyen tür: '{value}'. Geçerli: ilkokul, ortaokul.");
    }

    /// <summary>
    /// Host kurulduktan sonra çağrılır; süreç çıkış kodunu döner. Ortam guard'ı Program.cs'te host
    /// kurulmadan da uygulanır; burası ikinci katman (doğrudan çağrılırsa da reddetsin).
    /// Ctrl+C: komut modunda ApplicationStopping tetiklenmez, Console.CancelKeyPress ile iptal edilir.
    /// </summary>
    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, SchoolSeedCommand command)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("seed-schools");

        if (command.ShowHelp)
        {
            Console.WriteLine(Usage);
            return ExitOk;
        }

        if (!SchoolSeedService.IsAllowedEnvironment(environment))
        {
            logger.LogError("seed-schools reddedildi: ortam '{Environment}' (yalnızca Development/Staging).",
                environment.EnvironmentName);
            Console.Error.WriteLine($"seed-schools yalnızca Development/Staging ortamında çalışır; mevcut ortam: {environment.EnvironmentName}.");
            return ExitEnvironmentRefused;
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
            var service = scope.ServiceProvider.GetRequiredService<ISchoolSeedService>();
            var result = await service.RunAsync(command.Options, cts.Token);
            Console.WriteLine(Format(result, command.Options));
            return ExitOk;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("seed-schools iptal edildi (Ctrl+C).");
            return ExitFailed;
        }
        catch (SchoolSeedSourceException ex)
        {
            logger.LogError(ex, "seed-schools: kaynak dosya alınamadı.");
            Console.Error.WriteLine(ex.Message);
            return ExitFailed;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "seed-schools: veritabanına yazma başarısız.");
            Console.Error.WriteLine($"Veritabanına yazma başarısız: {ex.InnerException?.Message ?? ex.Message}");
            return ExitFailed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.InvalidDataException)
        {
            logger.LogError(ex, "seed-schools başarısız.");
            Console.Error.WriteLine(ex.Message);
            return ExitFailed;
        }
        catch (Exception ex)
        {
            // Bağlantı hatası (NpgsqlException) vb. — CLI aracı stack trace ile çökmesin, exit 3 versin.
            logger.LogError(ex, "seed-schools beklenmeyen hata.");
            Console.Error.WriteLine($"seed-schools başarısız: {ex.GetBaseException().Message}");
            return ExitFailed;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Konsol için özet tablo.</summary>
    public static string Format(SchoolSeedResult r, SchoolSeedOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.DryRun ? "== seed-schools DRY-RUN (hiçbir şey yazılmadı) ==" : "== seed-schools ==");
        sb.AppendLine($"Kaynak      : {r.Source}{(r.SourceFromCache ? " (önbellek)" : " (indirildi)")}");
        sb.AppendLine($"SHA-256     : {r.SourceSha256}");
        sb.AppendLine($"Parametreler: iller={string.Join(",", options.Provinces)} limit={options.LimitPerProvince?.ToString(CultureInfo.InvariantCulture) ?? "sınırsız"} " +
                      $"türler={string.Join(",", options.Kinds)} imamHatip={options.IncludeImamHatip}");
        sb.AppendLine($"Kaynak satır: {r.TotalRowsInSource}, filtreden geçen aday: {r.CandidateRows}");
        sb.AppendLine();
        sb.AppendLine($"{"İl",-12} {"Eşleşti",-8} {"İlk+",6} {"İlk=",6} {"Orta+",6} {"Orta=",6} {"Bşehir",7} {"ÖzelEğ",7} {"Limit",6} {"İlçe?",6}");
        foreach (var p in r.Provinces)
        {
            sb.AppendLine($"{p.Province,-12} {(p.ProvinceMatched ? "evet" : "HAYIR"),-8} {p.IlkokulAdded,6} {p.IlkokulExisting,6} {p.OrtaokulAdded,6} {p.OrtaokulExisting,6} " +
                          $"{p.SkippedBuyuksehir,7} {p.SkippedSpecialEducation,7} {p.SkippedByLimit,6} {p.UnmatchedDistrictRows,6}");
        }
        sb.AppendLine();
        sb.AppendLine($"Toplam: {(r.DryRun ? "eklenecek" : "eklendi")}={r.Added} mevcut={r.SkippedExisting} softDeleted={r.SkippedSoftDeleted} " +
                      $"büyükşehir={r.SkippedBuyuksehir} özelEğitim={r.SkippedSpecialEducation} uzun={r.SkippedTooLong} " +
                      $"limitDışı={r.SkippedByLimit} eşleşmeyenİlçeSatırı={r.UnmatchedDistrictRows}");

        if (r.UnmatchedProvinces.Count > 0)
            sb.AppendLine($"EŞLEŞMEYEN İL (Province tablosunda yok): {string.Join(", ", r.UnmatchedProvinces)}");
        if (r.ProvincesNotInSource.Count > 0)
            sb.AppendLine($"KAYNAKTA OLMAYAN İL: {string.Join(", ", r.ProvincesNotInSource)}");
        foreach (var u in r.UnmatchedDistricts)
            sb.AppendLine($"EŞLEŞMEYEN İLÇE: {u.Province}/{u.District} ({u.Rows} satır)");

        if (r.AddedSample.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{(r.DryRun ? "Eklenecek" : "Eklenen")} okullar (ilk {r.AddedSample.Count}/{r.Added}):");
            foreach (var s in r.AddedSample)
                sb.AppendLine($"  [{s.ExternalCode}] {s.Name} — {s.Province}/{s.District}");
        }

        return sb.ToString();
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
