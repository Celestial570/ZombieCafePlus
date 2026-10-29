using Raylib_cs;
using ZombieCafe.Data;
using ZombieCafe.Game.Cafe.Tasks;
using ZombieCafe.Gfx;

namespace ZombieCafe.Game.Cafe;

/// <summary>
/// A person or zombie in the cafe (original CharacterInstance, a CafeTaskObject, 0xb14 bytes): the animated
/// body of a <see cref="Game.Character"/> plus its queue of tasks.
/// </summary>
public sealed class CharacterInstance : CafeObject
{
    public Cafe Cafe { get; }
    public GameData Data { get; }
    /// <summary>+0x11c.</summary>
    public Character Character { get; }
    public CharacterDef Def => Character.Def;
    public bool IsZombie => Character.IsZombie;
    /// <summary>A pet (the original's PetInstance, 0x524 bytes, FUN_0009c1c0): the same walking body with the pets'
    /// animations (DAT_0042cd14), its own sounds, and a home (<see cref="Habitat"/>).</summary>
    public bool IsPet => Character.IsPet;
    /// <summary>PetInstance +0x11c: the pet's habitat.</summary>
    public Habitat? Home { get; set; }
    /// <summary>The zombie yeti (YetiInstance, 0x22c bytes, FUN_00193fa0).</summary>
    public bool IsYeti => Def.IsYeti;
    /// <summary>YetiInstance +0x114: in its yeti form (true) or spinning as a tornado (false).</summary>
    public bool YetiForm { get; private set; }
    /// <summary>YetiInstance +0x228 / VoltronInstance +0x324: ms until the yeti goes, or the Amptron comes apart.</summary>
    public int LifeMs { get; set; }
    /// <summary>
    /// +0x94: takes part in what goes on - found by those looking for someone to fight (FUN_00162408) and run by the
    /// raid. The robot pets sit out the fight folded into the Amptron; the Amptron counts its time only once it stands.
    /// </summary>
    public bool Active { get; set; } = true;
    /// <summary>+0xb12: a raided restaurant's chef (the Amptron's special takes only half its energy).</summary>
    public bool IsRivalChef { get; set; }
    /// <summary>The Amptron (VoltronInstance, 0x338 bytes, FUN_00189a9c): the five robot pets combined.</summary>
    public bool IsAmptron => Def.IsAmptron;
    /// <summary>The Big Bad Wolf (BigBadWolfInstance, 0x2ec bytes, FUN_0002b9e4) a fairy-tale zombie turns into.</summary>
    public bool IsWolf => Def.IsWolf;
    /// <summary>
    /// The Amptron or the Big Bad Wolf: their own animations, a clock that runs while they stand, and tasks dropped
    /// without the job going with them (their vf42 / vf43).
    /// </summary>
    public bool IsSummon => IsAmptron || IsWolf;
    /// <summary>BigBadWolfInstance +0x2e8: the zombie the wolf was.</summary>
    public CharacterInstance? Zombie { get; set; }
    /// <summary>VoltronInstance +0x334: special attacks left (three).</summary>
    public int SpecialsLeft { get; set; } = 3;
    // +0x330: the stomp of its walk, a second apart
    int stepMs;

    /// <summary>The Amptron's animations (DAT_0042d764).</summary>
    public const int AmptronAttack = 0, AmptronSpecial = 1, AmptronWalk = 2, AmptronIdle = 3, AmptronAppear = 4, AmptronDisappear = 5;
    static readonly string[] AmptronFiles = ["voltron_attackReg", "voltron_attackSpe", "voltron_walk", "voltron_idle", "voltron_appear", "voltron_disappear"];
    /// <summary>The Big Bad Wolf's animations (DAT_0042c5e8).</summary>
    public const int WolfAttack = 0, WolfWalk = 1, WolfIdle = 2, WolfAppear = 3, WolfDisappear = 4;
    static readonly string[] WolfFiles = ["bigBadWolf_attack", "bigBadWolf_walk", "bigBadWolf_idle", "bigBadWolf_appear", "bigBadWolf_disappear"];
    int SummonWalk => IsWolf ? WolfWalk : AmptronWalk;
    // +0x120: the tornado's whoosh, every two seconds
    int whooshMs;
    /// <summary>+0x109: current animation id.</summary>
    public int AnimId { get; private set; } = -1;
    /// <summary>Character +0x50.</summary>
    public Facing Facing { get; private set; } = Facing.SW;
    /// <summary>+0x90: drawing scale.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>+0x38: the cell the character stands on (or is leaving).</summary>
    public int Cell { get; set; } = -1;
    /// <summary>+0xe0: the cell it is heading for (its own cell when standing).</summary>
    public int NextCell { get; set; } = -1;
    /// <summary>+0x84: the object this character is walking to or dealing with.</summary>
    public CafeObject? Interacting { get; set; }
    /// <summary>+0xb05: a customer on the way in (the spawner waits for these).</summary>
    public bool Entering { get; set; }
    /// <summary>+0xb08: staff boredom clock (counts down while working; at 0 the zombie daydreams).</summary>
    public int Boredom { get; set; }
    /// <summary>+0xe4: staff: time to the next "hungry enough to go for a customer?" check.</summary>
    public int AttackCheck { get; set; }
    /// <summary>+0x10c: bites a zombie can take out of this body (a tenth of its full energy).</summary>
    public int Bites { get; set; }
    /// <summary>+0xb10: has screamed at an attack already.</summary>
    public bool Scared { get; set; }
    /// <summary>+0xb11: runs (rather than walks) out of the cafe.</summary>
    public bool Fleeing { get; set; }
    /// <summary>+0x108: a raid's sides (1 the player's raiders, 2 the restaurant's chef and staff, 0 neither).</summary>
    public int Team { get; set; }
    /// <summary>+0xb04: a zombie hit while below 60% of its energy (K 0x42cf34) - the raid's energize tip.</summary>
    public bool Weakened { get; set; }
    /// <summary>+0xb0c: a raid customer's tries at running from the fight before it heads for the street.</summary>
    public int PanicRuns { get; set; }

    // +0x114: last cry (hurt sounds and screams share it); +0x110: last attack grunt
    long lastCry = long.MinValue / 2, lastGrunt = long.MinValue / 2;
    // +0xe8 / +0xec: a body nobody finished eating fades away
    bool fading;
    int fadeMs;

    SubImage[] parts;
    FlashAnimation? anim;
    readonly Dictionary<int, FlashAnimation.PartOverride> overrides = new();

    public override CafeObjectType Type => CafeObjectType.Character;

    public CharacterInstance(Cafe cafe, Character character)
    {
        Cafe = cafe;
        Data = cafe.Data;
        Character = character;
        var name = Def.PartsSheet(character.IsZombie);
        parts = Data.Sheet(name, name + ".bin");
        Height = 150;
        SetAnimation(CharAnim.Idle, Facing.SW);
        barHeight = anim?.FrameBounds(1f).Height is > 0 and var bh ? bh : 150f;
        // CharacterInstance(): a tenth of full energy in bites; the boredom clock from the type's focus
        Bites = (int)(character.MaxEnergy(Data) / 10f);
        int spread = Data.K.Int(0x42cf1c);
        Boredom = character.Focus * Data.K.Int(0x42cf18) + (spread > 0 ? cafe.Rng.Next(spread) : 0);
    }

    /// <summary>Swap the body (a human customer turned into a zombie, etc.).</summary>
    public void RefreshParts()
    {
        var name = Def.PartsSheet(Character.IsZombie);
        parts = Data.Sheet(name, name + ".bin");
        SetAnimation((CharAnim)Math.Max(0, AnimId), Facing, true);
    }

    /// <summary>CharacterInstance::setAnimation (vf27).</summary>
    public void SetAnimation(CharAnim id, Facing facing, bool force = false)
    {
        if (IsPet)
        {
            SetPetAnimation(PetAnimFor(id), facing, force);
            return;
        }
        if (IsYeti)
        {
            SetYetiAnimation(facing, force);
            return;
        }
        if (IsSummon)
        {
            SetSummonAnimation(id switch
            {
                CharAnim.Walk or CharAnim.Run or CharAnim.Walk2 or CharAnim.Walk3 or CharAnim.Walk4 or CharAnim.Carry => SummonWalk,
                CharAnim.Attack or CharAnim.Attack2 or CharAnim.Kick => IsWolf ? WolfAttack : AmptronAttack,
                _ => IsWolf ? WolfIdle : AmptronIdle,
            }, facing, force);
            return;
        }
        if (AnimId == (int)id && Facing == facing && !force)
            return;
        AnimId = (int)id;
        Facing = facing;
        Character.Facing = (byte)facing;
        var (file, mirrored) = Data.Animations.Get((int)id, (int)facing);
        if (file == null)
        {
            anim?.Stop();
            return;
        }
        anim = new FlashAnimation(file, parts) { FlipX = mirrored, Overrides = overrides };
        if (CharacterAnimations.PlaysOnce((int)id))
            anim.LoopCount = 1;
        anim.Reset();
        // CharacterInstance::setAnimation: walking and running keep time with the character's speed (K 2.0 .. 4.0 over
        // a 0..10 stat), the attack at half that; turning into the wolf and back go a frame an update at most
        if (id is CharAnim.Walk or CharAnim.Run or CharAnim.Walk2 or CharAnim.Walk3 or CharAnim.Walk4 or CharAnim.Carry)
            anim.SetRate(Character.SpeedFactor(Data));
        else if (id == CharAnim.Attack)
            anim.SetRate(Character.SpeedFactor(Data) * 0.5f);
        else if (id is CharAnim.Transform or CharAnim.Detransform)
            anim.MultiStep = false;
        anim.Play();
    }

    /// <summary>What a pet does for a character animation (the pets have twelve: walk, idle, sit, attacks, dead ..).</summary>
    static int PetAnimFor(CharAnim id) => id switch
    {
        CharAnim.Walk or CharAnim.Run or CharAnim.Walk2 or CharAnim.Walk3 or CharAnim.Walk4 or CharAnim.Carry => PetAnim.Walk,
        CharAnim.Sit => PetAnim.Sit,
        CharAnim.Attack or CharAnim.Attack2 or CharAnim.Kick => PetAnim.Attack1,
        CharAnim.Death => PetAnim.Dead,
        _ => PetAnim.Idle,
    };

    /// <summary>
    /// PetInstance::vf27: one of the pets' own animations (<see cref="PetAnim"/>). Walking and the later attacks keep
    /// time with the pet's speed at three quarters of a zombie's rate.
    /// </summary>
    public void SetPetAnimation(int id, Facing facing, bool force = false)
    {
        if (AnimId == id && Facing == facing && !force)
            return;
        AnimId = id;
        Facing = facing;
        Character.Facing = (byte)facing;
        var (file, mirrored) = Data.PetAnimations.Get(id, (int)facing);
        if (file == null)
        {
            anim?.Stop();
            return;
        }
        anim = new FlashAnimation(file, parts) { FlipX = mirrored, Overrides = overrides };
        if (PetAnimations.PlaysOnce(id))
            anim.LoopCount = 1;
        anim.Reset();
        if (PetAnimations.FollowsSpeed(id))
            anim.SetRate(Character.SpeedFactor(Data) * 0.75f);
        anim.Play();
    }

    /// <summary>
    /// YetiInstance::vf27 (FUN_00194d50 / FUN_00194dd0): the yeti has one animation in each form - its attack as the
    /// yeti (yeti_attack, "000-YetiParts"), the spin as a tornado (tornado, "000-TornadoParts") - at its own pace.
    /// </summary>
    void SetYetiAnimation(Facing facing, bool force)
    {
        if (AnimId == 0 && Facing == facing && !force)
            return;
        AnimId = 0;
        Facing = facing;
        Character.Facing = (byte)facing;
        string name = YetiForm ? "yeti_attack" : "tornado";
        string sheet = YetiForm ? "000-YetiParts" : "000-TornadoParts";
        bool north = facing is Facing.NW or Facing.NE;
        var file = Data.Anim(name + (north ? "NW" : "SW") + ".anim");
        if (file == null)
        {
            anim?.Stop();
            return;
        }
        parts = Data.Sheet(sheet, sheet + ".bin");
        anim = new FlashAnimation(file, parts) { FlipX = facing is Facing.SE or Facing.NE, Overrides = overrides, MultiStep = false };
        anim.Reset();
        anim.Play();
    }

    /// <summary>
    /// VoltronInstance::vf27 / BigBadWolfInstance::vf27: one of the summon's own animations, a frame an update at most.
    /// Appearing and going exist facing SW only; the rest face SW or NW, mirrored for SE and NE.
    /// </summary>
    public void SetSummonAnimation(int id, Facing facing, bool force = false)
    {
        if (AnimId == id && Facing == facing && !force)
            return;
        if (id != SummonWalk)
            stepMs = 0;
        AnimId = id;
        Facing = facing;
        Character.Facing = (byte)facing;
        bool oneWay = id >= (IsWolf ? WolfAppear : AmptronAppear);
        bool north = !oneWay && facing is Facing.NW or Facing.NE;
        var file = Data.Anim((IsWolf ? WolfFiles : AmptronFiles)[id] + (north ? "NW" : "SW") + ".anim");
        if (file == null)
        {
            anim?.Stop();
            return;
        }
        anim = new FlashAnimation(file, parts) { FlipX = facing is Facing.SE or Facing.NE, Overrides = overrides, MultiStep = false };
        if (oneWay)
            anim.LoopCount = 1;
        anim.Reset();
        anim.Play();
    }

    /// <summary>FUN_00195504 / FUN_00194418: the yeti takes its yeti form, or spins up into a tornado again.</summary>
    public void SetYetiForm(bool yeti)
    {
        if (YetiForm == yeti)
            return;
        YetiForm = yeti;
        whooshMs = 0;
        SetYetiAnimation(Facing, true);
    }

    /// <summary>The frame the animation is on (the yeti's blows land on particular frames).</summary>
    public int AnimFrame => anim?.Frame ?? -1;

    /// <summary>ReanimateTask::vf02: down for the count - the fall's last frame, held.</summary>
    public void LieDead()
    {
        SetAnimation(CharAnim.Death, Facing, true);
        anim?.JumpToEnd();
    }

    /// <summary>Same animation, new facing (setAnimation with 0xff).</summary>
    public void Face(Facing facing)
    {
        if (IsPet)
            SetPetAnimation(Math.Max(0, AnimId), facing);
        else if (IsSummon)
            SetSummonAnimation(Math.Max(0, AnimId), facing);
        else
            SetAnimation((CharAnim)Math.Max(0, AnimId), facing);
    }

    public void SetAnimationRate(float rate) => anim?.SetRate(rate);

    /// <summary>vf24: where the body is drawn right now (cafe coordinates).</summary>
    public Rectangle BodyBounds
    {
        get
        {
            var b = anim?.FrameBounds(Scale) ?? default;
            return new Rectangle(X + b.X, Y + b.Y, b.Width, b.Height);
        }
    }

    /// <summary>vf35: a play-once animation has ended.</summary>
    public bool AnimationFinished => anim == null || anim.IsFinished;

    /// <summary>vf37: turn towards a point.</summary>
    public void FaceTowards(float px, float py)
    {
        var f = Facing;
        if (px < X)
        {
            if (py < Y) f = Facing.NW;
            else if (py > Y) f = Facing.SW;
        }
        else if (px > X)
        {
            if (py < Y) f = Facing.NE;
            else if (py > Y) f = Facing.SE;
        }
        Face(f);
    }

    // ---------------------------------------------------------------- part overrides (vf31..vf33)

    /// <summary>vf31: draw <paramref name="image"/> in place of animation part <paramref name="part"/>.</summary>
    public void SetOverride(int part, SubImage image, float x, float y, float scale, bool flipX = false, bool useAnimFlip = false, Color? tint = null)
    {
        overrides[part] = new FlashAnimation.PartOverride(image, x, y, scale, flipX, useAnimFlip, tint ?? Color.White);
    }

    /// <summary>Draw a piece of scenery in place (world top-left) on the layer of animation part <paramref name="part"/>.</summary>
    public void SetSceneryPart(int part, SubImage image, float x, float y, bool flipX, Color tint)
    {
        overrides[part] = new FlashAnimation.PartOverride(image, x, y, 1f, flipX, false, tint, true);
    }

    /// <summary>vf32.</summary>
    public void ClearOverride(int part) => overrides.Remove(part);

    /// <summary>vf33.</summary>
    public void ClearOverrides() => overrides.Clear();

    /// <summary>Carry a dish picture in the hands (part 0), as the serve/carry tasks do.</summary>
    public void CarryFood(SubImage? picture)
    {
        ClearOverride(0);
        if (picture == null)
            return;
        float s = Data.K.CarriedFoodScale;
        float h = picture.FullHeight * s;
        SetOverride(0, picture, 0, (h - 51f) * 0.5f, s);
    }

    // ---------------------------------------------------------------- tasks

    readonly List<CafeTask> tasks = new(); // +0x68 .. +0x78

    public int TaskCount => tasks.Count;
    public CafeTask? CurrentTask => tasks.Count > 0 ? tasks[0] : null;
    public bool IsIdle => tasks.Count == 0;
    public IReadOnlyList<CafeTask> Tasks => tasks;

    /// <summary>vf41: queue a task (it starts right away when the queue was empty).</summary>
    public void AddTask(CafeTask t)
    {
        bool start = tasks.Count == 0;
        tasks.Add(t);
        if (start)
            t.Start();
    }

    /// <summary>vf42: cancel the current task, forget the rest, stand idle.</summary>
    public void ClearTasks()
    {
        if (tasks.Count > 0)
            tasks[0].Cancel();
        DropTasks();
        // VoltronInstance / BigBadWolfInstance vf42: the summons only drop their tasks
        if (IsSummon)
            return;
        Character.Job = Job.None;
        if (!IsKnockedOut)
            SetAnimation(CharAnim.Idle, Facing, true);
    }

    /// <summary>vf43: the current task wraps up soon, queued ones are dropped (a new order is coming).</summary>
    public void Interrupt()
    {
        if (tasks.Count == 0)
            return;
        tasks[0].StopSoon();
        if (!IsSummon)
            Character.Job = Job.None;
        while (tasks.Count > 1)
        {
            tasks[1].Release();
            tasks.RemoveAt(1);
        }
    }

    /// <summary>vf44.</summary>
    public void DropTasks()
    {
        foreach (var t in tasks)
            t.Release();
        tasks.Clear();
    }

    /// <summary>vf18: out of energy.</summary>
    public bool IsKnockedOut => Character.Energy <= 0f;

    // ---------------------------------------------------------------- fighting

    static readonly int[] HurtMale = [144, 145, 146, 148, 151], HurtFemale = [159, 162, 163, 166, 167];
    static readonly int[] ZombieHurtMale = [107, 108, 113, 114, 117], ZombieHurtFemale = [22, 26, 27, 29, 31, 111];
    static readonly int[] GroanMale = [15, 16, 17, 86, 88], GroanFemale = [18, 19, 20, 87, 89];

    int Pick(int[] sounds) => sounds[Cafe.Rng.Next(sounds.Length)];

    /// <summary>vf22: struck by <paramref name="attacker"/> for <paramref name="damage"/> energy.</summary>
    public void TakeHit(CharacterInstance attacker, int damage)
    {
        long now = Cafe.ClockMs;
        if (IsSummon)
        {
            // VoltronInstance::vf22 / BigBadWolfInstance::vf22: a zombie's cry now and then, no knock-back; it turns on
            // the attacker only while still on its way to someone else
            if (now - lastCry > Data.K.Int(0x42cf24))
            {
                lastCry = now;
                Cafe.Events.Sound(Pick(ZombieHurtMale));
            }
            if (CurrentTask is AttackTask { Engaged: false })
            {
                Interacting = attacker;
                Interrupt();
            }
            Character.Energy = Math.Clamp(Character.Energy - damage, 0f, Character.MaxEnergy(Data));
            return;
        }
        if (IsPet || IsYeti)
        {
            // PetInstance::vf22 / YetiInstance::vf22: a yelp now and then (the cat mews like a zombie, the robots
            // clank, the yeti roars like a zombie), no knock-back
            if (now - lastCry > Data.K.Int(0x42cf24))
            {
                lastCry = now;
                var pet = Character.Pet;
                Cafe.Events.Sound(pet == null ? Pick(ZombieHurtMale) : pet.IsCat ? Pick(ZombieHurtFemale) : pet.IsRobot ? 215 : 208 + Cafe.Rng.Next(2));
            }
            if (Interacting != attacker)
            {
                Interacting = attacker;
                Interrupt();
            }
            Character.Energy = Math.Clamp(Character.Energy - damage, 0f, Character.MaxEnergy(Data));
            return;
        }
        // vf22: a zombie hit when already below 60% (K 0x42cf34) is worn down (a raid offers energizing)
        if (IsZombie && Character.Energy < Character.MaxEnergy(Data) * Data.K.Float(0x42cf34))
            Weakened = true;
        if (now - lastCry > Data.K.Int(0x42cf24))
        {
            lastCry = now;
            Cafe.Events.Sound(!IsZombie
                ? Pick(Def.IsFemale ? HurtFemale : HurtMale)
                : Pick(Def.IsFemale ? ZombieHurtFemale : ZombieHurtMale));
        }
        // turn on the attacker, unless already locked in a fight between two staff next to each other
        var foe = Interacting as CharacterInstance;
        bool locked = foe != null && (Cafe.AreNeighbours(Cell, foe.Cell) || Cell == foe.Cell)
            && Character.Role != Role.Customer && foe.Character.Role != Role.Customer && foe.Interacting == this;
        if (!locked)
        {
            if (Interacting != attacker)
            {
                Interacting = attacker;
                Interrupt();
            }
            if (AnimId != (int)CharAnim.Attack)
                SetAnimation(CharAnim.Hit, Facing, true);
        }
        // anyone but staff is knocked back in front of the attacker
        if (Character.Role != Role.Staff)
        {
            int cell = Cafe.Neighbour(attacker.Cell, (int)attacker.Facing);
            if (cell >= 0 && Cafe.IsInside(cell))
            {
                Cafe.PlaceOnCell(this, cell);
                FaceTowards(attacker.X, attacker.Y);
            }
        }
        Character.Energy = Math.Clamp(Character.Energy - damage, 0f, Character.MaxEnergy(Data));
    }

    /// <summary>
    /// +0x80 / +0x8c: an energy bar over the head (a raid's fighters): half-clear green by default (the rivals), the
    /// player's raiders and pets half-clear red (GameStateRaid::load sets 0x7fff0000).
    /// </summary>
    public bool ShowEnergyBar { get; set; }
    public Color EnergyBarColor { get; set; } = new(0, 0xff, 0, 0x7f);
    // the height the bar sits above the feet: the standing body's (the original keeps a box per animation)
    float barHeight;

    /// <summary>+0xf8 / +0xf4 (vf40): a tombstone's boost - 1 strength, 2 energy - and the ms it has left.</summary>
    public int BoostType { get; private set; }
    public int BoostMs { get; set; }

    /// <summary>vf40: boosted for K 0x42cdcc ms (five seconds).</summary>
    public void Boost(int type)
    {
        BoostType = type;
        BoostMs = (int)Data.K.Float(0x42cdcc);
    }

    /// <summary>vf19: down for good (customers) or until reanimated (zombies).</summary>
    public void KnockDown()
    {
        ShowEnergyBar = false;
        Character.Deaths++;
        ClearTasks();
        SetAnimation(CharAnim.Death, Facing);
    }

    /// <summary>vf20: the attacker's grunt as a blow lands (not more often than every 4 s).</summary>
    public void Grunt()
    {
        long now = Cafe.ClockMs;
        if (now - lastGrunt <= Data.K.Int(0x42cf28))
            return;
        lastGrunt = now;
        Cafe.Events.Sound(100 + Cafe.Rng.Next(4));
    }

    /// <summary>FUN_00035ae8: a zombie's hungry groan (FUN_0009d228 for a pet: the cat as a zombie, robots whirr, the rest growl).</summary>
    public void Groan()
    {
        if (IsSummon)
            return;
        if (Character.Pet is { } pet)
            Cafe.Events.Sound(pet.IsCat ? Pick(GroanFemale) : pet.IsRobot ? 217 : 206 + Cafe.Rng.Next(2));
        else
            Cafe.Events.Sound(Pick(Def.IsFemale ? GroanFemale : GroanMale));
    }

    /// <summary>AttackTask::vf05: a pet's last cry as it goes down (the cat's is a zombie's).</summary>
    public int PetDeathSound()
    {
        var pet = Character.Pet!;
        return pet.IsCat ? 92 + Cafe.Rng.Next(2) : pet.IsRobot ? 218 + Cafe.Rng.Next(2) : 202 + Cafe.Rng.Next(2);
    }

    /// <summary>FeastTask::vf05: a pet's crunch while it eats (the cat's is a zombie's).</summary>
    public int PetEatSound()
    {
        var pet = Character.Pet!;
        return pet.IsCat ? 96 : pet.IsRobot ? 216 : 204 + Cafe.Rng.Next(2);
    }

    /// <summary>FUN_00035c6c: one scream when an attack starts nearby.</summary>
    public void Scream()
    {
        if (Scared)
            return;
        Scared = true;
        long now = Cafe.ClockMs;
        if (now - lastCry < Data.K.Int(0x42cf24))
            return;
        lastCry = now;
        Cafe.Events.Sound(CrowdScream(Def.IsFemale ? 0 : 1, Def.IsFemale ? 1 : 0, Cafe.Rng));
    }

    /// <summary>FUN_00035cc0: the scream of a crowd of so many men and women (-1: none).</summary>
    public static int CrowdScream(int men, int women, Random rng)
    {
        if (men > 0 && women > 0)
        {
            int r = rng.Next(5);
            return r == 0 ? 66 : 136 + r;
        }
        if (men >= 2)
            return 141 + rng.Next(3);
        if (women >= 2)
            return 134 + rng.Next(3);
        if (men == 1)
            return 119 + rng.Next(7);
        return women == 1 ? 126 + rng.Next(8) : -1;
    }

    /// <summary>vf39: a body nobody finished eating fades out (K 3000 ms) and is then taken away.</summary>
    public void StartFading()
    {
        fading = true;
        fadeMs = Data.K.Int(0x42cdc8);
    }

    public bool Faded => fading && fadeMs <= 0;

    /// <summary>vf17: ms per world unit of walking.</summary>
    public float MoveTimePerUnit
    {
        get
        {
            var k = Data.K;
            float t = k.BaseWalkTime / k.WalkTimeDivisor / Character.SpeedFactor(Data);
            return IsZombie ? t * k.ZombieWalkSlowdown : t;
        }
    }

    /// <summary>CharacterInstance::update (vf07).</summary>
    public void Update(int dtMs)
    {
        if (IsSummon && Active)
        {
            // VoltronInstance::vf07 / BigBadWolfInstance::vf07: its time runs while it stands
            LifeMs -= dtMs;
        }
        if (IsYeti)
        {
            // YetiInstance::vf07: its time runs down; spinning, it whooshes every two seconds (sound 212)
            LifeMs -= dtMs;
            if (!YetiForm)
            {
                whooshMs -= dtMs;
                if (whooshMs < 1)
                {
                    whooshMs += 2000;
                    Cafe.Events.Sound(212);
                }
            }
        }
        NextCell = Cell;
        if (tasks.Count > 0)
        {
            var head = tasks[0];
            head.Update(dtMs);
            if (head.Done)
            {
                if (tasks.Remove(head))
                    head.Release();
                if (tasks.Count == 0)
                {
                    if (IsYeti && !IsKnockedOut)
                        SetYetiForm(false);
                    if (!IsKnockedOut)
                        SetAnimation(CharAnim.Idle, Facing);
                }
                else
                {
                    tasks[0].Start();
                }
            }
        }
        if (Character.Role == Role.Staff && Character.Job != Job.Resting && Character.Job != Job.Other)
            Boredom -= dtMs;
        Character.UpdateLevel(Data);
        anim?.Update(dtMs);
        // VoltronInstance::vf07 / BigBadWolfInstance::vf07: walking, the summon stomps every second (sound 223)
        if (IsSummon && AnimId == SummonWalk && (stepMs -= dtMs) < 1)
        {
            stepMs += 1000;
            Cafe.Events.Sound(223);
        }
        if (fading)
            fadeMs -= dtMs;
    }

    public override void Draw(Cafe cafe, GameData data)
    {
        // a pet at home in its habitat is not seen (DisappearTask hides it, AppearTask shows it again)
        if (!Visible)
            return;
        var tint = Color.White;
        if (fading)
            tint.A = (byte)(255 * Math.Clamp(fadeMs / (float)Math.Max(1, data.K.Int(0x42cdc8)), 0f, 1f));
        // vf (FUN_00034f2c): a boosted one pulses - red for strength, green for energy
        if (BoostMs > 0 && Character.Energy > 0)
        {
            byte p = (byte)(55 + 200 * MathF.Abs(MathF.Sin(cafe.ClockMs / 180f)));
            tint = BoostType == 2 ? new Color(p, (byte)255, p, tint.A) : new Color((byte)255, p, p, tint.A);
        }
        anim?.Draw((int)X, (int)Y, Scale, tint);
        // CharacterInstance::draw / PetInstance::draw (+0x80): a half-clear white box over the head (K 100 x 8, two
        // pixels round), the energy in it
        if (ShowEnergyBar)
        {
            int w = data.K.Int(0x42cf2c), h = data.K.Int(0x42cf30);
            float top = Y - barHeight * Scale;
            Gfx.Draw.Rect(new Rectangle(X - w / 2f - 2, top - h - 4, w + 4, h + 4), new Color(0xff, 0xff, 0xff, 0x80));
            float share = Math.Clamp(Character.Energy / Math.Max(1f, Character.MaxEnergy(data)), 0f, 1f);
            Gfx.Draw.Rect(new Rectangle(X - w / 2f, top - h - 2, w * share, h), EnergyBarColor);
        }
        // YetiInstance / VoltronInstance / BigBadWolfInstance::draw: the time it has left over its head, minutes and
        // seconds, in red (the summons' once they stand)
        if ((IsYeti || IsSummon && Active) && !IsKnockedOut)
        {
            int left = Math.Max(0, LifeMs / 1000);
            Text.DrawAnchored($"{left / 60:00}:{left % 60:00}", X, Y - barHeight * Scale - data.K.Int(0x42cf30) * 4,
                36, Anchor.Bottom | Anchor.HCenter, new Color(0xff, 0, 0, 0xff), new Color(0x80, 0x80, 0, 0xff), 2);
        }
        // PetInstance::draw: a pet that has gone up a level wears the bobbing "!" (ui 30) until it is looked at
        if (IsPet && Character.LevelledUp && data.IngameUi.Length > 30)
            data.IngameUi[30].Draw((int)X, BodyBounds.Y + cafe.Bob, Anchor.Bottom | Anchor.HCenter, Color.White);
    }
}
