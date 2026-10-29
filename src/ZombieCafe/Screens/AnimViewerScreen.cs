using System.Numerics;
using Raylib_cs;
using ZombieCafe.Assets;
using ZombieCafe.Core;
using ZombieCafe.Game;
using ZombieCafe.Gfx;

namespace ZombieCafe.Screens;

/// <summary>Developer tool: plays a character's .anim files side by side (--viewer anim [--anim parts a.anim ...]).</summary>
public sealed class AnimViewerScreen : GameScreen
{
    readonly SubImage[] parts;
    readonly List<(string Name, AnimFile Anim)> anims = new();
    readonly string partsName;
    double time;

    public AnimViewerScreen(GameData data, string[] args)
    {
        int ai = Array.IndexOf(args, "--anim");
        partsName = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "000-Man-zombieParts";
        for (int i = ai + 2; ai >= 0 && i < args.Length && !args[i].StartsWith("--"); i++)
            anims.Add((args[i], AnimFile.Parse(data.Ipa.Read(args[i]))));
        if (anims.Count == 0)
            foreach (var n in new[] { "walkSW.anim", "walkNW.anim", "idleSW.anim", "sitSW.anim", "eatSW.anim", "attackSW.anim" })
                anims.Add((n, AnimFile.Parse(data.Ipa.Read(n))));
        parts = data.Sheet(partsName, partsName + ".bin");
    }

    public override void Update(float dt) => time += dt;

    public override void Draw()
    {
        Raylib.ClearBackground(new Color(70, 76, 88, 255));
        int sw = Screen.PixelWidth, sh = Screen.PixelHeight;
        for (int k = 0; k < anims.Count; k++)
        {
            var (name, anim) = anims[k];
            int f = (int)(time * 1000 / Math.Max(1, anim.FrameTimeMs)) % anim.Frames.Count;
            float x = sw * (k + 0.5f) / anims.Count, y = sh * 0.62f;
            Gfx.Draw.Reset(Matrix3x2.CreateScale(1.4f) * Matrix3x2.CreateTranslation(x, y));
            foreach (var p in anim.Frames[f])
            {
                if (p.Index >= parts.Length)
                    continue;
                var img = parts[p.Index];
                Gfx.Draw.Push(new Matrix3x2(p.A, p.B, p.C, p.D, p.Tx, p.Ty));
                img.DrawAt(-img.FullWidth / 2f + img.TrimLeft, -img.FullHeight / 2f + img.TrimTop, img.Width, img.Height, Color.White);
                Gfx.Draw.Pop();
            }
            Gfx.Draw.Reset(Matrix3x2.Identity);
            Raylib.DrawText($"{name} {f + 1}/{anim.Frames.Count}", (int)x - 70, (int)(sh * 0.9f), 16, Color.RayWhite);
        }
        Raylib.DrawText(partsName, 20, 20, 20, Color.RayWhite);
    }
}
