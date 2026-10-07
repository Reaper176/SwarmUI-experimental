using NUnit.Framework;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using SwarmUI.WebAPI;
using Newtonsoft.Json.Linq;

namespace SwarmUITests;

public class ChantTests : SwarmUITest
{
    /// <summary>Prepares the basic test environment.</summary>
    [OneTimeSetUp]
    public static void PreInit()
    {
        Setup();
    }

    [Test]
    public void ParsesDemoFormatAndPreservesContent()
    {
        System.Collections.Generic.Dictionary<string, AutoCompleteListHelper.Chant> chants = AutoCompleteListHelper.ParseChants("""
            [{"name":"Basic-NegativePrompt","terms":"Basic,Negative","content":"(worst quality, low quality, normal quality)","color":3}]
            """);
        Assert.That(chants["basic-negativeprompt"].Content, Is.EqualTo("(worst quality, low quality, normal quality)"));
        Assert.That(chants["Basic-NegativePrompt"].Terms, Is.EqualTo("Basic,Negative"));
        Assert.That(chants["Basic-NegativePrompt"].Color, Is.EqualTo(3));
    }

    [TestCase("{}")]
    [TestCase("[{\"name\":\"missing-content\"}]")]
    [TestCase("[{\"name\":\"x\",\"content\":\"one\"},{\"name\":\"X\",\"content\":\"two\"}]")]
    public void RejectsInvalidOrAmbiguousFiles(string json)
    {
        Assert.Catch(() => AutoCompleteListHelper.ParseChants(json));
    }

    /// <summary>Editing one chant preserves other chants and unknown metadata.</summary>
    [Test]
    public void EditingPreservesUnrelatedChantData()
    {
        JObject edit = JObject.Parse("""{"name":"Renamed","terms":"Light","content":"soft light","color":2,"description":"Soft lighting","thumbnail":""}""");
        JArray result = ChantsAPI.UpdateDocument("""[{"name":"Old","content":"old","custom":42},{"name":"Other","content":"unchanged"}]""", "Old", edit);
        Assert.That(result[0]["custom"].Value<int>(), Is.EqualTo(42));
        Assert.That(result[0]["name"].Value<string>(), Is.EqualTo("Renamed"));
        Assert.That(result[1]["content"].Value<string>(), Is.EqualTo("unchanged"));
        Assert.That(AutoCompleteListHelper.ParseChants(result.ToString())["Renamed"].Content, Is.EqualTo("soft light"));
    }

    /// <summary>Names remain unambiguous and thumbnail data cannot contain active content.</summary>
    [TestCase("Other", "")]
    [TestCase("New", "data:image/svg+xml;base64,PHN2Zz4=")]
    [TestCase("New", "https://example.com/tracker.png")]
    [TestCase("New", "data:image/png;base64,bm90YW5pbWFnZQ==")]
    [TestCase("Bad:Name", "")]
    [TestCase(" LeadingSpace", "")]
    public void RejectsDuplicateNamesAndUnsafeThumbnails(string name, string thumbnail)
    {
        JObject edit = new() { ["name"] = name, ["content"] = "updated", ["terms"] = "", ["color"] = 1, ["description"] = "", ["thumbnail"] = thumbnail };
        Assert.Catch(() => ChantsAPI.UpdateDocument("""[{"name":"Old","content":"old"},{"name":"Other","content":"unchanged"}]""", "Old", edit));
    }

    /// <summary>Optional card metadata stays outside the prompt expansion text.</summary>
    [Test]
    public void CreatesChantWithEmbeddedThumbnail()
    {
        JObject edit = new()
        {
            ["name"] = "New", ["content"] = "soft light", ["terms"] = "Light", ["color"] = 2,
            ["description"] = "A gentle lighting treatment.",
            ["thumbnail"] = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aY9sAAAAASUVORK5CYII="
        };
        JArray result = ChantsAPI.UpdateDocument("[]", "", edit);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0]["description"].Value<string>(), Is.EqualTo("A gentle lighting treatment."));
        Assert.That(AutoCompleteListHelper.ParseChants(result.ToString())["New"].Content, Is.EqualTo("soft light"));
    }

    /// <summary>Verifies expansion uses only the selected source and preserves unknown tags.</summary>
    [Test]
    public void ExpandsSelectedChantsForGenerationAndTokenCounts()
    {
        string source = "chant-unit-test.json";
        AutoCompleteListHelper.ChantFileNames.Add(source);
        AutoCompleteListHelper.ChantLists[source] = AutoCompleteListHelper.ParseChants("""
            [{"name":"Basic-NegativePrompt","content":"(worst quality, low quality, normal quality)"}]
            """);
        try
        {
            string content = "(worst quality, low quality, normal quality)";
            T2IPromptHandling.PromptTagContext context = new() { ChantSource = source };
            Assert.That(T2IPromptHandling.PromptTagProcessors["chant"]("basic-negativeprompt", context), Is.EqualTo(content));
            Assert.That(T2IPromptHandling.ProcessPromptLikeForLength("before <chant:Basic-NegativePrompt> after", source), Is.EqualTo($"before {content} after"));
            Assert.That(T2IPromptHandling.ProcessPromptLikeForLength("<chant:Unknown>", source), Is.EqualTo("<chant:Unknown>"));
            Assert.That(T2IPromptHandling.ProcessPromptLikeForLength("<chant:Basic-NegativePrompt>", ""), Is.EqualTo("<chant:Basic-NegativePrompt>"));
        }
        finally
        {
            AutoCompleteListHelper.ChantFileNames.Remove(source);
            AutoCompleteListHelper.ChantLists.TryRemove(source, out _);
        }
    }
}
