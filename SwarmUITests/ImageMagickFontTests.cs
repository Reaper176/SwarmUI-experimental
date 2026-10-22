using System;
using System.IO;
using ImageMagick;
using ImageMagick.Drawing;
using NUnit.Framework;
using SwarmUI.Builtin_GridGeneratorExtension;

namespace SwarmUITests;

/// <summary>Checks the bundled server fonts instead of accepting ImageMagick's silent system-font fallback.</summary>
[TestFixture]
[NonParallelizable]
public class ImageMagickFontTests : SwarmUITest
{
    /// <summary>Restores the test host's working directory after resolving Swarm's runtime asset paths.</summary>
    private string PreviousDirectory;

    /// <summary>Uses the repository root, matching Swarm's launcher, without starting the application.</summary>
    [OneTimeSetUp]
    public void PreInit()
    {
        Setup();
        PreviousDirectory = Directory.GetCurrentDirectory();
        DirectoryInfo root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "SwarmUI.csproj")))
        {
            root = root.Parent;
        }
        Assert.That(root, Is.Not.Null, "The checked-out repository must contain the bundled fonts.");
        Directory.SetCurrentDirectory(root.FullName);
    }

    /// <summary>Leaves other test fixtures' file lookups unchanged.</summary>
    [OneTimeTearDown]
    public void Cleanup()
    {
        Directory.SetCurrentDirectory(PreviousDirectory);
    }

    /// <summary>Unifont's Latin glyphs have known advances; the broken WOFF2 fallback returns different metrics.</summary>
    [TestCase(1f, 24, 16)]
    [TestCase(2f, 48, 32)]
    public static void GridUsesBundledUnifont(float sizeMultiplier, int width, int height)
    {
        ITypeMetric metrics = GridGeneratorExtension.GetFont(sizeMultiplier).FontTypeMetrics("ABC");
        Assert.That(metrics.TextWidth, Is.EqualTo(width));
        Assert.That(metrics.TextHeight, Is.EqualTo(height));
    }

    /// <summary>The bundled Inter font used by audio previews must load as a real font file.</summary>
    [Test]
    public static void AudioPreviewFontLoadsWithExpectedMetrics()
    {
        IDrawables<ushort> font = new Drawables().Font("src/wwwroot/fonts/Inter.ttf", FontStyleType.Normal, FontWeight.Bold, FontStretch.Normal).FontPointSize(32);
        ITypeMetric metrics = font.FontTypeMetrics("Audio 01:23");
        Assert.That(metrics.TextWidth, Is.EqualTo(180));
        Assert.That(metrics.TextHeight, Is.EqualTo(39));
    }
}
