namespace ZombieCafe.Assets;

/// <summary>AngelCode BMFont, binary format version 3 (the game's thunder_*pt.fnt files).</summary>
public sealed class BmFont
{
    public readonly record struct Glyph(int X, int Y, int Width, int Height, int XOffset, int YOffset, int XAdvance, int Page);

    public string Face { get; private set; } = "";
    public int Size { get; private set; }
    public int LineHeight { get; private set; }
    public int Base { get; private set; }
    public int ScaleW { get; private set; }
    public int ScaleH { get; private set; }
    public List<string> Pages { get; } = new();
    public Dictionary<int, Glyph> Glyphs { get; } = new();
    public Dictionary<(int, int), int> Kerning { get; } = new();

    public static BmFont Parse(byte[] file)
    {
        if (file.Length < 4 || file[0] != 'B' || file[1] != 'M' || file[2] != 'F' || file[3] != 3)
            throw new InvalidDataException("not a binary BMFont v3 file");
        var f = new BmFont();
        var r = new BinaryReader(new MemoryStream(file));
        r.BaseStream.Position = 4;
        while (r.BaseStream.Position + 5 <= file.Length)
        {
            int type = r.ReadByte();
            int size = r.ReadInt32();
            long start = r.BaseStream.Position;
            switch (type)
            {
                case 1:
                    f.Size = Math.Abs(r.ReadInt16());
                    r.BaseStream.Position = start + 14;
                    f.Face = ReadCString(r, start + size);
                    break;
                case 2:
                    f.LineHeight = r.ReadUInt16();
                    f.Base = r.ReadUInt16();
                    f.ScaleW = r.ReadUInt16();
                    f.ScaleH = r.ReadUInt16();
                    break;
                case 3:
                    while (r.BaseStream.Position < start + size)
                        f.Pages.Add(ReadCString(r, start + size));
                    break;
                case 4:
                    for (int i = 0; i < size / 20; i++)
                    {
                        int id = (int)r.ReadUInt32();
                        f.Glyphs[id] = new Glyph(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(),
                            r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadByte());
                        r.ReadByte();
                    }
                    break;
                case 5:
                    for (int i = 0; i < size / 10; i++)
                    {
                        int a = (int)r.ReadUInt32(), b = (int)r.ReadUInt32();
                        f.Kerning[(a, b)] = r.ReadInt16();
                    }
                    break;
            }
            r.BaseStream.Position = start + size;
        }
        return f;
    }

    static string ReadCString(BinaryReader r, long limit)
    {
        var sb = new System.Text.StringBuilder();
        while (r.BaseStream.Position < limit)
        {
            byte b = r.ReadByte();
            if (b == 0)
                break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }
}
