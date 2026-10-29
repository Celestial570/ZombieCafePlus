using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// A raid on a rival restaurant (GameStateRaid: input FUN_00157018, the HUD's buttons FUN_00152f88, the raiders'
/// command bar, the tips FUN_00154cf0, the results RaidRewardScreen FUN_00133738). Tap a zombie to pick it, then
/// ATTACK to send it in, or tap a rival to set it on them, the counter to steal its food, or a spot in the room to
/// go there. The white flag retreats; ENERGIZE spends a vial of toxin on a tired raider. The tombstones standing in
/// the cafe give buttons down the right edge (strength, energy, the golden revive, the yeti; FUN_00152f88), and with
/// any of those "Send all zombies" in the bottom corner. From level 11 the Amptron's button stands by the flag: with
/// all five robot pets along they combine into it for a while (three times a day), and its command bar has SPECIAL.
/// From level 13 the Big Bad Wolf's follows: Little Red, the Huntsman or Granny Wolf inside turns into the wolf for a
/// while (three times a raid).
/// </summary>
public sealed class RaidScreen : GameScreen
{
    // ipad_ingameUi
    const int UiLogo = 7, UiFlag = 13, UiGreenBar = 10, UiAmptron = 210, UiSpecial = 211, UiWolf = 213, UiTransform = 214;
    // the code's own words: the "/" of the summons left out of three on the HUD (FUN_001110e8), the "/3" of the
    // Amptron's specials (FUN_000ec11c)
    const uint TextOutOf = 0x33f19b, TextOfThree = 0x3417d5;
    // strings.bin
    const int TextQuitAsk = 448, TextRetreatAsk = 449, TextYes = 461, TextNo = 462, TextOk = 362, TextLevel = 79,
        TextUseToxin = 319, TextEnergizeAsk = 317, TextEnergizeAsk2 = 318, TextEnergizeFull = 447,
        TextSummary = 64, TextWon = 3, TextLost = 1, TextRanAway = 2, TextDefeated = 483, TextStoleWon = 482,
        TextNoMatch = 479, TextCouldntStand = 481, TextStoleRan = 480, TextSendAll = 640;
    static readonly Color Chalk = Colors.FromArgb(0xffa7ce4fu);   // K 0x42d4ec

    readonly GameData data;
    readonly RaidController raid;
    readonly Audio.AudioSystem? audio;
    readonly Action<RaidController> done;
    readonly Action? quit;
    readonly ChefMessage chefMessage;
    readonly TutorialArrow arrow;
    Cafe cafe => raid.Cafe;

    Vector2 camera;
    float zoom = 0.75f;
    Vector2 lastMouse, pressMouse;
    bool dragging, pressed;
    float msCarry;
    (int, int)? laidOutFor;
    /// <summary>FUN_00111914: the rival's star rating on the HUD, made up for the raid.</summary>
    readonly float stars;
    // FUN_0015b068: the rival a raider was just set on flashes for half a second (K 0x42ce88)
    CharacterInstance? marked;
    int markMs;
    (string Text, string Yes, string No, Action OnYes)? question;
    string? note;
    Rectangle? attackRect, energizeRect, infoRect, flagRect, logoRect, okRect, sendAllRect, amptronRect, specialRect, wolfRect,
        transformRect;
    readonly Rectangle?[] tombstoneRects = new Rectangle?[4];
    CharacterInstance? bio;
    bool left;
    /// <summary>The raid runs this many times as fast (the cheat menu's speed; ZC_SPEED for tests).</summary>
    static float speed => Cheats.Speed;
    public override bool ScalesHud => true;

    public RaidScreen(GameData data, RaidController raid, Audio.AudioSystem? audio, Action<RaidController> done, Action? quit)
    {
        this.data = data;
        this.raid = raid;
        this.audio = audio;
        this.done = done;
        this.quit = quit;
        chefMessage = new ChefMessage(data, id => audio?.Play(id));
        arrow = new TutorialArrow(data);
        var hints = raid.Hints;
        hints.Check = RaidHint;
        hints.Show = text => chefMessage.Show(text);
        hints.Busy = () => chefMessage.Visible || question != null || note != null;
        stars = raid.Cafe.Rng.Next(101) * 0.01f;
        if (audio != null)
        {
            raid.SoundRequested += audio.Play;
            raid.MusicCue += audio.PlayMusicStinger;
            raid.Music += id => audio.PlayMusic(id, loop: true);
        }
        raid.Marked += c =>
        {
            marked = c;
            markMs = data.K.Int(0x42ce88);
        };
    }

    /// <summary>The room and the raiders waiting in the street (GameStateRaid::load starts with them in view).</summary>
    Rectangle ViewBounds()
    {
        var r = cafe.RoomBounds();
        float x0 = r.X, y0 = r.Y, x1 = r.X + r.Width, y1 = r.Y + r.Height;
        foreach (var z in raid.Raiders)
        {
            x0 = MathF.Min(x0, z.X - 60);
            x1 = MathF.Max(x1, z.X + 60);
            y0 = MathF.Min(y0, z.Y - 170);
            y1 = MathF.Max(y1, z.Y + 20);
        }
        return new Rectangle(x0, y0, x1 - x0, y1 - y0);
    }

    public override void Layout()
    {
        Input.TargetToPixels = ScriptTarget;
        Input.CellToPixels = (du, dv) =>
        {
            int cell = cafe.IndexOf(cafe.OriginU + du, cafe.OriginV + dv);
            return cell < 0 ? null : Vector2.Transform(cafe.CellCenter(cell), WorldToWindow);
        };
        var size = (Screen.PixelWidth, Screen.PixelHeight);
        if (laidOutFor == size)
            return;
        laidOutFor = size;
        // the room and the street where the raiders wait, clear of the HUD and the command bar
        var view = ViewBounds();
        float fit = MathF.Min(Screen.PixelWidth / view.Width, Screen.PixelHeight * 0.78f / view.Height) * 0.95f;
        zoom = Math.Clamp(fit, 0.2f, 1.5f);
        camera = new Vector2(view.X + view.Width / 2, view.Y + view.Height / 2 - 20 / zoom);
    }

    Matrix3x2 WorldToWindow =>
        Matrix3x2.CreateTranslation(-camera) * Matrix3x2.CreateScale(zoom) *
        Matrix3x2.CreateTranslation(Screen.PixelWidth / 2f, Screen.PixelHeight / 2f);

    Vector2 MouseWorld()
    {
        Matrix3x2.Invert(WorldToWindow, out var inv);
        return Vector2.Transform(Input.MousePosition, inv);
    }

    Vector2 WorldToScreen(Vector2 world) => Screen.FromPixels(Vector2.Transform(world, WorldToWindow));

    // ---------------------------------------------------------------- tips (FUN_00154cf0)

    bool RaidHint(int hint)
    {
        switch (hint)
        {
            case < 0x18:
                return hint != 0xf;
            case 0x18 or 0x1a or 0x1e or 0x20:
                return false;
            case 0x26: // "touch your zombies and then the attack button": the first raider
                if (raid.Raiders.Count == 0)
                    return false;
                arrow.PointAt(raid.Raiders[0]);
                return true;
            case 0x27: // a raider picked: its ATTACK button
                arrow.PointAt(() => attackRect, flipped: false);
                return true;
            case 0x28:
                arrow.Hide();
                return true;
            case 0x2b: // a raider low on energy: everything waits, a vial on the house, point at ENERGIZE
                raid.Paused = true;
                raid.Player.Toxin += 1;
                if (raid.Tired is { } tired)
                    raid.Selected = tired;
                arrow.PointAt(() => energizeRect);
                return true;
            case 0x3a:
                raid.Paused = false;
                arrow.Hide();
                return true;
            case 0x56 or 0x58: // the meat locker's and the ZombiePedia's tips wait for the cafe
                return false;
            default:
                if (CafeScreen.IsUnported(hint))
                {
                    raid.Hints.MarkShown(hint);
                    return false;
                }
                return true;
        }
    }

    // ---------------------------------------------------------------- update

    /// <summary>How long the raid has taken (the cafe catches up on it afterwards).</summary>
    public long ElapsedMs { get; private set; }

    public override void Update(float dt)
    {
        int frameMs = Math.Max(1, (int)MathF.Round(dt * 1000f));
        ElapsedMs += frameMs;
        var hints = raid.Hints;
        hints.Update(frameMs, data);
        chefMessage.Update(frameMs);
        arrow.Update(frameMs);
        // ZombieCafe::update under every screen: the rivals' clocks
        raid.Player.Neighbours.Update(data, raid.Player, frameMs, cafe.Rng);
        if (markMs > 0 && (markMs -= frameMs) <= 0)
            marked = null;
        bool modal = chefMessage.Visible || question != null || note != null || bio != null || raid.Ended;
        if (!modal)
        {
            msCarry += dt * 1000f * speed;
            while (msCarry >= 1f)
            {
                int step = (int)MathF.Min(msCarry, 50f);
                raid.Update(step);
                msCarry -= step;
                if (raid.Ended)
                    break;
            }
        }
        if (raid.Selected is { } sel && sel.IsKnockedOut)
            raid.Selected = null;
        HandleInput(dt, modal);
    }

    void HandleInput(float dt, bool modal)
    {
        var mouse = Input.MousePosition;
        if (modal)
        {
            pressed = false;
            return;
        }
        if (Input.IsKeyPressed(KeyboardKey.Escape))
            raid.Selected = null;
        if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right))
        {
            pressed = Input.IsMouseButtonPressed(MouseButton.Left);
            dragging = false;
            lastMouse = pressMouse = mouse;
        }
        if (Input.IsMouseButtonDown(MouseButton.Left) || Input.IsMouseButtonDown(MouseButton.Right))
        {
            if (Vector2.Distance(mouse, pressMouse) > 6)
                dragging = true;
            if (dragging)
                camera -= (mouse - lastMouse) / zoom;
            lastMouse = mouse;
        }
        bool click = Input.IsMouseButtonReleased(MouseButton.Left) && pressed && !dragging && !Widgets.MouseOverUi;
        if (click)
            Tap(MouseWorld());
        float wheel = Widgets.MouseOverUi ? 0 : Input.GetMouseWheelMove();
        if (wheel != 0)
        {
            Matrix3x2.Invert(WorldToWindow, out var inv);
            var before = Vector2.Transform(mouse, inv);
            zoom = Math.Clamp(zoom * MathF.Pow(1.15f, wheel), 0.2f, 1.5f);
            Matrix3x2.Invert(WorldToWindow, out inv);
            camera += before - Vector2.Transform(mouse, inv);
        }
    }

    static bool HitCharacter(CharacterInstance c, Vector2 p) =>
        p.X >= c.X - 45 && p.X <= c.X + 45 && p.Y >= c.Y - 160 && p.Y <= c.Y + 5;

    static bool HitFurniture(Furniture f, Vector2 p) => f.HitTest(p.X, p.Y);

    /// <summary>FUN_00157018: a tap in the raided cafe.</summary>
    void Tap(Vector2 p)
    {
        var objects = cafe.Objects;
        // the tutorial arrow's target takes the tap first (FUN_001504f0)
        CafeObject? hit = arrow.Target is CharacterInstance at && HitCharacter(at, p) ? at : null;
        for (int i = objects.Count - 1; i >= 0 && hit == null; i--)
            if (objects[i] is CharacterInstance c && c.Visible && HitCharacter(c, p) && !c.IsKnockedOut)
                hit = c;
        for (int i = objects.Count - 1; i >= 0 && hit == null; i--)
            if (objects[i] is Furniture f && f.Visible && f.Def.Category == FurnitureCategory.Counter && HitFurniture(f, p))
                hit = f;
        // a raider or the Amptron: pick it (or put it down again)
        if (hit is CharacterInstance r && r.Active && (raid.Raiders.Contains(r) || r == raid.Amptron))
        {
            if (raid.Selected == r)
                raid.Selected = null;
            else
            {
                raid.Selected = r;
                raid.Hint(0x10, false);
                r.Groan();
            }
            return;
        }
        if (raid.Selected is not { } sel)
            return;
        var (u, v) = cafe.ScreenToDiamond(p.X, p.Y);
        raid.Order(sel, hit, cafe.IndexOf(u, v));
    }

    // ---------------------------------------------------------------- draw

    public override void Draw()
    {
        Raylib.ClearBackground(Color.Black);
        Gfx.Draw.Reset(WorldToWindow);
        cafe.Draw();
        DrawMarks();
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        attackRect = energizeRect = infoRect = specialRect = transformRect = null;
        bool modal = chefMessage.Visible || question != null || note != null || bio != null || raid.Ended;
        Widgets.Blocked = modal;
        DrawHud();
        if (raid.Selected is { } sel && !raid.Ended)
            DrawCommandBar(sel);
        Widgets.Blocked = false;
        arrow.Draw(WorldToScreen);
        chefMessage.Draw();
        if (bio != null)
            DrawBio();
        if (raid.Ended)
            DrawResults();
        DrawBoxes();
    }

    /// <summary>The picked raider's ring and the rival it was just set on.</summary>
    void DrawMarks()
    {
        var ring = data.IngameUi[UiSprites.Ring];
        if (raid.Selected is { } s)
            ring.Draw(s.X, s.Y, Anchor.Center, new Color(120, 255, 120, 255), Flip.None, 1.6f, 0.9f);
        if (marked is { } m && cafe.Objects.Contains(m))
            ring.Draw(m.X, m.Y, Anchor.Center, new Color(255, 90, 90, 255), Flip.None, 1.6f, 0.9f);
    }

    /// <summary>The top of the screen: the logo and the white flag, the rival's name and stars, cash and toxin.</summary>
    void DrawHud()
    {
        var ui = data.IngameUi;
        var p = raid.Player;
        var awning = ui[UiSprites.Awning];
        for (float x = Screen.Width; x > -awning.FullWidth; x -= awning.FullWidth)
            awning.Draw(x, 0, Anchor.Top | Anchor.Right, Color.White);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, awning.FullHeight * 0.62f), new Color(0, 0, 0, 90));
        float y = awning.FullHeight * 0.31f;

        // HUD buttons 0 (the logo, back to the title) and 21 (retreat)
        var logo = ui[UiLogo];
        float bx = 12 + logo.FullWidth / 2f;
        logoRect = quit != null ? new Rectangle(bx - logo.FullWidth / 2f, y - logo.FullHeight / 2f, logo.FullWidth, logo.FullHeight) : null;
        if (quit != null && Widgets.ImageButton(logo, bx, y))
        {
            audio?.Play(36);
            question = (data.Strings[TextQuitAsk], data.Strings[TextYes], data.Strings[TextNo], () =>
            {
                left = true;
                quit();
            });
        }
        var flag = ui[UiFlag];
        float fx = bx + logo.FullWidth / 2f + 12 + flag.FullWidth / 2f;
        flagRect = new Rectangle(fx - flag.FullWidth / 2f, y - flag.FullHeight / 2f, flag.FullWidth, flag.FullHeight);
        if (Widgets.ImageButton(flag, fx, y, 1f, !raid.Retreating) && raid.Hints.IsShown(0x26))
        {
            audio?.Play(36);
            question = (data.Strings[TextRetreatAsk], data.Strings[TextYes], data.Strings[TextNo], raid.Retreat);
        }

        // HUD button 22 from level 11 (FUN_001108ec): the Amptron - lit when all five robot pets came along, with its
        // summons left out of three under it (FUN_001110e8)
        float nx = fx + flag.FullWidth / 2f + 24;
        amptronRect = null;
        if (p.Level > 10)
        {
            var amp = ui[UiAmptron];
            float ax = fx + flag.FullWidth / 2f + 12 + amp.FullWidth / 2f;
            amptronRect = new Rectangle(ax - amp.FullWidth / 2f, y - amp.FullHeight / 2f, amp.FullWidth, amp.FullHeight);
            if (Widgets.ImageButton(amp, ax, y, 1f, raid.AmptronReady))
                AmptronPressed();
            if (raid.AmptronReady)
                Text.DrawAnchored(p.AmptronUses + data.Exe.Get(TextOutOf) + "3", ax, y + 20, Text.GameFont[0], Anchor.Center,
                    Color.White, Color.Black, 2);
            nx = ax + amp.FullWidth / 2f + 24;
        }
        // HUD button 23 from level 13: the Big Bad Wolf - lit when a fairy-tale zombie came along, with its
        // transformations left out of three under it (FUN_001113d4)
        wolfRect = null;
        if (p.Level > 12)
        {
            var wolf = ui[UiWolf];
            float wx = nx - 12 + wolf.FullWidth / 2f;
            wolfRect = new Rectangle(wx - wolf.FullWidth / 2f, y - wolf.FullHeight / 2f, wolf.FullWidth, wolf.FullHeight);
            if (Widgets.ImageButton(wolf, wx, y, 1f, raid.WolfReady))
                WolfPressed();
            if (raid.WolfReady)
                Text.DrawAnchored(raid.WolfUses + data.Exe.Get(TextOutOf) + "3", wx, y + 20, Text.GameFont[0], Anchor.Center,
                    Color.White, Color.Black, 2);
            nx = wx + wolf.FullWidth / 2f + 24;
        }

        // the rival: its name, level and a star rating
        Text.DrawAnchored(raid.Name, nx, y - 10, Text.GameFont[3], Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        Text.DrawAnchored(data.Strings[TextLevel] + raid.Level, nx, y + 16, Text.GameFont[1], Anchor.Left | Anchor.VCenter, Widgets.Gold, Color.Black, 2);
        float sx = nx + MathF.Max(Text.Measure(raid.Name, Text.GameFont[3]).W, 120) + 30;
        for (int i = 0; i < 5; i++)
        {
            ui[173].Draw(sx + i * 24, y, Anchor.Center, Color.White, Flip.None, 1.1f, 1.1f);
            float fill = Math.Clamp(stars * 5f - i, 0f, 1f);
            if (fill > 0)
                ui[172].Draw(sx + i * 24, y, Anchor.Center, Color.White, Flip.None, 1.1f * MathF.Sqrt(fill), 1.1f * MathF.Sqrt(fill));
        }

        DrawTombstones();

        // the player's cash and toxin
        float cx = Screen.Width - 300;
        Widgets.Sprite(ui[UiSprites.Cash], cx, y, 0.8f);
        Text.DrawAnchored(p.Money.ToString("N0"), cx + 20, y, 26, Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        float tx = cx + 170;
        Widgets.Sprite(ui[UiSprites.VialMedium], tx, y, 0.7f);
        Text.DrawAnchored(p.Toxin.ToString(), tx + 18, y, 26, Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
    }

    /// <summary>FUN_00152f88 button 22: why the Amptron cannot come, or whether to summon it.</summary>
    void AmptronPressed()
    {
        audio?.Play(36);
        if (raid.CannotSummonAmptron() is { } why)
            note = why;
        else
            question = (raid.AmptronQuestion, data.Strings[TextOk], data.Strings[316], raid.SummonAmptron);
    }

    /// <summary>
    /// FUN_0015b574 (button 23) / FUN_0015b060 (TRANSFORM, command bar button 0): why no zombie (or not this one)
    /// can turn into the wolf, or whether the picked one should.
    /// </summary>
    void WolfPressed(CharacterInstance? pick = null)
    {
        audio?.Play(36);
        if ((pick == null ? raid.WolfCandidate() : raid.WolfCandidate(pick)) is { } why)
            note = why;
        else
            question = (raid.WolfQuestion, data.Strings[TextOk], data.Strings[316], raid.TransformSelected);
    }

    /// <summary>
    /// The Amptron picked (FUN_000ec11c, type 6): its name and energy, and SPECIAL (command bar button 11) with its
    /// specials left out of three - not to be pressed with none left.
    /// </summary>
    void DrawAmptronBar(CharacterInstance a)
    {
        var c = a.Character;
        float bw = 460, bh = 96;
        var r = new Rectangle((Screen.Width - bw) / 2, Screen.Height - bh - 40, bw, bh);
        Widgets.Panel(r);
        Widgets.Label(c.Name, r.X + 16, r.Y + 10, 26, Anchor.Top | Anchor.Left, Widgets.Gold);
        float max = MathF.Max(1f, c.MaxEnergy(data));
        Widgets.Label(data.Strings[512].Trim(), r.X + 16, r.Y + 66, 16);
        Widgets.Bar(new Rectangle(r.X + 96, r.Y + 68, 160, 16), c.Energy / max, new Color(120, 220, 90, 255));
        var special = data.IngameUi[UiSpecial];
        float s = 0.8f, sx = r.X + bw - 70, sy = r.Y + bh / 2;
        specialRect = new Rectangle(sx - special.FullWidth * s / 2, sy - special.FullHeight * s / 2, special.FullWidth * s, special.FullHeight * s);
        if (Widgets.ImageButton(special, sx, sy, s, a.SpecialsLeft > 0))
            raid.AmptronSpecial();
        Text.DrawAnchored(a.SpecialsLeft + data.Exe.Get(TextOfThree), sx + special.FullWidth * s / 2, sy + special.FullHeight * s / 2,
            Text.GameFont[2], Anchor.Center, Color.White, Color.Black, 2);
    }

    /// <summary>
    /// The picked raider: who it is, its energy, ATTACK, ENERGIZE and INFO (command bar buttons 1, 6, 8), and on
    /// Little Red, the Huntsman or Granny Wolf TRANSFORM (button 0, ui 214; FUN_00157018).
    /// </summary>
    void DrawCommandBar(CharacterInstance w)
    {
        if (w.IsAmptron)
        {
            DrawAmptronBar(w);
            return;
        }
        var c = w.Character;
        bool fairy = RaidController.IsFairyTale(w);
        float bw = fairy ? 660 : 560, bh = 96;
        var r = new Rectangle((Screen.Width - bw) / 2, Screen.Height - bh - 40, bw, bh);
        Widgets.Panel(r);
        Widgets.Label(c.Name, r.X + 16, r.Y + 10, 26, Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label($"Level {c.Level + 1}", r.X + 16, r.Y + 42, 18);
        float max = MathF.Max(1f, c.MaxEnergy(data));
        Widgets.Label(data.Strings[512].Trim(), r.X + 16, r.Y + 66, 16);
        Widgets.Bar(new Rectangle(r.X + 96, r.Y + 68, 160, 16), c.Energy / max, new Color(120, 220, 90, 255));
        var ui = data.IngameUi;
        float s = 0.8f, by = r.Y + bh / 2;
        Rectangle Spot(SubImage img, float x) => new(x - img.FullWidth * s / 2, by - img.FullHeight * s / 2, img.FullWidth * s, img.FullHeight * s);
        var attack = ui[UiSprites.Attack];
        float ax = r.X + bw - 60;
        attackRect = Spot(attack, ax);
        if (Widgets.ImageButton(attack, ax, by, s, !raid.Retreating))
        {
            audio?.Play(40);
            raid.Attack(w);
        }
        var energize = ui[UiSprites.Energize];
        float ex = ax - 100;
        energizeRect = Spot(energize, ex);
        if (Widgets.ImageButton(energize, ex, by, s, raid.CannotEnergize(w) is null or { Length: > 0 }))
        {
            if (raid.CannotEnergize(w) is { Length: > 0 } why)
                note = why;
            else
            {
                string ask = data.Strings[TextEnergizeAsk] + "1" + data.Strings[TextEnergizeAsk2];
                if (c.Energy / max > data.K.Float(0x42cf00))
                    ask = data.Strings[TextEnergizeFull] + (int)(data.K.Float(0x42cf00) * 100) + "%. " + ask;
                question = (ask, data.Strings[TextUseToxin], data.Strings[TextNo], () => raid.Energize(w));
            }
        }
        var info = ui[UiSprites.Info];
        float ix = ex - 100;
        infoRect = Spot(info, ix);
        if (Widgets.ImageButton(info, ix, by, s))
        {
            audio?.Play(36);
            bio = w;
        }
        if (fairy)
        {
            var transform = ui[UiTransform];
            float tx = ix - 100;
            transformRect = Spot(transform, tx);
            if (Widgets.ImageButton(transform, tx, by, s))
                WolfPressed(w);
        }
    }

    /// <summary>INFO in a raid: the zombie's name, level, stats and record (a plain panel here).</summary>
    void DrawBio()
    {
        var c = bio!.Character;
        float w = 460, h = 260;
        var r = new Rectangle((Screen.Width - w) / 2, (Screen.Height - h) / 2, w, h);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 120));
        Widgets.Panel(r);
        Text.Draw(c.Name, r.X + 24, r.Y + 20, Text.GameFont[4], Chalk);
        Text.Draw(c.Def.Name + "   Level " + (c.Level + 1), r.X + 24, r.Y + 62, Text.GameFont[2], Color.White);
        string[] lines =
        [
            data.Strings[510] + c.Speed, data.Strings[511] + c.Strength,
            data.Strings[61] + " " + c.Raids, data.Strings[62] + " " + c.Kills, data.Strings[63] + " " + c.Deaths,
        ];
        for (int i = 0; i < lines.Length; i++)
            Text.Draw(lines[i], r.X + 24, r.Y + 100 + i * 26, Text.GameFont[1], Color.White);
        if (Widgets.Button(new Rectangle(r.X + w - 130, r.Y + h - 56, 110, 40), data.Strings[TextOk], 22) || Input.IsKeyPressed(KeyboardKey.Escape))
        {
            audio?.Play(36);
            bio = null;
        }
    }

    /// <summary>
    /// RaidRewardScreen (FUN_00133738): "Raid Summary", how it went, and either the rival beaten (its picture and
    /// name) or the dish carried off; the toxin and money the raid brought; OK goes home.
    /// </summary>
    void DrawResults()
    {
        var outcome = raid.Outcome ?? RaidOutcome.Lost;
        var dialog = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        var board = dialog.Length > 2 ? dialog[2] : null;
        float bw = board?.FullWidth ?? 485, bh = board?.FullHeight ?? 327;
        float scale = 1.25f;
        var r = new Rectangle((Screen.Width - bw * scale) / 2, (Screen.Height - bh * scale) / 2, bw * scale, bh * scale);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 140));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        if (board != null)
            board.DrawAt(r.X, r.Y, r.Width, r.Height, Color.White);
        else
            Widgets.Panel(r);
        float top = r.Y + 24;
        Text.DrawAnchored(data.Strings[TextSummary], r.X + r.Width / 2, top, Text.GameFont[5], Anchor.Top | Anchor.HCenter, Color.White);
        string title = data.Strings[outcome switch { RaidOutcome.Won => TextWon, RaidOutcome.RanAway => TextRanAway, _ => TextLost }];
        Text.DrawAnchored(title, r.X + r.Width / 2, top + 48, Text.GameFont[4], Anchor.Top | Anchor.HCenter, Chalk);

        // the rival beaten, or what was stolen
        bool showLoot = raid.Loot.Count > 0 && outcome != RaidOutcome.Lost;
        string line = outcome switch
        {
            RaidOutcome.Won => data.Strings[showLoot ? TextStoleWon : TextDefeated],
            RaidOutcome.RanAway => data.Strings[showLoot ? TextStoleRan : TextNoMatch],
            _ => data.Strings[TextCouldntStand],
        };
        Text.DrawAnchored(line, r.X + r.Width / 2, top + 92, Text.GameFont[2], Anchor.Top | Anchor.HCenter, Color.White);
        var frame = data.IngameUi[UiSprites.PaperLarge];
        float fs = 0.6f;
        var fr = new Rectangle(r.X + 40, top + 124, frame.FullWidth * fs, frame.FullHeight * fs);
        frame.Draw(fr.X, fr.Y, Anchor.Top | Anchor.Left, Color.White, Flip.None, fs, fs);
        data.IngameUi[UiSprites.Pin].Draw(fr.X + fr.Width / 2, fr.Y, Anchor.Top | Anchor.HCenter, Color.White, Flip.None, fs, fs);
        SubImage? pic;
        string name;
        if (showLoot)
        {
            var dish = raid.Loot[0];
            pic = data.FoodImage(dish.Food);
            name = dish.DisplayName(data) + (raid.Loot.Count > 1 ? $" (+{raid.Loot.Count - 1})" : "");
        }
        else
        {
            var pictures = data.Sheet("cafeUiImages", "cafeUiOffsets.bin");
            int pi = raid.Rival.Picture;
            pic = pi >= 0 && pi < pictures.Length ? pictures[pi] : null;
            name = raid.Name;
        }
        if (pic != null)
        {
            float fit = MathF.Min(fr.Width * 0.75f / pic.FullWidth, fr.Height * 0.75f / pic.FullHeight);
            pic.Draw(fr.X + fr.Width / 2, fr.Y + fr.Height / 2, Anchor.Center, Color.White, Flip.None, fit, fit);
        }
        float tx = fr.X + fr.Width + 30;
        Text.DrawAnchored(name, tx, fr.Y + 20, Text.GameFont[3], Anchor.Top | Anchor.Left, Color.White);
        // toxin and money brought
        var ui = data.IngameUi;
        float iy = fr.Y + 80;
        ui[UiSprites.VialSmall].Draw(tx, iy, Anchor.Left | Anchor.VCenter, Color.White);
        Text.DrawAnchored((outcome == RaidOutcome.Lost ? 0 : raid.Toxin).ToString(), tx + 40, iy, Text.GameFont[3], Anchor.Left | Anchor.VCenter, Color.White);
        ui[UiSprites.CashSmall].Draw(tx, iy + 46, Anchor.Left | Anchor.VCenter, Color.White);
        Text.DrawAnchored(raid.Money.ToString(), tx + 40, iy + 46, Text.GameFont[3], Anchor.Left | Anchor.VCenter, Color.White);

        var ok = ui[UiGreenBar];
        var at = new Vector2(r.X + r.Width / 2, r.Y + r.Height - 20 - ok.FullHeight / 2f);
        okRect = new Rectangle(at.X - ok.FullWidth / 2f, at.Y - ok.FullHeight / 2f, ok.FullWidth, ok.FullHeight);
        bool pressedOk = Widgets.ImageButton(ok, at.X, at.Y) && question == null;
        Text.DrawAnchored(data.Strings[TextOk], at.X, at.Y, Text.GameFont[2], Anchor.Center, Color.White);
        if ((pressedOk || Input.IsKeyPressed(KeyboardKey.Enter)) && !left)
        {
            left = true;
            audio?.Play(36);
            done(raid);
        }
    }

    void DrawBoxes()
    {
        if (question is { } q)
        {
            int answer = Widgets.QuestionBox(q.Text, q.Yes, q.No);
            if (answer != 0)
            {
                audio?.Play(36);
                question = null;
                if (answer == 1)
                    q.OnYes();
            }
        }
        else if (note != null && Widgets.MessageBox("", [note], data.Strings[TextOk]))
        {
            audio?.Play(36);
            note = null;
        }
    }

    public override void Leave()
    {
        // the next screen may have taken the hints over already (it is made before this one leaves)
        var hints = raid.Hints;
        if (hints.Check != RaidHint)
            return;
        hints.Check = null;
        hints.Show = null;
        hints.Busy = null;
    }

    // ---------------------------------------------------------------- test scripts

    // strings.bin: the tombstones' questions, used up, and "Send all"
    const int TextEnergyAsk = 636, TextStrengthAsk = 637, TextUsedUp = 638, TextSendAllAsk = 639,
        TextReviveAsk = 659;
    // ipad_ingameUi: the red, green and golden tombstones (HUD buttons 16, 17, 18)
    static readonly int[] TombstoneSprites = [178, 177, 179, 209];

    /// <summary>
    /// FUN_001108ec / FUN_0010d3e0: the tombstones in the cafe down the right edge (x 985 of 1024, from 150, 120
    /// apart), each with its uses left over its uses a day; "Send all zombies" in the bottom right corner when any shows.
    /// </summary>
    void DrawTombstones()
    {
        var ui = data.IngameUi;
        var b = raid.Player.Boosts;
        // strength (slots 0, 2), energy (1, 3), golden (4), the yeti (6; HUD 19)
        int[][] groups = [[Boosts.Strength, Boosts.Strength2], [Boosts.Health, Boosts.Health2], [Boosts.Golden], [Boosts.Yeti]];
        float x = Screen.Width - (1024 - 985), y = 150;
        bool any = false;
        for (int g = 0; g < groups.Length; g++)
        {
            tombstoneRects[g] = null;
            if (!groups[g].Any(b.Placed))
                continue;
            any = true;
            var img = ui[TombstoneSprites[g]];
            tombstoneRects[g] = new Rectangle(x - img.FullWidth / 2f, y - img.FullHeight / 2f, img.FullWidth, img.FullHeight);
            int left = groups[g].Where(b.Placed).Sum(s => b.Charges[s]);
            int max = groups[g].Where(b.Placed).Sum(s => Boosts.Table[s].Charges);
            bool pressed = Widgets.ImageButton(img, x, y, 1f, !raid.Ended);
            Text.DrawAnchored($"{left}/{max}", x, y + img.FullHeight * 0.25f, Text.GameFont[0], Anchor.Center, Color.White, Color.Black, 2);
            if (pressed)
                TombstonePressed(g, groups[g]);
            y += 120;
        }
        sendAllRect = null;
        if (!any)
            return;
        var bar = ui[UiSprites.GreenBarWide];
        var at = new Vector2(Screen.Width - bar.FullWidth / 2f - 10, Screen.Height - bar.FullHeight / 2f - 10);
        sendAllRect = new Rectangle(at.X - bar.FullWidth / 2f, at.Y - bar.FullHeight / 2f, bar.FullWidth, bar.FullHeight);
        bool send = Widgets.ImageButton(bar, at.X, at.Y, 1f, !raid.Ended);
        Text.DrawAnchored(data.Strings[TextSendAll], at.X, at.Y, Text.GameFont[0], Anchor.Center, Color.White, Color.Black, 2);
        if (send)
        {
            audio?.Play(36);
            question = (data.Strings[TextSendAllAsk], data.Strings[TextOk], data.Strings[316], raid.AttackAll);
        }
    }

    /// <summary>FUN_00152f88 buttons 16 .. 18: used up (string 638), or "are you sure" and the tombstone's power.</summary>
    void TombstonePressed(int group, int[] slots)
    {
        audio?.Play(36);
        var b = raid.Player.Boosts;
        if (group == 3)
        {
            // button 19: the yeti - used up (638), nobody inside yet, or "Would you like to summon ...?"
            if (raid.CannotSummonYeti() is { } why)
                note = why;
            else
                question = (raid.YetiQuestion, data.Strings[TextOk], data.Strings[316], raid.SummonYeti);
            return;
        }
        if (!slots.Any(b.Usable))
        {
            note = data.Strings[TextUsedUp];
            return;
        }
        question = group switch
        {
            0 => (data.Strings[TextStrengthAsk], data.Strings[TextOk], data.Strings[316], raid.UseStrength),
            1 => (data.Strings[TextEnergyAsk], data.Strings[TextOk], data.Strings[316], raid.UseHealth),
            _ => (data.Strings[TextReviveAsk], data.Strings[TextOk], data.Strings[316], raid.UseGolden),
        };
    }

    /// <summary>Named places: raider0.., rival (the chef), staff0.., counter, attack, energize, info, flag, logo, ok, msgok, done.</summary>
    Vector2? ScriptTarget(string name)
    {
        Vector2 Body(CharacterInstance c) => Vector2.Transform(new Vector2(c.X, c.Y - 60), WorldToWindow);
        Vector2? Ui(Rectangle? r) => r is { } x ? Screen.ToPixelsPoint(new Vector2(x.X + x.Width / 2, x.Y + x.Height / 2)) : null;
        if (name.StartsWith("raider") && int.TryParse(name[6..], out int i))
            return i < raid.Raiders.Count ? Body(raid.Raiders[i]) : null;
        if (name.StartsWith("staff") && int.TryParse(name[5..], out int j))
            return j < raid.Staff.Count ? Body(raid.Staff[j]) : null;
        return name switch
        {
            "rival" => raid.RivalChef is { } c ? Body(c) : null,
            "counter" => cafe.FindFurniture(FurnitureCategory.Counter, -1).FirstOrDefault() is { } f
                ? Vector2.Transform(f.SolidPoint(), WorldToWindow) : null,
            "attack" => Ui(attackRect),
            "energize" => Ui(energizeRect),
            "info" => Ui(infoRect),
            "flag" => Ui(flagRect),
            "strength" => Ui(tombstoneRects[0]),
            "health" => Ui(tombstoneRects[1]),
            "golden" => Ui(tombstoneRects[2]),
            "yeti" => Ui(tombstoneRects[3]),
            "amptron" => Ui(amptronRect),
            "special" => Ui(specialRect),
            "amp" => raid.Amptron is { } amp ? Body(amp) : null,
            "wolf" => Ui(wolfRect),
            "transform" => Ui(transformRect),
            "bigwolf" => raid.Wolves.Count > 0 ? Body(raid.Wolves[0]) : null,
            "sendall" => Ui(sendAllRect),
            "logo" => Ui(logoRect),
            "done" => Ui(okRect),
            "ok" => chefMessage.ButtonCenter is { } okAt ? Screen.ToPixelsPoint(okAt) : null,
            "msgok" => Ui(Widgets.LastMessageButton),
            _ => null,
        };
    }
}
