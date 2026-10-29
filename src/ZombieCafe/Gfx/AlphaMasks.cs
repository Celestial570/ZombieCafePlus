namespace ZombieCafe.Gfx;

/// <summary>
/// Which pixels of each loaded texture are solid, kept as one bit a pixel when the texture is made, so things in the
/// cafe can be picked with the mouse by their visible shape rather than the box around their sprite.
/// </summary>
public static class AlphaMasks
{
    sealed record Mask(int Width, int Height, ulong[] Bits);

    // alpha at or above this counts as part of the picture (soft edges and shadows below it do not)
    const byte Solid = 64;

    static readonly Dictionary<uint, Mask> masks = new();

    /// <summary>Records the solid pixels of RGBA8888 <paramref name="rgba"/> for texture <paramref name="textureId"/>.</summary>
    public static void Record(uint textureId, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (textureId == 0 || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            return;
        var bits = new ulong[(width * height + 63) / 64];
        for (int i = 0, n = width * height; i < n; i++)
            if (rgba[i * 4 + 3] >= Solid)
                bits[i >> 6] |= 1UL << (i & 63);
        masks[textureId] = new Mask(width, height, bits);
    }

    /// <summary>Whether texel (x, y) of the texture is solid; null when the texture has no mask.</summary>
    public static bool? IsSolid(uint textureId, int x, int y)
    {
        if (!masks.TryGetValue(textureId, out var m))
            return null;
        if (x < 0 || y < 0 || x >= m.Width || y >= m.Height)
            return false;
        int i = y * m.Width + x;
        return (m.Bits[i >> 6] >> (i & 63) & 1) != 0;
    }
}
