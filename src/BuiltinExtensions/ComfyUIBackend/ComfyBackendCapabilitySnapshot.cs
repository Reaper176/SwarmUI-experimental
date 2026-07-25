using System.Collections.Frozen;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Represents an immutable snapshot of a ComfyUI backend's detected capabilities.</summary>
public sealed class ComfyBackendCapabilitySnapshot
{
    /// <summary>Gets an empty capability snapshot.</summary>
    public static ComfyBackendCapabilitySnapshot Empty { get; } = new([], [], "/", 0);

    /// <summary>Gets the feature IDs supported by the backend.</summary>
    public FrozenSet<string> Features { get; }

    /// <summary>Gets the ComfyUI node types exposed by the backend.</summary>
    public FrozenSet<string> NodeTypes { get; }

    /// <summary>Gets the path separator format used by the backend's model folders.</summary>
    public string ModelFolderFormat { get; }

    /// <summary>Gets the generation number assigned to this snapshot.</summary>
    public long Generation { get; }

    /// <summary>Creates an immutable capability snapshot from the supplied backend state.</summary>
    /// <param name="features">The feature IDs supported by the backend.</param>
    /// <param name="nodeTypes">The ComfyUI node types exposed by the backend.</param>
    /// <param name="modelFolderFormat">The path separator format used by the backend's model folders.</param>
    /// <param name="generation">The generation number assigned to this snapshot.</param>
    public ComfyBackendCapabilitySnapshot(IEnumerable<string> features, IEnumerable<string> nodeTypes, string modelFolderFormat, long generation)
    {
        Features = features.ToFrozenSet();
        NodeTypes = nodeTypes.ToFrozenSet();
        ModelFolderFormat = modelFolderFormat;
        Generation = generation;
    }
}
