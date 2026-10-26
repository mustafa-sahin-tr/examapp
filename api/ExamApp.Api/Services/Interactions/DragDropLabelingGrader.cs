using System.IO;
using System.Text;
using System.Text.Json;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #571: <c>dragDropLabeling</c> kontrolcüsü — hedef (drop zone) başına doğru/yanlış.
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"dragDropLabeling",dropZones:[{id,...}],draggables:[{id,label}]}</c>.</item>
/// <item>Çözüm (#570, <c>Question.InteractionSolution</c>): <c>{"placements":[{"draggableId","dropZoneId"}]}</c>.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"dragDropLabeling",placements:[{dropZoneId,draggableId}]}</c>.</item>
/// </list>
/// Satırlar = çözümdeki her hedef (boş bırakılan ya da başka kart konan hedef yanlış) + öğrencinin doldurduğu ama çözümde
/// olmayan her hedef (hep yanlış). Geri bildirim yalnız hedef id'si + doğru/yanlış taşır; beklenen kart ASLA yazılmaz
/// (çözüm sızmaz). Çözümü olmayan soru puanlanamaz (0/0).
/// <para>
/// Bilinen sızıntı (review #571, kabul edildi): satırlar çözümdeki hedeflerden kurulduğu için geri bildirim HANGİ hedeflerin
/// cevap hedefi olduğunu açığa çıkarır (hangi kartın değil). Kabul gerekçesi: etkinlikte çözüm zaten bir kontrolden sonra
/// açılabilir, testte geri bildirim yalnız test bittikten sonra gösterilir. Geri bildirim bir gün test SÜRERKEN gösterilirse
/// satırlar plandaki TÜM hedeflerden kurulmalı.
/// </para>
/// </summary>
public sealed class DragDropLabelingGrader : IInteractionGrader
{
    public const string Type = "dragDropLabeling";

    /// <summary>Öğrenci payload'unun üst sınırı (karakter).</summary>
    public const int MaxPayloadLength = InteractionPayload.MaxLength;

    // Payload (istemci girdisi) katı: yinelenen anahtar yasak (InteractionPlanSolution yazma kuralıyla aynı gerekçe).
    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 16, AllowDuplicateProperties = false };

    // Plan/çözüm (kayıtlı veri) okuması toleranslı: eski satır yinelenen anahtar taşıyabilir, okuma istisna atmamalı.
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    public string InteractionType => Type;

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > MaxPayloadLength)
            return false;
        if (!TryParsePayload(payload, out var placements))
            return false;
        if (placements.Count == 0)
            return true;
        if (!TryReadPlanIds(plan, out var zoneIds, out var draggableIds))
            return false;

        var seenZones = new HashSet<string>(StringComparer.Ordinal);
        var seenDraggables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (zoneId, draggableId) in placements)
        {
            if (!zoneIds.Contains(zoneId) || !draggableIds.Contains(draggableId))
                return false;
            // Bir hedefe en çok bir kart, bir kart en çok bir hedefe (UI bileşeniyle aynı kural).
            if (!seenZones.Add(zoneId) || !seenDraggables.Add(draggableId))
                return false;
        }

        return true;
    }

    /// <summary>Yerleşimsiz payload (<c>placements: []</c>) = cevap yok → null (boş sayılır, yanlış değil).</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var placements) && placements.Count == 0 ? null : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        var expected = ReadSolution(input.Solution);
        if (expected.Count == 0)
            return InteractionGradeResult.Ungradable;

        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        var actualOrder = new List<string>();
        if (input.Payload != null && TryParsePayload(input.Payload, out var placements))
        {
            foreach (var (zoneId, draggableId) in placements)
            {
                if (actual.TryAdd(zoneId, draggableId))
                    actualOrder.Add(zoneId);
            }
        }

        var rows = new List<(string Id, bool Correct)>();
        foreach (var (zoneId, draggables) in expected)
        {
            var correct = actual.TryGetValue(zoneId, out var placed) && draggables.Contains(placed);
            rows.Add((zoneId, correct));
        }

        var expectedZones = expected.Select(e => e.ZoneId).ToHashSet(StringComparer.Ordinal);
        foreach (var zoneId in actualOrder)
        {
            if (!expectedZones.Contains(zoneId))
                rows.Add((zoneId, false));
        }

        return InteractionGradeResult.Of(rows.Count(r => r.Correct), rows.Count, WriteFeedback(rows));
    }

    /// <summary>Katı payload ayrıştırma: nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, <c>placements</c> dizisi.</summary>
    private static bool TryParsePayload(string payload, out List<(string ZoneId, string DraggableId)> placements)
    {
        placements = new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("placements", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryNonEmptyString(item, "dropZoneId", out var zoneId)
                    || !TryNonEmptyString(item, "draggableId", out var draggableId))
                    return false;
                placements.Add((zoneId, draggableId));
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            placements.Clear();
            return false;
        }
    }

    private static bool TryNonEmptyString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return false;
        value = prop.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    /// <summary>Plandaki hedef ve kart id'leri; plan ayrıştırılamazsa ya da dizi yoksa false.</summary>
    private static bool TryReadPlanIds(string? plan, out HashSet<string> zoneIds, out HashSet<string> draggableIds)
    {
        zoneIds = new HashSet<string>(StringComparer.Ordinal);
        draggableIds = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(plan, ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            return CollectIds(root, "dropZones", zoneIds) && CollectIds(root, "draggables", draggableIds);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool CollectIds(JsonElement root, string arrayName, HashSet<string> ids)
    {
        if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && TryReadId(item, "id", out var id))
                ids.Add(id);
        }
        return true;
    }

    /// <summary>Kayıtlı veride id string ya da sayı olabilir (sayı ham metniyle karşılaştırılır).</summary>
    private static bool TryReadId(JsonElement obj, string name, out string id)
    {
        id = string.Empty;
        if (!obj.TryGetProperty(name, out var prop))
            return false;
        id = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? string.Empty,
            JsonValueKind.Number => prop.GetRawText(),
            _ => string.Empty
        };
        return id.Length > 0;
    }

    /// <summary>
    /// Çözümdeki hedefler (ilk görülme sırasıyla) ve her hedefe kabul edilen kart(lar). Toleranslı: bozuk çözüm = boş liste
    /// (puanlanamaz). Kartı boş hedef de satırdır (hiçbir yerleşim onu doğru yapmaz).
    /// </summary>
    private static List<(string ZoneId, HashSet<string> Draggables)> ReadSolution(string? solution)
    {
        var result = new List<(string ZoneId, HashSet<string> Draggables)>();
        if (string.IsNullOrWhiteSpace(solution))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("placements", out var list) || list.ValueKind != JsonValueKind.Array)
                return result;

            var byZone = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !TryReadId(item, "dropZoneId", out var zoneId))
                    continue;
                if (!byZone.TryGetValue(zoneId, out var draggables))
                {
                    draggables = new HashSet<string>(StringComparer.Ordinal);
                    byZone[zoneId] = draggables;
                    result.Add((zoneId, draggables));
                }
                if (TryReadId(item, "draggableId", out var draggableId))
                    draggables.Add(draggableId);
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new List<(string, HashSet<string>)>();
        }
    }

    private static string WriteFeedback(IReadOnlyList<(string Id, bool Correct)> rows)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("rows");
            foreach (var (id, correct) in rows)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteBoolean("correct", correct);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
