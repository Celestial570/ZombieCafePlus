using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// GameStateMeatLocker (load FUN_000e6440, update FUN_000e8004, a zombie picked FUN_000e9fe8): the cold room the
/// player's frozen zombies hang in, one to a hook - the locker holds five, five more at a time for 5 toxin (K
/// 0x42d620) up to sixty - each playing its hanging (animation 26). The row of hooks slides sideways (dragged, or
/// three and a quarter hooks a press of an arrow). A zombie picked struggles (27) under the spotlight with its name and
/// stats on show, and REVIVE (back on the staff while there is room) and FIRE come up; ADD 5 HOOKS is always there,
/// with the locker's count over its size; BACK goes to the café (sound 40).
/// </summary>
public sealed class MeatLockerScreen : GameScreen
{
    // ipad_MeatLockerUi
    const int UiArrowRight = 0, UiArrowLeft = 1, UiRoom = 2, UiBack = 3, UiFrost = 4, UiHook = 5, UiButton = 6, UiSpot = 7,
        UiVial = 11;
    // the code's own words: the buttons, the "/" of the count, why not
    const uint TextRevive = 0x34172c, TextAddHooks = 0x341733, TextFire = 0x34173f, TextOutOf = 0x33f19b,
        TextStaffFull = 0x341744, TextLimitedTo = 0x341773, TextNoMore = 0x34178c;
    // strings.bin
    const int TextTipRating = 509, TextAtkSpeed = 510, TextAtkStrength = 511, TextEnergy = 512, TextUseAsk = 317,
        TextUseAsk2 = 318, TextUseToxin = 319, TextNoToxin = 320, TextCancel = 316, TextOk = 362, TextFireAsk = 472;
    // the iPad's layout (DAT_00373250 .., DAT_00373400 the hooks' spacing, DAT_00373418 their height, DAT_00373430
    // where the zombies hang; the row starts so that the first five fill the room's window)
    const float HookStep = 188, HookY = 228, BodyY = 360, FirstHook = 136, FrostY = 260, ButtonX = 780, ReviveY = 500,
        FireY = 580, AddY = 660, CountX = 960, CountY = 390, VialX = 890, VialY = 630, CostY = 656, BackX = 1000, BackY = 754;
    const float PageW = 1024, PageH = 768;

    readonly GameData data;
    readonly CafeController game;
    readonly Audio.AudioSystem? audio;
    readonly Action back;
    readonly SubImage[] sheet;
    // +0x8c: the frozen zombies as they hang
    readonly List<FlashAnimation?> bodies = new();
    int picked = -1;          // +0x28
    float scroll, speed;      // +0x21c / +0x220
    bool dragging, pressed;
    Vector2 lastMouse, pressMouse;
    (string Text, string Yes, string No, Action OnYes)? question;
    string? note;
    bool left;
    Rectangle? backRect, reviveRect, fireRect, addRect, leftRect, rightRect;

    public long ElapsedMs { get; private set; }

    public MeatLockerScreen(GameData data, CafeController game, Audio.AudioSystem? audio, Action back)
    {
        this.data = data;
        this.game = game;
        this.audio = audio;
        this.back = back;
        sheet = data.Sheet("ipad_MeatLockerUiImages", "ipad_MeatLockerUiOffsets.bin");
        Rehang();
    }

    public override void Layout() => Input.TargetToPixels = ScriptTarget;

    Vector2 Origin => new((Screen.Width - PageW) / 2, (Screen.Height - PageH) / 2);

    /// <summary>GameStateMeatLocker::load: every frozen zombie hangs on its hook (FUN_000e77f8, animation 26).</summary>
    void Rehang()
    {
        bodies.Clear();
        foreach (var c in game.Player.Frozen)
            bodies.Add(Hanging(c, (int)CharAnim.Hung));
        picked = -1;
    }

    FlashAnimation? Hanging(Character c, int anim)
    {
        string name = c.Def.PartsSheet(true);
        var parts = data.Sheet(name, name + ".bin");
        var (file, mirrored) = data.Animations.Get(anim, (int)Facing.SW);
        if (file == null || parts.Length == 0)
            return null;
        var a = new FlashAnimation(file, parts) { FlipX = mirrored };
        a.Play();
        return a;
    }

    // ---------------------------------------------------------------- what the buttons do

    /// <summary>FUN_000e9fe8: a zombie picked - it struggles (27) under the spotlight; picked again, it hangs still.</summary>
    void Pick(int i)
    {
        if (picked >= 0 && picked < bodies.Count)
            bodies[picked] = Hanging(game.Player.Frozen[picked], (int)CharAnim.Hung);
        if (picked == i || i < 0)
        {
            picked = -1;
            return;
        }
        picked = i;
        bodies[i] = Hanging(game.Player.Frozen[i], (int)CharAnim.Hung2);
    }

    /// <summary>REVIVE (FUN_000e9b68): back on the staff while it has room (K 0x42d5b4), or why not.</summary>
    void Revive()
    {
        audio?.Play(40);
        if (picked < 0)
            return;
        if (game.Staff.Count >= game.StaffLimit)
        {
            note = data.Exe.Get(TextStaffFull);
            return;
        }
        game.Thaw(game.Player.Frozen[picked]);
        Rehang();
    }

    /// <summary>FIRE: "are you sure", then gone (FUN_000e9e90).</summary>
    void Fire()
    {
        audio?.Play(40);
        if (picked < 0)
            return;
        var c = game.Player.Frozen[picked];
        question = (data.Strings[TextFireAsk] + c.Name + "?", data.Strings[TextOk], data.Strings[TextCancel], () =>
        {
            game.FireFrozen(c);
            Rehang();
        });
    }

    /// <summary>
    /// ADD 5 HOOKS: at sixty, the limit; short of the toxin, the toxin store's question; else "use 5 toxin?" and five
    /// more hooks (FUN_000e9e08).
    /// </summary>
    void AddHooks()
    {
        audio?.Play(40);
        int cost = game.LockerCost;
        if (!game.CanExpandLocker)
            note = data.Exe.Get(TextLimitedTo) + CafeController.LockerMax + data.Exe.Get(TextNoMore);
        else if (game.Player.Toxin < cost)
            note = data.Strings[TextNoToxin];
        else
            question = (data.Strings[TextUseAsk] + cost + data.Strings[TextUseAsk2], data.Strings[TextUseToxin],
                data.Strings[TextCancel], () => game.ExpandLocker());
    }

    // ---------------------------------------------------------------- update

    public override void Update(float dt)
    {
        int ms = Math.Max(1, (int)MathF.Round(dt * 1000f));
        ElapsedMs += ms;
        // ZombieCafe::update runs under every screen: the rivals' clocks
        game.Player.Neighbours.Update(data, game.Player, ms, game.Cafe.Rng);
        foreach (var b in bodies)
            b?.Update(ms);
        // the row slides on after a drag, slowing (+0x220), and stops at its ends
        if (!dragging)
        {
            scroll += speed;
            speed *= MathF.Max(0f, 1f - dt * 4f);
            if (MathF.Abs(speed) < 0.5f)
                speed = 0;
        }
        ClampScroll();
        bool modal = question != null || note != null;
        if (modal)
        {
            pressed = false;
            return;
        }
        var mouse = Screen.FromPixels(Input.MousePosition);
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            pressed = true;
            dragging = false;
            lastMouse = pressMouse = mouse;
        }
        if (Input.IsMouseButtonDown(MouseButton.Left) && pressed)
        {
            if (Vector2.Distance(mouse, pressMouse) > 8)
                dragging = true;
            if (dragging)
            {
                speed = mouse.X - lastMouse.X;
                scroll += speed;
            }
            lastMouse = mouse;
        }
        if (Input.IsMouseButtonReleased(MouseButton.Left))
        {
            if (pressed && !dragging && !Widgets.MouseOverUi)
                Tap(mouse);
            pressed = dragging = false;
        }
    }

    float HookX(int i) => Origin.X + FirstHook + scroll + i * HookStep;

    void ClampScroll()
    {
        float min = -MathF.Max(0, (game.Player.LockerSize - 5) * HookStep);
        if (scroll < min)
        {
            scroll = min;
            speed = 0;
        }
        if (scroll > 0)
        {
            scroll = 0;
            speed = 0;
        }
    }

    /// <summary>A tap on the row: the zombie hanging there picked (or put back).</summary>
    void Tap(Vector2 p)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            float x = HookX(i), y = Origin.Y + BodyY;
            if (p.X >= x - 60 && p.X <= x + 60 && p.Y >= y - 200 && p.Y <= y + 60)
            {
                audio?.Play(36);
                Pick(i);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- draw

    public override void Draw()
    {
        Raylib.ClearBackground(new Color(20, 26, 30, 255));
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        var o = Origin;
        bool modal = question != null || note != null;
        Widgets.Blocked = modal;
        sheet[UiRoom].Draw(o.X, o.Y, Anchor.Top | Anchor.Left, Color.White);

        // the hooks, the spotlight on the one picked, the zombies hanging
        if (picked >= 0)
            sheet[UiSpot].Draw(HookX(picked), o.Y + HookY - 40, Anchor.Top | Anchor.HCenter, Color.White);
        for (int i = 0; i < game.Player.LockerSize; i++)
        {
            float x = HookX(i);
            if (x < -HookStep || x > Screen.Width + HookStep)
                continue;
            sheet[UiHook].Draw(x, o.Y + HookY, Anchor.Bottom | Anchor.HCenter, Color.White);
            if (i < bodies.Count)
                bodies[i]?.Draw(x, o.Y + BodyY, 1f, Color.White);
        }
        sheet[UiFrost].Draw(o.X + PageW / 2, o.Y + FrostY, Anchor.Top | Anchor.HCenter, new Color(255, 255, 255, 160));

        // the arrows: three and a quarter hooks a press
        leftRect = rightRect = null;
        float ay = o.Y + BodyY - 80;
        if (scroll < 0)
        {
            var l = sheet[UiArrowLeft];
            leftRect = new Rectangle(o.X + 30 - l.FullWidth / 2f, ay - l.FullHeight / 2f, l.FullWidth, l.FullHeight);
            if (Widgets.ImageButton(l, o.X + 30, ay))
            {
                audio?.Play(36);
                scroll += HookStep * 3.25f;
                speed = 0;
            }
        }
        if (scroll > -MathF.Max(0, (game.Player.LockerSize - 5) * HookStep))
        {
            var r = sheet[UiArrowRight];
            rightRect = new Rectangle(o.X + PageW - 30 - r.FullWidth / 2f, ay - r.FullHeight / 2f, r.FullWidth, r.FullHeight);
            if (Widgets.ImageButton(r, o.X + PageW - 30, ay))
            {
                audio?.Play(36);
                scroll -= HookStep * 3.25f;
                speed = 0;
            }
        }

        // the count over the locker's size
        Text.DrawAnchored(game.Player.Frozen.Count + data.Exe.Get(TextOutOf) + game.Player.LockerSize, o.X + CountX, o.Y + CountY,
            Text.GameFont[4], Anchor.Center, Color.White, Color.Black, 2);

        // the one picked: its name and stats
        if (picked >= 0 && picked < game.Player.Frozen.Count)
        {
            var c = game.Player.Frozen[picked];
            float max = c.MaxEnergy(data);
            string stats = data.Strings[TextTipRating] + c.TipRating + "\n" + data.Strings[TextAtkSpeed] + c.Speed + "\n"
                + data.Strings[TextAtkStrength] + c.Strength + "\n" + data.Strings[TextEnergy] + (int)c.Energy + data.Exe.Get(TextOutOf) + (int)max;
            Text.DrawAnchored(c.Name, o.X + 300, o.Y + 520, Text.GameFont[4], Anchor.Top | Anchor.HCenter, Color.White, Color.Black, 2);
            float ly = o.Y + 520 + Text.GameFont[4] + 8;
            foreach (var line in stats.Split('\n'))
            {
                Text.DrawAnchored(line, o.X + 300, ly, Text.GameFont[2], Anchor.Top | Anchor.HCenter, Color.White, Color.Black, 2);
                ly += Text.GameFont[2] + 4;
            }
        }

        // REVIVE and FIRE with one picked, ADD 5 HOOKS always (its price under it)
        reviveRect = fireRect = null;
        if (picked >= 0)
        {
            reviveRect = Button(data.Exe.Get(TextRevive), o.Y + ReviveY, Revive);
            fireRect = Button(data.Exe.Get(TextFire), o.Y + FireY, Fire);
        }
        addRect = Button(data.Exe.Get(TextAddHooks), o.Y + AddY, AddHooks);
        sheet[UiVial].Draw(o.X + VialX, o.Y + VialY, Anchor.Center, Color.White, Flip.None, 0.6f, 0.6f);
        Text.DrawAnchored(game.LockerCost.ToString(), o.X + VialX, o.Y + CostY, Text.GameFont[1], Anchor.Center, Color.White, Color.Black, 2);

        // BACK to the café
        var b = sheet[UiBack];
        backRect = new Rectangle(o.X + BackX - b.FullWidth, o.Y + BackY - b.FullHeight, b.FullWidth, b.FullHeight);
        if ((Widgets.ImageButton(b, backRect.Value.X + b.FullWidth / 2, backRect.Value.Y + b.FullHeight / 2)
                || (!modal && Input.IsKeyPressed(KeyboardKey.Escape))) && !left)
        {
            left = true;
            audio?.Play(40);
            back();
            return;
        }
        Widgets.Blocked = false;
        DrawBoxes();
    }

    Rectangle Button(string label, float y, Action press)
    {
        var img = sheet[UiButton];
        float x = Origin.X + ButtonX;
        var r = new Rectangle(x - img.FullWidth / 2f, y - img.FullHeight / 2f, img.FullWidth, img.FullHeight);
        if (Widgets.ImageButton(img, x, y))
            press();
        Text.DrawAnchored(label, x, y, Text.GameFont[2], Anchor.Center, Color.White, Color.Black, 2);
        return r;
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

    // ---------------------------------------------------------------- test scripts

    /// <summary>Named places: zombie0.., revive, fire, add, back, prev, next, msgok.</summary>
    Vector2? ScriptTarget(string name)
    {
        Vector2? Ui(Rectangle? r) => r is { } x ? Screen.ToPixelsPoint(new Vector2(x.X + x.Width / 2, x.Y + x.Height / 2)) : null;
        if (name.StartsWith("zombie") && int.TryParse(name[6..], out int i))
            return i < bodies.Count ? Screen.ToPixelsPoint(new Vector2(HookX(i), Origin.Y + BodyY - 60)) : null;
        return name switch
        {
            "revive" => Ui(reviveRect),
            "fire" => Ui(fireRect),
            "add" => Ui(addRect),
            "back" => Ui(backRect),
            "prev" => Ui(leftRect),
            "next" => Ui(rightRect),
            "msgok" => Ui(Widgets.LastMessageButton),
            _ => null,
        };
    }
}
