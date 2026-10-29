using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// TombstoneCommandBar (vf02 001b546c): a tombstone in the cafe tapped shows its name and its uses left today; while
/// it is short of uses, RECHARGE (the clock, ui 187) fills it for two vials ("Need more tombstone energy?", string
/// 649; full already: 651); the Super Server's ACTIVATE (ui 212) starts its two hours (string 635 asks first), and
/// while they run the bar counts them down.
/// </summary>
public sealed partial class CafeScreen
{
    const int TextActivateAsk = 635, TextNeedEnergy = 649, TextNotNeeded = 651;
    const int UiRecharge = 187, UiActivate = 212;

    /// <summary>The tombstone whose bar is up.</summary>
    Furniture? tombstoneBar;
    Rectangle? rechargeRect, activateRect;

    void DrawTombstoneBar(Furniture t)
    {
        rechargeRect = activateRect = null;
        if (!cafe.Objects.Contains(t) || t.Cell < 0)
        {
            tombstoneBar = null;
            return;
        }
        var ui = data.IngameUi;
        var boosts = game.Player.Boosts;
        int slot = Boosts.SlotOf(t.Def.Id);
        float bw = 560, bh = 96;
        var r = new Rectangle((Screen.Width - bw) / 2, Screen.Height - bh - 40, bw, bh);
        Widgets.Panel(r);
        Widgets.Label(t.Def.Name, r.X + 16, r.Y + 10, 26, Anchor.Top | Anchor.Left, Widgets.Gold);
        if (slot < 0 || !Boosts.Table[slot].Rechargeable)
        {
            // a decorative one (or the Grim Reaper, which simply works where it stands)
            var lines = Widgets.WrapText(t.Def.Description, bw - 32, 18);
            for (int i = 0; i < Math.Min(2, lines.Count); i++)
                Widgets.Label(lines[i], r.X + 16, r.Y + 44 + i * 20, 18);
            return;
        }
        var kind = Boosts.Table[slot];
        int left = boosts.Charges[slot];
        float bx = r.X + bw - 70, by = r.Y + bh / 2;
        if (kind.Timed && boosts.Running(slot))
        {
            long s = Math.Max(0, boosts.Remaining(slot));
            Widgets.Label($"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}", r.X + 16, r.Y + 46, 24, Anchor.Top | Anchor.Left, new Color(140, 230, 140, 255));
            return;
        }
        Widgets.Label($"{left}/{kind.Charges}", r.X + 16, r.Y + 44, 22);
        if (left < kind.Charges)
        {
            var lines = Widgets.WrapText(data.Strings[TextNeedEnergy], 300, 16);
            for (int i = 0; i < Math.Min(2, lines.Count); i++)
                Widgets.Label(lines[i], r.X + 70, r.Y + 46 + i * 18, 16);
            var clock = ui[UiRecharge];
            float s = 0.8f;
            rechargeRect = new Rectangle(bx - clock.FullWidth * s / 2, by - clock.FullHeight * s / 2, clock.FullWidth * s, clock.FullHeight * s);
            ui[UiSprites.VialSmall].Draw(bx - 70, by, Anchor.Center, Color.White);
            Widgets.Label(game.RechargeCost.ToString(), bx - 58, by, 22, Anchor.Left | Anchor.VCenter);
            if (Widgets.ImageButton(clock, bx, by, s))
                RechargePressed(t, slot);
        }
        else if (kind.Timed && left > 0)
        {
            var activate = ui[UiActivate];
            float ax = r.X + bw - activate.FullWidth / 2f - 16;
            activateRect = new Rectangle(ax - activate.FullWidth / 2f, by - activate.FullHeight / 2f, activate.FullWidth, activate.FullHeight);
            if (Widgets.ImageButton(activate, ax, by))
            {
                game.Sound(40);
                question = (data.Strings[TextActivateAsk], data.Strings[461], () => game.ActivateTombstone(t));
            }
        }
    }

    /// <summary>TombstoneCommandBar: full already (651), short of toxin (320), or "use 2 toxin" (317/318, 319).</summary>
    void RechargePressed(Furniture t, int slot)
    {
        game.Sound(40);
        if (game.Player.Boosts.Charges[slot] == Boosts.Table[slot].Charges)
        {
            messages.Enqueue(("", new List<string> { data.Strings[TextNotNeeded] }, null));
            return;
        }
        if (game.Player.Toxin < game.RechargeCost)
        {
            messages.Enqueue(("", new List<string> { data.Strings[320] }, null));
            return;
        }
        question = (data.Strings[317] + game.RechargeCost + data.Strings[318], data.Strings[319], () => game.RechargeTombstone(t));
    }
}
