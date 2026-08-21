using System.Collections.Frozen;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Tracks immutable capability snapshots for individual ComfyUI backend instances.</summary>
public static class ComfyCapabilityRegistry
{
    /// <summary>Stores the immutable evidence and interpreted snapshot for one backend owner.</summary>
    internal sealed record CapabilityEntry(FrozenSet<string> NodeTypes, FrozenSet<string> BackendFeatures, string ModelFolderFormat, ComfyBackendCapabilitySnapshot Snapshot);

    /// <summary>Stores candidate compatibility tracking state without changing current observations.</summary>
    internal sealed record CompatibilityTrackingState(
        HashSet<string> ForcedFeatures,
        HashSet<string> SuppressedFeatures,
        HashSet<string> LastDiscardHints,
        Dictionary<string, string> LastNodeMap);

    /// <summary>Stores a complete replacement registry, aggregate, and compatibility state before publication.</summary>
    internal sealed record RegistryCandidate(Dictionary<object, CapabilityEntry> Entries, HashSet<string> Aggregate, CompatibilityTrackingState Compatibility);

    /// <summary>Current capability entries, keyed by backend owner identity.</summary>
    private static Dictionary<object, CapabilityEntry> Entries = new(ReferenceEqualityComparer.Instance);

    /// <summary>Feature baseline captured after built-in compatibility hints are registered.</summary>
    private static HashSet<string> InitialFeatures = [];

    /// <summary>Compatibility features explicitly added through the public aggregate set after initialization.</summary>
    private static HashSet<string> ForcedFeatures = [];

    /// <summary>Compatibility features explicitly removed through the public aggregate set after publication.</summary>
    private static HashSet<string> SuppressedFeatures = [];

    /// <summary>Last aggregate published to the public compatibility feature set.</summary>
    private static HashSet<string> LastPublishedAggregate = [];

    /// <summary>Cached immutable copy of the last aggregate published by maintained code.</summary>
    private static FrozenSet<string> LastPublishedAggregateSnapshot = Array.Empty<string>().ToFrozenSet();

    /// <summary>Last observed copy of the mutable discard-if-not-found compatibility hints.</summary>
    private static HashSet<string> LastDiscardHints = [];

    /// <summary>Last observed copy of the mutable node-to-feature compatibility map.</summary>
    private static Dictionary<string, string> LastNodeMap = [];

    /// <summary>Generation number to assign to the next complete registry candidate.</summary>
    private static long NextGeneration = 1;

    /// <summary>Whether the compatibility baseline and mutable extension points have been captured.</summary>
    private static bool IsInitialized;

    /// <summary>Whether an interpreted registry candidate has replaced the raw startup compatibility aggregate.</summary>
    private static bool HasCommittedCandidate;

    /// <summary>Captures the compatibility baseline after built-in initialization hints have been registered.</summary>
    public static void Initialize()
    {
        lock (ComfyUIBackendExtension.ValueAssignmentLocker)
        {
            InitializeLocked();
        }
    }

    /// <summary>Publishes fresh node evidence and a capability snapshot for a backend owner.</summary>
    /// <param name="owner">The backend object whose identity owns the snapshot.</param>
    /// <param name="nodeTypes">The ComfyUI node types exposed by the backend.</param>
    /// <param name="modelFolderFormat">The path separator format used by the backend's model folders.</param>
    /// <returns>The immutable capability snapshot published for the owner.</returns>
    public static ComfyBackendCapabilitySnapshot Publish(object owner, IEnumerable<string> nodeTypes, string modelFolderFormat)
    {
        FrozenSet<string> frozenNodeTypes = nodeTypes.ToFrozenSet();
        lock (ComfyUIBackendExtension.ValueAssignmentLocker)
        {
            InitializeLocked();
            RegistryCandidate candidate = PreparePublish(owner, frozenNodeTypes, modelFolderFormat);
            Commit(candidate);
            return Entries[owner].Snapshot;
        }
    }

    /// <summary>Gets the current immutable capability snapshot for a backend owner.</summary>
    /// <param name="owner">The backend object whose identity owns the snapshot.</param>
    /// <returns>The owner's current snapshot, or <see cref="ComfyBackendCapabilitySnapshot.Empty"/> when unknown.</returns>
    public static ComfyBackendCapabilitySnapshot GetSnapshot(object owner)
    {
        lock (ComfyUIBackendExtension.ValueAssignmentLocker)
        {
            InitializeLocked();
            bool compatibilityChanged = CaptureCompatibilityChanges(out CompatibilityTrackingState compatibility);
            if (!HasCommittedCandidate || compatibilityChanged)
            {
                RegistryCandidate candidate = BuildCandidate(CopyEntries(), compatibility);
                Commit(candidate);
            }
            if (Entries.TryGetValue(owner, out CapabilityEntry entry))
            {
                return entry.Snapshot;
            }
            return ComfyBackendCapabilitySnapshot.Empty;
        }
    }

    /// <summary>Gets a stable immutable copy of the aggregate ComfyUI feature set.</summary>
    /// <returns>The aggregate feature IDs supported across registered ComfyUI backends.</returns>
    public static FrozenSet<string> GetAggregateSnapshot()
    {
        lock (ComfyUIBackendExtension.ValueAssignmentLocker)
        {
            InitializeLocked();
            bool compatibilityChanged = CaptureCompatibilityChanges(out CompatibilityTrackingState compatibility);
            if (!HasCommittedCandidate || compatibilityChanged)
            {
                RegistryCandidate candidate = BuildCandidate(CopyEntries(), compatibility);
                Commit(candidate);
            }
            return LastPublishedAggregateSnapshot;
        }
    }

    /// <summary>Removes the capability snapshot owned by a backend object.</summary>
    /// <param name="owner">The backend object whose identity owns the snapshot.</param>
    public static void Remove(object owner)
    {
        lock (ComfyUIBackendExtension.ValueAssignmentLocker)
        {
            InitializeLocked();
            bool compatibilityChanged = CaptureCompatibilityChanges(out CompatibilityTrackingState compatibility);
            bool ownerExists = Entries.ContainsKey(owner);
            if (!ownerExists && !compatibilityChanged)
            {
                return;
            }
            Dictionary<object, CapabilityEntry> candidateEntries = CopyEntries();
            candidateEntries.Remove(owner);
            RegistryCandidate candidate = BuildCandidate(candidateEntries, compatibility);
            Commit(candidate);
        }
    }

    /// <summary>Prepares a complete replacement candidate containing fresh evidence for one owner.</summary>
    /// <remarks>The caller must own <see cref="ComfyUIBackendExtension.ValueAssignmentLocker"/>. This method mutates no state.</remarks>
    /// <param name="owner">The backend object whose identity owns the snapshot.</param>
    /// <param name="nodeTypes">The frozen ComfyUI node types exposed by the backend.</param>
    /// <param name="modelFolderFormat">The path separator format used by the backend's model folders.</param>
    /// <param name="backendFeatures">Optional backend-local capabilities derived from object-info values.</param>
    /// <returns>A complete candidate ready to commit atomically with related shared values.</returns>
    internal static RegistryCandidate PreparePublish(object owner, FrozenSet<string> nodeTypes, string modelFolderFormat, FrozenSet<string> backendFeatures = null)
    {
        CaptureCompatibilityChanges(out CompatibilityTrackingState compatibility);
        Dictionary<object, CapabilityEntry> candidateEntries = CopyEntries();
        candidateEntries[owner] = new(nodeTypes, backendFeatures ?? Array.Empty<string>().ToFrozenSet(), modelFolderFormat, ComfyBackendCapabilitySnapshot.Empty);
        return BuildCandidate(candidateEntries, compatibility);
    }

    /// <summary>Commits a previously prepared complete registry candidate.</summary>
    /// <remarks>The caller must own <see cref="ComfyUIBackendExtension.ValueAssignmentLocker"/>.</remarks>
    /// <param name="candidate">The complete candidate to publish.</param>
    internal static void Commit(RegistryCandidate candidate)
    {
        Entries = CopyEntries(candidate.Entries);
        ForcedFeatures = [.. candidate.Compatibility.ForcedFeatures];
        SuppressedFeatures = [.. candidate.Compatibility.SuppressedFeatures];
        LastDiscardHints = [.. candidate.Compatibility.LastDiscardHints];
        LastNodeMap = new(candidate.Compatibility.LastNodeMap);
        ComfyUIBackendExtension.FeaturesSupported.Clear();
        ComfyUIBackendExtension.FeaturesSupported.UnionWith(candidate.Aggregate);
        LastPublishedAggregate = [.. candidate.Aggregate];
        LastPublishedAggregateSnapshot = candidate.Aggregate.ToFrozenSet();
        NextGeneration++;
        HasCommittedCandidate = true;
    }

    /// <summary>Captures initial public compatibility values while the shared value-assignment lock is owned.</summary>
    private static void InitializeLocked()
    {
        if (IsInitialized)
        {
            return;
        }
        InitialFeatures = [.. ComfyUIBackendExtension.FeaturesSupported];
        LastPublishedAggregate = [.. ComfyUIBackendExtension.FeaturesSupported];
        LastPublishedAggregateSnapshot = LastPublishedAggregate.ToFrozenSet();
        LastDiscardHints = [.. ComfyUIBackendExtension.FeaturesDiscardIfNotFound];
        LastNodeMap = new(ComfyUIBackendExtension.NodeToFeatureMap);
        IsInitialized = true;
    }

    /// <summary>Captures direct mutations to the public compatibility collections.</summary>
    /// <remarks>The caller must own <see cref="ComfyUIBackendExtension.ValueAssignmentLocker"/>.</remarks>
    /// <param name="compatibility">Receives candidate compatibility state containing all current observations.</param>
    /// <returns>Whether any public compatibility collection changed since it was last published.</returns>
    private static bool CaptureCompatibilityChanges(out CompatibilityTrackingState compatibility)
    {
        bool changed = false;
        HashSet<string> candidateForcedFeatures = [.. ForcedFeatures];
        HashSet<string> candidateSuppressedFeatures = [.. SuppressedFeatures];
        HashSet<string> observedFeatures = [.. ComfyUIBackendExtension.FeaturesSupported];
        foreach (string feature in observedFeatures.Except(LastPublishedAggregate))
        {
            candidateForcedFeatures.Add(feature);
            candidateSuppressedFeatures.Remove(feature);
            changed = true;
        }
        foreach (string feature in LastPublishedAggregate.Except(observedFeatures))
        {
            candidateSuppressedFeatures.Add(feature);
            candidateForcedFeatures.Remove(feature);
            changed = true;
        }

        HashSet<string> observedDiscardHints = [.. ComfyUIBackendExtension.FeaturesDiscardIfNotFound];
        if (!observedDiscardHints.SetEquals(LastDiscardHints))
        {
            changed = true;
        }

        Dictionary<string, string> observedNodeMap = new(ComfyUIBackendExtension.NodeToFeatureMap);
        if (!NodeMapsEqual(observedNodeMap, LastNodeMap))
        {
            changed = true;
        }

        compatibility = new(candidateForcedFeatures, candidateSuppressedFeatures, observedDiscardHints, observedNodeMap);
        return changed;
    }

    /// <summary>Builds a complete candidate without mutating current registry or public state.</summary>
    /// <param name="sourceEntries">The owner evidence to reinterpret into fresh snapshots.</param>
    /// <param name="compatibility">The candidate compatibility state to apply during interpretation.</param>
    /// <returns>A complete replacement candidate.</returns>
    private static RegistryCandidate BuildCandidate(Dictionary<object, CapabilityEntry> sourceEntries, CompatibilityTrackingState compatibility)
    {
        HashSet<string> effectiveBaseline = [.. InitialFeatures];
        effectiveBaseline.UnionWith(compatibility.ForcedFeatures);
        effectiveBaseline.ExceptWith(compatibility.SuppressedFeatures);

        Dictionary<object, CapabilityEntry> candidateEntries = new(ReferenceEqualityComparer.Instance);
        HashSet<string> noNodeTypes = [];
        HashSet<string> aggregate = ComfyCapabilityCatalog.Interpret(
            noNodeTypes,
            effectiveBaseline,
            compatibility.LastDiscardHints,
            compatibility.LastNodeMap,
            compatibility.SuppressedFeatures,
            "/");
        aggregate.Remove("folderbackslash");
        aggregate.Remove("folderslash");

        foreach ((object owner, CapabilityEntry entry) in sourceEntries)
        {
            HashSet<string> features = ComfyCapabilityCatalog.Interpret(
                entry.NodeTypes,
                effectiveBaseline,
                compatibility.LastDiscardHints,
                compatibility.LastNodeMap,
                compatibility.SuppressedFeatures,
                entry.ModelFolderFormat);
            features.UnionWith(entry.BackendFeatures);
            features.ExceptWith(compatibility.SuppressedFeatures);
            ComfyBackendCapabilitySnapshot snapshot = new(features, entry.NodeTypes, entry.ModelFolderFormat, NextGeneration);
            candidateEntries[owner] = new(entry.NodeTypes, entry.BackendFeatures, entry.ModelFolderFormat, snapshot);
            foreach (string feature in snapshot.Features)
            {
                if (feature != "folderbackslash" && feature != "folderslash")
                {
                    aggregate.Add(feature);
                }
            }
        }

        return new(candidateEntries, aggregate, compatibility);
    }

    /// <summary>Copies current entries into a reference-identity keyed candidate map.</summary>
    /// <returns>A mutable copy of the current owner entry map.</returns>
    private static Dictionary<object, CapabilityEntry> CopyEntries()
    {
        return CopyEntries(Entries);
    }

    /// <summary>Copies supplied entries into a reference-identity keyed candidate map.</summary>
    /// <param name="sourceEntries">The entries to copy.</param>
    /// <returns>A mutable reference-identity keyed copy of the supplied owner entry map.</returns>
    private static Dictionary<object, CapabilityEntry> CopyEntries(IReadOnlyDictionary<object, CapabilityEntry> sourceEntries)
    {
        Dictionary<object, CapabilityEntry> result = new(ReferenceEqualityComparer.Instance);
        foreach ((object owner, CapabilityEntry entry) in sourceEntries)
        {
            result[owner] = entry;
        }
        return result;
    }

    /// <summary>Tests two node-to-feature maps for exact key and value equality.</summary>
    /// <param name="first">The newly observed node map.</param>
    /// <param name="second">The previously observed node map.</param>
    /// <returns>Whether both maps contain exactly the same keys and values.</returns>
    private static bool NodeMapsEqual(Dictionary<string, string> first, Dictionary<string, string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }
        foreach ((string key, string value) in first)
        {
            if (!second.TryGetValue(key, out string otherValue) || value != otherValue)
            {
                return false;
            }
        }
        return true;
    }
}
