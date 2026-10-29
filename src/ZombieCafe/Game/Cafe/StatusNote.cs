using Raylib_cs;
using ZombieCafe.Gfx;

namespace ZombieCafe.Game.Cafe;

/// <summary>
/// A note floating over a character or piece of furniture (original StatusNote : CafeUIElement,
/// FUN_001dd430): money earned, experience, mood bubbles, "Ready!" and "Burned" signs. Icon notes of
/// kinds 0..3 rise and fade with text beside the icon; kinds 4+ sit in a speech bubble.
/// </summary>
public sealed class StatusNote
{
    /// <summary>Note kinds = index into the icon table DAT_0037a680.</summary>
    public enum Kind
    {
        Money = 0, Xp = 1, Toxin = 2, Food = 3, Happy = 4, Angry = 5, Impatient = 6, K7 = 7, K8 = 8,
        Ready = 11, Burnt = 12,
    }

    static readonly int[] Icons = [78, 77, 36, 8, 74, 72, 70, 73, 72, 0, 0, 69, 84];

    readonly CafeObject target;   // +4
    public Kind Type { get; }     // +8
    readonly bool iconMode;       // +9
    int elapsed;                  // +0xc
    float x, y;                   // +0x10, +0x14
    readonly int heightOffset;    // +0x18
    int alpha = 255;              // +0x1c
    public string Text { get; set; }
    /// <summary>+0x24: second line drawn after the first (the tip of a payment), with its own colour.</summary>
    public string? Extra { get; set; }
    public Color ExtraColor { get; set; } = Color.White;
    readonly int duration;        // +0x30
    readonly bool altBubble;      // +0x34
    readonly Color color;

    public StatusNote(Cafe cafe, CafeObject target, Kind type, string text, int durationMs,
        Color? color = null, bool altBubble = false, bool iconMode = true)
    {
        this.target = target;
        Type = type;
        Text = text;
        duration = durationMs;
        this.color = color ?? Color.White;
        this.altBubble = altBubble;
        this.iconMode = iconMode;
        // stand on top of the target: furniture by its sprite height, characters by their height
        heightOffset = target switch
        {
            Furniture f => (int)f.Height - (int)(f.Y - f.DrawY) + 0,
            CharacterInstance c => (int)c.Height,
            _ => 0,
        };
        Update(0);
    }

    public bool IsFinished => duration <= elapsed;

    /// <summary>vf03.</summary>
    public void Update(int dtMs)
    {
        elapsed += dtMs;
        x = target.X;
        y = target.Y - heightOffset;
        if (!iconMode || (int)Type < 4)
        {
            // rises 100 px and fades out over its lifetime
            if (duration > 0 && duration != int.MaxValue)
            {
                y -= elapsed * 100 / duration;
                alpha = Math.Clamp(255 - elapsed * 255 / duration, 0, 255);
            }
        }
    }

    /// <summary>vf02 (drawn at 170/255 opacity).</summary>
    public void Draw(GameData data)
    {
        var ui = data.IngameUi;
        byte a = (byte)(170 * alpha / 255);
        var tint = new Color((byte)255, (byte)255, (byte)255, a);
        var textColor = new Color(color.R, color.G, color.B, a);
        const float size = 20f;
        if (!iconMode)
        {
            DrawLine(x, y, size, textColor, a);
            return;
        }
        int icon = (int)Type < Icons.Length ? Icons[(int)Type] : 0;
        if ((int)Type > 3)
        {
            // speech bubble standing on the target, icon centred 78 px up
            int bubbleIndex = altBubble ? 75 : 71;
            if (bubbleIndex < ui.Length)
            {
                var bubble = ui[bubbleIndex];
                bubble.Draw(x, y, Anchor.Bottom | Anchor.Left, tint);
                if (icon < ui.Length)
                    ui[icon].Draw(x + (bubble.FullWidth >> 1), y - data.K.Int(0x42d558), Anchor.Center, tint);
            }
            return;
        }
        if (icon < ui.Length)
        {
            var img = ui[icon];
            img.Draw(x, y, Anchor.VCenter | Anchor.Right, tint);
            DrawLine(x, y - size * 0.6f, size, textColor, a);
        }
    }

    void DrawLine(float lx, float ly, float size, Color c, byte a)
    {
        var outline = new Color((byte)0, (byte)0, (byte)0, a);
        Gfx.Text.Draw(Text, lx, ly, size, c, outline, 2);
        if (Extra != null)
        {
            var (w, _) = Gfx.Text.Measure(Text, size);
            Gfx.Text.Draw(Extra, lx + w, ly, size, new Color(ExtraColor.R, ExtraColor.G, ExtraColor.B, a), outline, 2);
        }
    }
}
