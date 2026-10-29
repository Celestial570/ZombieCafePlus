using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The title screen (GameStateMainMenu: load FUN_001b7bb0, layout 001b87f0, update 001b8cf8): the zombie chef
/// with his tray (ipad_menuTitleImages #0) under the awning, the café's neon sign that sputters now and then
/// (NeonSign.caf), three flies that buzz from perch to perch while the chef's eye follows the first one, and
/// PLAY. The original's FRIENDS and GET MORE GAMES needed its servers and are left out; OPTIONS and HELP stand
/// on the counter where the original puts its right-hand buttons.
/// </summary>
public sealed class MainMenuScreen : GameScreen
{
    // the iPad's numbers (device 0 of the per-device tables DAT_003798f0 .. DAT_00379b90)
    static readonly Vector2[] Perches =
    [
        new(315, 450), new(750, 400), new(575, 490), new(120, 350), new(460, 180), new(80, 90), new(440, 360), new(245, 288),
    ];
    const float EyeX = 245, EyeY = 325, EyeRange = 8;
    const float PlayX = 910, PlayY = 665;
    const float ButtonGap = 40, OptionsDy = -10, HelpDy = -8;
    const float LogoX = 708, LogoY = 34;
    // ipad_menuTitleImages and ipad_menuImages
    const int TitleBackground = 0, TitleLogoDark = 1, TitleLogoLit = 2, TitleHelp = 6, TitleOptions = 7, TitleAwning = 8;
    const int MenuFly = 12, MenuFlyWingsUp = 13, MenuEye = 14, MenuPlay = 15;
    const int SoundClick = 40, SoundFly = 97, SoundNeon = 99, MusicTheme = 13;
    // the sine table DAT_00379bc4 (16.16 fixed point, by whole degrees)
    static float Sin(int degrees) => MathF.Round(MathF.Sin(degrees * MathF.PI / 180f) * 65536f) / 65536f;

    sealed class Fly
    {
        /// <summary>+0xb0: where it is on its way; +0x80: a jitter on top; +0x98: the jitter's drift.</summary>
        public Vector2 Pos, Offset, Drift;
        /// <summary>+0xc8: the swoop (degrees, 36..154); +0xd4: wing-beat count; +0xe0: time at this perch
        /// (10 ms units); +0xec: the perch it heads for.</summary>
        public int Angle, Beat, Timer, Perch;
        /// <summary>+0x138: wings up (the second frame); the image's flip: heading right.</summary>
        public bool WingsUp, Right;
        public Vector2 Drawn => Pos + Offset;
    }

    readonly GameData data;
    readonly Audio.AudioSystem? audio;
    readonly Action play;
    readonly Action? options, help;
    readonly Random rng = new();
    readonly Fly[] flies = new Fly[3];
    // +0x154: the eye's x; +0x13c / +0x140: the sign's flicker count and whether it is lit
    float eyeX = EyeX;
    int signCount;
    bool signLit = true, buzzing, left;
    float tickMs;

    /// <param name="play">PLAY: on to the cafe (or to choosing a chef first).</param>
    public MainMenuScreen(GameData data, Audio.AudioSystem? audio, Action play, Action? options = null, Action? help = null)
    {
        this.data = data;
        this.audio = audio;
        this.play = play;
        this.options = options;
        this.help = help;
        // load: the flies start at perches 2, 0 and 5, 7, 4 and 13 seconds from moving on
        int[] perch = [2, 0, 5];
        int[] timer = [700, 400, 1300];
        for (int i = 0; i < 3; i++)
            flies[i] = new Fly { Pos = Perches[perch[i]], Perch = perch[i], Timer = timer[i] };
        // ZombieCafe::loadStep: the theme starts as the menu comes up
        audio?.PlayMusic(MusicTheme, loop: true);
    }

    SubImage[] Titles => data.Sheet("ipad_menuTitleImages", "ipad_menuTitleOffsets.bin");
    SubImage[] Menu => data.Sheet("ipad_menuImages", "ipad_menuOffsets.bin");

    // the 1024 x 768 picture fitted into the window
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
        // the original steps once a frame, by the frame's milliseconds / 10 (at most 5)
        tickMs += dt * 1000;
        while (tickMs >= 1000f / 60)
        {
            tickMs -= 1000f / 60;
            Tick(1);
        }
    }

    void Tick(int step)
    {
        // the eye follows the first fly, a point at a time, within its socket
        float fx = flies[0].Drawn.X;
        if (eyeX < fx)
        {
            if (eyeX + 1 <= EyeX + EyeRange)
                eyeX += 1;
        }
        else if (eyeX - 1 >= EyeX - EyeRange)
        {
            eyeX -= 1;
        }

        // the neon sign goes dark in two bursts of flickers every 360 frames
        signCount = signCount > 359 ? 0 : signCount + 1;
        bool dark = (signCount is >= 101 and < 124 || signCount is >= 51 and < 66) && signCount % 8 > 4;
        if (dark && signLit)
        {
            signLit = false;
            audio?.Play(SoundNeon);
        }
        else if (!dark && !signLit)
        {
            signLit = true;
            audio?.Stop(SoundNeon);
        }

        bool moving = false;
        foreach (var f in flies)
            moving |= TickFly(f, step);
        if (moving && !buzzing)
        {
            audio?.Play(SoundFly);
            buzzing = true;
        }
        else if (!moving && buzzing)
        {
            audio?.Stop(SoundFly);
            buzzing = false;
        }
    }

    /// <summary>One fly's frame; true while it is flying (or rubbing its wings), which keeps the buzz going.</summary>
    bool TickFly(Fly f, int step)
    {
        // the jitter settles back a point a frame
        f.Offset.X += f.Offset.X > 0 ? -1 : f.Offset.X < 0 ? 1 : 0;
        f.Offset.Y += f.Offset.Y > 0 ? -1 : f.Offset.Y < 0 ? 1 : 0;
        bool moving = false, wingsUp = false;
        if (f.Timer < 400)
        {
            // the first four seconds at a perch: swoop over to the next one
            var target = Perches[f.Perch];
            float dx = target.X - f.Pos.X, dy = target.Y - f.Pos.Y;
            float sin = Sin(f.Angle);
            float stepX = step * Math.Clamp(0.2f * dx, -2f, 2f) * MathF.Abs(sin);
            float stepY = MathF.Abs(dx) < 0.001f ? 0 : (dy > 0 ? 1 : -1) * MathF.Abs(stepX * dy / dx) * 2 * sin;
            f.Pos += new Vector2(stepX, stepY);
            f.Right = stepX > 0;
            f.Beat = f.Beat > 5 ? 0 : f.Beat + 1;
            wingsUp = (f.Beat & 1) != 0 && MathF.Abs(dx) > 3;
            f.Angle = f.Angle + 3 > 154 ? 36 : f.Angle + 3;
            if (MathF.Abs(dx) > 0.01f || MathF.Abs(dy) > 0.01f)
            {
                f.Drift.X = MathF.Ceiling((f.Drift.X + rng.Next(3) - 1) * 0.8f);
                f.Drift.Y = MathF.Ceiling((f.Drift.Y + rng.Next(3) - 1) * 0.8f);
                f.Offset += f.Drift;
                // the jitter never carries it past where it is going
                int limitX = Math.Abs((int)dx), limitY = Math.Abs((int)dy);
                if (Math.Abs((int)f.Offset.X) > limitX)
                {
                    f.Offset.X = f.Offset.X <= 0 ? -limitX : limitX;
                    f.Drift.X = 0;
                }
                if (Math.Abs((int)f.Offset.Y) > limitY)
                {
                    f.Offset.Y = f.Offset.Y <= 0 ? -limitY : limitY;
                    f.Drift.Y = 0;
                }
                moving = true;
            }
            else
            {
                f.Drift = Vector2.Zero;
            }
        }
        else if (f.Timer is >= 751 and < 800 || f.Timer is >= 1621 and < 1700)
        {
            // sitting: now and then it rubs its wings
            f.Beat = f.Beat > 1 ? 0 : f.Beat + 1;
            wingsUp = f.Beat < 1;
            moving = true;
        }
        // the wings-up frame is wider: a resting fly shifts so its body stays put
        if (wingsUp != f.WingsUp)
        {
            f.WingsUp = wingsUp;
            if (f.Timer > 400)
                f.Pos.X += wingsUp ? 2 : -2;
        }
        f.Timer += step;
        if (f.Timer > 1400)
        {
            f.Timer = 0;
            f.Perch = f.Perch > 6 ? 0 : f.Perch + 1;
        }
        return moving;
    }

    public override void Draw()
    {
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        var titles = Titles;
        var menu = Menu;
        if (titles.Length <= TitleAwning || menu.Length <= MenuPlay)
            return;

        // the picture, then the counter buttons, the eye, the sign and the awning (the original's child order)
        titles[TitleBackground].DrawAt(origin.X, origin.Y, Screen.BaseWidth * scale, Screen.BaseHeight * scale, Color.White);
        if (options != null && CounterButton(titles[TitleOptions], Screen.BaseWidth - ButtonGap, OptionsDy))
            Go(options);
        if (help != null)
        {
            float right = Screen.BaseWidth - ButtonGap - titles[TitleOptions].FullWidth - ButtonGap;
            if (CounterButton(titles[TitleHelp], options != null ? right : Screen.BaseWidth - ButtonGap, HelpDy))
                Go(help);
        }
        var eye = P(eyeX, EyeY);
        menu[MenuEye].Draw(eye.X, eye.Y, Anchor.Center, Color.White, Flip.None, scale, scale);
        var logo = P(LogoX, LogoY);
        titles[signLit ? TitleLogoLit : TitleLogoDark].Draw(logo.X, logo.Y, Anchor.Top | Anchor.HCenter, Color.White, Flip.None, scale, scale);
        // the awning runs the width of the window
        var awning = titles[TitleAwning];
        float aw = awning.FullWidth * scale;
        for (float x = origin.X - MathF.Ceiling(origin.X / aw) * aw; x < Screen.Width; x += aw)
            awning.Draw(x, origin.Y, Anchor.Top | Anchor.Left, Color.White, Flip.None, scale, scale);

        var playImg = menu[MenuPlay];
        var playAt = P(PlayX - playImg.FullWidth / 2f, PlayY - playImg.FullHeight / 2f);
        if ((Widgets.ImageButton(playImg, playAt.X, playAt.Y, scale) || Input.IsKeyPressed(KeyboardKey.Enter)) && !left)
            Go(play);

        foreach (var f in flies)
        {
            var at = P(f.Drawn.X, f.Drawn.Y);
            menu[f.WingsUp ? MenuFlyWingsUp : MenuFly].Draw(at.X, at.Y, Anchor.Top | Anchor.HCenter, Color.White,
                f.Right ? Flip.Horizontal : Flip.None, scale, scale);
        }
    }

    /// <summary>A word on the counter, anchored by its bottom-right corner (layout: right to left from the edge).</summary>
    bool CounterButton(SubImage img, float right, float dy)
    {
        float bottom = Screen.BaseHeight - (img.FullHeight + dy);
        var c = P(right - img.FullWidth / 2f, bottom - img.FullHeight / 2f);
        return Widgets.ImageButton(img, c.X, c.Y, scale) && !left;
    }

    void Go(Action next)
    {
        left = true;
        audio?.Play(SoundClick);
        next();
    }

    public override void Leave()
    {
        audio?.Stop(SoundFly);
        audio?.Stop(SoundNeon);
    }

    /// <summary>Test scripts: "play", "options", "help" in window pixels.</summary>
    Vector2? ScriptTarget(string name)
    {
        var menu = Menu;
        var titles = Titles;
        if (menu.Length <= MenuPlay || titles.Length <= TitleAwning)
            return null;
        Vector2 Counter(SubImage img, float right, float dy) =>
            P(right - img.FullWidth / 2f, Screen.BaseHeight - (img.FullHeight + dy) - img.FullHeight / 2f);
        Vector2? at = name switch
        {
            "play" => P(PlayX - menu[MenuPlay].FullWidth / 2f, PlayY - menu[MenuPlay].FullHeight / 2f),
            "options" => Counter(titles[TitleOptions], Screen.BaseWidth - ButtonGap, OptionsDy),
            "help" => Counter(titles[TitleHelp], options != null
                ? Screen.BaseWidth - ButtonGap - titles[TitleOptions].FullWidth - ButtonGap : Screen.BaseWidth - ButtonGap, HelpDy),
            _ => null,
        };
        return at is { } p ? Screen.ToPixelsPoint(p) : null;
    }
}
