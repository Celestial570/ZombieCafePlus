using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>
/// The bonus quests (game +0x4b8, 0x20 bytes, FUN_001a736c; the purple-star HUD button 13 from level 6): four
/// tasks at a time; with all four done and ticked off, a critic reviews the cafe and the rating gets a bonus -
/// a fifth of a star per review in a row, three at most (FUN_001a81e4) - that lasts two days for the first review,
/// a day and a half for the second, a day for the third and on (K 0x42d43c, a week at most, K 0x42d464). When a
/// level's time runs out the bonus drops a level and starts that level's time again. Unfinished tasks can be
/// bribed through for two vials each (K 0x42d468).
/// </summary>
public sealed class Objectives
{
    /// <summary>One task (0x10 bytes): what kind (strings 589 + type), how far, how far to go, its recipe, ticked off.</summary>
    public sealed class Goal
    {
        /// <summary>+0: 0 raid enemy cafes, 1 raid friends, 2 raid any, 3 serve customers, 4 cook a recipe,
        /// 5 invite friends, 6 spend money, 7 collect fees from friends, 8 give gifts, 9 order from friends.</summary>
        public short Type { get; set; }
        /// <summary>+4.</summary>
        public int Progress { get; set; }
        /// <summary>+8.</summary>
        public int Target { get; set; }
        /// <summary>+0xc: the recipe a cooking task is about (-1 for the others).</summary>
        public short Param { get; set; } = -1;
        /// <summary>+0xe: ticked off in the panel (FUN_001a7d4c).</summary>
        public bool Claimed { get; set; }

        public bool IsComplete => Progress >= Target;

        public void Add(int n)
        {
            if (Progress < Target)
                Progress = Math.Min(Target, Progress + n);
        }
    }

    public const short RaidEnemy = 0, RaidFriend = 1, RaidAny = 2, Serve = 3, Cook = 4, Invite = 5, Spend = 6;

    /// <summary>+0x04.</summary>
    public List<Goal> Goals { get; } = new();
    /// <summary>+0x10: the current bonus level's full time, ms (-1: no bonus).</summary>
    public int Total { get; set; } = -1;
    /// <summary>+0x14: what is left of it, ms (-1: no bonus).</summary>
    public int Remaining { get; set; } = -1;
    /// <summary>+0x18: reviews in a row the bonus stands on.</summary>
    public int Level { get; set; }
    /// <summary>+0x1c: when these tasks were set (Unix seconds).</summary>
    public long SetAtUnix { get; set; }

    static long Now => GameClock.UnixSeconds;

    /// <summary>FUN_001a81e4: the rating the bonus adds (0.2 a level, K 0x42d450, up to three levels).</summary>
    public float RatingBonus(GameData data) => data.K.Float(0x42d450) * Math.Min(Level, 3);

    /// <summary>FUN_001a7d1c: tasks done.</summary>
    public int CompleteCount => Goals.Count(g => g.IsComplete);
    /// <summary>FUN_001a7cc8: tasks ticked off.</summary>
    public int ClaimedCount => Goals.Count(g => g.Claimed);
    /// <summary>FUN_001a7cf0: every task done.</summary>
    public bool AllComplete => Goals.All(g => g.IsComplete);
    /// <summary>FUN_001a7ca0: every task ticked off (the review can be had).</summary>
    public bool AllClaimed => Goals.All(g => g.Claimed);

    /// <summary>FUN_001a8198 (the HUD button's badge): a task to tick off, or the review to get.</summary>
    public bool Ready => Goals.Any(g => g.IsComplete && !g.Claimed) || AllClaimed;

    /// <summary>FUN_001a7718: a new set of four tasks for the player's level.</summary>
    public void Generate(GameData data, PlayerState player, Random rng)
    {
        Goals.Clear();
        Goals.Add(RaidGoal());
        Goals.Add(CookGoal(data, player, rng));
        var k = data.K;
        if ((Level & 3) == 0)
        {
            Goals.Add(rng.Next(2) == 1
                ? new Goal { Type = Serve, Target = player.Level * k.Int(0x42d458) }
                : new Goal { Type = Spend, Target = player.Level * k.Int(0x42d454) });
            Goals.Add(SocialGoal());
        }
        else
        {
            Goals.Add(new Goal { Type = Spend, Target = player.Level * k.Int(0x42d454) });
            Goals.Add(new Goal { Type = Serve, Target = player.Level * k.Int(0x42d458) });
        }
        SetAtUnix = Now;
    }

    /// <summary>
    /// FUN_001a7834: raid two enemy cafes (with friends, every fourth set may ask for friends' cafes or any instead;
    /// the port has no friends).
    /// </summary>
    static Goal RaidGoal() => new() { Type = RaidEnemy, Target = 2 };

    /// <summary>
    /// FUN_001a7c34: without friends, "invite a friend" (with friends: collect fees, give gifts, order or invite).
    /// The port has no friends to invite, so the task comes done.
    /// </summary>
    static Goal SocialGoal() => new() { Type = Invite, Target = 1, Progress = 1 };

    /// <summary>
    /// FUN_001a78b4: cook a recipe - one of the player's (its first known variant that cooks in the bonus time left
    /// less three hours, K 0x42d444), as many batches as twelve hours of its cooking (K 0x42d448), 1 to 5 (K 0x42d44c).
    /// </summary>
    Goal CookGoal(GameData data, PlayerState player, Random rng)
    {
        var k = data.K;
        long remaining = Remaining == -1 ? k.Longs(0x42d43c)[0] : Remaining;
        long limit = remaining / 1000 - k.Long(0x42d444);
        var candidates = new List<(int Food, int Variant)>();
        foreach (var f in data.Foods)
        {
            if (f.Level > player.Level)
                continue;
            for (int v = 0; v < 10; v++)
            {
                if (player.HasRecipe(f.Index, v) && limit >= f.CookSeconds(v))
                {
                    candidates.Add((f.Index, v));
                    break;
                }
            }
        }
        if (candidates.Count == 0)
            candidates.Add((0, 0));
        var pick = candidates[rng.Next(candidates.Count)];
        int cook = Math.Max(1, data.Foods[pick.Food].CookSeconds(pick.Variant));
        int target = Math.Clamp(k.Int(0x42d448) / cook, 1, k.Int(0x42d44c));
        return new Goal { Type = Cook, Target = target, Param = (short)pick.Food };
    }

    /// <summary>
    /// FUN_001a769c, every frame and for time away: the bonus runs down; a level whose time is up drops the bonus a
    /// level and starts that level's time again, until there is none.
    /// </summary>
    public void Tick(GameData data, long dtMs)
    {
        if (Remaining == -1 || dtMs < 1)
            return;
        var times = data.K.Longs(0x42d43c);
        long t = Remaining, rest = dtMs;
        do
        {
            long over = rest > t ? rest - t : 0;
            long left = t - rest;
            if (left < 1 || t <= rest)
            {
                Level = Math.Max(0, Level - 1);
                if (Level == 0)
                {
                    Total = -1;
                    Remaining = -1;
                    return;
                }
                left = times[Math.Min(Level - 1, times.Length - 1)];
                Total = (int)left;
            }
            rest = over;
            t = left;
        }
        while (rest > 0);
        Remaining = (int)t;
    }

    /// <summary>FUN_001a7d68 (GET REVIEW): the review - more bonus time (a week at most), a level up, new tasks.</summary>
    public void Review(GameData data, PlayerState player, Random rng)
    {
        var k = data.K;
        var times = k.Longs(0x42d43c);
        long remaining = Remaining + times[Math.Min(Level, times.Length - 1)];
        Remaining = (int)Math.Min(remaining, k.Long(0x42d464));
        Total = Remaining;
        Level++;
        Generate(data, player, rng);
    }

    /// <summary>FUN_001a6c04's price: two vials (K 0x42d468) for each task not done.</summary>
    public int BribeCost(GameData data) => (Goals.Count - CompleteCount) * data.K.Int(0x42d468);

    /// <summary>FUN_001a7de0: every task done at once.</summary>
    public void CompleteAll()
    {
        foreach (var g in Goals)
            g.Progress = g.Target;
    }

    // ---------------------------------------------------------------- progress (all only once the tip 0x53 is shown)

    /// <summary>FUN_001a7e64: a raid won (a friend's cafe counts for the friend and any tasks, an enemy's for enemy and any).</summary>
    public void RaidWon(bool friend)
    {
        foreach (var g in Goals)
            if (friend ? g.Type is RaidFriend or RaidAny : g.Type is RaidEnemy or RaidAny)
                g.Add(1);
    }

    /// <summary>FUN_001a7e08 (DishToCounterTask, FUN_001bab54): a batch of a recipe reached a counter.</summary>
    public void Cooked(int food)
    {
        foreach (var g in Goals)
            if (g.Type == Cook && g.Param == food)
                g.Add(1);
    }

    /// <summary>FUN_001a7ecc: customers paid.</summary>
    public void Served(int n)
    {
        foreach (var g in Goals)
            if (g.Type == Serve)
                g.Add(n);
    }

    /// <summary>FUN_001a7f74: money spent in the store (toxin counts K 0x42d138 each).</summary>
    public void Spent(int amount)
    {
        foreach (var g in Goals)
            if (g.Type == Spend)
                g.Add(amount);
    }
}
