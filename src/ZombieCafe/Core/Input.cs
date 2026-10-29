using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace ZombieCafe.Core;

/// <summary>
/// Mouse and keyboard for the whole game. Normally a thin layer over raylib; with ZC_INPUT=&lt;script&gt; it
/// replays a script instead (one event per line: "&lt;frame&gt; move x y | click x y | press left|right |
/// release left|right | key NAME | wheel n"), so UI flows can be tested end to end with screenshots.
/// </summary>
public static class Input
{
    sealed record Event(int Frame, string Cmd, string[] Args);

    static List<Event>? script;
    static int frame;
    static Vector2 mouse;
    static readonly bool[] down = new bool[3], pressed = new bool[3], released = new bool[3];
    static readonly HashSet<KeyboardKey> keysPressed = new();
    static float wheel;
    static readonly List<(int Frame, int Button)> pendingRelease = new();

    public static bool Scripted => script != null;

    /// <summary>Set by the cafe screen: window pixel position of the cell (originU + du, originV + dv), for "cell du dv".</summary>
    public static Func<int, int, Vector2?>? CellToPixels { get; set; }
    /// <summary>Set by the cafe screen: where a named thing is on screen now ("chef", "stove", ...), for "clickat name".</summary>
    public static Func<string, Vector2?>? TargetToPixels { get; set; }

    public static void Load(string path)
    {
        script = new List<Event>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
                continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out int f))
                script.Add(new Event(f, parts[1].ToLowerInvariant(), parts[2..]));
        }
    }

    static int Button(string name) => name.ToLowerInvariant() switch { "right" => 1, "middle" => 2, _ => 0 };

    /// <summary>Call once per frame before the screen updates.</summary>
    public static void BeginFrame()
    {
        frame++;
        if (script == null)
            return;
        Array.Clear(pressed);
        Array.Clear(released);
        keysPressed.Clear();
        wheel = 0;
        for (int i = pendingRelease.Count - 1; i >= 0; i--)
            if (pendingRelease[i].Frame <= frame)
            {
                int b = pendingRelease[i].Button;
                down[b] = false;
                released[b] = true;
                pendingRelease.RemoveAt(i);
            }
        foreach (var e in script)
        {
            if (e.Frame != frame)
                continue;
            float F(int i) => float.Parse(e.Args[i], CultureInfo.InvariantCulture);
            switch (e.Cmd)
            {
                case "move":
                    mouse = new Vector2(F(0), F(1));
                    break;
                case "click":
                    mouse = new Vector2(F(0), F(1));
                    down[0] = pressed[0] = true;
                    pendingRelease.Add((frame + 1, 0));
                    break;
                case "cell":
                case "clickcell":
                    if (CellToPixels?.Invoke(int.Parse(e.Args[0]), int.Parse(e.Args[1])) is { } p)
                        mouse = p;
                    if (e.Cmd == "clickcell")
                    {
                        down[0] = pressed[0] = true;
                        pendingRelease.Add((frame + 1, 0));
                    }
                    break;
                case "at":
                case "clickat":
                    if (TargetToPixels?.Invoke(e.Args[0].ToLowerInvariant()) is { } t)
                        mouse = t;
                    else
                        Console.WriteLine($"[input] frame {frame}: no '{e.Args[0]}' to click");
                    if (e.Cmd == "clickat")
                    {
                        down[0] = pressed[0] = true;
                        pendingRelease.Add((frame + 1, 0));
                    }
                    break;
                case "press":
                    down[Button(e.Args[0])] = pressed[Button(e.Args[0])] = true;
                    break;
                case "release":
                    down[Button(e.Args[0])] = false;
                    released[Button(e.Args[0])] = true;
                    break;
                case "key":
                    if (Enum.TryParse<KeyboardKey>(e.Args[0], true, out var k))
                        keysPressed.Add(k);
                    break;
                case "wheel":
                    wheel += F(0);
                    break;
            }
        }
    }

    public static Vector2 MousePosition => script != null ? mouse : Raylib.GetMousePosition();

    public static bool IsMouseButtonPressed(MouseButton b) => script != null ? pressed[(int)b] : Raylib.IsMouseButtonPressed(b);
    public static bool IsMouseButtonDown(MouseButton b) => script != null ? down[(int)b] : Raylib.IsMouseButtonDown(b);
    public static bool IsMouseButtonReleased(MouseButton b) => script != null ? released[(int)b] : Raylib.IsMouseButtonReleased(b);
    public static float GetMouseWheelMove() => script != null ? wheel : Raylib.GetMouseWheelMove();
    public static bool IsKeyPressed(KeyboardKey k) => script != null ? keysPressed.Contains(k) : Raylib.IsKeyPressed(k);
    public static bool IsKeyDown(KeyboardKey k) => script != null ? keysPressed.Contains(k) : Raylib.IsKeyDown(k);
}
