using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Audit;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Commands;

/// <summary>
/// Yetkili hesap denetimi (issue #267): <c>dotnet run -- audit-privileged-users [--format table|csv|json]</c>.
///
/// <para>Keycloak realm'inde <c>Admin</c> ve <c>exam-service</c> rollerine doğrudan atanmış tüm kullanıcıları listeler,
/// identity DB <c>Users</c> kaydıyla eşleştirir ve sınıflandırır (bkz. <see cref="PrivilegedUserAuditService.Classify"/>).
/// SALT OKUNUR: Keycloak'a ve DB'ye yazmaz, migration çalıştırmaz, Kestrel açmaz — bu yüzden seed komutlarının aksine
/// ortam guard'ı YOK, Production'da da çalışır. Otomatik disable/rol kaldırma yapılmaz.</para>
///
/// <para>Çıkış kodu: 0 = tüm hesaplar açıklandı, 1 = hatalı kullanım, 2 = en az bir UNEXPLAINED hesap var
/// ya da dolaylı yetki uyarısı (grup/kompozit) var (script/CI koşulu), 3 = çalışma hatası (Keycloak/DB erişimi, yapılandırma).</para>
/// </summary>
public static class PrivilegedUserAuditCommand
{
    public const string CommandName = "audit-privileged-users";
    public const int ExitOk = 0;
    public const int ExitUsage = 1;
    public const int ExitUnexplained = 2;
    public const int ExitFailed = 3;

    public enum OutputFormat { Table, Csv, Json }

    public sealed record Options(OutputFormat Format, bool ShowHelp);

    public static string Usage => $"""
        Kullanım: dotnet run -- {CommandName} [--format table|csv|json]

          --format <f>   Çıktı biçimi (varsayılan: table). csv'de özet stderr'e yazılır; json özeti içerir.
          --help         Bu metin

        Salt okunur. Gerekli yapılandırma: Keycloak:Host, Keycloak:RealmRolesUrl, Keycloak:AdminClientId,
        Keycloak__AdminClientSecret (env), ConnectionStrings__DefaultConnection (identity DB), isteğe bağlı
        PrivilegedAudit__KnownAccounts__<n> (yalnızca Keycloak id / sub).
        Çıkış kodu: 0 = bulgu yok, 2 = UNEXPLAINED ya da dolaylı yetki uyarısı var, 1 = kullanım hatası, 3 = çalışma hatası.
        Ayrıntı: auth-api/docs/privileged-user-audit.md
        """;

    public static bool IsRequested(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    /// <summary>Argümanları çözer; hatalı kullanımda <see cref="ArgumentException"/>.</summary>
    public static Options Parse(string[] args)
    {
        if (!IsRequested(args))
            throw new ArgumentException($"İlk argüman '{CommandName}' olmalı.");

        var format = OutputFormat.Table;
        var help = false;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            string? value = null;
            if (arg.StartsWith("--format=", StringComparison.Ordinal))
                value = arg["--format=".Length..];
            else if (arg == "--format")
                value = i + 1 < args.Length ? args[++i] : throw new ArgumentException($"--format bir değer ister.{Environment.NewLine}{Usage}");
            else if (arg is "--help" or "-h")
            {
                help = true;
                continue;
            }
            else
                throw new ArgumentException($"Bilinmeyen argüman: '{arg}'.{Environment.NewLine}{Usage}");

            format = value.ToLowerInvariant() switch
            {
                "table" => OutputFormat.Table,
                "csv" => OutputFormat.Csv,
                "json" => OutputFormat.Json,
                _ => throw new ArgumentException($"Geçersiz --format değeri: '{value}' (table|csv|json).")
            };
        }
        return new Options(format, help);
    }

    public static int ExitCodeFor(PrivilegedAuditReport report)
        => report.HasFindings ? ExitUnexplained : ExitOk;

    /// <summary>
    /// Web host'u KURMADAN (Kestrel, Redis, migration, ServiceDefaults yok) yalnızca denetimin ihtiyaç duyduğu servislerle
    /// bir DI kabı kurar. Günlükler stderr'e gider; stdout yalnızca rapor taşır (csv/json pipe edilebilsin).
    /// </summary>
    public static ServiceProvider BuildServices(IConfiguration configuration)
    {
        // Ayrı kap: web host'un kurulumu Database.Migrate() (yazma) + Redis/Kestrel/prod secret guard'ı içerir; salt okunur
        // komut bunların hiçbirine bağımlı olmamalı ve Production'da migration tetiklememeli.
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(b => b
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
            .SetMinimumLevel(LogLevel.Warning)
            .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
            .AddFilter("System.Net.Http", LogLevel.Warning));
        services.Configure<KeycloakSettings>(configuration.GetSection("Keycloak"));
        services.Configure<PrivilegedAuditSettings>(configuration.GetSection(PrivilegedAuditSettings.SectionName));
        services.AddHttpClient();
        services.AddKeycloakAdminHttpClient();
        services.AddSingleton<KeycloakAdminTokenCache>();
        services.AddScoped<IKeycloakService, KeycloakService>();
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<IPrivilegedUserAuditService, PrivilegedUserAuditService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Program.cs giriş noktası: yapılandırma kontrolü → servisler → <see cref="RunAsync"/>.</summary>
    public static async Task<int> ExecuteAsync(IConfiguration configuration, Options options, CancellationToken ct = default)
    {
        // Windows konsolunda Türkçe karakterler bozulmasın; csv/json pipe'ı da UTF-8 alsın.
        Console.OutputEncoding = Encoding.UTF8;

        if (string.IsNullOrWhiteSpace(configuration["Keycloak:AdminClientSecret"]))
        {
            Console.Error.WriteLine("Keycloak__AdminClientSecret ortam değişkeni set edilmeli (admin API erişimi için).");
            return ExitFailed;
        }
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            Console.Error.WriteLine("ConnectionStrings__DefaultConnection (identity DB) set edilmeli.");
            return ExitFailed;
        }

        await using var provider = BuildServices(configuration);
        await using var scope = provider.CreateAsyncScope();
        return await RunAsync(scope.ServiceProvider.GetRequiredService<IPrivilegedUserAuditService>(),
            options, Console.Out, Console.Error, ct);
    }

    /// <summary>Denetimi çalıştırır, raporu yazar ve çıkış kodunu döner (testlerin giriş noktası).</summary>
    public static async Task<int> RunAsync(
        IPrivilegedUserAuditService audit, Options options, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        PrivilegedAuditReport report;
        try
        {
            report = await audit.AuditAsync(PrivilegedUserAuditService.DefaultRoles, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Mesaj yeterli (Keycloak yanıtı / DB hatası); stack trace ve bağlantı dizesi yazılmaz.
            await stderr.WriteLineAsync($"{CommandName} başarısız: {ex.GetType().Name}: {Clean(ex.Message)}");
            return ExitFailed;
        }

        switch (options.Format)
        {
            case OutputFormat.Json:
                await stdout.WriteLineAsync(FormatJson(report));
                break;
            case OutputFormat.Csv:
                await stdout.WriteAsync(FormatCsv(report));
                await stderr.WriteAsync(FormatSummary(report));
                break;
            default:
                await stdout.WriteAsync(FormatTable(report));
                await stdout.WriteLineAsync();
                await stdout.WriteAsync(FormatSummary(report));
                break;
        }

        return ExitCodeFor(report);
    }

    // ---- Biçimlendirme ----

    /// <summary>
    /// Ham Keycloak/DB değerlerindeki kontrol karakterlerini (satır sonu, tab, ESC/ANSI dizileri dahil) <c>?</c> ile değiştirir:
    /// terminal/rapor çıktısı sahte satır ya da renk kodu ile manipüle edilemesin.
    /// </summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (!value.Any(char.IsControl)) return value;
        return string.Create(value.Length, value, (span, v) =>
        {
            for (var i = 0; i < v.Length; i++) span[i] = char.IsControl(v[i]) ? '?' : v[i];
        });
    }

    private static readonly string[] Headers =
    [
        "role", "class", "source", "keycloakId", "username", "email", "enabled", "kcCreatedUtc",
        "identity", "idCreatedUtc", "idDeleted", "idRole", "idSeed", "note"
    ];

    private static string[] Cells(PrivilegedAccountRow r) =>
    [
        Clean(r.Role),
        r.Class.ToLabel(),
        Clean(r.Source),
        Clean(r.KeycloakId),
        Clean(r.UsernameMasked),
        Clean(r.EmailMasked),
        r.Enabled ? "true" : "false",
        r.KeycloakCreatedAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "-",
        r.IdentityFound ? "yes" : "no",
        r.IdentityCreatedAt is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "-",
        r.IdentityIsDeleted is { } d ? (d ? "true" : "false") : "-",
        string.IsNullOrEmpty(r.IdentityRole) ? "-" : Clean(r.IdentityRole),
        r.IdentityIsSeedData is { } s ? (s ? "true" : "false") : "-",
        Clean(r.Note)
    ];

    public static string FormatTable(PrivilegedAuditReport report)
    {
        var rows = report.Rows.Select(Cells).ToList();
        var widths = Headers.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        var sb = new StringBuilder();
        void Line(IReadOnlyList<string> cells)
            => sb.AppendLine(string.Join("  ", cells.Select((c, i) => i == cells.Count - 1 ? c : c.PadRight(widths[i]))).TrimEnd());
        Line(Headers);
        Line(widths.Select(w => new string('-', w)).ToArray());
        foreach (var r in rows) Line(r);
        if (rows.Count == 0) sb.AppendLine("(yetkili rolde kullanıcı yok)");
        return sb.ToString();
    }

    public static string FormatCsv(PrivilegedAuditReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Headers));
        foreach (var r in report.Rows)
            sb.AppendLine(string.Join(',', Cells(r).Select(CsvEscape)));
        return sb.ToString();
    }

    /// <summary>Formül enjeksiyonu önekleri (OWASP CSV injection): bu karakterlerle başlayan hücre <c>'</c> ile nötrlenir.</summary>
    private const string CsvFormulaPrefixes = "=+-@\t\r";

    private static string CsvEscape(string value)
    {
        if (value.Length > 0 && CsvFormulaPrefixes.Contains(value[0]) && value != "-")
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string FormatJson(PrivilegedAuditReport report)
        => JsonSerializer.Serialize(new
        {
            generatedAtUtc = report.GeneratedAt.UtcDateTime,
            hasUnexplained = report.HasUnexplained,
            hasFindings = report.HasFindings,
            warnings = report.WarningList,
            summary = report.Summary.Select(s => new
            {
                role = s.Role, roleMissing = s.RoleMissing, total = s.Total, serviceAccount = s.ServiceAccount, known = s.Known, unexplained = s.Unexplained
            }),
            accounts = report.Rows.Select(r => new
            {
                role = r.Role,
                @class = r.Class.ToLabel(),
                source = r.Source,
                keycloakId = r.KeycloakId,
                username = r.UsernameMasked,
                email = r.EmailMasked,
                enabled = r.Enabled,
                keycloakCreatedAtUtc = r.KeycloakCreatedAt?.UtcDateTime,
                identityFound = r.IdentityFound,
                identityCreatedAtUtc = r.IdentityCreatedAt is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : (DateTime?)null,
                identityIsDeleted = r.IdentityIsDeleted,
                identityRole = r.IdentityRole,
                identityIsSeedData = r.IdentityIsSeedData,
                note = r.Note
            })
        }, JsonOptions);

    public static string FormatSummary(PrivilegedAuditReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Özet ({report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC):");
        foreach (var s in report.Summary)
        {
            sb.AppendLine(
                $"  {Clean(s.Role)}: toplam={s.Total} SERVICE_ACCOUNT={s.ServiceAccount} KNOWN={s.Known} UNEXPLAINED={s.Unexplained}" +
                (s.RoleMissing ? " (rol realm'de tanımlı değil)" : string.Empty));
        }

        if (report.WarningList.Count > 0)
        {
            sb.AppendLine("Dolaylı yetki uyarıları:");
            foreach (var w in report.WarningList)
                sb.AppendLine("  UYARI: " + Clean(w));
        }

        var unexplained = report.Rows.Where(r => r.Class == PrivilegedAccountClass.Unexplained)
            .Select(r => Clean(r.KeycloakId)).Distinct(StringComparer.Ordinal).ToList();
        if (!report.HasFindings)
            sb.AppendLine("Sonuç: açıklanamayan yetkili hesap ya da dolaylı yetki yok (exit 0).");
        else
        {
            sb.AppendLine($"Sonuç: {unexplained.Count} açıklanamayan hesap, {report.WarningList.Count} dolaylı yetki uyarısı (exit 2).");
            if (unexplained.Count > 0)
                sb.AppendLine($"İnceleme için sub listesi: {string.Join(",", unexplained)}");
        }
        return sb.ToString();
    }
}
