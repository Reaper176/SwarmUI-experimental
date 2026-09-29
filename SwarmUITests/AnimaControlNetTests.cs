using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace SwarmUITests;

/// <summary>Checks Anima ControlNet classification, node requirements, and graph connections without loading weights.</summary>
[TestFixture]
[NonParallelizable]
public class AnimaControlNetTests : SwarmUITest
{
    /// <summary>Initializes test conventions.</summary>
    [OneTimeSetUp]
    public static void PreInit()
    {
        Setup();
    }

    /// <summary>Builds only the host state consumed by the ControlNet graph helper.</summary>
    private static WorkflowGenerator Generator(string hostClass = "anima")
    {
        WorkflowGenerator g = new()
        {
            Workflow = new JObject(),
            FinalLoadedModel = new T2IModel { ModelClass = new T2IModelClass { ID = hostClass, CompatClass = T2IModelClassSorter.CompatAnima } },
            Features = [ComfyCapabilityCatalog.AnimaVaceControlNetFeature, ComfyCapabilityCatalog.AnimaLLLiteRemapFeature]
        };
        g.CurrentModel = new(["model", 0], g, WGNodeData.DT_MODEL, T2IModelClassSorter.CompatAnima);
        g.CurrentVae = new(["vae", 0], g, WGNodeData.DT_VAE, T2IModelClassSorter.CompatAnima);
        return g;
    }

    /// <summary>Creates a classified ControlNet without filesystem access.</summary>
    private static T2IModel Control(bool vace)
    {
        return new T2IModel
        {
            Name = "control.safetensors",
            ModelClass = new T2IModelClass { ID = vace ? "anima/controlnet-vace" : "anima/controlnet", CompatClass = T2IModelClassSorter.CompatAnima }
        };
    }

    /// <summary>VACE projection and Anima block signatures must both be present.</summary>
    [Test]
    public void RecognizesVaceSeparatelyFromLLLite()
    {
        JObject header = new()
        {
            ["control_blocks.0.after_proj.weight"] = new JObject(),
            ["control_blocks.0.block.adaln_modulation_cross_attn.2.weight"] = new JObject()
        };
        Assert.That(T2IModelClassSorter.IsAnimaVaceControlNet(header), Is.True);
        header.Remove("control_blocks.0.block.adaln_modulation_cross_attn.2.weight");
        Assert.That(T2IModelClassSorter.IsAnimaVaceControlNet(header), Is.False);
        Assert.That(T2IModelClassSorter.IsAnimaVaceControlNet(null), Is.False);
    }

    /// <summary>A partially installed backend must not advertise the VACE node chain.</summary>
    [TestCase(null, true)]
    [TestCase("ACN_ControlNetLoaderAdvanced", false)]
    [TestCase("AnimaVACEControlNetRemap", false)]
    [TestCase("ACN_AdvancedControlNetApply_v2", false)]
    public void RequiresCompleteVaceNodeChain(string missing, bool supported)
    {
        HashSet<string> nodes = ["ACN_ControlNetLoaderAdvanced", "AnimaVACEControlNetRemap", "ACN_AdvancedControlNetApply_v2"];
        if (missing is not null)
        {
            nodes.Remove(missing);
        }
        HashSet<string> features = ComfyCapabilityCatalog.Interpret(nodes, [ComfyCapabilityCatalog.AnimaVaceControlNetFeature],
            new HashSet<string>(), ComfyCapabilityCatalog.CreateNodeToFeatureMap(), new HashSet<string>(), "/");
        Assert.That(features.Contains(ComfyCapabilityCatalog.AnimaVaceControlNetFeature), Is.EqualTo(supported));
    }

    /// <summary>VACE keeps both conditioning chains and supplies the host model, VAE, and timing controls.</summary>
    [TestCase("anima")]
    [TestCase("anima-3_8b")]
    public void VaceUsesRemappedControlAndVae(string hostClass)
    {
        WorkflowGenerator g = Generator(hostClass);
        JArray positive = (JArray)g.FinalPrompt.DeepClone(), negative = (JArray)g.FinalNegativePrompt.DeepClone();
        WGNodeData image = new(["image", 0], g, WGNodeData.DT_IMAGE, T2IModelClassSorter.CompatAnima);
        g.ApplyAnimaControlNet(Control(true), image, 0.6, 0.1, 0.8);
        JObject apply = (JObject)g.Workflow[g.FinalPrompt[0].ToString()]["inputs"];
        JObject remap = (JObject)g.Workflow[apply["control_net"][0].ToString()];
        JObject loader = (JObject)g.Workflow[remap["inputs"]["control_net"][0].ToString()];
        Assert.That(g.Workflow[g.FinalPrompt[0].ToString()].Value<string>("class_type"), Is.EqualTo("ACN_AdvancedControlNetApply_v2"));
        Assert.That(loader.Value<string>("class_type"), Is.EqualTo("ACN_ControlNetLoaderAdvanced"));
        Assert.That(loader["inputs"].Value<string>("cnet"), Is.EqualTo("control.safetensors"));
        Assert.That(remap.Value<string>("class_type"), Is.EqualTo("AnimaVACEControlNetRemap"));
        Assert.That(JToken.DeepEquals(remap["inputs"]["model"], g.CurrentModel.Path), Is.True);
        Assert.That(remap["inputs"].Value<bool>("auto_remap"), Is.True);
        Assert.That(JToken.DeepEquals(apply["positive"], positive), Is.True);
        Assert.That(JToken.DeepEquals(apply["negative"], negative), Is.True);
        Assert.That(JToken.DeepEquals(apply["vae_optional"], g.CurrentVae.Path), Is.True);
        Assert.That(apply.Value<double>("strength"), Is.EqualTo(0.6));
        Assert.That(apply.Value<double>("start_percent"), Is.EqualTo(0.1));
        Assert.That(apply.Value<double>("end_percent"), Is.EqualTo(0.8));
        Assert.That(g.FinalNegativePrompt[0].ToString(), Is.EqualTo(g.FinalPrompt[0].ToString()));
        Assert.That(g.FinalNegativePrompt[1].Value<int>(), Is.EqualTo(1));
        Assert.That(g.Workflow.Properties().Count(), Is.EqualTo(3));
    }

    /// <summary>LLLite remaps the model and forwards an inpainting mask without changing prompt conditioning.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void LLLiteRemapsOnExpandedAnima(bool masked)
    {
        WorkflowGenerator g = Generator("anima-3_8b");
        if (masked)
        {
            g.FinalMask = ["mask", 0];
        }
        JArray positive = (JArray)g.FinalPrompt.DeepClone();
        WGNodeData image = new(["image", 0], g, WGNodeData.DT_IMAGE, T2IModelClassSorter.CompatAnima);
        g.ApplyAnimaControlNet(Control(false), image, 0.6, 0, 0.8);
        JObject node = (JObject)g.Workflow[g.CurrentModel.Path[0].ToString()];
        Assert.That(node.Value<string>("class_type"), Is.EqualTo("AnimaLLLiteRemapApply"));
        Assert.That(node["inputs"].Value<bool>("auto_remap"), Is.True);
        Assert.That(node["inputs"].Value<bool>("extend_to_new_layers"), Is.False);
        Assert.That(((JObject)node["inputs"]).ContainsKey("mask"), Is.EqualTo(masked));
        Assert.That(JToken.DeepEquals(g.FinalPrompt, positive), Is.True);
    }

    /// <summary>Missing custom nodes fail before emitting an unusable partial graph.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void RejectsMissingCapability(bool vace)
    {
        WorkflowGenerator g = Generator();
        g.Features.Clear();
        WGNodeData image = new(["image", 0], g, WGNodeData.DT_IMAGE, T2IModelClassSorter.CompatAnima);
        Assert.Throws<SwarmUserErrorException>(() => g.ApplyAnimaControlNet(Control(vace), image, 0.6, 0, 0.8));
        Assert.That(g.Workflow.Count, Is.Zero);
    }

    /// <summary>Multiple slots chain their conditioning rather than overwriting earlier controls.</summary>
    [Test]
    public void VacePreservesPreviousControlConditioning()
    {
        WorkflowGenerator g = Generator();
        WGNodeData image = new(["image", 0], g, WGNodeData.DT_IMAGE, T2IModelClassSorter.CompatAnima);
        g.ApplyAnimaControlNet(Control(true), image, 0.4, 0, 0.8);
        JArray previous = (JArray)g.FinalPrompt.DeepClone();
        g.ApplyAnimaControlNet(Control(true), image, 0.6, 0, 0.8);
        Assert.That(JToken.DeepEquals(g.Workflow[g.FinalPrompt[0].ToString()]["inputs"]["positive"], previous), Is.True);
    }

    /// <summary>Explicit custom-node restrictions must not silently use ordinary ControlNet nodes.</summary>
    [Test]
    public void RespectsCustomNodeRestriction()
    {
        WorkflowGenerator g = Generator();
        WGNodeData image = new(["image", 0], g, WGNodeData.DT_IMAGE, T2IModelClassSorter.CompatAnima);
        bool original = WorkflowGenerator.RestrictCustomNodes;
        try
        {
            WorkflowGenerator.RestrictCustomNodes = true;
            Assert.Throws<SwarmUserErrorException>(() => g.ApplyAnimaControlNet(Control(true), image, 0.6, 0, 0.8));
        }
        finally
        {
            WorkflowGenerator.RestrictCustomNodes = original;
        }
    }

    /// <summary>The new classification triggers a one-time recheck only for relevant cached ControlNets.</summary>
    [TestCase(null, 5, true)]
    [TestCase("anima/controlnet", 5, true)]
    [TestCase("anima/controlnet-vace", 6, false)]
    [TestCase("stable-diffusion-xl-v1-base/controlnet", 5, false)]
    public void RefreshesRelevantControlNetCache(string modelClass, int revision, bool stale)
    {
        MethodInfo method = typeof(T2IModelHandler).GetMethod("IsModelClassCacheStale", BindingFlags.Static | BindingFlags.NonPublic);
        T2IModelHandler.ModelMetadataStore metadata = new() { ModelClassType = modelClass, ModelClassRevision = revision };
        Assert.That(method, Is.Not.Null);
        Assert.That((bool)method.Invoke(null, [metadata, "ControlNet"]), Is.EqualTo(stale));
    }
}
