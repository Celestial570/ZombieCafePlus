using Raylib_cs;
using ZombieCafe.Core;
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

    /// <summary>The menu at the left, under the top bar: the speeds, then the cheats two to a row.</summary>
    void DrawCheats()
    {
        cheatRects.Clear();
        if (cheatsTimePage)
        {
            DrawTimeCheats();
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
