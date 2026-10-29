using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// A pet picked in the cafe (GameStateCafe::update, case 3 - the pet goes to +0x7c, apart from the characters): its
/// command bar has REST (button 4, ui 53: home for a nap, FUN_000bc510) and ENERGIZE (6, ui 14, FUN_000bafb8) while it
/// is up, REVIVE (7, ui 15) while it is knocked out. (INFO, button 8, opens the pet's details in the original.)
/// </summary>
public sealed partial class CafeScreen
{
    /// <summary>PetInstance::hitTest: the picture of the pet as it stands (only while it is out).</summary>
    static bool HitPet(CharacterInstance pet, Vector2 p)
    {
        if (!pet.Visible)
            return false;
        var b = pet.BodyBounds;
        return p.X >= b.X && p.X <= b.X + b.Width && p.Y >= b.Y && p.Y <= b.Y + b.Height;
    }

    // the code's own words (FUN_000bd7b0 button 9, FUN_0011b640's labels)
    const uint TextPetRoom = 0x340d18, TextCritChance = 0x341cef, TextCritMultiplier = 0x341cfc, TextPurchasedOn = 0x341d0d,
        TextDaysOwned = 0x341d1a, TextPercent = 0x341d2b;

    Rectangle? petInfoRect, petNextRect, petPrevRect;
    // PetPortrait (FUN_000e5054): the pet standing, at 0.7
    FlashAnimation? portrait;
    CharacterInstance? portraitOf;

    /// <summary>
    /// CharacterDetails for a pet (FUN_0011b640): on the chalkboard the pet itself standing (PetPortrait), its name, kind
    /// and level, what it is, its energy, and its stats and record - attack speed and strength, the odds and the
    /// multiplier of a critical bite, the day it was bought and the days since, raids and kills; arrows page through
    /// the pets. Looking clears the "!" of a level gone up (record +0x24).
    /// </summary>
    void DrawPetBio(CharacterInstance pet)
    {
        var c = pet.Character;
        var def = c.Pet!;
        c.LevelledUp = false;
        var dialog = data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin");
        var board = dialog.Length > 2 ? dialog[2] : null;
        float bw = board?.FullWidth ?? 485, bh = board?.FullHeight ?? 327;
        float scale = 1.25f;
        var r = new Rectangle((Screen.Width - bw * scale) / 2, (Screen.Height - bh * scale) / 2, bw * scale, bh * scale);
        Gfx.Draw.Rect(new Rectangle(0, 0, Screen.Width, Screen.Height), new Color(0, 0, 0, 120));
        Widgets.Hover(new Rectangle(0, 0, Screen.Width, Screen.Height));
        if (board != null)
            board.DrawAt(r.X, r.Y, r.Width, r.Height, Color.White);
        else
            Widgets.Panel(r);

        // the pet, standing
        if (portraitOf != pet || portrait == null)
        {
            portraitOf = pet;
            var parts = data.Sheet(def.PartsSheet, def.PartsSheet + ".bin");
            var (file, mirrored) = data.PetAnimations.Get(PetAnim.Idle, (int)Facing.SE);
            portrait = file == null ? null : new FlashAnimation(file, parts) { FlipX = mirrored };
            portrait?.Play();
        }
        portrait?.Update((int)(Raylib.GetFrameTime() * 1000));
        portrait?.Draw(r.X + 95, r.Y + 135, 0.7f, Color.White);

        // who it is
        Text.Draw(c.Name, r.X + 180, r.Y + 34, Text.GameFont[4], Chalk);
        Text.Draw($"{def.Name}   Level {c.Level + 1}", r.X + 180, r.Y + 70, Text.GameFont[2], Color.White);
        var about = Widgets.WrapText(def.Description, r.Width - 220, Text.GameFont[1]);
        for (int i = 0; i < Math.Min(2, about.Count); i++)
            Text.Draw(about[i], r.X + 180, r.Y + 98 + i * (Text.GameFont[1] + 4), Text.GameFont[1], new Color(220, 220, 220, 255));
        float max = MathF.Max(1f, c.MaxEnergy(data));
        Text.Draw(data.Strings[512].Trim(), r.X + 40, r.Y + 166, Text.GameFont[1], Color.White);
        Widgets.Bar(new Rectangle(r.X + 130, r.Y + 170, 150, 14), c.Energy / max, new Color(120, 220, 90, 255));

        // the stats and the record: labels in one column, values in the next
        int days = (int)((GameClock.UnixMs - c.JoinedUnixMs) / 86_400_000);
        var bought = DateTimeOffset.FromUnixTimeMilliseconds(c.JoinedUnixMs).ToLocalTime();
        (string Label, string Value)[] rows =
        [
            (data.Strings[TextAtkSpeed].Trim(), def.Speed.ToString()),
            (data.Strings[TextAtkStrength].Trim(), def.Strength.ToString()),
            (data.Exe.Get(TextCritChance).Trim(), (int)(def.CritChance * 100) + data.Exe.Get(TextPercent)),
            (data.Exe.Get(TextCritMultiplier).Trim(), def.CritMultiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
            (data.Exe.Get(TextPurchasedOn), bought.ToString("MMMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture)),
            (data.Exe.Get(TextDaysOwned), Math.Max(0, days).ToString()),
            (data.Strings[TextRaids].Trim(), c.Raids.ToString()),
            (data.Strings[TextKills].Trim(), c.Kills.ToString()),
        ];
        float size = Text.GameFont[1];
        for (int i = 0; i < rows.Length; i++)
        {
            float y = r.Y + 194 + i * (size + 3) + (i >= 4 ? 8 : 0);
            Text.Draw(rows[i].Label, r.X + 40, y, size, new Color(200, 200, 200, 255));
            Text.Draw(rows[i].Value, r.X + 270, y, size, Color.White);
        }

        // the way out, and the arrows through the pets
        var ui = data.IngameUi;
        if (Widgets.ImageButton(ui[UiSprites.CloseX], r.X + r.Width - 28, r.Y + 28, 0.9f) || (question == null && Input.IsKeyPressed(KeyboardKey.Escape)))
        {
            bio = null;
            portrait = null;
            game.Sound(36);
            return;
        }
        petPrevRect = petNextRect = null;
        int at = game.Pets.IndexOf(pet);
        if (at < 0 || game.Pets.Count < 2)
            return;
        // just outside the board, halfway down
        var left = ui[UiSprites.ArrowLeft];
        var right = ui[UiSprites.ArrowRight];
        float ay = r.Y + r.Height / 2, lx = r.X - left.FullWidth / 2f - 6, rx = r.X + r.Width + right.FullWidth / 2f + 6;
        if (at > 0)
        {
            petPrevRect = new Rectangle(lx - left.FullWidth / 2f, ay - left.FullHeight / 2f, left.FullWidth, left.FullHeight);
            if (Widgets.ImageButton(left, lx, ay))
            {
                game.Sound(36);
                bio = game.Pets[at - 1];
            }
        }
        if (at < game.Pets.Count - 1)
        {
            petNextRect = new Rectangle(rx - right.FullWidth / 2f, ay - right.FullHeight / 2f, right.FullWidth, right.FullHeight);
            if (Widgets.ImageButton(right, rx, ay))
            {
                game.Sound(36);
                bio = game.Pets[at + 1];
            }
        }
    }

    void DrawPetBar(CharacterInstance pet)
    {
        var c = pet.Character;
        string job = game.JobText(pet).Replace("\n", "");
        float bw = 520, bh = job.Length > 0 ? 116 : 96, jobRow = job.Length > 0 ? 20 : 0;
        var r = new Rectangle((Screen.Width - bw) / 2, Screen.Height - bh - 40, bw, bh);
        Widgets.Panel(r);
        Widgets.Label(c.Name, r.X + 16, r.Y + 10, 26, Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label($"{c.Pet?.Name}   Level {c.Level + 1}", r.X + 16, r.Y + 42, 18);
        if (job.Length > 0)
            Widgets.Label(job, r.X + 16, r.Y + 64, 16, Anchor.Top | Anchor.Left, new Color(170, 230, 120, 255));
        float max = MathF.Max(1f, c.MaxEnergy(data));
        Widgets.Label(data.Strings[512].Trim(), r.X + 16, r.Y + 66 + jobRow, 16);
        Widgets.Bar(new Rectangle(r.X + 96, r.Y + 68 + jobRow, 112, 16), c.Energy / max, new Color(120, 220, 90, 255));
        var need = data.K.ZombieLevelXp;
        float xpFrac = c.Level < need.Length ? (float)c.Xp / need[c.Level] : 1f;
        Widgets.Bar(new Rectangle(r.X + 96, r.Y + 86 + jobRow, 112, 6), xpFrac, new Color(90, 160, 255, 255));

        float s = 0.8f, rx = r.X + bw - 60, ry = r.Y + bh / 2, ex = rx - 100;
        // INFO (command bar button 8, FUN_000bd3e4): the pet's details
        var info = data.IngameUi[UiSprites.Info];
        float ix = rx - 200;
        petInfoRect = new Rectangle(ix - info.FullWidth * s / 2, ry - info.FullHeight * s / 2, info.FullWidth * s, info.FullHeight * s);
        if (Widgets.ImageButton(info, ix, ry, s))
            OpenBio(pet);
        if (c.ReanimateMs >= 1)
        {
            // knocked out: REVIVE for a vial
            var revive = data.IngameUi[UiSprites.Revive];
            reviveButtonRect = new Rectangle(ex - revive.FullWidth * s / 2, ry - revive.FullHeight * s / 2, revive.FullWidth * s, revive.FullHeight * s);
            if (Widgets.ImageButton(revive, ex, ry, s, game.CannotRevive(pet) is null or { Length: > 0 }))
            {
                if (game.CannotRevive(pet) is { Length: > 0 } why)
                    messages.Enqueue(("", new List<string> { why }, null));
                else
                    question = (game.EnergizeQuestion(pet), data.Strings[319], () => game.Revive(pet));
            }
            return;
        }
        // REST: home for a nap (not while it is in already)
        var rest = data.IngameUi[UiSprites.Rest];
        restButtonRect = new Rectangle(rx - rest.FullWidth * s / 2, ry - rest.FullHeight * s / 2, rest.FullWidth * s, rest.FullHeight * s);
        if (Widgets.ImageButton(rest, rx, ry, s, pet.Home is { PetInside: false }))
        {
            game.PetGoHome(pet);
            game.Selected = null;
        }
        var energize = data.IngameUi[UiSprites.Energize];
        energizeButtonRect = new Rectangle(ex - energize.FullWidth * s / 2, ry - energize.FullHeight * s / 2, energize.FullWidth * s, energize.FullHeight * s);
        if (Widgets.ImageButton(energize, ex, ry, s, game.CannotEnergize(pet) is null or { Length: > 0 }))
        {
            if (game.CannotEnergize(pet) is { Length: > 0 } why)
                messages.Enqueue(("", new List<string> { why }, null));
            else
                question = (game.EnergizeQuestion(pet), data.Strings[319], () => game.Energize(pet));
        }
    }
}
