using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>
/// The hint manager (game +0x2c0): the tutorial and the tips that come later are 92 numbered hints. Game
/// events trigger an id (FUN_0014cb88) and every hint with that trigger not yet shown is queued. The head of
/// the queue is looked at once a second, or at once when it was urgent or is a silent step (FUN_0014cc78):
/// if the hint it depends on has been shown, the current screen has its say (the cafe's is FUN_000b6814,
/// which also points the tutorial arrow), and then the hint counts as shown and its text goes up in the
/// message box.
/// </summary>
public sealed class HintManager
{
    public const int Count = 0x5c;

    readonly bool[] shown = new bool[Count];
    // +0x5d0: queued hints and whether each was urgent
    readonly List<(int Hint, bool Urgent)> queue = new();
    // +0x5c4
    int timer;

    /// <summary>+0x5cc: the current screen's say over a hint, by hint number (true: show it now).</summary>
    public Func<int, bool>? Check { get; set; }
    /// <summary>+0x5c0: puts a hint's text in the message box.</summary>
    public Action<string>? Show { get; set; }
    /// <summary>The message box is up; the queue waits until it is dismissed.</summary>
    public Func<bool>? Busy { get; set; }

    /// <summary>FUN_0014cb68.</summary>
    public bool IsShown(int hint) => shown[hint];
    /// <summary>FUN_0014cb70.</summary>
    public void MarkShown(int hint) => shown[hint] = true;
    /// <summary>FUN_0014cb7c.</summary>
    public void Reset(int hint) => shown[hint] = false;
    /// <summary>Hints are waiting their turn (level-ups wait for them, FUN_000d1f20).</summary>
    public bool Pending => queue.Count > 0;

    /// <summary>Hint 0x16 ("open for business") ends the tutorial; much of the game checks it.</summary>
    public bool TutorialDone => shown[0x16];

    /// <summary>FUN_0014cb88: queue every unseen hint that waits for this event.</summary>
    public void Trigger(int trigger, bool urgent)
    {
        for (int i = 0; i < Count; i++)
        {
            if (HintTable.All[i].Trigger != trigger || shown[i] || queue.Exists(q => q.Hint == i))
                continue;
            queue.Add((i, urgent));
        }
    }

    /// <summary>Holds the next hint back a little longer (the original does this while furniture is dragged).</summary>
    public void Delay() => timer = 0;

    /// <summary>FUN_0014cc78, every frame.</summary>
    public void Update(int dtMs, GameData data)
    {
        if (Check == null || (Busy?.Invoke() ?? false) || queue.Count == 0)
            return;
        timer += dtMs;
        var (i, urgent) = queue[0];
        var e = HintTable.All[i];
        if (!urgent && timer <= data.K.Int(0x42d584) && e.Text >= 0)
            return;
        timer = 0;
        queue.RemoveAt(0);
        bool ready = e.After < 0 || shown[e.After] || (e.Alt >= 0 && shown[e.Alt]);
        if (!ready || !Check(i))
            return;
        shown[i] = true;
        if (e.Text >= 0)
            Show?.Invoke(data.Strings[e.Text]);
    }

    /// <summary>The shown flags, for the save (FUN_0014cbf8 writes one byte per hint).</summary>
    public bool[] Save() => (bool[])shown.Clone();

    /// <summary>FUN_0014cc20.</summary>
    public void Load(bool[]? flags)
    {
        Array.Clear(shown);
        queue.Clear();
        if (flags != null)
            Array.Copy(flags, shown, Math.Min(flags.Length, Count));
    }
}
