using System.Numerics;
using Raylib_cs;
using ZombieCafe.Assets;

namespace ZombieCafe.Gfx;

/// <summary>Anchor flags of the original image API (J2ME style): x = LEFT/HCENTER/RIGHT, y = TOP/VCENTER/BOTTOM.</summary>
public static class Anchor
{
    public const int Top = 0x01, VCenter = 0x02, Bottom = 0x04, Left = 0x08, HCenter = 0x10, Right = 0x20;
    public const int Center = HCenter | VCenter;
}

/// <summary>Flip flags (bit 0 = mirror horizontally, bit 1 = vertically).</summary>
public static class Flip
{
    public const int None = 0, Horizontal = 1, Vertical = 2;
}

/// <summary>
/// A rectangle of a texture plus the transparent margins that were cropped off the original image
/// (the original's sub-image: x, y, w, h at +8..+0xe, trims at +0x10..+0x16, scale at +0x18).
/// </summary>
public sealed class SubImage
{
    public Texture2D Texture { get; }
    public Rectangle Src { get; }
    public int TrimLeft { get; }
    public int TrimTop { get; }
    public int TrimRight { get; }
    public int TrimBottom { get; }
    public float Scale { get; }

    public int Width => (int)Src.Width;
    public int Height => (int)Src.Height;
    public int FullWidth => Width + TrimLeft + TrimRight;
    public int FullHeight => Height + TrimTop + TrimBottom;

    public SubImage(Texture2D texture, Rectangle src, int trimLeft = 0, int trimTop = 0, int trimRight = 0, int trimBottom = 0, float scale = 1)
    {
        Texture = texture;
        Src = src;
        TrimLeft = trimLeft;
        TrimTop = trimTop;
        TrimRight = trimRight;
        TrimBottom = trimBottom;
        Scale = scale;
    }

    public static SubImage Whole(Texture2D t) => new(t, new Rectangle(0, 0, t.Width, t.Height));

    public static SubImage FromAtlas(Texture2D t, SpriteAtlas.Entry e) =>
        new(t, new Rectangle(e.X, e.Y, e.Width, e.Height), e.TrimLeft, e.TrimTop, e.TrimRight, e.TrimBottom);

    /// <summary>
    /// Port of the original quad builder (FUN_000421c8): positions are integers, trims are added/removed per
    /// anchor before the size-based anchor offset, flips swap texture coordinates only.
    /// </summary>
    public void Draw(float x, float y, int anchor, Color color, int flip = Flip.None, float sx = 1, float sy = 1)
    {
        int ix = (int)x, iy = (int)y;
        if ((anchor & 0x18) != 0) ix = (int)(ix + TrimLeft * sx);
        if ((anchor & 0x30) != 0) ix = (int)(ix - TrimRight * sx);
        if ((anchor & 0x03) != 0) iy = (int)(iy + TrimTop * sy);
        if ((anchor & 0x06) != 0) iy = (int)(iy - TrimBottom * sy);
        int w = (int)(Scale * sx * Width), h = (int)(Scale * sy * Height);
        if ((anchor & Anchor.Right) != 0) ix -= w;
        else if ((anchor & Anchor.HCenter) != 0) ix -= w >> 1;
        if ((anchor & Anchor.Bottom) != 0) iy -= h;
        else if ((anchor & Anchor.VCenter) != 0) iy -= h >> 1;
        DrawAt(ix, iy, w, h, color, flip);
    }

    /// <summary>
    /// Whether (px, py) falls on a solid pixel of this image drawn by <see cref="Draw"/> with the same arguments (a
    /// texture without a mask counts as solid all over its rectangle).
    /// </summary>
    public bool Hit(float x, float y, int anchor, int flip, float sx, float sy, float px, float py)
    {
        int ix = (int)x, iy = (int)y;
        if ((anchor & 0x18) != 0) ix = (int)(ix + TrimLeft * sx);
        if ((anchor & 0x30) != 0) ix = (int)(ix - TrimRight * sx);
        if ((anchor & 0x03) != 0) iy = (int)(iy + TrimTop * sy);
        if ((anchor & 0x06) != 0) iy = (int)(iy - TrimBottom * sy);
        int w = (int)(Scale * sx * Width), h = (int)(Scale * sy * Height);
        if ((anchor & Anchor.Right) != 0) ix -= w;
        else if ((anchor & Anchor.HCenter) != 0) ix -= w >> 1;
        if ((anchor & Anchor.Bottom) != 0) iy -= h;
        else if ((anchor & Anchor.VCenter) != 0) iy -= h >> 1;
        return HitAt(ix, iy, w, h, flip, px, py);
    }

    /// <summary>Whether (px, py) falls on a solid pixel of the rectangle <see cref="DrawAt"/> would fill.</summary>
    public bool HitAt(float x, float y, float w, float h, int flip, float px, float py)
    {
        if (w == 0 || h == 0)
            return false;
        float u = (px - x) / w, v = (py - y) / h;
        if (u < 0 || u >= 1 || v < 0 || v >= 1)
            return false;
        if ((flip & Flip.Horizontal) != 0)
            u = 1 - u;
        if ((flip & Flip.Vertical) != 0)
            v = 1 - v;
        int tx = (int)(Src.X + u * Src.Width), ty = (int)(Src.Y + v * Src.Height);
        return AlphaMasks.IsSolid(Texture.Id, tx, ty) ?? true;
    }

    /// <summary>Draws the cropped rectangle with its top-left at (x, y), size w x h, in the current transform.</summary>
    public void DrawAt(float x, float y, float w, float h, Color color, int flip = Flip.None)
    {
        var src = Src;
        if ((flip & Flip.Horizontal) != 0) src = new Rectangle(src.X + src.Width, src.Y, -src.Width, src.Height);
        if ((flip & Flip.Vertical) != 0) src = new Rectangle(src.X, src.Y + src.Height, src.Width, -src.Height);
        Gfx.Draw.Image(Texture, src, new Rectangle(x, y, w, h), color);
    }
}
