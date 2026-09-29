using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests.Support;

/// <summary>
/// BadgeEvaluator'ın (issue #146) locale çözücü + gerçek tr/en kaynak dosyalarıyla çalışan metin
/// fabrikası bağımlılıklarını testlerde tek noktadan kurar.
/// </summary>
public static class BadgeEvaluatorFactory
{
    public static BadgeEvaluator Create(BadgeDbContext ctx, IHubContext<BadgeNotificationHub> hub) =>
        new(ctx, hub, new UserLocaleResolver(ctx), CreateTextFactory());

    public static INotificationTextFactory CreateTextFactory()
    {
        var resourcesPath = Path.Combine(GetRepoRoot(), "Services", "BadgeService", "Resources");
        using var fileProvider = new PhysicalFileProvider(resourcesPath);
        var store = JsonResourceStore.Load(fileProvider, "", throwOnDuplicateKeys: false, NullLogger.Instance);
        return new NotificationTextFactory(store, NullLogger<NotificationTextFactory>.Instance);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Services", "BadgeService", "Resources")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
    }
}
