using System.Text;

namespace ZombieCafe.Assets;

/// <summary>
/// The few messages that live only in the game's code, never in strings.bin (the pet limit, the pet details' labels):
/// read at run time from the executable in the player's .ipa - the armv7 slice of the fat Mach-O - at the address the
/// code loads them from, so the port carries none of the text itself.
/// </summary>
public sealed class ExeStrings
{
    readonly byte[]? exe;
    readonly List<(uint Vm, uint Size, long File)> segments = new();
    readonly Dictionary<uint, string> cache = new();

    public ExeStrings(IpaArchive ipa)
    {
        exe = ipa.TryRead("ZombieCafe");
        if (exe != null)
            Parse(exe);
    }

    static uint Be(byte[] d, long at) => (uint)(d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3]);
    static uint Le(byte[] d, long at) => (uint)(d[at] | d[at + 1] << 8 | d[at + 2] << 16 | d[at + 3] << 24);

    void Parse(byte[] d)
    {
        if (d.Length < 32)
            return;
        long slice = 0;
        if (Be(d, 0) == 0xCAFEBABE)
        {
            // a fat binary: the armv7 slice (cpu 12, subtype 9), else the first ARM one
            uint n = Be(d, 4);
            long first = -1;
            for (uint i = 0; i < n && 8 + 20 * (i + 1) <= d.Length; i++)
            {
                long at = 8 + 20 * i;
                uint cpu = Be(d, at), sub = Be(d, at + 4), off = Be(d, at + 8);
                if (cpu != 12)
                    continue;
                if (first < 0)
                    first = off;
                if (sub == 9)
                {
                    first = off;
                    break;
                }
            }
            if (first < 0)
                return;
            slice = first;
        }
        if (slice + 28 > d.Length || Le(d, slice) != 0xFEEDFACE)
            return;
        uint cmds = Le(d, slice + 16);
        long p = slice + 28;
        for (uint i = 0; i < cmds && p + 8 <= d.Length; i++)
        {
            uint cmd = Le(d, p), size = Le(d, p + 4);
            // LC_SEGMENT: vmaddr, vmsize, fileoff, filesize after the 16-byte name
            if (cmd == 1 && p + 40 <= d.Length)
                segments.Add((Le(d, p + 24), Le(d, p + 36), slice + Le(d, p + 32)));
            if (size == 0)
                break;
            p += size;
        }
    }

    /// <summary>The zero-terminated text at a code address ("" when the executable is not there).</summary>
    public string Get(uint vm)
    {
        if (cache.TryGetValue(vm, out var s))
            return s;
        s = "";
        if (exe != null)
            foreach (var (start, size, file) in segments)
            {
                if (vm < start || vm >= start + size)
                    continue;
                long at = file + (vm - start);
                long end = at;
                while (end < exe.Length && exe[end] != 0)
                    end++;
                s = Encoding.UTF8.GetString(exe, (int)at, (int)(end - at));
                break;
            }
        cache[vm] = s;
        return s;
    }
}
