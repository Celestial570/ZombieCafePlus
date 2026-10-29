using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// CharacterDetails (FUN_00113778 / vf02 0011db84), the INFO button's bio: on a chalkboard (dialog sheet #2) the
/// zombie's face and name, its stats and its record (converted on, days on staff, customers served, raids, kills,
/// deaths), and "Fire Zombie" (asking first, FUN_0011daac); for the chef, the day the cafe opened and
/// "Change Avatar" (the chef store).
/// </summary>
public sealed partial class CafeScreen
{
    // strings.bin
    const int TextTipRating = 509, TextAtkSpeed = 510, TextAtkStrength = 511, TextConvertedOn = 58, TextDaysOnStaff = 59,
        TextServed = 60, TextRaids = 61, TextKills = 62, TextDeaths = 63, TextFire = 325, TextChangeAvatar = 326,
        TextOpenedShop = 331, TextFireSure = 472, TextYes = 461, TextNo = 462;
    static readonly Color Chalk = new(0xa7, 0xce, 0x51, 0xff);

    /// <summary>The character whose bio is open.</summary>
    CharacterInstance? bio;
    Rectangle? bioButtonRect, infoButtonRect;

    void OpenBio(CharacterInstance w)
    {
        bio = w;
        game.Sound(36);
    }

    void DrawBio()
    {
        var w = bio!;
        if (!cafe.Objects.Contains(w))
        {
            bio = null;
            return;
        }
        if (w.IsPet)
        {
            DrawPetBio(w);
            return;
        }
        var c = w.Character;
        bool chef = w == game.ChefActor;
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

        // the face (parts sprite 16) and who this is
        var parts = data.Sheet(c.Def.PartsSheet(c.IsZombie), c.Def.PartsSheet(c.IsZombie) + ".bin");
        int faceIndex = data.K.Int(0x42d618);
        if (faceIndex < parts.Length)
            parts[faceIndex].Draw(r.X + 90, r.Y + 95, Anchor.Center, Color.White, Flip.None, 1f, 1f);
        Text.Draw(c.Name, r.X + 170, r.Y + 40, Text.GameFont[4], Chalk);
        Text.Draw(c.Def.Name, r.X + 170, r.Y + 76, Text.GameFont[2], Color.White);
        if (!chef)
            Text.Draw($"Level {c.Level + 1}", r.X + 170, r.Y + 102, Text.GameFont[2], Color.White);

        // the stats and the record
        var joined = DateTimeOffset.FromUnixTimeMilliseconds(c.JoinedUnixMs).ToLocalTime();
        int days = (int)((GameClock.UnixMs - c.JoinedUnixMs) / 86_400_000);
        var lines = new List<string>
        {
            data.Strings[TextTipRating] + c.TipRating,
            data.Strings[TextAtkSpeed] + c.Speed,
            data.Strings[TextAtkStrength] + c.Strength,
            data.Strings[chef ? TextOpenedShop : TextConvertedOn] + " " + joined.ToString("d"),
            data.Strings[TextDaysOnStaff] + " " + Math.Max(0, days),
            data.Strings[TextServed] + " " + c.Served,
            data.Strings[TextRaids] + " " + c.Raids,
            data.Strings[TextKills] + " " + c.Kills,
            data.Strings[TextDeaths] + " " + c.Deaths,
        };
        // the type's stats under the face, the record in a column beside them
        float size = Text.GameFont[1];
        for (int i = 0; i < lines.Count; i++)
        {
            bool left = i < 3;
            float x = left ? r.X + 40 : r.X + r.Width / 2 - 10;
            float y = left ? r.Y + 160 + i * (size + 6) : r.Y + 142 + (i - 3) * (size + 6);
            Text.Draw(lines[i], x, y, size, Color.White);
        }

        // the way out, and the button: fire this zombie, or change the chef
        var ui = data.IngameUi;
        if (Widgets.ImageButton(ui[UiSprites.CloseX], r.X + r.Width - 28, r.Y + 28, 0.9f) || (question == null && Input.IsKeyPressed(KeyboardKey.Escape)))
        {
            bio = null;
            game.Sound(36);
            return;
        }
        var button = ui[UiSprites.GreenBarWide];
        float bx = r.X + r.Width / 2, by = r.Y + r.Height - 42;
        bioButtonRect = new Rectangle(bx - button.FullWidth / 2f, by - button.FullHeight / 2f, button.FullWidth, button.FullHeight);
        bool pressed = Widgets.ImageButton(button, bx, by);
        Text.DrawAnchored(data.Strings[chef ? TextChangeAvatar : TextFire], bx, by, Text.GameFont[3], Anchor.Center, Color.White, Color.Black, 2);
        if (!pressed)
            return;
        game.Sound(40);
        if (chef)
        {
            bio = null;
            OpenChefStore();
        }
        else
        {
            question = (data.Strings[TextFireSure] + c.Name + "?", data.Strings[TextYes], () =>
            {
                game.Fire(w);
                bio = null;
            });
        }
    }

    /// <summary>INFO (command bar button 8, ui 45) at the given spot of a command bar.</summary>
    void InfoButton(CharacterInstance w, float x, float y, float s)
    {
        var info = data.IngameUi[UiSprites.Info];
        infoButtonRect = new Rectangle(x - info.FullWidth * s / 2, y - info.FullHeight * s / 2, info.FullWidth * s, info.FullHeight * s);
        if (Widgets.ImageButton(info, x, y, s))
        {
            // FUN_000b9b4c: the zombie list's badge goes
            hudBadge[HudZombies] = false;
            OpenBio(w);
        }
    }
}
