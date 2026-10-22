using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ImageMagick;
using NUnit.Framework;
using SwarmUI.Media;
using SwarmImage = SwarmUI.Utils.Image;

namespace SwarmUITests;

/// <summary>CPU-only image codec and metadata compatibility checks for the ImageMagick migration.</summary>
[TestFixture]
public class ImageMagickMigrationTests : SwarmUITest
{
    /// <summary>Metadata includes characters outside both ASCII and the Unicode basic multilingual plane.</summary>
    private const string Metadata = "{\"sui_image_params\":{\"prompt\":\"café — 雪 😺\",\"seed\":123},\"extra\":\"line one\\nline two\"}";

    /// <summary>A valid 1x1 RGBA APNG with red and blue frames lasting 100 and 200 milliseconds.</summary>
    private const string TwoFrameApng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACGFjVEwAAAACAAAAAPONk3AAAAAaZmNUTAAAAAAAAAABAAAAAQAAAAAAAAAAAAEACgAAWn8w0AAAAA1JREFUeJxj+M/A8B8ABQAB/4mZPR0AAAAaZmNUTAAAAAEAAAABAAAAAQAAAAAAAAAAAAIACgAAhqyg1AAAABFmZEFUAAAAAnicY2Bg+P8fAAMCAf/1e6XXAAAAAElFTkSuQmCC";

    /// <summary>Prepares culture handling without starting Swarm or a generation backend.</summary>
    [OneTimeSetUp]
    public static void PreInit()
    {
        Setup();
    }

    /// <summary>Builds a small nonuniform RGBA fixture with exact eight-bit channel values.</summary>
    private static ImageFile CreateSource(uint width = 64, uint height = 48, bool transparent = false)
    {
        using MagickImage image = new(MagickColors.CornflowerBlue, width, height);
        image.ColorType = ColorType.TrueColorAlpha;
        image.Depth = 8;
        using (IPixelCollection<ushort> pixels = image.GetPixels())
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte alpha = transparent ? (byte)((x + y) % 3 == 0 ? 0 : (x + y) % 3 == 1 ? 128 : 255) : (byte)255;
                    pixels.SetPixel(x, y,
                    [
                        (ushort)(((x * 29 + y * 7) % 256) * 257),
                        (ushort)(((x * 11 + y * 31 + 64) % 256) * 257),
                        (ushort)(((x * 17 + y * 13 + 128) % 256) * 257),
                        (ushort)(alpha * 257)
                    ]);
                }
            }
        }
        return new SwarmImage(image.ToByteArray(MagickFormat.Png), MediaType.ImagePng);
    }

    /// <summary>Exports a decoded image in a fixed channel order independently of its internal color type.</summary>
    private static byte[] Rgba(IMagickImage<ushort> image)
    {
        using IPixelCollection<ushort> pixels = image.GetPixels();
        return pixels.ToByteArray(PixelMapping.RGBA);
    }

    /// <summary>Reads the documented stealth payload from serialized pixels, without using Swarm's encoder helpers.</summary>
    private static string ReadStealthMetadata(byte[] encoded, bool alpha)
    {
        using MagickImage image = new(encoded);
        byte[] rgba = Rgba(image);
        List<byte> payload = [];
        int partialByte = 0;
        int bitCount = 0;
        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                int pixel = (y * (int)image.Width + x) * 4;
                for (int channel = alpha ? 3 : 0; channel < (alpha ? 4 : 3); channel++)
                {
                    partialByte = (partialByte << 1) | (rgba[pixel + channel] & 1);
                    if (++bitCount == 8)
                    {
                        payload.Add((byte)partialByte);
                        partialByte = 0;
                        bitCount = 0;
                    }
                }
            }
        }
        byte[] data = payload.ToArray();
        Assert.That(Encoding.ASCII.GetString(data, 0, 15), Is.EqualTo(alpha ? "stealth_pngcomp" : "stealth_rgbcomp"));
        uint payloadBits = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(15, 4));
        Assert.That(payloadBits % 8, Is.Zero, "Stealth payload length is measured in bits.");
        Assert.That(payloadBits / 8, Is.LessThanOrEqualTo(data.Length - 19), "The complete payload must fit in the image.");
        using MemoryStream compressed = new(data, 19, (int)(payloadBits / 8));
        using GZipStream gzip = new(compressed, CompressionMode.Decompress);
        using StreamReader reader = new(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Saved PNG, JPEG and WebP files retain exact Unicode generation metadata.</summary>
    [TestCase("PNG")]
    [TestCase("JPG")]
    [TestCase("WEBP_LOSSLESS")]
    public static void UnicodeMetadataSurvivesEncodingAndReload(string format)
    {
        ImageFile encoded = CreateSource().ConvertTo(format, Metadata);
        ImageFile reloaded = ImageFile.FromDataString(encoded.AsDataString());
        Assert.That(reloaded.GetMetadata(), Is.EqualTo(Metadata));
        Assert.That(reloaded.GetSUIMetadata()["prompt"].ToString(), Is.EqualTo("café — 雪 😺"));
    }

    /// <summary>A raster MIME label must not make a harmless vector document eligible for image decoding.</summary>
    [Test]
    public static void RasterImageRejectsDisguisedSvg()
    {
        byte[] svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"><rect width=\"1\" height=\"1\" fill=\"red\"/></svg>");
        ImageFile disguised = new SwarmImage(svg, MediaType.ImagePng);
        Assert.That(() => disguised.GetResolution(), Throws.Exception);
    }

    /// <summary>APNG input reports the unsupported animation format instead of returning only its first frame.</summary>
    [Test]
    public static void ApngIsExplicitlyRejectedInsteadOfFlattened()
    {
        ImageFile apng = new SwarmImage(Convert.FromBase64String(TwoFrameApng), MediaType.ImagePng);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => apng.GetResolution());
        Assert.That(error.Message, Does.Contain("APNG").IgnoreCase);
        Assert.That(error.Message, Does.Match("(?i)(unsupported|not supported)"));
    }

    /// <summary>Allowed raster bytes remain readable when a caller supplies the other common raster MIME type.</summary>
    [TestCase(MagickFormat.Png, "jpg")]
    [TestCase(MagickFormat.Jpeg, "png")]
    public static void RasterDecodingUsesActualFormatWhenMimeTypeDiffers(MagickFormat format, string declaredExtension)
    {
        using MagickImage image = new(MagickColors.CornflowerBlue, 32, 24);
        ImageFile mislabeled = new SwarmImage(image.ToByteArray(format), MediaType.GetByExtension(declaredExtension));
        Assert.That(mislabeled.GetResolution(), Is.EqualTo((32, 24)));
        Assert.That(mislabeled.ToMagick.Format, Is.EqualTo(format));
    }

    /// <summary>EXIF comments written by other encoders honor an explicit Unicode byte order mark.</summary>
    [TestCase(MagickFormat.Jpeg, false)]
    [TestCase(MagickFormat.Jpeg, true)]
    [TestCase(MagickFormat.WebP, false)]
    [TestCase(MagickFormat.WebP, true)]
    public static void ExternalExifUnicodeCommentsHonorByteOrderMark(MagickFormat format, bool bigEndian)
    {
        using MagickImage image = new(MagickColors.CornflowerBlue, 32, 24);
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        ExifProfile profile = new();
        profile.SetValue(ExifTag.UserComment, [.. "UNICODE\0"u8, .. encoding.GetPreamble(), .. encoding.GetBytes(Metadata)]);
        image.SetProfile(profile);
        ImageFile encoded = new SwarmImage(image.ToByteArray(format), format == MagickFormat.Jpeg ? MediaType.ImageJpg : MediaType.ImageWebp);
        Assert.That(encoded.GetMetadata(), Is.EqualTo(Metadata));
    }

    /// <summary>Metadata thumbnails retain Unicode text and scale their shortest side to 256 pixels.</summary>
    [TestCase("PNG")]
    [TestCase("JPG")]
    [TestCase("WEBP_LOSSLESS")]
    public static void MetadataThumbnailPreservesTextAndAspectRatio(string format)
    {
        ImageFile source = CreateSource(96, 48).ConvertTo(format, Metadata);
        ImageFile thumbnail = source.ToMetadataJpg();
        Assert.That(thumbnail.Type, Is.SameAs(MediaType.ImageJpg));
        Assert.That(thumbnail.GetResolution(), Is.EqualTo((512, 256)));
        Assert.That(thumbnail.GetMetadata(), Is.EqualTo(Metadata));
        Assert.That(source.GetResolution(), Is.EqualTo((96, 48)));
    }

    /// <summary>The serialized codec, MIME type and data URL agree after a conversion.</summary>
    [TestCase("PNG", MagickFormat.Png, "image/png")]
    [TestCase("JPG", MagickFormat.Jpeg, "image/jpeg")]
    [TestCase("WEBP_LOSSLESS", MagickFormat.WebP, "image/webp")]
    public static void ConvertedFormatMatchesDataUrlAndDecodedImage(string format, MagickFormat expectedFormat, string mimeType)
    {
        ImageFile source = CreateSource();
        byte[] originalPixels = Rgba(source.ToMagick);
        ImageFile encoded = source.ConvertTo(format, Metadata);
        string dataUrl = encoded.AsDataString();
        using MagickImage decoded = new(encoded.RawData);
        Assert.That(decoded.Format, Is.EqualTo(expectedFormat));
        Assert.That(encoded.Type.MimeType, Is.EqualTo(mimeType));
        Assert.That(dataUrl, Does.StartWith($"data:{mimeType};base64,"));
        Assert.That(ImageFile.FromDataString(dataUrl).RawData, Is.EqualTo(encoded.RawData));
        Assert.That((decoded.Width, decoded.Height), Is.EqualTo((64u, 48u)));
        Assert.That(Rgba(source.ToMagick), Is.EqualTo(originalPixels), "Conversion must leave the source pixels intact.");
        Assert.That(source.GetMetadata(), Is.Null, "Conversion must leave the source metadata intact.");
    }

    /// <summary>Replacing generation metadata retains the valid ICC profile needed to display image colors correctly.</summary>
    [TestCase("PNG")]
    [TestCase("JPG")]
    [TestCase("WEBP_LOSSLESS")]
    public static void ConversionPreservesIccProfile(string format)
    {
        using MagickImage image = new(CreateSource().RawData);
        image.SetProfile(ColorProfiles.AdobeRGB1998);
        ImageFile source = new SwarmImage(image.ToByteArray(MagickFormat.Png), MediaType.ImagePng);
        IColorProfile sourceProfile = source.ToMagick.GetColorProfile();
        Assert.That(sourceProfile, Is.Not.Null, "The source fixture must contain an actual ICC profile.");
        byte[] originalProfile = sourceProfile.ToByteArray();
        ImageFile converted = source.ConvertTo(format, Metadata);
        using MagickImage decoded = new(converted.RawData);
        IColorProfile actualProfile = decoded.GetColorProfile();
        Assert.That(actualProfile, Is.Not.Null, "Conversion must retain the image's color profile.");
        Assert.That(actualProfile.ToByteArray(), Is.EqualTo(originalProfile));
        Assert.That(source.ToMagick.GetColorProfile().ToByteArray(), Is.EqualTo(originalProfile), "Conversion must leave the source profile intact.");
        Assert.That(converted.GetMetadata(), Is.EqualTo(Metadata));
    }

    /// <summary>Resizing uses the requested dimensions even when the requested aspect ratio changes.</summary>
    [TestCase(17, 31)]
    [TestCase(96, 12)]
    public static void ResizeUsesBothRequestedDimensions(int width, int height)
    {
        ImageFile source = CreateSource(37, 19, true);
        byte[] originalPixels = Rgba(source.ToMagick);
        ImageFile resized = source.Resize(width, height);
        using MagickImage decoded = new(resized.RawData);
        Assert.That(resized.Type, Is.SameAs(MediaType.ImagePng));
        Assert.That((decoded.Width, decoded.Height), Is.EqualTo(((uint)width, (uint)height)));
        Assert.That(decoded.HasAlpha, Is.True);
        Assert.That(source.GetResolution(), Is.EqualTo((37, 19)));
        Assert.That(Rgba(source.ToMagick), Is.EqualTo(originalPixels));
    }

    /// <summary>Lossless conversion keeps every RGBA value, including color hidden under transparent pixels.</summary>
    [TestCase("PNG")]
    [TestCase("WEBP_LOSSLESS")]
    public static void LosslessConversionPreservesTransparentPixels(string format)
    {
        ImageFile source = CreateSource(37, 19, true);
        ImageFile encoded = source.ConvertTo(format);
        using MagickImage decoded = new(encoded.RawData);
        Assert.That(decoded.HasAlpha, Is.True);
        Assert.That(Rgba(decoded), Is.EqualTo(Rgba(source.ToMagick)));
    }

    /// <summary>A forced PNG conversion also preserves transparency.</summary>
    [Test]
    public static void ForceToPngPreservesTransparency()
    {
        ImageFile source = CreateSource(37, 19, true);
        ImageFile converted = source.ForceToPng();
        using MagickImage decoded = new(converted.RawData);
        Assert.That(decoded.Format, Is.EqualTo(MagickFormat.Png));
        Assert.That(decoded.HasAlpha, Is.True);
        Assert.That(Rgba(decoded), Is.EqualTo(Rgba(source.ToMagick)));
    }

    /// <summary>Real encoded files remain readable by a stealth decoder using the existing wire format.</summary>
    [TestCase("PNG", "rgb")]
    [TestCase("PNG", "alpha")]
    [TestCase("WEBP_LOSSLESS", "rgb")]
    [TestCase("WEBP_LOSSLESS", "alpha")]
    [TestCase("WEBP", "alpha")]
    public static void StealthMetadataSurvivesSupportedCodecs(string format, string mode)
    {
        ImageFile source = CreateSource();
        ImageFile encoded = source.ConvertTo(format, Metadata, quality: 75, stealthMetadata: mode);
        Assert.That(ReadStealthMetadata(encoded.RawData, mode == "alpha"), Is.EqualTo(Metadata));
    }

    /// <summary>Stealth embedding changes only the low bit in its selected channels for lossless formats.</summary>
    [TestCase("PNG", "rgb")]
    [TestCase("PNG", "alpha")]
    [TestCase("WEBP_LOSSLESS", "rgb")]
    [TestCase("WEBP_LOSSLESS", "alpha")]
    public static void LosslessStealthEncodingPreservesOtherChannels(string format, string mode)
    {
        ImageFile source = CreateSource();
        byte[] original = Rgba(source.ToMagick);
        ImageFile encoded = source.ConvertTo(format, Metadata, stealthMetadata: mode);
        using MagickImage decoded = new(encoded.RawData);
        byte[] actual = Rgba(decoded);
        for (int i = 0; i < actual.Length; i++)
        {
            bool embeddedChannel = mode == "alpha" ? i % 4 == 3 : i % 4 != 3;
            Assert.That(Math.Abs(actual[i] - original[i]), Is.LessThanOrEqualTo(embeddedChannel ? 1 : 0), $"Unexpected change at RGBA byte {i}.");
        }
    }

    /// <summary>Creates distinct full-canvas frames so the animation encoder cannot combine identical frames.</summary>
    private static MagickImageCollection CreateAnimation(bool milliseconds)
    {
        MagickImageCollection frames = [];
        IMagickColor<ushort>[] colors = [MagickColors.Red, MagickColors.Lime, MagickColors.Blue];
        uint[] delays = milliseconds ? [17u, 83u, 125u] : [2u, 7u, 13u];
        for (int i = 0; i < colors.Length; i++)
        {
            MagickImage frame = new(colors[i], 48, 32)
            {
                AnimationTicksPerSecond = milliseconds ? 1000 : 100,
                AnimationDelay = delays[i],
                AnimationIterations = 0
            };
            frames.Add(frame);
        }
        return frames;
    }

    /// <summary>Animation helper output preserves frame order, dimensions, pixels and each frame's duration.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public static void AnimationRoundTripPreservesFramesAndDelays(bool milliseconds)
    {
        using MagickImageCollection frames = CreateAnimation(milliseconds);
        long[] expectedMilliseconds = milliseconds ? [17L, 83L, 125L] : [20L, 70L, 130L];
        byte[] encoded = ImageFile.MagickFramesToImageBytes(frames);
        ImageFile reloaded = new SwarmImage(encoded, MediaType.ImageWebp);
        MagickImageCollection decoded = reloaded.ToMagickFrames;
        Assert.That(decoded.Count, Is.EqualTo(frames.Count));
        for (int i = 0; i < frames.Count; i++)
        {
            Assert.That((decoded[i].Width, decoded[i].Height), Is.EqualTo((48u, 32u)));
            Assert.That(Rgba(decoded[i]), Is.EqualTo(Rgba(frames[i])), $"Frame {i} pixels changed.");
            Assert.That(decoded[i].AnimationDelay * 1000L / decoded[i].AnimationTicksPerSecond,
                Is.EqualTo(expectedMilliseconds[i]), $"Frame {i} duration changed.");
            Assert.That(frames[i].AnimationDelay * 1000L / frames[i].AnimationTicksPerSecond,
                Is.EqualTo(expectedMilliseconds[i]), $"Encoding changed source frame {i}'s duration.");
        }
        Assert.That(frames[0].AnimationTicksPerSecond, Is.EqualTo(milliseconds ? 1000 : 100), "Encoding must not mutate the source collection.");
    }

    /// <summary>Reading a GIF and writing a WebP preserves the original frame sequence and durations.</summary>
    [Test]
    public static void GifFramesSurviveWebpConversion()
    {
        using MagickImageCollection frames = CreateAnimation(false);
        ImageFile gif = new SwarmImage(frames.ToByteArray(MagickFormat.Gif), MediaType.ImageGif);
        ImageFile webp = new SwarmImage(ImageFile.MagickFramesToImageBytes(gif.ToMagickFrames), MediaType.ImageWebp);
        Assert.That(webp.ToMagickFrames.Count, Is.EqualTo(3));
        for (int i = 0; i < frames.Count; i++)
        {
            Assert.That(Rgba(webp.ToMagickFrames[i]), Is.EqualTo(Rgba(frames[i])));
            Assert.That(webp.ToMagickFrames[i].AnimationDelay * 1000L / webp.ToMagickFrames[i].AnimationTicksPerSecond,
                Is.EqualTo(frames[i].AnimationDelay * 1000L / frames[i].AnimationTicksPerSecond));
        }
    }
}
