using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The cheat menu (the 0 key, a port addition): the game's speed, toxin and cash, a level up, the rating, every stove
/// done, the staff rested, all recipes and cookbooks, a customer at the door, the rival cafes open again, and the
/// tutorial skipped. What it changes is saved like anything else.
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

    /// <summary>The cheats as (key, label, can be used now, what it does), two to a row.</summary>
    List<(string Key, string Label, bool Enabled, Action Run)> CheatActions()
    {
        var p = game.Player;
        return
        [
            ("toxin10", "+10 toxin", true, () => p.Toxin += 10),
            ("toxin100", "+100 toxin", true, () => p.Toxin += 100),
            ("cash1k", "+$1,000", true, () => p.Money += 1000),
            ("cash10k", "+$10,000", true, () => p.Money += 10000),
            ("levelup", "Level up", p.Level < data.K.PlayerMaxLevel, CheatLevelUp),
            ("rating", "Max rating", p.Rating < 1f, () => p.Rating = 1f),
            ("finish", "Finish cooking", cafe.Objects.OfType<Stove>().Any(s => s.Busy && !s.IsDone && s.Dish != null), CheatFinishCooking),
            ("rest", "Rest the staff", true, CheatRestStaff),
            ("recipes", "All recipes", true, CheatAllRecipes),
            ("customer", "Customer now", cafe.IsOpen, () => game.SpawnCustomer(true)),
            ("customers", "Send customer >", true, () => cheatsCustomerPage = true),
            ("rivals", "Reopen rivals", p.Neighbours.Timers.Any(t => t > 0), () => Array.Clear(p.Neighbours.Timers)),
            ("tutorial", "Skip tutorial", !game.Hints.TutorialDone, CheatSkipTutorial),
            ("time", "Time skip >", SkipTime != null, () => cheatsTimePage = true),
        ];
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
        foreach (var book in data.Cookbooks)
            if (book.Index > 0)
                p.Cookbooks.Add(book.Index);
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
        var types = game.CustomerTypes();
        if (!customerShowAll)
            types = types.Where(game.CanVisitNow).ToList();
        int pages = Math.Max(1, (types.Count + perPage - 1) / perPage);
        customerPage = Math.Clamp(customerPage, 0, pages - 1);
        float h = 58 + bh + gap + rowsPer * (cardH + gap) + bh + gap + bh + pad + 26;
        var r = new Rectangle(90, 70, w, h);
        Widgets.Panel(r);
        Widgets.Label("SEND A CUSTOMER", r.X + pad, r.Y + 12, Text.GameFont[3], Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label("0 to close", r.X + w - pad, r.Y + 18, Text.GameFont[0], Anchor.Top | Anchor.Right);
        if (customerNoteTime > 0)
            customerNoteTime -= Raylib.GetFrameTime();
        float y = r.Y + 58;
        // which types, and how many at a time
        var now = new Rectangle(r.X + pad, y, 150, bh);
        var all = new Rectangle(r.X + pad + 158, y, 150, bh);
        cheatRects["custnow"] = now;
        cheatRects["custall"] = all;
        if (Widgets.Button(now, "Can come now", Text.GameFont[0], customerShowAll))
        {
            customerShowAll = false;
            customerPage = 0;
        }
        if (Widgets.Button(all, "All types", Text.GameFont[0], !customerShowAll))
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
        bool open = cafe.IsOpen;
        int first = customerPage * perPage;
        for (int i = 0; i < perPage && first + i < types.Count; i++)
        {
            var d = types[first + i];
            var b = new Rectangle(r.X + pad + i % cols * (cardW + gap), y + i / cols * (cardH + gap), cardW, cardH);
            cheatRects["cust" + i] = b;
            if (Widgets.Button(b, "", Text.GameFont[0], open))
            {
                for (int n = 0; n < customerCount; n++)
                    game.SpawnCustomer(true, d);
                game.Sound(36);
                customerNote = $"Sent {(customerCount == 1 ? "a" : customerCount.ToString())} {d.Name}";
                customerNoteTime = 3f;
            }
            if (CustomerFace(d) is { } face)
            {
                float s = MathF.Min(52f / Math.Max(1, face.FullWidth), 52f / Math.Max(1, face.FullHeight));
                face.Draw(b.X + 32, b.Y + cardH / 2, Anchor.Center, open ? Color.White : new Color(150, 150, 150, 255), Flip.None, s, s);
            }
            string name = d.Name.Length > 15 ? d.Name[..14] + "." : d.Name;
            Widgets.Label(name, b.X + 62, b.Y + 8, Text.GameFont[0]);
            // when it comes by itself: the level, and the rating in stars when it needs one
            float stars = d.F00 * 5;
            string when = $"Lv {Math.Max(1, (int)d.B20)}" + (stars > 0 ? $", {stars:0.#} star{(stars == 1 ? "" : "s")}" : "");
            bool can = game.CanVisitNow(d);
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
            : "\"Not yet\": would not come by itself at your level and rating.";
        Widgets.Label(status, r.X + w / 2, y + 2, 15, Anchor.Top | Anchor.HCenter, open ? Widgets.Gold : new Color(255, 150, 130, 255));
    }

    /// <summary>A customer type's face (its parts sheet's head, K 0x42d618 - the chef store's pictures).</summary>
    SubImage? CustomerFace(CharacterDef d)
    {
        var name = d.PartsSheet(false);
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
        var actions = CheatActions();
        const float w = 430, pad = 16, bw = (w - pad * 3) / 2, bh = 42, gap = 8;
        int rows = (actions.Count + 1) / 2;
        float h = 58 + 30 + 46 + rows * (bh + gap) + pad;
        var r = new Rectangle(90, 70, w, h);
        Widgets.Panel(r);
        Widgets.Label("CHEATS", r.X + pad, r.Y + 12, Text.GameFont[3], Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label("0 to close", r.X + w - pad, r.Y + 18, Text.GameFont[0], Anchor.Top | Anchor.Right);

        // the speed, five steps (the HUD shows it under OPEN / CLOSED when not 1x)
        float y = r.Y + 58;
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
        for (int i = 0; i < actions.Count; i++)
        {
            var (key, label, enabled, run) = actions[i];
            var b = new Rectangle(r.X + pad + i % 2 * (bw + pad), y + i / 2 * (bh + gap), bw, bh);
            cheatRects[key] = b;
            if (Widgets.Button(b, label, Text.GameFont[1], enabled))
            {
                run();
                game.Sound(36);
            }
        }
    }
}
