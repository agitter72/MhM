using System.Buffers.Binary;

namespace MhM.UI.Services;

public static class ProfileImageSecurity
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 4096;
    public const long MaxPixels = 16_000_000;

    public static bool TryValidate(ReadOnlySpan<byte> data, out string contentType, out string error)
    {
        contentType = string.Empty;
        error = "Das Profilbild ist ungültig.";

        if (data.Length == 0 || data.Length > MaxFileSizeBytes)
        {
            error = "Das Profilbild darf höchstens 5 MB groß sein.";
            return false;
        }

        if (TryReadPng(data, out var width, out var height))
        {
            contentType = "image/png";
        }
        else if (TryReadJpeg(data, out width, out height))
        {
            contentType = "image/jpeg";
        }
        else
        {
            error = "Erlaubt sind ausschließlich echte JPEG- oder PNG-Dateien.";
            return false;
        }

        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
        {
            error = "Das Profilbild darf maximal 4096 × 4096 Pixel beziehungsweise 16 Megapixel haben.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryRemoveMetadata(byte[] data, string contentType, out byte[] sanitized)
    {
        sanitized = contentType switch
        {
            "image/jpeg" => RemoveJpegMetadata(data),
            "image/png" => RemovePngMetadata(data),
            _ => []
        };
        return sanitized.Length > 0;
    }

    private static byte[] RemoveJpegMetadata(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2);
        var offset = 2;

        while (offset + 4 <= data.Length && data[offset] == 0xff)
        {
            var marker = data[offset + 1];
            if (marker is 0xda or 0xd9)
            {
                output.Write(data, offset, data.Length - offset);
                return output.ToArray();
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2, 2));
            var totalLength = segmentLength + 2;
            if (segmentLength < 2 || offset + totalLength > data.Length)
                return [];

            // EXIF/XMP (APP1), IPTC/Photoshop (APP13) and comments may contain
            // location, device or author data. Color profiles remain intact.
            if (marker is not 0xe1 and not 0xed and not 0xfe)
                output.Write(data, offset, totalLength);

            offset += totalLength;
        }

        return [];
    }

    private static byte[] RemovePngMetadata(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8);
        var offset = 8;

        while (offset + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
            if (length < 0 || offset + 12L + length > data.Length)
                return [];

            var type = data.AsSpan(offset + 4, 4);
            var remove = type.SequenceEqual("eXIf"u8)
                || type.SequenceEqual("tEXt"u8)
                || type.SequenceEqual("zTXt"u8)
                || type.SequenceEqual("iTXt"u8);
            var chunkLength = length + 12;
            if (!remove)
                output.Write(data, offset, chunkLength);
            offset += chunkLength;

            if (type.SequenceEqual("IEND"u8))
                break;
        }

        return offset == data.Length ? output.ToArray() : [];
    }

    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 24 || !data[..8].SequenceEqual(signature) || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            return false;

        width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8)
            return false;

        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xff)
            {
                offset++;
                continue;
            }

            var marker = data[offset + 1];
            offset += 2;
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
                continue;
            if (offset + 2 > data.Length)
                return false;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > data.Length)
                return false;

            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                if (segmentLength < 7)
                    return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                return true;
            }

            offset += segmentLength;
        }

        return false;
    }
}
