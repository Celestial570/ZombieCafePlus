using System.Numerics;

namespace ZombieCafe.Core;

/// <summary>
/// The window as the game sees it. The original UI was laid out for an iPad (1024x768 points); here the
/// window is mapped to a virtual screen that is at least that big in both directions, so layouts that
/// anchor to the edges simply get more room when the window is wider or taller.
/// </summary>
public static class Screen
{
    public const float BaseWidth = 1024, BaseHeight = 768;

    /// <summary>Window size in pixels.</summary>
    public static int PixelWidth { get; private set; } = 1280;
    public static int PixelHeight { get; private set; } = 800;

    /// <summary>Virtual (iPad point) size of the whole window.</summary>
    public static float Width { get; private set; } = BaseWidth;
    public static float Height { get; private set; } = BaseHeight;

    /// <summary>Window pixels per virtual point.</summary>
    public static float Scale { get; private set; } = 1;

    /// <summary>Extra user UI scale (options menu), 1 = fit the 1024x768 layout to the window.</summary>
    public static float UserScale { get; set; } = 1;

    public static Matrix3x2 ToPixels => Matrix3x2.CreateScale(Scale);

    /// <summary>Recomputes the mapping; returns true when the virtual size changed.</summary>
    public static bool Update(int pixelWidth, int pixelHeight)
    {
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        float scale = MathF.Min(pixelWidth / BaseWidth, pixelHeight / BaseHeight) * UserScale;
        float w = pixelWidth / scale, h = pixelHeight / scale;
        bool changed = pixelWidth != PixelWidth || pixelHeight != PixelHeight || MathF.Abs(scale - Scale) > 1e-6f;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Scale = scale;
        Width = w;
        Height = h;
        return changed;
    }

    public static Vector2 FromPixels(Vector2 p) => p / Scale;
    public static Vector2 ToPixelsPoint(Vector2 p) => p * Scale;
}
