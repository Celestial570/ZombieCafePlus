namespace ZombieCafe.Assets;

/// <summary>
/// Sprite-sheet maps (*.bin next to a texture, *Offsets.bin), read big-endian like the original
/// PackedImage loader: u8 version, u16 count, then per entry [v2: UTF name] x, y, w, h and
/// [v2: trim left, top, right, bottom] (the transparent margins cropped off the original image).
/// </summary>
public sealed class SpriteAtlas
{
    public readonly record struct Entry(string Name, int X, int Y, int Width, int Height,
        int TrimLeft, int TrimTop, int TrimRight, int TrimBottom)
    {
        /// <summary>Size of the original image before its transparent margins were cropped.</summary>
        public int FullWidth => Width + TrimLeft + TrimRight;
        public int FullHeight => Height + TrimTop + TrimBottom;
    }

    public int Version { get; }
    public IReadOnlyList<Entry> Entries { get; }
    readonly Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

    SpriteAtlas(int version, List<Entry> entries)
    {
        Version = version;
        Entries = entries;
        for (int i = 0; i < entries.Count; i++)
            byName.TryAdd(entries[i].Name, i);
    }

    public int IndexOf(string name) => byName.TryGetValue(name, out int i) ? i : -1;

    public static SpriteAtlas Parse(byte[] data)
    {
        var r = new BigEndianReader(data);
        int version = r.ReadByte();
        int count = r.ReadShort();
        var list = new List<Entry>(count);
        for (int i = 0; i < count && !r.AtEnd; i++)
        {
            string name = version == 2 ? r.ReadUtf() : "";
            int x = r.ReadShort(), y = r.ReadShort(), w = r.ReadShort(), h = r.ReadShort();
            int l = 0, t = 0, rt = 0, b = 0;
            if (version == 2)
            {
                l = r.ReadShort();
                t = r.ReadShort();
                rt = r.ReadShort();
                b = r.ReadShort();
            }
            list.Add(new Entry(name, x, y, w, h, l, t, rt, b));
        }
        return new SpriteAtlas(version, list);
    }
}
