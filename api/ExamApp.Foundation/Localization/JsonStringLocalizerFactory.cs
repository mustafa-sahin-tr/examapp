using System;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Foundation.Localization;

/// <summary>
/// <see cref="IStringLocalizerFactory"/> uygulaması. Tüm çeviriler tek birleşik sözlükte
/// olduğu için hangi tip/baseName istenirse istensin aynı localizer döner — bu sayede
/// <c>AddDataAnnotationsLocalization</c> gibi factory üzerinden çalışan MVC parçaları da
/// aynı JSON sözlüğünü kullanır.
/// </summary>
public sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly JsonStringLocalizer _localizer;

    public JsonStringLocalizerFactory(JsonResourceStore store, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _localizer = new JsonStringLocalizer(store, loggerFactory?.CreateLogger<JsonStringLocalizer>());
    }

    public IStringLocalizer Create(Type resourceSource) => _localizer;

    public IStringLocalizer Create(string baseName, string location) => _localizer;
}
