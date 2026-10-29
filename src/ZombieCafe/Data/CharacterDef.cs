using ZombieCafe.Assets;

namespace ZombieCafe.Data;

/// <summary>
/// One characterData.bin record (the original's 0x58-byte character type, FUN_0003042c). Field names follow
/// how the game uses them; the original struct offset is noted where the meaning is still being traced.
/// </summary>
public sealed class CharacterDef
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Base of the parts sheet: "%03d-&lt;FileName without spaces&gt;-humanParts"/"-zombieParts" (+0x1c).</summary>
    public string FileName { get; init; } = "";
    public string Name3 { get; init; } = "";
    /// <summary>Byte read first (+0x20).</summary>
    public byte B20 { get; init; }
    /// <summary>+0x04 low byte.</summary>
    public byte B04 { get; init; }
    /// <summary>+0x00: third byte / 255.</summary>
    public float F00 { get; init; }
    /// <summary>+0x21: -1 = human or zombie, 0 = human only, 1 = zombie only.</summary>
    public sbyte Kind { get; init; }
    /// <summary>+0x0c (read as a short).</summary>
    public float F0C { get; init; }
    /// <summary>Packed into +0x04 bytes 1..3 and +0x08 bytes 0..2.</summary>
    public byte[] StatsA { get; init; } = new byte[3];
    public byte[] StatsB { get; init; } = new byte[3];
    /// <summary>+0x28 (true only for female characters).</summary>
    public bool IsFemale { get; init; }
    public int I34 { get; init; }
    public byte B38 { get; init; }
    public byte B39 { get; init; }
    public float F40 { get; init; }
    public int I44 { get; init; }
    public float F48 { get; init; }
    public float F4C { get; init; }
    public bool B50 { get; init; }
    public short S52 { get; init; }
    public byte B54 { get; init; }
    public string HumanDescription { get; init; } = "";
    public string ZombieDescription { get; init; } = "";

    public bool CanBeHuman => Kind is -1 or 0;
    public bool CanBeZombie => Kind is -1 or 1;

    /// <summary>The pet this stands in for (<see cref="PetDef.Body"/>), or null for a real character type.</summary>
    public PetDef? Pet { get; init; }
    /// <summary>The zombie yeti a tombstone summons in a raid (FUN_00193048's type, made in code, not read from a file).</summary>
    public bool IsYeti { get; init; }
    /// <summary>The Amptron the five robot pets combine into in a raid (FUN_001884a0's type, made in code).</summary>
    public bool IsAmptron { get; init; }
    /// <summary>The Big Bad Wolf a fairy-tale zombie turns into in a raid (FUN_0002a590's type, made in code).</summary>
    public bool IsWolf { get; init; }

    public string PartsSheet(bool zombie) => IsYeti ? "000-YetiParts" : IsAmptron ? "000-VoltronParts" : IsWolf ? "000-BigBadWolfParts" : Pet != null
        ? Pet.PartsSheet
        : $"{Index:000}-{FileName.Replace(" ", "")}-{(zombie ? "zombie" : "human")}Parts";

    public static List<CharacterDef> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadInt();
        var list = new List<CharacterDef>(n);
        for (int i = 0; i < n; i++)
        {
            byte b20 = r.ReadByte(), b04 = r.ReadByte(), b2 = r.ReadByte();
            string name = r.ReadUtf(), file2 = r.ReadUtf(), name3 = r.ReadUtf();
            sbyte kind = r.ReadSByte();
            short s4 = r.ReadShort();
            byte[] a = [r.ReadByte(), r.ReadByte(), r.ReadByte()];
            byte[] b = [r.ReadByte(), r.ReadByte(), r.ReadByte()];
            list.Add(new CharacterDef
            {
                Index = i, B20 = b20, B04 = b04, F00 = b2 / 255f, Name = name, FileName = file2, Name3 = name3,
                Kind = kind, F0C = s4, StatsA = a, StatsB = b, IsFemale = r.ReadBoolean(), I34 = r.ReadInt(),
                B38 = r.ReadByte(), B39 = r.ReadByte(), F40 = r.ReadFloat(), I44 = r.ReadInt(), F48 = r.ReadFloat(),
                F4C = r.ReadFloat(), B50 = r.ReadBoolean(), S52 = r.ReadShort(), B54 = r.ReadByte(),
                HumanDescription = r.ReadUtf(), ZombieDescription = r.ReadUtf(),
            });
        }
        return list;
    }
}

/// <summary>
/// Animation ids of the shared character animation table, as loadCharacterAnimations (FUN_00030a74) builds it - the
/// game's own table, which animationData.bin (never read by the game) does not match from 14 on.
/// </summary>
public enum CharAnim
{
    Walk = 0, Idle = 1, Sit = 2, Attack = 3, Carry = 4, Serve = 5, Death = 6, Eat = 7, Operate = 8, Run = 9,
    Bump = 10, Celebrate = 11, Hit = 12, Attack2 = 13, Idle3 = 14, MouthWipe = 15, Idle5 = 16,
    /// <summary>bump again: what a daydreaming zombie plays.</summary>
    Bump2 = 17,
    Walk2 = 18, Walk3 = 19, Walk4 = 20, Crouch = 21, Feast = 22,
    /// <summary>mouthwipe again: up from a meal.</summary>
    MouthWipe2 = 23,
    Hungry = 24, Rise = 25, Hung = 26, Hung2 = 27,
    /// <summary>attack_kick: what a character hits a pet with.</summary>
    Kick = 28,
    /// <summary>transform / detransform (SW only): a fairy-tale zombie turning into the Big Bad Wolf and back.</summary>
    Transform = 29, Detransform = 30,
}

/// <summary>Facing: 0 = SE (mirrored SW), 1 = SW, 2 = NW, 3 = NE (mirrored NW).</summary>
public enum Facing { SE = 0, SW = 1, NW = 2, NE = 3 }

/// <summary>The global [animId][direction] table of .anim files (DAT_0042c5fc).</summary>
public sealed class CharacterAnimations
{
    public const int Count = 31;
    readonly AnimFile?[,] table = new AnimFile?[Count, 4];

    /// <summary>
    /// loadCharacterAnimations (FUN_00030a74): each animation's file facing SW (direction 1) and NW (2), by name; the
    /// hung ones use one file both ways, transform and detransform face SW only.
    /// </summary>
    static readonly string[] Files =
    [
        "walk", "idle", "sit", "attack", "carry", "serve", "death", "eat", "operate", "run",
        "bump", "celebrate", "hit", "attack2", "idle3", "mouthwipe", "idle5", "bump", "walk2", "walk3",
        "walk4", "crouch", "feast", "mouthwipe", "hungry", "rise", "hung", "hung2", "attack_kick", "transform",
        "detransform",
    ];

    public CharacterAnimations(IpaArchive ipa)
    {
        var cache = new Dictionary<string, AnimFile?>();
        AnimFile? Load(string name)
        {
            if (!cache.TryGetValue(name, out var anim))
                cache[name] = anim = ipa.TryRead(name) is { } bytes ? AnimFile.Parse(bytes) : null;
            return anim;
        }
        for (int id = 0; id < Count; id++)
        {
            string f = Files[id];
            if (id is (int)CharAnim.Hung or (int)CharAnim.Hung2)
            {
                table[id, 1] = table[id, 2] = Load(f + ".anim");
                continue;
            }
            table[id, 1] = Load(f + "SW.anim");
            if (id < (int)CharAnim.Transform)
                table[id, 2] = Load(f + "NW.anim");
        }
    }

    /// <summary>FUN_00033f30: the file for (anim, facing); missing facings borrow their mirror image.</summary>
    public (AnimFile? Anim, bool Mirrored) Get(int animId, int facing)
    {
        if (animId < 0 || animId >= Count || facing < 0 || facing > 3)
            return (null, false);
        if (table[animId, facing] is { } a)
            return (a, false);
        int alt = facing switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
        return (table[animId, alt], true);
    }

    /// <summary>Animations that play once instead of looping (mask 0x60a3d060 in FUN_00033f30).</summary>
    public static bool PlaysOnce(int animId) => animId < 31 && (0x60a3d060u >> animId & 1) != 0;
}
