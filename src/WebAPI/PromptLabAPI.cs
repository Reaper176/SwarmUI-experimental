using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;

namespace SwarmUI.WebAPI;

/// <summary>API routes for Prompt Lab storage and wildcard preview expansion.</summary>
[API.APIClass("Prompt Lab API routes.")]
public static class PromptLabAPI
{
    /// <summary>Registers Prompt Lab API routes.</summary>
    public static void Register()
    {
        API.RegisterAPICall(PromptLabList, false, Permissions.ReadUserSettings);
        API.RegisterAPICall(PromptLabSave, true, Permissions.EditUserSettings);
        API.RegisterAPICall(PromptLabDelete, true, Permissions.EditUserSettings);
        API.RegisterAPICall(PromptLabDuplicate, true, Permissions.EditUserSettings);
        API.RegisterAPICall(PromptLabExpandWildcards, false, Permissions.ReadUserSettings);
    }

    /// <summary>Lists all Prompt Lab collections for the current user.</summary>
    public static async Task<JObject> PromptLabList(Session session)
    {
        return new JObject() { ["success"] = true, ["data"] = PromptLabStore.ListAll(session.User) };
    }

    /// <summary>Saves a Prompt Lab item. Input: collection, item.</summary>
    public static async Task<JObject> PromptLabSave(Session session, JObject rawInput)
    {
        string collection = rawInput.Value<string>("collection");
        if (rawInput["item"] is not JObject item)
        {
            return Utilities.ErrorObj("Missing Prompt Lab item.", "missing_item");
        }
        try
        {
            JObject saved = PromptLabStore.SaveItem(session.User, collection, item);
            return new JObject() { ["success"] = true, ["item"] = saved };
        }
        catch (SwarmReadableErrorException ex)
        {
            return Utilities.ErrorObj(ex.Message, "invalid_prompt_lab_collection");
        }
    }

    /// <summary>Deletes a Prompt Lab item. Input: collection, id.</summary>
    public static async Task<JObject> PromptLabDelete(Session session, JObject rawInput)
    {
        string collection = rawInput.Value<string>("collection");
        string id = rawInput.Value<string>("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return Utilities.ErrorObj("Missing Prompt Lab item ID.", "missing_id");
        }
        try
        {
            bool deleted = PromptLabStore.DeleteItem(session.User, collection, id);
            return new JObject() { ["success"] = deleted };
        }
        catch (SwarmReadableErrorException ex)
        {
            return Utilities.ErrorObj(ex.Message, "invalid_prompt_lab_collection");
        }
    }

    /// <summary>Duplicates a Prompt Lab item. Input: collection, id.</summary>
    public static async Task<JObject> PromptLabDuplicate(Session session, JObject rawInput)
    {
        string collection = rawInput.Value<string>("collection");
        string id = rawInput.Value<string>("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return Utilities.ErrorObj("Missing Prompt Lab item ID.", "missing_id");
        }
        try
        {
            JObject duplicated = PromptLabStore.DuplicateItem(session.User, collection, id);
            return new JObject() { ["success"] = true, ["item"] = duplicated };
        }
        catch (SwarmReadableErrorException ex)
        {
            return Utilities.ErrorObj(ex.Message, "prompt_lab_duplicate_failed");
        }
    }

    /// <summary>Detects and expands wildcard combinations for preview. Input: positive, negative, mode, sample_count, max_combinations, shuffle_results, wildcard_sets.</summary>
    public static async Task<JObject> PromptLabExpandWildcards(Session session, JObject rawInput)
    {
        string positive = rawInput.Value<string>("positive") ?? "";
        string negative = rawInput.Value<string>("negative") ?? "";
        string mode = rawInput.Value<string>("mode") ?? "all";
        int sampleCount = rawInput.Value<int?>("sample_count") ?? 1;
        int maxCombinations = rawInput.Value<int?>("max_combinations") ?? 1000;
        bool shuffleResults = rawInput.Value<bool?>("shuffle_results") ?? false;
        Dictionary<string, List<string>> wildcardSets = PromptLabStore.GetWildcardMap(session.User);
        if (rawInput["wildcard_sets"] is JObject rawSets)
        {
            foreach (JProperty prop in rawSets.Properties())
            {
                if (prop.Value is JArray arr)
                {
                    wildcardSets[prop.Name.ToLowerFast()] = [.. arr.Select(val => $"{val}").Where(val => !string.IsNullOrWhiteSpace(val))];
                }
            }
        }
        JObject result = WildcardExpansionHelper.Expand(positive, negative, wildcardSets, mode, sampleCount, maxCombinations, shuffleResults);
        result["success"] = true;
        return result;
    }
}
