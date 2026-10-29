using System.Numerics;
using Raylib_cs;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;

namespace ZombieCafe.Ui;

/// <summary>
/// The tutorial's big red arrow (TutorialArrow, GameStateCafe +0xec): it bobs over a cafe object
/// (FUN_001503bc) or over a button (FUN_00150428), or points up at it from below when flipped.
/// </summary>
public sealed class TutorialArrow
{
    readonly GameData data;
    // +0x68 / +0x6c: what it points at (a button is looked up every frame, the UI being immediate-mode)
    CafeObject? obj;
    Func<Rectangle?>? button;
    Vector2? spot;             // a place in the cafe with nothing standing there (the doorway)
    bool flip;                 // +0x70
    Vector2 offset;            // +0x80
    int clock;                 // +0x78

    public TutorialArrow(GameData data) => this.data = data;

    public bool Visible => obj != null || button != null || spot != null;
    public CafeObject? Target => obj;
    public bool PointsAtButton => button != null;

    /// <summary>FUN_001503bc.</summary>
    public void PointAt(CafeObject target, bool flipped = false, float dx = 0, float dy = 0)
    {
        SetObject(target);
        button = null;
        spot = null;
        flip = flipped;
        offset = new Vector2(dx, dy);
    }

    /// <summary>FUN_00150428: point at a button (its rectangle in virtual-screen points, or null while hidden).</summary>
    public void PointAt(Func<Rectangle?> target, bool flipped = false, float dx = 0, float dy = 0)
    {
        SetObject(null);
        button = target;
        spot = null;
        flip = flipped;
        offset = new Vector2(dx, dy);
    }

    /// <summary>Point down at a place in the cafe (cafe coordinates).</summary>
    public void PointAt(Vector2 world)
    {
        SetObject(null);
        button = null;
        spot = world;
        flip = false;
        offset = Vector2.Zero;
    }

    /// <summary>FUN_00150020.</summary>
    public void Hide()
    {
        SetObject(null);
        button = null;
        spot = null;
    }

    // the original listens for its target leaving the cafe (FUN_0019909c) and lets go of it
    void SetObject(CafeObject? target)
    {
        if (obj != null)
            obj.Removed -= OnRemoved;
        obj = target;
        if (obj != null)
            obj.Removed += OnRemoved;
    }

    void OnRemoved(CafeObject o)
    {
        if (o == obj)
            SetObject(null);
    }

    /// <summary>Tapping inside what the arrow points at (FUN_001504b4); false when it points at nothing.</summary>
    public bool Hits(Vector2 worldPoint, Vector2 screenPoint, Func<CafeObject, Vector2, bool> hitObject) =>
        obj != null ? hitObject(obj, worldPoint)
        : button?.Invoke() is { } r && Raylib.CheckCollisionPointRec(screenPoint, r);

    public void Update(int dtMs)
    {
        // FUN_001501c0: a half sine over the period (K 1000 ms, DAT_0042d588)
        int period = Math.Max(1, data.K.Int(0x42d588));
        clock += dtMs;
        if (clock > period)
            clock -= period;
    }

    /// <summary>
    /// Draws in the UI pass. <paramref name="worldToScreen"/> maps cafe coordinates to virtual-screen points.
    /// </summary>
    public void Draw(Func<Vector2, Vector2> worldToScreen)
    {
        var img = data.IngameUi[UiSprites.BigArrow];
        float bob = img.FullHeight / 3f * MathF.Sin(clock * MathF.PI / Math.Max(1, data.K.Int(0x42d588)));
        Vector2 tip;
        if (obj != null)
        {
            var p = new Vector2(obj.X, obj.Y);
            if (!flip)
            {
                // above the thing: the top of a piece of furniture's picture, a character's head
                if (obj is Furniture f)
                    p.Y = f.DrawY;
                else if (obj is CharacterInstance)
                    p.Y -= 165;
            }
            tip = worldToScreen(p + offset);
        }
        else if (spot is { } w)
        {
            tip = worldToScreen(w);
        }
        else if (button?.Invoke() is { } r)
        {
            tip = new Vector2(r.X + r.Width / 2, flip ? r.Y + r.Height : r.Y) - offset;
        }
        else
        {
            return;
        }
        if (flip)
            img.Draw(tip.X, tip.Y + bob, Anchor.Top | Anchor.HCenter, Color.White, Flip.Vertical);
        else
            img.Draw(tip.X, tip.Y - bob, Anchor.Bottom | Anchor.HCenter, Color.White);
    }
}
