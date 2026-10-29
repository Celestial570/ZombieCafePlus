using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The neighbourhood map (GameStateMap: load 00084808, update 0008b168, layout 0008a228, lot placement
/// FUN_0008a39c, the boss restaurant vf17 / vf18): the player's cafe and the rival restaurants of the levels around
/// the player's, three lots to a stretch of street, scrolled sideways. A rival is raided by tapping it; one shut
/// after a raid is boarded up with a countdown and can be reopened for toxin. The original's friends' cafes,
/// "Random Café" and leaderboard were online services and are left out.
/// </summary>
public sealed class MapScreen : GameScreen
{
    // the iPad's numbers (device 0 of the per-device tables DAT_00371b08 .. DAT_00371e70)
    /// <summary>DAT_00371bd8 / DAT_00371c20: a lot's building (its bottom-right corner), by the lot's column.</summary>
    static readonly float[] LotX = [465, 465, 60], LotY = [110, -360, -125];
    const float TileStep = 810;                      // a stretch of street: the background's width
    const float TileRowStep = 1024 - 78;             // the background's second row (DAT_0008becc)
    const float NameDx = 95, NameDy = -15;           // DAT_00371c80 / DAT_00371c98
    const float LevelDx = 265 - 82, LevelDy = 105 - 66;   // DAT_00371cb0 + ce0, DAT_00371cc8 + cf8
    const float BoardsDx = 8, BoardsDy = 21;         // DAT_00371d40 / d58: the boards over a shut cafe
    const float ReopenDx = 200, ReopenDy = 21;       // DAT_00371d70 / d88
    const float SmokeDx = 175, SmokeDy = 200;        // DAT_00371da0 / db8
    const float ScrollYMin = -50, ScrollYMax = 150;  // DAT_00371de8 / dd0
    const float EdgeGap = 10;                        // layout: BACK and the arrows 10 from the edges
    const float ArrowPush = 50;                      // an arrow tapped: the map's speed
    const float ReopenScaleX = 1.5f;                 // FUN_00027c84(REOPEN, 1.5, 1)
    /// <summary>
    /// The lots are measured from half the screen's *width* across and down (game +0x18 both times), which on the
    /// iPad puts them 128 below where the background's centre is; kept as that offset for any window shape.
    /// </summary>
    const float LotDrop = (Screen.BaseWidth - Screen.BaseHeight) / 2;
    /// <summary>+0x178: the names painted on the street (a, b, c, d, tx, ty; DAT_00371b18 / b30 add the 11, 8).</summary>
    static readonly Matrix3x2 Paint = new(0.6967067f, -0.41667467f, 0.71735609f, 0.40468052f, 11, 8);
    /// <summary>DAT_00371b08: the player's cafe grows with the recipes known (DAT_00371e70 of 10 x known / all).</summary>
    static readonly int[] YourCafeSprites = [3, 18, 19, 20];
    static readonly int[] YourCafeShift = [0, 8, 13, 15];     // DAT_00371e00
    static readonly int[] SizeClass = [0, 0, 1, 1, 1, 2, 2, 2, 3, 3];
    const int FontName = 6, FontSmall = 3;                    // DAT_00371ba8 / bc0: game +0x11c fonts
    // mapTiles
    const int MapBackground = 0, MapBack = 1, MapArrowLeft = 4, MapArrowRight = 5, MapBoards = 14, MapSmoke = 22, SmokeFrames = 10;
    // ipad_ingameUi
    const int UiReopen = 11, UiToxin = 36;
    // strings.bin
    const int TextYourCafe = 444, TextLevel = 79, TextReopening = 343, TextReopenAsk = 642, TextReopenAsk2 = 643,
        TextUseToxin = 319, TextCancel = 316, TextNoZombies = 413, TextOk = 362, TextNoToxin = 320, TextAddToxin = 321,
        TextGetToxin = 443;
    const int SoundClick = 40, SoundBoss = 201, MusicMap = 15;
    /// <summary>ZCImageButton held down without a pressed image (FUN_001061e4).</summary>
    static readonly Color Pressed = Colors.FromArgb(0xffccccccu);
    // a closed restaurant reopens on a tap only with this long to go (GameStateMap::update)
    const int ReopenMinimumMs = 5000;

    /// <summary>One rival on the map (the 0x48-byte entries at +0xd4).</summary>
    sealed class Lot
    {
        public int Level, Type, Row, Col;
        public bool Shut;            // +0x44
        public bool Boss;            // +0x46
        public string Name = "";     // [7]: the name, or "Reopening in ..." while shut
        public Rectangle Building, Reopen;
    }

    readonly GameData data;
    readonly CafeController game;
    readonly Audio.AudioSystem? audio;
    readonly Action back;
    readonly Action<string, int, int>? raid;
    readonly List<Lot> lots = new();
    readonly ChefMessage chefMessage;
    readonly TutorialArrow arrow;
    PlayerState Player => game.Player;
    Neighbourhood Neighbours => Player.Neighbours;

    // +0x1a8 / +0x1ac: the scroll; +0xb0 / +0xb4: its speed (per frame)
    Vector2 scroll, velocity;
    float stepCarry;
    // the touch: where it went down, how far it has been dragged (game +0x110), last frame's place
    Vector2 pressAt, lastAt;
    float dragged;
    bool pressing;
    // +0x55 / +0x58: the boss restaurant arriving (or leaving) in a puff of smoke, and the smoke's frame
    bool smoke, bossShown;
    int smokeFrame;
    // message boxes: a question with its yes action, or a note
    (string Text, string Yes, string No, Action OnYes)? question;
    string? note;
    bool left, musicStarted;
    Rectangle backRect, leftRect, rightRect, yourRect;

    public MapScreen(GameData data, CafeController game, Audio.AudioSystem? audio, Action back, Action<string, int, int>? raid)
    {
        this.data = data;
        this.game = game;
        this.audio = audio;
        this.back = back;
        this.raid = raid;
        chefMessage = new ChefMessage(data, id => audio?.Play(id));
        arrow = new TutorialArrow(data);
        var hints = Player.Hints;
        hints.Check = MapHint;
        hints.Show = text => chefMessage.Show(text);
        hints.Busy = () => chefMessage.Visible || question != null || note != null;

        foreach (int level in Neighbourhood.MapLevels(Player.Level))
        {
            int i = lots.Count, row = (i + 1) / 3;
            var lot = new Lot { Level = level, Row = row, Col = row * 3 - (i + 1) + 2, Type = Neighbours.TypeOf(data, level) };
            lot.Shut = Neighbours.IsShut(level);
            lot.Name = lot.Shut ? Countdown(level) : Neighbours.NameOf(data, lot.Type, level);
            lots.Add(lot);
        }
        // the boss restaurant: there already, or turning up now (FUN_001da250 / FUN_001da284 / FUN_001da2a0)
        if (Neighbours.BossPresent(data))
            BossArrives(animate: false);
        else if (Neighbours.BossTimer < 1 && Neighbours.RollForBoss(data, Player, GameClock.Now, game.Cafe.Rng))
            BossArrives(animate: true);
        // the first visit: point at the restaurant across the street (hint 0x24 again until a raid has begun)
        if (!Player.Hints.IsShown(0x25))
        {
            Player.Hints.Reset(0x24);
            game.Hint(0x1d, false);
        }
    }

    // ---------------------------------------------------------------- layout

    SubImage[] Tiles => data.Sheet("mapTiles");
    float Cx => Screen.Width / 2;
    float Cy => Screen.Height / 2;

    // GameStateMap::update: the background's columns and the scroll's limits (no friends: one stretch to the left)
    int Rows => (lots.Count - 1) / 3;
    float ScrollXMax => TileStep - Cx + TileStep / 2;
    float ScrollXMin => Cx - TileStep / 2 - TileStep * (Rows + 1);

    public override void Layout() => Input.TargetToPixels = ScriptTarget;

    /// <summary>FUN_0008a39c: where a lot's building stands (bottom-right corner), virtual screen.</summary>
    Vector2 LotPoint(int row, int col, float shift = 0) =>
        new(Cx + LotX[col] + row * TileStep + shift + scroll.X, Cy + LotDrop + LotY[col] + scroll.Y);

    /// <summary>The player's cafe's size class (10 x recipes known / recipes, FUN_0005cff8).</summary>
    int YourCafeClass()
    {
        int known = data.Foods.Count(f => Player.KnowsRecipe(f.Index));
        int step = Math.Min(10 * known / Math.Max(1, data.Foods.Count), 9);
        return SizeClass[step];
    }

    string Countdown(int level) => data.Strings[TextReopening] + Durations.Short(Neighbours.TimerOf(level) / 1000);

    // ---------------------------------------------------------------- hints

    /// <summary>FUN_0008852c: the map's say over the hints.</summary>
    bool MapHint(int hint)
    {
        switch (hint)
        {
            case 0x24:
                // the restaurant across the street - once there are zombies to send
                if (game.Staff.Count == 0)
                    return false;
                arrow.PointAt(() => lots.Count > 0 ? lots[0].Building : null, flipped: true);
                return true;
            case 0x25:
                return true;
            case 0x31:
            case 0x42:
                arrow.Hide();
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- the boss restaurant

    /// <summary>vf17: the boss restaurant takes the place of the highest-level rival on the map.</summary>
    void BossArrives(bool animate)
    {
        if (lots.Count == 0)
            return;
        var lot = lots[^1];
        lot.Boss = true;
        lot.Shut = false;
        lot.Type = Neighbours.BossType(data);
        lot.Name = Neighbours.NameOf(data, lot.Type, lot.Level);
        bossShown = true;
        smoke = animate;
        smokeFrame = 0;
    }

    /// <summary>vf18 (and the boss's leaving): scroll to the restaurant, puff, swap it at the fourth frame.</summary>
    void UpdateSmoke(bool leaving)
    {
        var lot = lots[^1];
        var at = new Vector2(lot.Building.X + lot.Building.Width, lot.Building.Y + lot.Building.Height);
        float dx = Cx - at.X;
        velocity = new Vector2(dx / 10f, (Cy - at.Y) / 10f);
        if (MathF.Abs(dx) >= 150)
            return;
        if (smokeFrame < SmokeFrames)
        {
            if (smokeFrame == 3)
            {
                audio?.Play(SoundBoss);
                if (leaving)
                {
                    // gone: the slot's own restaurant is back
                    lot.Boss = false;
                    lot.Type = Neighbours.TypeOf(data, lot.Level);
                    lot.Shut = Neighbours.IsShut(lot.Level);
                    lot.Name = lot.Shut ? Countdown(lot.Level) : Neighbours.NameOf(data, lot.Type, lot.Level);
                    bossShown = false;
                }
            }
            smokeFrame++;
        }
        else
        {
            smoke = false;
            smokeFrame = 0;
        }
    }

    // ---------------------------------------------------------------- update

    /// <summary>How long the map has been up (the cafe catches up on it when the player goes back).</summary>
    public long ElapsedMs { get; private set; }

    public override void Update(float dt)
    {
        int ms = Math.Max(1, (int)MathF.Round(dt * 1000f));
        ElapsedMs += ms;
        var hints = Player.Hints;
        hints.Update(ms, data);
        chefMessage.Update(ms);
        arrow.Update(ms);
        // ZombieCafe::update runs under every screen: the rivals' clocks
        Neighbours.Update(data, Player, ms, game.Cafe.Rng);
        if (!musicStarted)
        {
            musicStarted = true;
            audio?.PlayMusic(MusicMap, loop: true);
        }
        // the boss restaurant's stay ran out while the map is up
        if (bossShown && !smoke && Neighbours.BossTimer < 1)
        {
            smoke = true;
            smokeFrame = 0;
        }
        RefreshLots();

        // the map moves at the original's 60 Hz
        stepCarry += dt * 60f;
        bool modal = chefMessage.Visible || question != null || note != null;
        var mouse = Widgets.Mouse;
        if (!modal && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            pressing = true;
            pressAt = lastAt = mouse;
            dragged = 0;
        }
        while (stepCarry >= 1f)
        {
            stepCarry -= 1f;
            Step(mouse, 1000f / 60f);
        }
        if (pressing && Input.IsMouseButtonReleased(MouseButton.Left))
        {
            pressing = false;
            if (!modal && dragged <= data.K.Int(0x42d5c8))
                Tap(mouse);
        }
        if (!left && !modal && Input.IsKeyPressed(KeyboardKey.Escape))
            GoBack();
    }

    /// <summary>One 60 Hz step of GameStateMap::update's scrolling: a drag moves the map, then it glides to a stop.</summary>
    void Step(Vector2 mouse, float stepMs)
    {
        if (smoke)
            UpdateSmoke(leaving: Neighbours.BossTimer < 1);
        else if (pressing)
        {
            dragged += Vector2.Distance(mouse, lastAt);
            velocity = dragged > data.K.Int(0x42d5c8) ? mouse - lastAt : Vector2.Zero;
            lastAt = mouse;
        }
        scroll += velocity;
        float keep = stepMs * data.K.Float(0x42d2e8);
        keep = keep < 1 ? 1 - keep : 0;
        velocity *= keep;
        float stop = data.K.Float(0x42d2ec);
        if (MathF.Abs(velocity.X) < stop && MathF.Abs(velocity.Y) < stop)
            velocity = Vector2.Zero;
        float min = ScrollXMin, max = MathF.Max(ScrollXMin, ScrollXMax);
        scroll.X = MathF.Floor(Math.Clamp(scroll.X, min, max));
        scroll.Y = MathF.Floor(Math.Clamp(scroll.Y, ScrollYMin, ScrollYMax));
    }

    /// <summary>GameStateMap::update: shut restaurants count down, and the ones that reopen get their new name.</summary>
    void RefreshLots()
    {
        foreach (var lot in lots)
        {
            if (!lot.Shut || (lot.Boss && smoke))
                continue;
            if (Neighbours.TimerOf(lot.Level) > 0)
            {
                lot.Name = Countdown(lot.Level);
                continue;
            }
            lot.Shut = false;
            lot.Type = Neighbours.TypeOf(data, lot.Level);
            lot.Name = Neighbours.LocalNameOf(data, lot.Level);
        }
    }

    bool Hit(Rectangle r, Vector2 p) => Raylib.CheckCollisionPointRec(p, r);

    /// <summary>GameStateMap::update: a tap (released without dragging).</summary>
    void Tap(Vector2 at)
    {
        if (left)
            return;
        // the arrows push the map along
        if (Hit(leftRect, at) && ScrollXMax > scroll.X)
        {
            audio?.Play(SoundClick);
            velocity.X = ArrowPush;
            return;
        }
        if (Hit(rightRect, at) && scroll.X > ScrollXMin)
        {
            audio?.Play(SoundClick);
            velocity.X = -ArrowPush;
            return;
        }
        if (Hit(backRect, at) || Hit(yourRect, at))
        {
            GoBack();
            return;
        }
        bool started = Player.Hints.IsShown(0x25);
        for (int i = 0; i < lots.Count; i++)
        {
            var lot = lots[i];
            if (!Hit(lot.Building, at))
                continue;
            if (lot.Shut)
            {
                // the REOPEN button: toxin to have it open again (not in its last seconds)
                if (started && Hit(lot.Reopen, at) && Neighbours.TimerOf(lot.Level) >= ReopenMinimumMs)
                {
                    audio?.Play(SoundClick);
                    int cost = data.K.Int(0x42d6e4);
                    question = (data.Strings[TextReopenAsk] + cost + data.Strings[TextReopenAsk2], data.Strings[TextUseToxin],
                        data.Strings[TextCancel], () => Reopen(lot));
                }
                return;
            }
            // before the first raid only the restaurant across the street can be raided
            if (!started && i != 0)
                return;
            audio?.Play(SoundClick);
            if (!game.Staff.Any(z => z.Character.Job != Job.Other))
            {
                note = data.Strings[TextNoZombies];
                return;
            }
            StartRaid(lot);
            return;
        }
    }

    /// <summary>FUN_0008d288: the reopening paid for (or the toxin store suggested).</summary>
    void Reopen(Lot lot)
    {
        if (!Neighbours.PayToReopen(data, Player, lot.Level))
        {
            question = (data.Strings[TextNoToxin], data.Strings[TextAddToxin], data.Strings[TextCancel],
                () => note = data.Strings[TextGetToxin]);
        }
    }

    void StartRaid(Lot lot)
    {
        if (raid == null)
            return;
        left = true;
        game.Hint(0x1e, false);
        raid(lot.Name, lot.Level, lot.Type);
    }

    void GoBack()
    {
        if (left)
            return;
        left = true;
        audio?.Play(SoundClick);
        back();
    }

    public override void Leave()
    {
        // the next screen may have taken the hints over already (it is made before this one leaves)
        var hints = Player.Hints;
        if (hints.Check != MapHint)
            return;
        hints.Check = null;
        hints.Show = null;
        hints.Busy = null;
    }

    // ---------------------------------------------------------------- draw

    public override void Draw()
    {
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        var tiles = Tiles;
        if (tiles.Length <= 42)
            return;
        Widgets.Blocked = chefMessage.Visible || question != null || note != null;

        // the background: five stretches of street (one to the left of the player's), two rows (and one more
        // below for windows taller than the iPad's)
        var bg = tiles[MapBackground];
        // (half a texel in from the picture's edges so the sheet's neighbours do not bleed in, and a point wider
        // and taller so neighbouring pieces overlap instead of leaving a hairline where they meet)
        var src = new Rectangle(bg.Src.X + 0.5f, bg.Src.Y + 0.5f, bg.Src.Width - 1, bg.Src.Height - 1);
        for (int k = 0; k < Rows + 4; k++)
        {
            float x = Cx + TileStep * (k - 1) + scroll.X - bg.FullWidth / 2f;
            for (int r = 1; r >= -1; r--)
                Gfx.Draw.Image(bg.Texture, src, new Rectangle(x, Cy + scroll.Y + r * TileRowStep - bg.FullHeight / 2f,
                    bg.FullWidth + 1, bg.FullHeight + 1), Color.White);
        }

        // the player's cafe, its name and level
        int size = YourCafeClass();
        var yours = tiles[YourCafeSprites[size]];
        var yourAt = LotPoint(0, 2, YourCafeShift[size]);
        yourRect = Building(yours, yourAt, true);
        var plain = LotPoint(0, 2);
        Painted(data.Strings[TextYourCafe], plain + new Vector2(NameDx, NameDy));
        Painted(data.Strings[TextLevel] + Player.Level, plain + new Vector2(LevelDx, LevelDy));

        // the rivals
        var ui = data.IngameUi;
        foreach (var lot in lots)
        {
            var at = LotPoint(lot.Row, lot.Col);
            var img = tiles[data.EnemyCafes[lot.Type].MapSprite];
            lot.Building = Building(img, at, !lot.Shut);
            Painted(lot.Name, at + new Vector2(NameDx, NameDy));
            if (lot.Shut)
            {
                float left = at.X - img.FullWidth;
                // boarded up, with the REOPEN button (stretched wide, the toxin price at its left end)
                tiles[MapBoards].Draw(left + BoardsDx, at.Y - BoardsDy, Anchor.Bottom | Anchor.Left, Color.White);
                var button = ui[UiReopen];
                var corner = new Vector2(left + ReopenDx, at.Y - ReopenDy);
                var topLeft = corner - new Vector2(button.FullWidth, button.FullHeight);
                lot.Reopen = new Rectangle(topLeft.X, topLeft.Y, button.FullWidth * ReopenScaleX, button.FullHeight);
                bool down = pressing && Hit(lot.Reopen, Widgets.Mouse) && Hit(lot.Reopen, pressAt);
                button.Draw(topLeft.X, topLeft.Y, Anchor.Top | Anchor.Left, down ? Pressed : Color.White,
                    Flip.None, ReopenScaleX, 1);
                var mid = new Vector2(topLeft.X + lot.Reopen.Width / 2, topLeft.Y + button.FullHeight / 2f);
                Shadowed("REOPEN", mid, Text.GameFont[FontSmall], Color.White, Color.Black, ReopenScaleX);
                var vial = ui[UiToxin];
                vial.Draw(topLeft.X, topLeft.Y + button.FullHeight / 2f, Anchor.Bottom | Anchor.Left, Color.White);
                var vialMid = new Vector2(topLeft.X + vial.FullWidth / 2f, topLeft.Y + button.FullHeight / 2f - vial.FullHeight / 2f);
                Shadowed(data.K.Int(0x42d6e4).ToString(), vialMid, Text.GameFont[FontSmall], Color.Black, Color.White, 1);
            }
            else
            {
                lot.Reopen = default;
            }
            Painted(data.Strings[TextLevel] + Math.Min(lot.Level, data.K.Int(0x42d590)), at + new Vector2(LevelDx, LevelDy));
        }
        // the boss restaurant's puff of smoke (frames of the smoke cloud, twice the size)
        if (smoke && lots.Count > 0 && smokeFrame < SmokeFrames)
        {
            var b = lots[^1].Building;
            var puff = tiles[MapSmoke + smokeFrame];
            puff.Draw(b.X + SmokeDx, b.Y + b.Height - SmokeDy, Anchor.Center, Color.White, Flip.None, 2, 2);
        }

        // BACK at the bottom right, the arrows at the sides while there is more map that way (layout 0008a228)
        var backImg = tiles[MapBack];
        backRect = ButtonAt(backImg, Screen.Width - EdgeGap, Screen.Height - EdgeGap, Anchor.Bottom | Anchor.Right);
        leftRect = scroll.X < ScrollXMax ? ButtonAt(tiles[MapArrowLeft], EdgeGap, Cy, Anchor.Left | Anchor.VCenter) : default;
        rightRect = scroll.X > ScrollXMin ? ButtonAt(tiles[MapArrowRight], Screen.Width - EdgeGap, Cy, Anchor.Right | Anchor.VCenter) : default;

        arrow.Draw(p => p);
        // the map under the chef's board and the boxes stayed blocked; their own buttons take the clicks
        Widgets.Blocked = false;
        chefMessage.Draw();
        DrawBoxes();
    }

    /// <summary>A building on its lot (anchored by its bottom-right corner); greyed while pressed.</summary>
    Rectangle Building(SubImage img, Vector2 corner, bool pressable)
    {
        var r = new Rectangle(corner.X - img.FullWidth, corner.Y - img.FullHeight, img.FullWidth, img.FullHeight);
        bool down = pressable && pressing && dragged <= data.K.Int(0x42d5c8) && Hit(r, Widgets.Mouse) && Hit(r, pressAt)
            && question == null && note == null && !chefMessage.Visible;
        img.Draw(r.X, r.Y, Anchor.Top | Anchor.Left, down ? Pressed : Color.White);
        return r;
    }

    Rectangle ButtonAt(SubImage img, float x, float y, int anchor)
    {
        float l = (anchor & Anchor.Right) != 0 ? x - img.FullWidth : (anchor & Anchor.HCenter) != 0 ? x - img.FullWidth / 2f : x;
        float t = (anchor & Anchor.Bottom) != 0 ? y - img.FullHeight : (anchor & Anchor.VCenter) != 0 ? y - img.FullHeight / 2f : y;
        var r = new Rectangle(l, t, img.FullWidth, img.FullHeight);
        bool down = pressing && Hit(r, Widgets.Mouse) && Hit(r, pressAt) && question == null && note == null && !chefMessage.Visible;
        img.Draw(l, t, Anchor.Top | Anchor.Left, down ? Pressed : Color.White);
        return r;
    }

    /// <summary>A name painted on the street: the big font, top-centred on the point, under the street's slant.</summary>
    void Painted(string text, Vector2 at)
    {
        Gfx.Draw.Push(Paint * Matrix3x2.CreateTranslation(at));
        Text.DrawAnchored(text, 0, 0, Text.GameFont[FontName], Anchor.Top | Anchor.HCenter, Color.White);
        Gfx.Draw.Pop();
    }

    /// <summary>A label with the bitmap font's drop shadow (one pixel down and right), centred, optionally stretched.</summary>
    static void Shadowed(string text, Vector2 centre, float size, Color color, Color shadow, float stretchX)
    {
        Gfx.Draw.Push(Matrix3x2.CreateScale(stretchX, 1) * Matrix3x2.CreateTranslation(centre));
        Text.DrawAnchored(text, 1, 1, size, Anchor.Center, shadow);
        Text.DrawAnchored(text, 0, 0, size, Anchor.Center, color);
        Gfx.Draw.Pop();
    }

    void DrawBoxes()
    {
        if (question is { } q)
        {
            int answer = Widgets.QuestionBox(q.Text, q.Yes, q.No);
            if (answer != 0)
            {
                audio?.Play(SoundClick);
                question = null;
                if (answer == 1)
                    q.OnYes();
            }
        }
        else if (note != null && Widgets.MessageBox("", [note], data.Strings[TextOk]))
        {
            audio?.Play(SoundClick);
            note = null;
        }
    }

    // ---------------------------------------------------------------- test scripts

    /// <summary>Named places for scripted clicks: back, left, right, yours, cafe0..3, reopen0..3, ok (the chef's board), msgok.</summary>
    Vector2? ScriptTarget(string name)
    {
        Rectangle? r = name switch
        {
            "back" => backRect,
            "left" => leftRect,
            "right" => rightRect,
            "yours" => yourRect,
            "msgok" => Widgets.LastMessageButton,
            _ when name.StartsWith("cafe") && int.TryParse(name[4..], out int i) && i >= 0 && i < lots.Count => lots[i].Building,
            _ when name.StartsWith("reopen") && int.TryParse(name[6..], out int i) && i >= 0 && i < lots.Count => lots[i].Reopen,
            _ => null,
        };
        if (name == "ok" && chefMessage.ButtonCenter is { } c)
            return Screen.ToPixelsPoint(c);
        return r is { Width: > 0 } rr ? Screen.ToPixelsPoint(new Vector2(rr.X + rr.Width / 2, rr.Y + rr.Height / 2)) : null;
    }
}
