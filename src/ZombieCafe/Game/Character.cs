using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>What a character is doing, shown under its name (Character +0x44, CharacterInstance::vf38).</summary>
public enum Job : byte
{
    None = 0, Serving = 1, Cooking = 2, Resting = 3, Attacking = 4, Daydreaming = 5, Other = 6,
}

/// <summary>Who a character is in the cafe (Character +0x19).</summary>
public enum Role : byte
{
    /// <summary>Visitors of raid/friend cafes.</summary>
    Visitor = 0,
    Customer = 1,
    /// <summary>A zombie on the player's staff.</summary>
    Staff = 2,
    /// <summary>A raided restaurant's waiters (GameStateRaid::load's FUN_000273c0(..., 3, 0)).</summary>
    EnemyStaff = 3,
    /// <summary>The port's mark for a pet (the original's Pet record, a type of its own, PetInstance::getType 4).</summary>
    Pet = 4,
    /// <summary>The port's mark for a raid's summoned helper (the yeti, YetiInstance::getType 5).</summary>
    Summon = 5,
}

/// <summary>
/// The persistent part of a person or zombie (original Character, 0x68 bytes, FUN_000273c0): which
/// character type, the stats copied from it, level, experience, energy and current job. The cafe actor
/// that walks around is a <see cref="Cafe.CharacterInstance"/> pointing at one of these.
/// </summary>
public sealed class Character
{
    public CharacterDef Def { get; }
    /// <summary>+0x18.</summary>
    public bool IsZombie { get; set; }
    /// <summary>+0x19.</summary>
    public Role Role { get; set; }
    /// <summary>+0x1c.</summary>
    public string Name { get; set; }
    /// <summary>+0x24: customers this zombie has served.</summary>
    public int Served { get; set; }
    /// <summary>+0x2c: customers this zombie has brought down.</summary>
    public int Kills { get; set; }
    /// <summary>+0x30: times knocked out.</summary>
    public int Deaths { get; set; }
    /// <summary>+0x20: when this character joined (converted, or the chef's opening day), Unix ms.</summary>
    public long JoinedUnixMs { get; set; } = GameClock.UnixMs;
    /// <summary>+0x28: raids taken part in.</summary>
    public int Raids { get; set; }
    /// <summary>+0x38: zombie level (0-based).</summary>
    public int Level { get; set; }
    /// <summary>+0x3c: levelled up since last shown.</summary>
    public bool LevelledUp { get; set; }
    /// <summary>+0x40: energy; working drains it, at 0 the zombie collapses.</summary>
    public float Energy { get; set; }
    /// <summary>+0x44.</summary>
    public Job Job { get; set; }
    /// <summary>+0x50: facing (0..3).</summary>
    public byte Facing { get; set; }
    /// <summary>+0x54: ms until a knocked-out zombie gets back up (it takes no orders meanwhile).</summary>
    public int ReanimateMs { get; set; }
    /// <summary>+0x58: walk animation (0 walk; 18 is used as an alternate shuffle).</summary>
    public byte WalkAnim { get; set; }
    /// <summary>+0x60: how fast this character's energy drains (1.0).</summary>
    public float EnergyDrain { get; set; } = 1f;
    /// <summary>+0x64: experience towards the next level.</summary>
    public int Xp { get; set; }

    /// <summary>The pet type when this is a pet's record (Pet, 0x50 bytes, FUN_0009d748), else null.</summary>
    public PetDef? Pet => Def.Pet;
    public bool IsPet => Def.Pet != null;

    public Character(CharacterDef def, Role role, bool zombie, string name)
    {
        Def = def;
        Role = role;
        IsZombie = zombie;
        Name = name;
        Energy = def.F0C;
        Speed = def.StatsA[0];
        Strength = def.StatsA[1];
        BaseEnergy = def.F0C;
    }

    // the stats block (+0x08..+0x17) is a copy of the type's first 16 bytes (a raid sets its own for the rivals)
    /// <summary>+0x0d: speed (walk/run playback and movement).</summary>
    public int Speed { get; set; }
    /// <summary>+0x0e: attack strength.</summary>
    public int Strength { get; set; }
    /// <summary>What a running Super Server tombstone adds to speed, tip rating, focus and restraint (FUN_000b878c).</summary>
    public int StatBonus { get; set; }
    /// <summary>+0x0f: tip rating (how much extra a customer leaves when this zombie served them).</summary>
    public int TipRating => Def.StatsA[2] + StatBonus;
    /// <summary>+0x14: base energy.</summary>
    public float BaseEnergy { get; set; }
    /// <summary>+0x11: how long this type keeps its mind on the job (x 120 s between daydreams).</summary>
    public int Focus => Def.StatsB[1] + StatBonus;
    /// <summary>+0x12: how well this type holds its hunger in (11 - this sets when it starts eyeing customers).</summary>
    public int Restraint => Def.StatsB[2] + StatBonus;

    /// <summary>CharacterInstance::vf10: energy at full for the current level.</summary>
    public float MaxEnergy(GameData data) =>
        MathF.Floor(BaseEnergy * data.ZombieLevelMultiplier[Math.Clamp(Level, 0, data.ZombieLevelMultiplier.Length - 1)]);

    /// <summary>FUN_00034230 / CharacterInstance::vf14: 2.0 + 2.0 * speed / 10.</summary>
    public float SpeedFactor(GameData data) =>
        data.K.WalkRateMin + (data.K.WalkRateMax - data.K.WalkRateMin) * Speed / 10f;

    /// <summary>FUN_00034274: staff lose energy while they work (never below 1).</summary>
    public void Drain(GameData data, int dtMs)
    {
        if (Role != Role.Staff)
            return;
        float lost = data.K.EnergyDrainPerMs * (int)(dtMs * EnergyDrain);
        Energy = Math.Max(1f, Energy - lost);
    }

    /// <summary>The level-up part of CharacterInstance::update.</summary>
    public void UpdateLevel(GameData data)
    {
        var table = data.K.ZombieLevelXp;
        if (Level < data.K.ZombieMaxLevel && Level < table.Length)
        {
            int need = table[Level];
            if (need <= Xp)
            {
                Xp -= need;
                Level++;
                LevelledUp = true;
                Energy = Math.Max(0f, MaxEnergy(data));
            }
        }
        else
        {
            Xp = 0;
        }
    }

    /// <summary>FUN_00030848: a random first name (male names, then female names, in strings.bin).</summary>
    public static string RandomName(GameData data, bool female, Random rng)
    {
        int male = data.K.Int(0x42d630), fem = data.K.Int(0x42d634);
        return female
            ? data.Strings[StringTable.MaleNames + male + rng.Next(Math.Max(1, fem))]
            : data.Strings[StringTable.MaleNames + rng.Next(Math.Max(1, male))];
    }
}
