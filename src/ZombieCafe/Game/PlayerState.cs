using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>The player's progress (the ZombieCafe game object fields at +0x1b0.. and the toxin count).</summary>
public sealed class PlayerState
{
    /// <summary>+0x1b0.</summary>
    public int Money { get; set; }
    /// <summary>+0xd4.</summary>
    public int Toxin { get; set; }
    /// <summary>+0x1b4.</summary>
    public int Level { get; set; } = 1;
    /// <summary>+0x1b8: experience towards the next level.</summary>
    public int Xp { get; set; }
    /// <summary>+0x1bc: cafe rating 0..1 (drives how often customers come and who they are).</summary>
    public float Rating { get; set; }
    /// <summary>The rival restaurants: their clocks, the one stealing customers (+0x1c4 .. +0x1de).</summary>
    public Neighbourhood Neighbours { get; set; }
    /// <summary>+0x178: the chef the player picked.</summary>
    public CharacterDef Chef { get; set; }
    /// <summary>+0x2c0: the tutorial and tips.</summary>
    public HintManager Hints { get; } = new();
    /// <summary>DAT_0042cd80: the tombstones' powers.</summary>
    public Boosts Boosts { get; } = new();
    /// <summary>+0x4b8: the bonus quests (their tasks only count once the tip about them is shown, 0x53).</summary>
    public Objectives Objectives { get; } = new();
    /// <summary>K 0x42d450: the rating each level of the quests' bonus adds.</summary>
    readonly float bonusPerLevel;
    /// <summary>+0x34: the kinds of zombie the player has made (the ZombiePedia's entries).</summary>
    public HashSet<int> ZombieKinds { get; } = new();
    /// <summary>+0x24: the recipes and variants the player can cook, as recipe * 10 + variant.</summary>
    public HashSet<int> Recipes { get; } = new();
    /// <summary>+0x394: the chefs bought in the chef store (character type indices; free ones need no buying).</summary>
    public HashSet<int> OwnedChefs { get; } = new();
    /// <summary>+0x3a8: chefs unlocked early (beating a boss chef in a raid); the rest unlock at their level.</summary>
    public HashSet<int> UnlockedChefs { get; } = new();
    /// <summary>
    /// Cafe +0x108 (cookbook +0x1a): the cookbooks bought; the first three (favourites, general, raid) are everyone's.
    /// </summary>
    public HashSet<int> Cookbooks { get; } = new();

    /// <summary>A cookbook's recipes can be had (FUN_00138d10's test).</summary>
    public bool OwnsCookbook(int book) => book < 3 || Cookbooks.Contains(book);
    /// <summary>Game +0x68: the starred recipes - the favourites book (fifteen at most, food +0x22 marks them).</summary>
    public List<int> Favorites { get; } = new();
    /// <summary>+0x1a0: dishes brought home from a raid, on their way to the fridge (stocked when the cafe loads).</summary>
    public List<Dish> RaidLoot { get; } = new();
    /// <summary>+0x34c: the fridge's stock - raid loot and gifts, to serve or to learn the recipe of.</summary>
    public List<Dish> Fridge { get; } = new();
    /// <summary>+0x20e: the one-time toxin for a cafe's first raid won at level 3 with a 30% rating.</summary>
    public bool RaidBonusGiven { get; set; }
    /// <summary>
    /// +0x188: the pets (their records - a pet's number is its place here, record +0x48), one for each habitat in the
    /// cafe; +0x194: the pets whose habitats were put in the storage box, waiting for them to be placed again.
    /// </summary>
    public List<Character> Pets { get; } = new();
    public List<Character> StoredPets { get; } = new();

    /// <summary>
    /// Game +0x3e0 / +0x3d8: the Amptron's summons left (three a day; a new game starts with none) and the day they
    /// were last filled or ran out.
    /// </summary>
    public int AmptronUses { get; set; }
    /// <summary>Game +0x3c0: the most recipes the player has had unlocked (the milestones' XP is paid once).</summary>
    public int UnlockedBest { get; set; }
    public DateTime AmptronDay { get; set; } = GameClock.Today;

    /// <summary>FUN_00025c78: used up on an earlier day, the Amptron has its three summons again.</summary>
    public void RechargeAmptron()
    {
        if (AmptronUses != 0 || GameClock.Today <= AmptronDay.Date)
            return;
        AmptronUses = 3;
        AmptronDay = GameClock.Today;
    }

    /// <summary>
    /// Game +0x2c4 / +0x2e8 (Frequent Fryer, FUN_000d9bc4): the last day the cafe was played - null before the first -
    /// and how many days in a row it has been.
    /// </summary>
    public DateTime? LastPlayDay { get; set; }
    public int StreakDays { get; set; }

    /// <summary>Game +0x16c: the zombies frozen in the meat locker - off the staff, kept as they were.</summary>
    public List<Character> Frozen { get; } = new();
    /// <summary>Game +0x80: the meat locker's hooks (five to start; five more for 5 toxin, up to sixty).</summary>
    public int LockerSize { get; set; } = 5;

    /// <summary>K 0x42d5b0: how many pets a cafe of this level may keep.</summary>
    public int PetLimit(GameData data) => data.K.Ints(0x42d5b0) is var t && Level - 1 >= 0 && Level - 1 < t.Length ? t[Level - 1] : 0;

    public bool HasRecipe(int food, int variant = 0) => Recipes.Contains(food * 10 + variant);
    /// <summary>FUN_00058e8c.</summary>
    public void UnlockRecipe(int food, int variant) => Recipes.Add(food * 10 + variant);
    /// <summary>Some variant of the recipe is known.</summary>
    public bool KnowsRecipe(int food)
    {
        for (int v = 0; v < 10; v++)
            if (Recipes.Contains(food * 10 + v))
                return true;
        return false;
    }

    /// <summary>
    /// The recipes a cafe of this level would have been given (FUN_0004e1f0's two, then one per level,
    /// DAT_0042d5ac): for saves from before recipes had to be unlocked.
    /// </summary>
    public void GrantRecipesUpToLevel(GameData data)
    {
        UnlockRecipe(0, 0);
        UnlockRecipe(1, 0);
        var table = data.K.Ints(0x42d5ac);
        for (int l = 1; l <= Level && l - 1 < table.Length; l++)
            if (table[l - 1] >= 0 && table[l - 1] < data.Foods.Count)
                UnlockRecipe(table[l - 1], 0);
    }

    public PlayerState(GameData data, CharacterDef chef)
    {
        // FUN_0004e1f0: a new game
        Money = data.K.Int(0x42d628);
        Toxin = data.K.Int(0x42d620);
        Rating = data.K.Float(0x42d624);
        Chef = chef;
        bonusPerLevel = data.K.Float(0x42d450);
        var rng = int.TryParse(Environment.GetEnvironmentVariable("ZC_SEED"), out int seed) ? new Random(seed) : new Random();
        Neighbours = Neighbourhood.NewGame(data, Level, rng);
        // FUN_0004e1f0: Mystery Meat and Hobo Delight to start with
        UnlockRecipe(0, 0);
        UnlockRecipe(1, 0);
        // and the first bonus quest (FUN_001a736c + FUN_001a7718)
        Objectives.Generate(data, this, rng);
    }

    /// <summary>FUN_00054434: the rating, less a competitor's take, plus the quests' bonus (a whole rating at most).</summary>
    public float EffectiveRating => Math.Min(1f, Rating + Neighbours.Penalty + bonusPerLevel * Math.Min(Objectives.Level, 3));

    /// <summary>Experience needed to finish the current level.</summary>
    public int XpForNextLevel(GameData data)
    {
        var t = data.K.PlayerLevelXp;
        return Level >= 0 && Level < t.Length ? t[Level] : int.MaxValue;
    }

    /// <summary>
    /// FUN_000d1f20: level up once enough experience has come in. Returns true on a level-up. A level-up waits
    /// while hints are queued, and until the tutorial's table and chair are bought (hints 0x11 and 0x12 once
    /// 0x10 has been shown); at levels 4, 6, 8 and 9 it waits for that level's own tip and for the tutorial
    /// arrow (hint manager +0x5dc) to be put away.
    /// </summary>
    public bool CheckLevelUp(GameData data, bool arrowUp)
    {
        var h = Hints;
        if (h.IsShown(0x10) && !(h.IsShown(0x11) && h.IsShown(0x12)))
            return false;
        // (level, tip, its follow-up): the follow-up is marked once the tip has been read
        (int Level, int Tip, int Done)[] gates = [(4, 0x48, 0x49), (6, 0x53, 0x54), (8, 0x58, 0x59), (9, 0x56, 0x57)];
        bool gated = false;
        foreach (var g in gates)
        {
            if (Level != g.Level || h.IsShown(g.Done))
                continue;
            if (!h.IsShown(g.Tip) || arrowUp)
                return false;
            h.MarkShown(g.Done);
            gated = true;
        }
        if (!gated && h.Pending)
            return false;
        if (Level == data.K.PlayerMaxLevel)
        {
            Xp = 0;
            return false;
        }
        int need = XpForNextLevel(data);
        if (need > Xp)
            return false;
        Level++;
        Xp -= need;
        return true;
    }
}
