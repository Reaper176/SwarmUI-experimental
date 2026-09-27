using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SwarmUI.Text2Image;

namespace SwarmUITests;

/// <summary>Checks that Qwen3.5-2B routing is limited to projection-enabled Anima.</summary>
[TestFixture]
public class AnimaQwen35Tests
{
    /// <summary>Creates an Anima tensor header without loading any model weights.</summary>
    private static JObject Header(string prefix = "")
    {
        return new JObject
        {
            [$"{prefix}t_embedder.1.linear_2.weight"] = new JObject(),
            [$"{prefix}llm_adapter.blocks.0.self_attn.v_proj.weight"] = new JObject(),
            [$"{prefix}blocks.27.adaln_modulation_cross_attn.2.weight"] = new JObject(),
            [$"{prefix}llm_adapter.source_proj.weight"] = new JObject { ["shape"] = new JArray(1024, 2048) }
        };
    }

    /// <summary>Recognizes supported checkpoint wrappers independently of filenames.</summary>
    [TestCase("")]
    [TestCase("net.")]
    [TestCase("diffusion_model.")]
    [TestCase("model.diffusion_model.")]
    [TestCase("transformer.")]
    public void RecognizesProjectionVariant(string prefix)
    {
        Assert.That(T2IModelClassSorter.IsAnimaQwen35(Header(prefix)), Is.True);
    }

    /// <summary>Standard Anima has no source projection.</summary>
    [Test]
    public void LeavesStandardAnimaUnchanged()
    {
        JObject header = Header();
        header.Remove("llm_adapter.source_proj.weight");
        Assert.That(T2IModelClassSorter.IsAnimaQwen35(header), Is.False);
    }

    /// <summary>Other widths and architectures must not use the 2B loader.</summary>
    [TestCase(1024, 1024)]
    [TestCase(2048, 1024)]
    [TestCase(1024, 2560)]
    public void RejectsOtherProjectionShapes(int outputWidth, int inputWidth)
    {
        JObject header = Header();
        header["llm_adapter.source_proj.weight"]["shape"] = new JArray(outputWidth, inputWidth);
        Assert.That(T2IModelClassSorter.IsAnimaQwen35(header), Is.False);
    }

    /// <summary>The distinct 3.8B path remains separate.</summary>
    [Test]
    public void RejectsAnima38()
    {
        JObject header = Header();
        header["blocks.51.adaln_modulation_cross_attn.2.weight"] = new JObject();
        Assert.That(T2IModelClassSorter.IsAnimaQwen35(header), Is.False);
    }

    /// <summary>A similarly named tensor alone is insufficient.</summary>
    [Test]
    public void RejectsNonAnima()
    {
        JObject header = Header();
        header.Remove("t_embedder.1.linear_2.weight");
        Assert.That(T2IModelClassSorter.IsAnimaQwen35(header), Is.False);
    }
}
