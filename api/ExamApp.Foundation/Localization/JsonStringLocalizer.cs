using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Foundation.Localization;

/// <summary>
/// <see cref="JsonResourceStore"/> üzerinde çalışan <see cref="IStringLocalizer"/> uygulaması
/// (issue #184). Aktif dil <see cref="CultureInfo.CurrentUICulture"/>'dan okunur; bunu istek
/// başına ASP.NET Core'un RequestLocalization middleware'i ayarlar (bkz. exam API Program.cs, #181).
///
/// <para>Anahtar bulunamazsa <see cref="LocalizedString.ResourceNotFound"/> <c>true</c> döner ve
/// değer olarak anahtarın kendisi verilir — yanıt hiçbir zaman boş kalmaz. Eksik anahtar için
/// uyarı log'u anahtar başına yalnızca bir kez yazılır (istek başına log şişmesin diye).</para>
/// </summary>
public class JsonStringLocalizer : IStringLocalizer
{
    private static readonly ConcurrentDictionary<string, byte> WarnedKeys = new(StringComparer.Ordinal);

    private readonly JsonResourceStore _store;
    private readonly ILogger _logger;

    public JsonStringLocalizer(JsonResourceStore store, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// <c>null</c> ise <see cref="CultureInfo.CurrentUICulture"/> kullanılır. Alt sınıfların
    /// (ör. istek bağlamı dışında sabit dile kilitlenen fallback) ezmesi için.
    /// </summary>
    protected virtual CultureInfo? Culture => null;

    public LocalizedString this[string name] => Resolve(name, arguments: null);

    public LocalizedString this[string name, params object[] arguments] => Resolve(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => _store.GetAll(Culture ?? CultureInfo.CurrentUICulture, includeParentCultures)
            .Select(pair => new LocalizedString(pair.Key, pair.Value, resourceNotFound: false))
            .ToList();

    private LocalizedString Resolve(string name, object[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(name);

        var culture = Culture ?? CultureInfo.CurrentUICulture;
        var found = _store.TryGet(culture, name, out var template);

        if (!found && WarnedKeys.TryAdd(name, 0))
        {
            _logger.LogWarning(
                "Çeviri anahtarı bulunamadı: '{Key}' (kültür {Culture}). Anahtarın kendisi döndürülüyor.",
                name, culture.Name);
        }

        var value = arguments is { Length: > 0 }
            ? string.Format(culture, template, arguments)
            : template;

        return new LocalizedString(name, value, resourceNotFound: !found);
    }
}

/// <summary>
/// <c>IStringLocalizer&lt;T&gt;</c> uyumlu ince sarmalayıcı. Tüm anahtarlar tek birleşik
/// sözlükte olduğu için <typeparamref name="T"/> yalnızca DI'da tip ayrımı sağlar; pratikte
/// her yerde <c>IStringLocalizer&lt;Messages&gt;</c> enjekte edilir.
/// </summary>
public sealed class JsonStringLocalizer<T> : JsonStringLocalizer, IStringLocalizer<T>
{
    public JsonStringLocalizer(JsonResourceStore store, ILoggerFactory? loggerFactory = null)
        : base(store, loggerFactory?.CreateLogger(typeof(JsonStringLocalizer<T>)))
    {
    }
}
