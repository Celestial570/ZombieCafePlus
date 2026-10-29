using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;

namespace ZombieCafe.Ui;

/// <summary>
/// The chef's message box (MessageElement, GameStateCafe +0xa4): the zombie chef stands at the left end of a
/// chalkboard across the bottom of the screen and the hint is chalked up four lines at a time. "Next" turns
/// the page, "OK" on the last one puts the board away; the buttons only answer once the board has been up a
/// second (0.4 s after a page turn). The cafe stands still while it is up (GameStateCafe::update skips the
/// simulation).
/// </summary>
public sealed class ChefMessage
{
    readonly GameData data;
    readonly Action<int> sound;
    readonly SubImage? board, chef;
    List<string> lines = new();
    int page;
    // +0x88: time before the buttons answer; +0x8c: fade-in
    int buttonDelay, fadeIn;
    int amount;
    string text = "";

    /// <summary>Lines per page on the iPad (DAT_003757f8).</summary>
    const int LinesPerPage = 4;
    const float FontSize = 27, LineHeight = 33;
    static readonly Color Chalk = new(0xa7, 0xce, 0x51, 0xff);   // K 0xffa7ce51 (DAT_0042d428)

    public bool Visible { get; private set; }

    public ChefMessage(GameData data, Action<int> sound)
    {
        this.data = data;
        this.sound = sound;
        var dialog = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        board = dialog.Length > 1 ? dialog[1] : null;
        var portrait = data.Sheet("zombieChef2");
        chef = portrait.Length > 0 ? portrait[0] : null;
    }

    /// <summary>FUN_001134c4: chalk up a message; <paramref name="toxin"/> &gt; 0 also shows a toxin reward.</summary>
    public void Show(string message, int toxin = 0)
    {
        text = message;
        amount = toxin;
        lines.Clear();
        page = 0;
        Visible = true;
        buttonDelay = data.K.Int(0x42d42c);
        fadeIn = data.K.Int(0x42d438);
        sound(0x71);
    }

    public void Hide() => Visible = false;

    /// <summary>MessageElement::vf02 (the timers; the buttons are handled where they are drawn).</summary>
    public void Update(int dtMs)
    {
        if (!Visible)
            return;
        buttonDelay -= dtMs;
        fadeIn = Math.Max(0, fadeIn - dtMs);
    }

    // the chalk area between the chef and the button
    (float X, float Y, float W) TextArea(SubImage button)
    {
        float chefW = chef != null ? chef.FullWidth * ChefScale : 0;
        float boardH = board?.FullHeight ?? 171;
        float x = chefW * 7f / 8f;
        float right = Screen.Width - button.FullWidth - button.FullHeight / 2f - 12;
        return (x, Screen.Height - boardH + 18, MathF.Max(200, right - x));
    }

    // the portrait is drawn a little smaller than the iPad's so it leaves room for the text on a 1024 wide screen
    const float ChefScale = 0.62f;

    static List<string> Wrap(string text, float width, float size)
    {
        var result = new List<string>();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
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

    /// <summary>The OK / Next button's centre (virtual screen points), for test scripts.</summary>
    public System.Numerics.Vector2? ButtonCenter
    {
        get
        {
            if (!Visible)
                return null;
            var b = data.IngameUi[UiSprites.Ok];
            return new System.Numerics.Vector2(Screen.Width - b.FullHeight / 2f - b.FullWidth / 2f, Screen.Height - b.FullHeight);
        }
    }

    /// <summary>Draws the board and handles its button. Call in the UI pass (virtual screen points).</summary>
    public void Draw()
    {
        if (!Visible)
            return;
        var ui = data.IngameUi;
        bool lastPage;
        var ok = ui[UiSprites.Ok];
        var area = TextArea(ok);
        if (lines.Count == 0)
            lines = Wrap(text, area.W, FontSize);
        lastPage = (page + 1) * LinesPerPage >= lines.Count;
        var button = lastPage ? ok : ui[UiSprites.Next];

        float t = 1f - fadeIn / (float)Math.Max(1, data.K.Int(0x42d438));
        byte alpha = (byte)(255 * Math.Clamp(t, 0f, 1f));
        var white = new Color((byte)255, (byte)255, (byte)255, alpha);

        // the board takes the clicks under it, so taps there do not reach the cafe
        float boardH = board?.FullHeight ?? 171;
        Widgets.Hover(new Rectangle(0, Screen.Height - boardH, Screen.Width, boardH));
        if (board != null)
            board.DrawAt(0, Screen.Height - boardH, Screen.Width, boardH, white);
        else
            Gfx.Draw.Rect(new Rectangle(0, Screen.Height - boardH, Screen.Width, boardH), new Color((byte)30, (byte)35, (byte)30, alpha));
        chef?.Draw(0, Screen.Height, Anchor.Bottom | Anchor.Left, white, Flip.None, ChefScale, ChefScale);

        var chalk = new Color(Chalk.R, Chalk.G, Chalk.B, alpha);
        for (int i = 0; i < LinesPerPage; i++)
        {
            int n = page * LinesPerPage + i;
            if (n >= lines.Count)
                break;
            Text.Draw(lines[n], area.X, area.Y + i * LineHeight, FontSize, chalk);
        }
        if (amount > 0)
        {
            // the reward: the toxin vial and "+n" above the button (+0x70, +0x80)
            float ax = Screen.Width - button.FullHeight / 2f - button.FullWidth / 2f;
            float ay = Screen.Height - button.FullHeight * 1.5f - 26;
            Text.DrawAnchored("+" + amount, ax, ay, 30, Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
            ui[UiSprites.ToxinPlus].Draw(ax - 6, ay, Anchor.Right | Anchor.VCenter, white);
        }

        float cx = Screen.Width - button.FullHeight / 2f - button.FullWidth / 2f;
        float cy = Screen.Height - button.FullHeight;
        bool clicked = Widgets.ImageButton(button, cx, cy, 1f, buttonDelay < 0);
        if (clicked || (buttonDelay < 0 && (Input.IsKeyPressed(KeyboardKey.Enter) || Input.IsKeyPressed(KeyboardKey.Space))))
        {
            sound(0x24);
            buttonDelay = data.K.Int(0x42d430);
            if (lastPage)
                Visible = false;
            else
                page++;
        }
    }
}
