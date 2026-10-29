using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// GameStateZombiePedia (load FUN_0013f344, layout FUN_0014032c, update FUN_00140ea4, a zombie's page FUN_00141570):
/// every kind of customer that can be made a zombie, on the paper page of the ZombiePedia's own sheet - the FEATURED
/// four (types 301 .. 304, FUN_0013f130), the THEMED collections a page each (the names of DAT_0042cd38 but the
/// first, in alphabetical order; a type's collection is its +0x54), or ALL of them by level - eight to a page, four
/// by two. A card (FUN_001e8d90 / FUN_001e9328) shows the zombie standing in its frame with its name: in colour with
/// a tick once the player has had one on the staff (game +0x34), grey until then (texture mode 1), a black shape with
/// what it takes ("UNLOCK AT :", the player's level, the café's rating in stars; mode 2) while the player's level or
/// rating is short of it. A card opens the zombie's page - its stats and bonuses, what it takes and what it costs;
/// the arrows turn the pages (on the themed tab, the collections), the X goes back to the café (sound 40).
/// </summary>
public sealed class ZombiePediaScreen : GameScreen
{
    // ipad_ZombiePediaUi
    const int UiArrowLeft = 0, UiArrowRight = 1, UiPage = 2, UiCard = 3, UiDetail = 4, UiClose = 8, UiLock = 9, UiTick = 15;
    static readonly int[] TabOn = [5, 6, 7], TabOff = [13, 14, 12];
    // ipad_ingameUi: the cost's toxin and cash
    const int UiToxin = 36, UiCash = 29;
    // the code's own words
    const uint TextFeatured = 0x342527, TextAll = 0x34254a, TextThemed = 0x342568, TextCollection = 0x34257b,
        TextUnlockAt = 0x344cfd, TextPlayerLevel = 0x344d0f, TextLevelRequired = 0x34258a, TextRequired = 0x3425a6,
        TextCost = 0x3425b3, TextNewLine = 0x33a86d;
    // strings.bin: the stats, the bonuses, the café's rating
    const int TextTipRating = 509, TextAtkSpeed = 510, TextAtkStrength = 511, TextEnergy = 512, TextCookSpeed = 515,
        TextTipMultiplier = 516, TextRegen = 517, TextCookXp = 518, TextRating = 658;
    /// <summary>FUN_0013f130: the featured four.</summary>
    static readonly int[] Featured = [301, 302, 303, 304];
    /// <summary>DAT_0042cd38: the collections' names, kept in the code (the first is none).</summary>
    static readonly uint[] CollectionNames =
    [
        0x3415c5, 0x3415cc, 0x3415d3, 0x3415d9, 0x3415e3, 0x3415f3, 0x3415fb, 0x341604, 0x34160b, 0x341613, 0x34161e,
        0x341626, 0x34162c, 0x341635, 0x34163c, 0x341645, 0x34164b, 0x34165f, 0x341674, 0x34167e, 0x341684, 0x34168c,
        0x34169a, 0x3416a5, 0x3416ab, 0x3416bd, 0x3416c9,
    ];
    /// <summary>DAT_00376538: what a zombie on its own page does, one picked at random.</summary>
    static readonly int[] PageAnims = [0, 1, 2, 3, 4, 6, 7, 8, 9, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21, 24];
    // the iPad's layout: cards (DAT_00376418 / DAT_00376478, their frames standing on these points), tabs
    // (DAT_003762e0 ..), arrows (DAT_003763d0 ..), the X (DAT_00376370 ..), the heading (DAT_00376340 ..)
    static readonly float[] CardX = [180, 380, 580, 780], CardY = [420, 720], TabY = [38, 270, 488];
    const float TabX = 910, ArrowLeftX = 20, ArrowRightX = 920, ArrowY = 384, CloseX = 930, CloseY = 35, TitleX = 100, TitleY = 110;
    const float PageW = 1024, PageH = 768;

    readonly GameData data;
    readonly CafeController game;
    readonly Audio.AudioSystem? audio;
    readonly Action back;
    readonly SubImage[] sheet;
    /// <summary>+0x120: every kind there is, by level.</summary>
    readonly List<int> all = new();
    /// <summary>+0x130: the collections in alphabetical order.</summary>
    readonly List<int> collections = new();
    int tab;           // +0x14: 0 featured, 1 themed, 2 all
    int page;          // +0xe5 (+0x18 on the themed tab: the collection)
    readonly Card?[] cards = new Card?[8];   // +0xec
    Page? open;        // +0xe4 / +0xe8
    float bob;
    bool left;
    Rectangle? closeRect, prevRect, nextRect, pageCloseRect;
    readonly Rectangle?[] tabRects = new Rectangle?[3], cardRects = new Rectangle?[8];

    /// <summary>ZombiePediaItem (0x94 bytes): one kind in its frame.</summary>
    sealed class Card
    {
        public required CharacterDef Def;
        public bool Had, Locked;
        public FlashAnimation? Body;
        public string? Unlock;
    }

    /// <summary>The page FUN_00141570 builds for one kind.</summary>
    sealed class Page
    {
        public required CharacterDef Def;
        public FlashAnimation? Body;
        public required string Stats, Needs, Cost;
        public bool Toxin;
    }

    public long ElapsedMs { get; private set; }

    public ZombiePediaScreen(GameData data, CafeController game, Audio.AudioSystem? audio, Action back)
    {
        this.data = data;
        this.game = game;
        this.audio = audio;
        this.back = back;
        sheet = data.Sheet("ipad_ZombiePediaUiImages", "ipad_ZombiePediaUiOffsets.bin");
        // GameStateZombiePedia::load: the kinds that come as customers and have a level, by level (a bubble sort,
        // FUN_0013fd10)
        for (int i = 0; i < data.Characters.Count; i++)
            if (data.Characters[i].B04 != 0 && data.Characters[i].B20 != 0xff)
                all.Add(i);
        all = all.OrderBy(i => data.Characters[i].B20).ToList();
        // FUN_000e01e4: the collections but the first, by name
        collections = Enumerable.Range(1, CollectionNames.Length - 1)
            .OrderBy(i => data.Exe.Get(CollectionNames[i]), StringComparer.Ordinal).ToList();
        Turn(0);
    }

    public override void Layout() => Input.TargetToPixels = ScriptTarget;

    // ---------------------------------------------------------------- the pages (FUN_0014032c)

    /// <summary>The kinds on the current page: the featured four, the collection's, or eight of all of them.</summary>
    IEnumerable<int> Kinds() => tab switch
    {
        0 => Featured.Where(i => i < data.Characters.Count),
        1 => all.Where(i => data.Characters[i].B54 == collections[page]).Take(8),
        _ => all.Skip(page * 8).Take(8),
    };

    bool HasNext => tab switch
    {
        1 => page < collections.Count - 1,
        2 => page * 8 + 8 < all.Count,
        _ => false,
    };

    /// <summary>FUN_0014032c: the page's cards made afresh.</summary>
    void Turn(int to)
    {
        page = to;
        Array.Clear(cards);
        int k = 0;
        foreach (int i in Kinds())
            cards[k++] = MakeCard(data.Characters[i]);
    }

    /// <summary>
    /// FUN_001e9328: the card's zombie - had (game +0x34) or not, locked (FUN_00030a3c: the player's level under the
    /// kind's, or the café's rating under its) - standing facing SW, with what unlocks it written out when locked.
    /// </summary>
    Card MakeCard(CharacterDef def)
    {
        var p = game.Player;
        bool had = p.ZombieKinds.Contains(def.Index);
        bool locked = def.B20 > p.Level || def.F00 > p.EffectiveRating;
        string? unlock = null;
        if (locked)
        {
            unlock = data.Exe.Get(TextUnlockAt);
            if (p.Level < def.B20)
                unlock += data.Exe.Get(TextPlayerLevel) + def.B20 + "\n ";
            unlock += data.Strings[TextRating] + " : " + (int)(def.F00 * 5f);
        }
        return new Card { Def = def, Had = had, Locked = locked, Unlock = unlock, Body = Standing(def, !had && !locked, 1) };
    }

    /// <summary>The zombie of a kind (FUN_001e99e8: a staff zombie's look) playing one of its animations, facing SW.</summary>
    FlashAnimation? Standing(CharacterDef def, bool grey, int anim)
    {
        string name = def.PartsSheet(true);
        if (!data.Textures.Exists(name + ".cct"))
            name = def.PartsSheet(false);
        var parts = grey ? data.GreySheet(name, name + ".bin") : data.Sheet(name, name + ".bin");
        var (file, mirrored) = data.Animations.Get(anim, (int)Facing.SW);
        if (file == null || parts.Length == 0)
            return null;
        var a = new FlashAnimation(file, parts) { FlipX = mirrored };
        a.Play();
        return a;
    }

    // ---------------------------------------------------------------- a kind's own page (FUN_00141570)

    /// <summary>
    /// FUN_00141570: the kind's page - the zombie doing something at random, its name, its stats (tip rating, attack
    /// speed and strength, energy) and any bonus it brings (cook speed, cook experience, tip multiplier, rest), what it
    /// says about itself; the player's level and the café's rating it takes; what it costs, in cash or toxin.
    /// </summary>
    void Open(CharacterDef def)
    {
        var z = new Character(def, Role.Staff, true, def.Name);
        string nl = data.Exe.Get(TextNewLine);
        string stats = data.Strings[TextTipRating] + z.TipRating + nl + data.Strings[TextAtkSpeed] + z.Speed + nl
            + data.Strings[TextAtkStrength] + z.Strength + nl + data.Strings[TextEnergy] + (int)z.MaxEnergy(data);
        if (def.F40 != 1f)
            stats += nl + data.Strings[TextCookSpeed] + "+" + (int)((1f - (int)(1f / def.F40 * 100f) / 100f) * 100f) + "%";
        if (def.F4C != 1f)
            stats += nl + data.Strings[TextCookXp] + "+" + (int)(def.F4C * 100f - 100f) + "%";
        if (def.I44 != 1)
            stats += nl + data.Strings[TextTipMultiplier] + def.I44 + "x";
        if (def.F48 != 1f)
            stats += nl + data.Strings[TextRegen] + "+" + (int)(def.F48 * 100f - 100f) + "%";
        stats += "\n " + def.ZombieDescription;
        string needs = data.Exe.Get(TextLevelRequired) + def.B20 + "\n\n" + data.Strings[TextRating]
            + data.Exe.Get(TextRequired) + (int)(def.F00 * 5f);
        int anim = PageAnims[game.Cafe.Rng.Next(PageAnims.Length)];
        open = new Page
        {
            Def = def, Stats = stats, Needs = needs, Cost = data.Exe.Get(TextCost) + def.I34, Toxin = def.B38 != 0,
            Body = Standing(def, false, anim),
        };
    }

    // ---------------------------------------------------------------- update (GameStateZombiePedia::update)

    public override void Update(float dt)
    {
        int ms = Math.Max(1, (int)MathF.Round(dt * 1000f));
        ElapsedMs += ms;
        // ZombieCafe::update runs under every screen: the rivals' clocks
        game.Player.Neighbours.Update(data, game.Player, ms, game.Cafe.Rng);
        bob += ms;
        foreach (var c in cards)
            c?.Body?.Update(ms);
        open?.Body?.Update(ms);
    }

    // ---------------------------------------------------------------- draw

    Vector2 Origin => new((Screen.Width - PageW) / 2, (Screen.Height - PageH) / 2);

    public override void Draw()
    {
        Raylib.ClearBackground(new Color(30, 24, 20, 255));
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        var o = Origin;
        if (sheet.Length > UiPage)
            sheet[UiPage].Draw(o.X, o.Y, Anchor.Top | Anchor.Left, Color.White);
        Widgets.Blocked = open != null;

        // the heading: which book this is
        string heading = tab switch
        {
            0 => data.Exe.Get(TextFeatured),
            1 => data.Exe.Get(TextThemed) + data.Exe.Get(CollectionNames[collections[page]]) + data.Exe.Get(TextCollection),
            _ => data.Exe.Get(TextAll),
        };
        Text.DrawAnchored(heading, o.X + TitleX, o.Y + TitleY, Text.GameFont[5], Anchor.Left | Anchor.VCenter, Color.Black);

        // the tabs down the right (the chosen one white)
        for (int t = 0; t < 3; t++)
        {
            var img = sheet[t == tab ? TabOn[t] : TabOff[t]];
            var r = new Rectangle(o.X + TabX, o.Y + TabY[t], img.FullWidth, img.FullHeight);
            tabRects[t] = r;
            img.Draw(r.X, r.Y, Anchor.Top | Anchor.Left, Color.White);
            if (Widgets.Clicked(r) && tab != t && open == null)
            {
                audio?.Play(36);
                tab = t;
                Turn(0);
            }
        }

        // the cards, four by two
        for (int k = 0; k < cards.Length; k++)
        {
            cardRects[k] = null;
            if (cards[k] is not { } c)
                continue;
            float x = o.X + CardX[k % 4], y = o.Y + CardY[k / 4];
            DrawCard(c, x, y, k);
        }

        // the arrows, bobbing
        float swing = MathF.Abs(MathF.Sin(bob / 300f)) * 6f;
        prevRect = nextRect = null;
        if (page > 0 && tab != 0)
        {
            var img = sheet[UiArrowLeft];
            prevRect = new Rectangle(o.X + ArrowLeftX - swing, o.Y + ArrowY - img.FullHeight * 0.6f, img.FullWidth * 1.2f, img.FullHeight * 1.2f);
            if (Widgets.ImageButton(img, prevRect.Value.X + prevRect.Value.Width / 2, o.Y + ArrowY, 1.2f))
            {
                audio?.Play(36);
                Turn(page - 1);
            }
        }
        if (HasNext)
        {
            var img = sheet[UiArrowRight];
            nextRect = new Rectangle(o.X + ArrowRightX - 40 + swing, o.Y + ArrowY - img.FullHeight * 0.6f, img.FullWidth * 1.2f, img.FullHeight * 1.2f);
            if (Widgets.ImageButton(img, nextRect.Value.X + nextRect.Value.Width / 2, o.Y + ArrowY, 1.2f))
            {
                audio?.Play(36);
                Turn(page + 1);
            }
        }

        // the X: back to the cafe
        var close = sheet[UiClose];
        closeRect = new Rectangle(o.X + CloseX - close.FullWidth * 0.3f, o.Y + CloseY - close.FullHeight * 0.3f, close.FullWidth * 0.6f, close.FullHeight * 0.6f);
        if ((Widgets.ImageButton(close, o.X + CloseX, o.Y + CloseY, 0.6f) || (open == null && Input.IsKeyPressed(KeyboardKey.Escape))) && !left)
        {
            left = true;
            audio?.Play(40);
            back();
            return;
        }

        Widgets.Blocked = false;
        if (open != null)
            DrawPage(open, o);
    }

    /// <summary>A card: its frame standing on (x, y), the zombie in it, its name, and its tick or what unlocks it.</summary>
    void DrawCard(Card c, float x, float y, int k)
    {
        var frame = sheet[UiCard];
        float top = y - frame.FullHeight;
        var r = new Rectangle(x - frame.FullWidth / 2f, top, frame.FullWidth, frame.FullHeight);
        cardRects[k] = r;
        bool over = Widgets.Hover(r) && open == null;
        frame.Draw(x, y, Anchor.Bottom | Anchor.HCenter, over ? Color.White : new Color(235, 235, 235, 255));
        // the zombie stands in the frame's dark window (a black shape while locked)
        c.Body?.Draw(x, top + frame.FullHeight * 0.66f, 0.9f, c.Locked ? Color.Black : Color.White);
        // its name on the strip at the foot of the card
        var lines = Widgets.WrapText(c.Def.Name, frame.FullWidth * 0.9f, Text.GameFont[2]);
        for (int i = 0; i < lines.Count; i++)
            Text.DrawAnchored(lines[i], x, y - 34 + (i - (lines.Count - 1) / 2f) * Text.GameFont[2], Text.GameFont[2], Anchor.Center, Color.Black);
        if (c.Had && sheet.Length > UiTick)
            sheet[UiTick].Draw(r.X + r.Width - 26, top + 30, Anchor.Center, Color.White);
        if (c.Unlock != null)
        {
            if (sheet.Length > UiLock)
                sheet[UiLock].Draw(x, top + frame.FullHeight * 0.36f, Anchor.Center, new Color(255, 255, 255, 200), Flip.None, 0.45f, 0.45f);
            var lines2 = c.Unlock.Split('\n');
            float ly = top + 30;
            foreach (var l in lines2)
            {
                if (l.Trim().Length > 0)
                    Text.DrawAnchored(l.Trim(), x, ly, Text.GameFont[0], Anchor.Top | Anchor.HCenter, Color.White, Color.Black, 2);
                ly += Text.GameFont[0] * 0.8f;
            }
        }
        if (over && Widgets.Clicked(r))
        {
            audio?.Play(36);
            Open(c.Def);
        }
    }

    /// <summary>The kind's page over the book: the frame, the zombie, its name, stats, needs and cost, and its X.</summary>
    void DrawPage(Page p, Vector2 o)
    {
        var frame = sheet[UiDetail];
        float cx = o.X + PageW / 2, cy = o.Y + PageH / 2;
        var r = new Rectangle(cx - frame.FullWidth / 2f, cy - frame.FullHeight / 2f, frame.FullWidth, frame.FullHeight);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 110));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        frame.Draw(cx, cy, Anchor.Center, Color.White);
        // the zombie in the window on the left
        p.Body?.Draw(r.X + r.Width * 0.18f, r.Y + r.Height * 0.52f, 1f, Color.White);
        // its name, and its stats and what it says about itself on the right
        float tx = r.X + r.Width / 2.2f, tw = r.Width - r.Width / 2.2f - 24;
        Text.DrawAnchored(p.Def.Name, tx + tw / 2, r.Y + r.Height / 7f, Text.GameFont[3], Anchor.Center, Color.Black);
        float ly = r.Y + r.Height / 5f;
        foreach (var para in p.Stats.Split('\n'))
            foreach (var line in Widgets.WrapText(para.Trim(), tw, Text.GameFont[1]))
            {
                Text.Draw(line, tx, ly, Text.GameFont[1], Color.Black);
                ly += Text.GameFont[1] + 3;
            }
        // what it takes, and what it costs
        float ny = r.Y + r.Height * 0.62f;
        foreach (var line in p.Needs.Split('\n'))
        {
            if (line.Length > 0)
                Text.DrawAnchored(line, r.X + r.Width * 0.22f, ny, Text.GameFont[1], Anchor.Top | Anchor.HCenter, Color.White, Color.Black, 2);
            ny += Text.GameFont[1] * 0.7f;
        }
        // "Cost :", the cash or toxin in the gap its spaces leave, the price
        float costY = r.Y + r.Height * 0.81f, costX = r.X + r.Width * 0.07f;
        string label = data.Exe.Get(TextCost).TrimEnd();
        Text.DrawAnchored(label, costX, costY, Text.GameFont[2], Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        var icon = data.IngameUi[p.Toxin ? UiToxin : UiCash];
        float ix = costX + Text.Measure(label, Text.GameFont[2]).W + 10 + icon.FullWidth * 0.375f;
        icon.Draw(ix, costY, Anchor.Center, Color.White, Flip.None, 0.75f, 0.75f);
        Text.DrawAnchored(p.Cost[data.Exe.Get(TextCost).Length..], ix + icon.FullWidth * 0.375f + 8, costY, Text.GameFont[2],
            Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        // FUN_0013ffe4: the X closes the page
        var x = data.IngameUi[UiSprites.CloseX];
        float xx = r.X + r.Width - 24, xy = r.Y + 24;
        pageCloseRect = new Rectangle(xx - x.FullWidth / 2f, xy - x.FullHeight / 2f, x.FullWidth, x.FullHeight);
        if (Widgets.ImageButton(x, xx, xy) || Input.IsKeyPressed(KeyboardKey.Escape))
        {
            audio?.Play(36);
            open = null;
        }
    }

    // ---------------------------------------------------------------- test scripts

    /// <summary>Named places: featured, themed, all (tabs), prev, next, card0..card7, close, pageclose.</summary>
    Vector2? ScriptTarget(string name)
    {
        Vector2? Ui(Rectangle? r) => r is { } x ? Screen.ToPixelsPoint(new Vector2(x.X + x.Width / 2, x.Y + x.Height / 2)) : null;
        if (name.StartsWith("card") && int.TryParse(name[4..], out int k))
            return k < cardRects.Length ? Ui(cardRects[k]) : null;
        return name switch
        {
            "featured" => Ui(tabRects[0]),
            "themed" => Ui(tabRects[1]),
            "all" => Ui(tabRects[2]),
            "prev" => Ui(prevRect),
            "next" => Ui(nextRect),
            "close" => Ui(closeRect),
            "pageclose" => Ui(pageCloseRect),
            _ => null,
        };
    }
}
