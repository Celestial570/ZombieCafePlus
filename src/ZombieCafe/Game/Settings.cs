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

    // Two Point extras: getting toxin without the App Store. Edit settings.json to change them (0 = no limit).
    /// <summary>The toxin exchange (the + by the toxin count): dollars a vial (the game's own rate, K 0x42d138).</summary>
    public int ToxinPrice { get; set; } = 2000;
    /// <summary>The toxin exchange: vials that can be bought a day (0: no limit).</summary>
    public int ToxinDailyLimit { get; set; } = 5;
    /// <summary>The Toxin Generator's price in the store, in dollars (the original sold it for real money).</summary>
    public int ToxinGeneratorPrice { get; set; } = 40000;
    /// <summary>How many Toxin Generators the cafe may have, stored ones counted (0: no limit).</summary>
    public int ToxinGeneratorLimit { get; set; } = 2;
    /// <summary>Vials each Toxin Generator makes a day (it holds 5 until collected).</summary>
    public int ToxinGeneratorPerDay { get; set; } = 5;

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
                s.ToxinPrice = Math.Max(1, s.ToxinPrice);
                s.ToxinDailyLimit = Math.Max(0, s.ToxinDailyLimit);
                s.ToxinGeneratorPrice = Math.Max(0, s.ToxinGeneratorPrice);
                s.ToxinGeneratorLimit = Math.Max(0, s.ToxinGeneratorLimit);
                s.ToxinGeneratorPerDay = Math.Max(0, s.ToxinGeneratorPerDay);
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
