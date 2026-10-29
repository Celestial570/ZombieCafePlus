using System.Numerics;
using Raylib_cs;
using ZombieCafe.Data;
using ZombieCafe.Gfx;

namespace ZombieCafe.Game.Cafe;

/// <summary>One map cell (original 0x18-byte struct): floor tile, tint, mirror flag and up to three objects
/// (slot 0 = wall on the upper-left edge, slot 1 = wall on the upper-right edge, slot 2 = object on the floor).</summary>
public sealed class CafeCell
{
    public int Floor;
    public int Tint = unchecked((int)0xffffffff);
    public bool Flip;
    public readonly CafeObject?[] Slots = new CafeObject?[3];
}

/// <summary>What the cafe simulation needs from the game around it (hints, sounds, progress, the chef).</summary>
public interface ICafeEvents
{
    /// <summary>FUN_0014cb88: tell the tutorial/hint system that something happened.</summary>
    void Hint(int id, bool flag);
    /// <summary>FUN_00053dd4: play a sound effect by id.</summary>
    void Sound(int id);
    /// <summary>One of the eight hand-over sounds (DAT_0037a230: 59, 60, 61, 192..196).</summary>
    int RandomServeSound();
    /// <summary>Dishes only burn once the tutorial's cooking step is done (objective 5).</summary>
    bool BurningEnabled { get; }
    /// <summary>A double-experience bonus is running (FUN_000f54c4(5) == 2).</summary>
    bool DoubleXpEvent { get; }
    /// <summary>The player's chef: cook speed (F40), tip multiplier (I44), cooking XP (F4C).</summary>
    CharacterDef Chef { get; }
    /// <summary>FUN_000da190(..., 0): a dish reached a counter (the rest of its cooking experience).</summary>
    void DishPlaced(Dish dish, CharacterInstance by);
    /// <summary>What rest gains besides the barrels and the chef (0.1 with the Super Server tombstone placed).</summary>
    float RestBonus => 0f;
    /// <summary>FUN_0014cb68: a hint has been shown (some tips wait for others).</summary>
    bool HintShown(int id);
}

/// <summary>
/// The cafe and the street around it: a staggered isometric map (original "Cafe", no vtable). Cells are
/// indexed row * MapWidth + col; odd rows are shifted half a cell right. The room is a rectangle in
/// diamond coordinates (u, v): u in [OriginU - RoomWidth, OriginU), v in (OriginV, OriginV + RoomDepth].
/// </summary>
public sealed class Cafe
{
    readonly GameData data;
    public GameData Data => data;
    /// <summary>The cafe's random source (ZC_SEED=n makes test runs repeatable).</summary>
    public Random Rng { get; } = int.TryParse(Environment.GetEnvironmentVariable("ZC_SEED"), out int seed) ? new Random(seed) : new();
    public ICafeEvents Events { get; set; } = null!;
    public int MapWidth { get; }
    public int MapHeight { get; }
    public CafeCell[] Cells { get; }
    public int OriginU { get; private set; }
    public int OriginV { get; private set; }
    public int RoomWidth { get; private set; }
    public int RoomDepth { get; private set; }
    /// <summary>+0x70: open for business (the doorway is in the walking graph). Raid cafes are always open.</summary>
    public bool IsOpen { get; private set; }
    /// <summary>+0x8c / +0x8e: the two far ends of the sidewalk where passers-by appear and vanish.</summary>
    public int StreetEndA { get; private set; }
    public int StreetEndB { get; private set; }
    /// <summary>+0x98: customers sitting and waiting to be served.</summary>
    public List<CharacterInstance> Waiting { get; } = new();
    /// <summary>+0xa4: waiting customers no zombie can reach.</summary>
    public List<CharacterInstance> Unreachable { get; } = new();
    /// <summary>+0xcc: yard cells taken by resting zombies.</summary>
    public HashSet<int> RestSpots { get; } = new();
    /// <summary>+0xfc: the storage box - furniture ids put away, to be placed again for nothing.</summary>
    public List<int> Stored { get; } = new();
    /// <summary>+0xb0: tables with a dirty plate on them.</summary>
    public List<Furniture> DirtyTables { get; } = new();
    /// <summary>+0xc3: no serving experience bonuses here (visited cafes).</summary>
    public bool NoServeBonus { get; set; }
    /// <summary>+0xc4: kitchen upgrade level (1 doubles burn time, 2+ never burn, 3 gives 10% more servings).</summary>
    public int BurnMode { get; set; }
    /// <summary>+0xe0: characters crossing the room boundary right now.</summary>
    public int DoorTraffic { get; set; }
    /// <summary>The bobbing offset of "needs a cook" signs (12.5 sin(2 pi t), game +0x35c).</summary>
    public float Bob { get; set; }

    readonly List<StatusNote> notes = new();
    public IReadOnlyList<StatusNote> Notes => notes;
    // CafeUIElements drawn over everything (the "censored" sign over a zombie's meal)
    readonly List<CensoredSign> overlays = new();
    public void AddOverlay(CensoredSign s) => overlays.Add(s);
    /// <summary>The cafe's own clock (ms since it was built), for sound cooldowns.</summary>
    public long ClockMs { get; private set; }
    public void AddNote(StatusNote n) => notes.Add(n);
    public void RemoveNote(StatusNote n) => notes.Remove(n);

    readonly int tileW, tileH, stepX, stepY;
    readonly List<CafeObject> renderList = new();

    public IReadOnlyList<CafeObject> Objects => renderList;

    Cafe(GameData data)
    {
        this.data = data;
        var k = data.K;
        MapWidth = k.MapWidth;
        MapHeight = k.MapHeight;
        tileW = k.TileWidth;
        tileH = k.TileHeight;
        stepX = tileW + k.TileSpacingX;
        stepY = tileH + k.TileSpacingY;
        Cells = new CafeCell[MapWidth * MapHeight];
        for (int i = 0; i < Cells.Length; i++)
            Cells[i] = new CafeCell();
    }

    // ---------------------------------------------------------------- coordinates

    public (int Col, int Row) ColRow(int index) => (index % MapWidth, index / MapWidth);

    /// <summary>Staggered (col, row) to diamond (u, v) (FUN_001641c4).</summary>
    public static (int U, int V) ToDiamond(int col, int row)
    {
        int v = col + ((row + 1) >> 1);
        return (v - row, v);
    }

    public (int U, int V) Diamond(int index)
    {
        var (c, r) = ColRow(index);
        return ToDiamond(c, r);
    }

    /// <summary>Diamond (u, v) to cell index (FUN_0015f274), or -1 when off the map.</summary>
    public int IndexOf(int u, int v)
    {
        int row = v - u;
        int col = (u + v - (row & 1)) >> 1;
        if (row < 0 || row >= MapHeight || col < 0 || col >= MapWidth)
            return -1;
        return row * MapWidth + col;
    }

    /// <summary>Top-left of a cell's floor quad (the floor mesh layout of FUN_0015f2a4).</summary>
    public Vector2 CellTopLeft(int index)
    {
        var (c, r) = ColRow(index);
        int x = stepX * c + ((r & 1) != 0 ? stepX >> 1 : 0);
        int y = (stepY * r) >> 1;
        return new Vector2(x, y);
    }

    /// <summary>Corner k of a cell quad: 0 top-left, 1 bottom-left, 2 top-right, 3 bottom-right.</summary>
    public Vector2 CellVertex(int index, int k)
    {
        var tl = CellTopLeft(index);
        return tl + new Vector2((k & 2) != 0 ? tileW : 0, (k & 1) != 0 ? tileH : 0);
    }

    /// <summary>Cell centre (FUN_0015fe18: midpoint of corners 0 and 3, in integers).</summary>
    public Vector2 CellCenter(int index)
    {
        var a = CellVertex(index, 0);
        var b = CellVertex(index, 3);
        return new Vector2(((int)a.X + (int)b.X) >> 1, ((int)a.Y + (int)b.Y) >> 1);
    }

    /// <summary>Screen (world) position to diamond cell coordinates (FUN_001641ec).</summary>
    public (int U, int V) ScreenToDiamond(float x, float y)
    {
        float a = x / stepX, b = y / stepY;
        return ((int)MathF.Floor(a + 0.5f - b), (int)MathF.Floor(a + (b - 0.5f)));
    }

    /// <summary>Neighbouring cell (FUN_00161684): 0 = +v, 1 = -u, 2 = -v, 3 = +u; -1 at the map edge.</summary>
    public int Neighbour(int index, int dir)
    {
        int w = MapWidth, n = MapWidth * MapHeight;
        int odd = (index / w) & 1;
        switch (dir)
        {
            case 0:
                if (index < n - w && (index + 1) % (w * 2) != 0) return odd + index + w;
                break;
            case 1:
                if (index < n - w && index % (w * 2) != 0) return odd + index + w - 1;
                break;
            case 2:
                if (w <= index && index % (w * 2) != 0) return odd + index - 1 - w;
                break;
            case 3:
                if (w <= index && (index + 1) % (w * 2) != 0) return odd + index - w;
                break;
        }
        return -1;
    }

    public bool InRoom(int u, int v) => u < OriginU && u >= OriginU - RoomWidth && v > OriginV && v <= OriginV + RoomDepth;

    // ---------------------------------------------------------------- navigation

    public NavGraph Nav { get; private set; } = null!;
    /// <summary>+0x6c: cost of one cell step (the diagonal of a cell: sqrt(160^2 + 100^2)).</summary>
    public float StepCost { get; private set; }

    bool IsGrass(int cell)
    {
        int f = Cells[cell].Floor;
        var grass = data.K.GrassTiles;
        int n = Math.Min(grass.Length, data.K.GrassTileCount);
        for (int i = 0; i < n; i++)
            if (grass[i] == f)
                return true;
        return false;
    }

    /// <summary>Second half of Cafe::buildFloorMesh (FUN_0015f2a4): nodes, room/street edges, yard lanes.</summary>
    void BuildNavGraph()
    {
        int w = MapWidth, n = MapWidth * MapHeight;
        Nav = new NavGraph(n);
        StepCost = MathF.Sqrt(stepY * stepY + stepX * stepX);
        for (int i = 0; i < n; i++)
        {
            Nav.SetPosition(i, CellCenter(i));
            var (u, v) = Diamond(i);
            bool skip = IsGrass(i) && (OriginU < u || v < RoomDepth);
            if (!skip)
            {
                int row = i / w;
                // -u neighbour, through its upper-right wall only if that is an open (connected) door
                if (i < n - w && i % (w * 2) != 0)
                {
                    int nb = i + w + (row & 1) - 1;
                    if (nb >= 0 && WallLets(Cells[nb].Slots[1]) && !IsGrass(nb))
                        Nav.Connect(i, nb, StepCost);
                }
                // +v neighbour, through its upper-left wall likewise
                if (i < n - w && (i + 1) % (w * 2) != 0)
                {
                    int nb = i + w + (row & 1);
                    if (nb >= 0 && WallLets(Cells[nb].Slots[0]) && !IsGrass(nb))
                        Nav.Connect(i, nb, StepCost);
                }
            }
            if (Cells[i].Slots[2] != null)
                Nav.SetPassable(i, false);
        }
        // the yard in front of the room, 6 cells deep (L-shaped): strip A along the -u side ...
        for (int lane = 0; lane < 6; lane++)
        {
            int u = OriginU - RoomWidth - 1 - lane;
            for (int k = 0; k <= RoomDepth + 5; k++)
            {
                int cell = IndexOf(u, OriginV + 1 + k);
                if (cell < 0)
                    continue;
                Nav.SetPassable(cell, true);
                if (k < RoomDepth + 5)
                    Nav.Connect(cell, Neighbour(cell, 0), StepCost);
                if (lane < 5)
                    Nav.Connect(cell, Neighbour(cell, 1), StepCost);
            }
        }
        // ... and strip B along the +v side
        for (int j = 0; j <= RoomWidth; j++)
        {
            for (int k = 0; k < 6; k++)
            {
                int cell = IndexOf(OriginU - j, OriginV + RoomDepth + 1 + k);
                if (cell < 0)
                    continue;
                Nav.SetPassable(cell, true);
                if (k < 5)
                    Nav.Connect(cell, Neighbour(cell, 0), StepCost);
                Nav.Connect(cell, Neighbour(cell, 1), StepCost);
            }
        }
    }

    bool WallLets(CafeObject? o) => o is not CafeWall wall || (wall.IsDoor && IsOpen);

    // ---------------------------------------------------------------- objects

    public void Add(CafeObject obj)
    {
        // FUN_00013158: insert before the first object that sorts after it
        int i = 0;
        while (i < renderList.Count && renderList[i].Depth <= obj.Depth)
            i++;
        renderList.Insert(i, obj);
    }

    /// <summary>Takes an object out of the cafe and tells everyone following it.</summary>
    public void Remove(CafeObject obj)
    {
        renderList.Remove(obj);
        obj.NotifyRemoved();
    }

    /// <summary>FUN_00013008: move an object to its place in depth order after it moved.</summary>
    public void Resort(CafeObject obj)
    {
        int i = renderList.IndexOf(obj);
        if (i < 0)
            return;
        while (i > 0 && renderList[i - 1].Depth > obj.Depth)
        {
            renderList[i] = renderList[i - 1];
            renderList[--i] = obj;
        }
        while (i < renderList.Count - 1 && renderList[i + 1].Depth < obj.Depth)
        {
            renderList[i] = renderList[i + 1];
            renderList[++i] = obj;
        }
    }

    /// <summary>Furniture (stoves burn and time out their cooks) and the floating notes.</summary>
    public void Update(int dtMs)
    {
        ClockMs += dtMs;
        foreach (var o in renderList.ToArray())
        {
            if (o is Furniture f)
            {
                f.Update(this, dtMs);
                f.Earn(dtMs);
            }
            else if (o is ITimedObject t)
            {
                t.Tick(dtMs);
                if (t.Expired)
                    Remove(o);
            }
        }
        overlays.RemoveAll(s => s.Gone);
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            notes[i].Update(dtMs);
            if (notes[i].IsFinished)
                notes.RemoveAt(i);
        }
    }

    /// <summary>Puts a character on a cell (CafeObject::placeOnCell) and into the cafe.</summary>
    public CharacterInstance AddCharacter(Character character, int cell)
    {
        var c = new CharacterInstance(this, character) { Cell = cell, NextCell = cell };
        var p = CellCenter(cell);
        c.SetPosition(this, p.X, p.Y);
        Add(c);
        return c;
    }

    /// <summary>CafeObject::placeOnCell.</summary>
    public void PlaceOnCell(CharacterInstance c, int cell)
    {
        c.Cell = cell;
        c.NextCell = cell;
        var p = CellCenter(cell);
        c.SetPosition(this, p.X, p.Y);
        Resort(c);
    }

    // ---------------------------------------------------------------- queries used by the jobs

    /// <summary>FUN_00161774 / FUN_000de448: the cell has a cafe floor (not street, sidewalk or grass).</summary>
    public bool IsInside(int cell)
    {
        if (cell < 0 || cell >= Cells.Length)
            return false;
        int f = Cells[cell].Floor;
        var k = data.K;
        if (f == k.TileSidewalk || f == k.TileCurb || f == k.TileSidewalkCorner || f == k.TileRoad || f == k.TileRoadStripe)
            return false;
        return !IsGrass(cell);
    }

    /// <summary>FUN_00199060: b is one of a's four neighbours.</summary>
    public bool AreNeighbours(int a, int b)
    {
        if (a < 0 || b < 0)
            return false;
        for (int d = 0; d < 4; d++)
            if (Neighbour(a, d) == b)
                return true;
        return false;
    }

    public Furniture? FurnitureAt(int cell) => cell >= 0 && cell < Cells.Length ? Cells[cell].Slots[2] as Furniture : null;

    /// <summary>
    /// Cafe::findFurnitureByCategory: furniture of a category ordered by diamond distance from a cell
    /// (FUN_001622fc; equal distances keep the later object first, as the original's sorted insert does).
    /// </summary>
    public List<Furniture> FindFurniture(FurnitureCategory category, int nearCell)
    {
        var (cu, cv) = nearCell >= 0 ? Diamond(nearCell) : (0, 0);
        int Dist(Furniture f)
        {
            var (u, v) = Diamond(f.Cell);
            return Math.Abs(u - cu) + Math.Abs(v - cv);
        }
        var result = new List<Furniture>();
        foreach (var o in renderList)
        {
            if (o is not Furniture f || f.Def.Category != category || f.Cell < 0)
                continue;
            int d = Dist(f), i = 0;
            while (i < result.Count && d > Dist(result[i]))
                i++;
            result.Insert(i, f);
        }
        return result;
    }

    /// <summary>FUN_001627d4: the table a chair faces.</summary>
    public Furniture? TableInFrontOf(Furniture chair)
    {
        var t = FurnitureAt(Neighbour(chair.Cell, chair.Orientation));
        return t != null && t.Def.Category == FurnitureCategory.Table ? t : null;
    }

    /// <summary>FUN_001625a8: the table in front of the chair on a cell.</summary>
    public Furniture? TableAtSeat(int cell)
    {
        var chair = FurnitureAt(cell);
        return chair != null && chair.Def.Category == FurnitureCategory.Chair ? TableInFrontOf(chair) : null;
    }

    /// <summary>FUN_00162804: an occupied chair next door faces into this chair's cell.</summary>
    public bool ChairBlocked(Furniture chair)
    {
        for (int d = 0; d < 4; d++)
        {
            var other = FurnitureAt(Neighbour(chair.Cell, d));
            if (other == null || !other.Busy || other.Def.Category != FurnitureCategory.Chair)
                continue;
            int facing = d switch { 0 => 2, 1 => 3, 2 => 0, _ => 1 };
            if (other.Orientation == facing)
                return true;
        }
        return false;
    }

    /// <summary>FUN_00162880: servings on all counters.</summary>
    public int TotalServings()
    {
        int n = 0;
        foreach (var o in renderList)
            if (o is ServingCounter c)
                n += c.TotalServings;
        return n;
    }

    /// <summary>Counter score: servings stacked on it, 1000 less when its top dish is this food.</summary>
    static int CounterScore(ServingCounter c, int food)
    {
        int n = 0;
        for (int i = 0; i < c.Dishes.Count; i++)
        {
            n += c.Dishes[i].Servings;
            if (i == c.Dishes.Count - 1 && c.Dishes[i].Food == food)
                n -= 1000;
        }
        return n;
    }

    /// <summary>FUN_001628c0: where a dish of this food goes (same food on top, else the emptiest).</summary>
    public ServingCounter? BestCounterFor(int food)
    {
        ServingCounter? best = null;
        int bestScore = int.MaxValue;
        foreach (var f in FindFurniture(FurnitureCategory.Counter, -1))
        {
            if (f is not ServingCounter c)
                continue;
            int s = CounterScore(c, food);
            if (s < bestScore)
            {
                bestScore = s;
                best = c;
            }
            if (bestScore == 0)
                break;
        }
        return best;
    }

    /// <summary>FUN_00162b0c: counters to try for a dish, best score first, then nearest.</summary>
    public List<ServingCounter> CountersFor(int food, int nearCell)
    {
        var (cu, cv) = Diamond(nearCell);
        int Dist(ServingCounter c)
        {
            var (u, v) = Diamond(c.Cell);
            return Math.Abs(u - cu) + Math.Abs(v - cv);
        }
        var result = new List<ServingCounter>();
        foreach (var o in renderList)
        {
            if (o is not ServingCounter c)
                continue;
            int s = CounterScore(c, food), d = Dist(c), i = 0;
            while (i < result.Count)
            {
                int s2 = CounterScore(result[i], food);
                if (s < s2 || (s == s2 && d <= Dist(result[i])))
                    break;
                i++;
            }
            result.Insert(i, c);
        }
        return result;
    }

    /// <summary>The doorway cell of the player's cafe.</summary>
    public int DoorCell => DoorSide == 1
        ? IndexOf(OriginU - 1, OriginV + data.K.DoorOffset + 1)
        : IndexOf(OriginU - 1 - data.K.DoorOffset, OriginV + 1);

    /// <summary>The wall the doorway is in: the side (1, the player's cafe) or the front (0, a raided cafe).</summary>
    public int DoorSide { get; private set; } = 1;

    /// <summary>FUN_00163b20: lay a floor tile (a tile.bin entry) with its tint on a cell.</summary>
    public void SetFloor(int cell, FurnitureDef def)
    {
        if (cell < 0 || cell >= Cells.Length)
            return;
        Cells[cell].Floor = def.ImageIndex;
        Cells[cell].Tint = def.Tint;
        Cells[cell].Flip = false;
    }

    /// <summary>
    /// FUN_000c0ef4, an expansion bought: the room grows to <paramref name="width"/> x <paramref name="depth"/>
    /// (cafe +0x94 / +0x95). The new floor is laid plain (K 0x42d62c), the back walls are carried along the new
    /// stretch (Cafe::addWall), and the walking graph follows the new edges. The original lays one new row and
    /// column, which is all a step of the store's expansions adds; here every new cell is laid, so a bigger
    /// expansion bought straight away leaves no holes.
    /// </summary>
    public void Expand(int width, int depth)
    {
        int oldW = RoomWidth, oldH = RoomDepth;
        width = Math.Max(width, oldW);
        depth = Math.Max(depth, oldH);
        if (width == oldW && depth == oldH)
            return;
        var k = data.K;
        int u0 = OriginU, v0 = OriginV;
        RoomWidth = width;
        RoomDepth = depth;
        var floor = data.Furniture[k.PlainFloorDef];
        for (int u = u0 - width; u < u0; u++)
            for (int v = v0 + 1; v <= v0 + depth; v++)
                if (u < u0 - oldW || v > v0 + oldH)
                    SetFloor(IndexOf(u, v), floor);
        int plainWall = data.Furniture[k.PlainWallDef].ImageIndex;
        for (int u = u0 - width; u < u0 - oldW; u++)
            AddWall(IndexOf(u, v0 + 1), 0, plainWall);
        for (int v = v0 + oldH + 1; v <= v0 + depth; v++)
            AddWall(IndexOf(u0 - 1, v), 1, plainWall);
        // the graph again for the new edges (walls, the doorway when open and furniture are all taken into account)
        BuildNavGraph();
    }

    /// <summary>FUN_00162c58: open (connect the doorway) or close the cafe.</summary>
    public void SetOpen(bool open)
    {
        if (IsOpen == open)
            return;
        IsOpen = open;
        int door = DoorCell, outside = Neighbour(door, DoorSide == 0 ? 2 : 3);
        if (open)
            Nav.Connect(door, outside, StepCost);
        else
            Nav.Disconnect(door, outside);
        Events?.Sound(0x41);
    }

    /// <summary>FUN_00160a1c: a wall on the upper-left (side 0) or upper-right (side 1) edge of a cell.</summary>
    public CafeWall AddWall(int index, int side, int imageIndex)
    {
        var k = data.K;
        var plain = data.Furniture[k.PlainWallDef];
        var wall = new CafeWall(data, imageIndex, plain.Tint, side);
        var corner = CellVertex(index, side * 2);
        int inset = side != 0 ? k.WallInset : ~k.WallInset - k.TileSpacingX;
        wall.SetPosition(this, corner.X + (inset - 1), corner.Y + (tileH >> 1) + 1);
        Add(wall);
        Cells[index].Slots[side] = wall;
        Nav?.Disconnect(index, Neighbour(index, side == 0 ? 2 : 3));
        return wall;
    }

    /// <summary>FUN_000b1ddc / FUN_0015fe8c: stand a new piece of furniture on cell (u, v).</summary>
    public Furniture AddFurniture(int defId, int u, int v, int orientation = 0)
    {
        var f = Furniture.Create(data, data.Furniture[defId]);
        if (orientation != 0)
            f.SetOrientation(orientation);
        PlaceFurniture(f, IndexOf(u, v));
        return f;
    }

    /// <summary>
    /// The cells a piece of furniture covers (FUN_0015fe8c): width cells one way and depth cells the other
    /// from its anchor; orientation 0 runs the width along +u and the depth along -v, otherwise swapped.
    /// </summary>
    public List<int> Footprint(FurnitureDef def, int anchor, int orientation)
    {
        var cells = new List<int>();
        int w = Math.Max(1, (int)def.Width), d = Math.Max(1, (int)def.Depth);
        int outerDir = orientation == 0 ? 3 : 2, innerDir = orientation == 0 ? 2 : 3;
        int row = anchor;
        for (int i = 0; i < w && row >= 0; i++)
        {
            int c = row;
            for (int j = 0; j < d && c >= 0; j++)
            {
                cells.Add(c);
                c = Neighbour(c, innerDir);
            }
            row = Neighbour(row, outerDir);
        }
        return cells;
    }

    /// <summary>Can this furniture stand here: every cell of it inside the room, free, and not the doorway.</summary>
    public bool CanPlace(FurnitureDef def, int anchor, int orientation)
    {
        if (anchor < 0)
            return false;
        var cells = Footprint(def, anchor, orientation);
        if (cells.Count < Math.Max(1, (int)def.Width) * Math.Max(1, (int)def.Depth))
            return false;
        int door = DoorCell;
        foreach (int c in cells)
        {
            if (c < 0 || !IsInside(c) || c == door || !Nav.IsPassable(c) || Cells[c].Slots[2] != null)
                return false;
            var (u, v) = Diamond(c);
            if (!InRoom(u, v))
                return false;
        }
        return true;
    }

    /// <summary>
    /// FUN_000c42ec: tombstones, toxin generators and habitats stand in the yard, on the first row along the
    /// room's two open sides (v = v0 + depth + 1 and u = u0 - width - 1), where nothing stands yet.
    /// </summary>
    public bool CanPlaceInYard(int cell)
    {
        if (cell < 0 || cell >= Cells.Length || Cells[cell].Slots[2] != null)
            return false;
        var (u, v) = Diamond(cell);
        bool front = v == OriginV + RoomDepth + 1 && u >= OriginU - RoomWidth && u < OriginU;
        bool side = u == OriginU - RoomWidth - 1 && v > OriginV && v <= OriginV + RoomDepth;
        return front || side;
    }

    /// <summary>+0xc2: toxic barrels in the cafe (they speed up resting zombies).</summary>
    public int ToxinBarrels { get; private set; }

    /// <summary>FUN_0015fe8c: stand furniture on a cell (its whole footprint becomes impassable).</summary>
    public void PlaceFurniture(Furniture f, int anchor)
    {
        f.PlaceOn(this, anchor);
        var cells = Footprint(f.Def, anchor, f.Orientation);
        if (cells.Count > 1)
        {
            // Furniture::setPosition: big pieces sort by their topmost cell (+50 for a 2x2)
            float top = cells.Min(c => CellCenter(c).Y);
            f.SetSortY(this, top + (f.Def.Width == 2 && f.Depth2() ? 50 : 0));
        }
        Add(f);
        Cells[anchor].Slots[2] = f;
        foreach (int c in cells)
            Nav.SetPassable(c, false);
        if (f.Def.Category == FurnitureCategory.ToxinBarrel)
            ToxinBarrels++;
    }

    /// <summary>FUN_00163e40: take furniture out of the cafe (its cells become walkable again).</summary>
    public void RemoveFurniture(Furniture f)
    {
        if (f.Cell >= 0)
        {
            foreach (int c in Footprint(f.Def, f.Cell, f.Orientation))
                if (c >= 0)
                    Nav.SetPassable(c, true);
            if (Cells[f.Cell].Slots[2] == f)
                Cells[f.Cell].Slots[2] = null;
        }
        if (f.Def.Category == FurnitureCategory.ToxinBarrel)
            ToxinBarrels--;
        DirtyTables.Remove(f);
        Remove(f);
    }

    /// <summary>The wall on side 0/1 of a cell, if any.</summary>
    public CafeWall? WallAt(int cell, int side) => cell >= 0 && cell < Cells.Length ? Cells[cell].Slots[side] as CafeWall : null;

    /// <summary>FUN_000b2158: hang a decoration on the wall at cell (u, v), side 0 or 1.</summary>
    public Furniture? AddWallDecor(int defId, int u, int v, int side)
    {
        int cell = IndexOf(u, v);
        if (cell < 0 || Cells[cell].Slots[side] is not CafeWall wall)
            return null;
        var f = new Furniture(data, data.Furniture[defId]);
        wall.Hang(this, f);
        return f;
    }

    /// <summary>GameStateCafe::load, new game: the junk the player starts with (positions relative to the origin).</summary>
    public void AddStartingFurniture()
    {
        var k = data.K;
        int u0 = OriginU, v0 = OriginV;
        AddFurniture(k.Int(0x42d504), u0 - 7, v0 + 1); // dirty stove (a Stove)
        AddFurniture(k.Int(0x42d50c), u0 - 5, v0 + 1); // dirty sink
        AddFurniture(k.Int(0x42d508), u0 - 4, v0 + 1); // dirty fridge
        AddFurniture(k.Int(0x42d510), u0 - 7, v0 + 6); // dead plant
        AddFurniture(k.Int(0x42d4f0), u0 - 5, v0 + 3); // serving counters
        AddFurniture(k.Int(0x42d4f0), u0 - 5, v0 + 4);
        AddFurniture(k.Int(0x42d4f8), u0 - 3, v0 + 3); // table
        AddFurniture(k.Int(0x42d4fc), u0 - 3, v0 + 2); // chair
        AddWallDecor(k.Int(0x42d51c), u0 - 4, v0 + 1, 0); // boarded windows
        AddWallDecor(k.Int(0x42d51c), u0 - 2, v0 + 1, 0);
    }

    /// <summary>FUN_00160c0c: opens (or closes) a doorway in a wall and marks the neighbouring wall's frame.</summary>
    public void SetDoor(int index, int side, bool open)
    {
        if (Cells[index].Slots[side] is CafeWall w)
            w.IsDoor = open;
        int outside = Neighbour(index, side == 0 ? 2 : 3);
        if (outside >= 0)
        {
            if (open && IsOpen)
                Nav.Connect(index, outside, StepCost);
            else
                Nav.Disconnect(index, outside);
        }
        int next = Neighbour(index, side == 0 ? 3 : 2);
        if (next >= 0 && Cells[next].Slots[side] is CafeWall n)
            n.IsDoorNeighbour = open;
    }

    // ---------------------------------------------------------------- creation

    /// <summary>The player's starting cafe (FUN_001601c8): a dirty 7 x 6 room off a street corner.</summary>
    public static Cafe CreateDefault(GameData data, Random rng)
    {
        var k = data.K;
        var c = new Cafe(data) { OriginU = k.PlayerOriginU, OriginV = k.PlayerOriginV, IsOpen = false };
        int w = k.StartRoomWidth, h = k.StartRoomDepth;
        int plainFloor = data.Furniture[k.PlainFloorDef].ImageIndex;
        c.LayGround(rng, w, h, (u, v) => c.StartingFloor(u, v, c.OriginU, c.OriginV, w, k.DirtyFloorTile, plainFloor));
        int u0 = c.OriginU, v0 = c.OriginV;
        c.BuildNavGraph(); // before any wall exists, like the original

        // walls: the v = v0 + 1 edge (side 0), then the u = u0 - 1 edge (side 1); a few start out dirty
        int DirtyWall = k.DirtyWallImage;
        int plainWall = data.Furniture[k.PlainWallDef].ImageIndex;
        for (int i = 0; i < w; i++)
        {
            int img = i switch { 6 => DirtyWall + 6, 5 => DirtyWall + 5, 1 => DirtyWall, _ => plainWall };
            c.AddWall(c.IndexOf(u0 - w + i, v0 + 1), 0, img);
        }
        for (int j = 0; j > -h; j--)
        {
            int img = j switch { 0 => DirtyWall + 2, -5 => DirtyWall + 3, -4 => DirtyWall + 4, -1 => DirtyWall + 1, _ => plainWall };
            c.AddWall(c.IndexOf(u0 - 1, v0 + h + j), 1, img);
        }
        c.SetDoor(c.IndexOf(u0 - 1, v0 + k.DoorOffset + 1), 1, true);
        return c;
    }

    /// <summary>
    /// Cafe::createSized (FUN_00160c90): a raided restaurant - a w x d room of plain floor and walls off the corner of
    /// the raid's street (origin K 0x42ce48 / 0x42ce4c), open for business, the doorway in its front wall.
    /// </summary>
    public static Cafe CreateSized(GameData data, Random rng, int width, int depth)
    {
        var k = data.K;
        var c = new Cafe(data) { OriginU = k.RaidOriginU, OriginV = k.RaidOriginV, IsOpen = true, DoorSide = 0 };
        c.LayGround(rng, width, depth, (_, _) => k.TileCafeFloor);
        int u0 = c.OriginU, v0 = c.OriginV;
        c.BuildNavGraph();
        int plainWall = data.Furniture[k.PlainWallDef].ImageIndex;
        for (int i = 0; i < width; i++)
            c.AddWall(c.IndexOf(u0 - width + i, v0 + 1), 0, plainWall);
        for (int j = 0; j > -depth; j--)
            c.AddWall(c.IndexOf(u0 - 1, v0 + depth + j), 1, plainWall);
        c.SetDoor(c.DoorCell, 0, true);
        return c;
    }

    /// <summary>The street corner, the sidewalks, the grass and the room's floor (createDefault / createSized).</summary>
    void LayGround(Random rng, int w, int h, Func<int, int, int> roomFloor)
    {
        var k = data.K;
        var c = this;
        int road = k.RoadWidth;
        int u0 = c.OriginU, v0 = c.OriginV;
        int Stripe = k.TileRoadStripe, Road = k.TileRoad, Corner = k.TileSidewalkCorner, Curb = k.TileCurb,
            Sidewalk = k.TileSidewalk;
        int[] grass = k.GrassTiles.Take(Math.Max(1, k.GrassTileCount)).ToArray();

        for (int i = 0; i < c.Cells.Length; i++)
        {
            var cell = c.Cells[i];
            var (u, v) = c.Diamond(i);
            bool inRoadU = u0 < u && u < u0 + road + 1;
            bool inRoadV = v < v0 && v0 - 1 - road < v;
            if (inRoadU || inRoadV)
            {
                if (v == v0 - 1 - (road >> 1) && (u <= u0 || u0 + road + 1 <= u))
                    cell.Floor = Stripe;
                else if (u != u0 + (road >> 1) + 1 || (v < v0 && v0 - road - 1 < v))
                    cell.Floor = Road;
                else
                {
                    cell.Floor = Stripe;
                    cell.Flip = true;
                }
                continue;
            }
            int curbU = u0 + road + 1, curbV = v0 - road - 1;
            if ((u == curbU && v == v0) || (v == curbV && u == u0))
            {
                cell.Floor = Corner;
                cell.Flip = v != curbV;
                continue;
            }
            if (u == curbU || v == curbV)
            {
                cell.Floor = Curb;
                cell.Flip = v != curbV;
                continue;
            }
            if (u == u0 || v == v0)
            {
                cell.Floor = Sidewalk;
                cell.Flip = v == v0 || v == curbV;
                continue;
            }
            if (u < u0 && !(v0 + h < v || u < u0 - w || v <= v0))
            {
                cell.Floor = roomFloor(u, v);
                continue;
            }
            cell.Floor = grass[rng.Next(grass.Length)];
            cell.Flip = rng.Next(2) == 1;
        }
        c.RoomWidth = w;
        c.RoomDepth = h;
        // FUN_0015e788 / createSized: the sidewalk's two far ends
        int W = c.MapWidth;
        c.StreetEndA = W * (v0 * 2);
        c.StreetEndB = (W * 2 - u0 * 2 - 1) * W + ((W * 2 - 2) >> 1);
    }

    /// <summary>The starting room's floor: plain tiles with a hand-placed pattern of stains (tiles 11..28).</summary>
    int StartingFloor(int u, int v, int u0, int v0, int w, int dirt, int plain)
    {
        int left = u0 - w;
        bool farLeft = u >= left && u < left + 3; // u0-7 .. u0-5
        if (farLeft && v == v0 + 1) return dirt + 13;
        if (farLeft && v == v0 + 2) return dirt + 12;
        if (u == u0 - 2 && v == v0 + 1) return dirt + 7;
        if (u == u0 - 1 && v == v0 + 1) return dirt + 6;
        if (u == left)
        {
            if (v == v0 + 3) return dirt + 5;
            if (v == v0 + 4) return dirt + 4;
            if (v == v0 + 5) return dirt + 3;
            if (v == v0 + 6) return dirt + 2;
        }
        if (u == u0 + 1 - w)
        {
            if (v == v0 + 5) return dirt;
            if (v == v0 + 6) return dirt + 1;
            if (v == v0 + 3) return dirt + 11;
            if (v == v0 + 4) return dirt + 8;
        }
        if (u == u0 + 2 - w)
        {
            if (v == v0 + 3) return dirt + 10;
            if (v == v0 + 4) return dirt + 9;
            if (v == v0 + 5) return dirt + 14;
        }
        if (u == u0 - 1 && v == v0 + 6) return dirt + 15;
        if (u == u0 - 2 && v == v0 + 6) return dirt + 16;
        if (u == u0 - 1 && v == v0 + 5) return dirt + 17;
        return plain;
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>Cafe::draw (FUN_00162128): the floor mesh, then every object in depth order.</summary>
    public void Draw()
    {
        for (int i = 0; i < Cells.Length; i++)
        {
            var cell = Cells[i];
            if (cell.Floor < 0 || cell.Floor >= data.Tiles.Length)
                continue;
            var img = data.Tiles[cell.Floor];
            var tl = CellTopLeft(i);
            var src = new Rectangle(img.Src.X, img.Src.Y, tileW, tileH);
            if (cell.Flip)
                src = new Rectangle(src.X + src.Width, src.Y, -src.Width, src.Height);
            Gfx.Draw.Image(img.Texture, src, new Rectangle(tl.X, tl.Y, tileW, tileH), Colors.FromArgb(cell.Tint));
        }
        foreach (var o in renderList)
            o.Draw(this, data);
        foreach (var s in overlays)
            if (!s.Gone)
                s.Draw(data);
        foreach (var n in notes)
            n.Draw(data);
    }

    /// <summary>World-space bounds of the room (for framing the camera).</summary>
    public Rectangle RoomBounds()
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int u = OriginU - RoomWidth; u < OriginU; u++)
            for (int v = OriginV + 1; v <= OriginV + RoomDepth; v++)
            {
                int i = IndexOf(u, v);
                if (i < 0) continue;
                var a = CellVertex(i, 0);
                var b = CellVertex(i, 3);
                minX = Math.Min(minX, a.X); minY = Math.Min(minY, a.Y);
                maxX = Math.Max(maxX, b.X); maxY = Math.Max(maxY, b.Y);
            }
        return new Rectangle(minX, minY - 250, maxX - minX, maxY - minY + 250);
    }
}
