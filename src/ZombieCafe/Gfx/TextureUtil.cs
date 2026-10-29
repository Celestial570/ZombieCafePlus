using Raylib_cs;

namespace ZombieCafe.Gfx;

static unsafe class TextureUtil
{
    /// <summary>Uploads RGBA8888 pixels as a bilinear-filtered GPU texture.</summary>
    public static Texture2D Upload(int width, int height, byte[] rgba)
    {
        fixed (byte* p = rgba)
        {
            var img = new Image
            {
                Data = p,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8,
            };
            var tex = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
            AlphaMasks.Record(tex.Id, width, height, rgba);
            return tex;
        }
    }
}
