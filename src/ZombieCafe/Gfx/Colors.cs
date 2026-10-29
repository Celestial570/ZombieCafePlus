using Raylib_cs;

namespace ZombieCafe.Gfx;

/// <summary>The original stores colours as ARGB ints.</summary>
public static class Colors
{
    public static Color FromArgb(int argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)((uint)argb >> 24));

    public static Color FromArgb(uint argb) => FromArgb((int)argb);

    /// <summary>FUN_00013640: adds <paramref name="delta"/> to R, G and B with clamping; alpha unchanged.</summary>
    public static int AddRgb(int argb, int delta)
    {
        int r = Math.Clamp(((argb >> 16) & 0xff) + delta, 0, 255);
        int g = Math.Clamp(((argb >> 8) & 0xff) + delta, 0, 255);
        int b = Math.Clamp((argb & 0xff) + delta, 0, 255);
        return (int)((uint)argb & 0xff000000) | r << 16 | g << 8 | b;
    }
}
