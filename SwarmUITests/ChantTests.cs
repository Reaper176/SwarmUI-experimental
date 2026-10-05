using NUnit.Framework;
using SwarmUI.Utils;
using SwarmUI.Text2Image;

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
