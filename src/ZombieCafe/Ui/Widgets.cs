using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Gfx;

namespace ZombieCafe.Ui;

/// <summary>
/// Small immediate-mode widgets in virtual-screen points (the 1024x768-or-larger space of <see cref="Screen"/>).
/// Call <see cref="BeginFrame"/> once per update; widgets that see the mouse mark it as used so the world
/// underneath does not also react to the click.
/// </summary>
public static class Widgets
{
    static bool pressedHere;
    static Vector2 pressAt;

    /// <summary>The mouse is over some widget this frame (the world should ignore it).</summary>
    public static bool MouseOverUi { get; private set; }

    /// <summary>Widgets drawn while this is set show but do not react (a box is up over them).</summary>
    public static bool Blocked { get; set; }

    public static Vector2 Mouse => Screen.FromPixels(Input.MousePosition);

    /// <summary>Where the last message box's button (or a question's yes) was (test scripts click it by name).</summary>
    public static Rectangle? LastMessageButton { get; private set; }

    /// <summary>The mouse is over a button that can be pressed this frame (the cursor becomes the hand).</summary>
    public static bool OverButton { get; private set; }

    public static void BeginFrame()
    {
        MouseOverUi = false;
        OverButton = false;
        LastMessageButton = null;
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            pressedHere = true;
            pressAt = Mouse;
        }
    }

    /// <summary>Claims the mouse for a rectangle (panels and buttons).</summary>
    public static bool Hover(Rectangle r)
    {
        bool over = !Blocked && Raylib.CheckCollisionPointRec(Mouse, r);
        if (over)
            MouseOverUi = true;
        return over;
    }

    /// <summary>A click that started and ended inside <paramref name="r"/>.</summary>
    public static bool Clicked(Rectangle r) =>
        !Blocked && pressedHere && Input.IsMouseButtonReleased(MouseButton.Left)
        && Raylib.CheckCollisionPointRec(Mouse, r) && Raylib.CheckCollisionPointRec(pressAt, r);

    public static readonly Color PanelColor = new(36, 28, 22, 235);
    public static readonly Color PanelEdge = new(140, 30, 30, 255);
    public static readonly Color Gold = new(255, 230, 120, 255);

    /// <summary>
    /// A panel: with the original art, the in-game sheet's bar (ui 26, the command bars' and the store's backing)
    /// drawn out over the rectangle by its nine parts; else a plain filled box.
    /// </summary>
    public static void Panel(Rectangle r, Color? fill = null)
    {
        Hover(r);
        if (Skin is { } skin)
        {
            NineSlice(skin.Ui[26], new Rectangle(r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8), 16);
            return;
        }
        Gfx.Draw.Rect(new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), PanelEdge);
        Gfx.Draw.Rect(r, fill ?? PanelColor);
    }

    public static bool Button(Rectangle r, string text, float size = 24, bool enabled = true)
    {
        bool over = Hover(r) && enabled;
        OverButton |= over;
        bool down = over && Input.IsMouseButtonDown(MouseButton.Left);
        var bg = !enabled ? new Color(70, 70, 70, 255) : down ? new Color(150, 40, 40, 255) : over ? new Color(190, 60, 50, 255) : new Color(160, 45, 40, 255);
        Gfx.Draw.Rect(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(20, 10, 10, 255));
        Gfx.Draw.Rect(r, bg);
        Text.DrawAnchored(text, r.X + r.Width / 2, r.Y + r.Height / 2 + 1, size, Anchor.Center,
            enabled ? Color.White : new Color(170, 170, 170, 255), Color.Black, 2);
        return enabled && Clicked(r);
    }

    /// <summary>A sprite stretched over a rectangle by its nine parts: the corners as they are, the edges and the middle drawn out.</summary>
    public static void NineSlice(SubImage img, Rectangle r, float margin)
    {
        var s = img.Src;
        float m = MathF.Min(margin, MathF.Min(s.Width, s.Height) / 2);
        float dm = MathF.Min(m, MathF.Min(r.Width, r.Height) / 2);
        float[] sx = [s.X, s.X + m, s.X + s.Width - m, s.X + s.Width];
        float[] sy = [s.Y, s.Y + m, s.Y + s.Height - m, s.Y + s.Height];
        float[] dx = [r.X, r.X + dm, r.X + r.Width - dm, r.X + r.Width];
        float[] dy = [r.Y, r.Y + dm, r.Y + r.Height - dm, r.Y + r.Height];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Gfx.Draw.Image(img.Texture, new Rectangle(sx[i], sy[j], sx[i + 1] - sx[i], sy[j + 1] - sy[j]),
                    new Rectangle(dx[i], dy[j], dx[i + 1] - dx[i], dy[j + 1] - dy[j]), Color.White);
    }

    /// <summary>A button made of one of the original UI sprites, centred on (cx, cy); brightens on hover, dips when pressed.</summary>
    public static bool ImageButton(SubImage img, float cx, float cy, float scale = 1f, bool enabled = true)
    {
        float w = img.FullWidth * scale, h = img.FullHeight * scale;
        var r = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        bool over = Hover(r) && enabled;
        OverButton |= over;
        bool down = over && Input.IsMouseButtonDown(MouseButton.Left);
        float s = down ? scale * 0.94f : over ? scale * 1.05f : scale;
        var tint = !enabled ? new Color(120, 120, 120, 255) : over ? Color.White : new Color(235, 235, 235, 255);
        img.Draw(cx, cy, Anchor.Center, tint, Flip.None, s, s);
        return enabled && Clicked(r);
    }

    /// <summary>A sprite drawn centred at (cx, cy) with an optional scale.</summary>
    public static void Sprite(SubImage img, float cx, float cy, float scale = 1f, Color? tint = null, int anchor = Anchor.Center) =>
        img.Draw(cx, cy, anchor, tint ?? Color.White, Flip.None, scale, scale);

    /// <summary>A horizontal meter (energy, experience).</summary>
    public static void Bar(Rectangle r, float fraction, Color fill)
    {
        Gfx.Draw.Rect(r, new Color(15, 10, 10, 255));
        var inner = new Rectangle(r.X + 2, r.Y + 2, MathF.Max(0, (r.Width - 4) * Math.Clamp(fraction, 0, 1)), r.Height - 4);
        Gfx.Draw.Rect(inner, fill);
    }

    public static void Label(string text, float x, float y, float size, int anchor = Anchor.Top | Anchor.Left, Color? color = null) =>
        Text.DrawAnchored(text, x, y, size, anchor, color ?? Color.White, Color.Black, 2);

    /// <summary>
    /// The original dialogs' art, set once the game data is loaded: the dialog sheet (its chalkboards 5, 489x240, and
    /// 2, 485x327) and the in-game sheet (the green bar 10 and the red bar 17, 180x35). Null: plain panels.
    /// </summary>
    public static (SubImage[] Dialog, SubImage[] Ui)? Skin { get; set; }

    // what the chalkboard keeps clear around the words, and the bars' row at its foot
    const float BoardSide = 36, BoardTop = 30, BarsRow = 70;

    /// <summary>
    /// NoticeDialog / ConfirmDialog as GameStateCafe::load makes them (FUN_0017f480 / FUN_00165290): the screen dimmed
    /// and the chalkboard centred - the taller one, then stretched, when the words need more room.
    /// </summary>
    static Rectangle Chalkboard((SubImage[] Dialog, SubImage[] Ui) skin, float wordsH)
    {
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 140));
        MouseOverUi = true;
        float needed = BoardTop + wordsH + 14 + BarsRow;
        var board = needed <= skin.Dialog[5].FullHeight ? skin.Dialog[5] : skin.Dialog[2];
        float w = board.FullWidth, h = MathF.Max(board.FullHeight, needed);
        var r = new Rectangle(MathF.Round((Screen.Width - w) / 2), MathF.Round((Screen.Height - h) / 2), w, h);
        board.Draw(r.X, r.Y, Anchor.Top | Anchor.Left, Color.White, Flip.None, 1f, h / board.FullHeight);
        Hover(r);
        return r;
    }

    /// <summary>Where words of this height start, centred down the chalkboard above its bars.</summary>
    static float BoardTopFor(Rectangle r, float total) => r.Y + BoardTop + MathF.Max(0, (r.Height - BoardTop - BarsRow - total) / 2);

    /// <summary>Lines of words centred across the chalkboard from <paramref name="y"/> down; returns where they end.</summary>
    static float BoardWords(Rectangle r, IReadOnlyList<(string Text, float Size, Color Color)> rows, float y)
    {
        foreach (var (text, size, color) in rows)
        {
            Text.DrawAnchored(text, r.X + r.Width / 2, y, size, Anchor.Top | Anchor.HCenter, color, Color.Black, 2);
            y += size + 6;
        }
        return y;
    }

    const float PaperScale = 0.55f;

    /// <summary>The height a pinned picture takes on the chalkboard.</summary>
    static float PictureHeight((SubImage[] Dialog, SubImage[] Ui) skin) => skin.Ui[76].FullHeight * PaperScale + 14;

    /// <summary>
    /// A picture on a pinned sheet of paper (ui 76 and the pin, ui 40), as the original's recipe and reward dialogs
    /// (RecipeUnlockedDialog, FrequentFryerDialog) show the dish or the prize.
    /// </summary>
    static void PinnedPicture((SubImage[] Dialog, SubImage[] Ui) skin, SubImage picture, float cx, float top)
    {
        var paper = skin.Ui[76];
        float pw = paper.FullWidth * PaperScale, ph = paper.FullHeight * PaperScale;
        paper.Draw(cx, top + 4, Anchor.Top | Anchor.HCenter, Color.White, Flip.None, PaperScale, PaperScale);
        float k = MathF.Min(1f, MathF.Min((pw - 22) / Math.Max(1, picture.FullWidth), (ph - 30) / Math.Max(1, picture.FullHeight)));
        picture.Draw(cx, top + 4 + ph / 2 + 6, Anchor.Center, Color.White, Flip.None, k, k);
        skin.Ui[40].Draw(cx, top - 6, Anchor.Top | Anchor.HCenter, Color.White, Flip.None, 0.7f, 0.7f);
    }

    /// <summary>A button on one of the original's coloured bars, its word in the game font (32 pt, smaller if long).</summary>
    static bool BarButton(SubImage bar, float cx, float cy, string text, out Rectangle rect)
    {
        rect = new Rectangle(cx - bar.FullWidth / 2f, cy - bar.FullHeight / 2f, bar.FullWidth, bar.FullHeight);
        bool clicked = ImageButton(bar, cx, cy);
        float size = Text.GameFont[4];
        while (size > Text.GameFont[0] && Text.Measure(text, size).W > bar.FullWidth - 16)
            size -= 2;
        Text.DrawAnchored(text, cx, cy + 1, size, Anchor.Center, Color.White, Color.Black, 2);
        return clicked;
    }

    /// <summary>A yes/no question (the original's ConfirmDialog): its words (26 pt), yes on the green bar, no on the red. 0 while open, 1 yes, 2 no.</summary>
    public static int QuestionBox(string text, string yes, string no)
    {
        if (Skin is { } skin)
        {
            float size = Text.GameFont[3];
            var rows = Wrap(text, skin.Dialog[5].FullWidth - BoardSide * 2, size).Select(l => (l, size, Color.White)).ToList();
            var board = Chalkboard(skin, rows.Count * (size + 6));
            BoardWords(board, rows, BoardTopFor(board, rows.Count * (size + 6)));
            float cx = board.X + board.Width / 2, by = board.Y + board.Height - BarsRow / 2 - 4;
            bool yesHit = BarButton(skin.Ui[10], cx - 100, by, yes, out var yesBar);
            LastMessageButton = yesBar;
            if (yesHit || Input.IsKeyPressed(KeyboardKey.Enter))
                return 1;
            if (BarButton(skin.Ui[17], cx + 100, by, no, out _) || Input.IsKeyPressed(KeyboardKey.Escape))
                return 2;
            return 0;
        }
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 140));
        MouseOverUi = true;
        float w = 600;
        var lines = Wrap(text, w - 60, 22);
        float h = 140 + lines.Count * 30;
        var r = new Rectangle((Screen.Width - w) / 2, (Screen.Height - h) / 2, w, h);
        Panel(r);
        for (int i = 0; i < lines.Count; i++)
            Label(lines[i], r.X + w / 2, r.Y + 40 + i * 30, 22, Anchor.Center);
        var yesRect = new Rectangle(r.X + w / 2 - 190, r.Y + h - 62, 170, 44);
        LastMessageButton = yesRect;
        if (Button(yesRect, yes) || Input.IsKeyPressed(KeyboardKey.Enter))
            return 1;
        if (Button(new Rectangle(r.X + w / 2 + 20, r.Y + h - 62, 170, 44), no) || Input.IsKeyPressed(KeyboardKey.Escape))
            return 2;
        return 0;
    }

    /// <summary>Splits text into lines no wider than <paramref name="width"/> at <paramref name="size"/>.</summary>
    public static List<string> WrapText(string text, float width, float size) => Wrap(text, width, size);

    static List<string> Wrap(string text, float width, float size)
    {
        var result = new List<string>();
        // a line break starts a new line
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var attempt = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Text.Measure(attempt, size).W > width)
                {
                    result.Add(line);
                    line = word;
                }
                else
                {
                    line = attempt;
                }
            }
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// A modal box (the original's NoticeDialog): the title in gold (32 pt), the lines (26 pt), each wrapped to the
    /// chalkboard, and the button on the green bar. Returns true when dismissed.
    /// </summary>
    public static bool MessageBox(string title, IReadOnlyList<string> lines, string button, SubImage? picture = null)
    {
        if (Skin is { } skin)
        {
            float width = skin.Dialog[5].FullWidth - BoardSide * 2, big = Text.GameFont[4], size = Text.GameFont[3];
            var head = title.Length > 0 ? Wrap(title, width, big).Select(l => (l, big, Gold)).ToList() : [];
            var rows = new List<(string, float, Color)>();
            foreach (var line in lines)
                rows.AddRange(Wrap(line, width, size).Select(l => (l, size, Color.White)));
            float pictureH = picture != null ? PictureHeight(skin) : 0;
            float total = head.Sum(x => x.Item2 + 6) + pictureH + rows.Sum(x => x.Item2 + 6);
            var board = Chalkboard(skin, total);
            float y = BoardWords(board, head, BoardTopFor(board, total));
            if (picture != null)
            {
                PinnedPicture(skin, picture, board.X + board.Width / 2, y);
                y += pictureH;
            }
            BoardWords(board, rows, y);
            bool hit = BarButton(skin.Ui[10], board.X + board.Width / 2, board.Y + board.Height - BarsRow / 2 - 4, button, out var okRect);
            LastMessageButton = okRect;
            return hit || Input.IsKeyPressed(KeyboardKey.Enter) || Input.IsKeyPressed(KeyboardKey.Space);
        }
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 140));
        MouseOverUi = true;
        float w = 560, h = 150 + lines.Count * 30;
        var r = new Rectangle((Screen.Width - w) / 2, (Screen.Height - h) / 2, w, h);
        Panel(r);
        Label(title, r.X + w / 2, r.Y + 34, 34, Anchor.Center, Gold);
        for (int i = 0; i < lines.Count; i++)
            Label(lines[i], r.X + w / 2, r.Y + 80 + i * 30, 22, Anchor.Center);
        var ok = new Rectangle(r.X + w / 2 - 80, r.Y + h - 58, 160, 42);
        LastMessageButton = ok;
        return Button(ok, button) || Input.IsKeyPressed(KeyboardKey.Enter) || Input.IsKeyPressed(KeyboardKey.Space);
    }
}
