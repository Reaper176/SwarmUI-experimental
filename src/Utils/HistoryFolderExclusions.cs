namespace SwarmUI.Utils;

/// <summary>Per-user relative output folders omitted from history listings.</summary>
public class HistoryFolderExclusions
{
    /// <summary>Normalized folder paths, without trailing separators.</summary>
    private readonly string[] Folders;

    /// <summary>Matches the host platform's usual path case convention.</summary>
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Compiles the user's exact relative folder paths, ignoring empty or escaping entries.</summary>
    public HistoryFolderExclusions(IEnumerable<string> folders)
    {
        Folders = [.. (folders ?? []).Select(Normalize).Where(folder => !string.IsNullOrEmpty(folder)).Distinct()];
    }

    /// <summary>Normalizes relative path separators and dot segments. Returns null for paths escaping the output root.</summary>
    private static string Normalize(string path)
    {
        List<string> parts = [];
        foreach (string part in (path ?? "").Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    return null;
                }
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(part);
            }
        }
        return string.Join('/', parts);
    }

    /// <summary>Checks a folder or file path relative to Output, matching whole folder boundaries only.</summary>
    public bool IsExcluded(string relativePath)
    {
        if (Folders.Length == 0)
        {
            return false;
        }
        string path = Normalize(relativePath);
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        foreach (string folder in Folders)
        {
            if (path.Equals(folder, PathComparison) || path.StartsWith(folder + "/", PathComparison))
            {
                return true;
            }
        }
        return false;
    }
}
