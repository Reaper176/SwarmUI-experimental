using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using System.IO;

namespace SwarmUI.Utils;

/// <summary>JSON-backed storage for Prompt Lab prompts, fragments, and wildcard sets.</summary>
public static class PromptLabStore
{
    /// <summary>Thread lock for Prompt Lab file reads/writes.</summary>
    public static LockObject StoreLock = new();

    /// <summary>Supported Prompt Lab collection file names keyed by API type.</summary>
    public static Dictionary<string, string> CollectionFiles = new()
    {
        ["prompts"] = "prompts.json",
        ["fragments"] = "fragments.json",
        ["wildcards"] = "wildcards.json"
    };

    /// <summary>Gets the Prompt Lab root folder for a user.</summary>
    public static string GetUserRoot(User user)
    {
        string safeUser = Utilities.StrictFilenameClean(user.UserID);
        return Utilities.CombinePathWithAbsolute(Program.DataDir, "PromptLab", safeUser);
    }

    /// <summary>Gets the on-disk JSON file path for a collection.</summary>
    public static string GetCollectionPath(User user, string collection)
    {
        collection = (collection ?? "").ToLowerFast();
        if (!CollectionFiles.TryGetValue(collection, out string file))
        {
            throw new SwarmUserErrorException($"Invalid Prompt Lab collection '{collection}'.");
        }
        return $"{GetUserRoot(user)}/{file}";
    }

    /// <summary>Loads a Prompt Lab collection as a JSON array.</summary>
    public static JArray LoadCollection(User user, string collection)
    {
        lock (StoreLock)
        {
            string path = GetCollectionPath(user, collection);
            if (!File.Exists(path))
            {
                return new JArray();
            }
            try
            {
                JToken parsed = JToken.Parse(File.ReadAllText(path));
                return parsed is JArray arr ? arr : new JArray();
            }
            catch (Exception ex)
            {
                Logs.Warning($"Failed to read Prompt Lab collection '{collection}' for user '{user.UserID}': {ex.ReadableString()}");
                return new JArray();
            }
        }
    }

    /// <summary>Saves a Prompt Lab collection to disk.</summary>
    public static void SaveCollection(User user, string collection, JArray items)
    {
        lock (StoreLock)
        {
            string path = GetCollectionPath(user, collection);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, items.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }

    /// <summary>Returns all Prompt Lab collections for a user.</summary>
    public static JObject ListAll(User user)
    {
        JObject result = new();
        foreach (string collection in CollectionFiles.Keys)
        {
            result[collection] = LoadCollection(user, collection);
        }
        return result;
    }

    /// <summary>Saves or updates an item in the target Prompt Lab collection.</summary>
    public static JObject SaveItem(User user, string collection, JObject item)
    {
        JArray items = LoadCollection(user, collection);
        string id = item.Value<string>("id");
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString();
            item["id"] = id;
            item["created_at"] = now;
        }
        item["updated_at"] = now;
        bool replaced = false;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is JObject existing && existing.Value<string>("id") == id)
            {
                items[i] = item;
                replaced = true;
                break;
            }
        }
        if (!replaced)
        {
            items.Add(item);
        }
        SaveCollection(user, collection, items);
        return item;
    }

    /// <summary>Deletes an item by ID from a Prompt Lab collection.</summary>
    public static bool DeleteItem(User user, string collection, string id)
    {
        JArray items = LoadCollection(user, collection);
        int before = items.Count;
        JArray kept = new(items.Where(item => item is not JObject obj || obj.Value<string>("id") != id));
        if (kept.Count == before)
        {
            return false;
        }
        SaveCollection(user, collection, kept);
        return true;
    }

    /// <summary>Duplicates an item by ID in a Prompt Lab collection.</summary>
    public static JObject DuplicateItem(User user, string collection, string id)
    {
        JArray items = LoadCollection(user, collection);
        JObject found = items.OfType<JObject>().FirstOrDefault(item => item.Value<string>("id") == id);
        if (found is null)
        {
            throw new SwarmUserErrorException($"Prompt Lab item '{id}' was not found.");
        }
        JObject duplicate = new(found);
        duplicate["id"] = Guid.NewGuid().ToString();
        duplicate["parent_id"] = found.Value<string>("id");
        duplicate["name"] = $"{found.Value<string>("name") ?? "Untitled"} Copy";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        duplicate["created_at"] = now;
        duplicate["updated_at"] = now;
        items.Add(duplicate);
        SaveCollection(user, collection, items);
        return duplicate;
    }

    /// <summary>Gets Prompt Lab wildcard sets as a name-to-values map.</summary>
    public static Dictionary<string, List<string>> GetWildcardMap(User user)
    {
        Dictionary<string, List<string>> result = new();
        foreach (JObject item in LoadCollection(user, "wildcards").OfType<JObject>())
        {
            string name = item.Value<string>("name");
            if (string.IsNullOrWhiteSpace(name) || item["values"] is not JArray values)
            {
                continue;
            }
            result[name.ToLowerFast()] = [.. values.Select(val => $"{val}").Where(val => !string.IsNullOrWhiteSpace(val))];
        }
        return result;
    }
}
