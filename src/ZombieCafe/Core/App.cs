using System.Numerics;
using Raylib_cs;

namespace ZombieCafe.Core;

/// <summary>A full-window game screen (the port's counterpart of the original GameState).</summary>
public abstract class GameScreen
{
    /// <summary>Called on entry and whenever the window (and so the virtual screen) changes size.</summary>
    public virtual void Layout() { }
    public abstract void Update(float dt);
    public abstract void Draw();
    public virtual void Leave() { }
    /// <summary>The mouse is over something in the screen's world that a click would act on (shown with the hand).</summary>
    public virtual bool OverClickable => false;
    /// <summary>The screen's interface follows the HUD size setting (the cafe and raids; full screens keep their fit).</summary>
    public virtual bool ScalesHud => false;
}

/// <summary>Window, main loop and screen switching.</summary>
public sealed class App
{
    public static App Current { get; private set; } = null!;

    GameScreen? screen, next;

    /// <summary>Sound effects and music (set by Program once the bundle is open).</summary>
    public Audio.AudioSystem? Audio { get; set; }
    readonly string? shotPath;
    readonly int shotFrame;
    int frame;
    string? photoPath;
    bool handCursor;

    /// <summary>The next frame drawn goes to this file as well (the cafe's PHOTO, ScreenshotInterface::takeScreenshot).</summary>
    public void CapturePhoto(string path) => photoPath = path;

    public App(string? shotPath = null, int shotFrame = 5)
    {
        Current = this;
        this.shotPath = shotPath;
        this.shotFrame = shotFrame;
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(1280, 800, "Zombie Cafe Plus");
        Raylib.SetWindowMinSize(800, 500);
        Raylib.SetExitKey(KeyboardKey.Null);
        // mirrored sprites (characters are drawn with a negative x scale) must not be culled
        Rlgl.DisableBackfaceCulling();
        Screen.Update(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
    }

    public void SetScreen(GameScreen s) => next = s;

    public void Run(GameScreen first)
    {
        SetScreen(first);
        while (!Raylib.WindowShouldClose())
        {
            if (next != null)
            {
                screen?.Leave();
                screen = next;
                next = null;
                screen.Layout();
            }
            // the HUD size: Ctrl + = / - in steps of 10%, Ctrl + 0 back to 100% (also on the options screen)
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            var settings = Game.Settings.Current;
            if (ctrl && (Raylib.IsKeyPressed(KeyboardKey.Equal) || Raylib.IsKeyPressed(KeyboardKey.KpAdd)))
                settings.SetHudSize(settings.HudSize + 10);
            else if (ctrl && (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract)))
                settings.SetHudSize(settings.HudSize - 10);
            else if (ctrl && (Raylib.IsKeyPressed(KeyboardKey.Zero) || Raylib.IsKeyPressed(KeyboardKey.Kp0)))
                settings.SetHudSize(100);
            Screen.UserScale = screen!.ScalesHud ? settings.HudSize / 100f : 1f;
            if (Screen.Update(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()))
                screen!.Layout();
            // the original clamps long frames; keep the simulation stable after a stall or window drag
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.1f);
            Input.BeginFrame();
            // scripted and screenshot runs step at a steady 60 Hz so events land on the same game moment
            if (Input.Scripted || shotPath != null)
                dt = 1f / 60f;
            screen!.Update(dt);
            Audio?.Update();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            screen.Draw();
            Raylib.EndDrawing();
            // the pointing hand over buttons and over things in the world a click would act on
            bool hand = Ui.Widgets.OverButton || screen.OverClickable;
            if (hand != handCursor)
            {
                handCursor = hand;
                Raylib.SetMouseCursor(hand ? MouseCursor.PointingHand : MouseCursor.Default);
            }
            if (photoPath != null)
            {
                var image = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(image, photoPath);
                Raylib.UnloadImage(image);
                photoPath = null;
            }
            if (shotPath != null && ++frame == shotFrame)
            {
                Raylib.TakeScreenshot(shotPath);
                break;
            }
        }
        screen?.Leave();
        Raylib.CloseWindow();
    }
}
