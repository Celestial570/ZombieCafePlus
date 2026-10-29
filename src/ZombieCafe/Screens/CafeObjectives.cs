using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The bonus quests (HUD button 13): ObjectiveDialog (FUN_001a0198, vf02 001a3b24) - on a chalkboard (dialog sheet
/// #2) "NEXT CAFE BONUS:" and its stars, the TO DO note (ui 167) with the four tasks (a box to tick, the task, a
/// cyan bar and "done/to do", FINISH to tick it off once done), the cafe's paper (ui 163: the rating's stars, the
/// bonus part purple, and while a bonus runs "BONUS EXPIRES IN", the time and a purple bar), the last critic's
/// words, and BRIBE REVIEWER (two vials a task left, ui 35) - GET REVIEW once all four are ticked off. The review
/// (ObjectiveCompleteDialog, FUN_001a9040) says "BONUS ACHIEVED!", how many hours it lasts, and what the critic wrote.
/// </summary>
public sealed partial class CafeScreen
{
    // strings.bin
    const int TextNextBonus = 588, TextGoalMany = 589, TextGoalOne = 599, TextQuote = 610, TextCritic = 613,
        TextPercentDone = 617, TextExpiresIn = 618, TextAchieved = 619, TextLastsFor = 620, TextHours = 621,
        TextGetReview = 623, TextBribe = 628, TextNearMax = 629, TextYourCafe = 444, TextOk = 362;
    // ipad_ingameUi
    const int UiToDo = 167, UiCafePaper = 163, UiReviewPaper = 174, UiCritic = 161, UiBigStar = 162, UiBox = 165,
        UiTick = 166, UiCyanBar = 164, UiPurpleBar = 169, UiStarPurple = 171, UiRatingPurple = 170, UiRatingOn = 172,
        UiRatingOff = 173, UiFinish = 176, UiPin = 40;
    const float QuestScale = 1.3f;

    /// <summary>+0x148 / +0x1c: the quests' panel is open; the review is showing.</summary>
    bool questsOpen, reviewOpen;
    Rectangle? questButtonRect;
    readonly Rectangle?[] questFinishRects = new Rectangle?[4];

    static readonly Color Ink = new(30, 25, 25, 255);

    void OpenQuests()
    {
        game.Selected = null;
        questsOpen = true;
    }

    /// <summary>The words of a task: "Raid 2 enemy cafés", "Cook 3 dishes of Mystery Meat" (strings 589/599 + type).</summary>
    string GoalText(Objectives.Goal g)
    {
        string text = data.Strings[(g.Target == 1 ? TextGoalOne : TextGoalMany) + g.Type].Replace("#", g.Target.ToString());
        if (g.Type == Objectives.Cook && g.Param >= 0 && g.Param < data.Foods.Count)
            text += data.Foods[g.Param].Name;
        return text;
    }

    void DrawQuests()
    {
        var o = game.Player.Objectives;
        var ui = data.IngameUi;
        var sheet = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        var board = sheet.Length > 2 ? sheet[2] : null;
        float s = QuestScale, bw = (board?.FullWidth ?? 485) * s, bh = (board?.FullHeight ?? 327) * s;
        var r = new Rectangle(MathF.Round((Screen.Width - bw) / 2), MathF.Round((Screen.Height - bh) / 2), bw, bh);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 120));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        if (board != null)
            board.DrawAt(r.X, r.Y, r.Width, r.Height, Color.White);
        else
            Widgets.Panel(r);
        Vector2 P(float x, float y) => new(r.X + x * s, r.Y + y * s);

        // "NEXT CAFE BONUS:" and the stars the next review brings (three at most)
        float titleSize = Text.GameFont[3];
        var title = data.Strings[TextNextBonus];
        var at = P(18, 12);
        Text.Draw(title, at.X, at.Y, titleSize, Color.White, Color.Black, 2);
        float sx = at.X + Text.Measure(title, titleSize).W + 6;
        for (int i = 0; i < Math.Min(o.Level + 1, 3); i++)
            ui[UiStarPurple].Draw(sx + i * 32 * s, at.Y + titleSize / 2, Anchor.Left | Anchor.VCenter, Color.White, Flip.None, s, s);

        // the TO DO note: the four tasks
        var note = ui[UiToDo];
        var noteAt = P(10, 48);
        note.Draw(noteAt.X, noteAt.Y, Anchor.Top | Anchor.Left, Color.White, Flip.None, s, s);
        ui[UiPin].Draw(noteAt.X + note.FullWidth * s / 2, noteAt.Y, Anchor.Center, Color.White);
        float rowH = 43 * s, textSize = Text.GameFont[0];
        for (int i = 0; i < 4; i++)
        {
            questFinishRects[i] = null;
            if (i >= o.Goals.Count)
                continue;
            var g = o.Goals[i];
            var row = new Vector2(noteAt.X + 14 * s, noteAt.Y + 46 * s + i * rowH);
            ui[g.Claimed ? UiTick : UiBox].Draw(row.X + 6 * s, row.Y + 8 * s, Anchor.Center, Color.White, Flip.None, s, s);
            var lines = Widgets.WrapText(GoalText(g), 186 * s, textSize);
            int shown = Math.Min(2, lines.Count);
            for (int l = 0; l < shown; l++)
                Text.Draw(lines[l], row.X + 18 * s, row.Y + l * textSize, textSize, Ink);
            float barY = row.Y + shown * textSize + 6;
            if (g.Claimed)
                continue;
            if (g.IsComplete)
            {
                // ticked off with FINISH
                var finish = ui[UiFinish];
                var fc = new Vector2(row.X + 18 * s + finish.FullWidth * 0.8f * s / 2, barY + finish.FullHeight * 0.8f * s / 2 - 2);
                float fs = 0.8f * s;
                questFinishRects[i] = new Rectangle(fc.X - finish.FullWidth * fs / 2, fc.Y - finish.FullHeight * fs / 2, finish.FullWidth * fs, finish.FullHeight * fs);
                if (Widgets.ImageButton(finish, fc.X, fc.Y, fs) && !chefMessage.Visible)
                {
                    game.Sound(36);
                    g.Claimed = true;
                }
                continue;
            }
            // the cyan bar and "done/to do"
            var bar = ui[UiCyanBar];
            float full = bar.FullWidth * s, frac = Math.Clamp((float)g.Progress / Math.Max(1, g.Target), 0, 1);
            Gfx.Draw.Rect(new Rectangle(row.X + 18 * s, barY, full, bar.FullHeight * s), new Color(40, 40, 50, 120));
            if (frac > 0)
                bar.DrawAt(row.X + 18 * s, barY, full * frac, bar.FullHeight * s, Color.White);
            Text.Draw($"{g.Progress}/{g.Target}", row.X + 24 * s + full, barY - 4, textSize, Ink);
        }
        Text.DrawAnchored(o.ClaimedCount * 100 / Math.Max(1, o.Goals.Count) + data.Strings[TextPercentDone],
            noteAt.X + note.FullWidth * s / 2, noteAt.Y + (note.FullHeight - 8) * s, textSize, Anchor.Bottom | Anchor.HCenter, Ink);

        // the cafe's paper: the rating's stars (the bonus part purple) and the bonus clock
        var paper = ui[UiCafePaper];
        var paperAt = P(262, 50);
        float ps = s * 1.25f;
        paper.Draw(paperAt.X, paperAt.Y, Anchor.Top | Anchor.Left, Color.White, Flip.None, ps, ps);
        ui[UiPin].Draw(paperAt.X + paper.FullWidth * ps / 2, paperAt.Y, Anchor.Center, Color.White);
        float pcx = paperAt.X + paper.FullWidth * ps / 2;
        Text.DrawAnchored(data.Strings[TextYourCafe], pcx, paperAt.Y + 22 * s, Text.GameFont[1], Anchor.Top | Anchor.HCenter, Ink);
        DrawRatingStars(pcx, paperAt.Y + 56 * s, game.Player.EffectiveRating, o.RatingBonus(data));
        if (o.Remaining > 0)
        {
            Text.DrawAnchored(data.Strings[TextExpiresIn], pcx, paperAt.Y + 82 * s, textSize, Anchor.Top | Anchor.HCenter, Ink);
            int secs = o.Remaining / 1000;
            Text.DrawAnchored($"{secs / 3600:00}:{secs / 60 % 60:00}:{secs % 60:00}", pcx, paperAt.Y + 100 * s, Text.GameFont[2], Anchor.Top | Anchor.HCenter, Ink);
            var purple = ui[UiPurpleBar];
            float pw = purple.FullWidth * s, pf = Math.Clamp((float)o.Remaining / Math.Max(1, o.Total), 0, 1);
            float px = pcx - pw / 2, py = paperAt.Y + 132 * s;
            Gfx.Draw.Rect(new Rectangle(px, py, pw, purple.FullHeight * s), new Color(40, 40, 50, 120));
            if (pf > 0)
                purple.DrawAt(px, py, pw * pf, purple.FullHeight * s, Color.White);
        }

        // the last critic's words
        if (o.Level > 0)
        {
            int c = Math.Min(o.Level, 3) - 1;
            var quote = Widgets.WrapText(data.Strings[TextQuote + c], 200 * s, Text.GameFont[1]);
            float qy = paperAt.Y + (paper.FullHeight * ps) + 8;
            for (int l = 0; l < quote.Count; l++)
                Text.DrawAnchored(quote[l], pcx, qy + l * Text.GameFont[1], Text.GameFont[1], Anchor.Top | Anchor.HCenter, Color.White, Color.Black, 2);
            Text.DrawAnchored(data.Strings[TextCritic + c], pcx, qy + quote.Count * Text.GameFont[1] + 2, textSize,
                Anchor.Top | Anchor.HCenter, Widgets.Gold, Color.Black, 2);
        }

        // close
        if (Widgets.ImageButton(ui[UiSprites.CloseX], r.X + r.Width - 26, r.Y + 26, 0.9f)
            || (question == null && !chefMessage.Visible && Input.IsKeyPressed(KeyboardKey.Escape)))
        {
            game.Sound(36);
            questsOpen = false;
            return;
        }

        // BRIBE REVIEWER while tasks are left, GET REVIEW once all are ticked off (hidden while done ones wait for FINISH)
        bool review = o.AllClaimed;
        questButtonRect = null;
        if (o.AllComplete && !review)
            return;
        var button = ui[UiSprites.GreenBarWide];
        var bc = new Vector2(pcx, r.Y + r.Height - 34 * s);
        questButtonRect = new Rectangle(bc.X - button.FullWidth / 2f, bc.Y - button.FullHeight / 2f, button.FullWidth, button.FullHeight);
        bool pressed = Widgets.ImageButton(button, bc.X, bc.Y) && !chefMessage.Visible;
        Text.DrawAnchored(data.Strings[review ? TextGetReview : TextBribe], bc.X, bc.Y, Text.GameFont[2], Anchor.Center, Color.White, Color.Black, 2);
        if (!review)
        {
            int cost = o.BribeCost(data);
            float cx = bc.X - button.FullWidth / 2f - 10;
            ui[UiSprites.VialMedium].Draw(cx - 30, bc.Y, Anchor.Center, Color.White, Flip.None, 0.8f, 0.8f);
            Text.DrawAnchored(cost.ToString(), cx - 14, bc.Y, Text.GameFont[2], Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        }
        if (!pressed)
            return;
        game.Sound(36);
        if (review)
        {
            // FUN_001a7d68: the review
            o.Review(data, game.Player, cafe.Rng);
            reviewOpen = true;
            return;
        }
        // FUN_001a6c04 through a question: not enough toxin (320), or "use N toxin" - warned near the week's cap
        int price = o.BribeCost(data);
        if (game.Player.Toxin < price)
        {
            messages.Enqueue(("", new List<string> { data.Strings[320] }, null));
            return;
        }
        string ask = data.Strings[317] + price + data.Strings[318];
        if (o.Remaining > data.K.Long(0x42d464) - data.K.Int(0x42d46c))
            ask = data.Strings[TextNearMax] + " " + ask;
        question = (ask, data.Strings[319], () =>
        {
            game.Player.Toxin -= price;
            o.CompleteAll();
        });
    }

    /// <summary>FUN_00145074: five stars - orange for the rating without the bonus, purple for the bonus, then empty.</summary>
    void DrawRatingStars(float cx, float cy, float rating, float bonus)
    {
        var ui = data.IngameUi;
        int total = (int)MathF.Round(rating * 5), purple = Math.Min(total, (int)MathF.Round(bonus * 5));
        float step = 24 * QuestScale;
        for (int i = 0; i < 5; i++)
        {
            int img = i < total - purple ? UiRatingOn : i < total ? UiRatingPurple : UiRatingOff;
            ui[img].Draw(cx + (i - 2) * step, cy, Anchor.Center, Color.White, Flip.None, QuestScale, QuestScale);
        }
    }

    /// <summary>ObjectiveCompleteDialog: "BONUS ACHIEVED!", "THIS BONUS LASTS FOR n HOURS", and the critic's review.</summary>
    void DrawReview()
    {
        var o = game.Player.Objectives;
        var ui = data.IngameUi;
        var sheet = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        var board = sheet.Length > 2 ? sheet[2] : null;
        float s = QuestScale, bw = (board?.FullWidth ?? 485) * s, bh = (board?.FullHeight ?? 327) * s;
        var r = new Rectangle(MathF.Round((Screen.Width - bw) / 2), MathF.Round((Screen.Height - bh) / 2), bw, bh);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 120));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        if (board != null)
            board.DrawAt(r.X, r.Y, r.Width, r.Height, Color.White);
        else
            Widgets.Panel(r);
        Vector2 P(float x, float y) => new(r.X + x * s, r.Y + y * s);

        var head = P(485 / 2f, 14);
        Text.DrawAnchored(data.Strings[TextAchieved], head.X, head.Y, Text.GameFont[4], Anchor.Top | Anchor.HCenter, Widgets.Gold, Color.Black, 2);

        // the stars the cafe now has from reviews
        var big = ui[UiBigStar];
        int stars = Math.Min(o.Level, 3);
        for (int i = 0; i < stars; i++)
            big.Draw(head.X + (i - (stars - 1) / 2f) * (big.FullWidth + 6), head.Y + 48 * s, Anchor.Top | Anchor.HCenter, Color.White);

        // how long it lasts, on the torn paper
        var paper = ui[UiReviewPaper];
        var paperAt = P(26, 120);
        paper.Draw(paperAt.X, paperAt.Y, Anchor.Top | Anchor.Left, Color.White, Flip.None, s, s);
        float pcx = paperAt.X + paper.FullWidth * s / 2;
        var lasts = Widgets.WrapText(data.Strings[TextLastsFor], paper.FullWidth * s - 36, Text.GameFont[0]);
        for (int l = 0; l < lasts.Count; l++)
            Text.DrawAnchored(lasts[l], pcx, paperAt.Y + 22 * s + l * Text.GameFont[0], Text.GameFont[0], Anchor.Top | Anchor.HCenter, Ink);
        Text.DrawAnchored((o.Remaining / 3_600_000).ToString(), pcx, paperAt.Y + 76 * s, Text.GameFont[5], Anchor.Top | Anchor.HCenter, Ink);
        Text.DrawAnchored(data.Strings[TextHours], pcx, paperAt.Y + 116 * s, Text.GameFont[1], Anchor.Top | Anchor.HCenter, Ink);

        // the critic's note
        var critic = ui[UiCritic];
        var noteAt = P(196, 128);
        critic.Draw(noteAt.X, noteAt.Y, Anchor.Top | Anchor.Left, Color.White, Flip.None, s, s);
        ui[UiPin].Draw(noteAt.X + critic.FullWidth * s / 2, noteAt.Y, Anchor.Center, Color.White);
        int c = Math.Max(0, Math.Min(o.Level, 3) - 1);
        var quote = Widgets.WrapText(data.Strings[TextQuote + c], critic.FullWidth * s - 40, Text.GameFont[2]);
        for (int l = 0; l < quote.Count; l++)
            Text.DrawAnchored(quote[l], noteAt.X + critic.FullWidth * s / 2, noteAt.Y + 64 * s + l * Text.GameFont[2], Text.GameFont[2], Anchor.Top | Anchor.HCenter, Ink);
        Text.DrawAnchored(data.Strings[TextCritic + c], noteAt.X + critic.FullWidth * s - 24, noteAt.Y + 64 * s + quote.Count * Text.GameFont[2] + 6,
            Text.GameFont[1], Anchor.Top | Anchor.Right, Ink);

        // OK
        var button = ui[UiSprites.GreenBarWide];
        var bc = new Vector2(r.X + r.Width / 2, r.Y + r.Height - 32 * s);
        questButtonRect = new Rectangle(bc.X - button.FullWidth / 2f, bc.Y - button.FullHeight / 2f, button.FullWidth, button.FullHeight);
        bool ok = Widgets.ImageButton(button, bc.X, bc.Y);
        Text.DrawAnchored(data.Strings[TextOk], bc.X, bc.Y, Text.GameFont[3], Anchor.Center, Color.White, Color.Black, 2);
        if (ok || Input.IsKeyPressed(KeyboardKey.Escape) || Input.IsKeyPressed(KeyboardKey.Enter))
        {
            game.Sound(36);
            reviewOpen = false;
        }
    }
}
