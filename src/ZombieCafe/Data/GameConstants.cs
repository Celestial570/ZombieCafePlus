using ZombieCafe.Assets;

namespace ZombieCafe.Data;

/// <summary>
/// constants.bin, read in the original's order (<see cref="ConstantsSpec"/>). Values are keyed by the
/// original global's address so ported code can cite exactly which constant it uses; well-understood
/// ones get named properties below.
/// </summary>
public sealed class GameConstants
{
    readonly Dictionary<int, object> values = new();

    public static GameConstants Load(byte[] file)
    {
        var c = new GameConstants();
        var r = new BigEndianReader(file);
        foreach (var e in ConstantsSpec.Entries)
        {
            if (e.Kind == ConstantsSpec.Kind.Scalar)
            {
                c.values[e.Address] = ReadOne(r, e.Type);
                continue;
            }
            int n = (int)Convert.ToInt64(ReadOne(r, e.CountType));
            var arr = new object[Math.Max(0, n)];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = ReadOne(r, e.Type);
            c.values[e.Address] = arr;
        }
        if (r.Position != file.Length)
            throw new InvalidDataException($"constants.bin: read {r.Position} of {file.Length} bytes");
        return c;
    }

    static object ReadOne(BigEndianReader r, char t) => t switch
    {
        'I' => r.ReadInt(),
        'S' => (int)r.ReadShort(),
        'B' => (int)r.ReadByte(),
        'F' => r.ReadFloat(),
        'L' => r.ReadLong(),
        'D' => r.ReadDouble(),
        'U' => r.ReadUtf(),
        _ => throw new InvalidDataException($"constants spec: bad type '{t}'"),
    };

    public int Int(int address) => Convert.ToInt32(values[address]);
    public float Float(int address) => Convert.ToSingle(values[address]);
    public long Long(int address) => Convert.ToInt64(values[address]);
    public string String(int address) => (string)values[address];
    public int[] Ints(int address) => ((object[])values[address]).Select(Convert.ToInt32).ToArray();
    public float[] Floats(int address) => ((object[])values[address]).Select(Convert.ToSingle).ToArray();
    public long[] Longs(int address) => ((object[])values[address]).Select(Convert.ToInt64).ToArray();
    public string[] Strings(int address) => ((object[])values[address]).Cast<string>().ToArray();

    // ---- named constants (meaning established from the decompiled code that uses them) ----
    /// <summary>Floor tile sprite width/height and the overlap between neighbouring tiles (constants 0x42ce90..9c).</summary>
    public int TileWidth => Int(0x42ce90);
    public int TileHeight => Int(0x42ce94);
    public int TileSpacingX => Int(0x42ce98);
    public int TileSpacingY => Int(0x42ce9c);

    // map / starting cafe layout (FUN_001601c8, FUN_00160c90)
    public int MapWidth => Int(0x42ce38);
    public int MapHeight => Int(0x42ce3c);
    public int PlayerOriginU => Int(0x42ce40);
    public int PlayerOriginV => Int(0x42ce44);
    public int RaidOriginU => Int(0x42ce48);
    public int RaidOriginV => Int(0x42ce4c);
    public int RoadWidth => Int(0x42ce50);
    public int StartRoomWidth => Int(0x42ce54);
    public int StartRoomDepth => Int(0x42ce58);
    /// <summary>Cells from the room corner to the starting doorway.</summary>
    public int DoorOffset => Int(0x42ce5c);
    /// <summary>Furniture id of the wall used for new walls (a tinted "Plain wall").</summary>
    public int PlainWallDef => Int(0x42ce7c);
    public int[] GrassTiles => Ints(0x42cea0);
    public int GrassTileCount => Int(0x42cea4);
    public int TileSidewalk => Int(0x42cea8);
    public int TileRoad => Int(0x42ceac);
    public int TileRoadStripe => Int(0x42ceb0);
    /// <summary>Floor tile of a freshly built (raid) cafe.</summary>
    public int TileCafeFloor => Int(0x42ceb4);
    public int TileCurb => Int(0x42cec0);
    public int TileSidewalkCorner => Int(0x42cec4);
    /// <summary>Horizontal inset of a wall sprite from its cell corner.</summary>
    public int WallInset => Int(0x42cee8);
    /// <summary>Furniture id of the starting floor.</summary>
    public int PlainFloorDef => Int(0x42d62c);
    /// <summary>First of the dirty wall images (24 = dirty1) / stained floor tiles (11) of the starting cafe.</summary>
    public int DirtyWallImage => Int(0x42d52c);
    public int DirtyFloorTile => Int(0x42d530);

    // characters
    /// <summary>Walk/run animation rate at speed stat 0 and 10 (CharacterInstance::setAnimation).</summary>
    public float WalkRateMin => Float(0x42cf08);
    public float WalkRateMax => Float(0x42cf0c);
    /// <summary>getMoveTimePerUnit = BaseWalkTime / WalkTimeDivisor / speed (x ZombieWalkSlowdown for zombies).</summary>
    public float BaseWalkTime => Float(0x42d5e0);
    public float WalkTimeDivisor => Float(0x42cf04);
    public float ZombieWalkSlowdown => Float(0x42cf20);
    /// <summary>Energy a working zombie loses per ms (x its drain factor), FUN_00034274.</summary>
    public float EnergyDrainPerMs => Float(0x42cefc);
    /// <summary>Zombie experience needed for each level, and the level cap (CharacterInstance::update).</summary>
    public int[] ZombieLevelXp => Ints(0x42d698);
    public int ZombieMaxLevel => Int(0x42d69c);
    /// <summary>Max energy grows by this factor per zombie level (loadStep builds the multiplier table).</summary>
    public float ZombieEnergyGrowth => Float(0x42d6a4);

    // player progression
    /// <summary>Experience needed to leave each player level (FUN_000d1f20) and the level cap.</summary>
    public int[] PlayerLevelXp => Ints(0x42d5a8);
    public int PlayerMaxLevel => Int(0x42d590);
    /// <summary>Cooking experience multiplier per player level (FUN_000da190, indexed level - 1).</summary>
    public float[] CookXpByLevel => Floats(0x42d6e0);
    /// <summary>Experience a staff zombie gets for serving a customer.</summary>
    public int ServeXp => Int(0x42d6a0);

    // customers (GameStateCafe)
    /// <summary>Spawn interval at rating 0 and 1, and its random spread (FUN_000b678c).</summary>
    public int SpawnDelayMin => Int(0x42d0d8);
    public int SpawnDelayMax => Int(0x42d0d4);
    public int SpawnDelaySpread => Int(0x42d0dc);
    /// <summary>Chance (out of 256) that a passer-by comes in, at rating 0 and 1 (FUN_000d9744).</summary>
    public int EnterChanceMin => Int(0x42d0e0);
    public int EnterChanceMax => Int(0x42d0e4);
    /// <summary>Pause in the doorway before looking for a seat: base +- spread ms.</summary>
    public int DoorPause => Int(0x42d0ec);
    public int DoorPauseSpread => Int(0x42d0f0);
    /// <summary>EatTask timings: patience while waiting for food, pause before eating, eating time.</summary>
    public int CustomerPatience => Int(0x42cf94);
    public int PreEatPause => Int(0x42cf98);
    public int EatTime => Int(0x42cf9c);
    /// <summary>Rating lost for a customer who leaves hungry, gained for a fed one (FUN_000da4c8).</summary>
    public float RatingLoss => Float(0x42d0ac);
    public float RatingGain => Float(0x42d0b0);
    public float RatingHintThreshold => Float(0x42d15c);

    // jobs
    /// <summary>Length of the hand-over ("serve") animation beat in serve/cleanup/carry tasks.</summary>
    public int HandOverTime => Int(0x42d540);
    /// <summary>How long status notes ($, tips, angry faces) stay up.</summary>
    public int NoteTime => Int(0x42d544);
    /// <summary>Furniture ids of the plate a served dish sits on and the dirty plate left behind.</summary>
    public int PlateDef => Int(0x42d500);
    public int DirtyPlateDef => Int(0x42d520);
    /// <summary>Scale of a food image carried by a zombie or shown on a counter/table.</summary>
    public float CarriedFoodScale => Float(0x42d5a0);
    /// <summary>A stove that is being worked keeps its burn clock this many seconds short of burning.</summary>
    public int BurnMargin => Int(0x42d580);
}
