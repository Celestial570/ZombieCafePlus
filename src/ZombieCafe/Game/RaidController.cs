using ZombieCafe.Data;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Game.Cafe.Tasks;

namespace ZombieCafe.Game;

/// <summary>How a raid ended (FUN_00157a10's result for the RaidRewardScreen).</summary>
public enum RaidOutcome
{
    /// <summary>Every rival down: the restaurant shuts, its chef can be hired, the loot comes home.</summary>
    Won = 0,
    /// <summary>Every raider down: the loot is lost.</summary>
    Lost = 1,
    /// <summary>The raiders retreated: they keep what they took.</summary>
    RanAway = 2,
}

/// <summary>
/// A raid on a rival restaurant (GameStateRaid: FUN_001505c0, load 00150a60, update 00155d94, the raiders
/// FUN_00158230, the end FUN_00157a10). The restaurant is built from its layout (enemyLayouts.bin) with furniture
/// of its kind (enemyItems.bin); its chef cooks and its waiters serve customers until the player's zombies come
/// in. Raiders wait in the street until sent in (ATTACK, or a rival tapped); inside they fight whoever they meet,
/// steal food from the counter, and bring down people for money, experience and now and then toxin. The waiters
/// fight back; the chef joins in once they are all down (a boss chef at once). The raid is won when no rival is
/// left standing and nobody is eating inside; it is lost when every raider is down; retreating brings them home.
/// The original kept a raid in progress on disk (raidCafe / raidEnemies) to carry on later; here a raid left
/// half-way starts afresh next time.
/// </summary>
public sealed class RaidController : ICafeEvents
{
    readonly GameData data;
    public Cafe.Cafe Cafe { get; }
    public PlayerState Player { get; }
    /// <summary>The player's cafe the raiders come from (they are its staff).</summary>
    public CafeController Home { get; }
    /// <summary>+0x1ac: the restaurant's name as the map showed it.</summary>
    public string Name { get; }
    /// <summary>+0x30: the restaurant's level.</summary>
    public int MapLevel { get; }
    /// <summary>+0x3c: that level capped at 100 (K 0x42d590) - what the tables are read with.</summary>
    public int Level { get; }
    /// <summary>+0x38: the kind of restaurant.</summary>
    public int Type { get; }
    public EnemyCafeType Rival => data.EnemyCafes[Type];
    public bool IsBoss => Rival.Boss;
    /// <summary>+0x44 / +0x48: the dish on the counter, the raid's prize.</summary>
    public int RewardFood { get; private set; }
    public byte RewardVariant { get; private set; }
    /// <summary>+0x40: the prize as it was put out.</summary>
    Dish? prize;

    /// <summary>+0x104: the rival chef.</summary>
    public CharacterInstance? RivalChef { get; private set; }
    /// <summary>+0xd4: the rival's waiters.</summary>
    public List<CharacterInstance> Staff { get; } = new();
    /// <summary>+200: customers and passers-by.</summary>
    public List<CharacterInstance> Customers { get; } = new();
    /// <summary>+0x88: the player's zombies.</summary>
    public List<CharacterInstance> Raiders { get; } = new();
    /// <summary>+0xa0: the zombie yetis summoned (a tornado that becomes the yeti to fight, for a while).</summary>
    public List<CharacterInstance> Yetis { get; } = new();
    /// <summary>+0xb8: the Amptron, while the robot pets are folded into it.</summary>
    public CharacterInstance? Amptron { get; private set; }
    /// <summary>+0xbc: the robot pets folded into it (one of each kind), with the job each had (pet record +0x49).</summary>
    readonly List<(CharacterInstance Pet, Job Job)> combined = new();
    /// <summary>HUD button 22 lit (+0x8e): the raid began with all five robot pets along (FUN_00155558 at load).</summary>
    public bool AmptronReady { get; private set; }
    /// <summary>+0xac: the Big Bad Wolves fairy-tale zombies have turned into.</summary>
    public List<CharacterInstance> Wolves { get; } = new();
    /// <summary>HUD button 23 lit (+0x8e): a fairy-tale zombie came along (FUN_00155618 at load).</summary>
    public bool WolfReady { get; private set; }
    /// <summary>Game +0x3e4: the transformations left this raid (three).</summary>
    public int WolfUses { get; private set; }
    /// <summary>+0x17c: the stove the chef works.</summary>
    public Stove? Stove { get; private set; }
    /// <summary>+0x2c: the cell in front of it.</summary>
    int chefCell;
    /// <summary>+300: the raider the player has picked.</summary>
    public CharacterInstance? Selected { get; set; }

    /// <summary>+0x54: a raider has got inside.</summary>
    public bool CombatStarted { get; private set; }
    /// <summary>+0x4c: no rival left standing.</summary>
    public bool Won { get; private set; }
    /// <summary>+0x178: the raid is over.</summary>
    public bool Ended { get; private set; }
    /// <summary>+0x148: the raiders are heading home.</summary>
    public bool Retreating { get; private set; }
    /// <summary>+0x20: everything waits while the energize tip shows (hint 0x2b).</summary>
    public bool Paused { get; set; }
    /// <summary>+0x58: the raider the energize tip is about.</summary>
    public CharacterInstance? Tired { get; private set; }
    /// <summary>+0x130 / +0x134: toxin and money the raid brought.</summary>
    public int Toxin { get; private set; }
    public int Money { get; private set; }
    /// <summary>+0x138: dishes stolen.</summary>
    public List<Dish> Loot { get; } = new();
    public RaidOutcome? Outcome { get; private set; }

    int victoryDelay;          // +0x164
    int spawnTimer;            // +0x174
    bool musicStarted;         // +0x179
    bool screamed;             // +0x17a
    // +0x55 / +0x56 / +0x57: this frame - the fight just began, every waiter is down, the chef is down
    bool fightBegan, staffDown, chefDown;

    Random Rng => Cafe.Rng;

    static readonly bool LogEvents = Environment.GetEnvironmentVariable("ZC_LOG") == "1";
    int clockMs;

    /// <summary>Debug trace of the raid (ZC_LOG=1).</summary>
    void Log(string s)
    {
        if (LogEvents)
            Console.WriteLine($"[raid {clockMs / 1000,4}s] {s}");
    }
    public event Action<int>? SoundRequested;
    /// <summary>The original's second music channel (FUN_00053c58 with channel 1): the win / lose / retreat cues.</summary>
    public event Action<int>? MusicCue;
    /// <summary>The raid's music (FUN_00053c58 channel 0: 7, or 19 for a boss).</summary>
    public event Action<int>? Music;
    /// <summary>A rival hit by a raider (the target marker, FUN_0015b068).</summary>
    public event Action<CharacterInstance>? Marked;

    public HintManager Hints => Player.Hints;

    public RaidController(GameData data, CafeController home, string name, int level, int type)
    {
        this.data = data;
        Home = home;
        Player = home.Player;
        Name = name;
        MapLevel = level;
        Level = Math.Clamp(Math.Min(level, data.K.Int(0x42d590)), 1, data.K.Int(0x42d590));
        Type = Math.Clamp(type, 0, data.EnemyCafes.Count - 1);
        var layouts = data.EnemyLayouts;
        int li = Rival.Layout < 0 ? 0 : Math.Min(Rival.Layout, layouts.Count - 1);
        var layout = layouts[li];
        Cafe = global::ZombieCafe.Game.Cafe.Cafe.CreateSized(data, new Random(), layout.Width, layout.Depth);
        Cafe.Events = this;
        PickPrize();
        Furnish(layout);
        AddChef();
        AddStaff();
        AddCustomers();
        AddRaiders();
        // GameStateRaid::load: all five robot pets along light up the Amptron's button, its summons filled on a new day
        if (Robots().Count == RobotPets.Length)
        {
            Player.RechargeAmptron();
            AmptronReady = true;
        }
        // ... and a fairy-tale zombie along lights up the Big Bad Wolf's, three transformations a raid
        if (Raiders.Any(IsFairyTale))
        {
            WolfUses = 3;
            WolfReady = true;
        }
        ResetSpawnTimer();
        // GameStateRaid::load: the first raid's tip, or the reminder about energizing in a fight
        Hint(Player.Hints.IsShown(0x26) ? 0x42 : 0x1f, false);
    }

    T K<T>(int address, int index) where T : struct =>
        typeof(T) == typeof(float)
            ? (T)(object)data.K.Floats(address)[Math.Clamp(index, 0, data.K.Floats(address).Length - 1)]
            : (T)(object)data.K.Ints(address)[Math.Clamp(index, 0, data.K.Ints(address).Length - 1)];

    int ByLevel(int address) => K<int>(address, Level - 1);

    // ---------------------------------------------------------------- setting up (GameStateRaid::load)

    /// <summary>
    /// The prize: once raids have been explained (hint 0x2c), a recipe of the restaurant's own cookbook that an
    /// ordinary stove cooks - not one a level is still to bring, nor a rare one the player lacks - or, one time in
    /// a hundred (K 0x42d3d4), one of the special list (K 0x42d3d0) the player's level allows; a variant at the odds
    /// of K 0x42d3b8 when the recipe is already known. Before that, always recipe 3.
    /// </summary>
    void PickPrize()
    {
        RewardFood = 3;
        RewardVariant = 0;
        if (!Player.Hints.IsShown(0x2c))
            return;
        var levelRecipes = data.K.Ints(0x42d5ac);
        var candidates = new List<int>();
        foreach (var f in data.Foods)
        {
            if (f.Cookbook != Type)
                continue;
            bool locked = levelRecipes.Contains(f.Index) && !Player.HasRecipe(f.Index, 0);
            if (f.Kind == 4)
                locked |= !Player.HasRecipe(f.Index, 0);
            else if (f.Kind > 4)
                continue;
            if (f.B1E == 0 && !locked)
                candidates.Add(f.Index);
        }
        if (candidates.Count == 0)
            return;
        int pick = -1;
        if (Rng.Next(100) < data.K.Int(0x42d3d4))
        {
            var special = data.K.Ints(0x42d3d0).Take(data.K.Int(0x42d3cc))
                .Where(i => i >= 0 && i < data.Foods.Count && data.Foods[i].Level <= Player.Level).ToList();
            if (special.Count > 0)
                pick = special[Rng.Next(special.Count)];
        }
        if (pick < 0)
            pick = candidates[Rng.Next(candidates.Count)];
        RewardFood = pick;
        if (Player.HasRecipe(pick, 0))
        {
            int r = Rng.Next(100);
            var odds = data.K.Ints(0x42d3b8);
            for (int v = 0; v < data.K.Int(0x42d3bc) && v < odds.Length; v++)
                if (r < odds[v])
                {
                    RewardVariant = (byte)v;
                    break;
                }
        }
    }

    /// <summary>
    /// The room: walls and floor of the restaurant's kind (enemyItems.bin columns 7 and 8), then every piece of
    /// the layout, each a random pick of its letter's column. The first stove gets a pot of Hobo Delight going
    /// (FUN_00082614) and marks where the chef stands; the first counter gets the prize.
    /// </summary>
    void Furnish(EnemyLayout layout)
    {
        var items = data.EnemyItems[Math.Min(Type, data.EnemyItems.Count - 1)];
        int u0 = Cafe.OriginU, v0 = Cafe.OriginV, w = layout.Width, d = layout.Depth;
        if (Pick(items.List(EnemyLayout.WallColumn)) is { } wallDef)
        {
            for (int i = 0; i < w; i++)
                Cafe.WallAt(Cafe.IndexOf(u0 - w + i, v0 + 1), 0)?.SetDef(data, wallDef);
            for (int j = 0; j < d; j++)
                Cafe.WallAt(Cafe.IndexOf(u0 - 1, v0 + d - j), 1)?.SetDef(data, wallDef);
        }
        if (Pick(items.List(EnemyLayout.FloorColumn)) is { } floorDef)
            for (int u = u0 - w; u < u0; u++)
                for (int v = v0 + 1; v <= v0 + d; v++)
                    Cafe.SetFloor(Cafe.IndexOf(u, v), floorDef);

        ServingCounter? firstCounter = null;
        foreach (var item in layout.Items)
        {
            int column = EnemyLayout.Column(item.Letter);
            if (column < 0 || Pick(items.List(column)) is not { } def)
                continue;
            int u = u0 - w + item.U, v = v0 + item.V + 1;
            int cell = Cafe.IndexOf(u, v);
            if (cell < 0)
                continue;
            switch (def.Category)
            {
                case FurnitureCategory.Wall:
                case FurnitureCategory.Floor:
                case FurnitureCategory.Cash:
                case FurnitureCategory.Expansion:
                    break;
                case FurnitureCategory.WallDecor:
                    if (item.Orientation < 2 && Cafe.WallAt(cell, item.Orientation) is { Decor: null })
                        Cafe.AddWallDecor(def.Id, u, v, item.Orientation);
                    break;
                default:
                {
                    var f = Cafe.AddFurniture(def.Id, u, v, item.Orientation);
                    if (f is Stove s && Stove == null)
                    {
                        Stove = s;
                        chefCell = Cafe.Neighbour(s.Cell, item.Orientation);
                        s.StartCooking(Cafe, 1, 0, data.PotDef(s.Def.StoveFamily));
                    }
                    else if (f is ServingCounter c && firstCounter == null)
                    {
                        firstCounter = c;
                        prize = Dish.Cook(data, RewardFood, RewardVariant);
                        c.AddDish(Cafe, prize);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>FUN_001dbc14: one of a list at random.</summary>
    FurnitureDef? Pick(int[] list)
    {
        var ok = list.Where(i => i >= 0 && i < data.Furniture.Count).ToArray();
        return ok.Length == 0 ? null : data.Furniture[ok[Rng.Next(ok.Length)]];
    }

    /// <summary>
    /// The chef (enemyCafeData's chef type, FUN_001dbc0c), by the stove: energy, speed and strength from the level
    /// tables (K 0x42d37c / 0x42d390 / 0x42d38c) - a weakling with half the energy on the very first raid, and a
    /// boss chef's energy multiplied by the player's level (K 0x42d684).
    /// </summary>
    void AddChef()
    {
        if (Rival.ChefDef < 0 || Rival.ChefDef >= data.Characters.Count)
            return;
        var def = data.Characters[Rival.ChefDef];
        var ch = new Character(def, Role.Visitor, false, Character.RandomName(data, def.IsFemale, Rng));
        if (!Player.Hints.IsShown(0x26))
        {
            ch.Speed = 1;
            ch.Strength = 1;
            ch.BaseEnergy = ByLevel(0x42d37c) / 2;
        }
        else
        {
            ch.Speed = ByLevel(0x42d390);
            ch.Strength = ByLevel(0x42d38c);
            ch.BaseEnergy = ByLevel(0x42d37c);
            if (IsBoss)
                ch.BaseEnergy *= K<float>(0x42d684, Player.Level - 1);
        }
        ch.Energy = ch.BaseEnergy;
        int cell = chefCell >= 0 && Cafe.IsInside(chefCell) ? chefCell : RandomInsideCell();
        RivalChef = Cafe.AddCharacter(ch, cell);
        RivalChef.IsRivalChef = true;
        RivalChef.Team = 2;
        RivalChef.ShowEnergyBar = true;
    }

    /// <summary>The waiters: K 0x42d36c of them, of the restaurant's staff types, on random free cells inside.</summary>
    void AddStaff()
    {
        int n = ByLevel(0x42d36c);
        for (int i = 0; i < n && Rival.Staff.Length > 0; i++)
        {
            int t = Rival.Staff[Rng.Next(Rival.Staff.Length)];
            if (t < 0 || t >= data.Characters.Count)
                continue;
            var def = data.Characters[t];
            var ch = new Character(def, Role.EnemyStaff, false, Character.RandomName(data, def.IsFemale, Rng));
            ch.BaseEnergy *= 0.5f;
            if (!Player.Hints.IsShown(0x26))
            {
                ch.Speed = 1;
                ch.Strength = 1;
            }
            else
            {
                ch.Speed = ByLevel(0x42d374);
                ch.Strength = ByLevel(0x42d370);
            }
            ch.Energy = ch.BaseEnergy;
            ch.Job = Job.Serving;
            var z = Cafe.AddCharacter(ch, RandomInsideCell());
            z.Team = 2;
            z.ShowEnergyBar = true;
            Staff.Add(z);
        }
    }

    /// <summary>A random cell inside the room that can be walked on.</summary>
    int RandomInsideCell()
    {
        for (int tries = 0; tries < 2000; tries++)
        {
            int u = Cafe.OriginU - 1 - Rng.Next(Math.Max(1, Cafe.RoomWidth));
            int v = Cafe.OriginV + 1 + Rng.Next(Math.Max(1, Cafe.RoomDepth));
            int cell = Cafe.IndexOf(u, v);
            if (cell >= 0 && Cafe.IsInside(cell) && Cafe.Nav.IsPassable(cell))
                return cell;
        }
        return Cafe.DoorCell;
    }

    /// <summary>
    /// FUN_001636a4: the customers already eating when the raiders arrive (K 0x42d368 of them), each played forward
    /// 30 to 59 seconds - long enough to walk in, sit down and be handed something from the counter.
    /// </summary>
    void AddCustomers()
    {
        int n = ByLevel(0x42d368);
        for (int i = 0; i < n; i++)
        {
            var c = SpawnCustomer(true);
            int steps = 30 + Rng.Next(30);
            for (int s = 0; s < steps * 10; s++)
            {
                c.Update(100);
                if (Cafe.Waiting.Contains(c) && c.AnimId == (int)CharAnim.Sit
                    && c.CurrentTask is EatTask { Table: { Busy: false } table } && TakeFromCounter() is { } dish)
                {
                    // FUN_0016d0cc: a plate of it put in front of them (the meal then takes its course)
                    table.Attach(Cafe, new FoodItem(data, dish, 0));
                    table.Busy = true;
                }
            }
            Cafe.Resort(c);
        }
    }

    /// <summary>One serving off whichever counter has food (FUN_001c1e9c), or none.</summary>
    Dish? TakeFromCounter()
    {
        foreach (var o in Cafe.Objects)
            if (o is ServingCounter sc && sc.TotalServings > 0)
                return sc.TakeServing(Cafe, true);
        return null;
    }

    /// <summary>
    /// GameStateRaid::load: the player's zombies (all but the ones down and reanimating) in a row in the street in
    /// front of the restaurant (K u0 - 4 - i, v0 - 5), and their pets that are up a row nearer (v0 - 4); facing the
    /// street, each with a half-clear red energy bar, each counting a raid.
    /// </summary>
    void AddRaiders()
    {
        int u0 = Cafe.OriginU, v0 = Cafe.OriginV;
        int i = 0;
        foreach (var z in Home.Staff)
        {
            var ch = z.Character;
            if (ch.Job == Job.Other)
                continue;
            int cell = Cafe.IndexOf(u0 - 4 - i, v0 - 5);
            if (cell < 0)
                continue;
            Enlist(Cafe.AddCharacter(ch, cell));
            i++;
        }
        i = 0;
        foreach (var pet in Player.Pets)
        {
            if (pet.Job == Job.Other)
                continue;
            int cell = Cafe.IndexOf(u0 - 4 - i, v0 - 4);
            if (cell < 0)
                continue;
            Enlist(Cafe.AddCharacter(pet, cell));
            i++;
        }
    }

    void Enlist(CharacterInstance r)
    {
        r.Team = 1;
        r.ShowEnergyBar = true;
        r.EnergyBarColor = new Raylib_cs.Color(0xff, 0, 0, 0x7f);
        r.SetAnimation(CharAnim.Idle, Facing.SE, true);
        r.Character.Job = Job.None;
        r.Character.Raids++;
        Raiders.Add(r);
    }

    // ---------------------------------------------------------------- customers

    /// <summary>FUN_0015a280: the next passer-by in 20 s +- 1 s (5 s at the top rating, which raids never have).</summary>
    void ResetSpawnTimer()
    {
        int spread = data.K.Int(0x42d33c);
        spawnTimer = data.K.Int(0x42d338) + (spread > 0 ? Rng.Next(spread * 2) - spread : 0);
    }

    /// <summary>
    /// FUN_00159e80: someone appears at either end of the street; most come in (85 in 256 walk past, all of them
    /// once the fight is on), wait at the door for a second or so and look for a seat.
    /// </summary>
    CharacterInstance SpawnCustomer(bool force)
    {
        ResetSpawnTimer();
        var def = PickCustomerType();
        var ch = new Character(def, Role.Customer, false, Character.RandomName(data, def.IsFemale, Rng));
        bool fromA = (Rng.Next() & 1) != 0;
        var c = Cafe.AddCharacter(ch, fromA ? Cafe.StreetEndB : Cafe.StreetEndA);
        int pass = data.K.Int(0x42d340);
        if ((pass < Rng.Next(256) && !force) || CombatStarted)
        {
            c.AddTask(new MoveTask(c, Cafe, fromA ? Cafe.StreetEndA : Cafe.StreetEndB));
        }
        else
        {
            c.Entering = true;
            c.AddTask(new MoveTask(c, Cafe, Cafe.IndexOf(Cafe.OriginU - 1 - data.K.DoorOffset, Cafe.OriginV + 1)));
            int spread = data.K.Int(0x42d34c);
            c.AddTask(new WaitTask(c, data.K.Int(0x42d348) + (spread > 0 ? Rng.Next(spread * 2) - spread : 0)));
            c.AddTask(new EatTask(c, Cafe));
        }
        Customers.Add(c);
        return c;
    }

    /// <summary>FUN_001643ac at the raid's level and a rating of nothing (a special guest only for a known stove recipe).</summary>
    CharacterDef PickCustomerType()
    {
        var pool = data.Characters.Where(d => d.B20 <= Level && d.F00 <= 0f && d.CanBeHuman
            && (!d.B50 || data.Foods.Any(f => f.Index >= 0x78 && f.S20 == d.S52 && Player.KnowsRecipe(f.Index)))).ToList();
        if (pool.Count == 0)
            return data.Characters[0];
        int total = pool.Sum(d => d.B04);
        int r = total > 0 ? Rng.Next(total) : 0;
        foreach (var d in pool)
        {
            r -= d.B04;
            if (r < 0)
                return d;
        }
        return pool[^1];
    }

    // ---------------------------------------------------------------- the frame (GameStateRaid::update)

    public void Update(int dtMs)
    {
        clockMs += dtMs;
        fightBegan = false;
        staffDown = true;
        chefDown = true;
        if (Paused || Ended)
        {
            // the energize tip waits for the zombie it is about
            return;
        }
        Cafe.Update(dtMs);
        UpdateBodies(dtMs);

        // who is left standing: waiters, the chef, customers inside
        int standing = 0;
        foreach (var s in Staff)
            if (!s.IsKnockedOut)
            {
                staffDown = false;
                standing++;
            }
        if (RivalChef != null && !RivalChef.IsKnockedOut)
        {
            chefDown = false;
            standing++;
        }
        foreach (var c in Customers)
            if (!c.IsKnockedOut && Cafe.IsInside(c.Cell))
                standing++;
        if (standing == 0 && !Won)
        {
            MusicCue?.Invoke(2);
            Won = true;
            victoryDelay = data.K.Int(0x42d3c0);
        }
        if (Won)
        {
            victoryDelay -= dtMs;
            if (victoryDelay < 1)
            {
                Finish(RaidOutcome.Won);
                return;
            }
        }

        bool allDown = true, allOut = true;
        UpdateRaiders(dtMs, ref allDown, ref allOut);
        UpdateYetis(dtMs);
        UpdateAmptron(dtMs);
        UpdateWolves(dtMs);
        if (allDown)
        {
            MusicCue?.Invoke(6);
            Finish(RaidOutcome.Lost);
            return;
        }
        if (allOut)
        {
            // everyone made it out: a win if the chef went down on the way
            if (!chefDown)
                MusicCue?.Invoke(1);
            else
            {
                MusicCue?.Invoke(2);
                Won = true;
            }
            Finish(Won ? RaidOutcome.Won : RaidOutcome.RanAway);
            return;
        }

        UpdateChef(dtMs);
        if (!CombatStarted)
            AssignWaiters();
        UpdateStaff(dtMs);
        UpdateCustomers(dtMs);

        spawnTimer -= dtMs;
        if (spawnTimer < 0)
            SpawnCustomer(false);
        if (!musicStarted)
        {
            musicStarted = true;
            Music?.Invoke(IsBoss ? 0x13 : 7);
        }
        if (!screamed && fightBegan)
        {
            screamed = true;
            foreach (var c in Customers.Where(c => Cafe.IsInside(c.Cell) && !c.IsKnockedOut))
                c.Scream();
        }
    }

    readonly List<CharacterInstance> bodies = new();

    void UpdateBodies(int dtMs)
    {
        foreach (var b in bodies.ToArray())
        {
            b.Update(dtMs);
            if (b.Faded || !Cafe.Objects.Contains(b))
            {
                bodies.Remove(b);
                if (Cafe.Objects.Contains(b))
                    Cafe.Remove(b);
            }
        }
    }

    /// <summary>
    /// FUN_00158230: each raider. Inside, the fight is on and it attacks; one in attack mode with nobody to fight
    /// heads for the door, or picks a rival standing next to it; one sent for food walks to the counter and takes
    /// it; retreating, it heads back down the street. A tired raider brings up the energize tip once.
    /// </summary>
    void UpdateRaiders(int dtMs, ref bool allDown, ref bool allOut)
    {
        int u0 = Cafe.OriginU, v0 = Cafe.OriginV, door = data.K.DoorOffset;
        foreach (var r in Raiders)
        {
            bool down = r.IsKnockedOut;
            if (down)
            {
                if (r == Selected)
                    Selected = null;
            }
            else
            {
                allDown = false;
            }
            if (!Retreating || (Cafe.IsInside(r.Cell) && !down))
                allOut = false;
            r.Update(dtMs);
            Cafe.Resort(r);
            // FUN_00161b0c: a tombstone's boost runs down; the energy one heals as it goes (100 over its five seconds)
            if (r.BoostMs > 0)
            {
                r.BoostMs -= dtMs;
                var c = r.Character;
                if (r.BoostType == 2 && c.Energy > 0)
                    c.Energy = Math.Min(c.MaxEnergy(data), c.Energy + dtMs / data.K.Float(0x42cdcc) * data.K.Float(0x42cdd4));
            }
            if (down)
                continue;
            // folded into the Amptron: out of the fight until it comes apart
            if (!r.Active)
                continue;
            var target = r.Interacting;
            // the energize tip, the first time a raider is worn down by a fight (CharacterInstance +0xb04; not a pet's)
            if (!r.IsPet && !Player.Hints.IsShown(0x2b) && r.Weakened && !Paused)
            {
                Tired = r;
                Paused = true;
                Hint(0x22, true);
            }
            bool inside = Cafe.IsInside(r.Cell);
            if (inside && (staffDown || IsBoss) && RivalChef is { } chef && !chef.IsKnockedOut
                && chef.Interacting is not CharacterInstance)
            {
                chef.ClearTasks();
                chef.Interacting = r;
            }
            if (inside)
            {
                if (!CombatStarted)
                {
                    fightBegan = true;
                    Log($"{r.Character.Name} is in: the fight is on");
                }
                CombatStarted = true;
                r.Character.Job = Job.Attacking;
            }
            if (target is CharacterInstance foe && !Cafe.IsInside(foe.Cell))
            {
                r.Interacting = null;
                r.Interrupt();
                target = null;
            }
            if (r.IsIdle)
            {
                if (Retreating)
                {
                    r.AddTask(new MoveTask(r, Cafe, Cafe.IndexOf(u0 + 2, v0 - 5)));
                }
                else if (target == null)
                {
                    if (r.Character.Job == Job.Attacking)
                    {
                        Hint(0x20, false);
                        if (!inside)
                        {
                            r.Groan();
                            r.AddTask(new MoveTask(r, Cafe, Cafe.IndexOf(u0 - 1 - door, v0 + 1)));
                        }
                        else if (Neighbours(r).FirstOrDefault(o => o.Team != 1 && !o.IsKnockedOut && Cafe.IsInside(o.Cell)) is { } next)
                        {
                            r.Interacting = next;
                        }
                    }
                }
                else if (target is Furniture counter)
                {
                    // FUN_0015892c: a pet sent to the counter goes there, but it takes nothing
                    if (r.IsPet)
                    {
                        if (!Cafe.AreNeighbours(r.Cell, counter.Cell))
                            r.AddTask(new FollowTask(r, counter, Cafe));
                        else
                            r.Interacting = null;
                    }
                    else if (Cafe.AreNeighbours(r.Cell, counter.Cell))
                    {
                        var steal = new StealFoodTask(r, Cafe);
                        steal.Finished += FoodTaken;
                        r.AddTask(steal);
                    }
                    else
                    {
                        r.AddTask(new FollowTask(r, counter, Cafe));
                    }
                }
                else if (target is CharacterInstance victim)
                {
                    if (!victim.IsKnockedOut && Cafe.IsInside(victim.Cell))
                    {
                        var attack = new AttackTask(r, victim, Cafe);
                        attack.Finished += RivalDown;
                        r.AddTask(attack);
                    }
                    else
                    {
                        r.Interacting = null;
                    }
                }
            }
            Spread(r);
        }
    }

    // the code's own words: the yeti's question, and why it cannot come yet; its name
    const uint TextYetiAsk = 0x3397db, TextYetiNobodyIn = 0x339805, TextYetiName = 0x3399e1;

    /// <summary>
    /// FUN_00152f88 button 19: why the yeti cannot be summoned now (null: it can) - its tombstone's uses are gone for
    /// the day (string 638), or none of the player's zombies or pets is inside yet.
    /// </summary>
    public string? CannotSummonYeti()
    {
        if (!Player.Boosts.Usable(Boosts.Yeti))
            return data.Strings[638];
        return CombatStarted ? null : data.Exe.Get(TextYetiNobodyIn);
    }

    /// <summary>The yeti's question (button 19).</summary>
    public string YetiQuestion => data.Exe.Get(TextYetiAsk);

    /// <summary>
    /// FUN_0015b4e0 + FUN_0015d93c: a use of the yeti tombstone, and "Yeti N" spins in on a free cell inside as a
    /// tornado, on the player's side (a red energy bar), for 20 seconds and a second more for each of the player's levels.
    /// </summary>
    public void SummonYeti()
    {
        if (CannotSummonYeti() != null)
            return;
        Player.Boosts.Use(Boosts.Yeti);
        var ch = new Character(data.YetiDef, Role.Summon, true, data.Exe.Get(TextYetiName) + (Yetis.Count + 1));
        var y = Cafe.AddCharacter(ch, RandomInsideCell());
        y.Team = 1;
        y.ShowEnergyBar = true;
        y.EnergyBarColor = new Raylib_cs.Color(0xff, 0, 0, 0x7f);
        y.LifeMs = Player.Level * 1000 + 20000;
        y.SetAnimation(CharAnim.Idle, Facing.SE, true);
        ch.Job = Job.Attacking;
        Yetis.Add(y);
        Log($"{ch.Name} is summoned for {y.LifeMs / 1000} s");
    }

    /// <summary>
    /// FUN_00158f1c: the yetis. When its time is up a yeti spins back into a tornado with no energy left (FUN_00195518)
    /// and is gone a second later (FUN_0015d7ec); retreating sends it off the same way. Otherwise it goes for the
    /// nearest rival standing inside (the chef turns on it, as on a raider), and with nobody left it spins where it is.
    /// </summary>
    void UpdateYetis(int dtMs)
    {
        foreach (var y in Yetis.ToArray())
        {
            if (y.LifeMs < 1)
            {
                if (y.IsKnockedOut)
                {
                    RemoveYeti(y);
                    continue;
                }
                YetiLeaves(y);
            }
            bool down = y.IsKnockedOut;
            if (down && Selected == y)
                Selected = null;
            bool inside = Cafe.IsInside(y.Cell);
            if (!down && inside && (staffDown || IsBoss) && RivalChef is { } chef && !chef.IsKnockedOut
                && chef.Interacting is not CharacterInstance)
            {
                chef.ClearTasks();
                chef.Interacting = y;
            }
            var target = y.Interacting as CharacterInstance;
            if (target != null && !Cafe.IsInside(target.Cell))
            {
                y.Interacting = null;
                y.Interrupt();
                target = null;
            }
            if (Retreating && !down)
            {
                YetiLeaves(y);
                down = true;
            }
            if (y.IsIdle && !down)
            {
                if (target == null)
                {
                    if (Neighbours(y).FirstOrDefault(o => o.Team != 1 && !o.IsKnockedOut && Cafe.IsInside(o.Cell)) is { } next)
                        y.Interacting = next;
                    else
                        y.SetYetiForm(false);
                }
                else if (!target.IsKnockedOut)
                {
                    var attack = new AttackTask(y, target, Cafe);
                    attack.Finished += RivalDown;
                    y.AddTask(attack);
                }
                else
                {
                    y.Interacting = null;
                }
            }
            if (!down)
                Spread(y);
            y.Update(dtMs);
            Cafe.Resort(y);
        }
    }

    /// <summary>FUN_00195518: the yeti's time is up - a last second spinning, its energy gone, its clock hidden.</summary>
    static void YetiLeaves(CharacterInstance y)
    {
        y.LifeMs = 1000;
        y.ClearTasks();
        y.Interacting = null;
        y.Character.Energy = 0;
        y.ShowEnergyBar = false;
        y.SetYetiForm(false);
    }

    /// <summary>The yeti is gone; whoever was fighting it lets go.</summary>
    void RemoveYeti(CharacterInstance y)
    {
        LetGo(y);
        Yetis.Remove(y);
        Cafe.Remove(y);
        if (Selected == y)
            Selected = null;
        Log($"{y.Character.Name} is gone");
    }

    /// <summary>FUN_0015d7ec: whoever was fighting <paramref name="who"/> - the chef, a waiter, a customer - lets go.</summary>
    void LetGo(CharacterInstance who)
    {
        var holders = new List<CharacterInstance>(Staff.Count + Customers.Count + 1);
        if (RivalChef != null)
            holders.Add(RivalChef);
        holders.AddRange(Staff);
        holders.AddRange(Customers);
        foreach (var o in holders)
            if (!o.IsKnockedOut && o.Interacting == who)
            {
                o.ClearTasks();
                o.Interacting = null;
            }
    }

    // ---------------------------------------------------------------- the Amptron (VoltronInstance)

    // the code's own words: the Amptron's name; why it cannot come (not five robots standing, none left today, one at
    // a time, nobody inside yet); its question
    const uint TextAmptronName = 0x3399e7, TextNeedRobots = 0x33997f, TextNoAmptronToday = 0x33993e,
        TextOneAmptron = 0x339885, TextAmptronNobodyIn = 0x3398d6, TextAmptronAsk = 0x3398b0;
    /// <summary>PTR_DAT_003d8114: the robot pets, one of each of which makes the Amptron.</summary>
    static readonly int[] RobotPets = [PetDef.FieryHound, PetDef.GoldenLion, PetDef.RoyalBear, PetDef.RageBull, PetDef.MidnightPanther];

    /// <summary>FUN_00155558: the first raiding pet of each robot kind still standing.</summary>
    List<CharacterInstance> Robots()
    {
        var found = new List<CharacterInstance>();
        foreach (int kind in RobotPets)
            if (Raiders.FirstOrDefault(r => r.Character.Pet?.Index == kind && !r.IsKnockedOut) is { } pet)
                found.Add(pet);
        return found;
    }

    /// <summary>
    /// FUN_00152f88 button 22: why the Amptron cannot come now (null: it can) - not all five robot pets are standing,
    /// none of the day's three summons is left, it is here already, or nobody has got inside yet.
    /// </summary>
    public string? CannotSummonAmptron()
    {
        if (Robots().Count < RobotPets.Length)
            return data.Exe.Get(TextNeedRobots);
        if (Player.AmptronUses < 1)
            return data.Exe.Get(TextNoAmptronToday);
        if (Amptron != null)
            return data.Exe.Get(TextOneAmptron);
        return CombatStarted ? null : data.Exe.Get(TextAmptronNobodyIn);
    }

    /// <summary>The Amptron's question (button 22).</summary>
    public string AmptronQuestion => data.Exe.Get(TextAmptronAsk);

    /// <summary>
    /// FUN_0015dbe4: one of the day's summons - the five robot pets fold up where they stand (their animation 11 under
    /// the robotPetDisappearSW flash), out of the fight from now on and let go by whoever fought them; when the last has
    /// folded the Amptron assembles.
    /// </summary>
    public void SummonAmptron()
    {
        if (CannotSummonAmptron() != null)
            return;
        combined.Clear();
        TransformTask? last = null;
        foreach (var pet in Robots())
        {
            combined.Add((pet, pet.Character.Job));
            pet.ClearTasks();
            last = new TransformTask(pet, PetAnim.RobotDisappear, pet.Facing);
            pet.AddTask(last);
            pet.Active = false;
            LetGo(pet);
            Cafe.Add(new TransformEffect(Cafe, pet, false));
            if (Selected == pet)
                Selected = null;
        }
        last!.Finished += _ => AmptronAssembles();
        Player.AmptronUses--;
        Log("the robot pets combine");
    }

    /// <summary>
    /// FUN_0015d2dc / FUN_0015d30c: the robot pets folded away out of sight, the Amptron - three quarters size, on the
    /// player's side, no energy bar - assembles on a free cell inside (sound 222) and stands once its assembling has
    /// played (FUN_0015d5dc); the fight is on. It stays twenty seconds and a second for each of the player's levels,
    /// with three specials. The day the summons ran out is noted.
    /// </summary>
    void AmptronAssembles()
    {
        var ch = new Character(data.AmptronDef, Role.Summon, true, data.Exe.Get(TextAmptronName).Trim());
        var a = Cafe.AddCharacter(ch, RandomInsideCell());
        a.Scale = 0.75f;
        a.Team = 1;
        a.Active = false;
        a.LifeMs = Player.Level * 1000 + 20000;
        var appear = new TransformTask(a, CharacterInstance.AmptronAppear, Facing.SW);
        appear.Finished += _ => a.Active = true;
        a.AddTask(appear);
        Amptron = a;
        if (!CombatStarted)
            fightBegan = true;
        CombatStarted = true;
        Sound(222);
        if (Player.AmptronUses == 0)
            Player.AmptronDay = GameClock.Today;
        foreach (var (pet, _) in combined)
            pet.Visible = false;
        Log($"{ch.Name} assembles for {a.LifeMs / 1000} s");
    }

    /// <summary>
    /// FUN_001593dc: the Amptron (updated while it is seen). Once it stands it fights as a raider does - whoever it is
    /// set on or the nearest rival standing inside (the counter only draws it there), the chef turning on it once the
    /// waiters are down (a boss chef at once) - until its time is up or the raiders retreat, when it comes apart.
    /// </summary>
    void UpdateAmptron(int dtMs)
    {
        if (Amptron is not { } a || !a.Visible)
            return;
        a.Update(dtMs);
        Cafe.Resort(a);
        if (!a.Active)
            return;
        if (Retreating || a.LifeMs < 1)
        {
            AmptronSplits(a);
            return;
        }
        bool down = a.IsKnockedOut;
        bool inside = Cafe.IsInside(a.Cell);
        if (!down && inside && (staffDown || IsBoss) && RivalChef is { } chef && !chef.IsKnockedOut
            && chef.Interacting is not CharacterInstance)
        {
            chef.ClearTasks();
            chef.Interacting = a;
        }
        if (inside)
        {
            if (!CombatStarted)
                fightBegan = true;
            CombatStarted = true;
            a.Character.Job = Job.Attacking;
        }
        var target = a.Interacting;
        if (target is CharacterInstance gone && !Cafe.IsInside(gone.Cell))
        {
            a.Interacting = null;
            a.Interrupt();
            target = null;
        }
        if (a.IsIdle && !down)
        {
            if (target == null)
            {
                if (a.Character.Job == Job.Attacking
                    && Neighbours(a).FirstOrDefault(o => o.Team != 1 && !o.IsKnockedOut && Cafe.IsInside(o.Cell)) is { } next)
                    a.Interacting = next;
            }
            else if (target is Furniture counter)
            {
                if (!Cafe.AreNeighbours(a.Cell, counter.Cell))
                    a.AddTask(new FollowTask(a, counter, Cafe));
                else
                    a.Interacting = null;
            }
            else if (target is CharacterInstance victim && !victim.IsKnockedOut && Cafe.IsInside(victim.Cell))
            {
                var attack = new AttackTask(a, victim, Cafe);
                attack.Finished += RivalDown;
                a.AddTask(attack);
            }
            else
            {
                a.Interacting = null;
            }
        }
        if (!down)
            Spread(a);
    }

    /// <summary>SPECIAL on the Amptron's command bar (FUN_000ed028): its tip, and its special attack if it can.</summary>
    public void AmptronSpecial()
    {
        Hint(0x5b, true);
        if (Amptron is { Active: true } a && a.CurrentTask is AttackTask attack && attack.StartSpecial())
            Log($"{a.Character.Name}'s special on {attack.Target.Character.Name} ({a.SpecialsLeft} left)");
    }

    /// <summary>
    /// FUN_0015e1b4: the Amptron's time is up (or the raiders retreat) - its clock gone, out of the fight, it comes apart
    /// where it stands and whoever fought it lets go; then the pets come back out.
    /// </summary>
    void AmptronSplits(CharacterInstance a)
    {
        a.LifeMs = 0;
        a.Active = false;
        a.ClearTasks();
        var apart = new TransformTask(a, CharacterInstance.AmptronDisappear, Facing.SW);
        apart.Finished += _ => PetsReturn(a);
        a.AddTask(apart);
        LetGo(a);
        if (Selected == a)
            Selected = null;
        Log($"{a.Character.Name} comes apart");
    }

    /// <summary>
    /// FUN_0015d5e8: the Amptron gone from sight, each robot pet unfolds where it folded (its animation 10 under the
    /// robotPetAppearSW flash) with its job back; when the last has, they are back in the fight.
    /// </summary>
    void PetsReturn(CharacterInstance a)
    {
        TransformTask? last = null;
        foreach (var (pet, job) in combined)
        {
            pet.Character.Job = job;
            last = new TransformTask(pet, PetAnim.RobotAppear, (Facing)pet.Character.Facing);
            pet.AddTask(last);
            pet.Visible = true;
            Cafe.Add(new TransformEffect(Cafe, pet, true));
        }
        if (last != null)
            last.Finished += _ => AmptronGone(a);
        a.LifeMs = 0;
        a.Visible = false;
        a.Active = false;
        LetGo(a);
        if (last == null)
            AmptronGone(a);
    }

    /// <summary>FUN_0015d250: the robot pets are back in the fight and the Amptron is gone (sound 220).</summary>
    void AmptronGone(CharacterInstance a)
    {
        foreach (var (pet, _) in combined)
            pet.Active = true;
        Cafe.Remove(a);
        if (Amptron == a)
            Amptron = null;
        Sound(220);
        Log("the robot pets are back");
    }

    // ---------------------------------------------------------------- the Big Bad Wolf (BigBadWolfInstance)

    // the code's own words: the wolf's name; why no zombie can turn into it (no fairy-tale zombie standing inside, none
    // of the raid's three left); its question
    const uint TextWolfName = 0x3399f0, TextNeedFairyTale = 0x3399fd, TextNoWolvesLeft = 0x339a87, TextWolfAsk = 0x339add;

    /// <summary>FUN_00155618: Little Red, the Huntsman or Granny Wolf (character types 288 .. 290).</summary>
    public static bool IsFairyTale(CharacterInstance r) => r.Def.Index is >= 288 and <= 290;

    /// <summary>
    /// FUN_0015b574 / FUN_0015b978 (HUD button 23): why no zombie can turn into the wolf now (null: one can - the first
    /// fairy-tale zombie standing inside, picked for the question), there being no fairy-tale zombie up, none of the
    /// raid's three left, or none inside.
    /// </summary>
    public string? WolfCandidate()
    {
        var fairy = Raiders.Where(r => IsFairyTale(r) && !r.IsKnockedOut).ToList();
        if (fairy.Count == 0)
            return data.Exe.Get(TextNeedFairyTale);
        return WolfCandidate(fairy.FirstOrDefault(r => r.Active && Cafe.IsInside(r.Cell)) ?? fairy[^1]);
    }

    /// <summary>
    /// FUN_0015b978 (and TRANSFORM on a fairy-tale zombie's command bar, FUN_0015b060): none of the raid's three
    /// left, or this zombie not standing inside - or it is picked for the question.
    /// </summary>
    public string? WolfCandidate(CharacterInstance pick)
    {
        if (WolfUses == 0)
            return data.Exe.Get(TextNoWolvesLeft);
        if (!pick.Active || !Cafe.IsInside(pick.Cell))
            return data.Exe.Get(TextNeedFairyTale);
        Selected = pick;
        return null;
    }

    /// <summary>The wolf's question (button 23).</summary>
    public string WolfQuestion => data.Exe.Get(TextWolfAsk);

    /// <summary>FUN_0015de20: yes - the picked zombie turns into the wolf, one of the raid's three used.</summary>
    public void TransformSelected()
    {
        if (Selected is not { } z || !IsFairyTale(z) || !z.Active || WolfUses < 1)
            return;
        WolfUses--;
        TurnIntoWolf(z);
    }

    /// <summary>
    /// FUN_0015dea0: where the zombie stands the Big Bad Wolf (strength 20 and a fifth of the player's level, on the
    /// player's side) appears over the zombie's transforming, both out of the fight until it stands; whoever fought
    /// the zombie lets go, and the fight is on (sound 226). The wolf stays twenty seconds and a second for each of the
    /// player's levels.
    /// </summary>
    void TurnIntoWolf(CharacterInstance z)
    {
        var ch = new Character(data.WolfDef, Role.Summon, true, data.Exe.Get(TextWolfName)) { Job = Job.Attacking };
        ch.Strength = 20 + Player.Level / 5;
        var w = Cafe.AddCharacter(ch, z.Cell);
        w.SetPosition(Cafe, z.X, z.Y);
        Cafe.Resort(w);
        w.Team = 1;
        w.Active = false;
        w.LifeMs = Player.Level * 1000 + 20000;
        w.Zombie = z;
        var appear = new TransformTask(w, CharacterInstance.WolfAppear, Facing.SW);
        appear.Finished += _ => WolfStands(w);
        w.AddTask(appear);
        z.ClearTasks();
        z.Visible = true;
        z.Active = false;
        LetGo(z);
        z.AddTask(new TransformTask(z, (int)CharAnim.Transform, Facing.SW));
        if (!CombatStarted)
            fightBegan = true;
        CombatStarted = true;
        Sound(226);
        Wolves.Add(w);
        Selected = null;
        Log($"{z.Character.Name} turns into the {ch.Name} for {w.LifeMs / 1000} s");
    }

    /// <summary>FUN_0015d8b4: the wolf stands - in the fight with its clock showing, the zombie out of sight.</summary>
    static void WolfStands(CharacterInstance w)
    {
        w.Active = true;
        w.Visible = true;
        if (w.Zombie is { } z)
        {
            z.Visible = false;
            z.Active = false;
        }
    }

    /// <summary>
    /// FUN_001597f4: the wolves (updated while seen). One standing fights as a raider does - the nearest rival standing
    /// inside, the chef turning on it once the waiters are down (a boss chef at once) - until its time is up or the
    /// raiders retreat, when it turns back into the zombie.
    /// </summary>
    void UpdateWolves(int dtMs)
    {
        foreach (var w in Wolves.ToArray())
        {
            if (!w.Visible)
                continue;
            w.Update(dtMs);
            if (!Wolves.Contains(w))
                continue;
            Cafe.Resort(w);
            if (!w.Active)
                continue;
            if (Retreating || w.LifeMs < 1)
            {
                WolfLeaves(w);
                continue;
            }
            bool down = w.IsKnockedOut;
            if (!down && Cafe.IsInside(w.Cell) && (staffDown || IsBoss) && RivalChef is { } chef && !chef.IsKnockedOut
                && chef.Interacting is not CharacterInstance)
            {
                chef.ClearTasks();
                chef.Interacting = w;
            }
            var target = w.Interacting;
            if (target is CharacterInstance gone && !Cafe.IsInside(gone.Cell))
            {
                w.Interacting = null;
                w.Interrupt();
                target = null;
            }
            if (w.IsIdle && !down)
            {
                if (target == null)
                {
                    if (w.Character.Job == Job.Attacking
                        && Neighbours(w).FirstOrDefault(o => o.Team != 1 && !o.IsKnockedOut && Cafe.IsInside(o.Cell)) is { } next)
                        w.Interacting = next;
                }
                else if (target is CharacterInstance victim && !victim.IsKnockedOut && Cafe.IsInside(victim.Cell))
                {
                    var attack = new AttackTask(w, victim, Cafe);
                    attack.Finished += RivalDown;
                    w.AddTask(attack);
                }
                else
                {
                    w.Interacting = null;
                }
            }
            if (!down)
                Spread(w);
        }
    }

    /// <summary>
    /// FUN_0015e080: the wolf's time is up (or the raiders retreat) - whoever fought it lets go and it goes, its clock
    /// hidden, as the zombie comes back where it stands (sound 225).
    /// </summary>
    void WolfLeaves(CharacterInstance w)
    {
        LetGo(w);
        w.ClearTasks();
        w.Visible = true;
        w.Active = false;
        var going = new TransformTask(w, CharacterInstance.WolfDisappear, Facing.SW);
        going.Finished += _ => WolfGone(w);
        w.AddTask(going);
        if (w.Zombie is { } z)
        {
            Cafe.PlaceOnCell(z, w.Cell);
            z.SetPosition(Cafe, w.X, w.Y);
            Cafe.Resort(z);
            z.AddTask(new TransformTask(z, (int)CharAnim.Detransform, Facing.SW));
            z.Visible = true;
            z.Active = false;
        }
        Sound(225);
        Log($"the {w.Character.Name} turns back");
    }

    /// <summary>FUN_0015d8ec: the wolf is gone and the zombie is back in the fight.</summary>
    void WolfGone(CharacterInstance w)
    {
        w.Active = false;
        w.Visible = false;
        if (w.Zombie is { } z)
        {
            z.Visible = true;
            z.Active = true;
        }
        Wolves.Remove(w);
        Cafe.Remove(w);
    }

    /// <summary>
    /// FUN_00162408 / FUN_001622fc: everyone in the cafe who takes part (not a robot pet folded into the Amptron),
    /// nearest first (Manhattan distance on the diamond grid).
    /// </summary>
    IEnumerable<CharacterInstance> Neighbours(CharacterInstance c)
    {
        var (u0, v0) = Cafe.Diamond(c.Cell);
        return Cafe.Objects.OfType<CharacterInstance>().Where(o => o != c && o.Active)
            .OrderBy(o =>
            {
                var (u, v) = Cafe.Diamond(o.Cell);
                return Math.Abs(u - u0) + Math.Abs(v - v0);
            });
    }

    /// <summary>
    /// The rival chef (GameStateRaid::update): cooks while its waiters stand, fights whoever it has been set on
    /// (a boss chef guards its spot), and drops a target that went down or left.
    /// </summary>
    void UpdateChef(int dtMs)
    {
        var chef = RivalChef;
        if (chef == null)
            return;
        if (!chef.IsKnockedOut)
        {
            var target = chef.Interacting as CharacterInstance;
            if (chef.IsIdle)
            {
                if (target != null && !target.IsKnockedOut && Cafe.IsInside(target.Cell))
                {
                    CafeTask fight = IsBoss ? new GuardTask(chef, target, Cafe) : new AttackTask(chef, target, Cafe);
                    chef.AddTask(fight);
                }
                else if (target != null)
                {
                    chef.Interacting = null;
                }
                else if (Stove != null && !(Stove.Busy && Stove.IsDone) && !staffDown)
                {
                    chef.AddTask(new CookTask(chef, Stove, Cafe));
                }
            }
            else if (target != null && (target.IsKnockedOut || !Cafe.IsInside(target.Cell)))
            {
                chef.Interacting = null;
                chef.Interrupt();
            }
            Spread(chef);
        }
        chef.Update(dtMs);
        Cafe.Resort(chef);
    }

    /// <summary>
    /// Before the fight: an idle waiter (serving) clears a dirty table (CleanupTask) or serves the first customer
    /// waiting (ServeTask); with no waiters left the chef takes it on.
    /// </summary>
    void AssignWaiters()
    {
        var pool = Staff.Where(s => !s.IsKnockedOut && s.Character.Job == Job.Serving && s.IsIdle).ToList();
        if (pool.Count == 0 && Staff.Count == 0 && RivalChef is { } chef && !chef.IsKnockedOut && chef.IsIdle)
        {
            chef.Character.Job = Job.Serving;
            pool.Add(chef);
        }
        if (Cafe.DirtyTables.Count > 0 && pool.Count > 0)
        {
            var w = pool[Rng.Next(pool.Count)];
            pool.Remove(w);
            w.AddTask(new CleanupTask(w, Cafe, Cafe.DirtyTables[0]));
        }
        if (Cafe.Waiting.Count > 0 && Cafe.TotalServings() > 0 && pool.Count > 0)
        {
            var customer = Cafe.Waiting[0];
            if (customer.AnimId == (int)CharAnim.Sit)
            {
                var w = pool[Rng.Next(pool.Count)];
                w.AddTask(new ServeTask(w, Cafe, customer));
            }
        }
    }

    /// <summary>
    /// The waiters (GameStateRaid::update): when the fight begins they drop everything; then each fights a raider it
    /// is set on (or the first standing next to it) and otherwise wanders the room.
    /// </summary>
    void UpdateStaff(int dtMs)
    {
        foreach (var s in Staff)
        {
            if (fightBegan)
            {
                s.ClearTasks();
                s.Interacting = null;
            }
            if (!s.IsKnockedOut)
            {
                var target = s.Interacting;
                if (s.IsIdle)
                {
                    if (target == null)
                    {
                        if (CombatStarted && Neighbours(s).FirstOrDefault(o => o.Team == 1 && !o.IsKnockedOut && Cafe.IsInside(o.Cell)) is { } raider)
                            s.Interacting = raider;
                    }
                    else if (target is CharacterInstance foe)
                    {
                        if (!foe.IsKnockedOut && Cafe.IsInside(foe.Cell))
                        {
                            s.AddTask(new AttackTask(s, foe, Cafe));
                        }
                        else
                        {
                            s.Interacting = null;
                            s.AddTask(new MoveTask(s, Cafe, RandomInsideCell()));
                        }
                    }
                }
                else if (target is CharacterInstance foe && (foe.IsKnockedOut || !Cafe.IsInside(foe.Cell)))
                {
                    s.Interacting = null;
                    s.Interrupt();
                }
                Spread(s);
            }
            s.Update(dtMs);
            Cafe.Resort(s);
        }
    }

    /// <summary>
    /// The customers: once the fight starts they stop what they are doing; done (or frightened) they leave by either
    /// end of the street - during the fight running around the room a few times (K 0x42d330) first.
    /// </summary>
    void UpdateCustomers(int dtMs)
    {
        foreach (var c in Customers.ToArray())
        {
            c.Update(dtMs);
            Cafe.Resort(c);
            if (CombatStarted && c.PanicRuns == 0 && !c.IsKnockedOut)
            {
                c.Interrupt();
                c.PanicRuns = 1;
            }
            if (!c.IsIdle || c.IsKnockedOut)
                continue;
            if (c.Interacting is CharacterInstance attacker)
            {
                c.Scream();
                if (!(Cafe.AreNeighbours(c.Cell, attacker.Cell) || c.Cell == attacker.Cell) || attacker.Interacting != c)
                    c.Interacting = null;
                continue;
            }
            bool atEnd = c.Cell == Cafe.StreetEndA || c.Cell == Cafe.StreetEndB;
            if (atEnd)
            {
                Customers.Remove(c);
                Cafe.Waiting.Remove(c);
                c.DropTasks();
                Cafe.Remove(c);
                continue;
            }
            int end = (Rng.Next() & 1) == 0 ? Cafe.StreetEndA : Cafe.StreetEndB;
            if (Cafe.IsInside(c.Cell) && CombatStarted && ++c.PanicRuns < data.K.Int(0x42d330))
            {
                c.AddTask(new MoveTask(c, Cafe, RandomInsideCell(), CharAnim.Run));
                c.Scream();
            }
            else
            {
                c.AddTask(new MoveTask(c, Cafe, end, CombatStarted ? CharAnim.Run : null));
            }
        }
    }

    /// <summary>FUN_00159c70: nobody idles on a cell someone else already stands on - one steps aside.</summary>
    void Spread(CharacterInstance c)
    {
        if (!c.IsIdle || c.IsKnockedOut)
            return;
        bool crowded = Cafe.Objects.Any(o => o is CharacterInstance other && other != c && other.Cell == c.Cell
            && !other.IsKnockedOut && !(other.CurrentTask?.IsMoving ?? false));
        if (!crowded)
            return;
        for (int dir = 0; dir < 4; dir++)
        {
            int n = Cafe.Neighbour(c.Cell, dir);
            if (n >= 0 && Cafe.IsInside(n) == Cafe.IsInside(c.Cell) && Cafe.Nav.IsPassable(n)
                && !Cafe.Objects.Any(o => o is CharacterInstance other && other.Cell == n))
            {
                var job = c.Character.Job;
                c.AddTask(new MoveTask(c, Cafe, n));
                c.Character.Job = job;
                return;
            }
        }
    }

    // ---------------------------------------------------------------- what happens to the fallen

    /// <summary>
    /// FUN_0015c350 (a raider's attack over): a rival brought down pays K 0x42d378 by level (and is good for
    /// K 0x42d388 experience, the raider's and the player's), now and then drops toxin (always the first time, then
    /// at K 0x42d384's odds); a customer pays 20 (K 0x42d364). A zombie then eats whoever it brought down.
    /// </summary>
    void RivalDown(CafeTask t)
    {
        if (t is not AttackTask attack || !attack.Success)
            return;
        var raider = attack.Owner;
        var victim = attack.Target;
        Log($"{raider.Character.Name} brought down {victim.Character.Name} ({victim.Character.Role})");
        int xp = data.K.Int(0x42d360);
        if (victim.Team == 2)
        {
            bool drop = !Player.Hints.IsShown(0x3b) || Rng.Next(100) < K<float>(0x42d384, Level - 1) * 100f;
            if (drop)
            {
                int toxin = ByLevel(0x42d380);
                Toxin += toxin;
                Hint(0x38, true);
                Cafe.AddNote(new StatusNote(Cafe, victim, StatusNote.Kind.Toxin, "+" + toxin, data.K.Int(0x42d544)));
            }
            int money = ByLevel(0x42d378);
            Money += money;
            Player.Money += money;
            xp = ByLevel(0x42d388);
        }
        else
        {
            int money = data.K.Int(0x42d364);
            Money += money;
            Player.Money += money;
            Cafe.AddNote(new StatusNote(Cafe, victim, StatusNote.Kind.Money, "+" + money, data.K.Int(0x42d544)));
        }
        if (Player.Level != data.K.Int(0x42d590))
            Player.Xp += xp;
        raider.Character.Xp += xp;
        raider.Character.UpdateLevel(data);
        // a zombie or a pet then eats whoever it brought down
        if ((raider.Character.Role is Role.Staff or Role.Pet) && victim.Character.Role is not (Role.Staff or Role.Pet))
        {
            bodies.Add(victim);
            raider.AddTask(new FeastTask(raider, victim, Cafe));
        }
    }

    /// <summary>FUN_0015c060: a dish carried off - "+1" over the raider, into the loot.</summary>
    void FoodTaken(CafeTask t)
    {
        if (t is not StealFoodTask steal)
            return;
        steal.Owner.Interacting = null;
        if (!steal.Success || steal.Stolen == null)
            return;
        Cafe.AddNote(new StatusNote(Cafe, steal.Owner, StatusNote.Kind.Food, "+1", data.K.Int(0x42d544)));
        Loot.Add(steal.Stolen);
        Log($"{steal.Owner.Character.Name} stole {steal.Stolen.DisplayName(data)} x{steal.Stolen.Servings}");
    }

    // ---------------------------------------------------------------- orders

    /// <summary>ATTACK (command bar button 1, FUN_000ee468): the picked raider goes in.</summary>
    public void Attack(CharacterInstance r)
    {
        r.Character.Job = Job.Attacking;
        Hint(0x20, false);
    }

    /// <summary>FUN_0015b204: the strength tombstone (the level 2 one first) - every raider hits twice as hard for five seconds.</summary>
    public void UseStrength() => UseBoost(Boosts.Strength2, Boosts.Strength, 1);

    /// <summary>FUN_0015b29c: the health tombstone - every raider gets its energy back over five seconds.</summary>
    public void UseHealth() => UseBoost(Boosts.Health2, Boosts.Health, 2);

    void UseBoost(int first, int second, int type)
    {
        var b = Player.Boosts;
        int slot = b.Usable(first) ? first : b.Usable(second) ? second : -1;
        if (slot >= 0)
            b.Use(slot);
        foreach (var r in Raiders)
            r.Boost(type);
        Log($"tombstone boost {type}");
    }

    /// <summary>FUN_0015b334: the golden tombstone - every raider down is up again (energy to 100 at most) and back in.</summary>
    public void UseGolden()
    {
        var b = Player.Boosts;
        if (b.Usable(Boosts.Golden))
            b.Use(Boosts.Golden);
        foreach (var r in Raiders)
        {
            if (!r.IsKnockedOut)
                continue;
            var c = r.Character;
            c.Energy = Math.Min(c.MaxEnergy(data), 100f);
            c.ReanimateMs = 0;
            c.Job = Job.Attacking;
            r.SetAnimation(CharAnim.Idle, r.Facing, true);
        }
    }

    /// <summary>"Send all zombies" (FUN_0015b514).</summary>
    public void AttackAll()
    {
        foreach (var r in Raiders)
            r.Character.Job = Job.Attacking;
    }

    /// <summary>
    /// A tap with a raider picked (FUN_00157018): a rival or customer becomes its target (and it attacks), the
    /// counter sends it for food, an empty floor cell inside sends it there.
    /// </summary>
    public void Order(CharacterInstance r, CafeObject? tapped, int cell)
    {
        if (Retreating || r.IsKnockedOut || !r.Active)
            return;
        switch (tapped)
        {
            case CharacterInstance c when c != r && c.Team != 1 && !c.IsKnockedOut && r.Interacting != c:
                r.Interacting = c;
                Hint(0x20, false);
                r.Interrupt();
                r.Interacting = c;
                r.Character.Job = Job.Attacking;
                r.Groan();
                Marked?.Invoke(c);
                return;
            case Furniture f when f.Def.Category == FurnitureCategory.Counter && r.Interacting != f:
                r.Interrupt();
                r.Interacting = f;
                return;
        }
        if (cell >= 0 && Cafe.IsInside(cell) && Cafe.Nav.IsPassable(cell))
        {
            r.Interrupt();
            r.Interacting = null;
            r.AddTask(new MoveTask(r, Cafe, cell));
        }
    }

    /// <summary>Retreat (FUN_0015b1a8): every raider drops what it is doing and heads home.</summary>
    public void Retreat()
    {
        Retreating = true;
        foreach (var r in Raiders)
        {
            r.Interrupt();
            r.Interacting = null;
        }
    }

    /// <summary>ENERGIZE in a raid (FUN_0015a460 -> FUN_0015b830): a vial fills a raider up.</summary>
    public string? CannotEnergize(CharacterInstance r) =>
        r.IsKnockedOut ? "" : Player.Toxin < 1 ? data.Strings[320] : null;

    public bool Energize(CharacterInstance r)
    {
        if (CannotEnergize(r) != null)
            return false;
        r.Character.Energy = r.Character.MaxEnergy(data);
        Player.Toxin -= 1;
        if (!r.IsPet)
            Sound((r.Def.IsFemale ? 175 : 173) + Rng.Next(2));
        Hint(0x1a, true);
        Hint(0x37, true);
        if (Paused && Tired == r)
            Paused = false;
        return true;
    }

    // ---------------------------------------------------------------- the end (FUN_00157a10)

    /// <summary>
    /// The raid is over. Won: the restaurant shuts for a while (<see cref="Neighbourhood.Beaten"/>; a boss one leaves
    /// and the next boss is due), the toxin found comes home (at least one, the first time a level-3 cafe with a
    /// 30% rating wins), the prize dish joins the loot if nothing was stolen, and the rival's chef can be hired.
    /// Retreated: the toxin and the loot come home. Lost: the loot is gone. The loot goes into the fridge.
    /// </summary>
    void Finish(RaidOutcome outcome)
    {
        Ended = true;
        Outcome = outcome;
        Log($"raid over: {outcome}, toxin {Toxin}, money {Money}, loot {Loot.Count}");
        var n = Player.Neighbours;
        if (outcome == RaidOutcome.Won)
        {
            Hint(0x21, true);
            n.Beaten(data, Player, MapLevel, Level);
            // FUN_001a7e64(.., 0): an enemy cafe raided, for the quests
            if (Player.Hints.IsShown(0x53))
                Player.Objectives.RaidWon(friend: false);
            if (IsBoss)
            {
                n.BossTimer = 0;
                n.NextBoss(data);
            }
            if (!Player.RaidBonusGiven && Player.Level >= data.K.Int(0x42d65c) && Player.Rating >= data.K.Float(0x42d660))
            {
                if (Toxin == 0)
                    Toxin = 1;
                Player.RaidBonusGiven = true;
            }
            if (Loot.Count == 0 && prize != null)
                Loot.Add(new Dish(prize.Food, prize.Servings, prize.Modifier));
            // the rival chef in the chef store (K 0x42d694: its place among the store's chefs)
            var slots = data.K.Ints(0x42d694);
            if (Type < slots.Length)
            {
                var chef = data.Characters.Where(c => c.B39 != 0).ElementAtOrDefault(slots[Type]);
                if (chef != null)
                    Player.UnlockedChefs.Add(chef.Index);
            }
        }
        if (outcome != RaidOutcome.Lost)
        {
            Player.Toxin += Toxin;
            Player.RaidLoot.AddRange(Loot);
        }
        else
        {
            Loot.Clear();
        }
        // the raiders come home: those knocked out lie down to reanimate (GameStateCafe's FUN_000b5c10)
        ComeHome();
    }

    /// <summary>
    /// The raiders back in the cafe: the ones knocked out lie down to reanimate (job 6 for K 0x42cf10, GameStateCafe's
    /// FUN_000b5c10), the rest go back to serving.
    /// </summary>
    public void ComeHome()
    {
        foreach (var r in Raiders)
        {
            var c = r.Character;
            c.Job = Job.Serving;
            if (r.IsKnockedOut)
            {
                c.Job = Job.Other;
                c.ReanimateMs = data.K.Int(0x42cf10);
            }
        }
    }

    // ---------------------------------------------------------------- ICafeEvents

    public void Hint(int id, bool flag) => Player.Hints.Trigger(id, flag);
    public void Sound(int id) => SoundRequested?.Invoke(id);
    public int RandomServeSound() => Home.RandomServeSound();
    public bool BurningEnabled => false;
    public bool DoubleXpEvent => false;
    /// <summary>The rival chef's cooking speed runs the stove.</summary>
    public CharacterDef Chef => RivalChef?.Def ?? Player.Chef;
    /// <summary>FUN_001bab54 counts a raided chef's batches for the player's quest too.</summary>
    public void DishPlaced(Dish dish, CharacterInstance by)
    {
        if (Player.Hints.IsShown(0x53))
            Player.Objectives.Cooked(dish.Food);
    }
    public bool HintShown(int id) => Player.Hints.IsShown(id);
}
