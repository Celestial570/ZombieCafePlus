using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;

namespace ZombieCafe.Screens;

/// <summary>
/// Developer view (--viewer sheet &lt;image&gt; &lt;atlas&gt;): every sub-image of a sprite sheet in a grid with its index,
/// to find the UI pieces the original code refers to by number. Mouse wheel scrolls.
/// </summary>
public sealed class SheetViewerScreen : GameScreen
{
    readonly SubImage[] sheet;
    readonly string title;
    float scroll;

    public SheetViewerScreen(GameData data, string[] args)
    {
        int i = Array.IndexOf(args, "sheet");
        string image = i >= 0 && i + 1 < args.Length ? args[i + 1] : "ipad_ingameUiImages";
        string? atlas = i >= 0 && i + 2 < args.Length && !args[i + 2].StartsWith("--") ? args[i + 2] : null;
        sheet = data.Sheet(image, atlas);
        title = $"{image} ({sheet.Length} images)";
        scroll = float.TryParse(Environment.GetEnvironmentVariable("ZC_SCROLL"), out var s) ? s : 0;
    }

    public override void Update(float dt) => scroll = MathF.Max(0, scroll - Raylib.GetMouseWheelMove() * 120);

    public override void Draw()
    {
        Raylib.ClearBackground(new Color(70, 70, 90, 255));
        Gfx.Draw.Reset(Matrix3x2.Identity);
        // ZC_SPRITE=n: just that sprite, as large as fits
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_SPRITE"), out int one) && one >= 0 && one < sheet.Length)
        {
            var big = sheet[one];
            float fit = MathF.Min(Screen.PixelWidth / (float)Math.Max(1, big.FullWidth), Screen.PixelHeight / (float)Math.Max(1, big.FullHeight));
            big.Draw(Screen.PixelWidth / 2f, Screen.PixelHeight / 2f, Anchor.Center, Color.White, Flip.None, fit, fit);
            return;
        }
        const float cell = 150;
        int cols = Math.Max(1, (int)(Screen.PixelWidth / cell));
        for (int i = 0; i < sheet.Length; i++)
        {
            float x = i % cols * cell, y = i / cols * cell - scroll + 30;
            if (y < -cell || y > Screen.PixelHeight)
                continue;
            var img = sheet[i];
            float s = MathF.Min(1f, MathF.Min((cell - 10) / Math.Max(1, img.FullWidth), (cell - 26) / Math.Max(1, img.FullHeight)));
            Gfx.Draw.Rect(new Rectangle(x + 2, y + 2, cell - 4, cell - 4), new Color(50, 50, 66, 255));
            img.Draw(x + cell / 2, y + cell / 2 + 6, Anchor.Center, Color.White, Flip.None, s, s);
            Text.Draw($"{i}  {img.FullWidth}x{img.FullHeight}", x + 6, y + 4, 16, Color.Yellow, Color.Black, 1);
        }
        Text.Draw(title, 8, 4, 20, Color.White, Color.Black, 2);
    }
}
