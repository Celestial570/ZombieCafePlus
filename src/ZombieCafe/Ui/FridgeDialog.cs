using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;

namespace ZombieCafe.Ui;

/// <summary>
/// The fridge (FridgeDialog, FUN_001396f8, vf02 0013a06c): the inside of a fridge (ipad_dialogImages #3) holding two
/// dishes a page in the boxes recipeBookImageMap.bin marks out, an arrow at either side to turn the page and a
/// close cross in the corner. Each dish (FridgeRecipeElement, FUN_00137db8 / FUN_00138d10) shows its name, its
/// picture at half size, SERVE and UNLOCK - or the yellow button for a recipe that cannot be had - and its servings
/// and what they would earn. A press is remembered (+0x86, +0x88, +0x89) and the fridge shuts; the cafe acts on it.
/// </summary>
public sealed class FridgeDialog
{
    public enum Choice { Serve, Unlock, Locked }

    // strings.bin
    const int TextServings = 8, TextEarnings = 10, TextUnlock = 68, TextServe = 69;
    // ipad_dialogImages: the fridge inside; ipad_ingameUi: the button bars, the Beeline badge
    const int DialogFridge = 3, UiButton = 11, UiLockedButton = 206, UiBeeline = 189;
    const float PictureScale = 0.5f, BadgeScale = 0.7f;   // K 0x3d89e0 (iPad), FUN_00028348(.., 0.7, 0.7)

    readonly GameData data;
    readonly Vector2[] corners;   // image map 0: each box's top-left and bottom-right
    int page;                     // +0x84
    float clock;

    /// <summary>+0x1c.</summary>
    public bool Visible { get; private set; }
    /// <summary>+0x86 / +0x88 / +0x89: the dish picked and what for, once the fridge has shut on it.</summary>
    public (Choice Kind, Dish Item)? Picked { get; private set; }

    // last frame's buttons, for the tutorial arrow and test scripts
    public Rectangle? CloseRect { get; private set; }
    public Rectangle? PrevRect { get; private set; }
    public Rectangle? NextRect { get; private set; }
    public Rectangle?[] ServeRects { get; } = new Rectangle?[2];
    public Rectangle?[] UnlockRects { get; } = new Rectangle?[2];

    public FridgeDialog(GameData data)
    {
        this.data = data;
        corners = ReadImageMap(data.Ipa.Read("recipeBookImageMap.bin"), 0);
    }

    /// <summary>
    /// FUN_000e3600 / FUN_000e382c: an image map (type 2) - a count of maps, then for each a count of points and the
    /// points as big-endian signed shorts.
    /// </summary>
    static Vector2[] ReadImageMap(byte[] file, int map)
    {
        var fallback = new[] { new Vector2(44, 33), new Vector2(207, 289), new Vector2(277, 35), new Vector2(440, 291) };
        if (file.Length < 3 || file[0] != 2 || file[1] <= map)
            return fallback;
        int at = 2;
        for (int m = 0; m < map; m++)
            at += 1 + 4 * file[at];
        int n = file[at];
        if (n < 4 || at + 1 + 4 * n > file.Length)
            return fallback;
        var points = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int o = at + 1 + 4 * i;
            points[i] = new Vector2((short)(file[o] << 8 | file[o + 1]), (short)(file[o + 2] << 8 | file[o + 3]));
        }
        return points;
    }

    /// <summary>FUN_0013a828: open at the first page, nothing picked.</summary>
    public void Open()
    {
        if (Visible)
            return;
        page = 0;
        Picked = null;
        Visible = true;
    }

    /// <summary>The cafe has acted on the pick (FUN_0013a870 / FUN_0013a858).</summary>
    public void ClearPick() => Picked = null;

    /// <summary>FUN_00139d78: the page that shows a dish (the tutorial turns to it).</summary>
    public void ShowPageOf(int index) => page = Math.Max(0, index) / 2;

    /// <summary>
    /// vf02: the fridge and what is in it, and the presses. <paramref name="buttons"/> says which buttons a dish
    /// gets (the cafe's FUN_00138d10 rules); <paramref name="sound"/> plays the close sound.
    /// </summary>
    public void Draw(IReadOnlyList<Dish> stock, Func<Dish, (bool Serve, bool Unlock, bool Locked)> buttons, Action<int> sound, float dt)
    {
        CloseRect = PrevRect = NextRect = null;
        Array.Clear(ServeRects);
        Array.Clear(UnlockRects);
        if (!Visible)
            return;
        clock += dt;
        var sheet = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        var ui = data.IngameUi;
        var inside = sheet.Length > DialogFridge ? sheet[DialogFridge] : null;
        float w = inside?.FullWidth ?? 491, h = inside?.FullHeight ?? 333;
        var o = new Vector2(MathF.Round((Screen.Width - w) / 2), MathF.Round((Screen.Height - h) / 2));
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 120));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        inside?.Draw(o.X, o.Y, Anchor.Top | Anchor.Left, Color.White);

        // FUN_00139d78: two dishes a page; an empty fridge shows none
        int pages = Math.Max(1, (stock.Count + 1) / 2);
        page = Math.Clamp(page, 0, pages - 1);
        (Choice, Dish)? press = null;
        for (int slot = 0; slot < 2; slot++)
        {
            int i = page * 2 + slot;
            if (i >= stock.Count)
                continue;
            var tl = o + corners[slot * 2];
            var br = o + corners[slot * 2 + 1];
            if (DrawDish(stock[i], tl, br - tl, buttons(stock[i]), slot) is { } c)
                press ??= (c, stock[i]);
        }

        // the arrows bob in and out at the sides (+0x74 / +0x78: anchored left / right, half way down)
        float bob = MathF.Abs(MathF.Sin(clock * MathF.PI));
        var left = ui[UiSprites.ArrowLeft];
        var right = ui[UiSprites.ArrowRight];
        if (page > 0)
        {
            float x = o.X + bob * left.FullWidth / 4 + left.FullWidth / 2f;
            PrevRect = new Rectangle(x - left.FullWidth / 2f, o.Y + h / 2 - left.FullHeight / 2f, left.FullWidth, left.FullHeight);
            if (Widgets.ImageButton(left, x, o.Y + h / 2))
                page--;
        }
        if (page * 2 + 2 < stock.Count)
        {
            float x = o.X + w - bob * right.FullWidth / 4 - right.FullWidth / 2f;
            NextRect = new Rectangle(x - right.FullWidth / 2f, o.Y + h / 2 - right.FullHeight / 2f, right.FullWidth, right.FullHeight);
            if (Widgets.ImageButton(right, x, o.Y + h / 2))
                page++;
        }

        // +0x7c: the cross at the top right corner shuts the fridge (sound 36)
        var close = ui[UiSprites.CloseX];
        var cc = new Vector2(o.X + w - close.FullWidth / 2f, o.Y + close.FullHeight / 2f);
        CloseRect = new Rectangle(cc.X - close.FullWidth / 2f, cc.Y - close.FullHeight / 2f, close.FullWidth, close.FullHeight);
        if (Widgets.ImageButton(close, cc.X, cc.Y) || (!Widgets.Blocked && Input.IsKeyPressed(KeyboardKey.Escape)))
        {
            sound(36);
            Visible = false;
            return;
        }
        if (press is { } p)
        {
            Picked = p;
            Visible = false;
        }
    }

    /// <summary>
    /// One FridgeRecipeElement: the name across the top (black, 22 px), servings and earnings at the bottom (16 px,
    /// labels left, numbers right), SERVE above them on the left and UNLOCK on the right (a box's sixteenth
    /// clear of the text), the picture standing between them, and the Beeline badge on a Beeline dish.
    /// </summary>
    Choice? DrawDish(Dish dish, Vector2 at, Vector2 size, (bool Serve, bool Unlock, bool Locked) show, int slot)
    {
        var ui = data.IngameUi;
        var food = data.Foods[dish.Food];
        var ink = new Color(0, 0, 0, 255);
        float titleSize = Text.GameFont[2], textSize = Text.GameFont[0];

        var title = Widgets.WrapText(dish.DisplayName(data), size.X, titleSize);
        for (int l = 0; l < title.Count; l++)
            Text.DrawAnchored(title[l], at.X + size.X / 2, at.Y + l * titleSize, titleSize, Anchor.Top | Anchor.HCenter, ink);

        // +0x74 / +0x78: "Servings" / "Total Earnings", and the numbers (servings x price with the variant)
        float bottom = at.Y + size.Y;
        Text.DrawAnchored(data.Strings[TextServings], at.X, bottom - textSize, textSize, Anchor.Bottom | Anchor.Left, ink);
        Text.DrawAnchored(data.Strings[TextEarnings], at.X, bottom, textSize, Anchor.Bottom | Anchor.Left, ink);
        Text.DrawAnchored(dish.Servings.ToString(), at.X + size.X, bottom - textSize, textSize, Anchor.Bottom | Anchor.Right, ink);
        Text.DrawAnchored((food.PriceWith(dish.Modifier) * dish.Servings).ToString(), at.X + size.X, bottom, textSize, Anchor.Bottom | Anchor.Right, ink);

        var bar = ui[UiButton];
        float buttonsBottom = bottom - 2 * textSize - size.Y / 16;
        float buttonsTop = buttonsBottom - bar.FullHeight;
        Choice? press = null;

        // the picture, bottom centred on SERVE's top right corner
        var pic = data.FoodImage(dish.Food);
        if (pic != null)
        {
            float px = at.X + bar.FullWidth, py = buttonsTop;
            pic.Draw(px, py, Anchor.Bottom | Anchor.HCenter, Color.White, Flip.None, PictureScale, PictureScale);
            // +0x90: a Beeline dish (kind 4) wears the badge on top
            if (food.Kind == 4 && ui.Length > UiBeeline)
                ui[UiBeeline].Draw(px, py - pic.FullHeight * PictureScale, Anchor.Center, Color.White, Flip.None, BadgeScale, BadgeScale);
        }

        if (show.Serve)
        {
            var c = new Vector2(at.X + bar.FullWidth / 2f, buttonsBottom - bar.FullHeight / 2f);
            ServeRects[slot] = new Rectangle(c.X - bar.FullWidth / 2f, c.Y - bar.FullHeight / 2f, bar.FullWidth, bar.FullHeight);
            if (Widgets.ImageButton(bar, c.X, c.Y))
                press = Choice.Serve;
            Text.DrawAnchored(data.Strings[TextServe], c.X, c.Y, titleSize, Anchor.Center, Color.White, ink, 2);
        }
        if (show.Unlock || show.Locked)
        {
            var img = show.Locked && ui.Length > UiLockedButton ? ui[UiLockedButton] : bar;
            var c = new Vector2(at.X + size.X - img.FullWidth / 2f, buttonsBottom - img.FullHeight / 2f);
            UnlockRects[slot] = new Rectangle(c.X - img.FullWidth / 2f, c.Y - img.FullHeight / 2f, img.FullWidth, img.FullHeight);
            if (Widgets.ImageButton(img, c.X, c.Y))
                press = show.Locked ? Choice.Locked : Choice.Unlock;
            Text.DrawAnchored(data.Strings[TextUnlock], c.X, c.Y, titleSize, Anchor.Center, Color.White, ink, 2);
        }
        return press;
    }
}
