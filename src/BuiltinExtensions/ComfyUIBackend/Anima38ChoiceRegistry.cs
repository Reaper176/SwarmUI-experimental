namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Tracks Anima 3.8B model choices advertised by individual backend owners.</summary>
internal sealed class Anima38ChoiceRegistry
{
    /// <summary>Choices last advertised by one backend owner.</summary>
    /// <param name="Qwen">The owner's exact Qwen3.5 encoder choices.</param>
    /// <param name="Adapters">The owner's exact tagged adapter choices.</param>
    private sealed record OwnerChoices(string[] Qwen, string[] Adapters);

    /// <summary>Current owner choices keyed by backend object identity.</summary>
    private Dictionary<object, OwnerChoices> Entries = new(ReferenceEqualityComparer.Instance);

    /// <summary>Current aggregate lists published to parameter delegates.</summary>
    /// <param name="Qwen">All current Qwen choices with automatic selection first.</param>
    /// <param name="Adapters">All current adapter choices with automatic selection first.</param>
    internal sealed record Aggregate(List<string> Qwen, List<string> Adapters);

    /// <summary>Replaces one owner's advertised choices and returns the fresh current-owner aggregate.</summary>
    /// <remarks>The caller must synchronize this mutation with shared-value publication.</remarks>
    /// <param name="owner">The backend object whose identity owns the choices.</param>
    /// <param name="qwen">The owner's newly advertised Qwen choices.</param>
    /// <param name="adapters">The owner's newly advertised adapter choices.</param>
    /// <returns>The deterministic union of current owner choices.</returns>
    internal Aggregate Replace(object owner, IEnumerable<string> qwen, IEnumerable<string> adapters)
    {
        Entries[owner] = new([.. qwen], [.. adapters]);
        return BuildAggregate();
    }

    /// <summary>Removes one owner's advertised choices and returns the fresh current-owner aggregate.</summary>
    /// <remarks>The caller must synchronize this mutation with shared-value publication.</remarks>
    /// <param name="owner">The backend object whose identity owns the choices.</param>
    /// <returns>The deterministic union of remaining owner choices.</returns>
    internal Aggregate Remove(object owner)
    {
        Entries.Remove(owner);
        return BuildAggregate();
    }

    /// <summary>Builds an auto-first exact-value union from current owners without reading published globals.</summary>
    /// <returns>The deterministic current-owner aggregate.</returns>
    private Aggregate BuildAggregate()
    {
        return new(
            Merge(Entries.Values.SelectMany(entry => entry.Qwen)),
            Merge(Entries.Values.SelectMany(entry => entry.Adapters))
        );
    }

    /// <summary>Creates one stable, exact-value de-duplicated choice list.</summary>
    /// <param name="values">The current owners' advertised values.</param>
    /// <returns>An auto-first list sorted case-insensitively with an ordinal tie-breaker.</returns>
    private static List<string> Merge(IEnumerable<string> values)
    {
        HashSet<string> merged = new(values.Where(value => !string.IsNullOrWhiteSpace(value) && value != "auto"), StringComparer.Ordinal);
        return ["auto", .. merged.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ThenBy(value => value, StringComparer.Ordinal)];
    }
}
