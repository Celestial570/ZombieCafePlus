using ZombieCafe.Assets;

namespace ZombieCafe.Data;

/// <summary>furnitureData.bin category byte; doubles as the index into the texture prefix table.</summary>
public enum FurnitureCategory : byte
{
    Decor = 0, Stove = 1, Counter = 2, Table = 3, Chair = 4, Wall = 5, WallDecor = 6, Floor = 7, Fridge = 8,
    Pot = 9, Cash = 10, Sink = 11, Expansion = 12, ToxinBarrel = 13, Tombstone = 14, ToxinGenerator = 15,
    Billboard = 16, BurnTimeBooster = 17, Habitat = 18, Special = 255,
}

/// <summary>One furnitureData.bin record (the original's 100-byte struct; field order = file order).</summary>
public sealed class FurnitureDef
{
    static readonly string[] Prefixes =
    [
        "decor", "stove", "counter", "table", "chair", "wall", "wallDecor", "floor", "fridge", "pot", "cash",
        "sink", "expansion", "toxinBarrel", "tombstone", "toxinGenerator", "billboard", "burnTimeBooster", "habitat",
    ];

    public int Id { get; init; }
    /// <summary>Decor set (0 basic, 10 asian, 12 generic, ... 255 special).</summary>
    public byte Theme { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Image key; with category and per-category image index gives "&lt;prefix&gt;_NNN_&lt;image&gt;.cct".</summary>
    public string Image { get; init; } = "";
    public int Price { get; init; }
    /// <summary>0 = cash, 1 = toxin, 2 = other.</summary>
    public byte Currency { get; init; }
    public byte Width { get; init; }
    public byte Depth { get; init; }
    /// <summary>Image number: the NNN in "&lt;prefix&gt;_NNN_&lt;image&gt;.cct"; for floors the entry in tile.bin. (+0x18)</summary>
    public int ImageIndex { get; init; }
    public byte B1C { get; init; }
    public FurnitureCategory Category { get; init; }
    /// <summary>Shop tab (1..5, 255 = not in shop).</summary>
    public byte ShopTab { get; init; }
    /// <summary>ARGB tint for floors/walls (-1 = none). (+0x20)</summary>
    public int Tint { get; init; }
    public byte B24 { get; init; }
    public short S26 { get; init; }
    public short S28 { get; init; }
    public float F2C { get; init; }
    public int I30 { get; init; }
    /// <summary>Read and discarded by the original loader.</summary>
    public byte Unused { get; init; }
    public float F34 { get; init; }
    public string Description { get; init; } = "";
    public float F3C { get; init; }
    public bool ForSale { get; init; }
    /// <summary>Stove family 0..16 (picks pot / burnt pot images); 0 = basic pot.</summary>
    public byte StoveFamily { get; init; }
    public bool B42 { get; init; }
    public short S44 { get; init; }
    public string S48 { get; init; } = "";
    /// <summary>Pet living in this habitat (255 = none).</summary>
    public byte PetId { get; init; }

    /// <summary>"&lt;prefix&gt;_NNN_&lt;image&gt;" (append ".cct" / "Offsets.bin"); null for floors, which live in tile.cct.</summary>
    public string? TextureBaseName =>
        (int)Category < Prefixes.Length && Category != FurnitureCategory.Floor
            ? $"{Prefixes[(int)Category]}_{ImageIndex:000}_{Image}"
            : null;

    public static List<FurnitureDef> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadInt();
        var list = new List<FurnitureDef>(n);
        for (int i = 0; i < n; i++)
        {
            var f = new FurnitureDef
            {
                Id = i, Theme = r.ReadByte(), Name = r.ReadUtf(), Image = r.ReadUtf(), Price = r.ReadInt(),
                Currency = r.ReadByte(), Width = r.ReadByte(), Depth = r.ReadByte(), ImageIndex = r.ReadInt(),
                B1C = r.ReadByte(), Category = (FurnitureCategory)r.ReadByte(), ShopTab = r.ReadByte(),
                Tint = r.ReadInt(), B24 = r.ReadByte(), S26 = r.ReadShort(), S28 = r.ReadShort(), F2C = r.ReadFloat(),
                I30 = r.ReadInt(), Unused = r.ReadByte(), F34 = r.ReadFloat(), Description = r.ReadUtf(),
                F3C = r.ReadFloat(), ForSale = r.ReadBoolean(), StoveFamily = r.ReadByte(), B42 = r.ReadBoolean(),
                S44 = r.ReadShort(), S48 = r.ReadUtf(), PetId = r.ReadByte(),
            };
            list.Add(f);
        }
        return list;
    }
}
