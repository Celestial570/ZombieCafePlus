using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;

namespace ZombieCafe.Ui;

/// <summary>
/// The recipe book an empty stove opens (RecipeBook, GameStateCafe +0xc4): the dialog sheet's book page
/// (ipad_dialogImages #0) with the stove's cookbooks along the chalkboard (their covers from the CookBookUi sheet: the
/// favourites and, for an ordinary stove, the General, the Raid, the Pirate and the Vampire; a special stove's own),
/// a row of the open book's recipes, and the chosen one on the page below with its cost, time, takings and
/// experience, its ten variants (plain, Spicy .. Very Quick: in-game UI icons DAT_003741c0, dark ones still locked
/// DAT_003741f0), its star (the favourites, game +0x68), and COOK, or UNLOCK for toxin (FUN_001038d4). Recipes come
/// one per level (FUN_000d1f20); variants and missed recipes can be bought; a book on sale is bought for its cash or
/// toxin once the player's level allows (FUN_00101af0 / FUN_00101f78).
/// </summary>
public sealed class RecipeBook
{
    // DAT_003741c0 / DAT_003741f0: a variant's icon, and its dark (locked) version
    static readonly int[] VariantIcons = [133, 63, 67, 57, 65, 56, 58, 59, 61, 66];
    static readonly int[] LockedIcons = [153, 145, 151, 137, 147, 135, 139, 141, 143, 149];
    const int CardsPerPage = 5;
    const int UiUnlock = 188, UiCard = 163, UiStar = 201;
    // the code's own words: a book's level, its question, the money short; the favourites with none here
    const uint TextBookLevel = 0x341a7f, TextBuyBook = 0x341954, TextNoMoney = 0x341987, TextNoFavourites = 0x341a05,
        TextNoFavouritesHere = 0x341a16;
    static readonly Color Ink = new(70, 40, 20, 255), Chalk = new(0xa7, 0xce, 0x51, 0xff);

    readonly GameData data;
    readonly CafeController game;
    readonly Action<string, string, Action> ask;
    readonly Action<string> tell;
    Stove? stove;
    List<int> recipes = new();
    int page, selected, variant;
    // +0x230: the open cookbook; the stove family's books; +0x254: the family last opened (its book is kept)
    int book = 1, family, lastFamily = -1;
    List<int> books = new();
    readonly Rectangle?[] bookRects = new Rectangle?[8];

    /// <summary>A cookbook's cover on the chalkboard (book0 ..), for test scripts.</summary>
    public Rectangle? BookCover(int i) => i >= 0 && i < bookRects.Length ? bookRects[i] : null;
    /// <summary>The star of the recipe on the page, and the button buying the open book.</summary>
    public Rectangle? StarButton { get; private set; }
    public Rectangle? BuyButton { get; private set; }

    public bool IsOpen => stove != null || browsing;
    /// <summary>The COOK button (the tutorial's arrow points at it).</summary>
    public Rectangle? CookButton { get; private set; }

    /// <param name="ask">Puts a yes/no question to the player (text, yes label, what yes does).</param>
    /// <param name="tell">Shows the player a message.</param>
    public RecipeBook(GameData data, CafeController game, Action<string, string, Action> ask, Action<string> tell)
    {
        this.data = data;
        this.game = game;
        this.ask = ask;
        this.tell = tell;
    }

    // browsing from the HUD's cookbook button: no stove, nothing to cook
    bool browsing;

    public void Open(Stove s) => Open(s, s.Def.StoveFamily);

    /// <summary>The HUD's cookbook (after the tutorial): look through the recipes and buy variants.</summary>
    public void OpenToBrowse() => Open(null, 0);

    /// <summary>
    /// FUN_00103ee4: the book opens on the family's own book (the General for an ordinary stove), or on the one left
    /// open when the family is the same as last time.
    /// </summary>
    void Open(Stove? s, int family)
    {
        stove = s;
        browsing = s == null;
        this.family = family;
        books = game.BooksFor(family);
        if (family != lastFamily || !books.Contains(book))
            book = family == 0 ? 1 : books.Count > 1 ? books[1] : 0;
        lastFamily = family;
        LoadRecipes();
    }

    /// <summary>The open book's recipes by level (the favourites: those this stove cooks), on the first one to cook.</summary>
    void LoadRecipes()
    {
        recipes = book == 0
            ? game.Player.Favorites.Where(f => data.Foods[f].StoveFamily == family).ToList()
            : data.Foods.Where(f => f.Book == book && f.StoveFamily == family)
                .OrderBy(f => f.Level).ThenBy(f => f.Index).Select(f => f.Index).ToList();
        selected = Math.Max(0, recipes.FindIndex(r => game.Player.HasRecipe(r)));
        page = selected / CardsPerPage;
        variant = recipes.Count > 0 ? FirstVariant(Selected) : 0;
        CookButton = null;
    }

    public void Close()
    {
        stove = null;
        browsing = false;
        CookButton = null;
    }

    int Selected => recipes.Count == 0 ? 0 : recipes[Math.Clamp(selected, 0, recipes.Count - 1)];

    int FirstVariant(int food)
    {
        for (int v = 0; v < 10; v++)
            if (game.Player.HasRecipe(food, v))
                return v;
        return 0;
    }

    // the book page fitted to the screen; book coordinates are the sprite's 1024 x 768
    Rectangle bookRect;
    float scale;
    float X(float bx) => bookRect.X + bx * scale;
    float Y(float by) => bookRect.Y + by * scale;

    /// <summary>Draws the book and handles its clicks. Returns false once it was shut without cooking.</summary>
    public void Draw(Action cancelled, Action cooked)
    {
        if (!IsOpen)
            return;
        var ui = data.IngameUi;
        var page0 = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        scale = MathF.Min(Screen.Width / 1024f, Screen.Height / 768f);
        bookRect = new Rectangle((Screen.Width - 1024 * scale) / 2, (Screen.Height - 768 * scale) / 2, 1024 * scale, 768 * scale);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 150));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        if (page0.Length > 0)
            page0[0].DrawAt(bookRect.X, bookRect.Y, bookRect.Width, bookRect.Height, Color.White);

        // the chalkboard: the stove's cookbooks, the open one raised, its name beside them; and the way out
        DrawShelf();
        if (Widgets.ImageButton(ui[UiSprites.CloseX], X(975), Y(78), scale) || (!Widgets.Blocked && Input.IsKeyPressed(KeyboardKey.Escape)))
        {
            Close();
            cancelled();
            return;
        }
        StarButton = BuyButton = null;
        if (!game.HasBook(book))
        {
            DrawBookForSale();
            return;
        }
        if (recipes.Count == 0)
        {
            // the favourites with nothing starred for this stove
            Text.DrawAnchored(data.Exe.Get(browsing ? TextNoFavourites : TextNoFavouritesHere), X(512), Y(400), 30 * scale,
                Anchor.Center, Ink);
            return;
        }

        // the row of recipes, five at a time
        int pages = Math.Max(1, (recipes.Count + CardsPerPage - 1) / CardsPerPage);
        page = Math.Clamp(page, 0, pages - 1);
        if (page > 0 && Widgets.ImageButton(ui[UiSprites.ArrowLeft], X(62), Y(285), scale * 1.2f))
            page--;
        if (page < pages - 1 && Widgets.ImageButton(ui[UiSprites.ArrowRight], X(962), Y(285), scale * 1.2f))
            page++;
        for (int k = 0; k < CardsPerPage; k++)
        {
            int i = page * CardsPerPage + k;
            if (i >= recipes.Count)
                break;
            DrawCard(i, X(200 + k * 157), Y(285));
        }

        DrawDetails(ui, cooked);
    }

    /// <summary>The covers of the stove's books (CookBookUi, cover +0x08) along the chalkboard; a click opens one.</summary>
    void DrawShelf()
    {
        var covers = data.Sheet("ipad_CookBookUiImages", "ipad_CookBookUiOffsets.bin");
        Array.Clear(bookRects);
        float cs = 0.55f * scale;
        for (int i = 0; i < books.Count && i < bookRects.Length; i++)
        {
            int b = books[i];
            int ci = data.Cookbooks[b].S08;
            if (ci < 0 || ci >= covers.Length)
                continue;
            var cover = covers[ci];
            float cx = X(78 + i * 70), cy = Y(b == book ? 70 : 80);
            var r = new Rectangle(cx - cover.FullWidth * cs / 2, cy - cover.FullHeight * cs / 2, cover.FullWidth * cs, cover.FullHeight * cs);
            bookRects[i] = r;
            bool over = Widgets.Hover(r);
            var tint = b == book || over ? Color.White : game.HasBook(b) ? new Color(205, 205, 205, 255) : new Color(110, 110, 110, 255);
            cover.DrawAt(r.X, r.Y, r.Width, r.Height, tint);
            if (Widgets.Clicked(r) && b != book)
            {
                book = b;
                game.Sound(36);
                LoadRecipes();
            }
        }
        float left = X(78 + books.Count * 70 - 20), right = X(930);
        string name = Shorten(data.Cookbooks[book].Name, right - left, 38 * scale);
        Text.DrawAnchored(name, (left + right) / 2, Y(78), 38 * scale, Anchor.Center, Chalk);
    }

    /// <summary>
    /// A book the player does not have: its cover and what it says of itself, and when it can be had ("This cookbook
    /// unlocks at level ..."), or its price and the button to buy it ("Are you sure ...?", short of the money).
    /// </summary>
    void DrawBookForSale()
    {
        var c = data.Cookbooks[book];
        var covers = data.Sheet("ipad_CookBookUiImages", "ipad_CookBookUiOffsets.bin");
        if (c.S08 >= 0 && c.S08 < covers.Length)
            covers[c.S08].Draw(X(260), Y(430), Anchor.Center, Color.White, Flip.None, 1.6f * scale, 1.6f * scale);
        float y = Y(300);
        foreach (var line in Widgets.WrapText(c.Description, 540 * scale, 24 * scale))
        {
            Text.Draw(line, X(420), y, 24 * scale, Ink);
            y += 30 * scale;
        }
        var p = game.Player;
        if (p.Level < c.S18)
        {
            Text.Draw(data.Exe.Get(TextBookLevel).Trim() + " " + c.S18 + ".", X(420), y + 20 * scale, 24 * scale, new Color(150, 30, 30, 255));
            return;
        }
        if (c.Price <= 0 && c.S06 <= 0)
            return;
        var ui = data.IngameUi;
        bool toxin = c.S06 > 0;
        float bx = X(600), by = y + 80 * scale;
        ui[toxin ? UiSprites.VialMedium : UiSprites.CashSmall].Draw(bx - 70 * scale, by, Anchor.Center, Color.White, Flip.None, 0.7f * scale, 0.7f * scale);
        Text.DrawAnchored((toxin ? c.S06 : c.Price).ToString("N0"), bx - 50 * scale, by, 26 * scale, Anchor.Left | Anchor.VCenter, Ink);
        var buy = ui[UiUnlock];
        float kx = bx + 160 * scale;
        BuyButton = new Rectangle(kx - buy.FullWidth * scale * 0.6f, by - buy.FullHeight * scale * 0.6f, buy.FullWidth * scale * 1.2f, buy.FullHeight * scale * 1.2f);
        if (Widgets.ImageButton(buy, kx, by, 1.2f * scale))
        {
            int b = book;
            ask(data.Exe.Get(TextBuyBook), data.Strings[461], () =>
            {
                if (game.BuyBook(b))
                    LoadRecipes();
                else
                    tell(data.Exe.Get(TextNoMoney));
            });
        }
    }

    void DrawCard(int i, float cx, float cy)
    {
        int food = recipes[i];
        var f = data.Foods[food];
        bool known = game.Player.KnowsRecipe(food);
        var card = data.IngameUi[UiCard];
        float cs = scale * 0.95f;
        var r = new Rectangle(cx - card.FullWidth * cs / 2, cy - card.FullHeight * cs / 2, card.FullWidth * cs, card.FullHeight * cs);
        bool over = Widgets.Hover(r);
        var tint = i == selected ? Color.White : over ? new Color(250, 245, 235, 255) : new Color(225, 220, 210, 255);
        card.DrawAt(r.X, r.Y, r.Width, r.Height, tint);
        if (i == selected)
            Gfx.Draw.Rect(new Rectangle(r.X - 3, r.Y + r.Height - 4, r.Width + 6, 5), new Color(60, 160, 40, 255));
        var pic = data.FoodImage(food);
        var picTint = known ? Color.White : new Color(90, 90, 90, 255);
        pic?.Draw(cx, cy + r.Height * 0.12f, Anchor.Center, picTint, Flip.None, 0.32f * scale, 0.32f * scale);
        Text.DrawAnchored(Shorten(f.Name, r.Width - 10 * scale, 15 * scale), cx, r.Y + 6 * scale, 15 * scale, Anchor.Top | Anchor.HCenter, Ink);
        if (!known && f.Level > game.Player.Level)
            Text.DrawAnchored(data.Strings[StringTable.UnlockAtLevel] + f.Level, cx, r.Y + r.Height - 6 * scale, 11 * scale,
                Anchor.Bottom | Anchor.HCenter, new Color(150, 30, 30, 255));
        if (Widgets.Clicked(r) && i != selected)
        {
            selected = i;
            variant = FirstVariant(food);
            game.Sound(36);
        }
    }

    // FUN_00102280's colours: worse than the plain recipe, better, the same
    static readonly Color Worse = new(0xff, 0, 0, 0xff), Better = new(0, 0xaa, 0, 0xff);

    static Color Against(bool worse, bool better) => worse ? Worse : better ? Better : Ink;

    /// <summary>
    /// FUN_00102280: the cost the book shows - 2% off for each Wolf of the Steppes in the yard, and 2% more off a Very
    /// Quick dish for each Royal Bear. Cooking still charges the full price (FUN_000d9ce4), as in the original.
    /// </summary>
    int BookCost(int cost, int variant)
    {
        int shown = (int)(cost * (game.PetCount(PetDef.Wolf) * -0.02 + 1.0));
        if (variant == 9)
            shown = (int)(shown * (game.PetCount(PetDef.RoyalBear) * -0.02 + 1.0));
        return shown;
    }

    void DrawDetails(SubImage[] ui, Action cooked)
    {
        if (recipes.Count == 0)
            return;
        int food = Selected;
        var f = data.Foods[food];
        var p = game.Player;
        bool known = p.KnowsRecipe(food);
        bool have = p.HasRecipe(food, variant);

        // the picture on the little chalkboard
        var pic = data.FoodImage(food);
        pic?.Draw(X(218), Y(517), Anchor.Center, have ? Color.White : new Color(110, 110, 110, 255), Flip.None, 0.5f * scale, 0.5f * scale);

        // name and numbers (FUN_00102280: each against the plain recipe's - red where the variant does worse, green
        // where it does better; the experience as this level's cooking teaches it)
        string title = game.RecipeName(food, variant);
        Text.Draw(title, X(350), Y(405), 34 * scale, Ink);
        // its star: into the favourites or out of them (game +0x68)
        var star = ui[UiStar];
        float sx = X(350) + Text.Measure(title, 34 * scale).W + 30 * scale, sy = Y(405) + 18 * scale;
        var sr = new Rectangle(sx - star.FullWidth * scale / 2, sy - star.FullHeight * scale / 2, star.FullWidth * scale, star.FullHeight * scale);
        StarButton = sr;
        bool starred = p.Favorites.Contains(food);
        star.Draw(sx, sy, Anchor.Center, starred ? Color.White : new Color(95, 85, 75, 170), Flip.None, scale, scale);
        if (Widgets.Clicked(sr))
        {
            if (game.ToggleFavorite(food, book == 0, family) is { } why)
                tell(why);
            else
            {
                game.Sound(36);
                if (book == 0)
                    LoadRecipes();
            }
        }
        int cost = f.CostWith(variant);
        int shown = BookCost(cost, variant), plainCost = f.CostWith(0);
        int secs = f.CookSeconds(variant), plainSecs = f.CookSeconds(0);
        int earns = f.PriceWith(variant) * f.ServingsWith(variant), plainEarns = f.PriceWith(0) * f.ServingsWith(0);
        int xp = f.XpWith(variant), plainXp = f.XpWith(0);
        var byLevel = data.K.CookXpByLevel;
        int taught = (int)(xp * byLevel[Math.Clamp(p.Level - 1, 0, byLevel.Length - 1)]);
        Text.Draw($"{data.Strings[376]} ${shown}", X(350), Y(452), 22 * scale, Against(shown > plainCost, shown < plainCost));
        Text.Draw($"{data.Strings[377]} {FormatTime(secs)}", X(350), Y(480), 22 * scale, Against(secs > plainSecs, secs < plainSecs));
        Text.Draw($"{data.Strings[378]} ${f.PriceWith(variant)} x {f.ServingsWith(variant)}", X(350), Y(508), 22 * scale,
            Against(earns < plainEarns, earns > plainEarns));
        Text.Draw($"XP: {taught}", X(350), Y(536), 22 * scale, Against(xp < plainXp, xp > plainXp));

        // the ten variants
        for (int v = 0; v < 10; v++)
        {
            float vx = X(372 + v * 58), vy = Y(610);
            var icon = ui[p.HasRecipe(food, v) ? VariantIcons[v] : LockedIcons[v]];
            float s = 0.62f * scale * (v == variant ? 1.15f : 1f);
            if (v == variant)
                Gfx.Draw.Rect(new Rectangle(vx - 26 * scale, vy + 28 * scale, 52 * scale, 4), new Color(60, 160, 40, 255));
            if (Widgets.ImageButton(icon, vx, vy, s) && v != variant)
            {
                variant = v;
                game.Sound(36);
            }
        }
        if (variant > 0)
            Text.Draw(data.Strings[StringTable.ModifierNames + variant], X(350), Y(645), 18 * scale, Ink);

        // COOK, or what it takes to unlock this
        CookButton = null;
        float bx = X(845), by = Y(690);
        if (have && browsing)
        {
            // nothing to cook on from the cookbook button
        }
        else if (have)
        {
            var cook = ui[UiSprites.ButtonCook];
            bool canPay = p.Money >= cost;
            CookButton = new Rectangle(bx - cook.FullWidth * scale * 0.6f, by - cook.FullHeight * scale * 0.6f, cook.FullWidth * scale * 1.2f, cook.FullHeight * scale * 1.2f);
            if (Widgets.ImageButton(cook, bx, by, 1.2f * scale, canPay) && game.CookRecipe(stove!, food, (byte)variant))
            {
                game.Sound(36);
                Close();
                cooked();
            }
        }
        else if (!known && f.Level > p.Level && variant == 0)
        {
            Text.DrawAnchored(data.Strings[StringTable.UnlockAtLevel] + f.Level, bx, by, 22 * scale, Anchor.Center, new Color(150, 30, 30, 255));
        }
        else
        {
            int price = game.UnlockPrice(food, variant);
            var unlock = ui[UiUnlock];
            ui[UiSprites.VialMedium].Draw(bx - 60 * scale, by - 42 * scale, Anchor.Center, Color.White, Flip.None, 0.6f * scale, 0.6f * scale);
            Text.DrawAnchored(price.ToString(), bx - 44 * scale, by - 42 * scale, 22 * scale, Anchor.Left | Anchor.VCenter, Ink);
            if (Widgets.ImageButton(unlock, bx, by, 1.2f * scale))
            {
                int fd = food, vr = variant;
                ask(data.Strings[317] + price + data.Strings[318], data.Strings[319], () => game.BuyRecipe(fd, vr));
            }
        }
    }

    static string Shorten(string s, float width, float size)
    {
        if (Text.Measure(s, size).W <= width)
            return s;
        while (s.Length > 3 && Text.Measure(s + "...", size).W > width)
            s = s[..^1];
        return s + "...";
    }

    static string FormatTime(int seconds) =>
        seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60:00}m" : $"{seconds / 60}:{seconds % 60:00}";
}
