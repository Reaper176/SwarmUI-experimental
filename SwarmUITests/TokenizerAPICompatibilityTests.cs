using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SwarmUI.WebAPI;

namespace SwarmUITests;

/// <summary>Protects the tokenizer method signatures used by external extensions.</summary>
public class TokenizerAPICompatibilityTests : SwarmUITest
{
    /// <summary>Prepares the basic test environment.</summary>
    [OneTimeSetUp]
    public static void PreInit()
    {
        Setup();
    }

    /// <summary>Binding these delegates also verifies the original public signatures at compile time.</summary>
    [Test]
    public void OriginalTokenizerSignaturesRemainAvailable()
    {
        Func<string, bool, string, bool, Task<JObject>> count = UtilAPI.CountTokens;
        Func<string, bool, string, bool, Task<JObject>> detail = UtilAPI.TokenizeInDetail;
        Assert.That(count.Method.GetParameters()[0].Name, Is.EqualTo("text"));
        Assert.That(detail.Method.GetParameters()[0].Name, Is.EqualTo("text"));
        Assert.That(count.Method.GetParameters()[1].HasDefaultValue, Is.True);
        Assert.That(detail.Method.GetParameters()[1].HasDefaultValue, Is.True);
    }
}
