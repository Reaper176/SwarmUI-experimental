using System.Buffers.Binary;
using System.Text;
using Hartsy.Extensions.MagicPromptExtension;
using ImageMagick;
using WhatTheDuck;

/// <summary>Optional CPU-only checks against installed extension sources and the current SwarmUI core.</summary>
internal static class Program
{
    private const string OldComment = "original prompt";
    private const string NewComment = "Edited prompt: café, 日本語, العربية, 🦆\nsecond line";
    private const string Artist = "extension smoke fixture";

    private static int Main()
    {
        OpenCL.IsEnabled = false;
        int passed = 0;
        int failed = 0;
        (uint Width, uint Height, uint ExpectedWidth, uint ExpectedHeight)[] sizes =
        [
            (1024, 512, 256, 128),
            (64, 48, 64, 48),
            (4096, 1, 256, 1),
            (1, 4096, 1, 256)
        ];
        (string Name, MagickFormat Expected)[] formats =
        [
            ("PNG", MagickFormat.Png),
            ("WEBP", MagickFormat.WebP),
            ("JPG", MagickFormat.Jpeg)
        ];
        foreach ((uint width, uint height, uint expectedWidth, uint expectedHeight) in sizes)
        {
            using MagickImage source = new(MagickColors.CornflowerBlue, width, height);
            byte[] sourcePng = source.ToByteArray(MagickFormat.Png);
            foreach ((string format, MagickFormat expectedFormat) in formats)
            {
                Check($"MagicPrompt {width}x{height} -> {format} {expectedWidth}x{expectedHeight}", () =>
                    Thumbnail(sourcePng, format, expectedFormat, expectedWidth, expectedHeight), ref passed, ref failed);
            }
        }
        foreach (bool lossless in new[] { false, true })
        {
            foreach ((bool alpha, bool animated) in new[] { (false, false), (true, false), (true, true) })
            {
                foreach (bool exifHeader in new[] { false, true })
                {
                    string name = $"WhatTheDuck {(lossless ? "lossless" : "lossy")} alpha={alpha} animation={animated} ExifHeader={exifHeader}";
                    Check(name, () => RewriteWebp(lossless, alpha, animated, exifHeader), ref passed, ref failed);
                }
            }
        }
        Check("WhatTheDuck rejects missing EXIF and invalid input", RejectMissingExif, ref passed, ref failed);
        Console.WriteLine($"Extension checks: {passed} passed, {failed} failed. CPU image I/O only; no app, LLM, or GPU started.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action action, ref int passed, ref int failed)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            failed++;
            Console.Error.WriteLine($"FAIL {name}: {exception}");
        }
    }

    private static void Thumbnail(byte[] sourcePng, string target, MagickFormat expectedFormat, uint width, uint height)
    {
        BackendSchema.MediaContent media = new()
        {
            Type = "base64",
            Data = Convert.ToBase64String(sourcePng),
            MediaType = "image/png"
        };
        byte[] result = Convert.FromBase64String(BackendSchema.CompressImageForVision(media, target));
        using MagickImage decoded = new(result);
        Require(decoded.Format == expectedFormat, $"Expected {expectedFormat}, got {decoded.Format}.");
        Require(decoded.Width == width && decoded.Height == height,
            $"Expected {width}x{height}, got {decoded.Width}x{decoded.Height}.");
        Require(decoded.Width <= 256 && decoded.Height <= 256, "Thumbnail exceeds the 256px bound.");
    }

    private static void RewriteWebp(bool lossless, bool alpha, bool animated, bool exifHeader)
    {
        byte[] input = SetExifFraming(CreateWebp(lossless, alpha, animated), exifHeader);
        List<Chunk> before = ReadChunks(input);
        Require(before.Count(chunk => chunk.Id == "EXIF") == 1, "Fixture needs exactly one EXIF chunk.");
        string bitstream = lossless ? "VP8L" : "VP8 ";
        Require(animated
            ? before.Where(chunk => chunk.Id == "ANMF").All(chunk => ReadFrameChunks(chunk.Payload).Any(part => part.Id == bitstream))
            : before.Any(chunk => chunk.Id == bitstream), $"Fixture does not contain {bitstream} image data.");
        Require(!animated || before.Count(chunk => chunk.Id == "ANMF") == 2, "Fixture animation must have two frames.");
        ExifProfile originalProfile = ReadExifProfile(before.Single(chunk => chunk.Id == "EXIF").Payload);
        Require(ReadComment(originalProfile) == OldComment, "Fixture comment was not encoded correctly.");
        Require(originalProfile.GetValue(ExifTag.Artist)?.Value == Artist, "Fixture Artist was not encoded correctly.");

        byte[] result = WebpExifRewriter.RewriteUserComment(input, NewComment);
        Require(result is not null, "Rewriter rejected a valid WebP with EXIF.");
        List<Chunk> after = ReadChunks(result);
        Require(BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4, 4)) == result.Length - 8, "RIFF size is incorrect.");
        Require(before.Select(chunk => chunk.Id).SequenceEqual(after.Select(chunk => chunk.Id)), "Chunk order/count changed.");
        for (int i = 0; i < before.Count; i++)
        {
            if (before[i].Id != "EXIF")
            {
                Require(before[i].Raw.AsSpan().SequenceEqual(after[i].Raw), $"Non-EXIF chunk {i} ({before[i].Id}) changed.");
            }
        }
        byte[] newExif = after.Single(chunk => chunk.Id == "EXIF").Payload;
        Require(newExif.AsSpan().StartsWith("Exif\0\0"u8) == exifHeader, "EXIF framing changed.");
        using MagickImageCollection originalFrames = new(input);
        using MagickImageCollection editedFrames = new(result);
        Require(originalFrames.Count == (animated ? 2 : 1), "Unexpected source frame count.");
        Require(editedFrames.Count == originalFrames.Count, "Decoded frame count changed.");
        ExifProfile profile = ReadExifProfile(newExif);
        Require(ReadComment(profile) == NewComment, "Unicode UserComment did not round-trip.");
        Require(profile.GetValue(ExifTag.Artist)?.Value == Artist, "Unedited EXIF Artist changed.");
        byte[] expectedComment = [.. "UNICODE\0"u8, .. Encoding.Unicode.GetBytes(NewComment)];
        Require(profile.GetValue(ExifTag.UserComment).Value.AsSpan().SequenceEqual(expectedComment), "UserComment byte encoding is incorrect.");
        if (!animated)
        {
            Require(ReadComment(editedFrames[0].GetExifProfile()) == NewComment, "Still-image decoder did not read the edited comment.");
        }
        for (int i = 0; i < originalFrames.Count; i++)
        {
            IMagickImage<ushort> original = originalFrames[i];
            IMagickImage<ushort> edited = editedFrames[i];
            Require(original.Width == edited.Width && original.Height == edited.Height, $"Frame {i} dimensions changed.");
            Require(original.HasAlpha == edited.HasAlpha, $"Frame {i} alpha changed.");
            Require(!alpha || original.HasAlpha, $"Frame {i} fixture lost its alpha channel.");
            Require(original.AnimationDelay == edited.AnimationDelay && original.AnimationTicksPerSecond == edited.AnimationTicksPerSecond,
                $"Frame {i} animation timing changed.");
            using IPixelCollection<ushort> originalPixels = original.GetPixels();
            using IPixelCollection<ushort> editedPixels = edited.GetPixels();
            Require(originalPixels.ToByteArray(PixelMapping.RGBA).AsSpan().SequenceEqual(editedPixels.ToByteArray(PixelMapping.RGBA)),
                $"Frame {i} decoded pixels changed.");
        }
    }

    private static string ReadComment(IExifProfile profile)
    {
        byte[] comment = profile?.GetValue(ExifTag.UserComment)?.Value;
        Require(comment is not null && comment.AsSpan().StartsWith("UNICODE\0"u8), "Missing Unicode UserComment.");
        return Encoding.Unicode.GetString(comment, 8, comment.Length - 8).TrimEnd('\0');
    }

    private static ExifProfile ReadExifProfile(byte[] payload)
    {
        // WebP permits raw TIFF EXIF; the managed ExifProfile parser expects the JPEG-style prefix.
        return new ExifProfile(payload.AsSpan().StartsWith("Exif\0\0"u8) ? payload : [.. "Exif\0\0"u8, .. payload]);
    }

    private static byte[] CreateWebp(bool lossless, bool alpha, bool animated)
    {
        using MagickImageCollection frames = new();
        frames.Add(CreateFrame(lossless, alpha, 0));
        ExifProfile exif = new();
        exif.SetValue(ExifTag.UserComment, [.. "UNICODE\0"u8, .. Encoding.Unicode.GetBytes(OldComment)]);
        exif.SetValue(ExifTag.Artist, Artist);
        frames[0].SetProfile(exif);
        if (animated)
        {
            frames.Add(CreateFrame(lossless, alpha, 1));
        }
        return frames.ToByteArray(MagickFormat.WebP);
    }

    private static MagickImage CreateFrame(bool lossless, bool alpha, int frame)
    {
        MagickImage image = new(MagickColors.Transparent, 33, 19)
        {
            ColorType = ColorType.TrueColorAlpha,
            Quality = 72,
            AnimationDelay = (uint)(frame == 0 ? 7 : 13),
            AnimationTicksPerSecond = 100,
            AnimationIterations = 3
        };
        image.Settings.SetDefine(MagickFormat.WebP, "lossless", lossless);
        using IPixelCollection<ushort> pixels = image.GetPixels();
        for (int y = 0; y < 19; y++)
        {
            for (int x = 0; x < 33; x++)
            {
                IPixel<ushort> pixel = pixels.GetPixel(x, y);
                pixel.SetChannel(0, (ushort)(((x * 29 + frame * 71) % 256) * 257));
                pixel.SetChannel(1, (ushort)(((y * 47 + frame * 31) % 256) * 257));
                pixel.SetChannel(2, (ushort)(((x * 13 + y * 7) % 256) * 257));
                pixel.SetChannel(3, alpha ? (ushort)(x % 3 == 0 ? 0 : x % 3 == 1 ? 32768 : 65535) : ushort.MaxValue);
            }
        }
        return image;
    }

    private static void RejectMissingExif()
    {
        Require(WebpExifRewriter.RewriteUserComment(null, NewComment) is null, "Null input was accepted.");
        Require(WebpExifRewriter.RewriteUserComment("not a WebP"u8.ToArray(), NewComment) is null, "Invalid input was accepted.");
        using MagickImage image = new(MagickColors.Red, 3, 2);
        Require(WebpExifRewriter.RewriteUserComment(image.ToByteArray(MagickFormat.WebP), NewComment) is null, "WebP without EXIF was accepted.");
    }

    private static byte[] SetExifFraming(byte[] webp, bool header)
    {
        List<Chunk> chunks = ReadChunks(webp);
        using MemoryStream result = new();
        result.Write(webp, 0, 12);
        foreach (Chunk chunk in chunks)
        {
            if (chunk.Id != "EXIF")
            {
                result.Write(chunk.Raw);
                continue;
            }
            byte[] payload = chunk.Payload;
            if (payload.AsSpan().StartsWith("Exif\0\0"u8))
            {
                payload = payload[6..];
            }
            if (header)
            {
                payload = [.. "Exif\0\0"u8, .. payload];
            }
            WriteChunk(result, "EXIF", payload);
        }
        // Unknown odd-length metadata exercises preservation of arbitrary payload and padding.
        WriteChunk(result, "TEST", [1, 2, 3]);
        byte[] bytes = result.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8);
        return bytes;
    }

    private static void WriteChunk(Stream destination, string id, byte[] payload)
    {
        destination.Write(Encoding.ASCII.GetBytes(id));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, payload.Length);
        destination.Write(size);
        destination.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            destination.WriteByte(0);
        }
    }

    private static List<Chunk> ReadChunks(byte[] webp)
    {
        Require(webp.Length >= 12 && webp.AsSpan(0, 4).SequenceEqual("RIFF"u8) && webp.AsSpan(8, 4).SequenceEqual("WEBP"u8), "Invalid RIFF/WebP fixture.");
        return ParseChunks(webp.AsSpan(12));
    }

    private static List<Chunk> ReadFrameChunks(byte[] frame)
    {
        Require(frame.Length >= 16, "Invalid ANMF frame header.");
        return ParseChunks(frame.AsSpan(16));
    }

    private static List<Chunk> ParseChunks(ReadOnlySpan<byte> data)
    {
        List<Chunk> chunks = [];
        int offset = 0;
        while (offset < data.Length)
        {
            Require(data.Length - offset >= 8, "Truncated chunk header.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            Require(length <= int.MaxValue - 9, "Chunk is too large.");
            int fullLength = 8 + (int)length + ((int)length & 1);
            Require(fullLength <= data.Length - offset, "Truncated chunk payload.");
            string id = Encoding.ASCII.GetString(data.Slice(offset, 4));
            chunks.Add(new Chunk(id, data.Slice(offset + 8, (int)length).ToArray(), data.Slice(offset, fullLength).ToArray()));
            offset += fullLength;
        }
        return chunks;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Chunk(string Id, byte[] Payload, byte[] Raw);
}
