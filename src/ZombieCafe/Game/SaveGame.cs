using System.Text.Json;
using ZombieCafe.Data;
using ZombieCafe.Game.Cafe;

namespace ZombieCafe.Game;

/// <summary>
/// The port's own save file (the original kept binary .dat/.caf slots; the port stores what it simulates
/// as JSON): player progress, every piece of furniture with what is on it, and the staff.
/// </summary>
public static class SaveGame
{
    public const int Version = 1;

    public sealed class Data
    {
        public int Version { get; set; } = SaveGame.Version;
        public long SavedAtUnixMs { get; set; }
        /// <summary>How far the game's clock runs ahead of the computer's (the cheat menu's time skips).</summary>
        public long ClockOffsetMs { get; set; }
        public PlayerData Player { get; set; } = new();
        public bool Open { get; set; }
        public List<FurnitureData> Furniture { get; set; } = new();
        public List<StaffData> Staff { get; set; } = new();
        public StaffData? Chef { get; set; }
        /// <summary>The room's floor tiles: cell, tile, mirrored.</summary>
        public List<int[]> Floors { get; set; } = new();
        /// <summary>Cafe +0x94 / +0x95: the room's size after expansions (0: the starting room).</summary>
        public int RoomWidth { get; set; }
        public int RoomDepth { get; set; }
        /// <summary>The room's walls: cell, side, furniture id of the wall type.</summary>
        public List<int[]> Walls { get; set; } = new();
        /// <summary>The rival restaurants (missing in saves from before the map).</summary>
        public NeighbourhoodData? Neighbours { get; set; }
        /// <summary>The storage box (furniture ids).</summary>
        public List<int>? Stored { get; set; }
        /// <summary>The bonus quests (FUN_001a7490); missing in older saves.</summary>
        public ObjectivesData? Objectives { get; set; }
        /// <summary>The tombstones' uses left and when each was last used (Unix s), by slot.</summary>
        public int[]? BoostCharges { get; set; }
        public long[]? BoostLastUse { get; set; }
        /// <summary>The pets (game +0x188, in order: a habitat names its pet by its place here) and those in storage (+0x194).</summary>
        public List<PetData>? Pets { get; set; }
        public List<PetData>? StoredPets { get; set; }
        /// <summary>The Amptron's summons left and the day they were filled or ran out (game +0x3e0 / +0x3d8).</summary>
        public int AmptronUses { get; set; }
        public string? AmptronDay { get; set; }
        /// <summary>The most recipes unlocked so far (game +0x3c0).</summary>
        public int UnlockedBest { get; set; }
        /// <summary>Frequent Fryer: the last day played and the days in a row (game +0x2c4 / +0x2e8).</summary>
        public string? LastPlayDay { get; set; }
        public int StreakDays { get; set; }
    }

    /// <summary>A pet's record (Pet, 0x50 bytes).</summary>
    public sealed class PetData
    {
        public int Def { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int Xp { get; set; }
        public bool LevelledUp { get; set; }
        public float Energy { get; set; }
        public Job Job { get; set; }
        public int ReanimateMs { get; set; }
        public int Facing { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Raids { get; set; }
        public long JoinedUnixMs { get; set; }

        public static PetData From(Character c) => new()
        {
            Def = c.Pet?.Index ?? 0, Name = c.Name, Level = c.Level, Xp = c.Xp, LevelledUp = c.LevelledUp, Energy = c.Energy,
            Job = c.Job, ReanimateMs = c.ReanimateMs, Facing = c.Facing, Kills = c.Kills, Deaths = c.Deaths, Raids = c.Raids,
            JoinedUnixMs = c.JoinedUnixMs,
        };

        public Character? ToCharacter(GameData data)
        {
            if (Def < 0 || Def >= data.Pets.Count)
                return null;
            return new Character(data.Pets[Def].Body, Role.Pet, true, Name)
            {
                Level = Level, Xp = Xp, LevelledUp = LevelledUp, Energy = Energy, Job = Job, ReanimateMs = ReanimateMs,
                Facing = (byte)Facing, Kills = Kills, Deaths = Deaths, Raids = Raids,
                JoinedUnixMs = JoinedUnixMs > 0 ? JoinedUnixMs : GameClock.UnixMs,
            };
        }
    }

    public sealed class ObjectivesData
    {
        public int Level { get; set; }
        public int Remaining { get; set; } = -1;
        public int Total { get; set; } = -1;
        public long SetAtUnix { get; set; }
        /// <summary>Type, progress, target, recipe, ticked off (0/1).</summary>
        public List<int[]> Goals { get; set; } = new();

        public static ObjectivesData From(Objectives o) => new()
        {
            Level = o.Level, Remaining = o.Remaining, Total = o.Total, SetAtUnix = o.SetAtUnix,
            Goals = o.Goals.Select(g => new[] { g.Type, g.Progress, g.Target, g.Param, g.Claimed ? 1 : 0 }).ToList(),
        };

        public void ApplyTo(Objectives o)
        {
            o.Level = Level;
            o.Remaining = Remaining;
            o.Total = Total;
            o.SetAtUnix = SetAtUnix;
            o.Goals.Clear();
            foreach (var g in Goals.Where(g => g.Length >= 5))
                o.Goals.Add(new Objectives.Goal
                {
                    Type = (short)g[0], Progress = g[1], Target = g[2], Param = (short)g[3], Claimed = g[4] != 0,
                });
        }
    }

    /// <summary>The rivals' state (ZombieCafe save: +0x1c4 .. +0x1de, +0x1ac, +0x2d4, +0x2dc).</summary>
    public sealed class NeighbourhoodData
    {
        public int[] Timers { get; set; } = [];
        public int[] Types { get; set; } = [];
        public string[] Owners { get; set; } = [];
        public int BossTimer { get; set; }
        public int BossIndex { get; set; }
        public long BossDayUnixMs { get; set; }
        public long BossRolledUnixMs { get; set; }
        public float Penalty { get; set; }
        public int CurrentLevel { get; set; }
        public bool Stealing { get; set; }
        public bool LabOpened { get; set; }

        public static NeighbourhoodData From(Neighbourhood n) => new()
        {
            Timers = (int[])n.Timers.Clone(), Types = n.Types.Select(t => (int)t).ToArray(), Owners = (string[])n.Owners.Clone(),
            BossTimer = n.BossTimer, BossIndex = n.BossIndex, Penalty = n.Penalty, CurrentLevel = n.CurrentLevel,
            Stealing = n.Stealing, LabOpened = n.LabOpened,
            BossDayUnixMs = n.BossDay == default ? 0 : new DateTimeOffset(n.BossDay).ToUnixTimeMilliseconds(),
            BossRolledUnixMs = n.BossRolled == default ? 0 : new DateTimeOffset(n.BossRolled).ToUnixTimeMilliseconds(),
        };

        public void ApplyTo(Neighbourhood n, GameData data)
        {
            for (int i = 0; i < Neighbourhood.Slots; i++)
            {
                if (i < Timers.Length)
                    n.Timers[i] = Timers[i];
                if (i < Types.Length && Types[i] >= 0 && Types[i] < data.EnemyCafes.Count)
                    n.Types[i] = (byte)Types[i];
                if (i < Owners.Length && !string.IsNullOrEmpty(Owners[i]))
                    n.Owners[i] = Owners[i];
            }
            n.BossTimer = BossTimer;
            n.BossIndex = BossIndex;
            n.Penalty = Penalty;
            n.CurrentLevel = CurrentLevel;
            n.Stealing = Stealing;
            n.LabOpened = LabOpened;
            n.BossDay = BossDayUnixMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(BossDayUnixMs).LocalDateTime : default;
            n.BossRolled = BossRolledUnixMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(BossRolledUnixMs).LocalDateTime : default;
        }
    }

    public sealed class PlayerData
    {
        /// <summary>The meat locker: its zombies (game +0x16c) and its hooks (+0x80).</summary>
        public List<StaffData>? Frozen { get; set; }
        public int LockerSize { get; set; }
        public int Money { get; set; }
        public int Toxin { get; set; }
        public int Level { get; set; }
        public int Xp { get; set; }
        public float Rating { get; set; }
        public int ChefDef { get; set; }
        /// <summary>Which hints (tutorial steps and tips) have been shown; missing in saves from before the tutorial.</summary>
        public bool[]? Hints { get; set; }
        /// <summary>The kinds of zombie made so far (character def numbers).</summary>
        public List<int>? ZombieKinds { get; set; }
        /// <summary>Recipes and variants unlocked (recipe * 10 + variant); missing in saves from before.</summary>
        public List<int>? Recipes { get; set; }
        /// <summary>Chefs bought (+0x394) and unlocked early (+0x3a8), by character type.</summary>
        public List<int>? OwnedChefs { get; set; }
        public List<int>? UnlockedChefs { get; set; }
        /// <summary>Cookbooks bought (cafe +0x108).</summary>
        public List<int>? Cookbooks { get; set; }
        /// <summary>The starred recipes (game +0x68).</summary>
        public List<int>? Favorites { get; set; }
        /// <summary>The fridge's dishes (raid loot).</summary>
        /// <summary>The fridge's stock.</summary>
        public List<DishData>? Fridge { get; set; }
        /// <summary>Raid loot not yet in the fridge.</summary>
        public List<DishData>? RaidLoot { get; set; }
        public bool RaidBonusGiven { get; set; }
    }

    public sealed class DishData
    {
        public int Food { get; set; }
        public int Servings { get; set; }
        public byte Modifier { get; set; }

        public string? OrderedBy { get; set; }

        public static DishData From(Dish d) => new()
        {
            Food = d.Food, Servings = d.Servings, Modifier = d.Modifier, OrderedBy = d.OrderedBy.Length > 0 ? d.OrderedBy : null,
        };
        public Dish ToDish() => new(Food, Servings, Modifier) { OrderedBy = OrderedBy ?? "" };
    }

    public sealed class FurnitureData
    {
        public int Def { get; set; }
        public int Cell { get; set; }
        public int Orientation { get; set; }
        /// <summary>Hung on the wall of this cell (0 = upper-left edge, 1 = upper-right), or -1 on the floor.</summary>
        public int WallSide { get; set; } = -1;
        // stoves
        public DishData? Dish { get; set; }
        public int Progress { get; set; }
        public int BurnClock { get; set; }
        public bool Burnt { get; set; }
        // counters
        public List<DishData>? Stack { get; set; }
        // tables
        public bool DirtyPlate { get; set; }
        // cash items (Furniture::save writes +0x5e)
        public int Stored { get; set; }
        // habitats (Habitat::save writes +0xac): the pet's place among the pets
        public int Pet { get; set; } = -1;
        // magic fridges (+0x7a): the day of the last free dish
        public string? GiftDay { get; set; }
    }

    public sealed class StaffData
    {
        public int Def { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int Xp { get; set; }
        public float Energy { get; set; }
        public int Served { get; set; }
        public Job Job { get; set; }
        public int Cell { get; set; }
        /// <summary>The stove this character was cooking at (-1 none).</summary>
        public int CookingStoveCell { get; set; } = -1;
        public int ReanimateMs { get; set; }
        // the bio's tallies (CharacterDetails)
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Raids { get; set; }
        public long JoinedUnixMs { get; set; }
    }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZombieCafePC");
            return Path.Combine(dir, "save.json");
        }
    }

    /// <summary>A zombie with no place in the cafe (a frozen one): its record alone.</summary>
    static StaffData CaptureRecord(Character c) => new()
    {
        Def = c.Def.Index, Name = c.Name, Level = c.Level, Xp = c.Xp, Energy = c.Energy, Served = c.Served, Job = Job.None,
        Cell = -1, ReanimateMs = c.ReanimateMs, Kills = c.Kills, Deaths = c.Deaths, Raids = c.Raids, JoinedUnixMs = c.JoinedUnixMs,
    };

    static StaffData Capture(CharacterInstance c) => new()
    {
        Def = c.Def.Index, Name = c.Character.Name, Level = c.Character.Level, Xp = c.Character.Xp,
        Energy = c.Character.Energy, Served = c.Character.Served, Job = c.Character.Job, Cell = c.Cell,
        ReanimateMs = c.Character.ReanimateMs, Kills = c.Character.Kills, Deaths = c.Character.Deaths,
        Raids = c.Character.Raids, JoinedUnixMs = c.Character.JoinedUnixMs,
        CookingStoveCell = c.Tasks.OfType<Cafe.Tasks.CookTask>().FirstOrDefault()?.Stove.Cell ?? -1,
    };

    public static Data Capture(CafeController game)
    {
        var d = new Data
        {
            SavedAtUnixMs = GameClock.UnixMs,
            ClockOffsetMs = GameClock.OffsetMs,
            Open = game.Cafe.IsOpen,
            RoomWidth = game.Cafe.RoomWidth,
            RoomDepth = game.Cafe.RoomDepth,
            Player = new PlayerData
            {
                Money = game.Player.Money, Toxin = game.Player.Toxin, Level = game.Player.Level, Xp = game.Player.Xp,
                Rating = game.Player.Rating, ChefDef = game.Player.Chef.Index,
                Hints = game.Player.Hints.Save(), ZombieKinds = game.Player.ZombieKinds.ToList(),
                Frozen = game.Player.Frozen.Select(CaptureRecord).ToList(), LockerSize = game.Player.LockerSize,
                Recipes = game.Player.Recipes.OrderBy(r => r).ToList(),
                OwnedChefs = game.Player.OwnedChefs.OrderBy(r => r).ToList(),
                UnlockedChefs = game.Player.UnlockedChefs.OrderBy(r => r).ToList(),
                Cookbooks = game.Player.Cookbooks.OrderBy(r => r).ToList(),
                Favorites = game.Player.Favorites.ToList(),
                Fridge = game.Player.Fridge.Select(DishData.From).ToList(),
                RaidLoot = game.Player.RaidLoot.Select(DishData.From).ToList(),
                RaidBonusGiven = game.Player.RaidBonusGiven,
            },
            Chef = Capture(game.ChefActor),
            Neighbours = NeighbourhoodData.From(game.Player.Neighbours),
            Stored = game.Cafe.Stored.ToList(),
            Objectives = ObjectivesData.From(game.Player.Objectives),
            BoostCharges = game.Player.Boosts.Charges.ToArray(),
            BoostLastUse = game.Player.Boosts.LastUseUnix.ToArray(),
            Pets = game.Player.Pets.Select(PetData.From).ToList(),
            StoredPets = game.Player.StoredPets.Select(PetData.From).ToList(),
            AmptronUses = game.Player.AmptronUses,
            AmptronDay = game.Player.AmptronDay.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            LastPlayDay = game.Player.LastPlayDay?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            StreakDays = game.Player.StreakDays,
            UnlockedBest = game.Player.UnlockedBest,
        };
        foreach (var o in game.Cafe.Objects)
        {
            if (o is not Furniture f || f.Cell < 0)
                continue;
            var fd = new FurnitureData
            {
                Def = f.Def.Id, Cell = f.Cell, Orientation = f.Orientation, Stored = f.Stored,
                GiftDay = f.GiftDay?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            };
            switch (f)
            {
                case Stove s when s.Dish != null:
                    fd.Dish = DishData.From(s.Dish);
                    fd.Progress = s.Progress;
                    fd.BurnClock = s.BurnClock;
                    fd.Burnt = s.Burnt;
                    break;
                case ServingCounter c when c.Dishes.Count > 0:
                    fd.Stack = c.Dishes.Select(DishData.From).ToList();
                    break;
                case Habitat h:
                    fd.Pet = h.Pet != null ? game.Player.Pets.IndexOf(h.Pet.Character) : -1;
                    break;
                default:
                    // a meal being eaten is lost with its customer; a dirty plate stays to be cleared
                    if (f.Def.Category == FurnitureCategory.Table && f.Attached is FoodItem food && (food.IsDirtyPlate || food.Dish != null))
                        fd.DirtyPlate = true;
                    break;
            }
            d.Furniture.Add(fd);
        }
        var cafe = game.Cafe;
        var cells = cafe.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            for (int side = 0; side < 2; side++)
            {
                if (cells[i].Slots[side] is not CafeWall wall)
                    continue;
                d.Walls.Add([i, side, wall.Def.Id]);
                if (wall.Decor is { } decor)
                    d.Furniture.Add(new FurnitureData { Def = decor.Def.Id, Cell = i, WallSide = side });
            }
            var (u, v) = cafe.Diamond(i);
            if (cafe.InRoom(u, v))
                d.Floors.Add([i, cells[i].Floor, cells[i].Flip ? 1 : 0, cells[i].Tint]);
        }
        foreach (var s in game.Staff)
            d.Staff.Add(Capture(s));
        return d;
    }

    public static void Save(CafeController game, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Capture(game), Options));
        File.Move(tmp, path, true);
    }

    /// <summary>
    /// FUN_000b93a0 ("Delete Café" in Options): the next game starts from the beginning. The old save is kept
    /// beside it as save-deleted.json (replacing any earlier one) rather than destroyed outright.
    /// </summary>
    public static void Delete(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return;
        File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, "save-deleted.json"), true);
    }

    public static Data? TryRead(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return null;
        try
        {
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), Options);
            return d is { Version: <= Version } ? d : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rebuilds a cafe from a save: the starting room, then the saved furniture and staff, then what happened
    /// while the game was closed (<see cref="OfflineProgress"/>).
    /// </summary>
    /// <param name="rivalsAway">The game itself was closed, so the rival restaurants' clocks ran on too (false when
    /// coming back from the map or a raid, under which they kept running).</param>
    public static (CafeController Game, OfflineProgress.Summary? Away) Restore(GameData data, Data save, bool catchUp = true, bool rivalsAway = true)
    {
        // the game's clock as the save left it (never back before the computer's)
        GameClock.OffsetMs = Math.Max(0, save.ClockOffsetMs);
        var cafe = Cafe.Cafe.CreateDefault(data, new Random(1));
        // an expanded cafe first grows to its size, so its walls are there to be restored
        if (save.RoomWidth > 0 && save.RoomDepth > 0)
            cafe.Expand(save.RoomWidth, save.RoomDepth);
        if (save.Stored != null)
            cafe.Stored.AddRange(save.Stored.Where(id => id >= 0 && id < data.Furniture.Count));
        foreach (var f in save.Floors)
            if (f.Length >= 3 && f[0] >= 0 && f[0] < cafe.Cells.Length && f[1] >= 0 && f[1] < data.Tiles.Length)
            {
                cafe.Cells[f[0]].Floor = f[1];
                cafe.Cells[f[0]].Flip = f[2] != 0;
                if (f.Length >= 4)
                    cafe.Cells[f[0]].Tint = f[3];
            }
        foreach (var w in save.Walls)
            if (w.Length >= 3 && w[2] >= 0 && w[2] < data.Furniture.Count && cafe.WallAt(w[0], w[1]) is { } wall)
                wall.SetDef(data, data.Furniture[w[2]]);
        var chefDef = save.Player.ChefDef >= 0 && save.Player.ChefDef < data.Characters.Count
            ? data.Characters[save.Player.ChefDef] : data.Characters[0];
        var player = new PlayerState(data, chefDef)
        {
            Money = save.Player.Money, Toxin = save.Player.Toxin, Level = Math.Max(1, save.Player.Level),
            Xp = save.Player.Xp, Rating = save.Player.Rating,
        };
        player.Hints.Load(save.Player.Hints);
        if (save.Player.Hints == null)
            for (int i = 0; i < HintManager.Count; i++)
                player.Hints.MarkShown(i); // a cafe from before the tutorial was in the port is past it
        if (save.Player.ZombieKinds != null)
            player.ZombieKinds.UnionWith(save.Player.ZombieKinds);
        player.LockerSize = save.Player.LockerSize > 0 ? Math.Clamp(save.Player.LockerSize, 5, CafeController.LockerMax) : 5;
        foreach (var sd in save.Player.Frozen ?? [])
        {
            if (sd.Def < 0 || sd.Def >= data.Characters.Count)
                continue;
            var def = data.Characters[sd.Def];
            player.Frozen.Add(new Character(def, Role.Staff, true, sd.Name)
            {
                Level = sd.Level, Xp = sd.Xp, Energy = sd.Energy, Served = sd.Served, ReanimateMs = sd.ReanimateMs,
                Kills = sd.Kills, Deaths = sd.Deaths, Raids = sd.Raids, JoinedUnixMs = sd.JoinedUnixMs > 0 ? sd.JoinedUnixMs : 0,
            });
        }
        if (save.Player.OwnedChefs != null)
            player.OwnedChefs.UnionWith(save.Player.OwnedChefs);
        if (save.Player.UnlockedChefs != null)
            player.UnlockedChefs.UnionWith(save.Player.UnlockedChefs);
        foreach (int f in save.Player.Favorites ?? [])
            if (f >= 0 && f < data.Foods.Count && !player.Favorites.Contains(f))
                player.Favorites.Add(f);
        if (save.Player.Cookbooks != null)
            player.Cookbooks.UnionWith(save.Player.Cookbooks.Where(b => b >= 0 && b < data.Cookbooks.Count));
        if (save.Player.Fridge != null)
            foreach (var dd in save.Player.Fridge)
                if (dd.Food >= 0 && dd.Food < data.Foods.Count && dd.Servings > 0)
                    player.Fridge.Add(dd.ToDish());
        if (save.Player.RaidLoot != null)
            foreach (var dd in save.Player.RaidLoot)
                player.RaidLoot.Add(dd.ToDish());
        player.RaidBonusGiven = save.Player.RaidBonusGiven;
        if (save.Pets != null)
            foreach (var pd in save.Pets)
                if (pd.ToCharacter(data) is { } pet)
                    player.Pets.Add(pet);
        if (save.StoredPets != null)
            foreach (var pd in save.StoredPets)
                if (pd.ToCharacter(data) is { } pet)
                    player.StoredPets.Add(pet);
        player.AmptronUses = Math.Clamp(save.AmptronUses, 0, 3);
        if (DateTime.TryParseExact(save.AmptronDay, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var ampDay))
            player.AmptronDay = ampDay;
        if (DateTime.TryParseExact(save.LastPlayDay, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var lastDay))
            player.LastPlayDay = lastDay;
        player.StreakDays = Math.Max(0, save.StreakDays);
        player.UnlockedBest = Math.Max(0, save.UnlockedBest);
        save.Neighbours?.ApplyTo(player.Neighbours, data);
        if (save.BoostCharges is { Length: 8 } charges && save.BoostLastUse is { Length: 8 } last)
            for (int i = 0; i < 8; i++)
            {
                player.Boosts.Charges[i] = charges[i];
                player.Boosts.LastUseUnix[i] = last[i];
                player.Boosts.Active[i] = Boosts.Table[i].Timed && player.Boosts.Remaining(i) > 0;
            }
        // FUN_0004c69c: a save from before the quests gets a fresh set
        if (save.Objectives != null)
            save.Objectives.ApplyTo(player.Objectives);
        else
            player.Objectives.Generate(data, player, new Random());
        if (save.Neighbours == null)
            player.Neighbours.CurrentLevel = player.Level;
        player.Recipes.Clear();
        if (save.Player.Recipes != null)
            player.Recipes.UnionWith(save.Player.Recipes);
        else
            player.GrantRecipesUpToLevel(data);
        foreach (var fd in save.Furniture)
        {
            if (fd.Def < 0 || fd.Def >= data.Furniture.Count || fd.Cell < 0 || fd.Cell >= cafe.Cells.Length)
                continue;
            var (u, v) = cafe.Diamond(fd.Cell);
            if (fd.WallSide is 0 or 1)
            {
                cafe.AddWallDecor(fd.Def, u, v, fd.WallSide);
                continue;
            }
            var f = cafe.AddFurniture(fd.Def, u, v);
            if (fd.Orientation != 0)
                f.SetOrientation(fd.Orientation);
            if (f.EarnsOverTime)
                f.Stored = Math.Clamp(fd.Stored, 0, (int)f.Def.S28);
            if (DateTime.TryParseExact(fd.GiftDay, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var giftDay))
                f.GiftDay = giftDay;
            switch (f)
            {
                case Stove s when fd.Dish != null && fd.Dish.Food >= 0 && fd.Dish.Food < data.Foods.Count:
                    s.Restore(cafe, fd.Dish.ToDish(), fd.Progress, fd.BurnClock, fd.Burnt);
                    break;
                case ServingCounter c when fd.Stack != null:
                    foreach (var dd in fd.Stack)
                        if (dd.Food >= 0 && dd.Food < data.Foods.Count && dd.Servings > 0)
                            c.AddDish(cafe, dd.ToDish());
                    break;
                case Habitat h:
                    h.PetIndex = fd.Pet;
                    break;
                default:
                    if (fd.DirtyPlate && f.Def.Category == FurnitureCategory.Table)
                    {
                        f.Attach(cafe, FoodItem.DirtyPlate(data));
                        f.Busy = true;
                        cafe.DirtyTables.Add(f);
                    }
                    break;
            }
        }
        var game = new CafeController(data, cafe, player);
        if (save.Open)
            cafe.SetOpen(true);
        // GameStateCafe::load: the pets come out of their habitats into the yard
        game.SpawnPets();
        var cooking = new Dictionary<CharacterInstance, Stove>();
        void Apply(CharacterInstance z, StaffData sd)
        {
            var ch = z.Character;
            ch.Level = sd.Level;
            ch.Xp = sd.Xp;
            ch.Energy = sd.Energy;
            ch.Served = sd.Served;
            ch.Job = sd.Job;
            ch.ReanimateMs = sd.ReanimateMs;
            ch.Kills = sd.Kills;
            ch.Deaths = sd.Deaths;
            ch.Raids = sd.Raids;
            if (sd.JoinedUnixMs > 0)
                ch.JoinedUnixMs = sd.JoinedUnixMs;
            if (sd.Job == Job.Cooking && sd.CookingStoveCell >= 0 && cafe.FurnitureAt(sd.CookingStoveCell) is Stove s)
                cooking[z] = s;
            else if (sd.Job == Job.Cooking)
                ch.Job = Job.None;
        }
        if (save.Chef != null)
        {
            if (cafe.Nav.IsPassable(save.Chef.Cell))
                cafe.PlaceOnCell(game.ChefActor, save.Chef.Cell);
            Apply(game.ChefActor, save.Chef);
        }
        foreach (var sd in save.Staff)
        {
            if (sd.Def < 0 || sd.Def >= data.Characters.Count)
                continue;
            int cell = sd.Cell >= 0 && sd.Cell < cafe.Cells.Length ? sd.Cell : cafe.DoorCell;
            var z = game.AddStaff(sd.Def, cell);
            z.Character.Name = sd.Name;
            Apply(z, sd);
        }
        OfflineProgress.Summary? away = null;
        long elapsed = GameClock.UnixMs - save.SavedAtUnixMs;
        if (catchUp && elapsed > 1000)
        {
            away = OfflineProgress.Apply(game, data, Math.Min(elapsed, 30L * 24 * 3600 * 1000), cooking);
            // FUN_00057d90: the rivals' clocks ran on too
            if (rivalsAway)
                player.Neighbours.Away(data, player, elapsed / 1000.0, cafe.Rng);
            // and the quests' bonus (ZombieCafe::update ticks it in every state, FUN_00057d90 for time away)
            player.Objectives.Tick(data, elapsed);
        }
        // FUN_000b64e0: the pets' time away (their state is settled even with none)
        foreach (var pet in game.Pets)
            game.PetCatchUp(pet, catchUp && elapsed > 1000 ? Math.Min(elapsed, 30L * 24 * 3600 * 1000) : 0);
        // cooks still at it carry on; resting zombies go back out to the yard
        foreach (var w in new[] { game.ChefActor }.Concat(game.Staff))
        {
            if (w.Character.Job == Job.Cooking && cooking.TryGetValue(w, out var stove) && w.IsIdle && stove.Busy && !stove.IsDone)
                w.AddTask(new Cafe.Tasks.CookTask(w, stove, cafe));
            else if (w.Character.Job == Job.Cooking && w.IsIdle)
                w.Character.Job = Job.None;
            if (w.Character.Job == Job.Resting && w.IsIdle)
                w.AddTask(new Cafe.Tasks.RestTask(w, cafe));
        }
        // zombies knocked out in a raid lie in the yard until they reanimate (or are revived)
        foreach (var w in game.Staff)
            if (w.Character.Role == Role.Staff && w.Character.ReanimateMs >= 1)
                game.LayInYard(w);
        // and what the raid brought home goes into the fridge
        game.StockFridge();
        // GameStateCafe::load: the tombstones' powers (FUN_000b4524, FUN_000f5628) and the staff's stats (FUN_000b878c)
        player.Boosts.Refresh(data, cafe);
        player.Boosts.Recharge();
        game.RefreshStaffStats();
        return (game, away);
    }
}
