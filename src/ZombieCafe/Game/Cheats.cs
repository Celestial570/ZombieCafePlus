namespace ZombieCafe.Game;

/// <summary>Session-wide settings the cheat menu changes (a port addition); ZC_SPEED sets the starting speed for tests.</summary>
public static class Cheats
{
    /// <summary>How many times as fast the game runs - the cafe and raids alike.</summary>
    public static float Speed { get; set; } =
        float.TryParse(Environment.GetEnvironmentVariable("ZC_SPEED"), out var s) && s > 0 ? s : 1f;

    // the Toggles tab: on until switched off or the game is closed (not saved)
    /// <summary>Staff (and raiders) never tire.</summary>
    public static bool InfiniteEnergy { get; set; }
    /// <summary>The store charges nothing.</summary>
    public static bool FreeShopping { get; set; }
    /// <summary>Store items above the cafe's level can be bought.</summary>
    public static bool NoLevelLocks { get; set; }
    /// <summary>Whatever goes on a stove is done at once.</summary>
    public static bool InstantCooking { get; set; }
    /// <summary>Food left on a stove never burns.</summary>
    public static bool NoBurning { get; set; }
    /// <summary>Hungry zombies never go for the customers.</summary>
    public static bool PeacefulZombies { get; set; }
    /// <summary>Zombies never stop to daydream.</summary>
    public static bool NoDaydreaming { get; set; }
    /// <summary>Everyone walking by comes in, and four times as many walk by.</summary>
    public static bool BusyStreet { get; set; }
    /// <summary>Seated customers wait for their food however long it takes.</summary>
    public static bool PatientCustomers { get; set; }
    /// <summary>Cash items, toxin generators and the like empty themselves into the player's pockets.</summary>
    public static bool AutoCollect { get; set; }
    /// <summary>The cafe stands still (building and the menus still work).</summary>
    public static bool FreezeCafe { get; set; }
}
