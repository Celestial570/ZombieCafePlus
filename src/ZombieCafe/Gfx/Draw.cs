using System.Numerics;
using Raylib_cs;

namespace ZombieCafe.Gfx;

/// <summary>
/// Immediate-mode sprite drawing on top of rlgl. Everything the original drew with OpenGL ES 1.1
/// (textured quads under a 2D affine transform, tinted, alpha-blended) goes through here.
/// </summary>
public static class Draw
{
    const int RlQuads = 7;

    /// <summary>Current transform from caller space to window pixels.</summary>
    public static Matrix3x2 Transform = Matrix3x2.Identity;
    static readonly Stack<Matrix3x2> stack = new();

    public static void Push(Matrix3x2 local)
    {
        stack.Push(Transform);
        Transform = local * Transform;
    }

    public static void Pop() => Transform = stack.Pop();

    public static void Reset(Matrix3x2 root)
    {
        stack.Clear();
        Transform = root;
    }

    /// <summary>Draws texels <paramref name="src"/> of <paramref name="tex"/> as the rectangle <paramref name="dst"/> (caller space).</summary>
    public static void Image(Texture2D tex, Rectangle src, Rectangle dst, Color tint) =>
        Quad(tex, src, new Vector2(dst.X, dst.Y), new Vector2(dst.X + dst.Width, dst.Y),
            new Vector2(dst.X + dst.Width, dst.Y + dst.Height), new Vector2(dst.X, dst.Y + dst.Height), tint);

    /// <summary>
    /// A textured quad with explicit corners (TL, TR, BR, BL) in caller space and explicit texel corners for each
    /// (so a slanted strip of a picture can be drawn).
    /// </summary>
    public static void QuadUv(Texture2D tex, Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl,
        Vector2 uvTl, Vector2 uvTr, Vector2 uvBr, Vector2 uvBl, Color tint)
    {
        var m = Transform;
        tl = Vector2.Transform(tl, m);
        tr = Vector2.Transform(tr, m);
        br = Vector2.Transform(br, m);
        bl = Vector2.Transform(bl, m);
        float iw = 1f / tex.Width, ih = 1f / tex.Height;
        Rlgl.CheckRenderBatchLimit(4);
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(RlQuads);
        Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);
        Rlgl.TexCoord2f(uvTl.X * iw, uvTl.Y * ih); Rlgl.Vertex2f(tl.X, tl.Y);
        Rlgl.TexCoord2f(uvBl.X * iw, uvBl.Y * ih); Rlgl.Vertex2f(bl.X, bl.Y);
        Rlgl.TexCoord2f(uvBr.X * iw, uvBr.Y * ih); Rlgl.Vertex2f(br.X, br.Y);
        Rlgl.TexCoord2f(uvTr.X * iw, uvTr.Y * ih); Rlgl.Vertex2f(tr.X, tr.Y);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    /// <summary>Draws a textured quad with explicit corners (TL, TR, BR, BL) in caller space.</summary>
    public static void Quad(Texture2D tex, Rectangle src, Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl, Color tint)
    {
        var m = Transform;
        tl = Vector2.Transform(tl, m);
        tr = Vector2.Transform(tr, m);
        br = Vector2.Transform(br, m);
        bl = Vector2.Transform(bl, m);
        float iw = 1f / tex.Width, ih = 1f / tex.Height;
        float u0 = src.X * iw, v0 = src.Y * ih, u1 = (src.X + src.Width) * iw, v1 = (src.Y + src.Height) * ih;

        Rlgl.CheckRenderBatchLimit(4);
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(RlQuads);
        Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);
        Rlgl.TexCoord2f(u0, v0); Rlgl.Vertex2f(tl.X, tl.Y);
        Rlgl.TexCoord2f(u0, v1); Rlgl.Vertex2f(bl.X, bl.Y);
        Rlgl.TexCoord2f(u1, v1); Rlgl.Vertex2f(br.X, br.Y);
        Rlgl.TexCoord2f(u1, v0); Rlgl.Vertex2f(tr.X, tr.Y);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    /// <summary>Untextured filled rectangle in caller space.</summary>
    public static void Rect(Rectangle r, Color c)
    {
        var m = Transform;
        var tl = Vector2.Transform(new(r.X, r.Y), m);
        var tr = Vector2.Transform(new(r.X + r.Width, r.Y), m);
        var br = Vector2.Transform(new(r.X + r.Width, r.Y + r.Height), m);
        var bl = Vector2.Transform(new(r.X, r.Y + r.Height), m);
        Rlgl.CheckRenderBatchLimit(4);
        Rlgl.SetTexture(Rlgl.GetTextureIdDefault()); // rlgl keeps the last draw's texture otherwise
        Rlgl.Begin(RlQuads);
        Rlgl.Color4ub(c.R, c.G, c.B, c.A);
        Rlgl.TexCoord2f(0, 0); Rlgl.Vertex2f(tl.X, tl.Y);
        Rlgl.TexCoord2f(0, 1); Rlgl.Vertex2f(bl.X, bl.Y);
        Rlgl.TexCoord2f(1, 1); Rlgl.Vertex2f(br.X, br.Y);
        Rlgl.TexCoord2f(1, 0); Rlgl.Vertex2f(tr.X, tr.Y);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }
}
