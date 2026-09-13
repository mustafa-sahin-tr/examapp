namespace ExamApp.Foundation.Localization;

/// <summary>
/// Tüm client'a giden mesajların ortak "marker" tipi (issue #184). Controller ve
/// servislerde her zaman <c>IStringLocalizer&lt;Messages&gt;</c> enjekte edilir —
/// controller başına ayrı bir localizer tipi yoktur, çünkü
/// <see cref="JsonResourceStore"/> tüm <c>Resources/*.{culture}.json</c> dosyalarını
/// kültür başına TEK sözlükte birleştirir. Böylece her alan kendi dosyasına sahip olur
/// (paralel geliştirmede çakışma olmaz) ama arama tarafı tek ve basit kalır.
/// </summary>
public sealed class Messages
{
    private Messages()
    {
    }
}
