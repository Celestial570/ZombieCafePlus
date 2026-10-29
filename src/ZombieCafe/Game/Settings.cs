using System.Text.Json;

namespace ZombieCafe.Game;

/// <summary>
/// The player's options (the original keeps the volumes at game +0x84 / +0x85, 0..100, FUN_0004e1a8 and
/// FUN_0004e1cc). Kept apart from the save so deleting the cafe leaves them alone.
/// </summary>
public sealed class Settings
{
    public int MusicVolume { get; set; } = 45;
    public int SfxVolume { get; set; } = 80;
    /// <summary>The size of the cafe's and raids' HUD, in percent of the layout fitted to the window (a port option).</summary>
    public int HudSize { get; set; } = 100;
    public const int HudSizeMin = 60, HudSizeMax = 140;

    /// <summary>The settings in use (set at start-up).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static Settings Current { get; set; } = new();

    /// <summary>A new HUD size, kept within its range and saved.</summary>
    public void SetHudSize(int percent)
    {
        HudSize = Math.Clamp(percent, HudSizeMin, HudSizeMax);
        Save();
    }

    /// <summary>Test runs: changes are not written back.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReadOnly { get; init; }

    static string DefaultPath => Path.Combine(SaveGame.Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(DefaultPath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(DefaultPath)) is { } s)
            {
                s.MusicVolume = Math.Clamp(s.MusicVolume, 0, 100);
                s.SfxVolume = Math.Clamp(s.SfxVolume, 0, 100);
                s.HudSize = s.HudSize == 0 ? 100 : Math.Clamp(s.HudSize, HudSizeMin, HudSizeMax);
                return s;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new Settings();
    }

    public void Save()
    {
        if (ReadOnly)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
            File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
