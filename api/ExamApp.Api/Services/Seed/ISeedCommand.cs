using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Services.Seed;

/// <summary>
/// <c>dotnet run -- &lt;komut&gt; [...]</c> komut modu sözleşmesi (issue #216'daki desen, #217 ile
/// birden fazla komuta genelleştirildi). Program.cs tek bir <see cref="ISeedCommand"/> üzerinden:
/// parse → ortam guard'ı (host kurulmadan) → <c>--connection</c> override → Build → migration
/// (<see cref="NoMigrate"/> değilse) → <see cref="RunAsync"/> → çıkış kodu. Kestrel hiç açılmaz.
/// </summary>
public interface ISeedCommand
{
    /// <summary>Komut adı (ilk argüman), örn. <c>seed-schools</c>.</summary>
    string CommandName { get; }

    bool ShowHelp { get; }

    /// <summary>Bekleyen migration'ları ve referans seed'ini atla.</summary>
    bool NoMigrate { get; }

    /// <summary><c>ConnectionStrings:DefaultConnection</c> yerine kullanılacak bağlantı (yalnızca bu süreç).</summary>
    string? ConnectionString { get; }

    string UsageText { get; }

    /// <summary>Host kurulduktan sonra çağrılır; süreç çıkış kodunu döner.</summary>
    Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment);
}
