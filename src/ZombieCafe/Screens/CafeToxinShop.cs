using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The toxin exchange (a Two Point extra, behind the + by the toxin count, where the original opened its App Store
/// toxin shop): vials for cash at the settings' price, up to the settings' number a day. Its price and limit are in
/// settings.json (ToxinPrice, ToxinDailyLimit).
/// </summary>
public sealed partial class CafeScreen
{
    bool toxinShopOpen;
    Rectangle? toxinPlusRect;
    string? toxinNote;
    // where each of its buttons was last drawn (test scripts click "toxin:<key>")
    readonly Dictionary<string, Rectangle> toxinRects = new();

    void OpenToxinShop()
    {
        game.Sound(36);
        toxinNote = null;
        toxinShopOpen = true;
    }

    void DrawToxinShop()
    {
        toxinRects.Clear();
        var ui = data.IngameUi;
        var s = Settings.Current;
        int left = game.ToxinLeftToday;
        bool limited = left != int.MaxValue;
        const float w = 500, h = 290;
        var r = new Rectangle(Screen.Width / 2 - w / 2, Screen.Height / 2 - h / 2 - 40, w, h);
        Widgets.Panel(r);
        Widgets.Label("BUY TOXIN", r.X + w / 2, r.Y + 16, Text.GameFont[4], Anchor.Top | Anchor.HCenter, Widgets.Gold);
        Widgets.Sprite(ui[UiSprites.VialMedium], r.X + 60, r.Y + 110, 1.6f);
        Widgets.Label($"${s.ToxinPrice:N0} a vial", r.X + 110, r.Y + 70, Text.GameFont[2]);
        string today = !limited ? "No daily limit."
            : left == 0 ? "You've bought all you can today. More tomorrow."
            : $"{left} of {s.ToxinDailyLimit} left to buy today.";
        Widgets.Label(today, r.X + 110, r.Y + 106, Text.GameFont[1], Anchor.Top | Anchor.Left, left == 0 ? new Color(255, 170, 150, 255) : Color.White);
        Widgets.Label($"You have {game.Player.Toxin} toxin and ${game.Player.Money:N0}.", r.X + 110, r.Y + 138, Text.GameFont[0],
            Anchor.Top | Anchor.Left, new Color(210, 210, 210, 255));
        // buy 1, 5 (or what is left), and close
        int five = Math.Min(5, left);
        float by = r.Y + 182, bw = 140, bh = 46;
        var one = new Rectangle(r.X + 24, by, bw, bh);
        var many = new Rectangle(r.X + 24 + bw + 12, by, bw, bh);
        var close = new Rectangle(r.X + w - 24 - 130, by, 130, bh);
        toxinRects["buy1"] = one;
        toxinRects["buy5"] = many;
        toxinRects["close"] = close;
        if (Widgets.Button(one, "Buy 1", Text.GameFont[1], left > 0 && game.Player.Money >= s.ToxinPrice))
            toxinNote = game.BuyToxin(1) ?? "Bought 1 vial.";
        if (five > 1 && Widgets.Button(many, $"Buy {five}", Text.GameFont[1], game.Player.Money >= (long)s.ToxinPrice * five))
            toxinNote = game.BuyToxin(five) ?? $"Bought {five} vials.";
        if (Widgets.Button(close, "Close", Text.GameFont[1]))
        {
            toxinShopOpen = false;
            game.Sound(36);
        }
        if (toxinNote is { Length: > 0 })
            Widgets.Label(toxinNote, r.X + w / 2, r.Y + h - 40, Text.GameFont[0], Anchor.Top | Anchor.HCenter, Widgets.Gold);
        else if (game.Player.Money < s.ToxinPrice)
            Widgets.Label(data.Strings[441], r.X + w / 2, r.Y + h - 40, Text.GameFont[0], Anchor.Top | Anchor.HCenter, new Color(255, 170, 150, 255));
    }
}
