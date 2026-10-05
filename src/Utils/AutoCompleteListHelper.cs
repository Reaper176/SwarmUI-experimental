using FreneticUtilities.FreneticExtensions;
using SwarmUI.Core;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SwarmUI.Utils;

/// <summary>Helper for custom word autocomplete lists.</summary>
public class AutoCompleteListHelper
{
    /// <summary>Set of all filenames of auto complete files.</summary>
    public static HashSet<string> FileNames = [];

    /// <summary>Map between filenames and actual wordlists.</summary>
    public static ConcurrentDictionary<string, string[]> AutoCompletionLists = new();

    /// <summary>Available JSON chant files, separate from word lists.</summary>
    public static HashSet<string> ChantFileNames = [];

    /// <summary>Cached chant definitions by source file.</summary>
    public static ConcurrentDictionary<string, Dictionary<string, Chant>> ChantLists = new();

    /// <summary>A named prompt expansion and its autocomplete hints.</summary>
    public class Chant
    {
        /// <summary>Name used in the chant tag.</summary>
        public string Name;
        /// <summary>Comma-separated search terms.</summary>
        public string Terms;
        /// <summary>Prompt text to insert verbatim.</summary>
        public string Content;
        /// <summary>Autocomplete category color.</summary>
        public int Color;
    }

    /// <summary>Gets the correct folder path to use.</summary>
    public static string FolderPath;

    /// <summary>Initializes the helper.</summary>
    public static void Init()
    {
        Reload();
        Program.ModelRefreshEvent += Reload;
    }

    /// <summary>Reloads the list of files.</summary>
    public static void Reload()
    {
        try
        {
            FolderPath = $"{Program.DataDir}/Autocompletions";
            HashSet<string> files = [];
            HashSet<string> chantFiles = [];
            Directory.CreateDirectory(FolderPath);
            foreach (string file in Directory.GetFiles(FolderPath, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".txt") || file.EndsWith(".csv"))
                {
                    string path = Path.GetRelativePath(FolderPath, file).Replace("\\", "/").TrimStart('/');
                    files.Add(path);
                }
                else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    chantFiles.Add(Path.GetRelativePath(FolderPath, file).Replace("\\", "/").TrimStart('/'));
                }
            }
            FileNames = files;
            AutoCompletionLists.Clear();
            ChantFileNames = chantFiles;
            ChantLists.Clear();
        }
        catch (Exception ex)
        {
            Logs.Error($"Error while refreshing autocomplete lists: {ex.ReadableString()}");
        }
    }

    /// <summary>Parses a chant JSON array, rejecting invalid entries and duplicate names.</summary>
    public static Dictionary<string, Chant> ParseChants(string json)
    {
        Dictionary<string, Chant> chants = new(StringComparer.OrdinalIgnoreCase);
        foreach (JToken entry in JArray.Parse(json))
        {
            if (entry is not JObject obj || obj["name"]?.Type != JTokenType.String || obj["content"]?.Type != JTokenType.String)
            {
                throw new InvalidDataException("Each chant requires string name and content fields.");
            }
            string name = obj["name"].Value<string>();
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['<', '>', ':', '\n', '\r']) >= 0 || chants.ContainsKey(name))
            {
                throw new InvalidDataException($"Invalid or duplicate chant name '{name}'.");
            }
            chants.Add(name, new Chant
            {
                Name = name,
                Content = obj["content"].Value<string>(),
                Terms = obj["terms"]?.Value<string>() ?? "",
                Color = Math.Clamp(obj["color"]?.Value<int>() ?? 0, 0, 5)
            });
        }
        return chants;
    }

    /// <summary>Loads only a discovered chant file. Invalid files are logged and treated as empty.</summary>
    public static Dictionary<string, Chant> GetChants(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ChantFileNames.Contains(name))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
        return ChantLists.GetOrCreate(name, () =>
        {
            try
            {
                return ParseChants(File.ReadAllText($"{FolderPath}/{name}"));
            }
            catch (Exception ex)
            {
                Logs.Error($"Error loading chant file '{name}': {ex.ReadableString()}");
                return new Dictionary<string, Chant>(StringComparer.OrdinalIgnoreCase);
            }
        });
    }

    /// <summary>Gets a specific data list.</summary>
    public static string[] GetData(string name, bool escapeParens, string suffix, string spaceMode)
    {
        if (!FileNames.Contains(name))
        {
            return null;
        }
        string[] result = AutoCompletionLists.GetOrCreate(name, () =>
        {
            return [.. File.ReadAllText($"{FolderPath}/{name}").Replace('\r', '\n').SplitFast('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWithFast('#'))];
        });
        bool doSpace = spaceMode == "Spaces";
        bool doUnderscore = spaceMode == "Underscores";
        result = [.. result];
        for (int i = 0; i < result.Length; i++)
        {
            string[] parts = Utilities.SplitStandardCsv(result[i]);
            if (parts.Length == 2 && long.TryParse(parts[1], out _))
            {
                parts = [parts[0], "0", parts[1], ""];
            }
            string word = parts[0];
            if (doSpace)
            {
                word = word.Replace("_", " ");
            }
            else if (doUnderscore)
            {
                word = word.Replace(" ", "_");
            }
            word += suffix;
            if (escapeParens)
            {
                word = word.Replace("(", "\\(").Replace(")", "\\)");
            }
            result[i] = $"{word}\n{parts.JoinString("\n")}";
        }
        return result;
    }
}
