using Raylib_cs;
using ZombieCafe.Data;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Game.Cafe.Tasks;

namespace ZombieCafe.Game;

/// <summary>
/// The running cafe (the simulation half of GameStateCafe): customers come and go, staff zombies serve and
/// clean on their own, the player sends cooks to stoves and starts recipes, customers pay.
/// </summary>
public sealed class CafeController : ICafeEvents
{
    readonly GameData data;
    public Cafe.Cafe Cafe { get; }
    public PlayerState Player { get; }
    /// <summary>+0x1c: the chef standing in the cafe.</summary>
    public CharacterInstance ChefActor { get; private set; }
    /// <summary>+0x20: the staff zombies.</summary>
    public List<CharacterInstance> Staff { get; } = new();
    /// <summary>+0x50: customers and passers-by.</summary>
    public List<CharacterInstance> Customers { get; } = new();
    /// <summary>+0x78: the character the player picked.</summary>
    public CharacterInstance? Selected { get; set; }
    /// <summary>What happened while the game was closed (shown once on return).</summary>
    public OfflineProgress.Summary? Away { get; set; }
    /// <summary>+0x84: the customer the tutorial brings in to be served and then infected.</summary>
    public CharacterInstance? TutorialCustomer { get; set; }
    /// <summary>The tutorial arrow is out (level-ups at 4, 6, 8, 9 wait for it; set by the screen).</summary>
    public bool TutorialArrowUp { get; set; }
    public HintManager Hints => Player.Hints;

    int spawnTimer;   // +0x1f4
    int bobClock;     // game +0x358

    static readonly int[] ServeSounds = [59, 60, 61, 192, 193, 194, 195, 196];

    public event Action<int>? SoundRequested;
    /// <summary>A message box the original would show (title, text).</summary>
    public event Action<string>? Message;
    public event Action<int>? LevelUp;
    /// <summary>A HUD button's badge goes up or down (FUN_0011029c): button, on.</summary>
    public event Action<int, bool>? HudBadge;
    /// <summary>A HUD button appears (FUN_001102e4).</summary>
    public event Action<int>? HudButtonShown;
    /// <summary>
    /// A recipe was learned (FUN_000d9090's RecipeUnlockedDialog, FUN_00104c00): food, variant, and for one unlocked
    /// from the fridge the line on the unlocking milestones (empty when there is none).
    /// </summary>
    public event Action<int, int, string>? RecipeDiscovered;
    /// <summary>The recipe (and variant) the last level-up brought, for the level-up box.</summary>
    public (int Food, int Variant)? LastLevelReward { get; private set; }
    /// <summary>A short music cue (the original's second music channel, FUN_00053c58 with channel 1).</summary>
    public event Action<int>? MusicCue;

    Random Rng => Cafe.Rng;

    static readonly bool LogEvents = Environment.GetEnvironmentVariable("ZC_LOG") == "1";
    int clockMs;

    /// <summary>Debug trace of the simulation (ZC_LOG=1).</summary>
    void Log(string s)
    {
        if (LogEvents)
            Console.WriteLine($"[{clockMs / 1000,6}s] {s}");
    }

    /// <summary>strings.bin: the chef's name ("You", FUN_00132eb4).</summary>
    const int ChefName = 389;

    public CafeController(GameData data, Cafe.Cafe cafe, PlayerState player)
    {
        this.data = data;
        Cafe = cafe;
        Player = player;
        cafe.Events = this;
        var chef = new Character(player.Chef, Role.Visitor, player.Chef.Kind == 1, data.Strings[ChefName]);
        ChefActor = cafe.AddCharacter(chef, cafe.IndexOf(cafe.OriginU - 3, cafe.OriginV + 2));
        ResetSpawnTimer();
    }

    /// <summary>
    /// FUN_001329b0 (the chef store's "Select This Chef!"): the player's chef becomes this type (zombie types make
    /// a zombie chef). The chef keeps their name and tally; the new one stands where the old one stood.
    /// </summary>
    public void ChangeChef(CharacterDef def)
    {
        var old = ChefActor;
        if (old.Def == def)
            return;
        Player.Chef = def;
        var ch = new Character(def, Role.Visitor, def.Kind == 1, old.Character.Name) { Served = old.Character.Served };
        old.ClearTasks();
        int cell = old.Cell;
        Cafe.Remove(old);
        ChefActor = Cafe.AddCharacter(ch, cell);
        ChefActor.SetAnimation(CharAnim.Idle, old.Facing, true);
        if (Selected == old)
            Selected = null;
        Log($"the chef is now {def.Name}");
    }

    /// <summary>
    /// FUN_0011daac (the bio's "Fire Zombie", confirmed): the zombie is let go - human again (+0x18 cleared), a
    /// customer (+0x19 = 1) with nothing to do, so they walk off down the street. The tips about firing are done.
    /// </summary>
    public void Fire(CharacterInstance w)
    {
        if (!Staff.Remove(w))
            return;
        w.ClearTasks();
        var c = w.Character;
        c.Role = Role.Customer;
        c.IsZombie = false;
        c.Job = Job.None;
        w.RefreshParts();
        Customers.Add(w);
        if (Selected == w)
            Selected = null;
        Sound(40);
        var h = Player.Hints;
        if (h.IsShown(0x1e) && !h.IsShown(0x20))
        {
            h.MarkShown(0x1f);
            h.MarkShown(0x20);
            HintArrowDone?.Invoke();
        }
        else if (h.IsShown(0x17) && !h.IsShown(0x18))
        {
            h.MarkShown(0x18);
            HintArrowDone?.Invoke();
        }
        Log($"{c.Name} was fired");
    }

    /// <summary>FUN_00150020: the tutorial arrow can go (the screen hides it).</summary>
    public event Action? HintArrowDone;

    /// <summary>Hires a zombie of the given type (it starts out on serving duty).</summary>
    public CharacterInstance AddStaff(int defIndex, int cell)
    {
        var def = data.Characters[defIndex];
        var ch = new Character(def, Role.Staff, true, Character.RandomName(data, def.IsFemale, Rng)) { Job = Job.Serving };
        ch.Energy = ch.MaxEnergy(data);
        var c = Cafe.AddCharacter(ch, cell);
        c.AttackCheck = data.K.Int(0x42d114);
        Staff.Add(c);
        return c;
    }

    // ---------------------------------------------------------------- ICafeEvents

    /// <summary>FUN_0014cb88: tell the tutorial something happened.</summary>
    public void Hint(int id, bool flag) => Player.Hints.Trigger(id, flag);

    public void Sound(int id) => SoundRequested?.Invoke(id);
    public int RandomServeSound() => ServeSounds[Rng.Next(ServeSounds.Length)];
    public bool BurningEnabled => true;
    /// <summary>FUN_000f54c4(5) == 2: the Grim Reaper tombstone stands in the cafe (serving teaches twice as much).</summary>
    public bool DoubleXpEvent => Player.Boosts.Placed(Boosts.GrimReaper);
    /// <summary>FUN_000b5c10 case 3 (DAT_000b64d8): the Super Server tombstone in the cafe quickens rest.</summary>
    public float RestBonus => Player.Boosts.Placed(Boosts.SuperServer) ? 0.1f : 0f;
    public CharacterDef Chef => Player.Chef;
    public void DishPlaced(Dish dish, CharacterInstance by)
    {
        AwardCookXp(dish, by, false);
        // FUN_001bab54: a batch for a quest's recipe
        if (Hints.IsShown(0x53))
            Player.Objectives.Cooked(dish.Food);
    }
    public bool HintShown(int id) => Player.Hints.IsShown(id);

    /// <summary>+0x98: the zombie that last went for a customer (the tutorial points at it).</summary>
    public CharacterInstance? LastAttacker { get; private set; }

    // ---------------------------------------------------------------- frame

    public void Update(int dtMs)
    {
        clockMs += dtMs;
        // ZombieCafe::update: the 1 s sine every bobbing sign follows
        bobClock += dtMs;
        while (bobClock > 1000)
            bobClock -= 1000;
        Cafe.Bob = 25f * MathF.Sin(bobClock * MathF.Tau / 1000f) / 2f;
        // ZombieCafe::update: the rival restaurants' clocks, and the quests' bonus
        Player.Neighbours.Update(data, Player, dtMs, Rng);
        Player.Objectives.Tick(data, dtMs);
        // GameStateCafe::update: the tombstones (what stands where, the day's uses back, a timed one running out)
        var boosts = Player.Boosts;
        bool wasRunning = boosts.Running(Boosts.SuperServer);
        boosts.Refresh(data, Cafe);
        boosts.Recharge();
        if (boosts.Expire() || wasRunning != boosts.Running(Boosts.SuperServer))
            RefreshStaffStats();

        Cafe.Update(dtMs);
        if (Player.CheckLevelUp(data, TutorialArrowUp))
        {
            LastLevelReward = LevelReward(Player.Level);
            LevelUpExtras(Player.Level);
            LevelUp?.Invoke(Player.Level);
        }
        UpdateWorkers(dtMs);
        UpdateStaffBehaviour(dtMs);
        UpdateAutoBreaks(dtMs);
        UpdateCustomers(dtMs);
        UpdatePets(dtMs);
        UpdateHabitats(dtMs);
        // GameStateCafe::update: whoever is picked tells the tutorial where the player has got to (a pet is picked apart, +0x7c)
        if (Selected != null && !Selected.IsPet)
            Hint(Selected == ChefActor ? 1 : IsTutorialTarget(Selected) ? 0xd : 0x10, true);
    }

    /// <summary>
    /// FUN_000d1f20, the rest of a level up: badges on the cookbook, map and store; the rival a level up starts
    /// afresh (its saved layout dropped, its clock zeroed); the rival across the street catches up if it fell more
    /// than two levels behind; badges on the zombie list and the pets when the new level allows one more of either
    /// (K 0x42d5b4, 0x42d5b0).
    /// </summary>
    void LevelUpExtras(int level)
    {
        HudBadge?.Invoke(5, true);
        HudBadge?.Invoke(4, true);
        HudBadge?.Invoke(1, true);
        var n = Player.Neighbours;
        n.Timers[Neighbourhood.SlotOf(level + 1)] = 0;
        if (n.CurrentLevel < level - 2)
        {
            n.Penalty = 0;
            n.CurrentLevel = level;
        }
        bool More(int table) => data.K.Ints(table) is var t && level >= 2 && level - 1 < t.Length && t[level - 2] < t[level - 1];
        if (More(0x42d5b4))
            HudBadge?.Invoke(8, true);
        if (More(0x42d5b0))
            HudBadge?.Invoke(9, true);
    }

    /// <summary>
    /// FUN_000d1f20: each level brings its recipe (DAT_0042d5ac); once that is known, a random variant the
    /// player lacks of a recipe they know; with none left, a random dish (any recipe, any of K 0x42d3bc variants)
    /// in the fridge.
    /// </summary>
    (int, int)? LevelReward(int level)
    {
        var table = data.K.Ints(0x42d5ac);
        int r = level - 1 < table.Length ? table[level - 1] : -1;
        if (r >= 0 && r < data.Foods.Count && !Player.HasRecipe(r, 0))
        {
            Player.UnlockRecipe(r, 0);
            return (r, 0);
        }
        var candidates = new List<(int, int)>();
        foreach (var f in data.Foods)
        {
            if (!(f.Kind < 5 || f.StoveFamily == 0) || !Player.KnowsRecipe(f.Index))
                continue;
            for (int v = 0; v < 10; v++)
                if (!Player.HasRecipe(f.Index, v))
                    candidates.Add((f.Index, v));
        }
        if (candidates.Count == 0)
        {
            int food = data.Foods.Count < 1 ? 0 : Rng.Next(data.Foods.Count);
            int variants = data.K.Int(0x42d3bc);
            Player.Fridge.Add(Dish.Cook(data, food, (byte)(variants < 1 ? 0 : Rng.Next(variants))));
            return null;
        }
        var pick = candidates[Rng.Next(candidates.Count)];
        Player.UnlockRecipe(pick.Item1, pick.Item2);
        return pick;
    }

    /// <summary>"Spicy Mystery Meat" (FUN_0016d7fc).</summary>
    public string RecipeName(int food, int variant) =>
        variant == 0 ? data.Foods[food].Name : data.Strings[StringTable.ModifierNames + variant] + " " + data.Foods[food].Name;

    /// <summary>What unlocking a recipe or variant in the book costs in toxin (FUN_001038d4: 10 for a rare dish, else 5).</summary>
    public int UnlockPrice(int food, int variant) =>
        data.Foods[food].IsRare && variant == 0 ? data.K.Int(0x42d708) : data.K.Int(0x42d700);

    /// <summary>
    /// FUN_0016d9e8: toxin to finish a dish at once: what the batch is worth (servings x price + 10 x XP)
    /// over 500, for the part of the cooking time still to go; at least 1.
    /// </summary>
    public int FinishCost(Stove s)
    {
        if (s.Dish == null)
            return 0;
        var f = data.Foods[s.Dish.Food];
        int v = s.Dish.Modifier;
        int cook = f.CookSeconds(v);
        if (cook <= 0)
            return 1;
        int value = f.ServingsWith(v) * f.PriceWith(v) + f.XpWith(v) * 10;
        int left = cook - s.Progress / 1000;
        return Math.Max(1, (int)(left * (value / 500f) / cook));
    }

    /// <summary>FUN_00182eac: spend the toxin and have the dish done now.</summary>
    public bool FinishWithToxin(Stove s)
    {
        int cost = FinishCost(s);
        if (!s.Busy || s.IsDone || s.Dish == null)
            return false;
        if (Player.Toxin < cost)
        {
            Message?.Invoke(data.Strings[320]);
            return false;
        }
        Player.Toxin -= cost;
        s.FinishNow(Cafe);
        return true;
    }

    /// <summary>
    /// GameStateCafe::update, a tap on furniture that is holding something: a cash item is emptied into the
    /// player's money (def +0x24 = 1), toxin (2, FUN_0004ef9c) or experience, with a note and the till's ring.
    /// </summary>
    public bool Collect(Furniture f)
    {
        int n = f.Stored;
        if (n == 0)
            return false;
        StatusNote.Kind kind;
        switch (f.Def.B24)
        {
            case 2:
                Player.Toxin += n;
                kind = StatusNote.Kind.Toxin;
                break;
            case 1:
                Player.Money += n;
                kind = StatusNote.Kind.Money;
                break;
            default:
                Player.Xp += n;
                kind = StatusNote.Kind.Xp;
                break;
        }
        Cafe.AddNote(new StatusNote(Cafe, f, kind, n.ToString(), data.K.NoteTime));
        Sound(9);
        f.Stored = 0;
        Log($"emptied {f.Def.Name}: {n} ({kind})");
        return true;
    }

    /// <summary>
    /// FUN_000c0ef4 (an expansion bought, category 12): the room grows to the expansion's size (def +0x15 x
    /// +0x16, <see cref="Cafe.Expand"/>). Zombies resting beyond the far edge move out with it, and people
    /// standing where the room now reaches (or in the lanes beyond) are set back a step and start over
    /// (CharacterInstance vf42).
    /// </summary>
    public bool Expand(FurnitureDef def)
    {
        int oldW = Cafe.RoomWidth, oldH = Cafe.RoomDepth;
        if (def.Width <= oldW && def.Depth <= oldH)
            return false;
        Player.Hints.MarkShown(0x4e);
        Cafe.Expand(def.Width, def.Depth);
        int dw = Cafe.RoomWidth - oldW, dh = Cafe.RoomDepth - oldH;
        int u0 = Cafe.OriginU, v0 = Cafe.OriginV;
        foreach (var z in Staff)
        {
            if (z.Character.Job is not (Job.Resting or Job.Other))
                continue;
            var (u, v) = Cafe.Diamond(z.Cell);
            if (v > v0 + oldH && u < u0 && Cafe.IndexOf(u, v + dh) is int to and >= 0)
                Cafe.PlaceOnCell(z, to);
        }
        foreach (var c in Customers)
        {
            var (u, v) = Cafe.Diamond(c.Cell);
            int nu = u < u0 - oldW ? u - dw : u, nv = v > v0 + oldH ? v + dh : v;
            if ((nu, nv) == (u, v) || Cafe.IndexOf(nu, nv) is not (int to and >= 0))
                continue;
            c.ClearTasks();
            Cafe.PlaceOnCell(c, to);
        }
        Log($"cafe expanded to {Cafe.RoomWidth}x{Cafe.RoomDepth}");
        return true;
    }

    /// <summary>FUN_001038d4: buy a recipe or variant with toxin.</summary>
    public bool BuyRecipe(int food, int variant)
    {
        int price = UnlockPrice(food, variant);
        if (Player.Toxin < price)
        {
            Message?.Invoke(data.Strings[320]);
            return false;
        }
        Player.Toxin -= price;
        Player.UnlockRecipe(food, variant);
        Sound(9);
        return true;
    }

    // ---------------------------------------------------------------- staff (FUN_000d6ce4)

    IEnumerable<CharacterInstance> Workers()
    {
        yield return ChefActor;
        foreach (var s in Staff)
            yield return s;
    }

    /// <summary>FUN_000c0a64: a path exists from the character to (next to) a cell.</summary>
    bool CanReach(CharacterInstance c, int cell)
    {
        var nav = Cafe.Nav;
        bool passable = nav.IsPassable(cell);
        if (!passable)
            nav.SetPassable(cell, true);
        var path = nav.FindPath(c.Cell, cell);
        if (!passable)
            nav.SetPassable(cell, false);
        return path != null;
    }

    static CharacterInstance PopRandom(List<CharacterInstance> list, Random rng)
    {
        int i = list.Count < 1 ? 0 : rng.Next(list.Count);
        var c = list[i];
        list.RemoveAt(i);
        return c;
    }

    /// <summary>A free cell somewhere in the room (no furniture on it), or a random one when none turns up.</summary>
    public int FreeRoomCell()
    {
        for (int tries = 0; tries < 60; tries++)
        {
            int cell = RandomRoomCell();
            if (Cafe.Nav.IsPassable(cell) && Cafe.Cells[cell].Slots[2] == null)
                return cell;
        }
        return RandomRoomCell();
    }

    int RandomRoomCell()
    {
        int u = data.K.PlayerOriginU - 1 - (Cafe.RoomWidth == 0 ? 0 : Rng.Next(Cafe.RoomWidth));
        int v = data.K.PlayerOriginV + 1 + (Cafe.RoomDepth == 0 ? 0 : Rng.Next(Cafe.RoomDepth));
        return Cafe.IndexOf(u, v);
    }

    void UpdateWorkers(int dtMs)
    {
        // zombies on serving duty are the candidates for serving and clearing tables
        var candidates = new List<CharacterInstance>();
        if (Cafe.Waiting.Count > 0 || Cafe.DirtyTables.Count > 0)
            foreach (var w in Workers())
                if (w.Character.Job == Job.Serving)
                    candidates.Add(w);

        if (Cafe.Waiting.Count > 0 && Cafe.TotalServings() != 0)
        {
            var customer = Cafe.Waiting[0];
            if (customer.AnimId == (int)CharAnim.Sit)
            {
                if (candidates.Count > 0)
                {
                    bool unreachable = true;
                    int table = Cafe.Neighbour(customer.Cell, (int)customer.Facing);
                    while (candidates.Count > 0)
                    {
                        var w = PopRandom(candidates, Rng);
                        if (w.IsIdle)
                        {
                            if (CanReach(w, table))
                            {
                                Log($"{w.Character.Name} serves {customer.Character.Name}");
                                w.AddTask(new ServeTask(w, Cafe, customer));
                                unreachable = false;
                                break;
                            }
                        }
                        else if (unreachable)
                        {
                            unreachable = !CanReach(w, table);
                        }
                    }
                    if (unreachable && Cafe.Waiting.Count > 0 && Cafe.Waiting[0] == customer)
                    {
                        Cafe.Unreachable.Add(customer);
                        Cafe.Waiting.RemoveAt(0);
                        Hint(0x43, false);
                    }
                }
            }
            else
            {
                Cafe.Waiting.RemoveAt(0);
            }
        }

        if (Cafe.DirtyTables.Count > 0)
        {
            var table = Cafe.DirtyTables[0];
            bool noneReach = candidates.Count != 0;
            while (candidates.Count > 0)
            {
                var w = PopRandom(candidates, Rng);
                if (w.IsIdle)
                {
                    bool found = false;
                    foreach (var sink in Cafe.FindFurniture(FurnitureCategory.Sink, w.Cell))
                    {
                        if (CanReach(w, sink.Cell))
                        {
                            Log($"{w.Character.Name} clears a table");
                            w.AddTask(new CleanupTask(w, Cafe, table));
                            noneReach = false;
                            found = true;
                            break;
                        }
                    }
                    if (found)
                        break;
                }
                else if (noneReach)
                {
                    noneReach = true;
                    foreach (var sink in Cafe.FindFurniture(FurnitureCategory.Sink, w.Cell))
                        if (CanReach(w, sink.Cell))
                        {
                            noneReach = false;
                            break;
                        }
                }
            }
            if (noneReach)
                Hint(0x43, false);
        }

        // idle staff stand around, fidget or wander; nobody keeps standing on someone else's cell
        foreach (var w in Workers().ToArray())
        {
            if (w.IsIdle)
            {
                bool move;
                if (Cafe.Nav.IsPassable(w.Cell))
                {
                    move = !Cafe.IsInside(w.Cell);
                    if (!move)
                        foreach (var other in Workers())
                            if (other != w && !(other.CurrentTask?.IsMoving ?? false) && other.Cell == w.Cell)
                            {
                                move = true;
                                break;
                            }
                }
                else
                {
                    move = true;
                }
                if (Rng.Next(20) == 0)
                {
                    if (w.Character.Job == Job.Serving)
                        w.AddTask(new MoveTask(w, Cafe, RandomRoomCell()));
                    else
                        w.AddTask(new WaitTask(w, 1000, true));
                }
                else if (move)
                {
                    w.AddTask(new MoveTask(w, Cafe, RandomRoomCell()));
                }
                else
                {
                    w.AddTask(new WaitTask(w, 1000));
                }
            }
            w.Update(dtMs);
            Cafe.Resort(w);
        }
    }

    // ---------------------------------------------------------------- staff behaviour (FUN_000d7694)

    int NewBoredom(Character c)
    {
        // a resting or daydreaming zombie's next spell of work: 2 x 120 s - 30 s at random, plus focus x 120 s
        var k = data.K;
        int b = k.Int(0x42cf18), spread = 2 * b - k.Int(0x42cf1c);
        return (spread > 0 ? Rng.Next(spread) : 0) + c.Focus * b;
    }

    /// <summary>
    /// FUN_00035b74: is a tired zombie hungry enough to go for a customer? Below (11 - restraint) x 2% + 5% of
    /// full energy it might, the more likely the emptier it is (from 1.9%).
    /// </summary>
    bool Hungry(CharacterInstance z)
    {
        var c = z.Character;
        float limit = ((11 - c.Restraint) * 0.2f / 10f + 0.05f) * c.MaxEnergy(data);
        if (c.Energy > limit)
            return false;
        float floor = data.K.Float(0x42cef8);
        float chance = (floor + (1f - c.Energy / limit) * (1f - floor)) * 100f;
        return Rng.Next(100) < (int)chance;
    }

    /// <summary>FUN_001625c4: customers inside, nearest first (Manhattan distance on the diamond grid).</summary>
    List<CharacterInstance> AttackTargets(int fromCell)
    {
        var (u0, v0) = Cafe.Diamond(fromCell);
        return Customers
            .Where(c => c.Character.Role == Role.Customer && !c.IsKnockedOut && Cafe.IsInside(c.Cell))
            .OrderBy(c =>
            {
                var (u, v) = Cafe.Diamond(c.Cell);
                return Math.Abs(u - u0) + Math.Abs(v - v0);
            })
            .ToList();
    }

    /// <summary>
    /// FUN_000d7694: what staff do of their own accord. Resting refreshes their patience. Every minute a
    /// tired one may get hungry and go for the nearest customer (once the tutorial has shown it, hint 0x50);
    /// one already on the attack picks the next victim. When boredom runs out they stop and daydream.
    /// </summary>
    void UpdateStaffBehaviour(int dtMs)
    {
        var k = data.K;
        foreach (var z in Staff.ToArray())
        {
            var c = z.Character;
            switch (c.Job)
            {
                case Job.Resting:
                    z.Boredom = NewBoredom(c);
                    break;
                case Job.Attacking:
                    if (z.IsIdle && AttackTargets(z.Cell).FirstOrDefault() is { } next)
                        StartAttack(z, next);
                    break;
                case Job.Other:
                    // down for the count: up again when the time runs out (K 0x42d3d8 energy to start with)
                    c.ReanimateMs = Math.Max(0, c.ReanimateMs - dtMs);
                    if (c.ReanimateMs == 0)
                    {
                        c.Job = Job.None;
                        c.Energy = Math.Clamp(k.Int(0x42d3d8), 0, c.MaxEnergy(data));
                    }
                    break;
                default:
                    z.AttackCheck -= dtMs;
                    z.Boredom -= dtMs;
                    if (z.AttackCheck < 1)
                    {
                        if (Hungry(z) && Hints.IsShown(0x50))
                        {
                            z.Interrupt();
                            if (AttackTargets(z.Cell).FirstOrDefault() is { } victim)
                            {
                                z.Interrupt();
                                z.Groan();
                                StartAttack(z, victim);
                            }
                        }
                        z.AttackCheck = k.Int(0x42d114);
                    }
                    if (z.Boredom < 1)
                    {
                        z.Boredom = NewBoredom(c);
                        var job = c.Job;
                        var anim = (CharAnim)Math.Max(0, z.AnimId);
                        z.Interrupt();
                        var dream = new DaydreamTask(z, Cafe, k.Int(0x42cf14), job, anim);
                        dream.Finished += _ => DaydreamOver(dream);
                        z.AddTask(dream);
                        Log($"{c.Name} starts daydreaming");
                    }
                    break;
            }
        }
    }

    /// <summary>FUN_000d9620: back from a daydream to the animation and job it left (a cook to its stove).</summary>
    void DaydreamOver(DaydreamTask dream)
    {
        var z = dream.Owner;
        var c = z.Character;
        z.SetAnimation(dream.PreviousAnim, z.Facing);
        c.Job = dream.PreviousJob;
        if (c.Job != Job.Cooking)
            return;
        if (Cafe.FurnitureAt(dream.FacingCell) is Stove stove)
            z.AddTask(new CookTask(z, stove, Cafe));
        else
            c.Job = Job.None;
    }

    void StartAttack(CharacterInstance z, CharacterInstance victim)
    {
        z.Interacting = victim;
        var attack = new AttackTask(z, victim, Cafe);
        attack.Struck += t => Struck(t.Owner, t.Target, t.HitBefore);
        attack.Finished += _ => AttackOver(attack);
        z.AddTask(attack);
        Log($"{z.Character.Name} goes for {victim.Character.Name}");
    }

    /// <summary>
    /// FUN_000d927c, at every blow or bite: the tutorial hears of it (hint 0x1d), everyone else inside drops
    /// what they are doing and runs for it screaming, and the first blow costs the cafe 0.1 of its rating.
    /// </summary>
    void Struck(CharacterInstance attacker, CharacterInstance victim, bool before)
    {
        LastAttacker = attacker;
        Hint(5, true);
        int men = 0, women = 0;
        foreach (var c in Customers)
        {
            if (c.IsKnockedOut || !Cafe.IsInside(c.Cell) || c.Fleeing)
                continue;
            c.Interrupt();
            if (c.Def.IsFemale)
                women++;
            else
                men++;
            c.Scared = true;
            c.Fleeing = true;
        }
        if (!before)
        {
            int scream = CharacterInstance.CrowdScream(men, women, Rng);
            if (scream >= 0)
                Sound(scream);
            Player.Rating = Math.Max(0f, Player.Rating - data.K.Float(0x42d0a8));
        }
    }

    /// <summary>FUN_000d9378: the attack is over. A victim that got away runs off; one that went down is eaten.</summary>
    void AttackOver(AttackTask attack)
    {
        var victim = attack.Target;
        var z = attack.Owner;
        if (!attack.Success)
        {
            if (!victim.IsZombie && !victim.IsKnockedOut && Customers.Contains(victim))
            {
                victim.Interrupt();
                victim.AddTask(new MoveTask(victim, Cafe, (Rng.Next() & 1) == 0 ? Cafe.StreetEndB : Cafe.StreetEndA, CharAnim.Run));
                victim.Fleeing = true;
            }
            return;
        }
        if (Selected == victim)
            Selected = null;
        if (z.Character.Role != Role.Staff)
            return;
        var feast = new FeastTask(z, victim, Cafe);
        feast.Bite += t => Struck(t.Owner, t.Victim, true);
        feast.Finished += _ => FeastOver(feast);
        z.AddTask(feast);
    }

    /// <summary>FUN_000dcc80: the meal is over; the body leaves the customers (to fade away if anything is left).</summary>
    void FeastOver(FeastTask feast)
    {
        var victim = feast.Victim;
        Customers.Remove(victim);
        Cafe.Waiting.Remove(victim);
        Cafe.Unreachable.Remove(victim);
        if (!feast.Success)
            bodies.Add(victim);
        else if (feast.Owner.Character.Job == Job.Attacking)
            feast.Owner.Character.Job = Job.None;
        if (!feast.Success)
            victim.StartFading();
        Log($"{feast.Owner.Character.Name} {(feast.Success ? "ate" : "left")} {victim.Character.Name}");
    }

    // +0x2c: bodies fading away
    readonly List<CharacterInstance> bodies = new();

    // +0x200 / +0x204: where the next zombie laid out in the yard goes (seven to a row, K 0x42ce54)
    int yardSlot, yardRow;

    /// <summary>
    /// FUN_000b5c10's last branch, as the cafe loads: a zombie still reanimating (knocked out in a raid) lies in the
    /// yard behind the cafe, the next spot along the row below the room, and the hint about it is due (0x45).
    /// </summary>
    public void LayInYard(CharacterInstance z)
    {
        var k = data.K;
        z.Face(Facing.SE);
        if (yardSlot > k.Int(0x42ce54) - 1)
        {
            yardSlot = 0;
            yardRow++;
        }
        int cell = Cafe.IndexOf(k.PlayerOriginU - 1 - yardSlot, k.PlayerOriginV + yardRow + Cafe.RoomDepth + 1);
        if (cell >= 0)
            Cafe.PlaceOnCell(z, cell);
        z.AddTask(new ReanimateTask(z, Cafe));
        Hint(0x45, true);
        yardSlot++;
    }

    /// <summary>Why a zombie cannot be revived now (null: it can; "" for no message) - FUN_000b9f90's checks.</summary>
    public string? CannotRevive(CharacterInstance z)
    {
        if (z.Character.Role is not (Role.Staff or Role.Pet) || z.Character.ReanimateMs < 1)
            return "";
        return Player.Toxin < 1 ? data.Strings[320] : null;
    }

    /// <summary>
    /// FUN_000bad9c ("ReviveZombie"): a vial of toxin has a knocked-out zombie up at once, with a little energy
    /// (K 0x42d3d8) - its reanimation task sees the clock at 0 and lets it go back to work.
    /// </summary>
    public bool Revive(CharacterInstance z)
    {
        if (CannotRevive(z) is { } why)
        {
            if (why.Length > 0)
                Message?.Invoke(why);
            return false;
        }
        var c = z.Character;
        c.Energy = Math.Clamp(data.K.Int(0x42d3d8), 0, c.MaxEnergy(data));
        c.ReanimateMs = 0;
        c.Job = Job.None;
        Player.Toxin -= 1;
        if (c.Pet is { } pet)
            // "RevivePet": the cat purrs like a zombie, the robots whirr, the rest growl
            Sound(pet.IsCat ? 173 + Rng.Next(2) : pet.IsRobot ? 219 : 210 + Rng.Next(2));
        else
            Sound((z.Def.IsFemale ? 175 : 173) + Rng.Next(2));
        Log($"{c.Name} revived");
        return true;
    }

    // ---------------------------------------------------------------- pets (FUN_000d81b8, FUN_000d8448, FUN_000c2c50)

    /// <summary>GameStateCafe +0x38: the pets in the cafe (each lives in a habitat in the yard).</summary>
    public List<CharacterInstance> Pets { get; } = new();

    IEnumerable<Habitat> Habitats => Cafe.Objects.OfType<Habitat>().Where(h => h.Cell >= 0);

    /// <summary>
    /// GameStateCafe +0x240 (FUN_000f20a0 / FUN_000f2128): the habitats standing in the cafe, by the pet they bring -
    /// what the pets' bonuses count.
    /// </summary>
    public int PetCount(int pet) => Habitats.Count(h => h.Def.PetId == pet);

    /// <summary>DAT_0042cd08 (FUN_0008221c): the cookbook of each special stove's dishes.</summary>
    static readonly int[] StoveCookbooks = [-1, 2, 3, 4, 6, 9, 14, 15, 16, 17, 18, 19, 20, 22, 24, 25, 26];

    /// <summary>FUN_0009d748: a new pet's record - a random first name, full energy, today's date.</summary>
    Character NewPet(PetDef def) =>
        new(def.Body, Role.Pet, true, Character.RandomName(data, Rng.Next(2) == 1, Rng))
        {
            JoinedUnixMs = GameClock.UnixMs,
        };

    /// <summary>A pet's body in the cafe (PetInstance, FUN_0009c1c0), living in its habitat (FUN_00195ce4).</summary>
    CharacterInstance SpawnPet(Habitat h, Character record, int index)
    {
        var pet = new CharacterInstance(Cafe, record);
        h.Bind(pet, index);
        Cafe.Add(pet);
        Pets.Add(pet);
        return pet;
    }

    /// <summary>
    /// FUN_000c2c50 ("PetPurchased"): a habitat bought and set down brings its pet (habitat def +0x4c) - a new record
    /// among the player's pets (game +0x188), at home and about to come out.
    /// </summary>
    public CharacterInstance? PetPurchased(Habitat h)
    {
        if (h.Def.PetId >= data.Pets.Count)
            return null;
        var record = NewPet(data.Pets[h.Def.PetId]);
        Player.Pets.Add(record);
        var pet = SpawnPet(h, record, Player.Pets.Count - 1);
        pet.Visible = false;
        h.TakeInNew();
        // the pets button's "room for another" badge has done its job
        HudBadge?.Invoke(9, false);
        Log($"pet {record.Name} the {data.Pets[h.Def.PetId].Name} moves in");
        return pet;
    }

    /// <summary>
    /// FUN_000cb270, a habitat out of the storage box: the first pet of its kind waiting there (game +0x194) moves back
    /// in - dated today, as in the original - and comes out; one still knocked out lies in front of its home.
    /// </summary>
    public void PetFromStorage(Habitat h)
    {
        int i = Player.StoredPets.FindIndex(p => p.Pet?.Index == h.Def.PetId);
        if (i < 0)
        {
            PetPurchased(h);
            return;
        }
        var record = Player.StoredPets[i];
        Player.StoredPets.RemoveAt(i);
        record.JoinedUnixMs = GameClock.UnixMs;
        Player.Pets.Add(record);
        var pet = SpawnPet(h, record, Player.Pets.Count - 1);
        h.TakeInNew();
        if (record.Energy <= 0f)
        {
            int door = h.DoorCell(Cafe);
            Cafe.PlaceOnCell(pet, door >= 0 ? door : h.Cell);
            pet.Face(h.OutFacing);
            pet.AddTask(new ReanimateTask(pet, Cafe));
        }
        else
        {
            pet.Visible = false;
        }
    }

    /// <summary>FUN_000cb270, a habitat into the storage box: its pet goes along (its record to game +0x194).</summary>
    public void PetStored(Habitat h)
    {
        if (h.Pet is not { } pet)
            return;
        RemovePet(h, pet);
        Player.StoredPets.Add(pet.Character);
    }

    /// <summary>FUN_000c3118 ("PetSold"): a habitat salvaged takes its pet away for good.</summary>
    public void PetSold(Habitat h)
    {
        if (h.Pet is { } pet)
            RemovePet(h, pet);
    }

    void RemovePet(Habitat h, CharacterInstance pet)
    {
        pet.DropTasks();
        Cafe.Remove(pet);
        Pets.Remove(pet);
        if (Selected == pet)
            Selected = null;
        Player.Pets.Remove(pet.Character);
        h.Unbind();
        // the others' numbers close up (record +0x48, habitat +0xac, FUN_0009d218)
        foreach (var other in Pets)
            if (other.Home is { } home)
                home.PetIndex = Player.Pets.IndexOf(other.Character);
        Log($"pet {pet.Character.Name} has gone");
    }

    /// <summary>
    /// GameStateCafe::load: each habitat's pet (game +0x188 by habitat +0xac) out in the yard somewhere
    /// (FUN_00163a80). A habitat whose pet is missing from the save gets a new one.
    /// </summary>
    public void SpawnPets()
    {
        foreach (var h in Habitats.ToList())
        {
            if (h.Pet != null)
                continue;
            int index = h.PetIndex;
            if (index < 0 || index >= Player.Pets.Count || Pets.Any(p => p.Character == Player.Pets[index]))
            {
                if (h.Def.PetId >= data.Pets.Count)
                    continue;
                Player.Pets.Add(NewPet(data.Pets[h.Def.PetId]));
                index = Player.Pets.Count - 1;
            }
            var pet = SpawnPet(h, Player.Pets[index], index);
            int cell = RandomYardCell(5);
            if (cell >= 0)
                Cafe.PlaceOnCell(pet, cell);
        }
    }

    /// <summary>
    /// FUN_000b64e0 (the cafe loading, with the time away): a pet with no energy left is down for eight hours (K
    /// 0x42cf10); otherwise it is resting if it is short of energy. The time away runs its clock down - back up with a
    /// little energy (K 0x42d3d8) at the end - or brings its energy back at a twenty-fifth of the cafe's rate (K
    /// 0x42d14c), the barrels and the chef helping. One still down lies in front of its habitat.
    /// </summary>
    public void PetCatchUp(CharacterInstance pet, long elapsedMs)
    {
        var k = data.K;
        var c = pet.Character;
        if (pet.IsKnockedOut)
        {
            if (c.Job != Job.Other)
            {
                c.Job = Job.Other;
                c.ReanimateMs = k.Int(0x42cf10);
            }
            c.Energy = 0;
        }
        else if (c.Job != Job.Other)
        {
            c.Job = c.Energy < c.MaxEnergy(data) ? Job.Resting : Job.None;
        }
        else
        {
            c.Energy = 0;
        }
        if (elapsedMs > 0)
        {
            if (c.Job == Job.Other)
            {
                c.ReanimateMs = (int)Math.Max(0, c.ReanimateMs - elapsedMs);
                if (c.ReanimateMs == 0)
                {
                    c.Job = Job.None;
                    c.Energy = Math.Clamp(k.Int(0x42d3d8), 0, c.MaxEnergy(data));
                }
            }
            else if (c.Job is Job.Serving or Job.Cooking or Job.Resting or Job.Attacking && c.Energy < c.MaxEnergy(data))
            {
                int n = (int)Math.Min(int.MaxValue, elapsedMs * k.Float(0x42d14c) * (Cafe.ToxinBarrels + Player.Chef.F48));
                c.Energy = Math.Min(c.MaxEnergy(data), c.Energy + n * k.EnergyDrainPerMs);
            }
        }
        if (c.ReanimateMs > 0 && pet.Home is { } h)
        {
            int door = h.DoorCell(Cafe);
            Cafe.PlaceOnCell(pet, door >= 0 ? door : h.Cell);
            pet.Face(h.OutFacing);
            pet.AddTask(new ReanimateTask(pet, Cafe));
        }
    }

    /// <summary>
    /// FUN_000d81b8 (each frame): the pets. A knocked-out one's clock runs down; the others get their energy back as
    /// fast as resting zombies do and, with nothing to do and not at home, pick something: one time in five a nap at
    /// home, else half the time a trot to a spot in the yard, else a sit or a stand for up to four seconds.
    /// </summary>
    void UpdatePets(int dtMs)
    {
        var k = data.K;
        foreach (var pet in Pets.ToArray())
        {
            pet.Update(dtMs);
            Cafe.Resort(pet);
            var c = pet.Character;
            if (c.Job == Job.Other && c.ReanimateMs > 0)
            {
                // the original lets the clock run below zero and the pet lies there until the cafe is next loaded; the
                // port stops it at zero and has the pet up as the loading would
                c.ReanimateMs = Math.Max(0, c.ReanimateMs - dtMs);
                if (c.ReanimateMs == 0)
                {
                    c.Job = Job.None;
                    c.Energy = Math.Clamp(k.Int(0x42d3d8), 0, c.MaxEnergy(data));
                }
                continue;
            }
            // FUN_0009bba0
            int regen = (int)(dtMs * (Cafe.ToxinBarrels + Player.Chef.F48));
            c.Energy = Math.Min(c.MaxEnergy(data), c.Energy + regen * k.EnergyDrainPerMs);
            if (!pet.IsIdle || pet.Home is not { PetInside: false })
                continue;
            if (Rng.Next(5) == 0)
                PetGoHome(pet);
            else if (Rng.Next(2) == 0)
            {
                int cell = RandomYardCell(5);
                if (cell >= 0)
                    pet.AddTask(new MoveTask(pet, Cafe, cell));
            }
            else
                pet.AddTask(new WaitTask(pet, Rng.Next(5) * 1000, fidget: true));
        }
    }

    /// <summary>FUN_000d8448 (each frame): the habitats - the pet inside naps and blinks, and comes out when it is time.</summary>
    void UpdateHabitats(int dtMs)
    {
        foreach (var h in Habitats.ToList())
        {
            h.Tick(dtMs, Rng);
            if (h.TakeComeOut())
                PetComesOut(h);
        }
    }

    /// <summary>FUN_00195e30: the pet steps out in front of its habitat (AppearTask), the habitat empty again.</summary>
    void PetComesOut(Habitat h)
    {
        if (h.Pet is { } pet)
        {
            int door = h.DoorCell(Cafe);
            Cafe.PlaceOnCell(pet, door >= 0 ? door : h.Cell);
            pet.AddTask(new AppearTask(pet, h.OutFacing));
            Log($"pet {pet.Character.Name} comes out");
        }
        h.LetOut();
    }

    /// <summary>
    /// FUN_000bd26c (the pets' own choice) and FUN_000bc510 (REST on its command bar): the pet trots to the front of
    /// its habitat and goes in - unless it is in already.
    /// </summary>
    public void PetGoHome(CharacterInstance pet)
    {
        if (pet.Home is not { PetInside: false } h || !Cafe.Objects.Contains(h))
            return;
        int door = h.DoorCell(Cafe);
        pet.Interrupt();
        Log($"pet {pet.Character.Name} goes home");
        if (door >= 0)
            pet.AddTask(new MoveTask(pet, Cafe, door));
        pet.AddTask(new DisappearTask(pet, h.InFacing, Cafe));
    }

    /// <summary>FUN_00163a80: a random cell in the yard - outside the room, at most <paramref name="margin"/> cells beyond its open sides.</summary>
    int RandomYardCell(int margin)
    {
        int w = Cafe.RoomWidth, d = Cafe.RoomDepth, a, b;
        do
        {
            a = Rng.Next(w + margin + 1);
            b = Rng.Next(d + margin + 1);
        } while (a <= w && b <= d);
        return Cafe.IndexOf(Cafe.OriginU - 1 - a, Cafe.OriginV + 1 + b);
    }

    // ---------------------------------------------------------------- the fridge (FridgeDialog, FUN_000d5bb8)

    IEnumerable<Furniture> Fridges => Cafe.Objects.OfType<Furniture>().Where(f => f.Def.Category == FurnitureCategory.Fridge);

    /// <summary>
    /// GameStateCafe::load: what a raid brought home goes into the fridge - a quarter of a batch of each dish
    /// (K 0x42d3c4, the batch as the recipe makes it) - and the fridges show their "!" (the tip is due, 0x23).
    /// </summary>
    public void StockFridge()
    {
        float share = data.K.Float(0x42d3c4);
        foreach (var loot in Player.RaidLoot)
        {
            int food = Math.Clamp(loot.Food, 0, data.Foods.Count - 1);
            byte variant = Math.Min(loot.Modifier, (byte)9);
            Player.Fridge.Add(new Dish(food, (int)(data.Foods[food].ServingsWith(variant) * share), variant));
            Hint(0x23, false);
            foreach (var f in Fridges)
                f.Alert = true;
        }
        Player.RaidLoot.Clear();
    }

    /// <summary>FUN_000bd7b0, panel 2: the fridge opens - its "!" goes, and the tip about unlocking is due (0x24).</summary>
    public void FridgeOpened()
    {
        Hint(0x24, true);
        foreach (var f in Fridges)
            f.Alert = false;
    }

    /// <summary>
    /// FUN_00138d10 (the player's own fridge): which buttons a dish gets. UNLOCK trades the dish for its recipe;
    /// a recipe from a cookbook not owned, or a special stove's recipe not yet started on, gets the yellow button
    /// that says why instead (unless a related recipe is already known); a Beeline dish (kind 4) cannot be served
    /// before its recipe is known.
    /// </summary>
    public (bool Serve, bool Unlock, bool Locked) FridgeButtons(Dish item)
    {
        var f = data.Foods[item.Food];
        bool baseKnown = Player.HasRecipe(item.Food, 0), known = Player.HasRecipe(item.Food, item.Modifier);
        bool serve = !(f.Kind == 4 && !known);
        if (!(Player.OwnsCookbook(f.B23) || baseKnown) && f.StoveFamily == 0)
            return (serve, false, true);
        if (f.StoveFamily == 0)
            return (serve, !known, false);
        if (f.Kind != 5)
            return (serve, data.Foods.Any(o => o.StoveFamily == f.StoveFamily && Player.HasRecipe(o.Index, 0)), false);
        return baseKnown ? (serve, !known, false) : (serve, false, true);
    }

    /// <summary>
    /// FUN_000d5bb8, SERVE: the dish leaves the fridge for the counter with the least on it (FUN_001628c0 - one
    /// already topped with the same dish counts a thousand servings emptier), to one of the serving sounds. With
    /// no counter the dish is lost, as in the original.
    /// </summary>
    public void ServeFromFridge(Dish item)
    {
        if (!Player.Fridge.Remove(item))
            return;
        Sound(ServeSounds[Rng.Next(ServeSounds.Length)]);
        ServingCounter? best = null;
        int least = int.MaxValue;
        foreach (var c in Cafe.Objects.OfType<ServingCounter>())
        {
            int load = 0;
            for (int i = 0; i < c.Dishes.Count; i++)
                load += c.Dishes[i].Servings - (i == c.Dishes.Count - 1 && c.Dishes[i].Food == item.Food ? 1000 : 0);
            if (load < least)
            {
                least = load;
                best = c;
            }
            if (least == 0)
                break;
        }
        best?.AddDish(Cafe, item);
        Log($"served {item.DisplayName(data)} x{item.Servings} from the fridge");
    }

    /// <summary>FUN_000d5bb8, UNLOCK: the dish is given up for its recipe (FUN_000d9090), unless that is known already.</summary>
    public bool UnlockFromFridge(Dish item)
    {
        if (!Player.Fridge.Remove(item))
            return false;
        Hint(0x25, false);
        if (item.Food < 0 || item.Food >= data.Foods.Count || item.Modifier >= 10 || Player.HasRecipe(item.Food, item.Modifier))
            return false;
        Player.UnlockRecipe(item.Food, item.Modifier);
        HudBadge?.Invoke(5, true);
        RecipeDiscovered?.Invoke(item.Food, item.Modifier, UnlockMilestone());
        Log($"learned {item.DisplayName(data)} from the fridge");
        return true;
    }

    /// <summary>
    /// RecipeUnlockedDialog's line (FUN_0014df0c): the recipes the player has unlocked - all known, less one more
    /// than the level (those the start and the level-ups gave) - against the milestones (K 0x42d1dc, their XP K
    /// 0x42d1e0). A milestone passed for the first time (game +0x3c0 keeps the most there have been) pays its XP:
    /// "You've earned a N xp bonus for unlocking M recipes!" (strings 473-475); else the next one is named: "Unlock N
    /// more for a M xp bonus!" (476-478). Nothing at the top level.
    /// </summary>
    string UnlockMilestone()
    {
        var k = data.K;
        var goals = k.Ints(0x42d1dc);
        var xps = k.Ints(0x42d1e0);
        int n = Math.Min(k.Int(0x42d1d8), Math.Min(goals.Length, xps.Length));
        int count = Player.Recipes.Count - 1 - Player.Level;
        int reached = -1, next = -1;
        for (int i = 0; i < n; i++)
        {
            if (goals[i] > Player.UnlockedBest && count >= goals[i])
                reached = i;
            else if (next < 0 && count < goals[i])
                next = i;
        }
        Player.UnlockedBest = Math.Max(Player.UnlockedBest, count);
        if (Player.Level >= k.PlayerMaxLevel)
            return "";
        if (reached >= 0)
        {
            Player.Xp += xps[reached];
            Log($"recipe milestone {goals[reached]}: +{xps[reached]} XP");
            return data.Strings[473] + xps[reached] + data.Strings[474] + goals[reached] + data.Strings[475];
        }
        return next >= 0 ? data.Strings[476] + (goals[next] - count) + data.Strings[477] + xps[next] + data.Strings[478] : "";
    }

    // the code's own words for a stove recipe that needs an upgrade (FUN_000d5bb8)
    const uint TextUpgradeAsk = 0x341395, TextUpgradeLevel = 0x3413c5, TextLevelTwo = 0x3413dc, TextLevelThree = 0x3413e1;

    /// <summary>
    /// FUN_000d5bb8, the yellow button: why the recipe cannot be had - "Please purchase" the cookbook (strings 660
    /// + its name + 661), or for a special stove's recipe the stove that teaches it. When that stove is an upgrade
    /// and one of its line already stands in the cafe (FUN_00164644), the words in the code ask for the upgrade:
    /// the stove one step down and "level two", or - for the last of a line (furniture +0x42 clear) - two steps
    /// down and "level three".
    /// </summary>
    public string WhyLocked(Dish item)
    {
        var f = data.Foods[item.Food];
        if (f.Kind != 5)
            return data.Strings[660] + (f.B23 < data.Cookbooks.Count ? data.Cookbooks[f.B23].Name : "") + data.Strings[661];
        if (f.S20 < 0 || f.S20 >= data.Furniture.Count)
            return data.Strings[660] + data.Strings[661];
        var stove = data.Furniture[f.S20];
        bool lineHere = Cafe.Objects.OfType<Furniture>().Any(o => o.Def.StoveFamily == stove.StoveFamily);
        if (stove.S44 == 0 || !lineHere)
            return data.Strings[660] + stove.Name + data.Strings[661];
        int back = stove.B42 ? 1 : 2;
        return data.Exe.Get(TextUpgradeAsk) + data.Furniture[f.S20 - back].Name + data.Exe.Get(TextUpgradeLevel)
            + data.Exe.Get(stove.B42 ? TextLevelTwo : TextLevelThree);
    }

    /// <summary>
    /// FUN_000b878c: the staff's stats - the type's, plus one speed for each Golden Lion's habitat (GameStateCafe
    /// +0x245), and one speed, tip rating, focus and restraint while the Super Server tombstone runs, with half the
    /// energy drain.
    /// </summary>
    public void RefreshStaffStats()
    {
        bool running = Player.Boosts.Running(Boosts.SuperServer);
        int lions = PetCount(PetDef.GoldenLion);
        foreach (var z in Staff)
        {
            var c = z.Character;
            c.StatBonus = running ? 1 : 0;
            c.Speed = c.Def.StatsA[0] + lions + c.StatBonus;
            c.EnergyDrain = running ? 0.5f : 1f;
        }
    }

    /// <summary>TombstoneCommandBar: two vials (K 0x42d6fc) fill a tombstone's uses (FUN_001b65e4).</summary>
    public int RechargeCost => data.K.Int(0x42d6fc);

    public void RechargeTombstone(Furniture f)
    {
        int slot = Boosts.SlotOf(f.Def.Id);
        if (slot < 0 || Player.Toxin < RechargeCost)
            return;
        Player.Boosts.Charges[slot] = Boosts.Table[slot].Charges;
        Player.Toxin -= RechargeCost;
    }

    /// <summary>FUN_001b6664: a timed tombstone (the Super Server) is switched on.</summary>
    public void ActivateTombstone(Furniture f)
    {
        int slot = Boosts.SlotOf(f.Def.Id);
        if (slot < 0 || Player.Boosts.Charges[slot] < 1)
            return;
        Player.Boosts.Use(slot);
        RefreshStaffStats();
    }

    /// <summary>CharacterInstance::vf38: the line under a worker's name - its job, and how long it is still down.</summary>
    public string JobText(CharacterInstance z) => z.Character.Job switch
    {
        Job.Serving => data.Strings[71],
        Job.Cooking => data.Strings[74],
        Job.Resting => data.Strings[72],
        Job.Attacking => data.Strings[73],
        Job.Daydreaming => data.Strings[76],
        Job.Other => data.Strings[75] + (z.CurrentTask is { } t ? "\n" + t.Name : ""),
        _ => "",
    };

    /// <summary>Why a zombie cannot be energized now (null: it can; "" for no message).</summary>
    public string? CannotEnergize(CharacterInstance z)
    {
        if (z.Character.Role is not (Role.Staff or Role.Pet) || z.IsKnockedOut || z.Character.ReanimateMs >= 1)
            return "";
        return Player.Toxin < 1 ? data.Strings[320] : null;
    }

    /// <summary>The question FUN_000bafb8 asks first ("energy is above 80%" when it hardly needs it).</summary>
    public string EnergizeQuestion(CharacterInstance z)
    {
        var c = z.Character;
        string ask = data.Strings[317] + "1" + data.Strings[318];
        return c.Energy / MathF.Max(1f, c.MaxEnergy(data)) > data.K.Float(0x42cf00)
            ? data.Strings[447] + (int)(data.K.Float(0x42cf00) * 100) + "%. " + ask
            : ask;
    }

    /// <summary>
    /// FUN_000bc524: a vial of toxin fills a zombie up at once; it wakes from a daydream, and one resting or
    /// on the attack goes back to serving.
    /// </summary>
    public bool Energize(CharacterInstance z)
    {
        if (CannotEnergize(z) is { } why)
        {
            if (why.Length > 0)
                Message?.Invoke(why);
            return false;
        }
        var c = z.Character;
        c.Energy = c.MaxEnergy(data);
        Player.Toxin -= 1;
        // FUN_000bc524: a pet is simply topped up
        if (z.IsPet)
            return true;
        Sound((z.Def.IsFemale ? 175 : 173) + Rng.Next(2));
        if (Hints.IsShown(0x1d) && !Hints.IsShown(0x20))
        {
            Hints.MarkShown(0x1e);
            Hints.MarkShown(0x1f);
        }
        Hint(0x1a, true);
        Hint(0x37, true);
        if (c.Job == Job.Daydreaming)
        {
            z.Interrupt();
            Hint(0x3d, true);
            return true;
        }
        if (c.Job is Job.Resting or Job.Attacking)
        {
            z.Interrupt();
            c.Job = Job.Serving;
        }
        return true;
    }

    /// <summary>Tapping a daydreaming zombie wakes it (GameStateCafe::update, trigger 0x3d).</summary>
    public void WakeUp(CharacterInstance z)
    {
        if (z.Character.Job != Job.Daydreaming)
            return;
        z.Interrupt();
        Hint(0x3d, true);
    }

    // ---------------------------------------------------------------- customers (FUN_000d7bc0, FUN_000d9744)

    /// <summary>FUN_000b678c: time to the next passer-by, shorter for better rated cafes.</summary>
    void ResetSpawnTimer()
    {
        var k = data.K;
        float rating = Player.EffectiveRating;
        int spread = k.SpawnDelaySpread;
        int r = spread > 0 ? Rng.Next(spread * 2) - spread : 0;
        spawnTimer = (int)(k.SpawnDelayMin + (k.SpawnDelayMax - k.SpawnDelayMin) * rating + r);
    }

    /// <summary>
    /// FUN_001643ac: a customer type allowed at this level and rating, weighted by its spawn weight. A special guest
    /// (+0x50) comes only once a recipe of the stove it follows (+0x52 = the food's +0x20) is known - though no
    /// customer of this version's data is marked special, so the themed ones come like any other.
    /// </summary>
    CharacterDef PickCustomerType()
    {
        var pool = new List<CharacterDef>();
        int total = 0;
        foreach (var d in data.Characters)
        {
            if (!CanVisitNow(d))
                continue;
            pool.Add(d);
            total += d.B04;
        }
        if (pool.Count == 0)
            return data.Characters[0];
        int r = total > 0 ? Rng.Next(total) : 0;
        CharacterDef pick;
        int i = 0;
        do
        {
            pick = pool[i++];
            r -= pick.B04;
        }
        while (r > 0 && i < pool.Count);
        return pick;
    }

    /// <summary>FUN_001643ac's test: a customer type that can come at the cafe's level and rating.</summary>
    public bool CanVisitNow(CharacterDef d) =>
        d.CanBeHuman && d.B20 <= Player.Level && d.F00 <= Player.EffectiveRating
        && !(d.B50 && !data.Foods.Any(f => f.Index >= 0x78 && f.S20 == d.S52 && Player.KnowsRecipe(f.Index)));

    /// <summary>Every type of customer there is (human, with a spawn weight; the raid bosses left out), by level.</summary>
    public List<CharacterDef> CustomerTypes() => data.Characters
        .Where(d => d.CanBeHuman && d.B04 > 0 && d.B20 != 255)
        .OrderBy(d => d.B20).ThenBy(d => d.F00).ThenBy(d => d.Index)
        .ToList();

    /// <summary>
    /// FUN_000d9744: someone walks down the street; maybe they come in. <paramref name="type"/>: who (the cheat
    /// menu's Send customer), else a type picked as the original does.
    /// </summary>
    public CharacterInstance SpawnCustomer(bool force, CharacterDef? type = null)
    {
        ResetSpawnTimer();
        var def = type ?? PickCustomerType();
        var ch = new Character(def, Role.Customer, false, Character.RandomName(data, def.IsFemale, Rng));
        bool fromA = (Rng.Next() & 1) != 0;
        var c = Cafe.AddCharacter(ch, fromA ? Cafe.StreetEndA : Cafe.StreetEndB);
        var k = data.K;
        int chance = (int)(k.EnterChanceMin + (k.EnterChanceMax - k.EnterChanceMin) * Player.EffectiveRating);
        bool enter = (force || Rng.Next(256) <= chance) && Cafe.IsOpen;
        c.Entering = enter;
        if (enter)
        {
            int u0 = k.PlayerOriginU, v0 = k.PlayerOriginV, door = k.DoorOffset;
            c.AddTask(new MoveTask(c, Cafe, Cafe.IndexOf(u0, door + v0 + 1)));
            c.AddTask(new MoveTask(c, Cafe, Cafe.IndexOf(u0 - 1, door + v0 + 1)));
            int spread = k.DoorPauseSpread;
            c.AddTask(new WaitTask(c, k.DoorPause + (spread > 0 ? Rng.Next(spread * 2) - spread : 0)));
            var eat = new EatTask(c, Cafe);
            eat.Finished += OnCustomerFinished;
            c.AddTask(eat);
        }
        else
        {
            c.AddTask(new MoveTask(c, Cafe, fromA ? Cafe.StreetEndB : Cafe.StreetEndA));
        }
        Customers.Add(c);
        Log($"spawn {ch.Name} ({def.Name}) {(enter ? "comes in" : "walks by")}");
        return c;
    }

    void RemoveCustomer(CharacterInstance c)
    {
        Customers.Remove(c);
        Cafe.Waiting.Remove(c);
        Cafe.Unreachable.Remove(c);
        c.DropTasks();
        if (Selected == c)
            Selected = null;
        Cafe.Remove(c);
    }

    void UpdateCustomers(int dtMs)
    {
        bool noneEntering = true;
        foreach (var b in bodies.ToArray())
        {
            b.Update(dtMs);
            if (b.Faded)
            {
                bodies.Remove(b);
                Cafe.Remove(b);
            }
        }
        foreach (var c in Customers.ToArray())
        {
            c.Update(dtMs);
            if (c.IsIdle && !c.IsKnockedOut)
            {
                if (c.Interacting is CharacterInstance attacker)
                {
                    // under attack: scream, and stay put while the attacker is still at it
                    c.Scream();
                    if (!(Cafe.AreNeighbours(c.Cell, attacker.Cell) || c.Cell == attacker.Cell) || attacker.Interacting != c)
                        c.Interacting = null;
                }
                else if (c.Cell != Cafe.StreetEndA && c.Cell != Cafe.StreetEndB)
                {
                    // done here: walk (or, frightened, run) off to either end of the street
                    var m = new MoveTask(c, Cafe, (Rng.Next() & 1) == 0 ? Cafe.StreetEndB : Cafe.StreetEndA,
                        c.Fleeing ? CharAnim.Run : null);
                    c.AddTask(m);
                    if (m.PlanNow())
                    {
                        c.Scared = false;
                        c.Fleeing = false;
                        c.Entering = false;
                    }
                    else if (!c.Character.IsZombie)
                    {
                        // shut in: run around the room screaming
                        c.DropTasks();
                        c.AddTask(new MoveTask(c, Cafe, RandomRoomCell(), CharAnim.Run));
                        c.Scream();
                    }
                }
                else
                {
                    RemoveCustomer(c);
                    continue;
                }
            }
            Cafe.Resort(c);
            if (c.Entering)
                noneEntering = false;
        }
        // nobody on the way in: someone comes at once (not while the tutorial runs the door)
        if (noneEntering && Cafe.IsOpen && Player.Hints.TutorialDone)
        {
            SpawnCustomer(true);
            return;
        }
        spawnTimer -= dtMs;
        if (spawnTimer < 0)
            SpawnCustomer(false);
    }

    /// <summary>
    /// FUN_000b6814 cases 3 and 0x16: the tutorial's customer walks in, sits until served (it never gives up)
    /// and eats quickly; afterwards the cafe shuts again, so it stays inside to be infected.
    /// </summary>
    public CharacterInstance SpawnTutorialCustomer()
    {
        var k = data.K;
        var def = PickCustomerType();
        var ch = new Character(def, Role.Customer, false, Character.RandomName(data, def.IsFemale, Rng));
        var c = Cafe.AddCharacter(ch, Cafe.IndexOf(k.PlayerOriginU, k.PlayerOriginV + k.DoorOffset));
        int door = Cafe.DoorCell;
        var walkIn = new MoveTask(c, Cafe, door);
        c.AddTask(walkIn);
        walkIn.PlanNow();
        int spread = k.DoorPauseSpread;
        c.AddTask(new WaitTask(c, k.DoorPause + (spread > 0 ? Rng.Next(spread * 2) - spread : 0)));
        var eat = new EatTask(c, Cafe, raider: true, eatTime: k.Int(0x42d158));
        // FUN_000bf15c
        eat.Finished += t =>
        {
            Cafe.SetOpen(false);
            OnCustomerFinished(t);
        };
        c.AddTask(eat);
        c.AddTask(new MoveTask(c, Cafe, door));
        c.AddTask(new WaitTask(c, 3000));
        Customers.Add(c);
        TutorialCustomer = c;
        Log($"tutorial customer {ch.Name} ({def.Name}) comes in");
        return c;
    }

    /// <summary>
    /// The customer the tutorial wants infected. Customers are not saved, so after loading a game saved in the
    /// middle of that step any customer will do.
    /// </summary>
    bool IsTutorialTarget(CharacterInstance c) =>
        !c.Character.IsZombie && (c == TutorialCustomer
            || (TutorialCustomer == null && c.Character.Role == Role.Customer && Hints.IsShown(8) && !Hints.IsShown(9)));

    /// <summary>FUN_000da4c8: the customer's visit ended; they pay (or the rating suffers).</summary>
    void OnCustomerFinished(CafeTask task)
    {
        var eat = (EatTask)task;
        var c = eat.Owner;
        var k = data.K;
        if (c.Character.IsZombie)
            return;
        Log($"{c.Character.Name} leaves: {(eat.Success ? "fed" : "hungry")}");
        if (!eat.Success)
        {
            if (!eat.Cancelled && (Cafe.IsOpen || Cafe.IsInside(c.Cell)))
            {
                Cafe.AddNote(new StatusNote(Cafe, c, StatusNote.Kind.Angry, "", k.NoteTime));
                Player.Rating -= k.RatingLoss;
            }
        }
        else if (eat.Table?.Attached is FoodItem food && food.Dish != null)
        {
            if (!eat.Cancelled)
            {
                var def = data.Foods[food.Dish.Food];
                int price = def.PriceWith(food.Dish.Modifier);
                float tipF = price * 0.2f * food.ServerTipRating / 10f;
                int tip = (int)tipF;
                if (Rng.Next(100) < (tipF - tip) * 100f)
                    tip++;
                // FUN_000da4c8: a dollar more for each Midnight Panther on a level's gift dish, each Rottweiler on the rest
                int petBonus = def.Kind == 1 ? PetCount(PetDef.MidnightPanther)
                    : (def.Kind & 0xfd) == 0 ? PetCount(PetDef.Rottweiler) : 0;
                int extra = petBonus + tip * Player.Chef.I44;
                Player.Money += extra + price;
                Log($"  paid ${price} + {extra} tip for {def.Name}; money {Player.Money}");
                // FUN_000da4c8: a customer served, for the quests
                if (Hints.IsShown(0x53))
                    Player.Objectives.Served(1);
                var note = new StatusNote(Cafe, eat.Table, StatusNote.Kind.Money, price.ToString(), k.NoteTime);
                if (extra != 0)
                {
                    note.Extra = " +" + extra + " " + data.Strings[327];
                    note.ExtraColor = petBonus != 0 ? new Color(0, 0xff, 0, 0xff) : Color.White;
                    Sound(0xb7);
                }
                else
                {
                    Sound(9);
                }
                Cafe.AddNote(note);
                Cafe.AddNote(new StatusNote(Cafe, c, StatusNote.Kind.Happy, "", k.NoteTime));
                Player.Rating += k.RatingGain;
                if (Player.Rating >= k.RatingHintThreshold)
                    Hint(0x3a, true);
            }
            else if (Cafe.FindFurniture(FurnitureCategory.Counter, -1).FirstOrDefault() is ServingCounter counter)
            {
                counter.AddDish(Cafe, new Dish(food.Dish.Food, 1, food.Dish.Modifier));
            }
        }
        Player.Rating = Math.Clamp(Player.Rating, 0f, 1f);
    }

    // ---------------------------------------------------------------- cooking (FUN_000d9ce4, FUN_000da190)

    /// <summary>FUN_000da190: cooking experience (a third when the pot goes on, the rest at the counter).</summary>
    void AwardCookXp(Dish dish, CharacterInstance by, bool atStart)
    {
        var k = data.K;
        if (Player.Level == k.PlayerMaxLevel)
            return;
        var food = data.Foods[dish.Food];
        var byLevel = k.CookXpByLevel;
        float baseXp = food.XpWith(dish.Modifier) * byLevel[Math.Clamp(Player.Level - 1, 0, byLevel.Length - 1)];
        float bonus = Player.Chef.F4C * Cafe.BurnMode switch { 3 => 1.35f, 2 => 1.25f, 1 => 1.1f, _ => 1f };
        // FUN_000da190: 5% more for each Sphinx on Egyptian dishes (cookbook 14), Fiery Hound on 16, Cheshire Cat on
        // 17 - an ordinary dish's own cookbook, a special one's by its stove (DAT_0042cd08)
        int book = (food.Kind | 2) == 2 ? food.Cookbook : StoveCookbooks[Math.Clamp(food.StoveFamily, 0, StoveCookbooks.Length - 1)];
        int pets = book switch
        {
            14 => PetCount(PetDef.Sphinx),
            16 => PetCount(PetDef.FieryHound),
            17 => PetCount(PetDef.CheshireCat),
            _ => -1,
        };
        if (pets >= 0)
            bonus = (float)(bonus * (pets * 0.05 + 1.0));
        // FUN_000da190: the Super Server tombstone in the cafe makes cooking teach a tenth more
        if (Player.Boosts.Placed(Boosts.SuperServer))
            bonus *= 1.1f;
        int xp = (int)MathF.Round(baseXp * bonus * (atStart ? 1f / 3f : 2f / 3f));
        Player.Xp += xp;
        if (xp > 0)
        {
            var color = bonus > 1f ? new Color(0, 0xff, 0, 0xff) : Color.White;
            Cafe.AddNote(new StatusNote(Cafe, by, StatusNote.Kind.Xp, xp.ToString(), k.NoteTime, color));
        }
    }

    /// <summary>FUN_000d9ce4: pay for a recipe and put it on the stove. False when the money is short.</summary>
    public bool CookRecipe(Stove stove, int food, byte modifier = 0)
    {
        var def = data.Foods[food];
        int cost = def.CostWith(modifier);
        if (Player.Money < cost || stove.Busy || stove.Burnt)
            return false;
        Player.Money -= cost;
        var dish = Dish.Cook(data, food, modifier);
        AwardCookXp(dish, ChefActor, true);
        stove.StartCooking(Cafe, food, modifier, data.PotDef(stove.Def.StoveFamily));
        Log($"cooking {def.Name} for ${cost}");
        return true;
    }

    /// <summary>FUN_000bf184: the best character to fetch a finished dish (the stove's own cook first).</summary>
    CharacterInstance? FindCarrier(Stove stove)
    {
        var counters = Cafe.FindFurniture(FurnitureCategory.Counter, stove.Cell);
        bool CanDoIt(CharacterInstance w) =>
            CanReach(w, stove.Cell) && counters.Any(c => CanReach(w, c.Cell));
        var all = Workers().OrderBy(w => DiamondDistance(w.Cell, stove.Cell)).ToList();
        if (stove.Operator != null && all.Contains(stove.Operator) && CanDoIt(stove.Operator))
            return stove.Operator;
        foreach (var w in all)
            if (w.Character.Job == Job.None && CanDoIt(w))
                return w;
        foreach (var w in all)
            if (w.Character.Job == Job.Serving && CanDoIt(w))
                return w;
        return null;
    }

    int DiamondDistance(int a, int b)
    {
        var (ua, va) = Cafe.Diamond(a);
        var (ub, vb) = Cafe.Diamond(b);
        return Math.Abs(ua - ub) + Math.Abs(va - vb);
    }

    /// <summary>
    /// Between moving the tutorial's first dish (hint 5) and "cook another dish" (hint 0x21) the stoves wait - in
    /// the stretch where the tutorial wants a table and a chair bought (tip 0x10 shown, its purchases 0x11 / 0x12
    /// not both made yet), and while the first customer is served and infected.
    /// </summary>
    public bool StovesHeld => Hints.IsShown(5) && !Hints.IsShown(0x21);

    /// <summary>The tutorial is waiting for its table and chair to be bought (tip 0x10 shown, 0x11 / 0x12 not both).</summary>
    public bool WaitingForTableAndChair => Hints.IsShown(0x10) && !(Hints.IsShown(0x11) && Hints.IsShown(0x12));

    /// <summary>
    /// The player tapped a stove (GameStateCafe::update). An empty stove opens the recipe list (true is
    /// returned). A cook picked goes to the stove: to cook (waiting there for the recipe if it is empty; the
    /// cafe stands still while the list is open), to fetch the finished dish or to scrub the burnt pot. With
    /// nobody picked a finished dish is fetched by whoever is free. Between moving the tutorial's first dish
    /// (hint 5) and "cook another dish" (hint 0x21) the stoves wait.
    /// </summary>
    public bool TapStove(Stove stove)
    {
        bool held = StovesHeld;
        bool empty = !stove.Busy && !stove.Burnt;
        bool openList = empty && !held;
        var w = Selected;
        if (stove.Operator == null && w != null && (w == ChefActor || w.Character.Role == Role.Staff)
            && w.Character.ReanimateMs < 1 && !held)
        {
            Hint(2, false);
            if (stove.IsBurnt(Cafe))
            {
                w.Interrupt();
                w.AddTask(new CleanupTask(w, Cafe, stove));
            }
            else if (stove.IsDone)
            {
                w.Interrupt();
                w.AddTask(new DishToCounterTask(w, Cafe, stove));
            }
            else if (!w.Tasks.Any(t => t is CookTask ct && ct.Stove == stove))
            {
                w.Interrupt();
                var cook = new CookTask(w, stove, Cafe);
                // the tutorial's first dish: "the food is ready" when it is done (FUN_000d57a4)
                if (!Hints.IsShown(3))
                    cook.Finished += t =>
                    {
                        if (t.Success)
                            Hint(10, false);
                    };
                w.AddTask(cook);
                w.Character.Job = Job.Cooking;
            }
            return openList;
        }
        if (w == null && stove.IsDone)
        {
            // FUN_000b45c4, the stove bar's "serve" button (which also stands in for the tutorial's hint 4)
            var carrier = FindCarrier(stove);
            if (carrier == null)
            {
                Message?.Invoke(data.Strings[433]);
                return false;
            }
            Hints.MarkShown(4);
            carrier.Interrupt();
            Log($"{carrier.Character.Name} carries the dish to a counter");
            carrier.AddTask(new DishToCounterTask(carrier, Cafe, stove));
        }
        return openList;
    }

    /// <summary>FUN_000be78c with "cancel": the recipe list was shut without cooking; the cook stops.</summary>
    public void RecipeCancelled()
    {
        var w = Selected;
        if (w != null && w.Character.Role != Role.Customer && !w.IsKnockedOut)
        {
            w.Interrupt();
            w.Character.Job = Job.None;
        }
    }

    /// <summary>FUN_000be78c: a recipe was picked with nobody to cook it ("assign a cook", hint 0x52).</summary>
    public void RecipeChosen()
    {
        var w = Selected;
        if (w == null || w.Character.Role == Role.Customer || w.IsKnockedOut)
            Hint(0x53, false);
    }

    /// <summary>
    /// GameStateCafe::update, a serving counter, table or sink tapped with a cook or zombie picked: they go
    /// back to serving (the tutorial uses this to make the chef serve the first customer).
    /// </summary>
    public bool TapServingArea(Furniture f)
    {
        var w = Selected;
        if (w == null)
            return false;
        if (w != ChefActor && w.Character.Role != Role.Staff)
            return false;
        if (w.Character.ReanimateMs >= 1)
            return false;
        w.Interrupt();
        w.Character.Job = Job.Serving;
        Hint(3, false);
        return true;
    }

    /// <summary>
    /// GameStateCafe::update, the doorway tapped: after the tutorial it opens and shuts the cafe; during it,
    /// only the step that says so ("it's time to open your cafe", hint 0x51) may open it.
    /// </summary>
    public void TapDoor()
    {
        if (Hints.TutorialDone)
        {
            Cafe.SetOpen(!Cafe.IsOpen);
        }
        else if (Hints.IsShown(0x51) && !Cafe.IsOpen)
        {
            Cafe.SetOpen(true);
            Hint(4, true);
            MusicCue?.Invoke(3);
        }
    }

    /// <summary>FUN_000b9bdc: send a zombie out back to rest.</summary>
    public bool Rest(CharacterInstance z)
    {
        if (z.Character.ReanimateMs >= 1 || z.Character.Role != Role.Staff)
            return false;
        z.Interrupt();
        z.AddTask(new RestTask(z, Cafe));
        Hint(8, true);
        Hint(0x1c, true);
        return true;
    }

    /// <summary>FUN_000b9d14: back to work: walk in through the door and serve.</summary>
    public void Work(CharacterInstance z)
    {
        z.Interrupt();
        var k = data.K;
        z.AddTask(new MoveTask(z, Cafe, Cafe.IndexOf(k.PlayerOriginU - 1, k.DoorOffset + k.PlayerOriginV + 1)));
        z.Character.Job = Job.Serving;
    }

    // ---------------------------------------------------------------- getting toxin (Two Point extras)

    /// <summary>Toxin Generators in the cafe and in the storage box.</summary>
    public int ToxinGenerators() =>
        Cafe.FindFurniture(FurnitureCategory.ToxinGenerator, -1).Count
        + Cafe.Stored.Count(id => data.Furniture[id].Category == FurnitureCategory.ToxinGenerator);

    /// <summary>Vials the toxin exchange will still sell today (int.MaxValue: no limit).</summary>
    public int ToxinLeftToday
    {
        get
        {
            int limit = Settings.Current.ToxinDailyLimit;
            if (limit <= 0)
                return int.MaxValue;
            return Player.ToxinBoughtDay.Date == GameClock.Today ? Math.Max(0, limit - Player.ToxinBoughtToday) : limit;
        }
    }

    /// <summary>The toxin exchange: <paramref name="n"/> vials for cash, within today's limit. Why not, or null.</summary>
    public string? BuyToxin(int n)
    {
        int price = Settings.Current.ToxinPrice;
        if (n < 1)
            return "";
        if (n > ToxinLeftToday)
            return "That's all the toxin for today - come back tomorrow.";
        if ((long)price * n > Player.Money)
            return data.Strings[441];
        if (Player.ToxinBoughtDay.Date != GameClock.Today)
        {
            Player.ToxinBoughtDay = GameClock.Today;
            Player.ToxinBoughtToday = 0;
        }
        Player.Money -= price * n;
        Player.Toxin += n;
        Player.ToxinBoughtToday += n;
        // it counts towards a spending quest like any purchase
        if (Hints.IsShown(0x53))
            Player.Objectives.Spent(price * n);
        Sound(8);
        return null;
    }

    // ---------------------------------------------------------------- the staff panel (Two Point extras)

    int autoBreakClock;
    /// <summary>Auto break: rest from this share of full energy, back to work when full.</summary>
    const float AutoBreakAt = 0.25f;

    /// <summary>The staff panel's SERVE: back on serving duty (a resting zombie walks back in first).</summary>
    public void AssignServing(CharacterInstance w)
    {
        if (w.IsKnockedOut || w.Character.ReanimateMs >= 1)
            return;
        if (w.Character.Job == Job.Resting && w.Character.Role == Role.Staff)
        {
            Work(w);
            return;
        }
        w.Interrupt();
        w.Character.Job = Job.Serving;
        Hint(3, false);
    }

    /// <summary>
    /// The staff panel's COOK: off to the nearest stove that needs someone - a burnt pot to scrub, a finished dish to
    /// carry, or cooking with nobody at it - as if the stove were clicked with them picked. Why not, or null.
    /// </summary>
    public string? SendToStove(CharacterInstance w)
    {
        if (w.IsKnockedOut || w.Character.ReanimateMs >= 1)
            return "";
        if (StovesHeld)
            return data.Strings[16];
        var stoves = Cafe.FindFurniture(FurnitureCategory.Stove, w.Cell).OfType<Stove>()
            .Where(s => s.Operator == null && CanReach(w, s.Cell)).ToList();
        var stove = stoves.FirstOrDefault(s => s.IsBurnt(Cafe))
            ?? stoves.FirstOrDefault(s => s.IsDone)
            ?? stoves.FirstOrDefault(s => s.Busy && s.AssignedCooks == 0);
        if (stove == null)
            return "No stove needs a cook right now - start a recipe first.";
        var picked = Selected;
        Selected = w;
        TapStove(stove);
        Selected = picked;
        return null;
    }

    /// <summary>Once a second: zombies on auto break rest when low and go back to serving when full.</summary>
    void UpdateAutoBreaks(int dtMs)
    {
        autoBreakClock += dtMs;
        if (autoBreakClock < 1000)
            return;
        autoBreakClock = 0;
        foreach (var z in Staff.ToList())
        {
            var c = z.Character;
            if (!c.AutoBreak || z.IsKnockedOut || c.ReanimateMs >= 1)
                continue;
            float max = c.MaxEnergy(data);
            if (c.Job is Job.Serving or Job.None && z.IsIdle && c.Energy <= max * AutoBreakAt && Rest(z))
                c.OnBreak = true;
            else if (c.OnBreak && c.Job == Job.Resting && c.Energy >= max)
                Work(z);
            if (c.Job != Job.Resting)
                c.OnBreak = false;
        }
    }

    // ---------------------------------------------------------------- staff

    /// <summary>How many zombies the player may employ at their level (DAT_0042d5b4).</summary>
    // ---------------------------------------------------------------- Frequent Fryer (FUN_000d9bc4, FUN_00172cd0)

    /// <summary>A day of the Frequent Fryer: the days in a row, and the cash, toxin or dish (-1: none) it gave.</summary>
    public sealed record DailyBonus(int Days, int Money, int Toxin, int Food);

    /// <summary>
    /// FUN_000d9bc4, as the cafe loads: back the day after the last one played, the days in a row go up and the day's
    /// reward is given (FUN_00172cd0); back after a longer gap (or with the clock turned back) the count starts again.
    /// The first visit only notes the day. The original takes the date from its server, the port from the computer's
    /// clock. Null: nothing to give today.
    /// </summary>
    public DailyBonus? CheckDailyBonus(DateTime now)
    {
        var today = now.Date;
        var last = Player.LastPlayDay;
        if (last == today)
            return null;
        Player.LastPlayDay = today;
        if (last == null)
            return null;
        if ((today - last.Value).Days != 1)
        {
            Player.StreakDays = 0;
            return null;
        }
        Player.StreakDays++;
        return GiveDailyBonus(Player.StreakDays);
    }

    /// <summary>Which of the ten rewards a run of days earns: the ten in turn, then days 3..10 again (K 0x42d010).</summary>
    public int BonusDay(int days)
    {
        while (days > 10)
            days += data.K.Int(0x42d010) - 11;
        return days;
    }

    /// <summary>What a reward day gives, without giving it: cash, toxin, or a dish (true) for the fridge.</summary>
    public (int Money, int Toxin, bool Dish) BonusFor(int day)
    {
        var k = data.K;
        return day switch
        {
            1 => (k.Int(0x42cfd8), 0, false),
            2 => (k.Int(0x42cfdc), 0, false),
            5 => (0, k.Int(0x42cff0), false),
            6 => (k.Int(0x42cff4), 0, false),
            7 => (k.Int(0x42cff8), 0, false),
            10 => (0, k.Int(0x42d00c), false),
            _ => (0, 0, true),
        };
    }

    /// <summary>
    /// FUN_00172cd0: the reward for the days in a row - $25, $50, a dish, a dish, a vial of toxin, $250, $350, a dish,
    /// a dish and three vials (constants 0x42cfd8..0x42d00c).
    /// </summary>
    DailyBonus GiveDailyBonus(int days)
    {
        int day = BonusDay(days);
        var (money, toxin, _) = BonusFor(day);
        int food = day switch
        {
            3 => BonusDish(0x42cfe4, 0x42cfe0, true),
            4 => BonusDish(0x42cfec, 0x42cfe8, false),
            8 => BonusDish(0x42d000, 0x42cffc, false),
            9 => BonusDish(0x42d008, 0x42d004, false),
            _ => -1,
        };
        Player.Money += money;
        Player.Toxin += toxin;
        Log($"Frequent Fryer: {days} days in a row, ${money}, {toxin} toxin" + (food >= 0 ? $", {data.Foods[food].Name}" : ""));
        return new DailyBonus(days, money, toxin, food);
    }

    /// <summary>
    /// A dish of the day's list (its length at <paramref name="countAt"/>) into the fridge - or, K 0x42d01c times in
    /// a hundred, the special one (K 0x42d018; on day 3 only from its level).
    /// </summary>
    int BonusDish(int listAt, int countAt, bool fromLevel)
    {
        var k = data.K;
        int count = Math.Min(k.Int(countAt), k.Ints(listAt).Length);
        if (count <= 0)
            return -1;
        int food = -1;
        if (Rng.Next(100) < k.Int(0x42d01c))
        {
            var specials = k.Ints(0x42d018).Take(k.Int(0x42d014))
                .Where(f => f >= 0 && f < data.Foods.Count && (!fromLevel || data.Foods[f].Level <= Player.Level)).ToList();
            if (specials.Count > 0)
                food = specials[Rng.Next(specials.Count)];
        }
        if (food < 0)
            food = k.Ints(listAt)[Rng.Next(count)];
        Player.Fridge.Add(Dish.Cook(data, food, 0));
        return food;
    }

    // ---------------------------------------------------------------- the magic fridges

    /// <summary>The Magic Fridge and the Krypton Fridge (K 0x42d6b4 / 0x42d6b8), which give a free dish a day.</summary>
    public bool IsMagicFridge(Furniture f) => f.Def.Id == data.K.Int(0x42d6b4) || f.Def.Id == data.K.Int(0x42d6b8);

    /// <summary>
    /// GameStateCafe::update, a magic fridge tapped on a later day than its last gift: today's free dish (FUN_000d57bc)
    /// - a batch of one of the recipes the player knows, the nearer its level to the player's the likelier (weight =
    /// its level x the larger of 1 and K 0x42d6bc less the gap), into the fridge. Null: not today.
    /// </summary>
    public Dish? MagicFridgeGift(Furniture fridge, DateTime now)
    {
        if (!IsMagicFridge(fridge) || fridge.GiftDay is { } last && (now.Date - last.Date).Days < 1)
            return null;
        fridge.GiftDay = now.Date;
        var known = new List<(int Food, int Variant, int Upto)>();
        int total = 0;
        foreach (var f in data.Foods)
            for (int v = 0; v < 10; v++)
                if (Player.HasRecipe(f.Index, v))
                {
                    total += Math.Max(1, data.K.Int(0x42d6bc) - Math.Abs(Player.Level - f.Level)) * f.Level;
                    known.Add((f.Index, v, total));
                }
        if (known.Count == 0)
            return null;
        int r = total > 0 ? Rng.Next(total) : 0;
        var pick = known.FirstOrDefault(k => r < k.Upto, known[0]);
        var dish = Dish.Cook(data, pick.Food, (byte)pick.Variant);
        Player.Fridge.Add(dish);
        Log($"the {fridge.Def.Name} gives {dish.DisplayName(data)}");
        return dish;
    }

    // ---------------------------------------------------------------- the special stoves

    /// <summary>
    /// FUN_001d2ce0: what a special stove can become - the first stove from def 385 on that names it as the one it
    /// improves on (furniture +0x44), once the player has reached its level; null when there is none.
    /// </summary>
    public FurnitureDef? StoveUpgrade(FurnitureDef stove) =>
        data.Furniture.Skip(0x181).FirstOrDefault(d => d.S44 == stove.Id && d.Theme <= Player.Level);

    /// <summary>
    /// FUN_000b4a78, then FUN_000c0ef4 with the stove at +0x64: the upgrade paid for like anything from the store and
    /// stood on the old stove's cell, facing the same way, in its place - with the recipes it brings. A cook waiting
    /// at the old stove for a recipe stops. Null when it cannot be bought.
    /// </summary>
    public Stove? UpgradeStove(Stove old, FurnitureDef up)
    {
        if (old.Busy || old.Burnt || old.Cell < 0 || !Buy(up))
            return null;
        foreach (var w in Workers())
            if (w.Tasks.Any(t => t is CookTask ct && ct.Stove == old))
            {
                w.Interrupt();
                w.Character.Job = Job.None;
            }
        int cell = old.Cell, facing = old.Orientation;
        Cafe.RemoveFurniture(old);
        var stove = (Stove)Furniture.Create(data, up);
        if (facing != 0)
            stove.SetOrientation(facing);
        Cafe.PlaceFurniture(stove, cell);
        Sound(90);
        Log($"{old.Def.Name} upgraded to {up.Name}");
        StoveRecipes(up);
        return stove;
    }

    /// <summary>
    /// FUN_000c0ef4: a special stove bought brings the recipes made on it (kind 5, food +0x20 naming the stove), each
    /// one announced (FUN_00104c00) and the recipe book's button badged.
    /// </summary>
    public void StoveRecipes(FurnitureDef stove)
    {
        if (stove.Category != FurnitureCategory.Stove)
            return;
        foreach (var f in data.Foods)
            if (f.Index >= 0x78 && f.Kind == 5 && f.S20 == stove.Id && !Player.HasRecipe(f.Index, 0))
                Discover(f.Index);
    }

    /// <summary>
    /// FUN_000b867c (GameStateCafe::load): each special stove standing in the cafe gives the recipes that come with it
    /// if they are not known yet (announced), and quietly those of the stoves it was upgraded from (FUN_00160120).
    /// </summary>
    public void LearnStoveRecipes()
    {
        foreach (var cell in Cafe.Cells)
        {
            if (cell.Slots[2] is not Furniture placed)
                continue;
            var def = placed.Def;
            if (def.B42 || def.S44 > 0)
                foreach (var f in data.Foods)
                    if (f.S20 == def.Id && !Player.KnowsRecipe(f.Index))
                        Discover(f.Index);
            for (int prev = def.S44; prev > 0 && prev < data.Furniture.Count; prev = data.Furniture[prev].S44)
                foreach (var f in data.Foods)
                    if (f.S20 == prev && !Player.KnowsRecipe(f.Index))
                        Player.UnlockRecipe(f.Index, 0);
        }
    }

    void Discover(int food)
    {
        Player.UnlockRecipe(food, 0);
        RecipeDiscovered?.Invoke(food, 0, "");
        HudBadge?.Invoke(5, true);
        Log($"learned {data.Foods[food].Name} with the stove");
    }

    // ---------------------------------------------------------------- the cookbooks

    /// <summary>
    /// The recipe book's cookbooks for a stove family (RecipeBook, FUN_000fdf40): the favourites, then the books of
    /// that family in order - for the ordinary stoves the General, the Raid, the Pirate and the Vampire.
    /// </summary>
    public List<int> BooksFor(int family) =>
        [0, .. data.Cookbooks.Where(c => c.Index > 0 && c.StoveFamily == family).Select(c => c.Index)];

    /// <summary>
    /// A book open to the player: the favourites, the General and the Raid always, a special stove's book with the stove
    /// (its recipe book is only reached through it), the others once bought (cafe +0x108).
    /// </summary>
    public bool HasBook(int book) =>
        book < 3 || book < data.Cookbooks.Count && data.Cookbooks[book].StoveFamily > 0 || Player.Cookbooks.Contains(book);

    /// <summary>FUN_00101af0 / FUN_00101f78 + FUN_001053bc: a cookbook bought, for its cash or its toxin.</summary>
    public bool BuyBook(int book)
    {
        var c = data.Cookbooks[book];
        if (Player.Level < c.S18 || HasBook(book))
            return false;
        if (c.S06 > 0)
        {
            if (Player.Toxin < c.S06)
                return false;
            Player.Toxin -= c.S06;
            Sound(9);
        }
        else
        {
            if (Player.Money < c.Price)
                return false;
            Player.Money -= c.Price;
            Sound(8);
        }
        Player.Cookbooks.Add(book);
        // FUN_00105478: the book's own recipes (kind 6) come with it; the rest unlock as usual
        foreach (var f in data.Foods)
            if (f.Book == book && f.Kind == 6)
                Player.UnlockRecipe(f.Index, 0);
        Log($"cookbook {c.Name} bought");
        return true;
    }

    /// <summary>
    /// The star on a recipe (RecipeBook::vf02, game +0x68): into the favourites (fifteen at most - null: done, else why
    /// not), or out of them - never the last one, nor the last of this stove's while the favourites book is open.
    /// </summary>
    public string? ToggleFavorite(int food, bool favoritesOpen, int family)
    {
        var fav = Player.Favorites;
        if (!fav.Contains(food))
        {
            if (fav.Count >= 15)
                return data.Exe.Get(0x3419a4);
            fav.Add(food);
            return null;
        }
        if (fav.Count < 2 || favoritesOpen && fav.Count(f => data.Foods[f].StoveFamily == family) < 2)
            return data.Exe.Get(0x3419c3);
        fav.Remove(food);
        return null;
    }

    // ---------------------------------------------------------------- the meat locker

    /// <summary>The locker's hooks at most (FUN_000d51b0 / GameStateMeatLocker::update: 0x3c).</summary>
    public const int LockerMax = 60;

    /// <summary>K 0x42d620: five more hooks for this much toxin.</summary>
    public int LockerCost => data.K.Int(0x42d620) is > 0 and var k ? k : 5;

    public bool LockerFull => Player.Frozen.Count >= Player.LockerSize;
    public bool CanExpandLocker => Player.LockerSize < LockerMax;

    /// <summary>
    /// FUN_000d51b0 (MEATLOCKER on a zombie's command bar, from level 9): the zombie goes into the meat locker, off the
    /// staff and out of the cafe, kept as it is but for its job; false with the locker full.
    /// </summary>
    public bool Freeze(CharacterInstance z)
    {
        if (!Staff.Contains(z) || LockerFull)
            return false;
        var c = z.Character;
        c.Job = Job.None;
        z.Interacting = null;
        Staff.Remove(z);
        Player.Frozen.Add(c);
        z.Groan();
        z.ClearTasks();
        Cafe.Remove(z);
        if (Selected == z)
            Selected = null;
        Log($"{c.Name} goes into the meat locker");
        return true;
    }

    /// <summary>FUN_000dde48 / FUN_000e9e08: five more hooks for the toxin (sound 9) - false without the toxin.</summary>
    public bool ExpandLocker()
    {
        Sound(9);
        if (!CanExpandLocker || Player.Toxin < LockerCost)
            return false;
        Player.Toxin -= LockerCost;
        Player.LockerSize += 5;
        return true;
    }

    /// <summary>FUN_000e9b68 (REVIVE in the meat locker): the zombie back on the staff, somewhere in the room.</summary>
    public CharacterInstance? Thaw(Character c)
    {
        if (!Player.Frozen.Remove(c))
            return null;
        c.Role = Role.Staff;
        c.Job = Job.Serving;
        var z = Cafe.AddCharacter(c, RandomRoomCell());
        z.AttackCheck = data.K.Int(0x42d114);
        Staff.Add(z);
        RefreshStaffStats();
        Log($"{c.Name} comes out of the meat locker");
        return z;
    }

    /// <summary>FUN_000e9e90 (FIRE in the meat locker): gone for good - the firing tips as for one on the staff.</summary>
    public void FireFrozen(Character c)
    {
        if (!Player.Frozen.Remove(c))
            return;
        Sound(40);
        var h = Player.Hints;
        if (h.IsShown(0x1e) && !h.IsShown(0x20))
        {
            h.MarkShown(0x1f);
            h.MarkShown(0x20);
        }
        else if (h.IsShown(0x17) && !h.IsShown(0x18))
        {
            h.MarkShown(0x18);
        }
        Log($"{c.Name} is fired from the meat locker");
    }

    public int StaffLimit
    {
        get
        {
            var t = data.K.Ints(0x42d5b4);
            return t.Length == 0 ? 1 : t[Math.Clamp(Player.Level - 1, 0, t.Length - 1)];
        }
    }

    /// <summary>What turning this customer into staff costs (CharacterDef +0x34; +0x38 = paid in toxin).</summary>
    public (int Amount, bool Toxin) InfectCost(CharacterInstance c) => (c.Def.I34, c.Def.B38 != 0);

    /// <summary>Why a customer cannot be infected now (null when they can).</summary>
    public string? CannotInfect(CharacterInstance c)
    {
        if (c.Character.Role != Role.Customer || c.Character.IsZombie || !c.Def.CanBeZombie)
            return "";
        if (Staff.Count >= StaffLimit)
            return data.Strings[409];
        var (cost, toxin) = InfectCost(c);
        if (toxin ? Player.Toxin < cost : Player.Money < cost)
            return toxin ? data.Strings[320] : data.Strings[436];
        return null;
    }

    /// <summary>FUN_000bc788: the chef goes and bites the customer; on success they join the staff.</summary>
    public bool Infect(CharacterInstance victim)
    {
        if (CannotInfect(victim) is { } why)
        {
            if (why.Length > 0)
                Message?.Invoke(why);
            return false;
        }
        ChefActor.Interrupt();
        var task = new InfectTask(ChefActor, victim, Cafe);
        task.Finished += t =>
        {
            if (t.Success)
                Hire(victim);
        };
        ChefActor.AddTask(task);
        Hint(0xe, false);
        Hint(0x4d, true);
        return true;
    }

    /// <summary>FUN_000bd408: pay for the new zombie and put it to work.</summary>
    void Hire(CharacterInstance z)
    {
        var (cost, toxin) = InfectCost(z);
        if (toxin)
        {
            Player.Toxin -= cost;
            Sound(9);
        }
        else if (cost != 0)
        {
            Player.Money -= cost;
            Sound(8);
        }
        Customers.Remove(z);
        Cafe.Waiting.Remove(z);
        Cafe.Unreachable.Remove(z);
        if (Selected == z)
            Selected = null;
        z.ClearTasks();
        z.Entering = false;
        Staff.Add(z);
        var c = z.Character;
        c.IsZombie = true;
        c.Role = Role.Staff;
        c.Job = Job.Serving;
        c.Energy = Math.Clamp(data.K.Int(0x42d3d8), 0, c.MaxEnergy(data));
        z.RefreshParts();
        z.AttackCheck = data.K.Int(0x42d114);
        Player.ZombieKinds.Add(c.Def.Index);
        if (TutorialCustomer == z)
            TutorialCustomer = null;
        Hint(0xf, false);
        // FUN_000bd408: the zombie list shows, its "room for one more" badge goes; the new one's stats (FUN_000b878c)
        HudButtonShown?.Invoke(8);
        HudBadge?.Invoke(8, false);
        RefreshStaffStats();
        if (Hints.IsShown(0x51))
        {
            Hints.MarkShown(0x4d);
            Hints.MarkShown(0x4f);
        }
        Log($"{c.Name} the {c.Def.Name} joins the staff");
    }

    // ---------------------------------------------------------------- store

    /// <summary>Why an item cannot be bought now (null when it can).</summary>
    public string? CannotBuy(FurnitureDef def, int count = 1)
    {
        if (def.Theme > Player.Level)
            return data.Strings[587] + def.Theme;
        // the Toxin Generator (a Two Point extra): only so many, stored ones counted
        int genLimit = Settings.Current.ToxinGeneratorLimit;
        if (def.Category == FurnitureCategory.ToxinGenerator && genLimit > 0 && ToxinGenerators() + count > genLimit)
            return genLimit == 1 ? "You can only have one Toxin Generator." : $"You can only have {genLimit} Toxin Generators.";
        if (def.Currency == 1 ? Player.Toxin < def.Price * count : Player.Money < def.Price * count)
            return def.Currency == 1 ? data.Strings[320].Split('.')[0] + "." : data.Strings[441];
        return null;
    }

    /// <summary>Pays for store items (GameStateCafe edit mode): cash or toxin, plus the item's experience.</summary>
    public bool Buy(FurnitureDef def, int count = 1)
    {
        if (CannotBuy(def, count) != null)
            return false;
        if (def.Currency == 1)
            Player.Toxin -= def.Price * count;
        else
            Player.Money -= def.Price * count;
        // FUN_000c0ef4: what was spent counts towards a quest (a vial as K 0x42d138 dollars)
        if (Hints.IsShown(0x53))
            Player.Objectives.Spent((def.Currency == 1 ? data.K.Int(0x42d138) : 1) * def.Price * count);
        int xp = Player.Level == data.K.PlayerMaxLevel ? 0 : (int)(count * def.F3C);
        if (xp > 0)
        {
            Player.Xp += xp;
            Cafe.AddNote(new StatusNote(Cafe, ChefActor, StatusNote.Kind.Xp, xp.ToString(), data.K.NoteTime));
        }
        Sound(8);
        return true;
    }

    /// <summary>What the store pays back for an item: half its cash value (toxin counts 2000 each).</summary>
    public int SellPrice(FurnitureDef def)
    {
        int value = def.Currency switch { 2 => data.K.Int(0x42d138) * def.Price * 2, 1 => data.K.Int(0x42d138) * def.Price, _ => def.Price };
        return (int)(value * data.K.Float(0x42d13c));
    }

    public void Sell(Furniture f)
    {
        Player.Money += SellPrice(f.Def);
        Cafe.RemoveFurniture(f);
        Sound(8);
    }

    /// <summary>Things a cafe must keep one of (stoves, counters).</summary>
    public bool IsLastOfItsKind(Furniture f) =>
        f.Def.Category is FurnitureCategory.Stove or FurnitureCategory.Counter
        && !Cafe.FindFurniture(f.Def.Category, -1).Any(other => other != f);

    /// <summary>
    /// Undo in decorating mode (a Two Point extra): a purchase taken back - its price returned, and the experience
    /// and quest spending it brought taken off again. When that experience has already gone into a level up it
    /// can't be taken back, so the piece is paid for as if salvaged instead (false).
    /// </summary>
    public bool Refund(FurnitureDef def)
    {
        int xp = Player.Level == data.K.PlayerMaxLevel ? 0 : (int)def.F3C;
        if (Player.Xp < xp)
        {
            Player.Money += SellPrice(def);
            return false;
        }
        Player.Xp -= xp;
        if (def.Currency == 1)
            Player.Toxin += def.Price;
        else
            Player.Money += def.Price;
        if (Hints.IsShown(0x53))
            Player.Objectives.Spent(-(def.Currency == 1 ? data.K.Int(0x42d138) : 1) * def.Price);
        return true;
    }

}
