namespace ZombieCafe.Game;

/// <summary>How the original writes a length of time (FUN_0003e6bc).</summary>
public static class Durations
{
    /// <summary>Style 2, one part: the largest unit only - " 4h", " 12m", " 30s" (with the original's leading space).</summary>
    public static string Short(int seconds)
    {
        int s = Math.Max(0, seconds);
        (int Value, string Unit) part = s >= 86400 ? (s / 86400, "d") : s >= 3600 ? (s / 3600, "h") : s >= 60 ? (s / 60, "m") : (s, "s");
        return part.Value != 0 ? " " + part.Value + part.Unit : " ";
    }
}
