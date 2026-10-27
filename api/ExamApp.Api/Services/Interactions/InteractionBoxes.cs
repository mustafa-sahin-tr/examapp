using System.Text.Json;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// review #576 (L2): etkileşim planlarındaki kutu (<c>{x,y,width,height}</c>, soru bölgesine göre göreli piksel) için sınırlı kural:
/// yalnız bu dört alan, sonlu sayılar, <c>0 ≤ x, y</c>, <c>width, height ≥ 1</c>, <c>x + width ≤ 100000</c> ve
/// <c>y + height ≤ 100000</c>. dragPlace ve match kullanır; matrixChoice/slotFill bilerek eski (gevşek) kuralda kalır — bölge
/// dışına taşan (negatif göreli) kutular kayıtlı sorularda olabilir, yeniden kayıt bozulmasın.
/// </summary>
public static class InteractionBoxes
{
    public const double MaxExtent = 100_000;
    public const double MinSize = 1;

    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "x", "y", "width", "height" };

    public static bool IsValid(JsonElement box)
    {
        if (box.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in box.EnumerateObject())
        {
            if (!Keys.Contains(property.Name))
                return false;
        }
        return TryNumber(box, "x", out var x) && x >= 0
            && TryNumber(box, "y", out var y) && y >= 0
            && TryNumber(box, "width", out var width) && width >= MinSize && x + width <= MaxExtent
            && TryNumber(box, "height", out var height) && height >= MinSize && y + height <= MaxExtent;
    }

    private static bool TryNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDouble(out value) && double.IsFinite(value);
    }
}
