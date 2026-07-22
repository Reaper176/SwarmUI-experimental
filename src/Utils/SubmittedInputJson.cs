using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

namespace SwarmUI.Utils;

/// <summary>Parses browser/user-submitted JSON without retaining submitted content in parser failures.</summary>
public static class SubmittedInputJson
{
    /// <summary>Fixed parser failure text that contains no submitted content or native parser detail.</summary>
    private const string ParserFailureMessage = "JSON parsing failed (submitted content redacted).";

    /// <summary>Parses a submitted JSON object while replacing content-bearing JSON reader failures.</summary>
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

    /// <summary>Receives and parses one submitted WebSocket JSON object with the existing transport limits.</summary>
    public static async Task<JObject> ReceiveObject(WebSocket socket, TimeSpan maxDuration, long maxBytes)
    {
        byte[] data = await socket.ReceiveData(maxDuration, maxBytes);
        return ParseObject(Encoding.UTF8.GetString(data));
    }
}
