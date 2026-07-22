using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Parses Comfy-maintained submitted or private JSON without propagating content-bearing parser errors.</summary>
internal static class ComfySubmittedJson
{
    /// <summary>Fixed parser failure text that contains no submitted JSON or parser-derived location details.</summary>
    private const string ParserFailureMessage = "JSON parsing failed (submitted content redacted).";

    /// <summary>Parses a submitted or private JSON object through a value-free failure boundary.</summary>
    public static JObject ParseObject(string input)
    {
        try
        {
            return JObject.Parse(input);
        }
        catch (JsonReaderException)
        {
            throw new JsonReaderException(ParserFailureMessage);
        }
    }

    /// <summary>Unescapes a submitted workflow-tag string through a value-free failure boundary.</summary>
    public static string UnescapeString(string input)
    {
        try
        {
            return JObject.Parse("{ \"value\": \"" + input + "\" }")["value"].ToString();
        }
        catch (JsonReaderException)
        {
            throw new JsonReaderException(ParserFailureMessage);
        }
    }
}
