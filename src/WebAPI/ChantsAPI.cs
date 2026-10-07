using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwarmUI.WebAPI;

[API.APIClass("Browse and edit named prompt chants in the selected autocomplete JSON file.")]
public static class ChantsAPI
{
    /// <summary>Serializes chant reads and edits, including revision checks.</summary>
    private static readonly object FileLock = new();

    /// <summary>Registers chant routes.</summary>
    public static void Register()
    {
        API.RegisterAPICall(GetChantList, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(SaveChant, true, Permissions.EditChants);
    }

    /// <summary>Resolves only the selected, discovered file, rejecting links and traversal.</summary>
    private static string SelectedPath(Session session, string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source != session.User.Settings.AutoComplete.ChantSource || !AutoCompleteListHelper.ChantFileNames.Contains(source))
        {
            throw new InvalidDataException("Select an available Chant Source in User Settings first.");
        }
        string root = Path.GetFullPath(AutoCompleteListHelper.FolderPath);
        string path = Path.GetFullPath(Path.Combine(root, source));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid chant source path.");
        }
        for (string current = path; current != root; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Linked chant files and folders cannot be edited through this browser.");
            }
        }
        return path;
    }

    /// <summary>Calculates the revision of the complete source, including unknown fields.</summary>
    private static string Revision(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Validates an optional, bounded embedded raster thumbnail.</summary>
    public static void ValidateThumbnail(string thumbnail)
    {
        if (string.IsNullOrEmpty(thumbnail))
        {
            return;
        }
        string prefix = new string[] { "data:image/png;base64,", "data:image/jpeg;base64,", "data:image/webp;base64," }.FirstOrDefault(thumbnail.StartsWith);
        if (prefix is null || thumbnail.Length > 2800000)
        {
            throw new InvalidDataException("Thumbnail must be an embedded PNG, JPEG, or WebP image under 2 MiB.");
        }
        byte[] bytes = Convert.FromBase64String(thumbnail[prefix.Length..]);
        bool valid = prefix.Contains("png") ? bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            : prefix.Contains("jpeg") ? bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
            : bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP";
        if (!valid || bytes.Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("Invalid or oversized thumbnail image.");
        }
    }

    /// <summary>Edits or appends one chant without discarding other entries or custom fields.</summary>
    public static JArray UpdateDocument(string json, string originalName, JObject edit)
    {
        AutoCompleteListHelper.ParseChants(json);
        foreach (string field in new string[] { "name", "terms", "content", "description", "thumbnail" })
        {
            if (edit[field]?.Type != JTokenType.String)
            {
                throw new InvalidDataException($"Chant {field} must be text.");
            }
        }
        if (edit["color"]?.Type != JTokenType.Integer || edit["color"].Value<int>() < 0 || edit["color"].Value<int>() > 5)
        {
            throw new InvalidDataException("Chant color must be between 0 and 5.");
        }
        if (string.IsNullOrWhiteSpace(edit["content"].Value<string>()) || edit["content"].Value<string>().Length > 100000 || edit["name"].Value<string>().Length > 200 || edit["description"].Value<string>().Length > 10000 || edit["terms"].Value<string>().Length > 10000)
        {
            throw new InvalidDataException("Provide chant content and keep name, content, description, and search terms within their limits.");
        }
        if (edit["name"].Value<string>() != edit["name"].Value<string>().Trim())
        {
            throw new InvalidDataException("Chant names cannot start or end with whitespace.");
        }
        ValidateThumbnail(edit["thumbnail"].Value<string>());
        JArray document = JArray.Parse(json);
        JObject target = document.OfType<JObject>().FirstOrDefault(entry => string.Equals(entry["name"]?.Value<string>(), originalName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(originalName) && target is null)
        {
            throw new InvalidDataException("The original chant no longer exists. Refresh and try again.");
        }
        if (target is null)
        {
            target = new JObject();
            document.Add(target);
        }
        foreach (string field in new string[] { "name", "terms", "content", "color", "description", "thumbnail" })
        {
            target[field] = edit[field].DeepClone();
        }
        AutoCompleteListHelper.ParseChants(document.ToString());
        return document;
    }

    [API.APIDescription("Reads chants from the user's selected source. An empty source returns an empty list.", """{"source":"demo-chants.json","revision":"hash","chants":[{"name":"SoftLight","content":"soft light","terms":"Light","color":2,"description":"","thumbnail":""}]}""")]
    public static async Task<JObject> GetChantList(Session session)
    {
        string source = session.User.Settings.AutoComplete.ChantSource;
        if (string.IsNullOrWhiteSpace(source))
        {
            return new JObject() { ["source"] = "", ["revision"] = "", ["chants"] = new JArray() };
        }
        try
        {
            lock (FileLock)
            {
                string text = File.ReadAllText(SelectedPath(session, source));
                AutoCompleteListHelper.ParseChants(text);
                return new JObject() { ["source"] = source, ["revision"] = Revision(text), ["chants"] = JArray.Parse(text) };
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException or OverflowException)
        {
            return new JObject() { ["error"] = "Unable to read chants. Check the selected source and JSON format.", ["error_id"] = "invalid_chant_source" };
        }
    }

    [API.APIDescription("Saves one chant in the selected shared JSON file. Requires edit_chants permission. Unknown fields and other entries are preserved; stale revisions are rejected.", """{"success":true}""")]
    public static async Task<JObject> SaveChant(Session session,
        [API.APIParameter("Selected chant filename, exactly as returned by GetChantList.")] string source,
        [API.APIParameter("Revision from GetChantList. Refresh after an edit conflict.")] string revision,
        [API.APIParameter("Existing chant name; empty to create a new chant.")] string originalName,
        [API.APIParameter("Chant name used in <chant:Name> tags, maximum 200 characters.")] string name,
        [API.APIParameter("Comma-separated search terms, maximum 10000 characters.")] string terms,
        [API.APIParameter("Prompt fragment, maximum 100000 characters.")] string content,
        [API.APIParameter("Autocomplete color category, from 0 to 5.")] int color,
        [API.APIParameter("Optional description, maximum 10000 characters.")] string description = "",
        [API.APIParameter("Optional PNG, JPEG, or WebP base64 data URL, at most 2 MiB decoded. Empty removes the thumbnail.")] string thumbnail = "")
    {
        try
        {
            lock (FileLock)
            {
                string path = SelectedPath(session, source);
                string text = File.ReadAllText(path);
                if (revision != Revision(text))
                {
                    return new JObject() { ["error"] = "This chant file changed since you opened the editor. Refresh the Chants tab and reopen the chant before saving.", ["error_id"] = "chant_edit_conflict" };
                }
                JObject edit = new() { ["name"] = name, ["terms"] = terms, ["content"] = content, ["color"] = color, ["description"] = description, ["thumbnail"] = thumbnail };
                string updated = UpdateDocument(text, originalName, edit).ToString(Formatting.Indented) + "\n";
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, updated);
                    // Detect external editors that wrote while this save was being prepared.
                    if (Revision(File.ReadAllText(path)) != revision)
                    {
                        return new JObject() { ["error"] = "The chant file changed during saving. Refresh and try again.", ["error_id"] = "chant_edit_conflict" };
                    }
                    File.Move(temporary, path, true);
                    AutoCompleteListHelper.ChantLists.TryRemove(source, out _);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                return new JObject() { ["success"] = true };
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException or OverflowException)
        {
            return new JObject() { ["error"] = ex is InvalidDataException ? ex.Message : "Unable to save chants. Check the file format, thumbnail, and file permissions.", ["error_id"] = "invalid_chant_edit" };
        }
    }
}
