using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace ZombieCafe.Gfx;

/// <summary>
/// TrueType text in the game's own font (A Love of Thunder, shipped in the bundle), drawn glyph by glyph
/// through <see cref="Draw"/> so it follows the current transform (world or UI). The font is rasterised
/// once at a large size and scaled down; outlines are drawn as offset copies, like the original's
/// outlined TrueTypeTextElement.
/// </summary>
public static class Text
{
    const int BakeSize = 64;
    static Font font;
    static bool loaded;

    public static void Load(byte[] ttf)
    {
        if (loaded)
            Raylib.UnloadFont(font);
        // Latin-1 so names and the cafe's é come out right
        int[] codepoints = Enumerable.Range(32, 224).ToArray();
        font = Raylib.LoadFontFromMemory(".ttf", ttf, BakeSize, codepoints, codepoints.Length);
        glyphOf.Clear();
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        loaded = font.Texture.Id != 0;
    }

    public static bool IsLoaded => loaded;

    /// <summary>
    /// The original's seven bitmap fonts (game +0x11c, thunder_12pt .. thunder_36pt.fnt) as pixel sizes of this
    /// font: index 0 = 12pt (16 px) .. 6 = 36pt (48 px), from the .fnt headers.
    /// </summary>
    public static readonly float[] GameFont = [16, 19, 22, 26, 32, 37, 48];

    /// <summary>Width and line height of <paramref name="s"/> at <paramref name="size"/> pixels.</summary>
    public static (float W, float H) Measure(string s, float size)
    {
        if (!loaded)
            return (s.Length * size * 0.5f, size);
        float scale = size / font.BaseSize, w = 0;
        foreach (char ch in Expand(s))
        {
            int i = GlyphIndex(ch);
            if (i < 0)
                continue;
            unsafe
            {
                var g = font.Glyphs[i];
                w += (g.AdvanceX != 0 ? g.AdvanceX : font.Recs[i].Width) * scale;
            }
        }
        return (w, size);
    }

    static readonly Dictionary<int, int> glyphOf = new();

    /// <summary>Glyph slot of a character (raylib keeps only the glyphs the font has, so look them up by codepoint).</summary>
    static unsafe int GlyphIndex(char ch)
    {
        if (glyphOf.Count == 0)
            for (int g = 0; g < font.GlyphCount; g++)
                glyphOf[font.Glyphs[g].Value] = g;
        if (glyphOf.TryGetValue(ch, out int i))
            return i;
        // the game font has no accented letters or curly quotes: use the plain ones (Café -> Cafe, you’ll -> you'll)
        char plain = Plain(ch);
        return plain != ch && glyphOf.TryGetValue(plain, out i) ? i : -1;
    }

    const string Accented = "ÀÁÂÃÄÅàáâãäåÇçÈÉÊËèéêëÌÍÎÏìíîïÑñÒÓÔÕÖòóôõöÙÚÛÜùúûüÝýÿ‘’‚′“”„″–—− ";
    const string Unaccented = "AAAAAAaaaaaaCcEEEEeeeeIIIIiiiiNnOOOOOoooooUUUUuuuuYyy''''\"\"\"\"--- ";

    /// <summary>Spells out what the font cannot draw as one glyph (the ellipsis).</summary>
    static string Expand(string s) => s.Contains('…') ? s.Replace("…", "...") : s;

    static char Plain(char ch)
    {
        int i = Accented.IndexOf(ch);
        return i >= 0 ? Unaccented[i] : ch;
    }

    /// <summary>Draws text with its top-left at (x, y) in the current transform, optionally outlined.</summary>
    public static void Draw(string s, float x, float y, float size, Color color, Color outline = default, float outlineWidth = 0)
    {
        if (!loaded || string.IsNullOrEmpty(s))
            return;
        if (outlineWidth > 0 && outline.A > 0)
        {
            int steps = 12;
            for (int k = 0; k < steps; k++)
            {
                float ang = k * MathF.Tau / steps;
                DrawRun(s, x + MathF.Cos(ang) * outlineWidth, y + MathF.Sin(ang) * outlineWidth, size, outline);
            }
        }
        DrawRun(s, x, y, size, color);
    }

    static void DrawRun(string s, float x, float y, float size, Color color)
    {
        float scale = size / font.BaseSize;
        float pen = x;
        foreach (char ch in Expand(s))
        {
            int i = GlyphIndex(ch);
            if (i < 0)
                continue;
            unsafe
            {
                var g = font.Glyphs[i];
                var r = font.Recs[i];
                float px = pen + g.OffsetX * scale, py = y + g.OffsetY * scale;
                if (r.Width > 0 && r.Height > 0)
                {
                    var src = new Rectangle(r.X - font.GlyphPadding, r.Y - font.GlyphPadding, r.Width + 2 * font.GlyphPadding, r.Height + 2 * font.GlyphPadding);
                    var dst = new Rectangle(px - font.GlyphPadding * scale, py - font.GlyphPadding * scale, src.Width * scale, src.Height * scale);
                    global::ZombieCafe.Gfx.Draw.Image(font.Texture, src, dst, color);
                }
                pen += (g.AdvanceX != 0 ? g.AdvanceX : r.Width) * scale;
            }
        }
    }

    /// <summary>Draws text anchored like the original (Anchor flags).</summary>
    public static void DrawAnchored(string s, float x, float y, float size, int anchor, Color color, Color outline = default, float outlineWidth = 0)
    {
        var (w, h) = Measure(s, size);
        if ((anchor & Anchor.Right) != 0) x -= w;
        else if ((anchor & Anchor.HCenter) != 0) x -= w * 0.5f;
        if ((anchor & Anchor.Bottom) != 0) y -= h;
        else if ((anchor & Anchor.VCenter) != 0) y -= h * 0.5f;
        Draw(s, x, y, size, color, outline, outlineWidth);
    }
}
