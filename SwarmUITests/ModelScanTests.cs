using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SwarmUI.Core;
using SwarmUI.Text2Image;

namespace SwarmUITests;

/// <summary>Checks scan snapshots against the live metadata fingerprint contract.</summary>
[TestFixture]
[NonParallelizable]
public class ModelScanTests : SwarmUITest
{
    /// <summary>Temporary folder isolated from user models and metadata caches.</summary>
    private string Folder;

    /// <summary>Initializes test conventions.</summary>
    [OneTimeSetUp]
    public static void PreInit()
    {
        Setup();
    }

    /// <summary>Creates an isolated filesystem fixture.</summary>
    [SetUp]
    public void BeforeTest()
    {
        Folder = Directory.CreateTempSubdirectory("swarm-model-scan-").FullName;
    }

    /// <summary>Removes only this test's temporary files.</summary>
    [TearDown]
    public void AfterTest()
    {
        Directory.Delete(Folder, true);
    }

    /// <summary>Captures the same conservative file-name membership used by scanning.</summary>
    private HashSet<string> Snapshot()
    {
        MethodInfo method = typeof(T2IModelHandler).GetMethod("GetModelScanFileNames", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        return (HashSet<string>)method.Invoke(null, [Directory.EnumerateFiles(Folder)]);
    }

    /// <summary>Reads the private fingerprint helper without exposing a production test API.</summary>
    private string Fingerprint(HashSet<string> files)
    {
        MethodInfo method = typeof(T2IModelHandler).GetMethod("GetModelSidecarFingerprint", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(string), typeof(HashSet<string>)], null);
        Assert.That(method, Is.Not.Null);
        return (string)method.Invoke(null, [Path.Combine(Folder, "model"), files]);
    }

    /// <summary>The optimization must not invalidate existing cache records.</summary>
    [Test]
    public void SnapshotPreservesFingerprintFormat()
    {
        string sidecar = Path.Combine(Folder, "model.swarm.json");
        File.WriteAllText(sidecar, "{}");
        FileInfo info = new(sidecar);
        string expected = FormattableString.Invariant($".swarm.json:{info.Length}:{info.LastWriteTimeUtc.Ticks}|.json:missing|.cm-info.json:missing|.civitai.info:missing");
        Assert.That(Fingerprint(Snapshot()), Is.EqualTo(expected));
        Assert.That(Fingerprint(Snapshot()), Is.EqualTo(Fingerprint(null)));
    }

    /// <summary>Present sidecars are statted again, not cached with stale size or timestamps.</summary>
    [Test]
    public void ExistingSidecarChangesRemainVisible()
    {
        string sidecar = Path.Combine(Folder, "model.json");
        File.WriteAllText(sidecar, "{}");
        HashSet<string> files = Snapshot();
        string before = Fingerprint(files);
        File.WriteAllText(sidecar, "{\"changed\":true}");
        File.SetLastWriteTimeUtc(sidecar, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.That(Fingerprint(files), Is.Not.EqualTo(before));
        Assert.That(Fingerprint(files), Is.EqualTo(Fingerprint(null)));
    }

    /// <summary>A new scan observes additions and deletions without persistent negative caching.</summary>
    [Test]
    public void FreshSnapshotObservesAddedAndRemovedSidecars()
    {
        string missing = Fingerprint(Snapshot());
        string sidecar = Path.Combine(Folder, "model.civitai.info");
        File.WriteAllText(sidecar, "{}");
        HashSet<string> files = Snapshot();
        Assert.That(Fingerprint(files), Is.Not.EqualTo(missing));
        Assert.That(Fingerprint(files), Is.EqualTo(Fingerprint(null)));
        File.Delete(sidecar);
        Assert.That(Fingerprint(files), Is.EqualTo(missing));
        Assert.That(Fingerprint(Snapshot()), Is.EqualTo(missing));
    }

    /// <summary>Mixed-case membership must defer to the actual filesystem's case semantics.</summary>
    [Test]
    public void MixedCaseSidecarsMatchLiveFilesystemChecks()
    {
        File.WriteAllText(Path.Combine(Folder, "MODEL.JSON"), "{}");
        Assert.That(Fingerprint(Snapshot()), Is.EqualTo(Fingerprint(null)));
    }

    /// <summary>Unicode normalization and case equivalences must remain the filesystem's decision.</summary>
    [Test]
    public void UnicodeFileNamesFallBackToLiveChecks()
    {
        File.WriteAllText(Path.Combine(Folder, "caf\u00e9.safetensors"), "");
        File.WriteAllText(Path.Combine(Folder, "cafe\u0301.json"), "{}");
        HashSet<string> files = Snapshot();
        Assert.That(files, Is.Null);
        string before = Fingerprint(files);
        File.WriteAllText(Path.Combine(Folder, "model.json"), "{}");
        Assert.That(Fingerprint(files), Is.Not.EqualTo(before));
        Assert.That(Fingerprint(files), Is.EqualTo(Fingerprint(null)));
    }

    /// <summary>A snapshot avoids probing sidecars known to be absent at enumeration time.</summary>
    [Test]
    public void MissingSidecarUsesScanSnapshotUntilNextRefresh()
    {
        HashSet<string> files = Snapshot();
        string missing = Fingerprint(files);
        File.WriteAllText(Path.Combine(Folder, "model.cm-info.json"), "{}");
        Assert.That(Fingerprint(files), Is.EqualTo(missing));
        Assert.That(Fingerprint(Snapshot()), Is.Not.EqualTo(missing));
    }

    /// <summary>Bounded traversal preserves nested models, hidden-file filtering, and root precedence.</summary>
    [Test]
    public void TraversalPreservesCatalogAndDuplicatePaths()
    {
        string first = Directory.CreateDirectory(Path.Combine(Folder, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(Folder, "second")).FullName;
        Directory.CreateDirectory(Path.Combine(first, "nested"));
        Directory.CreateDirectory(Path.Combine(first, ".hidden"));
        File.WriteAllText(Path.Combine(first, "root.ckpt"), "");
        File.WriteAllText(Path.Combine(first, "nested", "child.ckpt"), "");
        File.WriteAllText(Path.Combine(first, ".hidden", "ignored.ckpt"), "");
        File.WriteAllText(Path.Combine(first, ".ignored.ckpt"), "");
        File.WriteAllText(Path.Combine(second, "root.ckpt"), "");
        ConcurrentDictionary<string, T2IModel> models = new();
        T2IModelHandler handler = new() { ModelType = "LoRA" };
        try
        {
            handler.AddAllFromFolder(first, "", models);
            handler.AddAllFromFolder(second, "", models);
            Assert.That(models.Keys, Is.EquivalentTo(new[] { "root.ckpt", "nested/child.ckpt" }));
            Assert.That(models["root.ckpt"].RawFilePath, Is.EqualTo(Path.Combine(first, "root.ckpt").Replace('\\', '/')));
            Assert.That(models["root.ckpt"].OtherPaths, Is.EquivalentTo(new[] { Path.Combine(second, "root.ckpt").Replace('\\', '/') }));
        }
        finally
        {
            Program.ModelRefreshEvent -= handler.Refresh;
        }
    }
}
