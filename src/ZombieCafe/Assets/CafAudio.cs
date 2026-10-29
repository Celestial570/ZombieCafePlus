using System.Buffers.Binary;

namespace ZombieCafe.Assets;

/// <summary>Apple Core Audio Format (.caf) with linear PCM, converted to an in-memory RIFF WAV.</summary>
public static class CafAudio
{
    public static byte[] ToWav(byte[] caf)
    {
        if (caf.Length < 8 || caf[0] != 'c' || caf[1] != 'a' || caf[2] != 'f' || caf[3] != 'f')
            throw new InvalidDataException("not a CAF file");
        double rate = 44100;
        int channels = 1, bits = 16;
        bool littleEndian = false, isFloat = false;
        ReadOnlySpan<byte> pcm = default;
        int p = 8;
        while (p + 12 <= caf.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32BigEndian(caf.AsSpan(p));
            long size = BinaryPrimitives.ReadInt64BigEndian(caf.AsSpan(p + 4));
            int body = p + 12;
            if (size < 0 || body + size > caf.Length)
                size = caf.Length - body;
            if (type == 0x64657363) // 'desc'
            {
                rate = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(caf.AsSpan(body)));
                uint format = BinaryPrimitives.ReadUInt32BigEndian(caf.AsSpan(body + 8));
                uint flags = BinaryPrimitives.ReadUInt32BigEndian(caf.AsSpan(body + 12));
                channels = (int)BinaryPrimitives.ReadUInt32BigEndian(caf.AsSpan(body + 24));
                bits = (int)BinaryPrimitives.ReadUInt32BigEndian(caf.AsSpan(body + 28));
                if (format != 0x6C70636D) // 'lpcm'
                    throw new NotSupportedException("only linear PCM .caf files are supported");
                isFloat = (flags & 1) != 0;
                littleEndian = (flags & 2) != 0;
            }
            else if (type == 0x64617461) // 'data': u32 edit count, then samples
            {
                pcm = caf.AsSpan(body + 4, (int)size - 4);
            }
            p = body + (int)size;
        }
        if (isFloat)
            throw new NotSupportedException("float PCM .caf files are not supported");

        var samples = pcm.ToArray();
        if (!littleEndian && bits == 16)
            for (int i = 0; i + 1 < samples.Length; i += 2)
                (samples[i], samples[i + 1]) = (samples[i + 1], samples[i]);

        int blockAlign = channels * bits / 8;
        var wav = new byte[44 + samples.Length];
        var w = wav.AsSpan();
        "RIFF"u8.CopyTo(w);
        BinaryPrimitives.WriteInt32LittleEndian(w[4..], 36 + samples.Length);
        "WAVEfmt "u8.CopyTo(w[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(w[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(w[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(w[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(w[24..], (int)rate);
        BinaryPrimitives.WriteInt32LittleEndian(w[28..], (int)rate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(w[32..], (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(w[34..], (short)bits);
        "data"u8.CopyTo(w[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(w[40..], samples.Length);
        samples.CopyTo(w[44..]);
        return wav;
    }
}
