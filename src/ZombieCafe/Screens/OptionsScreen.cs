using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// Options from the title screen (GameStateOptions: load FUN_0000e764, layout 00010174, update 00010acc): the
/// music and sound-effect volumes on two sliders (0..100, FUN_0004e1a8 / FUN_0004e1cc), and "Restart Café",
/// which after two warnings deletes the cafe (FUN_00011148 -> FUN_000115b4). The original's notification,
/// Facebook and date-of-birth panels belonged to its online services and are left out.
/// </summary>
public sealed class OptionsScreen : GameScreen
{
    // the iPad's numbers (device 0 of DAT_00370040 .. DAT_00370130)
    const float TitleY = 70, TitleGap = 60, SliderGap = 32, PanelGap = 30;
    const float BackX = 1000, BackY = 754;
    // ipad_menuImages
    const int MenuWall = 4, MenuBack = 5, MenuDeleteButton = 16, MenuPanel = 18, MenuTrack = 23, MenuKnob = 24, MenuAwning = 29;
    // strings.bin
    const int TextMusic = 50, TextSfx = 51, TextRestart = 53, TextTitle = 54, TextDeleteData = 57, TextCancel = 316,
        TextDeleteCafe = 322, TextDeleteWarning = 323, TextDeleteSure = 324;
    const int SoundTap = 36, SoundClick = 40;
    static readonly Color Maroon = Colors.FromArgb(unchecked((int)0xff6c1526));

    readonly GameData data;
    readonly Audio.AudioSystem? audio;
    readonly Settings settings;
    readonly Action back;
    int dragging = -1;
    // the delete warnings: 0 none, 1 the first, 2 "are you sure?"
    int asking;
    bool deleted, left;

    public OptionsScreen(GameData data, Audio.AudioSystem? audio, Settings settings, Action back)
    {
        this.data = data;
        this.audio = audio;
        this.settings = settings;
        this.back = back;
    }

    SubImage[] Menu => data.Sheet("ipad_menuImages", "ipad_menuOffsets.bin");

    float scale = 1;
    Vector2 origin;
    Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;

    public override void Layout()
    {
        scale = MathF.Min(Screen.Width / Screen.BaseWidth, Screen.Height / Screen.BaseHeight);
        origin = new Vector2((Screen.Width - Screen.BaseWidth * scale) / 2, (Screen.Height - Screen.BaseHeight * scale) / 2);
        Input.TargetToPixels = ScriptTarget;
    }

    public override void Update(float dt)
    {
    }

    // the layout's rows: the sliders' tops and the restart panel's top
    float LabelHeight => Text.GameFont[3];
    float SliderTop(int i, SubImage[] menu)
    {
        // layout: 60 + 32 + the title's and the label's heights; each next one a slider, 32 and a label lower
        float first = TitleGap + SliderGap + Text.GameFont[6] + LabelHeight;
        return first + i * (SliderHeight(menu) + SliderGap + LabelHeight);
    }

    static float SliderHeight(SubImage[] menu) => Math.Max(menu[MenuTrack].FullHeight, menu[MenuKnob].FullHeight);

    public override void Draw()
    {
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        var menu = Menu;
        if (menu.Length <= MenuAwning)
            return;
        Widgets.Blocked = asking != 0;

        var wall = menu[MenuWall];
        float cover = MathF.Max(Screen.Width / wall.FullWidth, Screen.Height / wall.FullHeight);
        wall.Draw(Screen.Width / 2, Screen.Height / 2, Anchor.Center, Color.White, Flip.None, cover, cover);
        var awning = menu[MenuAwning];
        float aw = awning.FullWidth * scale;
        for (float x = origin.X - MathF.Ceiling(origin.X / aw) * aw; x < Screen.Width; x += aw)
            awning.Draw(x, 0, Anchor.Top | Anchor.Left, Color.White, Flip.None, scale, scale);

        var title = P(Screen.BaseWidth / 2, TitleY);
        Text.DrawAnchored(data.Strings[TextTitle], title.X, title.Y, Text.GameFont[6] * scale, Anchor.Top | Anchor.HCenter, Maroon);

        int music = Slider(menu, 0, data.Strings[TextMusic], settings.MusicVolume);
        if (music != settings.MusicVolume)
        {
            settings.MusicVolume = music;
            if (audio != null)
                audio.MusicVolume = music / 100f;
        }
        int sfx = Slider(menu, 1, data.Strings[TextSfx], settings.SfxVolume);
        if (sfx != settings.SfxVolume)
        {
            settings.SfxVolume = sfx;
            if (audio != null)
                audio.EffectsVolume = sfx / 100f;
        }
        // the HUD size (a port option): 60%..140%, in steps of 5; set when the knob is let go, as the cafe uses it
        int hudKnob = dragging == 2 ? hudDrag : (settings.HudSize - Settings.HudSizeMin) * 100 / (Settings.HudSizeMax - Settings.HudSizeMin);
        hudDrag = Slider(menu, 2, "HUD SIZE", hudKnob, v => HudPercent(v) + "%");
        if (dragging >= 0 && !Input.IsMouseButtonDown(MouseButton.Left))
        {
            // let go: keep the setting, and let the effects one be heard
            if (dragging == 1)
                audio?.Play(SoundTap);
            if (dragging == 2)
                settings.HudSize = HudPercent(hudDrag);
            dragging = -1;
            settings.Save();
        }

        // Restart Café: a panel with the delete button, gone once the cafe has been deleted
        if (!deleted)
        {
            var panel = menu[MenuPanel];
            float top = SliderTop(2, menu) + SliderHeight(menu) + PanelGap;
            var at = P(Screen.BaseWidth / 2, top);
            panel.Draw(at.X, at.Y, Anchor.Top | Anchor.HCenter, Color.White, Flip.None, scale, scale);
            Text.DrawAnchored(data.Strings[TextRestart], at.X, at.Y + 30 * scale, Text.GameFont[3] * scale, Anchor.Center, Color.White);
            var del = menu[MenuDeleteButton];
            var delAt = new Vector2(at.X, at.Y + (panel.FullHeight - 30) * scale);
            bool pressed = Widgets.ImageButton(del, delAt.X, delAt.Y, scale);
            Text.DrawAnchored(data.Strings[TextDeleteData], delAt.X, delAt.Y, Text.GameFont[2] * scale, Anchor.Center, Color.White);
            if (pressed)
            {
                audio?.Play(SoundClick);
                asking = 1;
            }
        }

        var backImg = menu[MenuBack];
        var backAt = P(BackX - backImg.FullWidth / 2f, BackY - backImg.FullHeight / 2f);
        if ((Widgets.ImageButton(backImg, backAt.X, backAt.Y, scale) || (asking == 0 && Input.IsKeyPressed(KeyboardKey.Escape))) && !left)
        {
            left = true;
            audio?.Play(SoundClick);
            settings.Save();
            back();
            return;
        }

        Widgets.Blocked = false;
        if (asking != 0)
        {
            int answer = Widgets.QuestionBox(data.Strings[asking == 1 ? TextDeleteWarning : TextDeleteSure], data.Strings[TextDeleteCafe], data.Strings[TextCancel]);
            if (answer == 1 && asking == 1)
            {
                asking = 2;
            }
            else if (answer == 1)
            {
                // FUN_000115b4: the cafe is gone; the next PLAY starts from the beginning
                SaveGame.Delete();
                deleted = true;
                asking = 0;
            }
            else if (answer == 2)
            {
                asking = 0;
            }
        }
    }

    /// <summary>One labelled slider; returns its (possibly dragged) value.</summary>
    int hudDrag;

    /// <summary>The HUD slider's 0..100 as a size in percent (steps of 5).</summary>
    static int HudPercent(int knob) =>
        (int)MathF.Round((Settings.HudSizeMin + knob / 100f * (Settings.HudSizeMax - Settings.HudSizeMin)) / 5f) * 5;

    int Slider(SubImage[] menu, int row, string label, int value, Func<int, string>? shown = null)
    {
        var track = menu[MenuTrack];
        var knob = menu[MenuKnob];
        float h = SliderHeight(menu);
        float top = SliderTop(row, menu);
        float left = Screen.BaseWidth / 2 - track.FullWidth / 2f;
        var labelAt = P(left, top);
        Text.DrawAnchored(label, labelAt.X, labelAt.Y, Text.GameFont[3] * scale, Anchor.Bottom | Anchor.Left, Maroon);
        var valueAt = P(left + track.FullWidth, top);
        Text.DrawAnchored(shown?.Invoke(value) ?? value.ToString(), valueAt.X, valueAt.Y, Text.GameFont[3] * scale, Anchor.Bottom | Anchor.Right, Maroon);

        var mid = P(Screen.BaseWidth / 2, top + h / 2);
        track.Draw(mid.X, mid.Y, Anchor.Center, Color.White, Flip.None, scale, scale);
        float span = track.FullWidth - knob.FullWidth;
        var area = new Rectangle(P(left, top).X, P(left, top).Y, track.FullWidth * scale, h * scale);
        if (Widgets.Hover(area) && Input.IsMouseButtonPressed(MouseButton.Left))
            dragging = row;
        if (dragging == row && Input.IsMouseButtonDown(MouseButton.Left))
        {
            float x = (Widgets.Mouse.X - origin.X) / scale - left - knob.FullWidth / 2f;
            value = (int)MathF.Round(Math.Clamp(x / span, 0f, 1f) * 100);
        }
        var knobAt = P(left + knob.FullWidth / 2f + span * value / 100f, top + h / 2);
        knob.Draw(knobAt.X, knobAt.Y, Anchor.Center, Color.White, Flip.None, scale, scale);
        return value;
    }

    Vector2? ScriptTarget(string name)
    {
        var menu = Menu;
        if (menu.Length <= MenuAwning)
            return null;
        if (name == "back")
            return Screen.ToPixelsPoint(P(BackX - menu[MenuBack].FullWidth / 2f, BackY - menu[MenuBack].FullHeight / 2f));
        if (name is "music" or "sfx" or "hud")
        {
            // the middle of the slider's track
            float top = SliderTop(name == "music" ? 0 : name == "sfx" ? 1 : 2, menu);
            return Screen.ToPixelsPoint(P(Screen.BaseWidth / 2, top + SliderHeight(menu) / 2));
        }
        return null;
    }
}
