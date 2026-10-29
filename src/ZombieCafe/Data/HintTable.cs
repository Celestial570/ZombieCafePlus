namespace ZombieCafe.Data;

/// <summary>The original's 92 hints: the tutorial steps and the tips shown later (FUN_00148958).</summary>
public static partial class HintTable
{
    /// <param name="Trigger">The game event that queues the hint (FUN_0014cb88).</param>
    /// <param name="After">The hint that must have been shown first (-1: none).</param>
    /// <param name="Alt">An alternative to <paramref name="After"/> (-1: none).</param>
    /// <param name="Text">strings.bin line of the message, or -1 for a silent tutorial step.</param>
    public readonly record struct Entry(int Trigger, int After, int Alt, int Text);
}
