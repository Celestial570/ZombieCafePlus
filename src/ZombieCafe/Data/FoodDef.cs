using ZombieCafe.Assets;

namespace ZombieCafe.Data;

/// <summary>One foodData.bin record (the original's 0x24-byte recipe struct, FUN_000595a0).</summary>
public sealed class FoodDef
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    /// <summary>+0x04: price of cooking one batch.</summary>
    public short Cost { get; init; }
    /// <summary>+0x16: player level needed.</summary>
    public byte Level { get; init; }
    /// <summary>+0x06: cooking time in minutes.</summary>
    public short CookMinutes { get; init; }
    /// <summary>+0x08: servings one batch makes.</summary>
    public short Servings { get; init; }
    /// <summary>+0x0a: what a customer pays per serving.</summary>
    public short Price { get; init; }
    /// <summary>+0x0c: experience for cooking a batch.</summary>
    public short Xp { get; init; }
    public short S14 { get; init; }
    /// <summary>+0x18: cookbook this recipe belongs to.</summary>
    public int Cookbook { get; init; }
    /// <summary>
    /// +0x1c: 1 = the recipe a level brings, 0 = an ordinary one, 2..4 = rare (10 toxin to unlock in the book);
    /// also picks the decor tip bonus.
    /// </summary>
    public byte Kind { get; init; }
    public byte B1D { get; init; }
    /// <summary>+0x1e: the stove family that cooks it (0 = any ordinary stove; others need their special stove).</summary>
    public byte B1E { get; init; }
    public int StoveFamily => B1E;
    public bool IsRare => Kind is >= 2 and <= 4;
    /// <summary>+0x20: links special recipes to the special customers they attract.</summary>
    public short S20 { get; init; }
    /// <summary>+0x23: the cookbook the recipe is in (1 the General, 2 the Raid, 7 the Pirate, a special stove's ..).</summary>
    public byte B23 { get; init; }
    public int Book => B23;

    /// <summary>FUN_0016da80: "%03d-" + the name without '?', '"' and spaces (".cct" + ".bin" atlas, image 0).</summary>
    public string ImageName => $"{Index:000}-{Name.Replace("?", "").Replace("\"", "").Replace(" ", "")}";

    /// <summary>FUN_0016d720: cooking time in seconds; Frozen doubles it, Quick/Very Quick take 90%/80%.</summary>
    public int CookSeconds(int modifier) => modifier switch
    {
        7 => CookMinutes * 120,
        9 => 4800 * CookMinutes / 100,
        8 => 5400 * CookMinutes / 100,
        _ => CookMinutes * 60,
    };

    /// <summary>FUN_0016d7dc: seconds until the dish burns (max(cook + 600, 2 x cook); Fresh: 3 x cook).</summary>
    public int BurnSeconds(int modifier)
    {
        int cook = CookSeconds(modifier);
        int scaled = modifier == 6 ? cook * 3 : cook << 1;
        return Math.Max(cook + 600, scaled);
    }

    /// <summary>FUN_0016d698: price of cooking with a modifier.</summary>
    public int CostWith(int modifier) => modifier switch
    {
        3 => 95 * Servings / 100 + Cost,
        4 => 180 * Servings / 100 + Cost,
        5 => Cost << 1,
        7 => Cost * 3 >> 2,
        _ => Cost,
    };

    /// <summary>FUN_0016d76c: price per serving with a modifier (Fancy +1, Very Fancy +2).</summary>
    public int PriceWith(int modifier) => modifier switch { 4 => Price + 2, 3 => Price + 1, _ => Price };

    /// <summary>FUN_0016d710: servings with a modifier (Bulk doubles).</summary>
    public int ServingsWith(int modifier) => Servings << (modifier == 5 ? 1 : 0);

    /// <summary>FUN_0016d788: experience with a modifier (Spicy +10%/+1, Very Spicy +20%/+2).</summary>
    public int XpWith(int modifier) => modifier switch
    {
        1 => Math.Max(Xp + 1, Xp * 110 / 100),
        2 => Math.Max(Xp + 2, Xp * 120 / 100),
        _ => Xp,
    };

    public static List<FoodDef> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadShort();
        var list = new List<FoodDef>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(new FoodDef
            {
                Index = i, Name = r.ReadUtf(), Cost = r.ReadShort(), Level = r.ReadByte(), CookMinutes = r.ReadShort(),
                Servings = r.ReadShort(), Price = r.ReadShort(), Xp = r.ReadShort(), S14 = r.ReadShort(),
                Cookbook = r.ReadByte(), Kind = r.ReadByte(), B1D = r.ReadByte(), B1E = r.ReadByte(),
                S20 = r.ReadShort(), B23 = r.ReadByte(),
            });
        }
        if (r.Position != file.Length)
            throw new InvalidDataException($"foodData.bin: read {r.Position} of {file.Length} bytes");
        return list;
    }
}

/// <summary>One cookbookData.bin record (0x1c bytes, FUN_000597a4).</summary>
public sealed class CookbookDef
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    /// <summary>+0x04: the cash a book on sale costs (the Pirate's, the Vampire's; 0 = not sold).</summary>
    public short Price { get; init; }
    /// <summary>+0x06: the toxin a book costs instead (FUN_00101af0), 0 for none.</summary>
    public short S06 { get; init; }
    /// <summary>+0x08: its cover in the CookBookUi sheet.</summary>
    public short S08 { get; init; }
    public short S0A { get; init; }
    /// <summary>+0x0c: how to unlock it / what it offers.</summary>
    public string Description { get; init; } = "";
    /// <summary>+0x10: special stove family that unlocks it (0 = none).</summary>
    public short StoveFamily { get; init; }
    public string S14 { get; init; } = "";
    /// <summary>+0x18: the level the book can be had from ("This cookbook unlocks at level ...").</summary>
    public short S18 { get; init; }

    public static List<CookbookDef> LoadAll(byte[] file)
    {
        var r = new BigEndianReader(file);
        int n = r.ReadByte();
        var list = new List<CookbookDef>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(new CookbookDef
            {
                Index = i, Name = r.ReadUtf(), Price = r.ReadShort(), S06 = r.ReadShort(), S08 = r.ReadShort(),
                S0A = r.ReadShort(), Description = r.ReadUtf(), StoveFamily = r.ReadShort(), S14 = r.ReadUtf(),
                S18 = r.ReadShort(),
            });
        }
        if (r.Position != file.Length)
            throw new InvalidDataException($"cookbookData.bin: read {r.Position} of {file.Length} bytes");
        return list;
    }
}

/// <summary>strings.bin: the game's text, one string per line (FUN_0003e1f0 splits on '\n').</summary>
public sealed class StringTable
{
    readonly string[] lines;

    public StringTable(byte[] file)
    {
        var raw = new List<string>();
        int start = 0;
        for (int i = 0; i <= file.Length; i++)
        {
            if (i < file.Length && file[i] != (byte)'\n')
                continue;
            raw.Add(Decode(file.AsSpan(start, i - start)));
            start = i + 1;
        }
        lines = raw.ToArray();
    }

    // the file is mostly UTF-8 with a few Latin-1 lines
    static string Decode(ReadOnlySpan<byte> b)
    {
        try
        {
            return new System.Text.UTF8Encoding(false, true).GetString(b);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return System.Text.Encoding.Latin1.GetString(b);
        }
    }

    public int Count => lines.Length;
    public string this[int index] => index >= 0 && index < lines.Length ? lines[index] : "";

    // indices used by the cafe (string ids the binary keeps in __const)
    public const int StatusServing = 71, StatusResting = 72, StatusAttacking = 73, StatusCooking = 74,
        StatusDaydreaming = 76, MaleNames = 91, ModifierNames = 333, CookRecipe = 560, UnlockAtLevel = 587;
}
