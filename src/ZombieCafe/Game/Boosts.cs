using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>
/// The tombstones' powers (DAT_0042cd8c / DAT_0042cd80, eight slots, FUN_000f5374 ..): each tombstone works while it
/// stands in the cafe (FUN_000b4524 marks it placed - one put away in the storage box marks it stored instead).
/// Most have a number of uses a day (a used-up one fills again a day after its last use, FUN_000f5628, or at once
/// for two vials, TombstoneCommandBar); the raids use them (strength, energy, the golden revive, the yeti), the
/// Grim Reaper doubles what serving teaches, and the Super Server makes cooking teach a tenth more, rest faster,
/// and when switched on works the staff harder for two hours.
/// </summary>
public sealed class Boosts
{
    /// <summary>One slot of DAT_0042cd8c (0x14 bytes): the tombstone, whether it has uses, how many a day, timed, how long.</summary>
    public sealed record Kind(int Furniture, bool Rechargeable, int Charges, bool Timed, int DurationS);

    public const int Strength = 0, Health = 1, Strength2 = 2, Health2 = 3, Golden = 4, GrimReaper = 5, Yeti = 6, SuperServer = 7;

    /// <summary>FUN_000f57f0's table (furniture ids 285, 286, 303, 304, 485, 565, 619, 654).</summary>
    public static readonly Kind[] Table =
    [
        new(285, true, 5, false, 0),
        new(286, true, 5, false, 0),
        new(303, true, 7, false, 0),
        new(304, true, 7, false, 0),
        new(485, true, 1, false, 0),
        new(565, false, 0, false, 0),
        new(619, true, 3, false, 0),
        new(654, true, 1, true, 7200),
    ];

    /// <summary>+0: 0 not owned, 1 in the storage box, 2 in the cafe.</summary>
    public byte[] Placement { get; } = new byte[8];
    /// <summary>+4: uses left today.</summary>
    public int[] Charges { get; } = new int[8];
    /// <summary>+8: when it was last used (Unix seconds).</summary>
    public long[] LastUseUnix { get; } = new long[8];
    /// <summary>+0x10: a timed one is running.</summary>
    public bool[] Active { get; } = new bool[8];

    static long Now => GameClock.UnixSeconds;

    /// <summary>FUN_000f5374: a new game - every slot full, never used.</summary>
    public Boosts()
    {
        for (int i = 0; i < 8; i++)
            Charges[i] = Table[i].Charges;
    }

    /// <summary>FUN_000f53f0: the slot of a tombstone (-1: none).</summary>
    public static int SlotOf(int furniture) => Array.FindIndex(Table, k => k.Furniture == furniture);

    public bool Placed(int slot) => Placement[slot] == 2;

    /// <summary>A placed tombstone with a use left.</summary>
    public bool Usable(int slot) => Placed(slot) && Charges[slot] > 0;

    /// <summary>FUN_000b4524: what stands in the cafe and what is in the storage box.</summary>
    public void Refresh(GameData data, Cafe.Cafe cafe)
    {
        Array.Clear(Placement);
        foreach (var o in cafe.Objects)
            if (o is Cafe.Furniture f && f.Cell >= 0 && f.Def.Category == FurnitureCategory.Tombstone && SlotOf(f.Def.Id) is >= 0 and var s)
                Placement[s] = 2;
        foreach (int id in cafe.Stored)
            if (id >= 0 && id < data.Furniture.Count && data.Furniture[id].Category == FurnitureCategory.Tombstone && SlotOf(id) is >= 0 and var s)
                Placement[s] = 1;
    }

    /// <summary>FUN_000f5484: a tombstone bought comes with its full uses.</summary>
    public void Refill(int furniture)
    {
        int s = SlotOf(furniture);
        if (s >= 0)
            Charges[s] = Table[s].Charges;
    }

    /// <summary>FUN_000f55f4 + FUN_000f550c(-1): one use, from now (a timed one starts its time).</summary>
    public void Use(int slot)
    {
        LastUseUnix[slot] = Now;
        Active[slot] = Table[slot].Timed && Remaining(slot) > 0;
        Charges[slot]--;
    }

    /// <summary>FUN_000f55ac: seconds a timed one has left.</summary>
    public long Remaining(int slot) => Table[slot].DurationS - (Now - LastUseUnix[slot]);

    /// <summary>A timed one is running (FUN_000f54c4(7) == 2 and FUN_000f55ac(7) > 0).</summary>
    public bool Running(int slot) => Placed(slot) && Table[slot].Timed && Remaining(slot) > 0;

    /// <summary>
    /// FUN_000f5628 (cafe load and every frame): a used-up one fills again a day (plus its running time) after its
    /// last use.
    /// </summary>
    public void Recharge()
    {
        long now = Now;
        for (int i = 0; i < 8; i++)
        {
            var k = Table[i];
            if (!k.Rechargeable || Charges[i] != 0)
                continue;
            if (now - LastUseUnix[i] - 86400 - k.DurationS >= 0)
            {
                LastUseUnix[i] = now;
                Charges[i] = k.Charges;
            }
        }
    }

    /// <summary>FUN_000f56c4: a timed one whose time ran out stops (true: the staff need their stats back).</summary>
    public bool Expire()
    {
        bool any = false;
        for (int i = 0; i < 8; i++)
        {
            if (Table[i].Timed && Active[i] && Remaining(i) < 1)
            {
                Active[i] = false;
                any = true;
            }
        }
        return any;
    }
}
