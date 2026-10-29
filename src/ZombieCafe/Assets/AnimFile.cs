using System.Buffers.Binary;

namespace ZombieCafe.Assets;

/// <summary>
/// Flash-exported cut-out animation (.anim), read in place by the original FlashAnimation (little-endian):
/// u16 version, u16 frame time (ms), u16 frame count, then per frame a u8 part count followed by
/// records of {f32 a, b, c, d, tx, ty (Flash matrix), u16 part index[, u32 colour when version != 1]}.
/// A part's image is centred on its untrimmed size before the matrix is applied.
/// </summary>
public sealed class AnimFile
{
    public readonly record struct Part(float A, float B, float C, float D, float Tx, float Ty, int Index, uint Color);

    public int Version { get; }
    public int FrameTimeMs { get; }
    public IReadOnlyList<Part[]> Frames { get; }

    AnimFile(int version, int frameTimeMs, List<Part[]> frames)
    {
        Version = version;
        FrameTimeMs = frameTimeMs;
        Frames = frames;
    }

    public static AnimFile Parse(byte[] d)
    {
        var s = d.AsSpan();
        int version = BinaryPrimitives.ReadUInt16LittleEndian(s);
        int frameMs = BinaryPrimitives.ReadUInt16LittleEndian(s[2..]);
        int frameCount = BinaryPrimitives.ReadUInt16LittleEndian(s[4..]);
        int recSize = version == 1 ? 26 : 30;
        int p = 6;
        var frames = new List<Part[]>(frameCount);
        for (int f = 0; f < frameCount && p < d.Length; f++)
        {
            int n = d[p++];
            var parts = new Part[n];
            for (int i = 0; i < n && p + recSize <= d.Length; i++, p += recSize)
            {
                var r = s.Slice(p, recSize);
                parts[i] = new Part(
                    BinaryPrimitives.ReadSingleLittleEndian(r),
                    BinaryPrimitives.ReadSingleLittleEndian(r[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(r[8..]),
                    BinaryPrimitives.ReadSingleLittleEndian(r[12..]),
                    BinaryPrimitives.ReadSingleLittleEndian(r[16..]),
                    BinaryPrimitives.ReadSingleLittleEndian(r[20..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(r[24..]),
                    version == 1 ? 0xFFFFFFFFu : BinaryPrimitives.ReadUInt32LittleEndian(r[26..]));
            }
            frames.Add(parts);
        }
        return new AnimFile(version, frameMs, frames);
    }
}
