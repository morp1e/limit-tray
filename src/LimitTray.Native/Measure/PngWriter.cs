using System.Buffers.Binary;
using System.IO.Compression;
using LimitTray.Native.Graphics;

namespace LimitTray.Native.Measure;

/// <summary>
/// Minimal PNG encoder for the render command: 8-bit RGBA, filter 0, zlib via the BCL.
/// Screen capture is avoided on purpose; the capture tool, not the app, was the source
/// of a long false hunt in v0.2 (a non-DPI-aware capture cropped the window).
/// </summary>
internal static unsafe class PngWriter
{
    /// <summary>Writes the surface composited over <paramref name="background"/> (opaque).</summary>
    public static void Write(string path, Surface surface, Colour background)
    {
        var width = surface.Width;
        var height = surface.Height;
        var raw = new byte[height * (1 + width * 4)];
        var o = 0;
        for (var y = 0; y < height; y++)
        {
            raw[o++] = 0; // filter: none
            var row = surface.Bits + y * width;
            for (var x = 0; x < width; x++)
            {
                var p = row[x];
                var a = p >> 24;
                var inv = 255 - a;
                raw[o++] = (byte)(((p >> 16) & 0xFF) + (background.R * inv + 127) / 255);
                raw[o++] = (byte)(((p >> 8) & 0xFF) + (background.G * inv + 127) / 255);
                raw[o++] = (byte)((p & 0xFF) + (background.B * inv + 127) / 255);
                raw[o++] = 255;
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // RGBA
        Chunk(file, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            Chunk(file, "IDAT", compressed.ToArray());
        }
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint[]? _table;

    private static uint Crc(byte[] type, byte[] data)
    {
        var table = _table ??= BuildTable();
        var crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
