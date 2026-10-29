using Raylib_cs;
using ZombieCafe.Data;
using ZombieCafe.Gfx;

namespace ZombieCafe.Game.Cafe;

/// <summary>Object types returned by the original CafeObject::getType (vtable slot 5).</summary>
public enum CafeObjectType
{
    None = 0,
    Furniture = 1,
    Wall = 2,
    Character = 3,
    /// <summary>What is left of an eaten customer.</summary>
    BonePile = 8,
}

/// <summary>Anything placed in the cafe and drawn in depth order (original CafeObject).</summary>
public abstract class CafeObject
{
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Width { get; protected set; }
    public float Height { get; protected set; }
    /// <summary>Draw order key (+0x2c): y, +10 for walls, +5000 for objects inside the room.</summary>
    public int Depth { get; protected set; }

    public abstract CafeObjectType Type { get; }

    /// <summary>+0x18: drawn at all (a chair hides while someone sits on it; the sitter draws it instead).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Raised when the object leaves the cafe (the original's per-object listener list, FUN_0019909c).</summary>
    public event Action<CafeObject>? Removed;

    public void NotifyRemoved() => Removed?.Invoke(this);

    /// <summary>CafeObject::setPosition (vtable slot 3).</summary>
    public virtual void SetPosition(Cafe cafe, float x, float y)
    {
        X = x;
        Y = y;
        int depth = (short)(int)y;
        var (u, v) = cafe.ScreenToDiamond((int)x, (int)y);
        // walls get +10 here and CafeWall::setPosition takes it off again; everything else standing
        // inside the room sorts after the whole street
        if (Type != CafeObjectType.Wall && u < cafe.OriginU && v > cafe.OriginV)
            depth += 5000;
        Depth = (short)depth;
    }

    public abstract void Draw(Cafe cafe, GameData data);

    public bool HitTest(float px, float py) => X <= px && px <= X + Width && Y <= py && py <= Y + Height;
}

/// <summary>A wall segment on the back (upper-left = side 0, upper-right = side 1) edge of a cell.</summary>
public sealed class CafeWall : CafeObject
{
    public int Side { get; }
    public FurnitureDef Def { get; private set; }
    /// <summary>+0x45: this wall is an open doorway.</summary>
    public bool IsDoor { get; set; }
    /// <summary>+0x46: the wall next to a doorway (draws the door frame).</summary>
    public bool IsDoorNeighbour { get; set; }
    /// <summary>+0x48: window, poster etc. hung on this wall (drawn by the wall, moves with it).</summary>
    public Furniture? Decor { get; private set; }

    public override CafeObjectType Type => CafeObjectType.Wall;

    /// <summary>FUN_00163e34 / FUN_000a4204: hang a wall decoration (its orientation follows the wall's side).</summary>
    public void Hang(Cafe cafe, Furniture? decor)
    {
        Decor = decor;
        if (decor == null)
            return;
        decor.SetOrientation(Side != 0 ? 1 : 0);
        decor.SetPosition(cafe, X, Y);
    }

    public override void SetPosition(Cafe cafe, float x, float y)
    {
        base.SetPosition(cafe, x, y);
        Decor?.SetPosition(cafe, x, y);
    }

    public CafeWall(GameData data, int imageIndex, int tint, int side)
    {
        Side = side;
        SetDef(data, data.WallDef(imageIndex, tint));
    }

    /// <summary>Repaint the wall with another wall type from the store.</summary>
    public void SetDef(GameData data, FurnitureDef def)
    {
        Def = def;
        var sheet = data.Sheet(Def);
        Width = data.K.TileWidth >> 1;
        Height = sheet.Length > 0 ? (int)(sheet[0].Height * sheet[0].Scale + sheet[0].TrimTop + sheet[0].TrimBottom) : 0;
    }

    /// <summary>Screen box of the wall panel (for picking it with the mouse).</summary>
    public bool Contains(float px, float py)
    {
        float left = Side == 0 ? X : X - Width;
        return px >= left && px <= left + Width && py <= Y && py >= Y - Height;
    }

    // the door's pictures: "Door Close" (wall type 0x1f) standing in the doorway, "Door Open" (0x20) swung back against
    // the wall beside it (wallDefFor's table, FUN_000a38f0)
    const int DoorClosedDef = 0x250, DoorOpenDef = 0x251;

    public override void Draw(Cafe cafe, GameData data)
    {
        var sheet = data.Sheet(Def);
        if (sheet.Length == 0)
            return;
        int color = Def.Tint;
        if (Side != 0)
            color = Colors.AddRgb(color, -51);
        int anchor = Side == 0 ? Anchor.Left | Anchor.Bottom : Anchor.Right | Anchor.Bottom;
        int flip = Side == 0 ? Flip.None : Flip.Horizontal;
        // CafeWall::draw: the far side's door is shaded like its wall
        var doorTint = Side == 0 ? Color.White : new Color(0xbb, 0xbb, 0xbb, 0xff);
        int sign = Side == 0 ? 1 : -1;
        var k = data.K;
        if (IsDoor)
        {
            // a doorway: only the wall above the door, and the door standing in it while the cafe is shut and nobody
            // is passing (K 0x42ced0 in along the wall, K 0x42ced4 up)
            var closed = data.Sheet(data.Furniture[DoorClosedDef]);
            if (closed.Length > 0)
            {
                DrawAboveDoor(sheet[0], closed[0], anchor, flip, Colors.FromArgb(color));
                if (!cafe.IsOpen && cafe.DoorTraffic == 0)
                    closed[0].Draw((int)X + sign * k.Int(0x42ced0), (int)Y + k.Int(0x42ced4), anchor, doorTint, flip);
            }
            Decor?.Draw(cafe, data);
            return;
        }
        sheet[0].Draw((int)X, (int)Y, anchor, Colors.FromArgb(color), flip);
        // the wall beside a doorway: the door folded back against it while the cafe is open or someone is passing
        if (IsDoorNeighbour && (cafe.IsOpen || cafe.DoorTraffic > 0))
        {
            var open = data.Sheet(data.Furniture[DoorOpenDef]);
            if (open.Length > 0)
                open[0].Draw((int)X - sign * (Width - k.Int(0x42ced0)), (int)Y + k.Int(0x42ced4), anchor,
                    Color.White, flip);
        }
        Decor?.Draw(cafe, data);
    }

    /// <summary>
    /// The strip of wall over a doorway (CafeWall::draw's door mesh): the wall picture's slanted top band, as deep as
    /// the wall stands taller than the door - its slant read from the pictures' own solid pixels.
    /// </summary>
    void DrawAboveDoor(SubImage wall, SubImage door, int anchor, int flip, Color tint)
    {
        int ww = wall.Width, wh = wall.Height;
        float top0 = FirstSolidRow(wall, 2), top1 = FirstSolidRow(wall, ww - 3);
        float doorTop0 = FirstSolidRow(door, 2), doorTop1 = FirstSolidRow(door, door.Width - 3);
        // the face heights: the wall's picture, its door's (each less the top edge's slant), with a few rows of overlap
        float wallFace = wh - Math.Abs(top1 - top0) - Math.Min(top0, top1);
        float doorFace = door.Height - Math.Abs(doorTop1 - doorTop0) - Math.Min(doorTop0, doorTop1);
        float band = Math.Clamp(wallFace - doorFace + 6, 4, wh);
        // where the picture is drawn (as SubImage.Draw places it)
        int w = (int)(wall.Scale * ww), h = (int)(wall.Scale * wh);
        float x = (anchor & Anchor.Right) != 0 ? X - wall.TrimRight - w : X + wall.TrimLeft;
        float y = Y - wall.TrimBottom - h;
        float s = wall.Scale;
        var src = wall.Src;
        // texel columns at the left and right of the drawn picture (mirrored for the other side)
        float uL = (flip & Flip.Horizontal) != 0 ? src.X + ww : src.X, uR = (flip & Flip.Horizontal) != 0 ? src.X : src.X + ww;
        float tL = (flip & Flip.Horizontal) != 0 ? top1 : top0, tR = (flip & Flip.Horizontal) != 0 ? top0 : top1;
        Gfx.Draw.QuadUv(wall.Texture,
            new System.Numerics.Vector2(x, y + tL * s), new System.Numerics.Vector2(x + w, y + tR * s),
            new System.Numerics.Vector2(x + w, y + (tR + band) * s), new System.Numerics.Vector2(x, y + (tL + band) * s),
            new System.Numerics.Vector2(uL, src.Y + tL), new System.Numerics.Vector2(uR, src.Y + tR),
            new System.Numerics.Vector2(uR, src.Y + tR + band), new System.Numerics.Vector2(uL, src.Y + tL + band), tint);
    }

    /// <summary>The first solid row of a column of a picture (counted from its top), 0 when unknown.</summary>
    static float FirstSolidRow(SubImage img, int column)
    {
        int cx = (int)img.Src.X + Math.Clamp(column, 0, img.Width - 1);
        for (int r = 0; r < img.Height; r++)
            if (AlphaMasks.IsSolid(img.Texture.Id, cx, (int)img.Src.Y + r) is not false)
                return r;
        return 0;
    }
}
