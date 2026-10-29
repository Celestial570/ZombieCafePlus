using System.Numerics;
using Raylib_cs;
using ZombieCafe.Assets;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// "Choose Your Chef" (GameStateChooseChef: load FUN_0012e7c0, layout 001311d4, update 00131be4). For a new
/// game it offers the free chef types (character flag +0x39 set, price +0x34 zero: the nine 'Chef' looks);
/// from the cafe (the chef's CHEF button, state +0xd8 set) it is the chef store: every type that may be a
/// chef, nine to a page, sorted by the level that unlocks them (+0x20), with the price of those not yet
/// bought in cash or toxin (+0x38). Faces are ChefHeadButtons on a note pinned to the brick wall; picking one
/// stands that chef on the chalkboard playing a showcase animation, with its stats and description
/// (FUN_0012feec), and "Select This Chef!" buys (if need be) and takes it (FUN_001329b0).
/// </summary>
public sealed class ChooseChefScreen : GameScreen
{
    // the iPad's numbers (device 0 of DAT_00375e58 .. DAT_003760c8)
    const float PaperX = 40, PaperY = 465;
    const float BoardRight = 984, BoardY = 450;
    const float NoteX = 286, NoteY = 730;
    const float BackX = 1000, BackY = 754;
    const float ToxinRight = 141, CashRight = 302, CounterY = 4;
    static readonly float[] Columns = [150, 280, 410], Rows = [360, 520, 680];
    const int PerPage = 9;
    // on the chalkboard: the chef's feet, the stats' top-left, where the description starts, its width
    const float ChefX = 285, ChefY = 242, StatsX = 32, StatsY = 110, DescTop = 242, DescWidth = 314;
    // ipad_menuImages
    const int MenuArrowRight = 0, MenuArrowLeft = 3, MenuWall = 4, MenuBack = 5, MenuBoard = 21, MenuPaper = 25,
        MenuPin = 26, MenuAwning = 29, MenuSelect = 37, MenuToxin = 38, MenuCash = 39, MenuToxinBig = 40,
        MenuCashBig = 47, MenuLock = 48;
    // strings.bin
    const int TextTitle = 46, TextNote = 47, TextSelect = 48, TextTipRating = 509, TextAtkSpeed = 510, TextAtkStrength = 511,
        TextEnergy = 512, TextCookSpeed = 515, TextTipMultiplier = 516, TextZombieRegen = 517, TextCookXp = 518,
        TextAskToxin = 317, TextToxinQ = 318, TextUseToxin = 319, TextNoToxin = 320, TextCancel = 316, TextOk = 362,
        TextNoCash = 436, TextDefeatToUnlock = 513, TextToxinStore = 443, TextAddToxin = 321;
    const int SoundTap = 36, SoundClick = 40;
    static readonly Color Maroon = Colors.FromArgb(unchecked((int)0xff6c1526)); // K 0x42d2a8

    readonly GameData data;
    readonly Audio.AudioSystem? audio;
    readonly Action<CharacterDef> chosen;
    readonly Action back;
    /// <summary>The chef store (from the cafe): the player buying; null for a new game's choice.</summary>
    readonly PlayerState? player;
    readonly List<CharacterDef> chefs;
    readonly Random rng = new();
    int selected = -1, page;
    bool left;
    float clock;

    // the chef on the chalkboard
    FlashAnimation? showcase;
    string stats = "", description = "";
    // a box over everything: a question (buy with toxin) or a message
    (string Text, string Yes, Action? OnYes)? box;

    /// <param name="chosen">"Select This Chef!": start the new game with this chef (or change to it).</param>
    /// <param name="back">BACK: return to where this came from.</param>
    /// <param name="store">The player, for the chef store reached from the cafe.</param>
    public ChooseChefScreen(GameData data, Audio.AudioSystem? audio, Action<CharacterDef> chosen, Action back, PlayerState? store = null)
    {
        this.data = data;
        this.audio = audio;
        this.chosen = chosen;
        this.back = back;
        player = store;
        if (store == null)
        {
            // load: types that may be chefs (+0x39), and in a new game only the ones that cost nothing (+0x34)
            chefs = data.Characters.Where(c => c.B39 != 0 && c.I34 == 0).Take(PerPage).ToList();
        }
        else
        {
            // the store: every chef type, by the level that unlocks it (FUN_0012fa24; specials, 255, last)
            chefs = data.Characters.Where(c => c.B39 != 0).OrderBy(c => c.B20 == 255 ? int.MaxValue : c.B20).ToList();
            // layout: open on the page of the last chef the player's level has reached
            int last = chefs.FindLastIndex(c => c.B20 <= store.Level);
            page = Math.Max(0, last) / PerPage;
        }
    }

    SubImage[] Menu => data.Sheet("ipad_menuImages", "ipad_menuOffsets.bin");
    int Pages => Math.Max(1, (chefs.Count + PerPage - 1) / PerPage);

    /// <summary>A store chef the player's level has not reached yet (or a boss chef not yet beaten).</summary>
    bool Locked(CharacterDef d) => player != null && d.B20 > player.Level && !player.UnlockedChefs.Contains(d.Index);
    /// <summary>A store chef with a price, not bought yet.</summary>
    bool ForSale(CharacterDef d) => player != null && d.I34 > 0 && !player.OwnedChefs.Contains(d.Index) && player.Chef != d;

    // the 1024 x 768 layout, centred
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
        clock += dt;
        showcase?.Update((int)(dt * 1000));
    }

    /// <summary>FUN_0012feec: show this chef on the chalkboard.</summary>
    void Select(int i)
    {
        selected = i;
        var def = chefs[i];
        var who = new Character(def, Role.Staff, def.Kind == 1, def.Name);
        // the stats as the original lists them, with the type's bonuses when it has any
        var lines = new List<string>
        {
            data.Strings[TextTipRating] + who.TipRating,
            data.Strings[TextAtkSpeed] + who.Speed,
            data.Strings[TextAtkStrength] + who.Strength,
            data.Strings[TextEnergy] + (int)who.MaxEnergy(data),
        };
        if (def.F40 != 1f)
            lines.Add(data.Strings[TextCookSpeed] + "+" + (int)((1f - MathF.Round(100f / def.F40) / 100f) * 100f) + "%");
        if (def.F4C != 1f)
            lines.Add(data.Strings[TextCookXp] + "+" + (int)(def.F4C * 100f - 100f) + "%");
        if (def.I44 != 1)
            lines.Add(data.Strings[TextTipMultiplier] + def.I44);
        if (def.F48 != 1f)
            lines.Add(data.Strings[TextZombieRegen] + "+" + (int)(def.F48 * 100f - 100f) + "%");
        stats = string.Join("\n", lines);
        // the human or the zombie blurb, as the chef will be (+0x2c / +0x30)
        description = who.IsZombie ? def.ZombieDescription : def.HumanDescription;

        // a random one of the showcase animations (K 0x42d214), looping
        var anims = data.K.Ints(0x42d214);
        int count = Math.Min(anims.Length, data.K.Int(0x42d218));
        int animId = count > 0 ? anims[rng.Next(count)] : (int)CharAnim.Idle;
        var (file, mirrored) = data.Animations.Get(animId, (int)Facing.SW);
        var name = def.PartsSheet(who.IsZombie);
        showcase = file == null ? null : new FlashAnimation(file, data.Sheet(name, name + ".bin")) { FlipX = mirrored };
        if (showcase != null)
        {
            if (animId is (int)CharAnim.Walk or (int)CharAnim.Run or (int)CharAnim.Walk2 or (int)CharAnim.Walk3 or (int)CharAnim.Walk4 or (int)CharAnim.Carry)
                showcase.SetRate(who.SpeedFactor(data));
            showcase.Reset();
            showcase.Play();
        }
    }

    /// <summary>update, "Select This Chef!": a chef for sale is paid for first (with a word if it cannot be).</summary>
    void Confirm()
    {
        var d = chefs[selected];
        if (player == null || !ForSale(d))
        {
            Take(d);
            return;
        }
        if (d.B38 == 0)
        {
            if (d.I34 <= player.Money)
                Take(d);
            else
                box = (data.Strings[TextNoCash], data.Strings[TextOk], null);
        }
        else if (player.Toxin < d.I34)
        {
            // "Add Toxin" opens the toxin store, which needed the original's servers: it says so (string 443)
            box = (data.Strings[TextNoToxin], data.Strings[TextAddToxin],
                () => box = (data.Strings[TextToxinStore], data.Strings[TextOk], null));
        }
        else
        {
            box = (data.Strings[TextAskToxin] + d.I34 + data.Strings[TextToxinQ], data.Strings[TextUseToxin], () => Take(d));
        }
    }

    /// <summary>FUN_001329b0: pay for the chef if it is for sale, and go with it.</summary>
    void Take(CharacterDef d)
    {
        if (left)
            return;
        if (player != null && ForSale(d))
        {
            if (d.B38 == 0)
                player.Money -= d.I34;
            else
                player.Toxin -= d.I34;
            player.OwnedChefs.Add(d.Index);
        }
        left = true;
        audio?.Play(SoundClick);
        chosen(d);
    }

    public override void Draw()
    {
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        var menu = Menu;
        if (menu.Length <= MenuLock)
            return;
        Widgets.Blocked = box != null;

        // the brick wall fills the window; the awning runs along the top
        var wall = menu[MenuWall];
        float cover = MathF.Max(Screen.Width / wall.FullWidth, Screen.Height / wall.FullHeight);
        wall.Draw(Screen.Width / 2, Screen.Height / 2, Anchor.Center, Color.White, Flip.None, cover, cover);
        var awning = menu[MenuAwning];
        float aw = awning.FullWidth * scale;
        for (float x = origin.X - MathF.Ceiling(origin.X / aw) * aw; x < Screen.Width; x += aw)
            awning.Draw(x, 0, Anchor.Top | Anchor.Left, Color.White, Flip.None, scale, scale);
        if (player != null)
            DrawCounters(menu);

        // the note with the faces, and the chalkboard
        var paper = menu[MenuPaper];
        var paperAt = P(PaperX, PaperY);
        paper.Draw(paperAt.X, paperAt.Y, Anchor.VCenter | Anchor.Left, Color.White, Flip.None, scale, scale);
        var board = menu[MenuBoard];
        var boardAt = P(BoardRight, BoardY);
        board.Draw(boardAt.X, boardAt.Y, Anchor.VCenter | Anchor.Right, Color.White, Flip.None, scale, scale);
        var boardTopLeft = new Vector2(BoardRight - board.FullWidth, BoardY - board.FullHeight / 2f);
        Vector2 OnBoard(float x, float y) => P(boardTopLeft.X + x, boardTopLeft.Y + y);

        // the title under the awning (the iPad's only), the note's line, the pin
        var title = new Vector2(Screen.Width / 2, origin.Y + awning.FullHeight * scale);
        Text.DrawAnchored(data.Strings[TextTitle], title.X, title.Y, Text.GameFont[6] * scale, Anchor.Top | Anchor.HCenter, Maroon);
        var note = P(NoteX, NoteY);
        Text.DrawAnchored(data.Strings[TextNote], note.X, note.Y, Text.GameFont[3] * scale, Anchor.Top | Anchor.HCenter, Maroon);
        var pin = menu[MenuPin];
        var pinAt = P(PaperX + paper.FullWidth / 2f, PaperY - paper.FullHeight / 2f - pin.FullHeight / 4f);
        pin.Draw(pinAt.X, pinAt.Y, Anchor.Top | Anchor.HCenter, Color.White, Flip.None, scale, scale);

        // the page arrows at the note's sides, nudging back and forth
        float nudge = MathF.Abs(MathF.Sin(clock * 3f)) * 4 * scale;
        if (page > 0)
        {
            var at = P(PaperX, PaperY);
            if (Widgets.ImageButton(menu[MenuArrowLeft], at.X - nudge, at.Y, scale) && !left)
            {
                audio?.Play(SoundTap);
                page--;
            }
        }
        if (page < Pages - 1)
        {
            var arrow = menu[MenuArrowRight];
            var at = P(PaperX + paper.FullWidth - arrow.FullWidth / 2f, PaperY);
            if (Widgets.ImageButton(arrow, at.X + nudge, at.Y, scale) && !left)
            {
                audio?.Play(SoundTap);
                page++;
            }
        }

        var backImg = menu[MenuBack];
        var backAt = P(BackX - backImg.FullWidth / 2f, BackY - backImg.FullHeight / 2f);
        if ((Widgets.ImageButton(backImg, backAt.X, backAt.Y, scale) || (box == null && Input.IsKeyPressed(KeyboardKey.Escape))) && !left)
        {
            left = true;
            audio?.Play(SoundClick);
            back();
            return;
        }

        // the faces (ChefHeadButton: part 16 of each chef's parts sheet), three by three
        for (int k = 0; k < PerPage; k++)
        {
            int i = page * PerPage + k;
            if (i >= chefs.Count)
                break;
            DrawHead(menu, i, P(Columns[k % 3], Rows[k / 3]));
        }

        if (selected >= 0)
        {
            // the chosen chef on the chalkboard, its stats beside it and its blurb below
            var feet = OnBoard(ChefX, ChefY);
            showcase?.Draw(feet.X, feet.Y, scale, Color.White);
            var statsAt = OnBoard(StatsX, StatsY);
            float lineH = Text.GameFont[2] * scale * 1.15f;
            var statLines = stats.Split('\n');
            for (int k = 0; k < statLines.Length; k++)
                Text.Draw(statLines[k], statsAt.X, statsAt.Y + k * lineH, Text.GameFont[2] * scale, Color.White);

            var select = menu[MenuSelect];
            float selectBottom = board.FullHeight;
            float selectTop = selectBottom - select.FullHeight;
            float descY = DescTop + (selectTop - DescTop) / 2;
            var desc = Widgets.WrapText(description, DescWidth * scale, Text.GameFont[2] * scale);
            var descAt = OnBoard(board.FullWidth / 2f, descY);
            float top = descAt.Y - desc.Count * lineH / 2;
            for (int k = 0; k < desc.Count; k++)
                Text.DrawAnchored(desc[k], descAt.X, top + k * lineH, Text.GameFont[2] * scale, Anchor.Top | Anchor.HCenter, Color.White);

            var selectAt = OnBoard(board.FullWidth / 2f, selectBottom - select.FullHeight / 2f);
            bool picked = Widgets.ImageButton(select, selectAt.X, selectAt.Y, scale);
            Text.DrawAnchored(data.Strings[TextSelect], selectAt.X, selectAt.Y, Text.GameFont[3] * scale, Anchor.Center, Color.White);
            if (picked && !left)
                Confirm();
        }

        Widgets.Blocked = false;
        if (box is { } b)
        {
            if (b.OnYes == null)
            {
                if (Widgets.MessageBox("", [b.Text], b.Yes))
                    box = null;
            }
            else
            {
                int answer = Widgets.QuestionBox(b.Text, b.Yes, data.Strings[TextCancel]);
                if (answer != 0)
                {
                    box = null;
                    if (answer == 1)
                        b.OnYes();
                }
            }
        }
    }

    /// <summary>The store's cash and toxin at the top right (+0xe4 / +0xf4 and +0xec / +0xf0).</summary>
    void DrawCounters(SubImage[] menu)
    {
        float right = Screen.Width / scale;
        void Counter(SubImage icon, float fromRight, int amount)
        {
            var at = new Vector2((right - fromRight) * scale, CounterY * scale);
            icon.Draw(at.X, at.Y, Anchor.Top | Anchor.Right, Color.White, Flip.None, scale, scale);
            Text.DrawAnchored(amount.ToString("N0"), at.X + 6 * scale, at.Y + icon.FullHeight * scale / 2, Text.GameFont[4] * scale,
                Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        }
        Counter(menu[MenuCashBig], CashRight, player!.Money);
        Counter(menu[MenuToxinBig], ToxinRight, player.Toxin);
    }

    /// <summary>ChefHeadButton (FUN_0018e294 / vf03): the face, dimmed with a lock while out of reach, with its price.</summary>
    void DrawHead(SubImage[] menu, int i, Vector2 c)
    {
        var def = chefs[i];
        var head = Head(def);
        if (head == null)
            return;
        bool locked = Locked(def);
        var r = new Rectangle(c.X - head.FullWidth * scale / 2, c.Y - head.FullHeight * scale / 2, head.FullWidth * scale, head.FullHeight * scale);
        bool over = !locked && Widgets.Hover(r);
        float s = scale * (over ? 1.05f : 1f);
        head.Draw(c.X, c.Y, Anchor.Center, locked ? new Color(255, 255, 255, 0x60) : Color.White, Flip.None, s, s);
        if (locked)
        {
            var lockImg = menu[MenuLock];
            lockImg.Draw(c.X, c.Y, Anchor.Center, Color.White, Flip.None, scale, scale);
            if (def.B20 != 255)
            {
                // the level that unlocks it, in the white digits (16 px, K 0x42d09c) on the lock's body
                float top = c.Y - lockImg.FullHeight * scale / 2 + lockImg.FullHeight * scale * 2 / 3;
                Text.DrawAnchored(def.B20.ToString(), c.X, top, 16 * scale, Anchor.VCenter | Anchor.HCenter, Color.White);
            }
            else
            {
                // a boss chef: how it is won, across the face (black, wrapped to 85 points)
                string why = def.B50 ? def.ZombieDescription : data.Strings[TextDefeatToUnlock];
                float size = Text.GameFont[2] * scale;
                var lines = Widgets.WrapText(why, 85 * scale, size);
                float y = c.Y - lines.Count * size / 2;
                for (int k = 0; k < lines.Count; k++)
                    Text.DrawAnchored(lines[k], c.X, y + k * size, size, Anchor.Top | Anchor.HCenter, Color.Black);
            }
        }
        else if (ForSale(def))
        {
            var icon = menu[def.B38 == 0 ? MenuCash : MenuToxin];
            string price = def.I34.ToString();
            float size = Text.GameFont[2] * scale;
            float w = icon.FullWidth * scale * 0.7f + 4 * scale + Text.Measure(price, size).W;
            float x = c.X - w / 2, y = r.Y + r.Height + icon.FullHeight * scale * 0.35f;
            icon.Draw(x, y, Anchor.Left | Anchor.VCenter, Color.White, Flip.None, scale * 0.7f, scale * 0.7f);
            Text.DrawAnchored(price, x + icon.FullWidth * scale * 0.7f + 4 * scale, y, size, Anchor.Left | Anchor.VCenter, Color.Black);
        }
        if (!locked && Widgets.Clicked(r) && !left)
        {
            audio?.Play(SoundTap);
            Select(i);
        }
    }

    SubImage? Head(CharacterDef def)
    {
        var name = def.PartsSheet(def.Kind == 1);
        var parts = data.Sheet(name, name + ".bin");
        int i = data.K.Int(0x42d618);
        return i < parts.Length ? parts[i] : null;
    }

    /// <summary>Test scripts: "chef0".."chef8" (on the page shown), "select", "back", "next", "prev", "yes".</summary>
    Vector2? ScriptTarget(string name)
    {
        var menu = Menu;
        if (menu.Length <= MenuLock)
            return null;
        if (name.StartsWith("chef") && int.TryParse(name[4..], out int i) && i < PerPage && page * PerPage + i < chefs.Count)
            return Screen.ToPixelsPoint(P(Columns[i % 3], Rows[i / 3]));
        if (name == "select" && selected >= 0)
        {
            var board = menu[MenuBoard];
            return Screen.ToPixelsPoint(P(BoardRight - board.FullWidth / 2f, BoardY + board.FullHeight / 2f - menu[MenuSelect].FullHeight / 2f));
        }
        if (name == "back")
            return Screen.ToPixelsPoint(P(BackX - menu[MenuBack].FullWidth / 2f, BackY - menu[MenuBack].FullHeight / 2f));
        if (name == "prev")
            return Screen.ToPixelsPoint(P(PaperX, PaperY));
        if (name == "next")
            return Screen.ToPixelsPoint(P(PaperX + menu[MenuPaper].FullWidth - menu[MenuArrowRight].FullWidth / 2f, PaperY));
        return null;
    }
}
