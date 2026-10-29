using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>
/// The rival restaurants around the player's cafe (game +0x1c4 .. +0x1de, +0x1ac, +0x2d4 .. +0x2e2).
/// Rivals are numbered by level: the map shows those of levels player level - 2 .. + 1, and level L lives in slot
/// L mod 4 (K 0x42d594). A slot's timer counts down while it is shut after a raid (K 0x42d5b8: 4 to 18 hours), and
/// on through negative time while it is open. The rival across the street (+0x1d8) that has been open for a day
/// (K 0x42d5bc) starts stealing the player's customers (-0.1 rating, K 0x42d5c0) until it is raided.
/// Once a day at most a boss restaurant may move in for four hours (+0x1c8, FUN_001da2a0) in place of the
/// highest-level rival on the map.
/// </summary>
public sealed class Neighbourhood
{
    public const int Slots = 4;
    /// <summary>0x88ca6c00: a slot that has been open long enough and is left alone.</summary>
    public const int Settled = -2_000_000_000;

    /// <summary>+0x1c4: ms; above 0 shut (reopens at 0), 0 or below open for that long.</summary>
    public int[] Timers { get; } = new int[Slots];
    /// <summary>+0x1cc: the kind of restaurant in each slot (an enemyCafeData index).</summary>
    public byte[] Types { get; } = new byte[Slots];
    /// <summary>+0x1d0: who runs it (a first name, for restaurants without their own name).</summary>
    public string[] Owners { get; } = new string[Slots];
    /// <summary>+0x1c8: ms the boss restaurant stays (it is there while 0 &lt; timer &lt; K 0x42d59c).</summary>
    public int BossTimer { get; set; }
    /// <summary>+0x1ac: the boss restaurant due next (an index into the boss list).</summary>
    public int BossIndex { get; set; }
    /// <summary>+0x2d4: the day the last boss restaurant came (one a day at most).</summary>
    public DateTime BossDay { get; set; }
    /// <summary>+0x2dc: when the boss dice were last rolled (once a minute at most).</summary>
    public DateTime BossRolled { get; set; }
    /// <summary>+0x1d4: rating taken by a competitor (added to the cafe's rating, FUN_00054434).</summary>
    public float Penalty { get; set; }
    /// <summary>+0x1d8: the level of the rival across the street.</summary>
    public int CurrentLevel { get; set; }
    /// <summary>+0x1dc: that rival is stealing customers.</summary>
    public bool Stealing { get; set; }
    /// <summary>+0x1de: the one-off laboratory restaurant has opened (level 10).</summary>
    public bool LabOpened { get; set; }

    public static int SlotOf(int level) => ((level % Slots) + Slots) % Slots;

    /// <summary>FUN_0004e1f0: a new game's neighbours - all open, plain cafes with random owners.</summary>
    public static Neighbourhood NewGame(GameData data, int level, Random rng)
    {
        var n = new Neighbourhood { CurrentLevel = level };
        for (int i = 0; i < Slots; i++)
            n.Owners[i] = Character.RandomName(data, false, rng);
        return n;
    }

    /// <summary>The levels the map shows: two below the player's to one above (at least 1).</summary>
    public static List<int> MapLevels(int playerLevel)
    {
        var list = new List<int>();
        for (int l = Math.Max(1, playerLevel - 2); l < playerLevel + 2; l++)
            list.Add(l);
        return list;
    }

    public bool IsShut(int level) => Timers[SlotOf(level)] > 0;
    public int TimerOf(int level) => Timers[SlotOf(level)];
    /// <summary>The kind of restaurant at a level (capped to the registry, like the map's load).</summary>
    public int TypeOf(GameData data, int level) => Math.Min(Types[SlotOf(level)], data.EnemyCafes.Count - 1);

    /// <summary>
    /// FUN_001dae74 / FUN_001daf94: the name the map and the raid use - the restaurant's own first part, or
    /// "Owner's"; a "Chez" puts its name first ("Chez Owner's").
    /// </summary>
    public string NameOf(GameData data, int type, int level)
    {
        var t = data.EnemyCafes[type];
        string first = t.Prefix.Length > 0 ? t.Prefix : Owners[SlotOf(level)] + "'s";
        return t.Name == "Chez" ? t.Name + " " + first : first + " " + t.Name;
    }

    // strings.bin: the restaurants' names (347 + type) and the special owners' (FUN_00053ea8)
    const int TextCafeTypes = 347, TextGoblin = 632, TextDeadbeard = 633, TextDrac = 634;

    /// <summary>
    /// FUN_00053ea8: the name a reopened restaurant gets on the map, from the string table rather than the
    /// registry ("Owner's Diner", "Chez Owner", and the three that bring their own owner).
    /// </summary>
    public string LocalNameOf(GameData data, int level)
    {
        int slot = SlotOf(level), type = Types[slot];
        string kind = data.Strings[TextCafeTypes + type];
        return type switch
        {
            6 => kind + " " + Owners[slot],
            10 => kind + " " + data.Strings[TextGoblin],
            11 => data.Strings[TextDeadbeard] + "'s " + kind,
            12 => data.Strings[TextDrac] + "'s " + kind,
            _ => Owners[slot] + "'s " + kind,
        };
    }

    /// <summary>ZombieCafe::update, every frame: the slots' clocks, and the rival across the street turning on the player.</summary>
    public void Update(GameData data, PlayerState player, int dtMs, Random rng)
    {
        var shut = data.K.Ints(0x42d5b8);
        for (int i = 0; i < Slots; i++)
        {
            int was = Timers[i];
            if (was == Settled)
                continue;
            long next = (long)was - dtMs;
            // a clock put back leaves nothing shut for longer than the longest closure
            Advance(data, player, i, was, next > shut[^1] ? 0 : (int)Math.Max(next, int.MinValue + 1L), rng);
        }
        if (BossTimer > 0)
            BossTimer -= dtMs;
    }

    /// <summary>
    /// FUN_00057d90: the time the game was closed. Two days or more away leaves every open restaurant at the end
    /// of its day (the next frame settles it); the boss restaurant's stay runs down as well.
    /// </summary>
    public void Away(GameData data, PlayerState player, double seconds, Random rng)
    {
        if (seconds <= 0)
            return;
        int day = data.K.Int(0x42d5bc);
        for (int i = 0; i < Slots; i++)
        {
            int was = Timers[i];
            if (was == Settled)
                continue;
            int now = seconds < day / 1000 * 2 ? (int)Math.Max(was - seconds * 1000.0, int.MinValue + 1.0) : -day;
            Advance(data, player, i, was, now, rng);
        }
        if (BossTimer > 0 && BossTimer <= data.K.Int(0x42d59c))
            BossTimer = (int)Math.Max(BossTimer - seconds * 1000.0, int.MinValue + 1.0);
    }

    void Advance(GameData data, PlayerState player, int slot, int was, int now, Random rng)
    {
        int day = data.K.Int(0x42d5bc);
        Timers[slot] = now;
        if (now < -day)
        {
            if (slot == SlotOf(CurrentLevel) && player.Hints.IsShown(0x22))
            {
                Stealing = true;
                Penalty = data.K.Float(0x42d5c0);
            }
            Timers[slot] = Settled;
            now = Settled;
        }
        if (now < 1 && was > 0)
            Reopen(data, player, slot, rng);
    }

    /// <summary>FUN_00052cbc: a new restaurant moves into an emptied slot (one the player's level allows).</summary>
    public void Reopen(GameData data, PlayerState player, int slot, Random rng)
    {
        var candidates = data.EnemyCafes.Where(c => !c.Boss && c.MinLevel <= player.Level).Select(c => c.Index).ToList();
        Types[slot] = (byte)(candidates.Count == 0 ? 0 : candidates[rng.Next(candidates.Count)]);
        if (player.Level > 9 && !LabOpened)
        {
            LabOpened = true;
            Types[slot] = 7;
        }
        Owners[slot] = Character.RandomName(data, false, rng);
    }

    /// <summary>
    /// FUN_00157a10 (a raid won): the restaurant shuts for 4 to 18 hours - longer the further it is below the
    /// player's level (K 0x42d5b8 by level + 2 - player level). If it was the one across the street, it stops
    /// stealing customers and the rival at the player's own level takes over the street (its clock restarted if it
    /// had been open a while).
    /// </summary>
    /// <param name="mapLevel">The level the map showed it at (its slot).</param>
    /// <param name="level">That level capped at 100 (what the tables and the street go by).</param>
    public void Beaten(GameData data, PlayerState player, int mapLevel, int level)
    {
        var shut = data.K.Ints(0x42d5b8);
        Timers[SlotOf(mapLevel)] = shut[Math.Clamp(level + 2 - player.Level, 0, shut.Length - 1)];
        if (level != CurrentLevel)
            return;
        if (level < player.Level && Timers[SlotOf(player.Level)] < 0)
            Timers[SlotOf(player.Level)] = 0;
        Penalty = 0;
        CurrentLevel = player.Level;
    }

    /// <summary>FUN_0008d288: pay toxin (K 0x42d6e4) to have a shut restaurant open again in a second.</summary>
    public bool PayToReopen(GameData data, PlayerState player, int level)
    {
        int cost = data.K.Int(0x42d6e4);
        if (player.Toxin < cost || !IsShut(level))
            return false;
        player.Toxin -= cost;
        Timers[SlotOf(level)] = 1000;
        return true;
    }

    // ---------------------------------------------------------------- the boss restaurant

    /// <summary>FUN_001da168: the registry's boss restaurants, in order.</summary>
    public static List<int> BossList(GameData data) => data.EnemyCafes.Where(c => c.Boss).Select(c => c.Index).ToList();

    /// <summary>FUN_001da250: a boss restaurant is on the map.</summary>
    public bool BossPresent(GameData data) => BossTimer > 0 && BossTimer < data.K.Int(0x42d59c);

    /// <summary>FUN_001da1dc: the boss restaurant due (or there).</summary>
    public int BossType(GameData data)
    {
        var list = BossList(data);
        return list.Count == 0 ? 0 : list[Math.Clamp(BossIndex, 0, list.Count - 1)];
    }

    /// <summary>FUN_001da21c: the next boss restaurant in turn.</summary>
    public void NextBoss(GameData data)
    {
        int n = BossList(data).Count;
        BossIndex = n == 0 ? 0 : (BossIndex + 1) % n;
    }

    /// <summary>
    /// FUN_001da2a0 (opening the map with no boss restaurant around): not on a day one has already come, and at
    /// most once a minute (K 0x42d020 s), roll the level's chance (K 0x42d680, percent). On a hit the boss
    /// restaurant stays four hours (K 0x42d59c) - the first in turn the player's level allows; none allowed, none comes.
    /// </summary>
    public bool RollForBoss(GameData data, PlayerState player, DateTime now, Random rng)
    {
        if (now.Date == BossDay.Date || (now - BossRolled).TotalSeconds <= data.K.Int(0x42d020))
            return false;
        BossRolled = now;
        var chance = data.K.Ints(0x42d680);
        int pct = chance[Math.Clamp(player.Level - 1, 0, chance.Length - 1)];
        if (rng.Next(100) >= pct)
            return false;
        BossDay = now;
        BossTimer = data.K.Int(0x42d59c);
        var list = BossList(data);
        for (int tries = 0; tries < list.Count; tries++)
        {
            if (data.EnemyCafes[BossType(data)].MinLevel <= player.Level)
                return true;
            NextBoss(data);
        }
        return false;
    }
}
