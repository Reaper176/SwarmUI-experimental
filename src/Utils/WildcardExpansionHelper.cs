using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace SwarmUI.Utils;

/// <summary>Expands prompt wildcard tokens such as <c>&lt;wildcard:species&gt;</c> or <c>&lt;random:cat,dog&gt;</c>.</summary>
public static partial class WildcardExpansionHelper
{
    /// <summary>Compiled matcher for wildcard-like prompt tokens.</summary>
    [GeneratedRegex("<(wildcard|random):([^>]+)>", RegexOptions.IgnoreCase)]
    public static partial Regex WildcardTokenRegex();

    /// <summary>Detects wildcard tokens in prompt order, de-duplicated case-insensitively.</summary>
    public static List<string> DetectTokens(params string[] prompts)
    {
        List<string> tokens = [];
        foreach (string prompt in prompts)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }
            foreach (Match match in WildcardTokenRegex().Matches(prompt))
            {
                string token = TokenIdFor(match);
                if (!tokens.Any(existing => existing.ToLowerFast() == token.ToLowerFast()))
                {
                    tokens.Add(token);
                }
            }
        }
        return tokens;
    }

    /// <summary>Expands wildcard prompts according to request data.</summary>
    public static JObject Expand(string positivePrompt, string negativePrompt, Dictionary<string, List<string>> wildcardSets, string mode, int sampleCount, int maxCombinations, bool shuffleResults)
    {
        positivePrompt ??= "";
        negativePrompt ??= "";
        mode = (mode ?? "all").ToLowerFast().Replace("_", "");
        sampleCount = Math.Max(1, sampleCount);
        maxCombinations = Math.Max(1, maxCombinations);
        List<string> tokens = DetectTokens(positivePrompt, negativePrompt);
        List<string> warnings = [];
        List<List<string>> valueLists = [];
        foreach (string token in tokens)
        {
            List<string> values;
            if (token.StartsWith("random:", StringComparison.OrdinalIgnoreCase))
            {
                values = ParseRandomValues(token["random:".Length..]);
            }
            else if (!wildcardSets.TryGetValue(token.ToLowerFast(), out values))
            {
                WildcardsHelper.Wildcard existingWildcard = WildcardsHelper.GetWildcard(token);
                values = existingWildcard?.Options?.ToList();
            }
            if (values is null)
            {
                warnings.Add($"Missing wildcard set: {token}");
                values = [];
            }
            values = [.. values.Where(value => !string.IsNullOrWhiteSpace(value))];
            if (values.Count == 0)
            {
                warnings.Add($"Empty wildcard set: {token}");
            }
            valueLists.Add(values);
        }
        long total = valueLists.Count == 0 ? 1 : valueLists.Aggregate(1L, (current, list) => current * Math.Max(1, list.Count));
        if (valueLists.Any(list => list.Count == 0))
        {
            return BuildResult(tokens, total, [], warnings);
        }
        int wanted = mode switch
        {
            "randomsingle" => 1,
            "randombatch" => Math.Min(sampleCount, maxCombinations),
            "samplencombinations" => Math.Min(sampleCount, maxCombinations),
            "sample" => Math.Min(sampleCount, maxCombinations),
            _ => total > maxCombinations ? maxCombinations : (int)total
        };
        if ((mode == "all" || mode == "allcombinations") && total > maxCombinations)
        {
            warnings.Add($"Total combinations {total} exceeds max {maxCombinations}; returning first {maxCombinations}.");
        }
        Random random = new();
        List<int[]> indexes = mode switch
        {
            "randomsingle" => [RandomIndexes(valueLists, random)],
            "randombatch" => [.. Enumerable.Range(0, wanted).Select(_ => RandomIndexes(valueLists, random))],
            "samplencombinations" => SampleIndexes(valueLists, wanted, random),
            "sample" => SampleIndexes(valueLists, wanted, random),
            _ => AllIndexes(valueLists, wanted)
        };
        if (shuffleResults)
        {
            indexes = [.. indexes.OrderBy(_ => random.Next())];
        }
        JArray outputs = [];
        foreach (int[] choice in indexes)
        {
            Dictionary<string, string> chosen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tokens.Count; i++)
            {
                chosen[tokens[i]] = valueLists[i][choice[i]];
            }
            outputs.Add(new JObject()
            {
                ["positive"] = ApplyChoices(positivePrompt, chosen),
                ["negative"] = ApplyChoices(negativePrompt, chosen),
                ["wildcard_values"] = JObject.FromObject(chosen)
            });
        }
        return BuildResult(tokens, total, outputs, warnings);
    }

    /// <summary>Gets a stable token ID for a regex match.</summary>
    public static string TokenIdFor(Match match)
    {
        string type = match.Groups[1].Value.Trim().ToLowerFast();
        string data = match.Groups[2].Value.Trim();
        return type == "random" ? $"random:{data}" : data;
    }

    /// <summary>Parses comma-separated inline random values.</summary>
    public static List<string> ParseRandomValues(string data)
    {
        char splitChar = data.Contains('|') ? '|' : ',';
        return [.. data.Split(splitChar).Select(part => part.Trim()).Where(part => !string.IsNullOrWhiteSpace(part))];
    }

    /// <summary>Builds the API result object.</summary>
    public static JObject BuildResult(List<string> tokens, long total, JArray prompts, List<string> warnings)
    {
        return new JObject()
        {
            ["tokens"] = new JArray(tokens),
            ["total_possible_combinations"] = total,
            ["returned_combinations"] = prompts.Count,
            ["prompts"] = prompts,
            ["warnings"] = new JArray(warnings)
        };
    }

    /// <summary>Applies chosen wildcard values to a prompt.</summary>
    public static string ApplyChoices(string prompt, Dictionary<string, string> choices)
    {
        return WildcardTokenRegex().Replace(prompt, match =>
        {
            string token = TokenIdFor(match);
            return choices.TryGetValue(token, out string value) ? value : match.Value;
        });
    }

    /// <summary>Gets a single random index set.</summary>
    public static int[] RandomIndexes(List<List<string>> valueLists, Random random)
    {
        return [.. valueLists.Select(list => random.Next(list.Count))];
    }

    /// <summary>Gets sequential Cartesian-product index sets, capped at the requested count.</summary>
    public static List<int[]> AllIndexes(List<List<string>> valueLists, int max)
    {
        List<int[]> results = [];
        if (valueLists.Count == 0)
        {
            results.Add([]);
            return results;
        }
        int[] current = new int[valueLists.Count];
        while (results.Count < max)
        {
            results.Add([.. current]);
            int position = current.Length - 1;
            while (position >= 0)
            {
                current[position]++;
                if (current[position] < valueLists[position].Count)
                {
                    break;
                }
                current[position] = 0;
                position--;
            }
            if (position < 0)
            {
                break;
            }
        }
        return results;
    }

    /// <summary>Gets a non-repeating random sample of Cartesian-product index sets.</summary>
    public static List<int[]> SampleIndexes(List<List<string>> valueLists, int count, Random random)
    {
        List<int[]> results = [];
        HashSet<string> seen = [];
        long total = valueLists.Count == 0 ? 1 : valueLists.Aggregate(1L, (current, list) => current * Math.Max(1, list.Count));
        int wanted = (int)Math.Min(count, total);
        int maxAttempts = Math.Max(wanted * 10, 100);
        for (int attempts = 0; results.Count < wanted && attempts < maxAttempts; attempts++)
        {
            int[] indexes = RandomIndexes(valueLists, random);
            string key = string.Join(",", indexes);
            if (seen.Add(key))
            {
                results.Add(indexes);
            }
        }
        if (results.Count < wanted)
        {
            foreach (int[] indexes in AllIndexes(valueLists, wanted))
            {
                string key = string.Join(",", indexes);
                if (seen.Add(key))
                {
                    results.Add(indexes);
                    if (results.Count >= wanted)
                    {
                        break;
                    }
                }
            }
        }
        return results;
    }
}
