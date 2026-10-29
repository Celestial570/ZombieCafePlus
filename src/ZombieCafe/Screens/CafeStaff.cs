using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The staff panel (J, or the STAFF button; a Two Point extra): the chef and every zombie in a list with level,
/// energy and job, and their orders in one place - SERVE, COOK (to the nearest stove that needs someone), REST /
/// WORK, an auto break that rests a zombie when it runs low and sends it back when full, and FIND to pick them
/// out in the cafe. The cafe keeps running while it is open.
/// </summary>
public sealed partial class CafeScreen
{
    bool staffOpen;
    float staffScroll;
    string? staffNote;
    float staffNoteTime;
    // where each of the panel's buttons was last drawn (test scripts click "panel:<key>")
    readonly Dictionary<string, Rectangle> staffRects = new();

    const float StaffRowH = 46, StaffW = 880;

    bool StaffButtonShows => game.Hints.TutorialDone && !edit.Active && !RecipesOpen && game.Selected == null
        && stoveBar == null && specialBar == null && tombstoneBar == null && bio == null && !fridge.Visible && !questsOpen && !reviewOpen;

    void ToggleStaff()
    {
        staffOpen = !staffOpen;
        staffScroll = 0;
        game.Sound(36);
    }

    /// <summary>J opens and closes the panel; Esc closes it.</summary>
    void StaffKeys(float dt)
    {
        if (staffNoteTime > 0)
            staffNoteTime -= dt;
        if (!game.Hints.TutorialDone || edit.Active || chefMessage.Visible)
        {
            staffOpen = false;
            return;
        }
        if (Input.IsKeyPressed(KeyboardKey.J))
            ToggleStaff();
        else if (staffOpen && Input.IsKeyPressed(KeyboardKey.Escape))
            staffOpen = false;
    }

    void StaffSay(string text)
    {
        staffNote = text;
        staffNoteTime = 3f;
    }

    bool StaffButton(string key, Rectangle r, string text, bool enabled = true)
    {
        staffRects[key] = r;
        return Widgets.Button(r, text, 16, enabled);
    }

    /// <summary>The STAFF button at the bottom right, while nothing else is using the bottom of the screen.</summary>
    void DrawStaffButton()
    {
        if (!StaffButtonShows)
            return;
        if (StaffButton("open", new Rectangle(Screen.Width - 140, Screen.Height - 84, 124, 44), staffOpen ? "STAFF  x" : "STAFF (J)"))
            ToggleStaff();
    }

    void DrawStaff()
    {
        var people = new List<CharacterInstance> { game.ChefActor };
        people.AddRange(game.Staff);
        float top = data.IngameUi[UiSprites.Awning].FullHeight + 8;
        int visible = Math.Max(3, (int)((Screen.Height - top - 200) / StaffRowH));
        int rows = Math.Min(visible, people.Count);
        float h = 104 + rows * StaffRowH + 34;
        var r = new Rectangle(Screen.Width / 2 - StaffW / 2, top, StaffW, h);
        Widgets.Panel(r);
        Widgets.Label("STAFF", r.X + 16, r.Y + 10, 28, Anchor.Top | Anchor.Left, Widgets.Gold);
        int serving = people.Count(p => p.Character.Job == Job.Serving), cooking = people.Count(p => p.Character.Job == Job.Cooking),
            resting = people.Count(p => p.Character.Job == Job.Resting), down = people.Count(p => p.IsKnockedOut || p.Character.ReanimateMs >= 1);
        Widgets.Label($"Serving {serving}   Cooking {cooking}   Resting {resting}   Down {down}   Idle {people.Count - serving - cooking - resting - down}",
            r.X + 120, r.Y + 18, 18, Anchor.Top | Anchor.Left, new Color(220, 220, 220, 255));
        if (StaffButton("close", new Rectangle(r.X + r.Width - 52, r.Y + 10, 38, 34), "X"))
        {
            staffOpen = false;
            game.Sound(36);
            return;
        }
        // column heads
        float hy = r.Y + 66;
        var head = new Color(200, 190, 150, 255);
        Widgets.Label("NAME", r.X + 16, hy, 15, Anchor.Top | Anchor.Left, head);
        Widgets.Label("ENERGY", r.X + 222, hy, 15, Anchor.Top | Anchor.Left, head);
        Widgets.Label("JOB", r.X + 348, hy, 15, Anchor.Top | Anchor.Left, head);
        Widgets.Label("ORDERS", r.X + 472, hy, 15, Anchor.Top | Anchor.Left, head);
        Widgets.Label("AUTO BREAK", r.X + 706, hy, 15, Anchor.Top | Anchor.Left, head);

        int maxScroll = Math.Max(0, people.Count - visible);
        if (Widgets.Hover(r))
            staffScroll = Math.Clamp(staffScroll - Input.GetMouseWheelMove(), 0, maxScroll);
        int first = (int)staffScroll;
        float y = r.Y + 96;
        for (int i = first; i < Math.Min(people.Count, first + visible); i++, y += StaffRowH)
            if (DrawStaffRow(people[i], i, r.X, y))
                return;
        if (maxScroll > 0)
            Widgets.Label($"{first + 1}-{Math.Min(people.Count, first + visible)} of {people.Count} (scroll for more)",
                r.X + 16, r.Y + r.Height - 28, 15, Anchor.Top | Anchor.Left, head);
        if (staffNoteTime > 0 && staffNote is { Length: > 0 })
            Widgets.Label(staffNote, r.X + r.Width - 16, r.Y + r.Height - 28, 16, Anchor.Top | Anchor.Right, Widgets.Gold);
    }

    /// <summary>One person's row. True when the panel was closed by it (FIND).</summary>
    bool DrawStaffRow(CharacterInstance w, int i, float x, float y)
    {
        var c = w.Character;
        bool chef = w == game.ChefActor;
        bool isDown = w.IsKnockedOut || c.ReanimateMs >= 1;
        if (i % 2 == 1)
            Gfx.Draw.Rect(new Rectangle(x + 8, y - 4, StaffW - 16, StaffRowH - 2), new Color(255, 255, 255, 18));
        string name = c.Name.Length > 14 ? c.Name[..13] + "." : c.Name;
        Widgets.Label(name, x + 16, y + 2, 20, Anchor.Top | Anchor.Left, chef ? Widgets.Gold : Color.White);
        Widgets.Label((chef ? "Chef" : "Lv " + (c.Level + 1)), x + 16, y + 24, 14, Anchor.Top | Anchor.Left, new Color(190, 190, 190, 255));
        // energy
        float max = c.MaxEnergy(data);
        float frac = max > 0 ? c.Energy / max : 0;
        var barColor = frac <= 0.25f ? new Color(230, 70, 60, 255) : frac <= 0.5f ? new Color(240, 190, 60, 255) : new Color(90, 200, 90, 255);
        Widgets.Bar(new Rectangle(x + 222, y + 8, 110, 18), frac, barColor);
        Text.DrawAnchored($"{(int)c.Energy}/{(int)max}", x + 277, y + 17, 13, Anchor.Center, Color.White, Color.Black, 2);
        // job
        string job = isDown ? "Down" : game.JobText(w).Split('\n')[0];
        if (job.Length == 0)
            job = "Idle";
        Widgets.Label(job, x + 348, y + 8, 17);
        // orders
        float bx = x + 472, by = y + 2, bw = 72, bh = 34;
        if (StaffButton("serve" + i, new Rectangle(bx, by, bw, bh), "SERVE", !isDown && c.Job != Job.Serving))
        {
            game.Sound(36);
            game.AssignServing(w);
        }
        if (StaffButton("cook" + i, new Rectangle(bx + 78, by, bw, bh), "COOK", !isDown))
        {
            game.Sound(36);
            if (game.SendToStove(w) is { Length: > 0 } why)
                StaffSay(why);
        }
        if (!chef)
        {
            bool resting = c.Job == Job.Resting;
            if (StaffButton("rest" + i, new Rectangle(bx + 156, by, bw, bh), resting ? "WORK" : "REST", !isDown))
            {
                game.Sound(36);
                if (resting)
                    game.Work(w);
                else
                    game.Rest(w);
            }
            if (StaffButton("auto" + i, new Rectangle(x + 706, by, 76, bh), c.AutoBreak ? "ON" : "OFF"))
            {
                game.Sound(36);
                c.AutoBreak = !c.AutoBreak;
            }
            if (c.AutoBreak)
                Gfx.Draw.Rect(new Rectangle(x + 706, by + bh - 4, 76, 4), new Color(120, 255, 120, 255));
        }
        if (StaffButton("find" + i, new Rectangle(x + StaffW - 84, by, 70, bh), "FIND", w.Visible))
        {
            game.Sound(36);
            game.Selected = w;
            camera = new Vector2(w.X, w.Y - 60);
            staffOpen = false;
            return true;
        }
        return false;
    }
}
