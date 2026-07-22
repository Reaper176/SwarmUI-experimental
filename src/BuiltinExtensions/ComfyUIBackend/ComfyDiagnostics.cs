using System.Globalization;
using System.IO;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Builds opaque, value-eliding diagnostic descriptions for Comfy workflows, typed inputs, and exceptions.</summary>
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
            parsed = ParseJson(workflow);
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
        JToken parsed;
        try
        {
            parsed = ParseJson(envelope);
        }
        catch
        {
            return FixedStatus("invalid-prompt-envelope-json");
        }
        try
        {
            if (parsed is not JObject root || root["prompt"] is not JObject graph)
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

    /// <summary>Describes the number of typed-input parameters without reading or formatting their names or values.</summary>
    public static string DescribeParameters(T2IParamInput input)
    {
        try
        {
            if (input?.InternalSet?.ValuesInput is null)
            {
                return FixedStatus("unavailable-parameter-summary");
            }
            int parameterCount = input.InternalSet.ValuesInput.Count;
            JObject summary = new()
            {
                ["parameter_count"] = parameterCount
            };
            return summary.ToString(Formatting.None);
        }
        catch
        {
            return FixedStatus("unavailable-parameter-summary");
        }
    }

    /// <summary>Describes an exception while redacting submitted content from JSON parser failures.</summary>
    public static string DescribeException(Exception exception)
    {
        try
        {
            if (exception is null)
            {
                return "Unknown error.";
            }
            if (ContainsJsonReaderException(exception))
            {
                return "JSON parsing failed (submitted content redacted).";
            }
            return exception.ReadableString();
        }
        catch
        {
            return "Error details unavailable.";
        }
    }

    /// <summary>Parses JSON without coercing ISO-looking strings into dates.</summary>
    private static JToken ParseJson(string json)
    {
        using StringReader stringReader = new(json);
        using JsonTextReader reader = new(stringReader)
        {
            DateParseHandling = DateParseHandling.None
        };
        return JToken.ReadFrom(reader);
    }

    /// <summary>Describes a parsed workflow graph without reproducing submitted identifiers or input values.</summary>
    private static string DescribeGraph(JObject graph)
    {
        Dictionary<string, string> validNodeAliases = [];
        int nodeNumber = 0;
        foreach (JProperty nodeProperty in graph.Properties())
        {
            nodeNumber++;
            if (IsValidNode(nodeProperty.Value))
            {
                validNodeAliases[nodeProperty.Name] = $"node_{nodeNumber}";
            }
        }
        JArray nodes = [];
        nodeNumber = 0;
        foreach (JProperty nodeProperty in graph.Properties())
        {
            nodeNumber++;
            nodes.Add(DescribeNode(nodeProperty.Value, $"node_{nodeNumber}", validNodeAliases));
        }
        JObject summary = new()
        {
            ["node_count"] = nodes.Count,
            ["nodes"] = nodes
        };
        return summary.ToString(Formatting.None);
    }

    /// <summary>Returns whether a graph node has the syntactically valid shape accepted as a connection source.</summary>
    private static bool IsValidNode(JToken nodeToken)
    {
        return nodeToken is JObject node && node["class_type"]?.Type == JTokenType.String && node["inputs"] is JObject;
    }

    /// <summary>Describes one graph node with generated aliases and value-free input metadata.</summary>
    private static JObject DescribeNode(JToken nodeToken, string nodeAlias, Dictionary<string, string> validNodeAliases)
    {
        string status;
        JObject inputs = null;
        if (nodeToken is not JObject node)
        {
            status = "invalid-node-shape";
        }
        else if (node["class_type"]?.Type != JTokenType.String)
        {
            status = "invalid-class-type";
            inputs = node["inputs"] as JObject;
        }
        else if (node["inputs"] is not JObject nodeInputs)
        {
            status = "invalid-inputs-shape";
        }
        else
        {
            status = "valid-node-shape";
            inputs = nodeInputs;
        }
        JArray inputSummaries = [];
        if (inputs is not null)
        {
            int inputNumber = 0;
            foreach (JProperty inputProperty in inputs.Properties())
            {
                inputNumber++;
                inputSummaries.Add(DescribeInput(inputProperty.Value, $"input_{inputNumber}", validNodeAliases));
            }
        }
        return new JObject()
        {
            ["node"] = nodeAlias,
            ["status"] = status,
            ["input_count"] = inputSummaries.Count,
            ["inputs"] = inputSummaries
        };
    }

    /// <summary>Describes one workflow input as a validated aliased connection or fixed value-kind marker.</summary>
    private static JObject DescribeInput(JToken input, string inputAlias, Dictionary<string, string> validNodeAliases)
    {
        JObject summary = new()
        {
            ["input"] = inputAlias
        };
        if (TryGetConnectionSourceAlias(input, validNodeAliases, out string sourceNodeAlias))
        {
            summary["kind"] = "connection";
            summary["source_node"] = sourceNodeAlias;
        }
        else
        {
            summary["kind"] = $"redacted:{GetValueKind(input)}";
        }
        return summary;
    }

    /// <summary>Returns a generated source-node alias only for valid two-item Comfy connections with a nonnegative int32 output index.</summary>
    private static bool TryGetConnectionSourceAlias(JToken input, Dictionary<string, string> validNodeAliases, out string sourceNodeAlias)
    {
        sourceNodeAlias = null;
        if (input is not JArray array || array.Count != 2)
        {
            return false;
        }
        JToken sourceToken = array[0];
        JToken outputToken = array[1];
        if (sourceToken is null || (sourceToken.Type != JTokenType.String && sourceToken.Type != JTokenType.Integer) || outputToken?.Type != JTokenType.Integer)
        {
            return false;
        }
        if (!int.TryParse(outputToken.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int outputIndex) || outputIndex < 0)
        {
            return false;
        }
        string sourceIdentifier = Convert.ToString(((JValue)sourceToken).Value, CultureInfo.InvariantCulture);
        if (sourceIdentifier is null || !validNodeAliases.TryGetValue(sourceIdentifier, out sourceNodeAlias))
        {
            sourceNodeAlias = null;
            return false;
        }
        return true;
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

    /// <summary>Returns whether an exception or any exception in its linear inner chain is a JSON reader failure.</summary>
    private static bool ContainsJsonReaderException(Exception exception)
    {
        Exception current = exception;
        while (current is not null)
        {
            if (current is JsonReaderException)
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }

    /// <summary>Creates a fixed, value-free status object.</summary>
    private static string FixedStatus(string status)
    {
        return new JObject() { ["status"] = status }.ToString(Formatting.None);
    }
}
