using ZombieCafe.Assets;

namespace ZombieCafe.Data;

/// <summary>
/// One petData.bin record (the original's 0x44-byte pet type, FUN_0009a118 from ZombieCafe::loadStep): the pet a
/// habitat brings (furniture def +0x4c), its stats and the parts sheet "NNN-&lt;image&gt;" its body is cut from.
/// </summary>
public sealed class PetDef
{
    /// <summary>+0x00: the pet's number (its place in the file; also the NNN of the parts sheet).</summary>
    public int Index { get; init; }
    /// <summary>+0x01: the record's first byte.</summary>
    public byte B01 { get; init; }
    /// <summary>+0x04.</summary>
    public string Name { get; init; } = "";
    /// <summary>+0x08: the parts sheet's name ("Rottweiler", "roboticPetA", ...).</summary>
    public string Image { get; init; } = "";
    public byte B0C { get; init; }
    /// <summary>+0x10 / +0x14: what the pet itself costs (20 toxin for every one).</summary>
    public int Price { get; init; }
    public byte Currency { get; init; }
    /// <summary>+0x18.</summary>
    public string Description { get; init; } = "";
    /// <summary>+0x20: "Atk. Strength" (PetInstance::vf11, the damage of a bite).</summary>
    public int Strength { get; init; }
    /// <summary>+0x24: "Atk. Speed" (PetInstance::vf12; also how fast it walks, vf14).</summary>
    public int Speed { get; init; }
    public int I28 { get; init; }
    public int I2C { get; init; }
    /// <summary>+0x30: "Cri. Chance" (a fraction).</summary>
    public float CritChance { get; init; }
    /// <summary>+0x34: "Cri. Multiplier".</summary>
    public float CritMultiplier { get; init; }
    /// <summary>+0x38: energy at level 0 (times the zombies' level multiplier above that, PetInstance::vf10).</summary>
    public float Energy { get; init; }
    public int I3C { get; init; }

    // the pets by number (petData.bin order), for the bonuses their habitats give (GameStateCafe +0x240)
    public const int Wolf = 0, Rottweiler = 1, CheshireCat = 2, Sphinx = 3, FieryHound = 4, GoldenLion = 5, RoyalBear = 6,
        RageBull = 7, MidnightPanther = 8;

    /// <summary>The Cheshire Cat (PetInstance::vf22 / FUN_000bad9c pick its sounds by this).</summary>
    public bool IsCat => Index == 2;
    /// <summary>The five robot pets (4 .. 8) clank instead of growl.</summary>
    public bool IsRobot => Index is >= 4 and <= 8;

    /// <summary>FUN_0009b3a4: the parts sheet, "%03d-%s" (".cct" + ".bin").</summary>
    public string PartsSheet => $"{Index:000}-{Image}";

    CharacterDef? body;

    /// <summary>
    /// The port's stand-in character type for a pet, so a pet can be a <see cref="Game.Cafe.CharacterInstance"/>
    /// walking the same paths as everyone else: speed and strength in the stats block, the energy as base energy.
    /// </summary>
    public CharacterDef Body => body ??= new CharacterDef
    {
        Index = -1 - Index, Name = Name, FileName = Image, Kind = 1, F0C = Energy,
        StatsA = [(byte)Math.Clamp(Speed, 0, 255), (byte)Math.Clamp(Strength, 0, 255), 0], StatsB = [0, 0, 0],
        Pet = this,
    };

    public static List<PetDef> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadByte();
        var list = new List<PetDef>(n);
        for (int i = 0; i < n; i++)
        {
            byte b01 = r.ReadByte();
            string name = r.ReadUtf(), image = r.ReadUtf();
            byte b0c = r.ReadByte();
            int price = r.ReadInt();
            byte currency = r.ReadByte();
            string description = r.ReadUtf();
            int energy = r.ReadInt();
            list.Add(new PetDef
            {
                Index = i, B01 = b01, Name = name, Image = image, B0C = b0c, Price = price, Currency = currency,
                Description = description, Energy = energy, Strength = r.ReadInt(), Speed = r.ReadInt(), I28 = r.ReadInt(),
                I2C = r.ReadInt(), CritChance = r.ReadFloat(), CritMultiplier = r.ReadFloat(), I3C = r.ReadInt(),
            });
        }
        return list;
    }
}

/// <summary>The pets' animation ids (the [anim][facing] table DAT_0042cd14, FUN_0009a3f4).</summary>
public static class PetAnim
{
    public const int Walk = 0, Idle = 1, Sit = 2, Attack1 = 3, Attack2 = 4, Attack3 = 5, CriticalAttack = 6, Dead = 7,
        Appear = 8, Disappear = 9, RobotAppear = 10, RobotDisappear = 11;
    public const int Count = 12;
}

/// <summary>
/// The pets' .anim files (DAT_0042cd14, filled by FUN_0009a3f4): one SW and one NW file per animation (the other two
/// facings borrow their mirror image), except the dog's appearing (SW only) and disappearing (NW only).
/// </summary>
public sealed class PetAnimations
{
    readonly AnimFile?[,] table = new AnimFile?[PetAnim.Count, 4];

    // the file names FUN_0009a3f4 loads, by animation and facing (1 SW, 2 NW)
    static readonly (int Anim, int Facing, string File)[] Files =
    [
        (PetAnim.Walk, 1, "dog_walkSW.anim"), (PetAnim.Walk, 2, "dog_walkNW.anim"),
        (PetAnim.Idle, 1, "dog_idleSW.anim"), (PetAnim.Idle, 2, "dog_idleNW.anim"),
        (PetAnim.Sit, 1, "dog_sitSW.anim"), (PetAnim.Sit, 2, "dog_sitNW.anim"),
        (PetAnim.Attack1, 1, "dog_attack1SW.anim"), (PetAnim.Attack1, 2, "dog_attack1NW.anim"),
        (PetAnim.Attack2, 1, "dog_attack2SW.anim"), (PetAnim.Attack2, 2, "dog_attack2NW.anim"),
        (PetAnim.Attack3, 1, "dog_attack3SW.anim"), (PetAnim.Attack3, 2, "dog_attack3NW.anim"),
        (PetAnim.CriticalAttack, 1, "dog_critical_attackSW.anim"), (PetAnim.CriticalAttack, 2, "dog_critical_attackNW.anim"),
        (PetAnim.Dead, 1, "dog_deadSW.anim"), (PetAnim.Dead, 2, "dog_deadNW.anim"),
        (PetAnim.Appear, 1, "dog_appear.anim"),
        (PetAnim.Disappear, 2, "dog_disappear.anim"),
        (PetAnim.RobotAppear, 1, "robotdog_appearSW.anim"), (PetAnim.RobotAppear, 2, "robotdog_appearNW.anim"),
        (PetAnim.RobotDisappear, 1, "robotdog_disappearSW.anim"), (PetAnim.RobotDisappear, 2, "robotdog_disappearNW.anim"),
    ];

    public PetAnimations(IpaArchive ipa)
    {
        foreach (var (anim, facing, file) in Files)
            if (ipa.TryRead(file) is { } bytes)
                table[anim, facing] = AnimFile.Parse(bytes);
    }

    /// <summary>FUN_0009b964: the file for (anim, facing); a missing facing borrows its mirror image (0x2030001).</summary>
    public (AnimFile? Anim, bool Mirrored) Get(int anim, int facing)
    {
        if (anim < 0 || anim >= PetAnim.Count || facing < 0 || facing > 3)
            return (null, false);
        if (table[anim, facing] is { } a)
            return (a, false);
        int alt = facing switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
        return (table[anim, alt], true);
    }

    /// <summary>FUN_0009b964: dying, appearing and disappearing play once, and so does sitting down; the rest loop.</summary>
    public static bool PlaysOnce(int anim) => anim is >= PetAnim.Dead and <= PetAnim.RobotDisappear or PetAnim.Sit;

    /// <summary>PetInstance::vf27: walking and the later attacks keep time with the pet's speed (mask 0x71).</summary>
    public static bool FollowsSpeed(int anim) => anim < 7 && (0x71 >> anim & 1) != 0;
}
