using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Builds value-eliding diagnostic descriptions for Comfy workflows and typed inputs.</summary>
internal static class ComfyDiagnostics
{
    /// <summary>Describes a raw direct workflow graph without reproducing submitted input values.</summary>
    public static string DescribeWorkflow(string workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow))
        {
            return FixedStatus("empty-workflow");
        }
        JToken parsed;
        try
        {
            parsed = JToken.Parse(workflow);
        }
        catch
        {
            return FixedStatus("invalid-workflow-json");
        }
        if (parsed is not JObject graph)
        {
            return FixedStatus("invalid-workflow-shape");
        }
        try
        {
            return DescribeGraph(graph);
        }
        catch
        {
            return FixedStatus("unavailable-workflow-summary");
        }
    }

    /// <summary>Describes a raw Comfy prompt envelope without reproducing submitted input values.</summary>
    public static string DescribePromptEnvelope(string envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope))
        {
            return FixedStatus("empty-prompt-envelope");
        }
        try
        {
            return DescribePromptEnvelope(JToken.Parse(envelope));
        }
        catch
        {
            return FixedStatus("invalid-prompt-envelope-json");
        }
    }

    /// <summary>Describes a parsed Comfy prompt envelope without reproducing submitted input values.</summary>
    public static string DescribePromptEnvelope(JToken envelope)
    {
        try
        {
            if (envelope is not JObject root || root["prompt"] is not JObject graph)
            {
                return FixedStatus("invalid-prompt-envelope-shape");
            }
            return DescribeGraph(graph);
        }
        catch
        {
            return FixedStatus("unavailable-prompt-envelope-summary");
        }
    }

    /// <summary>Describes typed-input parameter names without reading or formatting their values.</summary>
    public static string DescribeParameters(T2IParamInput input)
    {
        try
        {
            if (input?.InternalSet?.ValuesInput is null)
            {
                return FixedStatus("unavailable-parameter-summary");
            }
            JArray names = [];
            foreach (string name in input.InternalSet.ValuesInput.Keys)
            {
                names.Add(name);
            }
            JObject summary = new()
            {
                ["parameter_count"] = names.Count,
                ["parameter_names"] = names
            };
            return summary.ToString(Formatting.None);
        }
        catch
        {
            return FixedStatus("unavailable-parameter-summary");
        }
    }

    /// <summary>Describes a normalized workflow-tag name without retaining defaults, suffixes, or values.</summary>
    public static string DescribeNormalizedTagName(string tagName)
    {
        try
        {
            return new JValue(tagName ?? "invalid-tag-name").ToString(Formatting.None);
        }
        catch
        {
            return "\"invalid-tag-name\"";
        }
    }

    /// <summary>Describes a parsed direct workflow graph without reproducing submitted input values.</summary>
    private static string DescribeGraph(JObject graph)
    {
        HashSet<string> validNodeIds = [.. graph.Properties()
            .Where(property => property.Value is JObject node && node["class_type"]?.Type == JTokenType.String && node["inputs"] is JObject)
            .Select(property => property.Name)];
        JObject nodes = [];
        foreach (JProperty nodeProperty in graph.Properties())
        {
            JObject nodeSummary = [];
            if (nodeProperty.Value is not JObject node)
            {
                nodeSummary["status"] = "invalid-node-shape";
                nodes[nodeProperty.Name] = nodeSummary;
                continue;
            }
            JToken classType = node["class_type"];
            nodeSummary["class_type"] = classType?.Type == JTokenType.String ? classType.Value<string>() : "invalid-class-type";
            if (node["inputs"] is JObject inputs)
            {
                JObject inputSummary = [];
                foreach (JProperty inputProperty in inputs.Properties())
                {
                    inputSummary[inputProperty.Name] = DescribeInput(inputProperty.Value, validNodeIds);
                }
                nodeSummary["inputs"] = inputSummary;
            }
            else
            {
                nodeSummary["inputs"] = "invalid-inputs-shape";
            }
            nodes[nodeProperty.Name] = nodeSummary;
        }
        JObject summary = new()
        {
            ["node_count"] = nodes.Count,
            ["nodes"] = nodes
        };
        return summary.ToString(Formatting.None);
    }

    /// <summary>Describes one workflow input as a validated connection or fixed value-kind marker.</summary>
    private static JToken DescribeInput(JToken input, HashSet<string> validNodeIds)
    {
        if (input is JArray array && array.Count == 2)
        {
            JToken sourceToken = array[0];
            JToken outputToken = array[1];
            if (sourceToken is not null
                && (sourceToken.Type == JTokenType.String || sourceToken.Type == JTokenType.Integer)
                && outputToken?.Type == JTokenType.Integer
                && int.TryParse(outputToken.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int outputIndex)
                && outputIndex >= 0)
            {
                string sourceNode = sourceToken.ToString();
                if (validNodeIds.Contains(sourceNode))
                {
                    return new JObject()
                    {
                        ["source_node"] = sourceNode,
                        ["output_index"] = new JValue(outputIndex)
                    };
                }
            }
        }
        return $"redacted:{GetValueKind(input)}";
    }

    /// <summary>Maps a JSON token to a fixed value-kind name without reading its content.</summary>
    private static string GetValueKind(JToken input)
    {
        if (input is null)
        {
            return "null";
        }
        return input.Type switch
        {
            JTokenType.String => "string",
            JTokenType.Integer => "integer",
            JTokenType.Float => "float",
            JTokenType.Boolean => "boolean",
            JTokenType.Null or JTokenType.Undefined => "null",
            JTokenType.Array => "array",
            JTokenType.Object => "object",
            _ => "other"
        };
    }

    /// <summary>Creates a fixed, value-free status object.</summary>
    private static string FixedStatus(string status)
    {
        return new JObject() { ["status"] = status }.ToString(Formatting.None);
    }
}
