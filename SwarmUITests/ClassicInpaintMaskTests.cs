using System;
using ImageMagick;
using NUnit.Framework;
using SwarmUI.WebAPI;

namespace SwarmUITests;

/// <summary>Protects the CPU pixel operations used to prepare Classic Inpaint masks.</summary>
[TestFixture]
public class ClassicInpaintMaskTests
{
    /// <summary>Creates an RGBA mask with a fully transparent, black background.</summary>
    private static MagickImage CreateMask(uint width = 17, uint height = 17)
    {
        return new MagickImage(MagickColors.Transparent, width, height)
        {
            ColorType = ColorType.TrueColorAlpha,
            Depth = 8
        };
    }

    /// <summary>Selects the right half of a transparent mask.</summary>
    private static MagickImage CreateHalfMask()
    {
        MagickImage mask = CreateMask();
        using IPixelCollection<ushort> pixels = mask.GetPixels();
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 8; x < mask.Width; x++)
            {
                pixels.GetPixel(x, y).SetValues([ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue]);
            }
        }
        return mask;
    }

    /// <summary>Reads RGBA bytes independent of an encoder's chosen PNG color type.</summary>
    private static byte[] ReadPixels(MagickImage mask)
    {
        using IPixelCollection<ushort> pixels = mask.GetPixels();
        return pixels.ToByteArray(PixelMapping.RGBA);
    }

    /// <summary>Reads a mask pixel's red channel.</summary>
    private static byte ReadValue(MagickImage mask, int x, int y)
    {
        return ReadPixels(mask)[(y * (int)mask.Width + x) * 4];
    }

    /// <summary>Any nonzero channel selects a pixel, including color beneath transparent alpha.</summary>
    [TestCase(0, 0, 0, 0, 0)]
    [TestCase(1, 0, 0, 0, 255)]
    [TestCase(0, 1, 0, 0, 255)]
    [TestCase(0, 0, 1, 0, 255)]
    [TestCase(0, 0, 0, 1, 255)]
    [TestCase(0, 0, 0, 255, 255)]
    public static void AnyNonzeroChannelSelectsPixel(int red, int green, int blue, int alpha, int expected)
    {
        using MagickImage mask = CreateMask(1, 1);
        using (IPixelCollection<ushort> pixels = mask.GetPixels())
        {
            pixels.GetPixel(0, 0).SetValues([(ushort)(red * 257), (ushort)(green * 257), (ushort)(blue * 257), (ushort)(alpha * 257)]);
        }

        ClassicInpaintAPI.PrepareClassicInpaintMask(mask, 0, 0);

        Assert.That(ReadPixels(mask), Is.EqualTo(new byte[] { (byte)expected, (byte)expected, (byte)expected, 255 }));
    }

    /// <summary>Images without alpha retain the existing opaque-black selection behavior.</summary>
    [Test]
    public static void OpaqueRgbBlackIsSelected()
    {
        using MagickImage mask = new(MagickColors.Black, 3, 2) { ColorType = ColorType.TrueColor, Depth = 8 };

        ClassicInpaintAPI.PrepareClassicInpaintMask(mask, 0, 0);

        Assert.That(ReadPixels(mask), Is.All.EqualTo(255));
        Assert.That(mask.Width, Is.EqualTo(3));
        Assert.That(mask.Height, Is.EqualTo(2));
    }

    /// <summary>Expansion uses a low threshold so the blurred mask grows beyond its original edge.</summary>
    [Test]
    public static void ExpansionUsesLowThresholdAndProducesBinaryPixels()
    {
        using MagickImage mask = CreateHalfMask();

        ClassicInpaintAPI.PrepareClassicInpaintMask(mask, 2, 0);

        Assert.That(ReadValue(mask, 6, 8), Is.EqualTo(255));
        Assert.That(ReadValue(mask, 4, 8), Is.Zero);
        Assert.That(ReadValue(mask, 8, 8), Is.EqualTo(255));
        byte[] rgba = ReadPixels(mask);
        for (int i = 0; i < rgba.Length; i += 4)
        {
            Assert.That(rgba[i], Is.EqualTo(0).Or.EqualTo(255));
            Assert.That(rgba[i + 1], Is.EqualTo(rgba[i]));
            Assert.That(rgba[i + 2], Is.EqualTo(rgba[i]));
            Assert.That(rgba[i + 3], Is.EqualTo(255));
        }
    }

    /// <summary>Feathering smooths both sides of the selected edge while preserving opaque output.</summary>
    [Test]
    public static void FeatherProducesGrayscaleTransition()
    {
        using MagickImage mask = CreateHalfMask();

        ClassicInpaintAPI.PrepareClassicInpaintMask(mask, 0, 4);

        byte left = ReadValue(mask, 7, 8);
        byte right = ReadValue(mask, 8, 8);
        Assert.That(left, Is.InRange(1, 127));
        Assert.That(right, Is.InRange(128, 254));
        Assert.That(Math.Abs(left + right - 255), Is.LessThanOrEqualTo(2));
        Assert.That(ReadValue(mask, 0, 8), Is.Zero);
        Assert.That(ReadValue(mask, 16, 8), Is.EqualTo(255));
        byte[] rgba = ReadPixels(mask);
        for (int i = 0; i < rgba.Length; i += 4)
        {
            Assert.That(rgba[i + 1], Is.EqualTo(rgba[i]));
            Assert.That(rgba[i + 2], Is.EqualTo(rgba[i]));
            Assert.That(rgba[i + 3], Is.EqualTo(255));
        }
    }

    /// <summary>The expanded binary mask is feathered afterward, retaining soft pixels around the grown edge.</summary>
    [Test]
    public static void ExpansionPrecedesFeather()
    {
        using MagickImage expanded = CreateHalfMask();
        using MagickImage feathered = CreateHalfMask();
        using MagickImage both = CreateHalfMask();

        ClassicInpaintAPI.PrepareClassicInpaintMask(expanded, 2, 0);
        ClassicInpaintAPI.PrepareClassicInpaintMask(feathered, 0, 4);
        ClassicInpaintAPI.PrepareClassicInpaintMask(both, 2, 4);

        Assert.That(ReadValue(expanded, 6, 8), Is.EqualTo(255));
        Assert.That(ReadValue(both, 6, 8), Is.InRange(1, 254));
        Assert.That(ReadValue(both, 5, 8), Is.GreaterThan(ReadValue(feathered, 5, 8)));
    }

    /// <summary>The prepared mask remains an opaque grayscale mask after the route's PNG encode/decode path.</summary>
    [Test]
    public static void PreparedMaskSurvivesPngRoundTrip()
    {
        using MagickImage mask = CreateHalfMask();
        ClassicInpaintAPI.PrepareClassicInpaintMask(mask, 2, 4);

        using MagickImage decoded = new(mask.ToByteArray(MagickFormat.Png));

        Assert.That(decoded.Width, Is.EqualTo(mask.Width));
        Assert.That(decoded.Height, Is.EqualTo(mask.Height));
        byte[] expected = ReadPixels(mask);
        byte[] actual = ReadPixels(decoded);
        for (int i = 0; i < actual.Length; i += 4)
        {
            // Q16 blur results can round one byte differently when encoded as an 8-bit PNG.
            Assert.That(Math.Abs(actual[i] - expected[i]), Is.LessThanOrEqualTo(1));
            Assert.That(actual[i + 1], Is.EqualTo(actual[i]));
            Assert.That(actual[i + 2], Is.EqualTo(actual[i]));
            Assert.That(actual[i + 3], Is.EqualTo(255));
        }
    }
}
