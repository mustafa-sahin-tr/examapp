namespace ExamApp.Api.Services.Interactions;

/// <summary>issue #571: DI'daki tüm <see cref="IInteractionGrader"/>'ları tipe göre (büyük/küçük harf duyarsız) eşler.</summary>
public sealed class InteractionGraderRegistry : IInteractionGraderRegistry
{
    private readonly Dictionary<string, IInteractionGrader> _graders;
    private readonly IInteractionGrader _mcq;

    public InteractionGraderRegistry(IEnumerable<IInteractionGrader> graders)
    {
        _graders = new Dictionary<string, IInteractionGrader>(StringComparer.OrdinalIgnoreCase);
        foreach (var grader in graders)
        {
            if (!_graders.TryAdd(grader.InteractionType, grader))
                throw new InvalidOperationException($"Duplicate interaction grader for type '{grader.InteractionType}'.");
        }

        _mcq = _graders.TryGetValue(McqGrader.Type, out var mcq) ? mcq : new McqGrader();
    }

    /// <summary>DI'sız kurulan servisler (birim testleri) için yerleşik kontrolcüler.</summary>
    public static InteractionGraderRegistry Default { get; } =
        new(new IInteractionGrader[] { new McqGrader(), new DragDropLabelingGrader(), new MatrixChoiceGrader(), new SlotFillGrader(),
            new DragPlaceGrader(), new MatchGrader(), new TextSelectGrader() });

    public IInteractionGrader Resolve(string? interactionType)
        => !string.IsNullOrWhiteSpace(interactionType) && _graders.TryGetValue(interactionType.Trim(), out var grader)
            ? grader
            : _mcq;
}
