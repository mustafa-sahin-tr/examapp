using System.IO;
using System.Text;
using System.Text.Json;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #585: <c>mazeTree</c> kontrolcüsü — bileşen G'nin yol oyunları tek motor: D/Y dallanan ağaç (<c>tree</c>), odalı labirent
/// (<c>rooms</c>), ışıklı pano (<c>board</c>) ve ırmak taşları (<c>stones</c>).
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"mazeTree",variant,start?,nodes:[{id,label?,end?,row?,col?}],edges:[{id,from,to,label?}]}</c>.
/// Düğüm = ifade / oda / pano satırı / taş; kenar = seçenek (D/Y dalı, kapı, pano kutusu). Seçilen kenar öğrenciyi
/// <c>to</c> düğümüne götürür; <c>rooms</c>'ta kapılar iki yönlüdür. <c>end</c> düğümü (çıkış, bitiş) yolu bitirir.
/// <c>stones</c> yolsuzdur: kenar ve başlangıç yok, öğrenci taşları işaretler.</item>
/// <item>Çözüm (#570): yol varyantlarında <c>{"choices":{"n1":"e2",...}}</c> — düğüm başına doğru kenar (rota dışı düğümler de
/// olabilir: ağaçta her ifadenin doğrusu); rota = başlangıçtan seçimleri izleyerek bir <c>end</c> düğümüne varan yol.
/// <c>stones</c>: <c>{"marked":["s2",...]}</c> — işaretlenmesi gereken taşlar.</item>
/// <item>Öğrenci payload'u: yol varyantlarında <c>{version:1,type:"mazeTree",path:["e1","e4"]}</c> (başlangıçtan seçilen
/// kenarlar, kesintisiz; <c>end</c>'den sonra adım yok); <c>stones</c>'ta <c>{version:1,type:"mazeTree",marked:["s1"]}</c>.</item>
/// </list>
/// Yol kontrolü ilk sapmada durur: satırlar = öğrencinin geçtiği düğümler (sapma düğümü yanlış), toplam = rota uzunluğu, doğru ⇔
/// rotanın sonuna varıldı. Geri bildirim <c>{"rows":[{id,correct}],"status":"reached|deviated|incomplete","at"}</c> yalnız
/// öğrencinin kendi yolunu anlatır; doğru kenar ve rota ASLA yazılmaz. Taşlarda satır = taş (plan sırası), boş cevap hepsi
/// yanlış, satırlar ilk yanlış taşta durur; <c>at</c> = geçiş sırasındaki ilk yanlış taş. Çözümü olmayan soru puanlanamaz (0/0).
/// </summary>
public sealed class MazeTreeGrader : IInteractionGrader
{
    public const string Type = "mazeTree";

    public const string VariantTree = "tree";
    public const string VariantRooms = "rooms";
    public const string VariantBoard = "board";
    public const string VariantStones = "stones";

    public const int MaxNodes = 64;
    public const int MaxEdges = 160;
    public const int MaxLabelLength = 300;
    public const int MaxTreeDepth = 5;
    public const int MaxTreeBranches = 4;
    public const int MaxBoardRows = 12;
    public const int MaxBoardBoxes = 6;
    public const int MaxGridSize = 8;
    public const int MinStones = 2;
    public const int MaxStones = 40;

    /// <summary>Yol payload'u en çok bu kadar adım (odalı labirentte geri dönüş ve döngü olabilir).</summary>
    public const int MaxPathLength = 2 * MaxNodes;

    public const string StatusReached = "reached";
    public const string StatusDeviated = "deviated";
    public const string StatusIncomplete = "incomplete";

    private static readonly HashSet<string> Variants = new(StringComparer.Ordinal) { VariantTree, VariantRooms, VariantBoard, VariantStones };
    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "variant", "start", "nodes", "edges" };
    private static readonly HashSet<string> NodeKeys = new(StringComparer.Ordinal) { "id", "label", "end", "row", "col" };
    private static readonly HashSet<string> EdgeKeys = new(StringComparer.Ordinal) { "id", "from", "to", "label" };

    // Payload (istemci girdisi) katı: yinelenen anahtar yasak.
    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 16, AllowDuplicateProperties = false };

    // Plan/çözüm (kayıtlı veri) okuması toleranslı: okuma istisna atmamalı.
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    // Yazma yolu (ValidateDefinition) katı.
    private static readonly JsonDocumentOptions DefinitionOptions = new() { MaxDepth = 64, AllowDuplicateProperties = false };

    public string InteractionType => Type;

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        if (!TryParsePayload(payload, out var answer))
            return false;
        if (answer.IsEmpty)
            return true;
        if (!TryReadPlan(plan, ReadOptions, strict: false, out var maze))
            return false;

        if (maze.IsStones)
            return answer.Path == null && answer.Marked!.All(maze.Nodes.ContainsKey);
        return answer.Marked == null && TryWalk(maze, answer.Path!, out _);
    }

    /// <summary>Adımsız / işaretsiz payload = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var answer) && answer.IsEmpty ? null : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var maze))
            return InteractionGradeResult.Ungradable;
        var answer = input.Payload != null && TryParsePayload(input.Payload, out var parsed) ? parsed : Answer.Empty;

        if (maze.IsStones)
        {
            if (!TryReadMarkedSolution(input.Solution, maze, out var expected) || maze.Order.Count == 0)
                return InteractionGradeResult.Ungradable;
            // Yol varyantları gibi ilk yanlış taşta durur: taş ikili olduğu için bütün satırlar çözümü ele verirdi.
            var all = StoneRows(maze, expected, answer);
            var firstWrong = all.FindIndex(r => !r.Correct);
            var rows = firstWrong < 0 ? all : all.Take(firstWrong + 1).ToList();
            var at = firstWrong < 0 ? null : all[firstWrong].Id;
            return InteractionGradeResult.Of(rows.Count(r => r.Correct), all.Count,
                WriteFeedback(rows, at == null ? StatusReached : StatusDeviated, at));
        }

        if (!TryReadChoices(input.Solution, maze, out var choices) || !TryRoute(maze, choices, out var route))
            return InteractionGradeResult.Ungradable;
        var walk = Follow(maze, choices, answer.Path ?? []);
        return InteractionGradeResult.Of(walk.Steps.Count(s => s.Correct), route.Count,
            WriteFeedback(walk.Steps.Select(s => (s.NodeId, s.Correct)).ToList(), walk.Status, walk.At));
    }

    /// <summary>
    /// issue #584: yol varyantlarında öğe = rotadaki karar düğümü (rota sırası), cevap = öğrencinin o düğümde seçtiği kenar (yalnız
    /// ilk sapmaya kadar geçtiği düğümler; sonrası cevapsız). Taşlarda öğe = taş, cevap = işaretli / işaretsiz.
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var maze))
            return [];
        var answer = input.Payload != null && TryParsePayload(input.Payload, out var parsed) ? parsed : Answer.Empty;

        if (maze.IsStones)
        {
            if (!TryReadMarkedSolution(input.Solution, maze, out var expected))
                return [];
            var marked = answer.Marked?.ToHashSet(StringComparer.Ordinal);
            return maze.Order.Select((id, i) =>
            {
                var isMarked = marked?.Contains(id);
                return new InteractionItemResponse(id, LabelOf(maze.Nodes[id], i),
                    marked != null && isMarked == expected.Contains(id),
                    marked == null ? null : isMarked == true ? "marked" : "unmarked",
                    marked == null ? null : isMarked == true ? "✓" : "–");
            }).ToList();
        }

        if (!TryReadChoices(input.Solution, maze, out var choices) || !TryRoute(maze, choices, out var route))
            return [];
        var walk = Follow(maze, choices, answer.Path ?? []);
        var chosenAt = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var step in walk.Steps)
            chosenAt.TryAdd(step.NodeId, step.EdgeId);

        var items = new List<InteractionItemResponse>(route.Count);
        var nodeId = maze.Start!;
        foreach (var edgeId in route)
        {
            var chosen = chosenAt.GetValueOrDefault(nodeId);
            items.Add(new InteractionItemResponse(nodeId, LabelOf(maze.Nodes[nodeId], maze.Order.IndexOf(nodeId)),
                chosen != null && string.Equals(chosen, edgeId, StringComparison.Ordinal),
                chosen,
                chosen == null ? null : EdgeLabel(maze.Edges[chosen])));
            nodeId = maze.Edges[edgeId].Target(nodeId)!;
        }
        return items;
    }

    /// <summary>
    /// Kayıt kuralı (issue #585): plan zorunlu ve varyantın yapısına uygun; çözüm boşsa geçerli (taslak). Doluysa yol varyantlarında
    /// her seçim bir karar düğümünden kullanılabilir bir kenar ve başlangıçtan seçimler bir <c>end</c> düğümüne döngüsüz varır;
    /// taşlarda işaretli taşlar plandaki farklı taşlardır.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var maze))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (maze.IsStones)
            {
                if (!HasOnlyKeys(root, ["marked"]) || !root.TryGetProperty("marked", out var marked)
                    || marked.ValueKind != JsonValueKind.Array)
                    return false;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in marked.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } id
                        || !maze.Nodes.ContainsKey(id) || !seen.Add(id))
                        return false;
                }
                // Hiç işaretsiz çözüm çözülemezdi: işaretsiz cevap boş cevaptır (Normalize → null).
                return seen.Count > 0;
            }

            if (!HasOnlyKeys(root, ["choices"]) || !root.TryGetProperty("choices", out var choicesElement)
                || choicesElement.ValueKind != JsonValueKind.Object)
                return false;
            var choices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in choicesElement.EnumerateObject())
            {
                if (!maze.Nodes.TryGetValue(property.Name, out var node) || node.End
                    || property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { } edgeId
                    || !maze.Edges.TryGetValue(edgeId, out var edge) || edge.Target(node.Id) == null)
                    return false;
                choices[node.Id] = edgeId;
            }
            return TryRoute(maze, choices, out _);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Model ----

    private sealed record MazeNode(string Id, string? Label, bool End, int? Row, int? Col);

    private sealed record MazeEdge(string Id, string From, string To, string? Label, bool Undirected)
    {
        /// <summary><paramref name="nodeId"/>'den bu kenarla varılan düğüm; kenar o düğümden kullanılamıyorsa null.</summary>
        public string? Target(string? nodeId)
            => nodeId == From ? To : Undirected && nodeId == To ? From : null;
    }

    private sealed record Maze(
        string Variant, string? Start, Dictionary<string, MazeNode> Nodes, List<string> Order, Dictionary<string, MazeEdge> Edges)
    {
        public bool IsStones => Variant == VariantStones;
    }

    private sealed record Answer(List<string>? Path, List<string>? Marked)
    {
        public static Answer Empty { get; } = new(null, null);

        public bool IsEmpty => (Path?.Count ?? 0) == 0 && (Marked?.Count ?? 0) == 0;
    }

    private sealed record Step(string NodeId, string EdgeId, bool Correct);

    private sealed record Walk(List<Step> Steps, string Status, string? At);

    // ---- Yol ----

    /// <summary>Payload yolu plana göre kesintisiz mi (her kenar bulunulan düğümden kullanılabilir, <c>end</c>'den sonra adım yok)?</summary>
    private static bool TryWalk(Maze maze, List<string> path, out string? last)
    {
        last = maze.Start;
        if (last == null || path.Count > MaxPathLength)
            return false;
        foreach (var edgeId in path)
        {
            if (maze.Nodes[last].End || !maze.Edges.TryGetValue(edgeId, out var edge) || edge.Target(last) is not { } next)
                return false;
            last = next;
        }
        return true;
    }

    /// <summary>Öğrenci yolunu çözümle karşılaştırır; ilk sapmada durur. Geçersiz adımda (bozuk kayıt) eksik sayılır.</summary>
    private static Walk Follow(Maze maze, Dictionary<string, string> choices, List<string> path)
    {
        var steps = new List<Step>();
        var current = maze.Start!;
        foreach (var edgeId in path.Take(MaxPathLength))
        {
            if (maze.Nodes[current].End || !maze.Edges.TryGetValue(edgeId, out var edge) || edge.Target(current) is not { } next)
                break;
            var correct = choices.TryGetValue(current, out var expected) && string.Equals(expected, edgeId, StringComparison.Ordinal);
            steps.Add(new Step(current, edgeId, correct));
            if (!correct)
                return new Walk(steps, StatusDeviated, current);
            current = next;
        }
        return maze.Nodes[current].End
            ? new Walk(steps, StatusReached, current)
            : new Walk(steps, StatusIncomplete, current);
    }

    /// <summary>Rota: başlangıçtan seçimleri izleyen kenarlar; bir <c>end</c> düğümüne döngüsüz varmazsa false.</summary>
    private static bool TryRoute(Maze maze, Dictionary<string, string> choices, out List<string> route)
    {
        route = new List<string>();
        if (maze.Start == null)
            return false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = maze.Start;
        while (!maze.Nodes[current].End)
        {
            if (!visited.Add(current) || !choices.TryGetValue(current, out var edgeId)
                || !maze.Edges.TryGetValue(edgeId, out var edge) || edge.Target(current) is not { } next)
                return false;
            route.Add(edgeId);
            current = next;
        }
        return route.Count > 0;
    }

    // ---- Taşlar ----

    private static List<(string Id, bool Correct)> StoneRows(Maze maze, HashSet<string> expected, Answer answer)
    {
        // Boş cevap: hiçbir taş doğru sayılmaz ("hiçbir şey yapmamak" kısmen doğru görünmesin).
        if (answer.Marked == null)
            return maze.Order.Select(id => (id, false)).ToList();
        var marked = answer.Marked.ToHashSet(StringComparer.Ordinal);
        return maze.Order.Select(id => (id, marked.Contains(id) == expected.Contains(id))).ToList();
    }

    // ---- Okuma ----

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): düğümler, kenarlar, varyant ve başlangıç; bozuk öğe atlanır. true
    /// (kayıt): tam şema ve varyantın yapı kuralları (bkz. <see cref="ValidateStructure"/>).
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Maze maze)
    {
        maze = new Maze(VariantTree, null, new Dictionary<string, MazeNode>(StringComparer.Ordinal), new List<string>(),
            new Dictionary<string, MazeEdge>(StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(plan, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (!root.TryGetProperty("variant", out var variantElement) || variantElement.ValueKind != JsonValueKind.String
                || variantElement.GetString() is not { } variant || !Variants.Contains(variant))
                return false;

            if (strict)
            {
                if (!HasOnlyKeys(root, RootKeys))
                    return false;
                if (root.TryGetProperty("version", out var version)
                    && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                    return false;
            }

            var nodes = new Dictionary<string, MazeNode>(StringComparer.Ordinal);
            var order = new List<string>();
            if (!root.TryGetProperty("nodes", out var nodesElement) || nodesElement.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var item in nodesElement.EnumerateArray())
            {
                if (!TryReadNode(item, strict, out var node) || nodes.ContainsKey(node.Id))
                {
                    if (strict) return false;
                    continue;
                }
                nodes[node.Id] = node;
                order.Add(node.Id);
            }

            var undirected = variant == VariantRooms;
            var edges = new Dictionary<string, MazeEdge>(StringComparer.Ordinal);
            if (root.TryGetProperty("edges", out var edgesElement) && edgesElement.ValueKind != JsonValueKind.Null)
            {
                if (edgesElement.ValueKind != JsonValueKind.Array)
                {
                    if (strict) return false;
                }
                else
                {
                    foreach (var item in edgesElement.EnumerateArray())
                    {
                        if (!TryReadEdge(item, strict, undirected, out var edge) || edges.ContainsKey(edge.Id) || nodes.ContainsKey(edge.Id)
                            || !nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To) || edge.From == edge.To)
                        {
                            if (strict) return false;
                            continue;
                        }
                        edges[edge.Id] = edge;
                    }
                }
            }

            string? start = null;
            if (root.TryGetProperty("start", out var startElement) && startElement.ValueKind != JsonValueKind.Null)
            {
                if (startElement.ValueKind != JsonValueKind.String)
                {
                    if (strict) return false;
                }
                else
                {
                    start = startElement.GetString();
                }
            }

            if (variant == VariantStones)
            {
                if (start != null || edges.Count > 0)
                {
                    if (strict) return false;
                    start = null;
                    edges.Clear();
                }
            }
            else if (start == null || !nodes.TryGetValue(start, out var startNode) || startNode.End)
            {
                return false;
            }

            maze = new Maze(variant, start, nodes, order, edges);
            return !strict || ValidateStructure(maze);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Varyant yapı kuralları (kayıt):
    /// <list type="bullet">
    /// <item>Ortak: 1..<see cref="MaxNodes"/> düğüm, en çok <see cref="MaxEdges"/> kenar; <c>end</c> olmayan düğümün etiketi (pano satırı hariç), her
    /// kenarın etiketi zorunlu (taşlarda her taşın etiketi); bir düğümün kenar etiketleri tekil; her düğüm başlangıçtan erişilebilir.</item>
    /// <item><c>tree</c>: ağaç (başlangıç dışındaki her düğüme tam bir kenar girer), karar düğümünde 2..4 dal, <c>end</c> düğümünde
    /// dal yok, derinlik ≤ <see cref="MaxTreeDepth"/>; konum alanı yok.</item>
    /// <item><c>board</c>: zincir — her satırın 2..6 kutusu aynı sonraki düğüme gider, son satır tek <c>end</c> düğümüne; 1..12 satır.</item>
    /// <item><c>rooms</c>: her oda tekil <c>row</c>/<c>col</c>'da (0..7), kapılar komşu (çapraz dahil) iki oda arasında, aynı iki oda
    /// arasında tek kapı, bir dörtlüde en çok bir çapraz kapı; en az bir <c>end</c> oda.</item>
    /// <item><c>stones</c>: 2..40 taş, kenar / başlangıç / <c>end</c> / konum yok.</item>
    /// </list>
    /// </summary>
    private static bool ValidateStructure(Maze maze)
    {
        if (maze.Nodes.Count is 0 or > MaxNodes || maze.Edges.Count > MaxEdges)
            return false;

        if (maze.IsStones)
        {
            return maze.Nodes.Count is >= MinStones and <= MaxStones
                && maze.Nodes.Values.All(n => !n.End && n.Row == null && n.Col == null && !string.IsNullOrWhiteSpace(n.Label));
        }

        var rooms = maze.Variant == VariantRooms;
        // Pano satır adı (ör. "Gaz") isteğe bağlı; ifade / oda metni zorunlu.
        if (maze.Variant != VariantBoard && maze.Nodes.Values.Any(n => !n.End && string.IsNullOrWhiteSpace(n.Label)))
            return false;
        if (maze.Edges.Values.Any(e => string.IsNullOrWhiteSpace(e.Label)))
            return false;
        if (!rooms && maze.Nodes.Values.Any(n => n.Row != null || n.Col != null))
            return false;

        // Düğüm başına kullanılabilir kenarlar (rooms: iki yön).
        var outgoing = maze.Nodes.Keys.ToDictionary(id => id, _ => new List<MazeEdge>(), StringComparer.Ordinal);
        foreach (var edge in maze.Edges.Values)
        {
            outgoing[edge.From].Add(edge);
            if (rooms)
                outgoing[edge.To].Add(edge);
        }
        foreach (var (_, list) in outgoing)
        {
            var labels = list.Select(e => e.Label!.Trim()).ToList();
            if (labels.Distinct(StringComparer.Ordinal).Count() != labels.Count)
                return false;
        }

        // Erişilebilirlik (BFS, end düğümünden çıkılmaz).
        var reached = new HashSet<string>(StringComparer.Ordinal) { maze.Start! };
        var queue = new Queue<string>([maze.Start!]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (maze.Nodes[id].End)
                continue;
            foreach (var edge in outgoing[id])
            {
                if (edge.Target(id) is { } next && reached.Add(next))
                    queue.Enqueue(next);
            }
        }
        if (reached.Count != maze.Nodes.Count)
            return false;

        return maze.Variant switch
        {
            VariantTree => ValidateTree(maze, outgoing),
            VariantBoard => ValidateBoard(maze, outgoing),
            VariantRooms => ValidateRooms(maze),
            _ => false
        };
    }

    private static bool ValidateTree(Maze maze, Dictionary<string, List<MazeEdge>> outgoing)
    {
        var incoming = maze.Edges.Values.GroupBy(e => e.To, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (incoming.ContainsKey(maze.Start!))
            return false;
        foreach (var node in maze.Nodes.Values)
        {
            if (node.Id != maze.Start && incoming.GetValueOrDefault(node.Id) != 1)
                return false;
            var branches = outgoing[node.Id].Count;
            if (node.End ? branches != 0 : branches is < 2 or > MaxTreeBranches)
                return false;
        }
        return Depth(maze.Start!) <= MaxTreeDepth;

        int Depth(string id) => outgoing[id].Count == 0 ? 0 : 1 + outgoing[id].Max(e => Depth(e.To));
    }

    private static bool ValidateBoard(Maze maze, Dictionary<string, List<MazeEdge>> outgoing)
    {
        var rows = 0;
        var current = maze.Start!;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!maze.Nodes[current].End)
        {
            var boxes = outgoing[current];
            if (!visited.Add(current) || boxes.Count is < 2 or > MaxBoardBoxes)
                return false;
            var next = boxes[0].To;
            if (boxes.Any(b => b.To != next))
                return false;
            rows++;
            current = next;
        }
        // Zincir tüm düğümleri kapsar (erişilebilirlikle birlikte: yan dal yok), tek bitiş var ve bitişten kenar çıkmaz.
        return rows is >= 1 and <= MaxBoardRows && visited.Count + 1 == maze.Nodes.Count && outgoing[current].Count == 0;
    }

    private static bool ValidateRooms(Maze maze)
    {
        var cells = new HashSet<(int, int)>();
        foreach (var node in maze.Nodes.Values)
        {
            if (node.Row is not { } row || node.Col is not { } col || row is < 0 or >= MaxGridSize || col is < 0 or >= MaxGridSize
                || !cells.Add((row, col)))
                return false;
        }
        if (!maze.Nodes.Values.Any(n => n.End))
            return false;

        var pairs = new HashSet<(string, string)>();
        foreach (var edge in maze.Edges.Values)
        {
            var a = maze.Nodes[edge.From];
            var b = maze.Nodes[edge.To];
            if (Math.Abs(a.Row!.Value - b.Row!.Value) > 1 || Math.Abs(a.Col!.Value - b.Col!.Value) > 1)
                return false;
            var key = string.CompareOrdinal(a.Id, b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
            if (!pairs.Add(key))
                return false;
        }

        // Bir dörtlüde iki çapraz kapı aynı köşede üst üste çizilirdi.
        var position = maze.Nodes.Values.ToDictionary(n => (n.Row!.Value, n.Col!.Value), n => n.Id);
        string? At(int r, int c) => position.GetValueOrDefault((r, c));
        bool Door(string? a, string? b) => a != null && b != null
            && pairs.Contains(string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a));
        foreach (var node in maze.Nodes.Values)
        {
            int r = node.Row!.Value, c = node.Col!.Value;
            if (Door(node.Id, At(r + 1, c + 1)) && Door(At(r, c + 1), At(r + 1, c)))
                return false;
        }
        return true;
    }

    private static bool TryReadNode(JsonElement item, bool strict, out MazeNode node)
    {
        node = new MazeNode(string.Empty, null, false, null, null);
        if (item.ValueKind != JsonValueKind.Object || !TryReadString(item, "id", out var id) || !InteractionIds.IsValid(id))
            return false;
        if (strict && !HasOnlyKeys(item, NodeKeys))
            return false;
        if (!TryReadLabel(item, strict, out var label))
            return false;

        var end = false;
        if (item.TryGetProperty("end", out var endElement) && endElement.ValueKind != JsonValueKind.Null)
        {
            if (endElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                if (strict) return false;
            }
            else
            {
                end = endElement.GetBoolean();
            }
        }

        if (!TryReadInt(item, "row", strict, out var row) || !TryReadInt(item, "col", strict, out var col))
            return false;
        node = new MazeNode(id, label, end, row, col);
        return true;
    }

    private static bool TryReadEdge(JsonElement item, bool strict, bool undirected, out MazeEdge edge)
    {
        edge = new MazeEdge(string.Empty, string.Empty, string.Empty, null, undirected);
        if (item.ValueKind != JsonValueKind.Object || !TryReadString(item, "id", out var id) || !InteractionIds.IsValid(id)
            || !TryReadString(item, "from", out var from) || !TryReadString(item, "to", out var to))
            return false;
        if (strict && !HasOnlyKeys(item, EdgeKeys))
            return false;
        if (!TryReadLabel(item, strict, out var label))
            return false;
        edge = new MazeEdge(id, from, to, label, undirected);
        return true;
    }

    private static bool TryReadLabel(JsonElement item, bool strict, out string? label)
    {
        label = null;
        if (!item.TryGetProperty("label", out var element) || element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.String)
            return !strict;
        label = element.GetString();
        return !strict || (label?.Length ?? 0) <= MaxLabelLength;
    }

    private static bool TryReadInt(JsonElement item, string name, bool strict, out int? value)
    {
        value = null;
        if (!item.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed))
        {
            value = parsed;
            return true;
        }
        return !strict;
    }

    private static bool TryReadString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool HasOnlyKeys(JsonElement obj, IReadOnlySet<string> allowed)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                return false;
        }
        return true;
    }

    private static bool HasOnlyKeys(JsonElement obj, string[] allowed) => HasOnlyKeys(obj, allowed.ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Katı payload ayrıştırma: nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, ya <c>path</c> ya <c>marked</c> (string
    /// id dizisi, id deseni); <c>marked</c>'ta tekrar yasak.
    /// </summary>
    private static bool TryParsePayload(string payload, out Answer answer)
    {
        answer = Answer.Empty;
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "path", "marked"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;

            var hasPath = root.TryGetProperty("path", out var path);
            var hasMarked = root.TryGetProperty("marked", out var marked);
            if (hasPath == hasMarked)
                return false;
            if (!TryReadIds(hasPath ? path : marked, out var ids))
                return false;
            if (hasPath)
            {
                if (ids.Count > MaxPathLength)
                    return false;
                answer = new Answer(ids, null);
            }
            else
            {
                if (ids.Count > MaxNodes || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                    return false;
                answer = new Answer(null, ids);
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadIds(JsonElement array, out List<string> ids)
    {
        ids = new List<string>();
        if (array.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } id || !InteractionIds.IsValid(id))
                return false;
            ids.Add(id);
        }
        return true;
    }

    /// <summary>Yol çözümü: düğüm → kenar. Toleranslı: plana uymayan seçim atlanır; bozuk çözüm = boş (puanlanamaz).</summary>
    private static bool TryReadChoices(string? solution, Maze maze, out Dictionary<string, string> choices)
    {
        choices = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("choices", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } edgeId
                    && maze.Nodes.ContainsKey(property.Name) && maze.Edges.ContainsKey(edgeId))
                    choices[property.Name] = edgeId;
            }
            return choices.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            choices.Clear();
            return false;
        }
    }

    /// <summary>Taş çözümü: işaretlenmesi gereken taşlar (boş dizi geçerli: hiçbir taş işaretlenmez). Okunamazsa false.</summary>
    private static bool TryReadMarkedSolution(string? solution, Maze maze, out HashSet<string> marked)
    {
        marked = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("marked", out var element)
                || element.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } id && maze.Nodes.ContainsKey(id))
                    marked.Add(id);
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            marked.Clear();
            return false;
        }
    }

    private static string LabelOf(MazeNode node, int index)
        => string.IsNullOrWhiteSpace(node.Label) ? "#" + (index + 1) : InteractionAnalysis.TextLabel(node.Label);

    private static string EdgeLabel(MazeEdge edge)
        => string.IsNullOrWhiteSpace(edge.Label) ? edge.Id : InteractionAnalysis.TextLabel(edge.Label);

    private static string WriteFeedback(IReadOnlyList<(string Id, bool Correct)> rows, string status, string? at)
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
            writer.WriteString("status", status);
            if (at != null)
                writer.WriteString("at", at);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
