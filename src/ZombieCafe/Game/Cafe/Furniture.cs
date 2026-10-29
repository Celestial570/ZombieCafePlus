using Raylib_cs;
using ZombieCafe.Data;
using ZombieCafe.Gfx;

namespace ZombieCafe.Game.Cafe;

/// <summary>
/// A piece of furniture standing on a cell, or hung on a wall (category WallDecor). Original class Furniture:
/// the sprite is drawn top-left-anchored at position + a per-image offset (FurnitureOffsets), mirrored for
/// some orientations. It can carry one object on top (a meal, a dirty plate, the dish shown on a counter)
/// and one piece of furniture (the pot on a stove).
/// </summary>
public class Furniture : CafeObject
{
    public FurnitureDef Def { get; }
    /// <summary>+0x44: 0..3; 0 = as drawn, 1 = mirrored (or the second image), 2/3 = second image (chairs: 4 facings).</summary>
    public int Orientation { get; private set; }
    /// <summary>+0x38: the cell this furniture stands on (-1 for wall decorations).</summary>
    public int Cell { get; set; } = -1;
    /// <summary>+0x3c: chair taken, table has a meal or a dirty plate on it, stove is cooking.</summary>
    public bool Busy { get; set; }
    /// <summary>+0x4c: the object on top (meal, dirty plate, dish on a counter).</summary>
    public CafeObject? Attached { get; private set; }
    /// <summary>+0x50: furniture fixed to this one (the pot on a stove).</summary>
    public Furniture? AttachedFurniture { get; private set; }
    /// <summary>+0x60: show the bobbing "!" sign (a stove waiting for its cook, a fridge with new food in it).</summary>
    public bool Alert { get; set; }
    /// <summary>
    /// +0x5e: what a cash item (vending machine, arcade, ATM...: def +0x26 a rate per hour, +0x28 the most it
    /// holds) has taken in and not yet been emptied of; def +0x24 says in what (1 cash, 2 toxin, else XP).
    /// </summary>
    public int Stored { get; set; }
    /// <summary>+0x74: the part of the next unit earned so far.</summary>
    public float StoredProgress { get; set; }
    /// <summary>+0x7a: the day a magic fridge last gave its free dish - set when bought (null: never).</summary>
    public DateTime? GiftDay { get; set; }
    /// <summary>Earns something by itself over time (def +0x26 above zero; not the daily toxin generator).</summary>
    public bool EarnsOverTime => Def.S26 > 0 && Def.Category != FurnitureCategory.ToxinGenerator;
    /// <summary>Draw tint instead of the def's (the placement ghost: green = fits, red = does not).</summary>
    public Color? TintOverride { get; set; }
    /// <summary>+0x6c / +0x70: top-left of the sprite.</summary>
    public float DrawX { get; private set; }
    public float DrawY { get; private set; }

    protected readonly GameData data;
    SubImage? image;
    bool mirrored;
    float offsetX, offsetY;

    public override CafeObjectType Type => CafeObjectType.Furniture;

    public Furniture(GameData data, FurnitureDef def)
    {
        this.data = data;
        Def = def;
        SetOrientation(0);
    }

    /// <summary>The object factory (FUN_001e1de0): stoves and counters are their own classes.</summary>
    public static Furniture Create(GameData data, FurnitureDef def) => def.Category switch
    {
        FurnitureCategory.Stove => new Stove(data, def),
        FurnitureCategory.Counter => new ServingCounter(data, def),
        FurnitureCategory.Habitat => new Habitat(data, def),
        _ => new Furniture(data, def),
    };

    public SubImage? Image => image;

    /// <summary>Full (untrimmed) width of the current sprite.</summary>
    public int FullWidth => image == null ? 0 : (int)(image.Width * image.Scale + image.TrimLeft + image.TrimRight);

    /// <summary>FUN_00083564 + Furniture::vf14: picks the image, mirroring and offset for an orientation.</summary>
    public void SetOrientation(int orientation)
    {
        Orientation = orientation;
        var sheet = data.Sheet(Def);
        if (sheet.Length == 0)
        {
            image = null;
            return;
        }
        var rec = FurnitureOffsets.Get((int)Def.Category, Def.ImageIndex);
        var second = sheet.Length > 1 ? sheet[1] : sheet[0];
        float ox, oy;
        switch (orientation)
        {
            // Habitat::updateOrientation: the picture of its state (empty, pet inside, blinking), mirrored for 1
            case 0 or 1 when this is Habitat h:
                (image, mirrored, ox, oy) = (sheet[Math.Clamp(h.ImageState, 0, sheet.Length - 1)], orientation == 1, rec[0], rec[1]);
                break;
            case 0:
                (image, mirrored, ox, oy) = (sheet[0], false, rec[0], rec[1]);
                break;
            case 1 when Def.Category == FurnitureCategory.Chair:
                (image, mirrored, ox, oy) = (sheet[0], true, rec[0], rec[1]);
                break;
            case 1 when sheet.Length > 1:
                (image, mirrored, ox, oy) = (sheet[1], false, rec[0], rec[1]);
                break;
            case 1:
                (image, mirrored, ox, oy) = (sheet[0], true, rec[0], rec[1]);
                break;
            case 2:
                (image, mirrored, ox, oy) = (second, false, rec[4], rec[5]);
                break;
            case 3:
                (image, mirrored, ox, oy) = (second, true, rec[4], rec[5]);
                break;
            default:
                (image, mirrored, ox, oy) = (sheet[0], false, 0, 0);
                break;
        }
        if (mirrored)
            ox = -ox - FullWidth;
        offsetX = ox;
        offsetY = oy;
        Width = FullWidth;
        Height = (int)(image.Height * image.Scale + image.TrimTop + image.TrimBottom);
        UpdateDrawPosition(null);
    }

    void UpdateDrawPosition(Cafe? cafe)
    {
        DrawX = offsetX + X;
        DrawY = offsetY + Y;
        if (cafe == null)
            return;
        if (Attached != null)
        {
            if (Attached is Furniture f)
                f.SetOrientation(Orientation);
            PositionAttachment(cafe, Attached);
        }
        if (AttachedFurniture != null)
        {
            AttachedFurniture.SetOrientation(Orientation);
            PositionAttachment(cafe, AttachedFurniture);
        }
    }

    public override void SetPosition(Cafe cafe, float x, float y)
    {
        base.SetPosition(cafe, x, y);
        UpdateDrawPosition(cafe);
    }

    /// <summary>Sort as if standing at <paramref name="y"/> (multi-cell furniture sorts by its topmost cell).</summary>
    public void SetSortY(Cafe cafe, float y)
    {
        int depth = (short)(int)y;
        var (u, v) = cafe.ScreenToDiamond((int)X, (int)y);
        if (u < cafe.OriginU && v > cafe.OriginV)
            depth += 5000;
        Depth = (short)depth;
    }

    public bool Depth2() => Def.Depth == 2;

    /// <summary>
    /// The mouse at (px, py) is on this piece: on a solid pixel of its sprite (or of the pot on it), give or
    /// take a few pixels so thin legs and edges are easy to catch - its transparent corners let the click through
    /// to whatever is behind (the original tests the sprite's box, with the base corners cut off).
    /// </summary>
    public bool HitTest(float px, float py)
    {
        if (image == null || !Visible)
            return false;
        if (Def.Category == FurnitureCategory.WallDecor)
            return px >= DrawX && px <= DrawX + Width && py >= DrawY && py <= DrawY + Height;
        const float slack = 3;
        int flip = mirrored ? Flip.Horizontal : Flip.None;
        for (int k = 0; k < 5; k++)
        {
            float x = px + (k == 1 ? slack : k == 2 ? -slack : 0), y = py + (k == 3 ? slack : k == 4 ? -slack : 0);
            if (image.Hit((int)DrawX, (int)DrawY, Anchor.Top | Anchor.Left, flip, 1, 1, x, y))
                return true;
        }
        return AttachedFurniture?.HitTest(px, py) == true;
    }

    /// <summary>
    /// A point on the piece that <see cref="HitTest"/> takes (test scripts click it): the middle of its sprite, or the
    /// nearest solid pixel to it.
    /// </summary>
    public System.Numerics.Vector2 SolidPoint()
    {
        float cx = DrawX + Width / 2f, cy = DrawY + Height / 2f;
        for (int r = 0; r < Math.Max(Width, Height); r += 4)
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4;
                float x = cx + r * MathF.Cos(a), y = cy + r * MathF.Sin(a);
                if (HitTest(x, y))
                    return new System.Numerics.Vector2(x, y);
            }
        return new System.Numerics.Vector2(cx, cy);
    }

    /// <summary>FUN_00199038: stand on a cell (position = cell centre).</summary>
    public void PlaceOn(Cafe cafe, int cell)
    {
        Cell = cell;
        var c = cafe.CellCenter(cell);
        SetPosition(cafe, c.X, c.Y);
    }

    /// <summary>vf09: put an object on top (it takes this furniture's orientation).</summary>
    public virtual bool Attach(Cafe cafe, CafeObject obj)
    {
        Attached = obj;
        if (obj is Furniture f)
            f.SetOrientation(Orientation);
        PositionAttachment(cafe, obj);
        return true;
    }

    /// <summary>vf10: fix a new piece of furniture (by id) to this one, replacing the old.</summary>
    public bool AttachFurniture(Cafe cafe, FurnitureDef def)
    {
        AttachedFurniture = new Furniture(data, def);
        AttachedFurniture.SetOrientation(Orientation);
        PositionAttachment(cafe, AttachedFurniture);
        return true;
    }

    /// <summary>vf11: take the object on top off (the caller owns it).</summary>
    public virtual CafeObject? Detach()
    {
        var o = Attached;
        Attached = null;
        return o;
    }

    /// <summary>vf12.</summary>
    public Furniture? DetachFurniture()
    {
        var o = AttachedFurniture;
        AttachedFurniture = null;
        return o;
    }

    /// <summary>Which pot-offset table a pot uses (FurnitureDef +0x60, set up by FUN_001e1514).</summary>
    int PotKind(FurnitureDef pot)
    {
        if (pot.Id == data.K.Int(0x42d744)) return 2;
        if (pot.Id == data.K.Int(0x42d740)) return 3;
        if (pot.Id == data.K.Int(0x42d748)) return 4;
        for (int fam = 0; fam < 17; fam++)
            if (data.BurntPotDef(fam).Id == pot.Id)
                return 1;
        return 0;
    }

    /// <summary>FUN_001e21e0: where an attached object goes.</summary>
    void PositionAttachment(Cafe cafe, CafeObject obj)
    {
        switch (Def.Category)
        {
            case FurnitureCategory.Counter:
            {
                var r = AttachOffsets.Get(AttachOffsets.Counter, Def.ImageIndex);
                obj.SetPosition(cafe, DrawX + r[0], DrawY + r[1]);
                break;
            }
            case FurnitureCategory.Table:
            {
                var r = AttachOffsets.Get(AttachOffsets.Table, Def.ImageIndex);
                obj.SetPosition(cafe, DrawX + r[0], DrawY + r[1]);
                break;
            }
            case FurnitureCategory.Stove when obj is Furniture pot:
            {
                var r = AttachOffsets.Get(AttachOffsets.Pot[PotKind(pot.Def)], Def.ImageIndex);
                float x = Orientation == 1 ? FullWidth - r[0] : r[0];
                obj.SetPosition(cafe, DrawX + x, DrawY + r[1]);
                break;
            }
            default:
                obj.SetPosition(cafe, DrawX, DrawY);
                break;
        }
    }

    public virtual void Update(Cafe cafe, int dtMs)
    {
    }

    /// <summary>FUN_00161b0c (furniture case): a cash item earns its rate per hour, one unit at a time, up to its cap.</summary>
    public void Earn(int dtMs)
    {
        if (!EarnsOverTime)
            return;
        StoredProgress += dtMs * Def.S26 / 3.6e6f;
        if (StoredProgress >= 1f)
        {
            StoredProgress -= 1f;
            Stored = Math.Min(Stored + 1, (int)Def.S28);
        }
    }

    /// <summary>Draws one sub-image of this furniture where its sprite normally goes (for sitters' chair parts).</summary>
    public (SubImage? Image, float X, float Y, bool Mirrored) SpriteAt() => (image, DrawX, DrawY, mirrored);

    public override void Draw(Cafe cafe, GameData data)
    {
        if (image == null || !Visible)
            return;
        var color = Colors.FromArgb(Def.Tint);
        if (Def.Category == FurnitureCategory.WallDecor)
        {
            // FUN_001e2648: hung relative to the wall's anchor, half a tile plus the wall inset along the wall
            int inset = (data.K.TileWidth >> 1) + data.K.WallInset;
            var rec = FurnitureOffsets.Get((int)Def.Category, Def.ImageIndex);
            int ox = (int)rec[0], oy = (int)rec[1];
            float x = Orientation is 1 or 2
                ? (int)X - inset - (ox + FullWidth)
                : ox + (int)X + inset;
            image.Draw(x, oy + (int)Y, Anchor.Top | Anchor.Left, Color.White, mirrored ? Flip.Horizontal : Flip.None);
            return;
        }
        // Furniture::drawFloor: the sprite, what sits on it, the pot, then the bobbing sign
        if (TintOverride is { } t)
            color = t;
        image.Draw((int)DrawX, (int)DrawY, Anchor.Top | Anchor.Left, color, mirrored ? Flip.Horizontal : Flip.None);
        Attached?.Draw(cafe, data);
        AttachedFurniture?.Draw(cafe, data);
        // the sign: "!" for a stove that needs a cook or a full cash item, else what the item is holding
        if ((Alert || Stored != 0) && data.IngameUi.Length > 36)
        {
            int icon = !Alert && Stored < Def.S28 ? Def.B24 switch { 1 => 28, 2 => 36, _ => 30 } : 30;
            data.IngameUi[icon].Draw(DrawX + Width * 0.5f, DrawY + cafe.Bob, Anchor.Bottom | Anchor.HCenter, Color.White);
        }
    }
}

/// <summary>
/// A meal on a table, the dish shown on a counter, or a dirty plate (original Food, a Furniture of the
/// plate type, 0xb4 bytes, FUN_0016d0cc): draws only its picture, at 30%, standing on its position.
/// </summary>
public sealed class FoodItem : Furniture
{
    /// <summary>+0xa8 (null for a dirty plate).</summary>
    public Dish? Dish { get; }
    /// <summary>+0xac.</summary>
    public SubImage? Picture { get; }
    /// <summary>+0xb0: tip rating of the zombie who served it.</summary>
    public int ServerTipRating { get; }

    public FoodItem(GameData data, Dish? dish, int serverTipRating)
        : this(data, data.Furniture[data.K.PlateDef], dish, serverTipRating, null)
    {
    }

    FoodItem(GameData data, FurnitureDef def, Dish? dish, int serverTipRating, SubImage? picture) : base(data, def)
    {
        if (dish != null)
            Dish = new Dish(dish.Food, data.Foods[dish.Food].ServingsWith(dish.Modifier), dish.Modifier);
        ServerTipRating = serverTipRating;
        Picture = picture ?? (dish != null ? data.FoodImage(dish.Food) : null);
    }

    public bool IsDirtyPlate => Def.Id == data.K.DirtyPlateDef;

    /// <summary>The dirty plate customers leave (EatTask: a Food of the dirty-plate type showing xxx-dirtyPlate.png).</summary>
    public static FoodItem DirtyPlate(GameData data) =>
        new(data, data.Furniture[data.K.DirtyPlateDef], null, 0,
            data.Sheet("xxx-dirtyPlate") is { Length: > 0 } s ? s[0] : null);

    public override void Draw(Cafe cafe, GameData data)
    {
        // Food::draw: picture at 0.3 scale, anchored bottom-centre on the position
        float s = data.K.CarriedFoodScale;
        Picture?.Draw(X, Y, Anchor.Bottom | Anchor.HCenter, Color.White, Flip.None, s, s);
    }
}
