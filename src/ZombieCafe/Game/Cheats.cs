namespace ZombieCafe.Game;

/// <summary>Session-wide settings the cheat menu changes (a port addition); ZC_SPEED sets the starting speed for tests.</summary>
public static class Cheats
{
    /// <summary>How many times as fast the game runs - the cafe and raids alike.</summary>
    public static float Speed { get; set; } =
        float.TryParse(Environment.GetEnvironmentVariable("ZC_SPEED"), out var s) && s > 0 ? s : 1f;
}
