using System.Numerics;
using Raylib_cs;
using ZombieCafe.Assets;

namespace ZombieCafe.Gfx;

/// <summary>
/// Playback of a cut-out .anim over a parts sheet (original ImageAnimation + FlashAnimation).
/// Timing and looping follow ImageAnimation::update exactly; drawing follows FlashAnimation::draw.
/// </summary>
public sealed class FlashAnimation
{
    public AnimFile Anim { get; }
    public SubImage[] Parts { get; }

    /// <summary>+7 bit 0: mirror horizontally (used for the SE/NE facings).</summary>
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    /// <summary>+0x50: colour multiplied into every part.</summary>
    public Color Tint { get; set; } = Color.White;
    /// <summary>+0x54: draw the parts of a frame back to front.</summary>
    public bool ReverseOrder { get; set; }

    /// <summary>
    /// An image drawn instead of one part (the +0x38 list, filled by CharacterInstance::vf31): it follows the
    /// part's position (+ offset) but not its rotation, e.g. the dish a zombie carries or a chair back.
    /// </summary>
    public readonly record struct PartOverride(SubImage Image, float X, float Y, float Scale, bool FlipX, bool UseAnimFlip, Color Tint, bool WorldSpace = false);

    /// <summary>Part index -> replacement (shared with the owning character).</summary>
    public IReadOnlyDictionary<int, PartOverride>? Overrides { get; set; }

    /// <summary>+4: number of times to play (-1 = forever).</summary>
    public int LoopCount { get; set; } = -1;
    int loopsLeft = 1;           // +5
    bool multiStep = true;       // +6 bit 0: may advance several frames in one update

    /// <summary>+6 bit 0: may skip frames when an update covers several (off: one frame an update at most).</summary>
    public bool MultiStep { get => multiStep; set => multiStep = value; }
    bool playing;                // +6 bit 1
    bool finished;               // +6 bit 2
    bool reverse;                // +0x20

    /// <summary>+0x20: play backwards.</summary>
    public bool Reverse { get => reverse; set => reverse = value; }
    int timeMs;                  // +0x10

    public int FrameDurationMs { get; set; } // +0xc
    public int Frame { get; private set; }   // +9
    public int FrameCount => Anim.Frames.Count;
    public bool IsPlaying => playing;
    public bool IsFinished => finished;

    public FlashAnimation(AnimFile anim, SubImage[] parts)
    {
        Anim = anim;
        Parts = parts;
        FrameDurationMs = anim.FrameTimeMs;
        Reset();
    }

    /// <summary>FUN_001bd610: playback speed relative to the authored frame time.</summary>
    public void SetRate(float rate) => FrameDurationMs = (int)(Anim.FrameTimeMs / rate);

    /// <summary>ImageAnimation::reset (vf04).</summary>
    public void Reset()
    {
        Frame = reverse ? FrameCount - 1 : 0;
        loopsLeft = LoopCount;
        finished = false;
    }

    /// <summary>ImageAnimation::play (vf05): restarts a finished animation.</summary>
    public void Play()
    {
        if (finished)
            Reset();
        if (loopsLeft != 0)
            playing = true;
    }

    public void Stop() => playing = false;

    /// <summary>ImageAnimation vf0b(last frame) + stop: held on its final frame (a body lying where it fell).</summary>
    public void JumpToEnd()
    {
        Frame = reverse ? 0 : FrameCount - 1;
        finished = true;
        playing = false;
    }

    /// <summary>ImageAnimation::update (vf02), dt in milliseconds.</summary>
    public void Update(int dtMs)
    {
        if (finished || !playing || FrameDurationMs <= 0)
            return;
        timeMs += dtMs;
        if (timeMs < FrameDurationMs)
            return;
        int steps = multiStep ? timeMs / FrameDurationMs : 1;
        timeMs %= FrameDurationMs;
        int count = FrameCount;
        int f;
        if (!reverse)
        {
            f = Frame + steps;
            if (f >= count)
            {
                if (LoopCount == -1)
                {
                    f %= count;
                    if (loopsLeft >= 1)
                        loopsLeft--;
                }
                else if (loopsLeft < 2)
                {
                    playing = false;
                    f = count - 1;
                    finished = true;
                }
                else
                {
                    f %= count;
                    loopsLeft--;
                }
            }
        }
        else
        {
            f = Frame - steps;
            if (f < 0)
            {
                if (LoopCount == -1)
                {
                    f += count;
                    if (loopsLeft >= 1)
                        loopsLeft--;
                }
                else if (loopsLeft < 2)
                {
                    playing = false;
                    f = 0;
                    finished = true;
                }
                else
                {
                    f += count;
                    loopsLeft--;
                }
            }
        }
        Frame = Math.Clamp(f, 0, count - 1);
    }

    /// <summary>
    /// FUN_001bde3c + FlashAnimation::draw: translate to (x, y), scale by s, draw the frame's parts. The original's
    /// sign for x is +1 unless the facing is a mirrored one (+7 bit 0); its constant -1 belongs to the y axis of its
    /// y-up GL view, which this y-down screen does not need - so the art is drawn as authored ("SW" faces down-left).
    /// </summary>
    public void Draw(float x, float y, float scale, Color tint)
    {
        if (FrameCount == 0)
            return;
        var world = global::ZombieCafe.Gfx.Draw.Transform;
        global::ZombieCafe.Gfx.Draw.Push(Matrix3x2.CreateScale(scale, scale) * Matrix3x2.CreateTranslation(x, y));
        var mirror = Matrix3x2.CreateScale(FlipX ? -1 : 1, FlipY ? -1 : 1);
        var color = Multiply(Tint, tint);
        var parts = Anim.Frames[Frame];
        for (int k = 0; k < parts.Length; k++)
        {
            var p = parts[ReverseOrder ? parts.Length - 1 - k : k];
            var c = Anim.Version == 1 ? color : Multiply(color, FromRecordColor(p.Color));
            if (Overrides != null && Overrides.TryGetValue(p.Index, out var ov) && ov.WorldSpace)
            {
                // a piece of scenery (the chair under a sitter) drawn in place, on this part's layer
                var saved = global::ZombieCafe.Gfx.Draw.Transform;
                global::ZombieCafe.Gfx.Draw.Transform = world;
                ov.Image.Draw(ov.X, ov.Y, Anchor.Top | Anchor.Left, ov.Tint, ov.FlipX ? Flip.Horizontal : Flip.None);
                global::ZombieCafe.Gfx.Draw.Transform = saved;
                continue;
            }
            if (Overrides != null && Overrides.TryGetValue(p.Index, out ov))
            {
                // FUN_001bd7f0: an override keeps the part's translation (+ its offset) with a plain scale
                var at = System.Numerics.Vector2.Transform(new Vector2(p.Tx + ov.X, p.Ty - ov.Y), mirror);
                float sx = ov.Scale * (ov.FlipX ? -1 : 1) * (ov.UseAnimFlip && FlipX ? -1 : 1);
                var oimg = ov.Image;
                global::ZombieCafe.Gfx.Draw.Push(Matrix3x2.CreateScale(sx, ov.Scale) * Matrix3x2.CreateTranslation(at));
                oimg.DrawAt(-oimg.FullWidth / 2f + oimg.TrimLeft, -oimg.FullHeight / 2f + oimg.TrimTop, oimg.Width, oimg.Height, Multiply(c, ov.Tint));
                global::ZombieCafe.Gfx.Draw.Pop();
                continue;
            }
            if (p.Index >= Parts.Length)
                continue;
            var img = Parts[p.Index];
            global::ZombieCafe.Gfx.Draw.Push(new Matrix3x2(p.A, p.B, p.C, p.D, p.Tx, p.Ty) * mirror);
            img.DrawAt(-img.FullWidth / 2f + img.TrimLeft, -img.FullHeight / 2f + img.TrimTop, img.Width, img.Height, c);
            global::ZombieCafe.Gfx.Draw.Pop();
        }
        global::ZombieCafe.Gfx.Draw.Pop();
    }

    /// <summary>
    /// FlashAnimation::frameBounds: the box the current frame's parts cover, relative to the anchor point
    /// (as drawn by <see cref="Draw"/> at that scale).
    /// </summary>
    public Raylib_cs.Rectangle FrameBounds(float scale)
    {
        if (FrameCount == 0)
            return default;
        var root = Matrix3x2.CreateScale(scale, scale);
        var mirror = Matrix3x2.CreateScale(FlipX ? -1 : 1, FlipY ? -1 : 1);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in Anim.Frames[Frame])
        {
            if (p.Index >= Parts.Length)
                continue;
            var img = Parts[p.Index];
            var m = new Matrix3x2(p.A, p.B, p.C, p.D, p.Tx, p.Ty) * mirror * root;
            float x0 = -img.FullWidth / 2f + img.TrimLeft, y0 = -img.FullHeight / 2f + img.TrimTop;
            foreach (var corner in new[] { new Vector2(x0, y0), new Vector2(x0 + img.Width, y0), new Vector2(x0, y0 + img.Height), new Vector2(x0 + img.Width, y0 + img.Height) })
            {
                var q = Vector2.Transform(corner, m);
                minX = MathF.Min(minX, q.X);
                minY = MathF.Min(minY, q.Y);
                maxX = MathF.Max(maxX, q.X);
                maxY = MathF.Max(maxY, q.Y);
            }
        }
        return minX > maxX ? default : new Raylib_cs.Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    static Color FromRecordColor(uint c) => new((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));

    static Color Multiply(Color a, Color b) =>
        new((byte)(a.R * b.R / 255), (byte)(a.G * b.G / 255), (byte)(a.B * b.B / 255), (byte)(a.A * b.A / 255));
}
