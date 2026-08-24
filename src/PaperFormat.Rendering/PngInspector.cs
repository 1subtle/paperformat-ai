using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PaperFormat.Rendering;

public static class PngInspector
{
    private static readonly byte[] Signature =
        [137, 80, 78, 71, 13, 10, 26, 10];

    public static (int Width, int Height, string Sha256) Inspect(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(Signature)
            || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' is not a supported PNG.");
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(
            header.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(
            header.Slice(20, 4));
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' has invalid dimensions.");
        }

        stream.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        return (width, height, hash);
    }

    public static PngPageAnalysis Analyze(string path)
    {
        byte[] png = File.ReadAllBytes(path);
        if (png.Length < Signature.Length
            || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' is not a supported PNG.");
        }

        int offset = Signature.Length;
        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        int interlace = 0;
        using var compressed = new MemoryStream();
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(
                png.AsSpan(offset, 4));
            if (length < 0 || offset + 12 + length > png.Length)
            {
                throw new InvalidDataException("The PNG chunk is invalid.");
            }

            ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(
                    data.Slice(4, 4));
                bitDepth = data[8];
                colorType = data[9];
                interlace = data[12];
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset += 12 + length;
        }

        int bytesPerPixel = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException(
                $"PNG color type {colorType} is not supported for page analysis."),
        };
        if (width <= 0
            || height <= 0
            || bitDepth != 8
            || interlace != 0)
        {
            throw new InvalidDataException(
                "Rendered-page analysis requires a non-interlaced 8-bit PNG.");
        }

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        using (var zlib = new ZLibStream(
                   compressed,
                   CompressionMode.Decompress,
                   leaveOpen: true))
        {
            zlib.CopyTo(decompressed);
        }

        byte[] scanlines = decompressed.ToArray();
        int stride = checked(width * bytesPerPixel);
        int expected = checked((stride + 1) * height);
        if (scanlines.Length != expected)
        {
            throw new InvalidDataException(
                "The rendered PNG scanline length is unexpected.");
        }

        byte[] previous = new byte[stride];
        byte[] current = new byte[stride];
        bool[] rowHasInk = new bool[height];
        long inkPixels = 0;
        int position = 0;
        for (int row = 0; row < height; row++)
        {
            byte filter = scanlines[position++];
            scanlines.AsSpan(position, stride).CopyTo(current);
            position += stride;
            Unfilter(current, previous, bytesPerPixel, filter);
            int rowInk = 0;
            for (int column = 0; column < width; column++)
            {
                int pixel = column * bytesPerPixel;
                byte red;
                byte green;
                byte blue;
                byte alpha = 255;
                switch (colorType)
                {
                    case 0:
                        red = green = blue = current[pixel];
                        break;
                    case 2:
                        red = current[pixel];
                        green = current[pixel + 1];
                        blue = current[pixel + 2];
                        break;
                    case 4:
                        red = green = blue = current[pixel];
                        alpha = current[pixel + 1];
                        break;
                    default:
                        red = current[pixel];
                        green = current[pixel + 1];
                        blue = current[pixel + 2];
                        alpha = current[pixel + 3];
                        break;
                }

                if (alpha > 16
                    && (red < 245 || green < 245 || blue < 245))
                {
                    rowInk++;
                }
            }

            inkPixels += rowInk;
            rowHasInk[row] = rowInk >= Math.Max(2, width / 2_000);
            (previous, current) = (current, previous);
        }

        int start = height / 20;
        int end = height - start;
        int longest = 0;
        int run = 0;
        for (int row = start; row < end; row++)
        {
            if (!rowHasInk[row])
            {
                run++;
                longest = Math.Max(longest, run);
            }
            else
            {
                run = 0;
            }
        }

        return new PngPageAnalysis(
            width,
            height,
            inkPixels / (double)(width * (long)height),
            longest / (double)height);
    }

    private static void Unfilter(
        Span<byte> row,
        ReadOnlySpan<byte> previous,
        int bytesPerPixel,
        byte filter)
    {
        for (int index = 0; index < row.Length; index++)
        {
            int left = index >= bytesPerPixel
                ? row[index - bytesPerPixel]
                : 0;
            int up = previous[index];
            int upperLeft = index >= bytesPerPixel
                ? previous[index - bytesPerPixel]
                : 0;
            int predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upperLeft),
                _ => throw new InvalidDataException(
                    $"Unsupported PNG filter {filter}."),
            };
            row[index] = unchecked((byte)(row[index] + predictor));
        }
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        int prediction = left + up - upperLeft;
        int leftDistance = Math.Abs(prediction - left);
        int upDistance = Math.Abs(prediction - up);
        int upperLeftDistance = Math.Abs(prediction - upperLeft);
        return leftDistance <= upDistance
            && leftDistance <= upperLeftDistance
                ? left
                : upDistance <= upperLeftDistance
                    ? up
                    : upperLeft;
    }
}

public sealed record PngPageAnalysis(
    int Width,
    int Height,
    double InkRatio,
    double LargestInteriorBlankRatio);
