using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Builds value-eliding diagnostic descriptions for Comfy workflows and typed inputs.</summary>
internal static class ComfyDiagnostics
{
    /// <summary>Describes a raw workflow without reproducing submitted input values.</summary>
    public static string DescribeWorkflow(string workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow))
        {
            return FixedStatus("empty-workflow");
        }
        try
        {
            return DescribeWorkflow(JToken.Parse(workflow));
        }
        catch
        {
            return FixedStatus("invalid-workflow-json");
        }
    }

    /// <summary>Describes a parsed workflow without reproducing submitted input values.</summary>
    public static string DescribeWorkflow(JToken workflow)
    {
        try
        {
            JObject graph = GetGraph(workflow);
            if (graph is null)
            {
                return FixedStatus("invalid-workflow-shape");
            }
            HashSet<string> nodeIds = [.. graph.Properties().Select(property => property.Name)];
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
                        inputSummary[inputProperty.Name] = DescribeInput(inputProperty.Value, nodeIds);
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
        catch
        {
            return FixedStatus("unavailable-workflow-summary");
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
    public static string DescribeTag(string tagName)
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

    /// <summary>Gets a direct graph or the graph inside a Comfy prompt envelope.</summary>
    private static JObject GetGraph(JToken workflow)
    {
        if (workflow is not JObject root)
        {
            return null;
        }
        if (root["prompt"] is JObject prompt)
        {
            return prompt;
        }
        return root;
    }

    /// <summary>Describes one workflow input as a validated connection or fixed value-kind marker.</summary>
    private static JToken DescribeInput(JToken input, HashSet<string> nodeIds)
    {
        if (input is JArray array && array.Count == 2 && array[1]?.Type == JTokenType.Integer)
        {
            JToken sourceToken = array[0];
            if (sourceToken is not null && (sourceToken.Type == JTokenType.String || sourceToken.Type == JTokenType.Integer))
            {
                string sourceNode = sourceToken.ToString();
                if (nodeIds.Contains(sourceNode))
                {
                    return new JObject()
                    {
                        ["source_node"] = sourceNode,
                        ["output_index"] = array[1].DeepClone()
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
