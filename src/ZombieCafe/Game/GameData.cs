using ZombieCafe.Assets;
using ZombieCafe.Data;
using ZombieCafe.Gfx;

namespace ZombieCafe.Game;

/// <summary>Everything loaded from the original bundle that the game logic reads: tables, constants, sprite sheets.</summary>
public sealed class GameData : IDisposable
{
    public IpaArchive Ipa { get; }
    public TextureStore Textures { get; }
    public GameConstants K { get; }
    public IReadOnlyList<FurnitureDef> Furniture { get; }

    /// <summary>An item of the port's own, after the game's (the Two Point build's bigger expansions).</summary>
    public void AddFurniture(FurnitureDef def) => ((List<FurnitureDef>)Furniture).Add(def);
    public IReadOnlyList<CharacterDef> Characters { get; }
    public CharacterAnimations Animations { get; }
    /// <summary>The pets (petData.bin, DAT_0042c664) and their animations (DAT_0042cd14).</summary>
    public IReadOnlyList<PetDef> Pets { get; }
    public PetAnimations PetAnimations { get; }
    readonly Dictionary<string, AnimFile?> namedAnims = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An .anim file by name (the specials' animations, loaded when first used).</summary>
    public AnimFile? Anim(string file)
    {
        if (!namedAnims.TryGetValue(file, out var a))
            namedAnims[file] = a = Ipa.TryRead(file) is { } bytes ? AnimFile.Parse(bytes) : null;
        return a;
    }

    CharacterDef? yeti;

    /// <summary>
    /// FUN_00193048: the zombie yeti's type, made in code - strength 20, speed 10 and a million energy (it cannot be
    /// worn down; its time runs out instead).
    /// </summary>
    public CharacterDef YetiDef => yeti ??= new CharacterDef
    {
        Index = -100, Name = Exe.Get(0x3399e1).Trim(), FileName = "Yeti", Kind = 1, F0C = 1_000_000f,
        StatsA = [10, 20, 0], StatsB = [0, 0, 0], IsYeti = true,
    };

    CharacterDef? amptron;

    /// <summary>FUN_001884a0: the Amptron's type, made in code - strength 20, speed 10, ten thousand energy.</summary>
    public CharacterDef AmptronDef => amptron ??= new CharacterDef
    {
        Index = -101, Name = Exe.Get(0x3399e7).Trim(), FileName = "Voltron", Kind = 1, F0C = 10_000f,
        StatsA = [10, 20, 0], StatsB = [0, 0, 0], IsAmptron = true,
    };

    CharacterDef? wolf;

    /// <summary>FUN_0002a590: the Big Bad Wolf's type, made in code - strength 20, speed 10, ten thousand energy.</summary>
    public CharacterDef WolfDef => wolf ??= new CharacterDef
    {
        Index = -102, Name = Exe.Get(0x3399f0), FileName = "BigBadWolf", Kind = 1, F0C = 10_000f,
        StatsA = [10, 20, 0], StatsB = [0, 0, 0], IsWolf = true,
    };

    ExeStrings? exe;
    /// <summary>Text that is only in the game's code (read from the .ipa's executable when first asked for).</summary>
    public ExeStrings Exe => exe ??= new ExeStrings(Ipa);
    public IReadOnlyList<FoodDef> Foods { get; }
    public IReadOnlyList<CookbookDef> Cookbooks { get; }
    public StringTable Strings { get; }
    /// <summary>The rival restaurants (enemyCafeData.bin), their rooms (enemyLayouts.bin) and furniture (enemyItems.bin).</summary>
    public IReadOnlyList<EnemyCafeType> EnemyCafes { get; }
    public IReadOnlyList<EnemyLayout> EnemyLayouts { get; }
    public IReadOnlyList<EnemyItems> EnemyItems { get; }
    /// <summary>Floor tiles (tile.cct + tile.bin), indexed by a cell's floor number.</summary>
    public SubImage[] Tiles { get; }
    /// <summary>In-game UI sprites (ipad_ingameUi*), the original's sheet at game+0x224.</summary>
    public SubImage[] IngameUi { get; }

    /// <summary>Zombie max energy multiplier per level: 1.0, then x1.03 per level (built in loadStep).</summary>
    public float[] ZombieLevelMultiplier { get; }

    // pot shown on a working stove and the burnt pot, per stove family (FUN_001e0f24, DAT_0042d948/954)
    static readonly int[] PotDefs = [0xba, 0x18b, 400, 0x19d, 0x1bb, 0x1e1, 0x1fd, 0x20f, 0x221, 0x232, 0x247, 0x265, 0x27a, 0x29c, 0xba, 0xba, 0xba];
    static readonly int[] BurntPotDefs = [0xc5, 0x18c, 0x191, 0x19e, 0x1bc, 0x1e2, 0x1fe, 0x210, 0x222, 0x233, 0x248, 0x266, 0x27b, 0x29d, 0xc5, 0xc5, 0xc5];

    public FurnitureDef PotDef(int stoveFamily) => Furniture[PotDefs[Math.Clamp(stoveFamily, 0, PotDefs.Length - 1)]];
    public FurnitureDef BurntPotDef(int stoveFamily) => Furniture[BurntPotDefs[Math.Clamp(stoveFamily, 0, BurntPotDefs.Length - 1)]];

    readonly Dictionary<int, SubImage?> foodImages = new();

    /// <summary>FUN_0016e254 / FUN_0016da80: a recipe's picture (first image of "NNN-Name.cct").</summary>
    public SubImage? FoodImage(int foodIndex)
    {
        if (foodImages.TryGetValue(foodIndex, out var img))
            return img;
        if (foodIndex >= 0 && foodIndex < Foods.Count)
        {
            var name = Foods[foodIndex].ImageName;
            var s = Sheet(name, name + ".bin");
            img = s.Length > 0 ? s[0] : null;
        }
        foodImages[foodIndex] = img;
        return img;
    }

    readonly Dictionary<string, SubImage[]> sheets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, int> wallDefByImage = new();

    public GameData(IpaArchive ipa)
    {
        Ipa = ipa;
        Textures = new TextureStore(ipa);
        K = GameConstants.Load(ipa.Read("constants.bin"));
        Furniture = FurnitureDef.LoadAll(ipa.Read("furnitureData.bin"));
        Characters = CharacterDef.LoadAll(ipa.Read("characterData.bin"));
        Animations = new CharacterAnimations(ipa);
        Pets = PetDef.LoadAll(ipa.Read("petData.bin"));
        PetAnimations = new PetAnimations(ipa);
        Foods = FoodDef.LoadAll(ipa.Read("foodData.bin"));
        Cookbooks = CookbookDef.LoadAll(ipa.Read("cookbookData.bin"));
        Strings = new StringTable(ipa.Read("strings.bin"));
        EnemyCafes = EnemyCafeType.LoadAll(ipa.Read("enemyCafeData.bin"));
        EnemyLayouts = EnemyLayout.LoadAll(ipa.Read("enemyLayouts.bin"));
        EnemyItems = Data.EnemyItems.LoadAll(ipa.Read("enemyItems.bin"));
        Tiles = Sheet("tile", "tile.bin");
        IngameUi = Sheet("ipad_ingameUiImages", "ipad_ingameUiOffsets.bin");
        ZombieLevelMultiplier = new float[K.ZombieMaxLevel + 1];
        ZombieLevelMultiplier[0] = 1f;
        for (int i = 1; i < ZombieLevelMultiplier.Length; i++)
            ZombieLevelMultiplier[i] = ZombieLevelMultiplier[i - 1] * K.ZombieEnergyGrowth;
        // walls with image numbers >= 4 are looked up through this table (built in FUN_001e0f24)
        int[] keys = [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32];
        int[] ids = [0xd1, 0xdc, 0xea, 0xeb, 0x171, 0x172, 0x173, 0x174, 0x175, 0x176, 0x196, 0x197, 0x1a3, 0x1b6, 0x1cf,
            0x1fc, 0x20c, 0x21d, 0x234, 0x23a, 0x249, 0x24a, 0x24b, 0x24c, 0x24d, 0x24e, 0x24f, 0x250, 0x251];
        for (int i = 0; i < keys.Length; i++)
            wallDefByImage[keys[i]] = ids[i];
    }

    /// <summary>
    /// Sub-images of "&lt;baseName&gt;.cct" cut by its atlas ("&lt;baseName&gt;Offsets.bin" unless given);
    /// a missing atlas yields one sub-image covering the whole texture.
    /// </summary>
    public SubImage[] Sheet(string baseName, string? atlasName = null)
    {
        if (sheets.TryGetValue(baseName, out var s))
            return s;
        var tex = Textures.Get(baseName + ".cct");
        if (tex.Id == 0)
            tex = Textures.Get(baseName + ".png");
        var atlasBytes = Ipa.TryRead(atlasName ?? baseName + "Offsets.bin");
        if (tex.Id == 0)
            s = [];
        else if (atlasBytes == null)
            s = [SubImage.Whole(tex)];
        else
            s = SpriteAtlas.Parse(atlasBytes).Entries.Select(e => SubImage.FromAtlas(tex, e)).ToArray();
        sheets[baseName] = s;
        return s;
    }

    public SubImage[] Sheet(FurnitureDef def) => def.TextureBaseName is { } n ? Sheet(n) : [];

    /// <summary>A sheet's grey copy (<see cref="TextureStore.GetGrey"/>), cut by the same atlas.</summary>
    public SubImage[] GreySheet(string baseName, string? atlasName = null)
    {
        string key = "grey:" + baseName;
        if (sheets.TryGetValue(key, out var s))
            return s;
        var tex = Textures.GetGrey(baseName + ".cct");
        var atlasBytes = Ipa.TryRead(atlasName ?? baseName + "Offsets.bin");
        if (tex.Id == 0)
            s = [];
        else if (atlasBytes == null)
            s = [SubImage.Whole(tex)];
        else
            s = SpriteAtlas.Parse(atlasBytes).Entries.Select(e => SubImage.FromAtlas(tex, e)).ToArray();
        sheets[key] = s;
        return s;
    }

    // walls whose image number is 0..3 come in colour variants; the variant is chosen by tint (FUN_001e1bd8)
    static readonly int[][] WallVariants = [[27, 28, 29, 30, 31, 32, 101], [101, 102, 103, 104, 105, 108], [137, 146], [167, 168, 169, 170, 171, 172]];

    /// <summary>The furniture record behind a wall with the given image number and tint.</summary>
    public FurnitureDef WallDef(int imageIndex, int tint)
    {
        if (imageIndex is >= 0 and < 4)
        {
            foreach (int id in WallVariants[imageIndex])
                if (Furniture[id].Tint == tint)
                    return Furniture[id];
            return Furniture[0];
        }
        return Furniture[wallDefByImage.TryGetValue(imageIndex, out int d) ? d : 0];
    }

    public void Dispose()
    {
        Textures.Dispose();
        Ipa.Dispose();
    }
}
