using System.IO.Compression;

namespace ZombieCafe.Assets;

/// <summary>
/// The game's ".cct" textures: "CCTX", u32 version, u32 width, u32 height, u32 0, u32 0, u32 zlib length,
/// then zlib-compressed pixels. Version 2 = RGBA4444 (u16 LE, red in the high nibble), version 1 = RGBA8888.
/// </summary>
public sealed class CctImage
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>RGBA8888, row-major, top row first.</summary>
    public byte[] Rgba { get; }

    CctImage(int w, int h, byte[] rgba)
    {
        Width = w;
        Height = h;
        Rgba = rgba;
    }

    public static bool IsCct(ReadOnlySpan<byte> file) =>
        file.Length >= 28 && file[0] == 'C' && file[1] == 'C' && file[2] == 'T' && file[3] == 'X';

    public static CctImage Decode(byte[] file)
    {
        if (!IsCct(file))
            throw new InvalidDataException("not a CCTX texture");
        var r = new BinaryReader(new MemoryStream(file));
        r.ReadUInt32();
        uint version = r.ReadUInt32();
        int w = r.ReadInt32(), h = r.ReadInt32();
        r.ReadUInt32();
        r.ReadUInt32();
        int zlen = r.ReadInt32();
        int bpp = version == 1 ? 4 : 2;
        var px = new byte[w * h * bpp];
        using (var z = new ZLibStream(new MemoryStream(file, 28, zlen), CompressionMode.Decompress))
            z.ReadExactly(px);
        if (bpp == 4)
            return new CctImage(w, h, px);

        var rgba = new byte[w * h * 4];
        for (int i = 0, o = 0; i < px.Length; i += 2, o += 4)
        {
            int v = px[i] | px[i + 1] << 8;
            rgba[o] = (byte)((v >> 12 & 15) * 17);
            rgba[o + 1] = (byte)((v >> 8 & 15) * 17);
            rgba[o + 2] = (byte)((v >> 4 & 15) * 17);
            rgba[o + 3] = (byte)((v & 15) * 17);
        }
        return new CctImage(w, h, rgba);
    }
}
