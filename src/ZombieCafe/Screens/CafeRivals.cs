using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The rival across the street, as the cafe sees it: half a minute after it starts stealing customers
/// (GameStateCafe::update, K 0x3d86b4) the EnemyCafeDialog (FUN_0016e4e0, vf02 0016f484) says so - its picture in a
/// frame, "A Competitor is Stealing Your Customers!", its name, and "Raid Now!".
/// </summary>
public sealed partial class CafeScreen
{
    // strings.bin
    const int TextStealingTitle = 344, TextStealingBody = 345, TextRaidNow = 346;
    // ipad_ingameUi: the picture frame, its clip, the close cross, the green button
    const int UiFrame = 76, UiFrameClip = 40, UiGreenButton = 10;
    const float StealingTitleY = 20, StealingMargin = 10;   // K 0x3d89bc (iPad), the frame's and the button's gaps
    const int StealingDelayMs = 30000;                       // K 0x3d86b4

    /// <summary>The raid button's way into a raid (rival's name, level, kind); none: no raids.</summary>
    public Action<string, int, int>? Raid { get; init; }

    /// <summary>+0xc8: the rival the dialog is about (its level), or none.</summary>
    int? competitor;
    int stealingClock;    // +0x1fc
    Rectangle? raidNowRect;

    /// <summary>GameStateCafe::update: the stealing rival is announced once, half a minute in.</summary>
    void WatchRivals(int dtMs)
    {
        var n = game.Player.Neighbours;
        if (!n.Stealing || Raid == null)
            return;
        stealingClock += dtMs;
        if (stealingClock < StealingDelayMs)
            return;
        n.Stealing = false;
        stealingClock = 0;
        competitor = game.Player.Level;
    }

    void DrawCompetitor()
    {
        int level = competitor!.Value;
        var n = game.Player.Neighbours;
        int type = n.TypeOf(data, level);
        var dialog = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        var ui = data.IngameUi;
        var board = dialog.Length > 2 ? dialog[2] : null;
        float w = board?.FullWidth ?? 485, h = board?.FullHeight ?? 327;
        var o = new Vector2((Screen.Width - w) / 2, (Screen.Height - h) / 2);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 120));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        board?.Draw(o.X, o.Y, Anchor.Top | Anchor.Left, Color.White);

        // the title across the top, clear of the close cross
        var close = ui[UiSprites.CloseX];
        float titleSize = Text.GameFont[4];
        var title = Widgets.WrapText(data.Strings[TextStealingTitle], w - 20 - close.FullWidth, titleSize);
        for (int i = 0; i < title.Count; i++)
            Text.DrawAnchored(title[i], o.X + w / 2, o.Y + StealingTitleY + i * titleSize, titleSize, Anchor.Top | Anchor.HCenter, Color.White);
        float top = o.Y + StealingTitleY + title.Count * titleSize;

        // the rival's picture in a frame (FUN_0016ef20: three quarters of the frame at most), the clip on top
        var frame = ui[UiFrame];
        var frameAt = new Vector2(o.X + StealingMargin, top);
        frame.Draw(frameAt.X, frameAt.Y, Anchor.Top | Anchor.Left, Color.White);
        var pictures = data.Sheet("cafeUiImages", "cafeUiOffsets.bin");
        int pic = data.EnemyCafes[type].Picture;
        if (pic >= 0 && pic < pictures.Length)
        {
            var p = pictures[pic];
            float fit = MathF.Min(frame.FullWidth * 0.75f / p.FullWidth, frame.FullHeight * 0.75f / p.FullHeight);
            p.Draw(frameAt.X + frame.FullWidth / 2f, frameAt.Y + frame.FullHeight / 2f, Anchor.Center, Color.White, Flip.None, fit, fit);
        }
        ui[UiFrameClip].Draw(frameAt.X + frame.FullWidth / 2f, frameAt.Y, Anchor.Top | Anchor.HCenter, Color.White);

        // "<name> has established a thriving business ...", centred in the room right of the frame
        float bodySize = Text.GameFont[0], bodyW = w - frame.FullWidth - 30;
        var body = Widgets.WrapText(n.NameOf(data, type, level) + " " + data.Strings[TextStealingBody], bodyW, bodySize);
        float bodyX = frameAt.X + frame.FullWidth + bodyW / 2, bodyY = frameAt.Y + frame.FullHeight / 2f - body.Count * bodySize / 2;
        for (int i = 0; i < body.Count; i++)
            Text.DrawAnchored(body[i], bodyX, bodyY + i * bodySize, bodySize, Anchor.Top | Anchor.HCenter, Color.White);

        // close, or raid now
        if (Widgets.ImageButton(close, o.X + w - close.FullWidth / 2f, o.Y + close.FullHeight / 2f) || Input.IsKeyPressed(KeyboardKey.Escape))
        {
            game.Sound(36);
            competitor = null;
            return;
        }
        var button = ui[UiGreenButton];
        var at = new Vector2(o.X + w / 2, o.Y + h - StealingMargin - button.FullHeight / 2f);
        raidNowRect = new Rectangle(at.X - button.FullWidth / 2f, at.Y - button.FullHeight / 2f, button.FullWidth, button.FullHeight);
        bool pressed = Widgets.ImageButton(button, at.X, at.Y);
        Text.DrawAnchored(data.Strings[TextRaidNow], at.X, at.Y, bodySize, Anchor.Center, Color.White);
        // FUN_000581c8: only with someone to send
        if (pressed && game.Staff.Any(z => z.Character.Job != Job.Other))
        {
            game.Sound(36);
            competitor = null;
            Raid?.Invoke(n.NameOf(data, type, level), level, type);
        }
    }
}
