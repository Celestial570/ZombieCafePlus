using Raylib_cs;
using ZombieCafe.Assets;

namespace ZombieCafe.Gfx;

/// <summary>Loads the game's images (.cct, .png, .jpg) from the .ipa on first use and keeps them on the GPU.</summary>
public sealed unsafe class TextureStore : IDisposable
{
    readonly IpaArchive ipa;
    readonly Dictionary<string, Texture2D> cache = new(StringComparer.OrdinalIgnoreCase);

    public TextureStore(IpaArchive ipa) => this.ipa = ipa;

    public bool Exists(string name) => ipa.Exists(name);

    /// <summary>Returns the texture, or a zero-id texture if the file does not exist.</summary>
    public Texture2D Get(string name)
    {
        if (cache.TryGetValue(name, out var t))
            return t;
        t = Load(name);
        cache[name] = t;
        return t;
    }

    Texture2D Load(string name)
    {
        var bytes = ipa.TryRead(name);
        if (bytes == null)
            return default;
        if (CctImage.IsCct(bytes))
        {
            var img = CctImage.Decode(bytes);
            return TextureUtil.Upload(img.Width, img.Height, img.Rgba);
        }
        string ext = Path.GetExtension(name).ToLowerInvariant();
        var image = Raylib.LoadImageFromMemory(ext, bytes);
        if (image.Data == null)
            return default;
        Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
        var tex = Raylib.LoadTextureFromImage(image);
        AlphaMasks.Record(tex.Id, image.Width, image.Height, new ReadOnlySpan<byte>(image.Data, image.Width * image.Height * 4));
        Raylib.UnloadImage(image);
        Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
        return tex;
    }

    /// <summary>
    /// A grey copy of a .cct image (FUN_000a2944 in mode 1: every pixel the mean of its red, green and blue, its
    /// alpha kept) - the ZombiePedia's zombies not had yet.
    /// </summary>
    public Texture2D GetGrey(string name)
    {
        string key = "grey:" + name;
        if (cache.TryGetValue(key, out var t))
            return t;
        var bytes = ipa.TryRead(name);
        if (bytes == null || !CctImage.IsCct(bytes))
        {
            cache[key] = default;
            return default;
        }
        var img = CctImage.Decode(bytes);
        var px = (byte[])img.Rgba.Clone();
        for (int i = 0; i + 3 < px.Length; i += 4)
            px[i] = px[i + 1] = px[i + 2] = (byte)((px[i] + px[i + 1] + px[i + 2]) / 3);
        t = TextureUtil.Upload(img.Width, img.Height, px);
        cache[key] = t;
        return t;
    }

    public void Unload(string name)
    {
        if (cache.Remove(name, out var t) && t.Id != 0)
            Raylib.UnloadTexture(t);
    }

    public void Dispose()
    {
        foreach (var t in cache.Values)
            if (t.Id != 0)
                Raylib.UnloadTexture(t);
        cache.Clear();
    }
}
