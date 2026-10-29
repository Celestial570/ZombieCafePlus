using ZombieCafe.Assets;

namespace ZombieCafe.Data;

/// <summary>
/// One kind of rival restaurant (enemyCafeData.bin, FUN_001da48c; the registry FUN_001da434): what the map shows
/// for it, who runs it and works there, and which room layout it uses.
/// </summary>
public sealed class EnemyCafeType
{
    public int Index { get; init; }
    public int Id { get; init; }
    /// <summary>+0xc: the name's first part ("0" in the file: none, an owner's name goes there instead).</summary>
    public string Prefix { get; init; } = "";
    /// <summary>+0x10: "Cafe", "Diner", ...</summary>
    public string Name { get; init; } = "";
    /// <summary>+0x14: a boss restaurant (the special chefs).</summary>
    public bool Boss { get; init; }
    /// <summary>+0x18: its picture in the cafeUi sheet (FUN_001db2f4; the competitor dialog, the raid).</summary>
    public int Picture { get; init; }
    /// <summary>+0x1c: the chef's character type.</summary>
    public int ChefDef { get; init; }
    /// <summary>+0x20: the staff's character types.</summary>
    public int[] Staff { get; init; } = [];
    /// <summary>+0x28: the building on the map (mapTiles sprite).</summary>
    public int MapSprite { get; init; }
    /// <summary>+0x2c: the room layout (enemyLayouts.bin), -1 = the first.</summary>
    public int Layout { get; init; }
    /// <summary>+0x30: the player level from which it opens (and the boss chefs' place in the chef store).</summary>
    public int MinLevel { get; init; }

    public static List<EnemyCafeType> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadByte();
        var list = new List<EnemyCafeType>(n);
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadShort();
            string prefix = r.ReadUtf(), name = r.ReadUtf();
            bool boss = r.ReadShort() != 0;
            int picture = r.ReadShort(), chef = r.ReadShort();
            string staff = r.ReadUtf();
            int sprite = r.ReadShort(), layout = r.ReadShort(), minLevel = r.ReadShort();
            list.Add(new EnemyCafeType
            {
                Index = i, Id = id, Prefix = prefix == "0" ? "" : prefix, Name = name, Boss = boss, Picture = picture,
                ChefDef = chef, Staff = ParseList(staff), MapSprite = sprite, Layout = layout, MinLevel = minLevel,
            });
        }
        return list;
    }

    /// <summary>"55_56_57" -> [55, 56, 57] (FUN_0003db70 split on '_').</summary>
    public static int[] ParseList(string s) =>
        s.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t, out int v) ? v : -1).Where(v => v >= 0).ToArray();
}

/// <summary>
/// The rival restaurants' rooms (enemyLayouts.bin, FUN_0018cd0c): runs of a header {"", item count, width, depth}
/// then that many items {letter, orientation, u, v}. The letter says what stands there; enemyItems.bin says
/// which furniture for each kind of restaurant (FUN_001dbcc4).
/// </summary>
public sealed class EnemyLayout
{
    public sealed record Item(char Letter, int Orientation, int U, int V);

    public int Width { get; init; }
    public int Depth { get; init; }
    public List<Item> Items { get; } = new();

    public static List<EnemyLayout> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadShort();
        var layouts = new List<EnemyLayout>();
        EnemyLayout? current = null;
        for (int i = 0; i < n; i++)
        {
            string letter = r.ReadUtf();
            int a = r.ReadInt(), b = r.ReadInt(), c = r.ReadInt();
            if (letter.Length == 0)
            {
                current = new EnemyLayout { Width = b, Depth = c };
                layouts.Add(current);
            }
            else
            {
                current?.Items.Add(new Item(letter[0], a, b, c));
            }
        }
        return layouts;
    }

    /// <summary>FUN_001dbcc4's jump table: which enemyItems.bin column a layout letter takes its furniture from.</summary>
    public static int Column(char letter) => letter switch
    {
        'A' => 11, 'B' => 13, 'C' => 4, 'D' => 14, 'E' => 16, 'F' => 5, 'G' => 6, 'H' => 1, 'I' => 15, 'L' => 10,
        'P' => 12, 'S' => 3, 'T' => 2, 'W' => 9, _ => -1,
    };

    /// <summary>enemyItems.bin columns 7 and 8: the walls and the floors.</summary>
    public const int WallColumn = 7, FloorColumn = 8;
}

/// <summary>enemyItems.bin (FUN_0018d194): per kind of restaurant, its name and 16 lists of furniture.</summary>
public sealed class EnemyItems
{
    public string Name { get; init; } = "";
    public int[][] Lists { get; init; } = [];

    public int[] List(int column) => column >= 1 && column <= Lists.Length ? Lists[column - 1] : [];

    public static List<EnemyItems> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadShort();
        var list = new List<EnemyItems>(n);
        for (int i = 0; i < n; i++)
        {
            string name = r.ReadUtf();
            var lists = new int[16][];
            for (int k = 0; k < 16; k++)
                lists[k] = EnemyCafeType.ParseList(r.ReadUtf());
            list.Add(new EnemyItems { Name = name, Lists = lists });
        }
        return list;
    }
}
