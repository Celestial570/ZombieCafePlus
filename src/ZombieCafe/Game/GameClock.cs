namespace ZombieCafe.Game;

/// <summary>
/// The game's clock: the computer's, plus however far the cheat menu has moved it on (kept in the save, so the
/// game's days stay in order). Everything that goes by the date or time of day - the Frequent Fryer, the magic
/// fridges, the Amptron's summons, the tombstones, the quests' bonus - reads it.
/// </summary>
public static class GameClock
{
    /// <summary>How far ahead of the computer's clock the game is, in milliseconds.</summary>
    public static long OffsetMs { get; set; }

    public static DateTime Now => DateTime.Now.AddMilliseconds(OffsetMs);
    public static DateTime Today => Now.Date;
    public static long UnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + OffsetMs;
    public static long UnixSeconds => UnixMs / 1000;
}
