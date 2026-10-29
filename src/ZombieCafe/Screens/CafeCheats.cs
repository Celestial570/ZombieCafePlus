using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The cheat menu (the 0 key, a port addition), in tabs: Main (speed, cash, toxin, levels, rating, the tutorial, the
/// Time page), Cafe (cooking, cleaning, collecting, the fridge, tombstones, customers), Staff (rest, level, hire any
/// zombie), Unlock (recipes, cookbooks, chefs, the ZombiePedia, quests) and Toggles (effects that stay on until switched
/// off or the game closes). What the actions change is saved like anything else; the toggles are not.
/// </summary>
public sealed partial class CafeScreen
{
    // open across screens (a time skip makes the cafe anew)
    static bool cheatsOpen;
    // the Time page instead of the cheats, and the days its skip covers
    static bool cheatsTimePage;
    static int skipDays = 1;
    // the Send customer page: which page of types, all types or only those that can come now, how many at a time
    static bool cheatsCustomerPage, customerShowAll;
    // the same page hiring zombies instead of sending customers
    static bool customerHire;
    // the tab shown
    static int cheatsTab;
    static readonly string[] CheatTabs = ["Main", "Cafe", "Staff", "Unlock", "Toggles"];
    static int customerPage, customerCount = 1;
    string? customerNote;
    float customerNoteTime;
    // where each cheat's button was last drawn (test scripts click "cheat:<key>")
    readonly Dictionary<string, Rectangle> cheatRects = new();
    static readonly float[] CheatSpeeds = [1, 2, 5, 10, 20];

    void ToggleCheats()
    {
        cheatsOpen = !cheatsOpen;
        game.Sound(36);
    }

    /// <summary>A tab's cheats as (key, label, can be used now, what it does), two to a row.</summary>
    List<(string Key, string Label, bool Enabled, Action Run)> CheatActions(int tab)
    {
        var p = game.Player;
        var stoves = cafe.Objects.OfType<Stove>().ToList();
        var pieces = cafe.Objects.OfType<Furniture>().ToList();
        return tab switch
        {
            0 =>
            [
                ("cash1k", "+$1,000", true, () => p.Money += 1000),
                ("cash10k", "+$10,000", true, () => p.Money += 10000),
                ("cash100k", "+$100,000", true, () => p.Money += 100000),
                ("cash1m", "+$1,000,000", true, () => p.Money += 1000000),
                ("toxin10", "+10 toxin", true, () => p.Toxin += 10),
                ("toxin100", "+100 toxin", true, () => p.Toxin += 100),
                ("toxin1000", "+1,000 toxin", true, () => p.Toxin += 1000),
                ("nomoney", "Money to $0", p.Money != 0, () => p.Money = 0),
                ("levelup", "Level up", p.Level < data.K.PlayerMaxLevel, () => CheatLevels(1)),
                ("level5", "Level +5", p.Level < data.K.PlayerMaxLevel, () => CheatLevels(5)),
                ("rating", "Max rating", p.Rating < 1f, () => p.Rating = 1f),
                ("rating0", "Rating to 0", p.Rating > 0f, () => p.Rating = 0f),
                ("tutorial", "Skip tutorial", !game.Hints.TutorialDone, CheatSkipTutorial),
                ("time", "Time skip >", SkipTime != null, () => cheatsTimePage = true),
            ],
            1 =>
            [
                ("finish", "Finish cooking", stoves.Any(s => s.Busy && !s.IsDone && s.Dish != null), CheatFinishCooking),
                ("burnt", "Clean burnt pots", stoves.Any(s => s.IsBurnt(cafe)), () => stoves.Where(s => s.IsBurnt(cafe)).ToList().ForEach(s => s.CleanBurnt(cafe))),
                ("tables", "Clean dirty tables", cafe.DirtyTables.Count > 0, CheatCleanTables),
                ("collect", "Collect cash items", pieces.Any(f => f.Stored > 0 && f.Cell >= 0), () => pieces.Where(f => f.Stored > 0 && f.Cell >= 0).ToList().ForEach(f => game.Collect(f))),
                ("fridge", "+5 dishes in fridge", true, CheatFillFridge),
                ("tombstones", "Recharge tombstones", true, CheatRechargeTombstones),
                ("door", cafe.IsOpen ? "Close the cafe" : "Open the cafe", game.Hints.TutorialDone, () => cafe.SetOpen(!cafe.IsOpen)),
                ("customer", "Customer now", cafe.IsOpen, () => game.SpawnCustomer(true)),
                ("customers10", "10 customers now", cafe.IsOpen, () => { for (int i = 0; i < 10; i++) game.SpawnCustomer(true); }),
                ("customers", "Send customer >", true, () => { customerHire = false; customerPage = 0; cheatsCustomerPage = true; }),
                ("rivals", "Reopen rivals", p.Neighbours.Timers.Any(t => t > 0), () => Array.Clear(p.Neighbours.Timers)),
                ("toxinlimit", "Reset toxin limit", p.ToxinBoughtToday > 0, () => p.ToxinBoughtToday = 0),
            ],
            2 =>
            [
                ("rest", "Rest the staff", true, CheatRestStaff),
                ("hire", "Hire zombie >", true, () => { customerHire = true; customerPage = 0; cheatsCustomerPage = true; }),
                ("zlevelup", "Zombies level up", game.Staff.Any(z => z.Character.Level < data.K.ZombieMaxLevel), () => CheatZombieLevels(false)),
                ("zmax", "Zombies max level", game.Staff.Any(z => z.Character.Level < data.K.ZombieMaxLevel), () => CheatZombieLevels(true)),
                ("autobreak", "Auto break: all on", game.Staff.Any(z => !z.Character.AutoBreak), () => game.Staff.ForEach(z => z.Character.AutoBreak = true)),
                ("serveall", "Everyone serve", game.Staff.Count > 0, () => game.Staff.ForEach(z => game.AssignServing(z))),
            ],
            3 =>
            [
                ("recipes", "All recipes", true, CheatAllRecipes),
                ("books", "All cookbooks", true, CheatAllCookbooks),
                ("chefs", "All chefs", true, CheatAllChefs),
                ("pedia", "Fill ZombiePedia", true, CheatFillPedia),
                ("quests", "Finish quest goals", p.Objectives.Goals.Any(g => !g.IsComplete), () => p.Objectives.Goals.ForEach(g => g.Progress = g.Target)),
            ],
            _ => [],
        };
    }

    /// <summary>The Toggles tab: (key, label, getter, setter).</summary>
    static readonly (string Key, string Label, Func<bool> Get, Action<bool> Set)[] CheatToggles =
    [
        ("energy", "Infinite energy", () => Cheats.InfiniteEnergy, v => Cheats.InfiniteEnergy = v),
        ("free", "Free shopping", () => Cheats.FreeShopping, v => Cheats.FreeShopping = v),
        ("nolocks", "No level locks", () => Cheats.NoLevelLocks, v => Cheats.NoLevelLocks = v),
        ("instant", "Instant cooking", () => Cheats.InstantCooking, v => Cheats.InstantCooking = v),
        ("noburn", "No burning", () => Cheats.NoBurning, v => Cheats.NoBurning = v),
        ("peaceful", "Peaceful zombies", () => Cheats.PeacefulZombies, v => Cheats.PeacefulZombies = v),
        ("nodream", "No daydreaming", () => Cheats.NoDaydreaming, v => Cheats.NoDaydreaming = v),
        ("busy", "Busy street", () => Cheats.BusyStreet, v => Cheats.BusyStreet = v),
        ("patient", "Patient customers", () => Cheats.PatientCustomers, v => Cheats.PatientCustomers = v),
        ("autocollect", "Auto-collect", () => Cheats.AutoCollect, v => Cheats.AutoCollect = v),
        ("freeze", "Freeze cafe", () => Cheats.FreezeCafe, v => Cheats.FreezeCafe = v),
    ];

    /// <summary>Experience for <paramref name="levels"/> levels (each level-up, its box and rewards follow as usual).</summary>
    void CheatLevels(int levels)
    {
        var p = game.Player;
        var t = data.K.PlayerLevelXp;
        long xp = 0;
        for (int l = p.Level; l < p.Level + levels && l < t.Length && l < data.K.PlayerMaxLevel; l++)
            xp += t[l];
        p.Xp = (int)Math.Min(int.MaxValue, Math.Max(p.Xp, xp));
    }

    /// <summary>Every dirty table cleared (the plates gone, as a zombie at the sink would leave it).</summary>
    void CheatCleanTables()
    {
        foreach (var t in cafe.DirtyTables.ToList())
        {
            t.Detach();
            t.Busy = false;
        }
        cafe.DirtyTables.Clear();
    }

    /// <summary>Five dishes of recipes the player knows into the fridge.</summary>
    void CheatFillFridge()
    {
        var known = data.Foods.Where(f => game.Player.KnowsRecipe(f.Index)).ToList();
        if (known.Count == 0)
            return;
        for (int i = 0; i < 5; i++)
            game.Player.Fridge.Add(Dish.Cook(data, known[cafe.Rng.Next(known.Count)].Index, 0));
    }

    /// <summary>Every tombstone at its full uses, and the Amptron's three summons back.</summary>
    void CheatRechargeTombstones()
    {
        var boosts = game.Player.Boosts;
        for (int i = 0; i < Boosts.Table.Length; i++)
            boosts.Refill(Boosts.Table[i].Furniture);
        game.Player.AmptronUses = 3;
    }

    void CheatAllCookbooks()
    {
        foreach (var book in data.Cookbooks)
            if (book.Index > 0)
                game.Player.Cookbooks.Add(book.Index);
    }

    /// <summary>Every chef in the chef store owned and unlocked (the raid bosses' too).</summary>
    void CheatAllChefs()
    {
        foreach (var d in data.Characters.Where(c => c.B39 != 0))
        {
            game.Player.OwnedChefs.Add(d.Index);
            game.Player.UnlockedChefs.Add(d.Index);
        }
    }

    /// <summary>Every kind of zombie in the ZombiePedia marked as made.</summary>
    void CheatFillPedia()
    {
        foreach (var d in data.Characters.Where(c => c.B04 != 0 && c.B20 != 255))
            game.Player.ZombieKinds.Add(d.Index);
    }

    /// <summary>The staff a level up each, or all the way.</summary>
    void CheatZombieLevels(bool max)
    {
        int top = data.K.ZombieMaxLevel;
        var table = data.K.ZombieLevelXp;
        foreach (var z in game.Staff)
        {
            var c = z.Character;
            if (c.Level >= top)
                continue;
            c.Level = max ? Math.Min(top, table.Length) : c.Level + 1;
            c.Xp = 0;
            c.LevelledUp = true;
            c.Energy = c.MaxEnergy(data);
        }
        game.RefreshStaffStats();
    }

    /// <summary>A zombie of the given kind joins the staff at once (staff limit or not), in the ZombiePedia too.</summary>
    void CheatHire(CharacterDef d)
    {
        game.AddStaff(d.Index, game.FreeRoomCell());
        game.Player.ZombieKinds.Add(d.Index);
    }

    /// <summary>Experience enough to finish the level (the level-up, its dialog and rewards follow as usual).</summary>
    void CheatLevelUp()
    {
        var p = game.Player;
        int need = p.XpForNextLevel(data);
        if (need != int.MaxValue)
            p.Xp = Math.Max(p.Xp, need);
    }

    void CheatFinishCooking()
    {
        foreach (var s in cafe.Objects.OfType<Stove>().ToList())
            if (s.Busy && !s.IsDone && s.Dish != null)
                s.FinishNow(cafe);
    }

    /// <summary>The chef, staff and pets at full energy, the knocked-out back up and the daydreamers awake.</summary>
    void CheatRestStaff()
    {
        foreach (var w in game.Staff.Append(game.ChefActor).Concat(game.Pets).ToList())
        {
            var c = w.Character;
            c.Energy = c.MaxEnergy(data);
            if (c.ReanimateMs > 0)
            {
                c.ReanimateMs = 0;
                c.Job = Job.None;
            }
            if (c.Job == Job.Daydreaming)
                game.WakeUp(w);
        }
    }

    /// <summary>Every recipe (as plain dishes) and every cookbook.</summary>
    void CheatAllRecipes()
    {
        var p = game.Player;
        foreach (var f in data.Foods)
            p.UnlockRecipe(f.Index, 0);
        CheatAllCookbooks();
    }

    /// <summary>
    /// The tutorial marked done as ZC_SKIPTUTORIAL leaves it: its tips seen, the HUD's buttons all given, and - if the
    /// tutorial's customer was never infected - the zombie it would have brought.
    /// </summary>
    void CheatSkipTutorial()
    {
        foreach (int h in Enumerable.Range(0, 0x17).Concat([0x21, 0x3c, 0x4d, 0x4f, 0x50, 0x51, 0x52]))
            game.Hints.MarkShown(h);
        if (game.Staff.Count == 0)
            game.AddStaff(0, game.FreeRoomCell());
        // its customer is an ordinary one now (eats, pays and goes)
        game.TutorialCustomer = null;
        chefMessage.Hide();
        arrow.Hide();
        RefreshHud();
    }

    /// <summary>
    /// The Time page: the game's clock (and how far it runs ahead of the computer's), skips of hours, and a skip of
    /// 1-30 days. A skip moves the clock on and catches the cafe up as after that long away - cooking, customers,
    /// rest and energy, rivals reopening, tombstones and the Amptron recharging, the next day's Frequent Fryer.
    /// </summary>
    void DrawTimeCheats()
    {
        const float w = 430, pad = 16, gap = 8, bh = 42;
        float h = 58 + 34 + 30 + 2 * (bh + gap) + 30 + bh + gap + bh + pad;
        var r = new Rectangle(90, 70, w, h);
        Widgets.Panel(r);
        Widgets.Label("TIME", r.X + pad, r.Y + 12, Text.GameFont[3], Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label("0 to close", r.X + w - pad, r.Y + 18, Text.GameFont[0], Anchor.Top | Anchor.Right);
        float y = r.Y + 58;
        var now = GameClock.Now;
        Widgets.Label(now.ToString("ddd d MMM yyyy  h:mm tt"), r.X + pad, y, Text.GameFont[2]);
        y += 34;
        var ahead = TimeSpan.FromMilliseconds(GameClock.OffsetMs);
        Widgets.Label(ahead.TotalMinutes < 1 ? "Game time = real time" : $"Ahead of real time: {(int)ahead.TotalDays}d {ahead.Hours}h {ahead.Minutes}m",
            r.X + pad, y, Text.GameFont[0]);
        y += 30;
        // hours
        (string Key, string Label, long Ms)[] hours =
        [
            ("skip1h", "+1 hour", 3_600_000L), ("skip3h", "+3 hours", 3 * 3_600_000L), ("skip6h", "+6 hours", 6 * 3_600_000L),
            ("skip12h", "+12 hours", 12 * 3_600_000L), ("skip1d", "+1 day", 24 * 3_600_000L), ("skip7d", "+7 days", 7 * 24 * 3_600_000L),
        ];
        float bw3 = (w - pad * 2 - gap * 2) / 3;
        for (int i = 0; i < hours.Length; i++)
        {
            var b = new Rectangle(r.X + pad + i % 3 * (bw3 + gap), y + i / 3 * (bh + gap), bw3, bh);
            cheatRects[hours[i].Key] = b;
            if (Widgets.Button(b, hours[i].Label, Text.GameFont[1]))
            {
                game.Sound(36);
                SkipTime?.Invoke(hours[i].Ms);
                return;
            }
        }
        y += 2 * (bh + gap);
        // a chosen number of days
        Widgets.Label("Skip days:", r.X + pad, y + 6, Text.GameFont[1]);
        float cx = r.X + 170;
        var minus = new Rectangle(cx, y, 40, 34);
        var plus = new Rectangle(cx + 110, y, 40, 34);
        cheatRects["daysminus"] = minus;
        cheatRects["daysplus"] = plus;
        if (Widgets.Button(minus, "-", Text.GameFont[2], skipDays > 1))
            skipDays--;
        Text.DrawAnchored(skipDays.ToString(), cx + 75, y + 17, Text.GameFont[3], Anchor.Center, Color.White, Color.Black, 2);
        if (Widgets.Button(plus, "+", Text.GameFont[2], skipDays < 30))
            skipDays++;
        y += 30 + gap;
        var go = new Rectangle(r.X + pad, y, w - pad * 2, bh);
        cheatRects["skipdays"] = go;
        if (Widgets.Button(go, $"Skip {skipDays} day{(skipDays == 1 ? "" : "s")}", Text.GameFont[1]))
        {
            game.Sound(36);
            SkipTime?.Invoke(skipDays * 24L * 3_600_000L);
            return;
        }
        y += bh + gap;
        var back = new Rectangle(r.X + pad, y, w - pad * 2, bh);
        cheatRects["timeback"] = back;
        if (Widgets.Button(back, "< Back to cheats", Text.GameFont[1]))
            cheatsTimePage = false;
    }

    /// <summary>
    /// The Send customer page: every type of customer as a card with its face, a page at a time - only those that can
    /// come at the cafe's level and rating, or all of them - and how many to send. A card sends that many through the
    /// door (the cafe must be open).
    /// </summary>
    void DrawCustomerCheats()
    {
        const float w = 640, pad = 16, gap = 8, cardW = (w - pad * 2 - gap * 2) / 3, cardH = 60, bh = 40;
        const int cols = 3, rowsPer = 5, perPage = cols * rowsPer;
        var types = customerHire ? game.ZombieTypes() : game.CustomerTypes();
        if (!customerShowAll && !customerHire)
            types = types.Where(game.CanVisitNow).ToList();
        int pages = Math.Max(1, (types.Count + perPage - 1) / perPage);
        customerPage = Math.Clamp(customerPage, 0, pages - 1);
        float h = 58 + bh + gap + rowsPer * (cardH + gap) + bh + gap + bh + pad + 26;
        var r = new Rectangle(90, 70, w, h);
        Widgets.Panel(r);
        Widgets.Label(customerHire ? "HIRE A ZOMBIE" : "SEND A CUSTOMER", r.X + pad, r.Y + 12, Text.GameFont[3], Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label("0 to close", r.X + w - pad, r.Y + 18, Text.GameFont[0], Anchor.Top | Anchor.Right);
        if (customerNoteTime > 0)
            customerNoteTime -= Raylib.GetFrameTime();
        float y = r.Y + 58;
        // which types, and how many at a time
        var now = new Rectangle(r.X + pad, y, 150, bh);
        var all = new Rectangle(r.X + pad + 158, y, 150, bh);
        cheatRects["custnow"] = now;
        cheatRects["custall"] = all;
        if (customerHire)
            Widgets.Label($"Staff: {game.Staff.Count} (limit {game.StaffLimit}, ignored)", r.X + pad, y + bh / 2, Text.GameFont[0], Anchor.Left | Anchor.VCenter);
        else if (Widgets.Button(now, "Can come now", Text.GameFont[0], customerShowAll))
        {
            customerShowAll = false;
            customerPage = 0;
        }
        if (!customerHire && Widgets.Button(all, "All types", Text.GameFont[0], !customerShowAll))
        {
            customerShowAll = true;
            customerPage = 0;
        }
        int[] counts = [1, 3, 10];
        Widgets.Label("Send:", r.X + w - pad - counts.Length * 58 - 8, y + bh / 2, Text.GameFont[1], Anchor.Right | Anchor.VCenter);
        for (int i = 0; i < counts.Length; i++)
        {
            var b = new Rectangle(r.X + w - pad - (counts.Length - i) * 58, y, 50, bh);
            cheatRects["send" + counts[i]] = b;
            if (Widgets.Button(b, "x" + counts[i], Text.GameFont[1], customerCount != counts[i]))
                customerCount = counts[i];
        }
        y += bh + gap;
        // the cards
        bool open = cafe.IsOpen || customerHire;
        int first = customerPage * perPage;
        for (int i = 0; i < perPage && first + i < types.Count; i++)
        {
            var d = types[first + i];
            var b = new Rectangle(r.X + pad + i % cols * (cardW + gap), y + i / cols * (cardH + gap), cardW, cardH);
            cheatRects["cust" + i] = b;
            if (Widgets.Button(b, "", Text.GameFont[0], open))
            {
                for (int n = 0; n < customerCount; n++)
                {
                    if (customerHire)
                        CheatHire(d);
                    else
                        game.SpawnCustomer(true, d);
                }
                game.Sound(36);
                customerNote = $"{(customerHire ? "Hired" : "Sent")} {(customerCount == 1 ? "a" : customerCount.ToString())} {d.Name}";
                customerNoteTime = 3f;
            }
            if (CustomerFace(d, customerHire) is { } face)
            {
                float s = MathF.Min(52f / Math.Max(1, face.FullWidth), 52f / Math.Max(1, face.FullHeight));
                face.Draw(b.X + 32, b.Y + cardH / 2, Anchor.Center, open ? Color.White : new Color(150, 150, 150, 255), Flip.None, s, s);
            }
            string name = d.Name.Length > 15 ? d.Name[..14] + "." : d.Name;
            Widgets.Label(name, b.X + 62, b.Y + 8, Text.GameFont[0]);
            // when it comes by itself: the level, and the rating in stars when it needs one
            float stars = d.F00 * 5;
            string when = $"Lv {Math.Max(1, (int)d.B20)}" + (stars > 0 ? $", {stars:0.#} star{(stars == 1 ? "" : "s")}" : "");
            bool can = customerHire || game.CanVisitNow(d);
            Widgets.Label(can ? when : "Not yet: " + when, b.X + 62, b.Y + 34, 14, Anchor.Top | Anchor.Left,
                can ? new Color(215, 215, 215, 255) : Widgets.Gold);
        }
        y += rowsPer * (cardH + gap);
        // pages
        var prev = new Rectangle(r.X + pad, y, 120, bh);
        var next = new Rectangle(r.X + w - pad - 120, y, 120, bh);
        cheatRects["custprev"] = prev;
        cheatRects["custnext"] = next;
        if (Widgets.Button(prev, "< Prev", Text.GameFont[1], customerPage > 0))
            customerPage--;
        Text.DrawAnchored($"Page {customerPage + 1} of {pages}  ({types.Count} types)", r.X + w / 2, y + bh / 2, Text.GameFont[0],
            Anchor.Center, Color.White, Color.Black, 2);
        if (Widgets.Button(next, "Next >", Text.GameFont[1], customerPage < pages - 1))
            customerPage++;
        y += bh + gap;
        var back = new Rectangle(r.X + pad, y, w - pad * 2, bh);
        cheatRects["custback"] = back;
        if (Widgets.Button(back, "< Back to cheats", Text.GameFont[1]))
            cheatsCustomerPage = false;
        y += bh + 4;
        string status = !open ? "The cafe is closed: click the doorway to open it first."
            : customerNoteTime > 0 && customerNote != null ? customerNote
            : customerHire ? "They join the staff at once, in the cafe."
            : "\"Not yet\": would not come by itself at your level and rating.";
        Widgets.Label(status, r.X + w / 2, y + 2, 15, Anchor.Top | Anchor.HCenter, open ? Widgets.Gold : new Color(255, 150, 130, 255));
    }

    /// <summary>A customer type's face (its parts sheet's head, K 0x42d618 - the chef store's pictures).</summary>
    SubImage? CustomerFace(CharacterDef d, bool zombie = false)
    {
        var name = d.PartsSheet(zombie);
        var parts = data.Sheet(name, name + ".bin");
        int i = data.K.Int(0x42d618);
        return i < parts.Length ? parts[i] : null;
    }

    /// <summary>The menu at the left, under the top bar: the speeds, then the cheats two to a row.</summary>
    void DrawCheats()
    {
        cheatRects.Clear();
        if (cheatsTimePage)
        {
            DrawTimeCheats();
            return;
        }
        if (cheatsCustomerPage)
        {
            DrawCustomerCheats();
            return;
        }
        const float w = 460, pad = 16, bw = (w - pad * 3) / 2, bh = 40, gap = 7;
        bool toggles = cheatsTab == 4;
        var actions = CheatActions(cheatsTab);
        int count = toggles ? CheatToggles.Length : actions.Count;
        int rows = (count + 1) / 2;
        float h = 58 + 44 + (cheatsTab == 0 ? 30 + 46 : 0) + rows * (bh + gap) + pad + (toggles ? 24 : 0);
        var r = new Rectangle(90, 70, w, h);
        Widgets.Panel(r);
        Widgets.Label("CHEATS", r.X + pad, r.Y + 12, Text.GameFont[3], Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label("0 to close", r.X + w - pad, r.Y + 18, Text.GameFont[0], Anchor.Top | Anchor.Right);

        // the tabs
        float y = r.Y + 56;
        float tw = (w - pad * 2 - 4 * 6) / CheatTabs.Length;
        for (int i = 0; i < CheatTabs.Length; i++)
        {
            var b = new Rectangle(r.X + pad + i * (tw + 6), y, tw, 36);
            cheatRects["tab" + CheatTabs[i].ToLowerInvariant()] = b;
            if (Widgets.Button(b, CheatTabs[i], Text.GameFont[0], cheatsTab != i))
            {
                cheatsTab = i;
                game.Sound(36);
            }
            if (cheatsTab == i)
                Gfx.Draw.Rect(new Rectangle(b.X, b.Y + b.Height - 4, b.Width, 4), Widgets.Gold);
        }
        y += 44;

        if (cheatsTab == 0)
        {
            // the speed, five steps (the HUD shows it under OPEN / CLOSED when not 1x)
            Widgets.Label($"Speed: x{Cheats.Speed:0.#}", r.X + pad, y, Text.GameFont[1]);
            y += 30;
            float sw = (w - pad * 2 - gap * (CheatSpeeds.Length - 1)) / CheatSpeeds.Length;
            for (int i = 0; i < CheatSpeeds.Length; i++)
            {
                float v = CheatSpeeds[i];
                var b = new Rectangle(r.X + pad + i * (sw + gap), y, sw, 38);
                cheatRects["speed" + v] = b;
                if (Widgets.Button(b, "x" + v, Text.GameFont[1], Cheats.Speed != v))
                {
                    Cheats.Speed = v;
                    game.Sound(36);
                }
            }
            y += 46;
        }

        if (toggles)
        {
            for (int i = 0; i < CheatToggles.Length; i++)
            {
                var (key, label, get, set) = CheatToggles[i];
                bool on = get();
                var b = new Rectangle(r.X + pad + i % 2 * (bw + pad), y + i / 2 * (bh + gap), bw, bh);
                cheatRects[key] = b;
                if (Widgets.Button(b, $"{label}: {(on ? "ON" : "off")}", Text.GameFont[0]))
                {
                    set(!on);
                    game.Sound(36);
                }
                if (on)
                    Gfx.Draw.Rect(new Rectangle(b.X, b.Y + b.Height - 4, b.Width, 4), new Color(120, 255, 120, 255));
            }
            Widgets.Label("Toggles stay on until switched off or the game closes.", r.X + w / 2, r.Y + h - pad - 18, 15,
                Anchor.Top | Anchor.HCenter, new Color(210, 210, 210, 255));
            return;
        }
        for (int i = 0; i < actions.Count; i++)
        {
            var (key, label, enabled, run) = actions[i];
            var b = new Rectangle(r.X + pad + i % 2 * (bw + pad), y + i / 2 * (bh + gap), bw, bh);
            cheatRects[key] = b;
            if (Widgets.Button(b, label, Text.GameFont[0], enabled))
            {
                run();
                game.Sound(36);
            }
        }
    }
}
