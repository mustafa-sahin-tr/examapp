using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// review #574 (L2) / #575 (L2): etkileşim planlarındaki id'ler (satır, kolon, kutu, seçenek) — plan, çözüm ve payload'da aynı
/// kural: <c>^[A-Za-z0-9_-]{1,32}$</c> ve JS nesne anahtarı olarak tehlikeli ayrılmış adlar (<c>__proto__</c>,
/// <c>constructor</c>, <c>prototype</c>; büyük/küçük harf duyarlı) yasak — istemci id'leri düz nesnede anahtar olarak kullanır.
/// </summary>
public static class InteractionIds
{
    public const int MaxLength = 32;

    private static readonly Regex Pattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "__proto__", "constructor", "prototype" };

    public static bool IsValid(string? id) => id != null && Pattern.IsMatch(id) && !Reserved.Contains(id);
}
