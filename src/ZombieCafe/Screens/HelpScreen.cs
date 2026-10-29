using System.Numerics;
using System.Text;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// Help from the title screen (GameStateHelp: load FUN_0002d2d0, layout 0002e280, update 0002eb20): the zombie
/// chef beside a chalkboard showing one of the bundled pages, help.html to start with, and HELP / ABOUT / LEGAL
/// to switch between them (the original's web view; here <see cref="HtmlPage"/>), and BACK.
/// </summary>
public sealed class HelpScreen : GameScreen
{
    // the iPad's numbers (DAT_00370920 / DAT_00370938: the board's bottom-left; DAT_00370950: the gaps)
    const float BoardX = 300, BoardBottom = 500, Gap = 50;
    const float BackX = 1000, BackY = 754;
    // ipad_menuImages
    const int MenuWall = 4, MenuBack = 5, MenuAbout = 9, MenuHelp = 11, MenuBoard = 20, MenuChef = 28, MenuAwning = 29, MenuLegal = 49;
    const int SoundClick = 40;
    static readonly string[] Pages = ["help.html", "about.html", "legal.html"];

    readonly GameData data;
    readonly Audio.AudioSystem? audio;
    readonly Action back;
    readonly HtmlPage?[] pages = new HtmlPage?[Pages.Length];
    int shown;
    bool left;

    public HelpScreen(GameData data, Audio.AudioSystem? audio, Action back)
    {
        this.data = data;
        this.audio = audio;
        this.back = back;
    }

    SubImage[] Menu => data.Sheet("ipad_menuImages", "ipad_menuOffsets.bin");

    float scale = 1;
    Vector2 origin;
    Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;

    public override void Layout()
    {
        scale = MathF.Min(Screen.Width / Screen.BaseWidth, Screen.Height / Screen.BaseHeight);
        origin = new Vector2((Screen.Width - Screen.BaseWidth * scale) / 2, (Screen.Height - Screen.BaseHeight * scale) / 2);
        Input.TargetToPixels = ScriptTarget;
    }

    public override void Update(float dt)
    {
    }

    HtmlPage Page(int i)
    {
        if (pages[i] == null)
        {
            var bytes = data.Ipa.TryRead(Pages[i]);
            string html = bytes == null ? "" : Encoding.UTF8.GetString(bytes);
            pages[i] = new HtmlPage(html, src =>
            {
                var sheet = data.Sheet(Path.GetFileNameWithoutExtension(src));
                return sheet.Length > 0 ? sheet[0] : null;
            });
        }
        return pages[i]!;
    }

    // the page buttons in a row under the board, HELP, ABOUT, LEGAL
    (int Sprite, Vector2 Centre)[] PageButtons(SubImage[] menu)
    {
        int[] sprites = [MenuHelp, MenuAbout, MenuLegal];
        float total = sprites.Sum(s => menu[s].FullWidth) + Gap * (sprites.Length - 1);
        var board = menu[MenuBoard];
        float x = BoardX + board.FullWidth / 2f - total / 2;
        float y = BoardBottom + (BackY - menu[MenuBack].FullHeight - BoardBottom) / 2 + 10;
        var result = new (int, Vector2)[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
        {
            float w = menu[sprites[i]].FullWidth;
            result[i] = (sprites[i], new Vector2(x + w / 2, y));
            x += w + Gap;
        }
        return result;
    }

    public override void Draw()
    {
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        var menu = Menu;
        if (menu.Length <= MenuLegal)
            return;

        var wall = menu[MenuWall];
        float cover = MathF.Max(Screen.Width / wall.FullWidth, Screen.Height / wall.FullHeight);
        wall.Draw(Screen.Width / 2, Screen.Height / 2, Anchor.Center, Color.White, Flip.None, cover, cover);
        var awning = menu[MenuAwning];
        float aw = awning.FullWidth * scale;
        for (float x = origin.X - MathF.Ceiling(origin.X / aw) * aw; x < Screen.Width; x += aw)
            awning.Draw(x, 0, Anchor.Top | Anchor.Left, Color.White, Flip.None, scale, scale);

        // the chef in the corner, the board beside him
        var chefAt = new Vector2(origin.X, Screen.Height);
        menu[MenuChef].Draw(chefAt.X, chefAt.Y, Anchor.Bottom | Anchor.Left, Color.White, Flip.None, scale, scale);
        var board = menu[MenuBoard];
        var boardAt = P(BoardX, BoardBottom);
        board.Draw(boardAt.X, boardAt.Y, Anchor.Bottom | Anchor.Left, Color.White, Flip.None, scale, scale);
        var text = new Rectangle(boardAt.X + 34 * scale, boardAt.Y - (board.FullHeight - 30) * scale,
            (board.FullWidth - 68) * scale, (board.FullHeight - 60) * scale);
        Page(shown).Draw(text, scale);

        var buttons = PageButtons(menu);
        for (int i = 0; i < buttons.Length; i++)
        {
            var (sprite, c) = buttons[i];
            var at = P(c.X, c.Y);
            float s = scale * (i == shown ? 1.08f : 1f);
            if (Widgets.ImageButton(menu[sprite], at.X, at.Y, s) && i != shown)
            {
                audio?.Play(SoundClick);
                shown = i;
                Page(i).Reset();
            }
        }

        var backImg = menu[MenuBack];
        var backAt = P(BackX - backImg.FullWidth / 2f, BackY - backImg.FullHeight / 2f);
        if ((Widgets.ImageButton(backImg, backAt.X, backAt.Y, scale) || Input.IsKeyPressed(KeyboardKey.Escape)) && !left)
        {
            left = true;
            audio?.Play(SoundClick);
            back();
        }
    }

    Vector2? ScriptTarget(string name)
    {
        var menu = Menu;
        if (menu.Length <= MenuLegal)
            return null;
        var buttons = PageButtons(menu);
        Vector2? at = name switch
        {
            "back" => P(BackX - menu[MenuBack].FullWidth / 2f, BackY - menu[MenuBack].FullHeight / 2f),
            "helppage" => P(buttons[0].Centre.X, buttons[0].Centre.Y),
            "about" => P(buttons[1].Centre.X, buttons[1].Centre.Y),
            "legal" => P(buttons[2].Centre.X, buttons[2].Centre.Y),
            _ => null,
        };
        return at is { } p ? Screen.ToPixelsPoint(p) : null;
    }
}
