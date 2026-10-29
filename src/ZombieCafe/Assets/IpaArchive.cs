using System.IO.Compression;

namespace ZombieCafe.Assets;

/// <summary>
/// Read-only view of the original game's bundle inside the player's own .ipa
/// (Payload/ZombieCafe.app/...). The port never ships Capcom's files; everything is read from here.
/// </summary>
public sealed class IpaArchive : IDisposable
{
    readonly ZipArchive zip;
    readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();

    public string FilePath { get; }
    public IReadOnlyCollection<string> Names => entries.Keys;

    IpaArchive(string path, ZipArchive zip)
    {
        FilePath = path;
        this.zip = zip;
        foreach (var e in zip.Entries)
        {
            var n = e.FullName;
            if (!n.StartsWith("Payload/", StringComparison.Ordinal) || n.EndsWith('/'))
                continue;
            int app = n.IndexOf(".app/", StringComparison.Ordinal);
            if (app >= 0)
                entries[n[(app + 5)..]] = e;
        }
    }

    /// <summary>Opens <paramref name="path"/> if it is a Zombie Cafe .ipa, otherwise returns null.</summary>
    public static IpaArchive? TryOpen(string path)
    {
        try
        {
            var zip = ZipFile.OpenRead(path);
            var ipa = new IpaArchive(path, zip);
            if (ipa.Exists("characterData.bin") && ipa.Exists("mapTiles.cct"))
                return ipa;
            ipa.Dispose();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    public bool Exists(string name) => entries.ContainsKey(name);

    public byte[]? TryRead(string name)
    {
        if (!entries.TryGetValue(name, out var e))
            return null;
        lock (gate)
        {
            using var s = e.Open();
            var buf = new byte[e.Length];
            s.ReadExactly(buf);
            return buf;
        }
    }

    public byte[] Read(string name) => TryRead(name) ?? throw new FileNotFoundException($"'{name}' is not in {FilePath}");

    public void Dispose() => zip.Dispose();
}
