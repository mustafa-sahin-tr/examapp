using System;
using System.Collections.Generic;
using System.Linq;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Maps outbox <see cref="Persistence.OutboxMessage.Type"/> strings to CLR types.
///
/// Producers store the stable logical name (<see cref="Type.FullName"/> — namespace +
/// class, no assembly/version). The registry is the single place that knows the set of
/// publishable events, so a rename shows up here as a compile error instead of a silently
/// dropped message in the publisher.
/// </summary>
public static class OutboxEventRegistry
{
    private static readonly IReadOnlyList<Type> KnownEvents = new[]
    {
        typeof(AnswerSubmittedEvent),
        typeof(QuestionCreatedEvent),
    };

    private static readonly Dictionary<string, Type> ByFullName =
        KnownEvents.ToDictionary(t => t.FullName!, StringComparer.Ordinal);

    /// <summary>The value producers must persist in <c>OutboxMessage.Type</c>.</summary>
    public static string NameFor(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return eventType.FullName
            ?? throw new InvalidOperationException($"Event type {eventType} has no FullName.");
    }

    public static string NameFor<T>() => NameFor(typeof(T));

    /// <summary>
    /// Resolves a stored type string. Handles the current format (FullName), the legacy
    /// format (AssemblyQualifiedName written by older producers), and returns null for a
    /// genuinely unknown type so the caller can dead-letter it instead of looping forever.
    /// </summary>
    public static Type? Resolve(string storedType)
    {
        if (string.IsNullOrWhiteSpace(storedType))
            return null;

        if (ByFullName.TryGetValue(storedType, out var exact))
            return exact;

        // Legacy rows stored "Namespace.Type, Assembly, Version=..., Culture=..., PublicKeyToken=..."
        var comma = storedType.IndexOf(',');
        if (comma > 0 && ByFullName.TryGetValue(storedType[..comma].Trim(), out var legacy))
            return legacy;

        // Last resort for anything else a legacy producer may have written.
        return Type.GetType(storedType);
    }
}
