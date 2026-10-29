using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// The store and decorating mode of the cafe (GameStateCafe's edit states): a tabbed store along the bottom
/// with the original tab art, and tools to place new furniture (a ghost that is green where it fits), hang
/// wall decorations, paint floors and walls, move, salvage or put away what is already there. The storage box
/// (Storage, FUN_001b1f60; HUD button 7) is the store's panel showing what was put away, to place again free.
/// </summary>
public sealed partial class EditMode
{
    readonly GameData data;
    readonly CafeController game;
    Cafe cafe => game.Cafe;

    public bool StoreOpen { get; private set; }
    /// <summary>The panel shows the storage box rather than the store.</summary>
    public bool StorageOpen => StoreOpen && storage;
    bool storage;
    bool fromStorage;   // the piece in hand came out of the storage box
    int tab = 1;
    float scroll;

    enum Tool { None, Place, Move, WallDecor, Floor, Wall, Group }
    Tool tool;
    FurnitureDef? def;          // the item being bought / placed
    Furniture? ghost;           // the piece following the mouse
    int fromCell = -1, fromOrientation; // where a moved piece came from
    Furniture? heldDecor;       // a wall decoration taken down to move, sell or store
    CafeWall? decorFrom;        // the wall it came off
    int hoverCell = -1;
    CafeWall? hoverWall;
    bool hoverOk;
    string? message;
    float messageTime;

    public EditMode(GameData data, CafeController game)
    {
        this.data = data;
        this.game = game;
    }

    /// <summary>Edit mode takes the clicks (store open, a tool in hand, or rearranging).</summary>
    public bool Active => StoreOpen || tool != Tool.None || Editing;

    /// <summary>In decorating mode with nothing in hand (furniture can be picked up).</summary>
    public bool Editing { get; private set; }

    /// <summary>Painting tools use left-drag, so the camera should not pan with it.</summary>
    public bool UsesLeftDrag => tool is Tool.Floor or Tool.Wall || boxing;

    public void OpenStore(int shopTab = 0)
    {
        if (shopTab is >= 1 and <= 5)
            tab = shopTab;
        storage = false;
        StoreOpen = true;
        Editing = true;
        game.Selected = null;
        // FUN_000bd7b0 and the store's list (FUN_0012987c): the tutorial's "a table and a chair" steps
        game.Hint(0x2a, true);
        game.Hint(0x2d, true);
    }

    /// <summary>FUN_000bd7b0 button 7 / FUN_001b2f90: the storage box, on its first tab.</summary>
    public void OpenStorage()
    {
        CancelTool();
        tab = UiSprites.TabOrder[0];
        scroll = 0;
        storage = true;
        StoreOpen = true;
        Editing = true;
        game.Selected = null;
    }

    public void Close()
    {
        CancelTool();
        StoreOpen = false;
        Editing = false;
        EndSession();
    }

    void Say(string text)
    {
        message = text;
        messageTime = 3f;
    }

    // ---------------------------------------------------------------- tools

    static bool Rotatable(FurnitureDef d) => d.B1C != 0;

    static int Orientations(FurnitureDef d) => !Rotatable(d) ? 1 : d.Category == FurnitureCategory.Chair ? 4 : 2;

    void StartPlacing(FurnitureDef d, bool stored = false)
    {
        CancelTool();
        def = d;
        fromStorage = stored;
        switch (d.Category)
        {
            case FurnitureCategory.WallDecor:
                tool = Tool.WallDecor;
                break;
            case FurnitureCategory.Floor:
                tool = Tool.Floor;
                Say(data.Strings[312]);
                break;
            case FurnitureCategory.Wall:
                tool = Tool.Wall;
                Say(data.Strings[313]);
                break;
            default:
                tool = Tool.Place;
                ghost = Furniture.Create(data, d);
                Say(data.Strings[407]);
                break;
        }
        StoreOpen = false;
    }

    /// <summary>A right click: the piece in hand goes back (a moved one to where it was), as Esc does.</summary>
    public void CancelHeld()
    {
        if (tool != Tool.None)
            CancelTool();
        else
            ClearSelection();
    }

    /// <summary>The mouse is over a piece that a click would pick up (decorating, nothing in hand).</summary>
    public bool OverPiece(Vector2 world)
    {
        if (!Editing || tool != Tool.None)
            return false;
        foreach (var o in cafe.Objects)
            if (o is Furniture f && f.Cell >= 0 && f.Visible && Hit(f, world))
                return true;
        return DecorUnder(world) != null;
    }

    /// <summary>A wall with a decoration hung on it under the mouse (the wall's panel stands for its decoration).</summary>
    CafeWall? DecorUnder(Vector2 world) => WallUnder(world, w => w.Decor != null);

    /// <summary>A decoration taken off its wall to hang elsewhere, sell or put in the storage box.</summary>
    void PickUpDecor(CafeWall wall)
    {
        CancelTool();
        heldDecor = wall.Decor;
        decorFrom = wall;
        wall.Hang(cafe, null);
        def = heldDecor!.Def;
        tool = Tool.WallDecor;
        StoreOpen = false;
        game.Sound(36);
    }

    /// <summary>The decoration in hand is done with (hung, sold or stored).</summary>
    void DropDecor()
    {
        heldDecor = null;
        decorFrom = null;
        tool = Tool.None;
        def = null;
    }

    void PickUp(Furniture f)
    {
        CancelTool();
        fromCell = f.Cell;
        fromOrientation = f.Orientation;
        cafe.RemoveFurniture(f);
        ghost = f;
        def = f.Def;
        tool = Tool.Move;
        StoreOpen = false;
    }

    void CancelTool()
    {
        CancelGroup();
        if (heldDecor != null && decorFrom != null && decorFrom.Decor == null)
            decorFrom.Hang(cafe, heldDecor);
        heldDecor = null;
        decorFrom = null;
        if (tool == Tool.Move && ghost != null && fromCell >= 0)
        {
            ghost.TintOverride = null;
            if (ghost.Orientation != fromOrientation)
                ghost.SetOrientation(fromOrientation);
            cafe.PlaceFurniture(ghost, fromCell);
        }
        tool = Tool.None;
        ghost = null;
        def = null;
        fromCell = -1;
        fromStorage = false;
    }

    /// <summary>
    /// FUN_000cb270, a piece dropped on the storage box: it goes in - unless it is one the box does not take (shop
    /// tab 255, string 647) or the last of something the cafe needs (string 330). The port keeps a piece holding
    /// food (a stove cooking, a counter with dishes) out of the box so the food is not lost.
    /// </summary>
    bool StoreHeld()
    {
        if (tool != Tool.Move || ghost == null || def == null)
            return false;
        if (def.ShopTab == 255)
        {
            Say(data.Strings[647]);
            return false;
        }
        if (game.IsLastOfItsKind(ghost))
        {
            Say(data.Strings[330]);
            return false;
        }
        cafe.Stored.Add(def.Id);
        // a habitat's pet goes into storage with it
        if (ghost is Habitat home)
            game.PetStored(home);
        RecordStored([(ghost, fromCell, fromOrientation)]);
        game.Sound(64);
        ghost.TintOverride = null;
        tool = Tool.None;
        ghost = null;
        def = null;
        fromCell = -1;
        return true;
    }

    /// <summary>The piece in hand has food on it (it would be lost in the box).</summary>
    bool HoldsFood => ghost is Stove { Busy: true } || ghost is ServingCounter { Dishes.Count: > 0 } || ghost?.Attached != null;

    void Rotate()
    {
        if (ghost == null || def == null)
            return;
        int n = Orientations(def);
        if (n > 1)
        {
            ghost.SetOrientation((ghost.Orientation + 1) % n);
            game.Sound(186);
        }
    }

    /// <summary>Per-frame input for the tools; <paramref name="world"/> is the mouse in world space.</summary>
    public void Update(Vector2 world, bool click, float dt)
    {
        if (messageTime > 0)
            messageTime -= dt;
        if (Input.IsKeyPressed(KeyboardKey.Escape))
        {
            if (tool != Tool.None)
                CancelTool();
            else if (selected.Count > 0)
                ClearSelection();
            else if (StoreOpen)
                StoreOpen = false;
            else
            {
                Editing = false;
                EndSession();
            }
            return;
        }
        if (!Input.IsMouseButtonDown(MouseButton.Left))
            EndStroke();
        if (Input.IsKeyPressed(KeyboardKey.R))
            Rotate();
        if (Shortcuts(world))
            return;
        var (u, v) = cafe.ScreenToDiamond(world.X, world.Y);
        hoverCell = cafe.IndexOf(u, v);
        hoverWall = null;
        switch (tool)
        {
            case Tool.Group:
                UpdateGroup(click);
                break;
            case Tool.Place:
            case Tool.Move:
                if (ghost == null || def == null)
                    break;
                // FUN_000c42ec (mask 0x4c000): tombstones, toxin generators and habitats go out in the yard
                hoverOk = hoverCell >= 0 && (def.Category is FurnitureCategory.Tombstone or FurnitureCategory.ToxinGenerator or FurnitureCategory.Habitat
                    ? cafe.CanPlaceInYard(hoverCell)
                    : cafe.CanPlace(def, hoverCell, ghost.Orientation));
                if (hoverCell >= 0)
                    ghost.PlaceOn(cafe, hoverCell);
                if (click && hoverOk)
                    Drop();
                break;
            case Tool.WallDecor:
                hoverWall = WallUnder(world, w => w.Decor == null && !w.IsDoor);
                hoverOk = hoverWall != null;
                if (click && hoverWall != null && heldDecor != null)
                {
                    // a decoration taken down: hung again where clicked, for nothing
                    hoverWall.Hang(cafe, heldDecor);
                    RecordDecorMoved(heldDecor, decorFrom, hoverWall);
                    game.Sound(90);
                    DropDecor();
                }
                else if (click && hoverWall != null && def != null && fromStorage)
                {
                    // out of the storage box: nothing to pay
                    cafe.Stored.Remove(def.Id);
                    var hung = new Furniture(data, def);
                    hoverWall.Hang(cafe, hung);
                    RecordDecorFromStorage(hung, hoverWall);
                    game.Sound(90);
                    tool = Tool.None;
                    def = null;
                    fromStorage = false;
                }
                else if (click && hoverWall != null && def != null && game.Buy(def))
                {
                    var f = new Furniture(data, def);
                    hoverWall.Hang(cafe, f);
                    RecordDecorBought(f, hoverWall);
                    game.Sound(90);
                    if (game.CannotBuy(def) != null || !Input.IsKeyDown(KeyboardKey.LeftShift))
                        tool = Tool.None;
                }
                break;
            case Tool.Wall:
                hoverWall = WallUnder(world, w => w.Def.Id != def?.Id);
                hoverOk = hoverWall != null;
                if (Input.IsMouseButtonDown(MouseButton.Left) && hoverWall != null && def != null && game.Buy(def))
                {
                    PaintedWall(hoverWall, def);
                    hoverWall.SetDef(data, def);
                    game.Sound(90);
                }
                break;
            case Tool.Floor:
                hoverOk = hoverCell >= 0 && InRoom(hoverCell) && def != null
                    && (cafe.Cells[hoverCell].Floor != def.ImageIndex || cafe.Cells[hoverCell].Tint != def.Tint);
                if (Input.IsMouseButtonDown(MouseButton.Left) && hoverOk && def != null && game.Buy(def))
                {
                    PaintedFloor(hoverCell, def);
                    cafe.SetFloor(hoverCell, def);
                    game.Sound(90);
                }
                break;
            case Tool.None:
                if (UpdateSelection(world, click))
                    break;
                if (click)
                {
                    // pick up a piece of furniture to move or salvage it - also with the store showing, as the
                    // original's strip leaves the cafe to be touched (the panel takes its own clicks; picking closes it)
                    var objs = cafe.Objects;
                    bool picked = false;
                    for (int i = objs.Count - 1; i >= 0; i--)
                        if (objs[i] is Furniture f && f.Cell >= 0 && f.Visible && Hit(f, world))
                        {
                            // one of several picked out with Ctrl: they all come up together
                            if (selected.Count > 1 && selected.Contains(f))
                                PickUpGroup(f);
                            else
                            {
                                ClearSelection();
                                PickUp(f);
                            }
                            picked = true;
                            break;
                        }
                    // else a decoration on the wall under the mouse
                    if (!picked && DecorUnder(world) is { } decorWall)
                    {
                        ClearSelection();
                        PickUpDecor(decorWall);
                    }
                    else if (!picked)
                        ClearSelection();
                }
                break;
        }
    }

    bool InRoom(int cell)
    {
        var (u, v) = cafe.Diamond(cell);
        return cafe.InRoom(u, v);
    }

    static bool Hit(Furniture f, Vector2 p) => f.HitTest(p.X, p.Y);

    CafeWall? WallUnder(Vector2 p, Func<CafeWall, bool> ok)
    {
        foreach (var o in cafe.Objects)
            if (o is CafeWall w && w.Contains(p.X, p.Y) && ok(w))
                return w;
        return null;
    }

    /// <summary>The code's own words (FUN_000cb270): "You have the maximum number of pets for your level. ..."</summary>
    const uint TextPetLimit = 0x3412b7;

    void Drop()
    {
        if (ghost == null || def == null)
            return;
        // FUN_000cb270: a new habitat (bought, or out of the storage box) only while there is room for another pet
        if (tool == Tool.Place && def.Category == FurnitureCategory.Habitat && game.Pets.Count >= game.Player.PetLimit(data))
        {
            Say(data.Exe.Get(TextPetLimit));
            return;
        }
        if (tool == Tool.Place && fromStorage)
        {
            // out of the storage box: nothing to pay
            ghost.TintOverride = null;
            cafe.Stored.Remove(def.Id);
            cafe.PlaceFurniture(ghost, hoverCell);
            if (ghost is Habitat stored)
                game.PetFromStorage(stored);
            RecordFromStorage(ghost);
            game.Sound(90);
            tool = Tool.None;
            ghost = null;
            def = null;
            fromStorage = false;
            return;
        }
        if (tool == Tool.Place)
        {
            if (!game.Buy(def))
            {
                Say(game.CannotBuy(def) ?? "");
                return;
            }
            ghost.TintOverride = null;
            cafe.PlaceFurniture(ghost, hoverCell);
            game.Sound(90);
            Bought(ghost);
            RecordBought([ghost]);
            // keep placing copies while Shift is held and the money lasts (a pet at a time)
            if (Input.IsKeyDown(KeyboardKey.LeftShift) && game.CannotBuy(def) == null && def.Category != FurnitureCategory.Habitat)
            {
                int o = ghost.Orientation;
                ghost = Furniture.Create(data, def);
                if (o != 0)
                    ghost.SetOrientation(o);
            }
            else
            {
                tool = Tool.None;
                ghost = null;
                def = null;
            }
            return;
        }
        // moving
        ghost.TintOverride = null;
        cafe.PlaceFurniture(ghost, hoverCell);
        RecordMoved([(ghost, fromCell, fromOrientation)]);
        game.Sound(90);
        tool = Tool.None;
        ghost = null;
        def = null;
        fromCell = -1;
    }

    /// <summary>What comes with a piece bought and placed (FUN_000c0ef4).</summary>
    void Bought(Furniture f)
    {
        var d = f.Def;
        // what is bought is dated (a magic fridge's first free dish comes the day after)
        f.GiftDay = GameClock.Today;
        // a special stove bought brings its recipes
        game.StoveRecipes(d);
        // case 18: a habitat bought brings its pet (FUN_000c2c50)
        if (f is Habitat bought)
            game.PetPurchased(bought);
        // FUN_000f5484: a tombstone bought comes with its full uses
        if (d.Category == FurnitureCategory.Tombstone)
            game.Player.Boosts.Refill(d.Id);
        // the tutorial waits for a table (0x13) and a chair (0x14)
        if (d.Category == FurnitureCategory.Table)
            game.Hint(0x13, true);
        else if (d.Category == FurnitureCategory.Chair)
            game.Hint(0x14, true);
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>The ghost and wall highlight, in world space (after the cafe).</summary>
    public void DrawWorld()
    {
        if (ghost != null && hoverCell >= 0 && tool is Tool.Place or Tool.Move)
        {
            ghost.TintOverride = hoverOk ? new Color(140, 255, 140, 220) : new Color(255, 110, 110, 200);
            ghost.Draw(cafe, data);
        }
        DrawGroupWorld();
        if (hoverWall != null)
        {
            float left = hoverWall.Side == 0 ? hoverWall.X : hoverWall.X - hoverWall.Width;
            Gfx.Draw.Rect(new Rectangle(left, hoverWall.Y - hoverWall.Height, hoverWall.Width, hoverWall.Height), new Color(120, 255, 120, 70));
        }
        if (tool == Tool.Floor && hoverCell >= 0)
        {
            var c = cafe.CellCenter(hoverCell);
            Gfx.Draw.Rect(new Rectangle(c.X - 40, c.Y - 12, 80, 24), hoverOk ? new Color(120, 255, 120, 90) : new Color(255, 120, 120, 90));
        }
    }

    /// <summary>Store panel and tool bar, in virtual-screen space.</summary>
    public void DrawUi()
    {
        var ui = data.IngameUi;
        Buttons.Clear();
        StoreButtonRect = null;
        FirstStoredRect = null;
        if (StoreOpen && storage)
            DrawStorage(ui);
        else if (StoreOpen)
            DrawStore(ui);
        else if (tool == Tool.Group)
            DrawGroupBar(ui);
        else if (tool != Tool.None)
            DrawToolBar(ui);
        else if (selected.Count > 0)
            DrawSelectionBar(ui);
        else
            DrawEditBar(ui);
        if (messageTime > 0 && message != null)
            Widgets.Label(message, Screen.Width / 2, Screen.Height - 190, 22, Anchor.Center, Widgets.Gold);
    }

    void DrawEditBar(SubImage[] ui)
    {
        // nothing in hand: click furniture to move it; back to the store or out of edit mode
        // the store and OK buttons stay where the original bar had them; the extras widen it to the left
        var r = new Rectangle(Screen.Width / 2 + 260 - 760, Screen.Height - 92, 760, 64);
        Widgets.Panel(r);
        Widgets.Label("Click furniture to move or salvage it.", r.X + 16, r.Y + 10, 20);
        Widgets.Label("Ctrl+click or Ctrl+drag picks out several.", r.X + 16, r.Y + 36, 16, Anchor.Top | Anchor.Left, new Color(210, 210, 210, 255));
        DrawUndoPaste(r.X + r.Width - 360, r.Y + 12);
        if (Widgets.ImageButton(ui[UiSprites.ShopBag], r.X + r.Width - 100, r.Y + 32, 0.8f))
            StoreOpen = true;
        if (Widgets.ImageButton(ui[UiSprites.Ok], r.X + r.Width - 40, r.Y + 32, 0.55f))
            Close();
    }

    void DrawToolBar(SubImage[] ui)
    {
        var r = new Rectangle(Screen.Width / 2 - 300, Screen.Height - 110, 600, 84);
        Widgets.Panel(r);
        if (def != null)
        {
            Widgets.Label(def.Name, r.X + 16, r.Y + 10, 24, Anchor.Top | Anchor.Left, Widgets.Gold);
            string price = tool == Tool.Move || fromStorage || heldDecor != null ? "" : def.Currency == 1 ? $"{def.Price} toxin" : $"${def.Price}";
            string hint = tool switch
            {
                Tool.Place => "Click a free spot to place it" + (Orientations(def) > 1 ? ", R to rotate" : "") + ". Shift keeps placing.",
                Tool.Move => "Click a free spot to put it down" + (Orientations(def) > 1 ? ", R to rotate" : "") + ".",
                Tool.WallDecor => "Click a bare wall to hang it.",
                Tool.Floor => data.Strings[312],
                Tool.Wall => data.Strings[313],
                _ => "",
            };
            Widgets.Label($"{price}  {hint}", r.X + 16, r.Y + 44, 18);
        }
        float bx = r.X + r.Width - 46;
        if (Widgets.ImageButton(ui[UiSprites.BigX], bx, r.Y + 42, 0.6f))
            CancelTool();
        if (tool is Tool.Place or Tool.Move && def != null && Orientations(def) > 1)
        {
            bx -= 70;
            if (Widgets.ImageButton(ui[UiSprites.Rotate], bx, r.Y + 42, 0.75f))
                Rotate();
        }
        if (heldDecor != null)
        {
            // a decoration taken down: into the storage box, or sold (half its worth, as any piece)
            bx -= 95;
            StoreButtonRect = new Rectangle(bx - 35, r.Y + 10, 70, 64);
            if (Widgets.ImageButton(ui[UiSprites.Storage], bx, r.Y + 42, 0.8f))
            {
                if (heldDecor.Def.ShopTab == 255)
                    Say(data.Strings[647]);
                else
                {
                    cafe.Stored.Add(heldDecor.Def.Id);
                    RecordDecorStored(heldDecor, decorFrom);
                    game.Sound(64);
                    DropDecor();
                }
                return;
            }
            bx -= 95;
            if (Widgets.ImageButton(ui[UiSprites.Salvage], bx, r.Y + 42, 0.7f))
            {
                int price = game.SellPrice(heldDecor.Def);
                game.Player.Money += price;
                RecordDecorSold(heldDecor, decorFrom);
                game.Sound(8);
                Say($"{data.Strings[329]}: ${price}");
                DropDecor();
            }
            return;
        }
        if (tool == Tool.Move && ghost != null)
        {
            // into the storage box (ui 186)
            bx -= 95;
            StoreButtonRect = new Rectangle(bx - 35, r.Y + 10, 70, 64);
            if (Widgets.ImageButton(ui[UiSprites.Storage], bx, r.Y + 42, 0.8f, !HoldsFood))
            {
                StoreHeld();
                return;
            }
            bx -= 95;
            bool last = game.IsLastOfItsKind(ghost);
            if (Widgets.ImageButton(ui[UiSprites.Salvage], bx, r.Y + 42, 0.7f, !last))
            {
                // FUN_000c3118: a habitat sold takes its pet with it
                if (ghost is Habitat sold)
                    game.PetSold(sold);
                else
                    RecordSold([(ghost, fromCell, fromOrientation)]);
                int price = game.SellPrice(ghost.Def);
                game.Player.Money += price;
                game.Sound(8);
                Say($"{data.Strings[329]}: ${price}");
                tool = Tool.None;
                ghost = null;
                def = null;
                fromCell = -1;
            }
            if (last && Widgets.Hover(new Rectangle(bx - 40, r.Y + 10, 80, 64)))
                Widgets.Label(data.Strings[330], Screen.Width / 2, r.Y - 24, 18, Anchor.Center, Widgets.Gold);
        }
    }

    List<FurnitureDef> Items(int shopTab) => data.Furniture
        .Where(d => d.ShopTab == shopTab && d.ForSale && d.Theme != 255 && d.Currency <= 1)
        // FUN_00129b3c: a special stove's upgrades (furniture +0x44) are bought from the stove, not the store
        .Where(d => d.S44 < 1)
        // FUN_00129b3c: expansions only while they would make the room bigger
        .Where(d => d.Category != FurnitureCategory.Expansion
            || (d.Width > cafe.RoomWidth && d.Depth >= cafe.RoomDepth) || (d.Width >= cafe.RoomWidth && d.Depth > cafe.RoomDepth))
        .OrderBy(d => d.Theme).ThenBy(d => d.Currency).ThenBy(d => d.Price)
        .ToList();

    /// <summary>Test scripts: the centre of store tab <paramref name="t"/> (virtual points), while the store is open.</summary>
    public System.Numerics.Vector2? TabCenter(int t)
    {
        if (!StoreOpen)
            return null;
        var ui = data.IngameUi;
        float h = 330;
        var r = new Rectangle(12, Screen.Height - h - 12, Screen.Width - 24, h);
        float tx = r.X + 20;
        foreach (int i in UiSprites.TabOrder)
        {
            var (on, off) = UiSprites.Tab(i);
            float w = ui[i == tab ? on : off].FullWidth * 1.2f;
            if (i == t)
                return new System.Numerics.Vector2(tx + w / 2, r.Y + 24);
            tx += w + 12;
        }
        return null;
    }

    /// <summary>
    /// Test scripts: the centre of the store's card for a piece of furniture (virtual points) - its tab is opened and
    /// the row scrolled to show it.
    /// </summary>
    public System.Numerics.Vector2? ItemCard(int defId)
    {
        if (!StoreOpen || storage || defId < 0 || defId >= data.Furniture.Count)
            return null;
        var d = data.Furniture[defId];
        if (d.ShopTab != tab)
        {
            tab = d.ShopTab;
            scroll = 0;
        }
        int i = Items(tab).IndexOf(d);
        if (i < 0)
            return null;
        const float cardW = 170, cardH = 250, gap = 12;
        float h = 330;
        var r = new Rectangle(12, Screen.Height - h - 12, Screen.Width - 24, h);
        float rowX = r.X + 20, rowY = r.Y + 60, rowW = r.Width - 40;
        float maxScroll = MathF.Max(0, Items(tab).Count * (cardW + gap) - rowW);
        scroll = Math.Clamp(i * (cardW + gap) - rowW / 2 + cardW / 2, 0, maxScroll);
        return new System.Numerics.Vector2(rowX + i * (cardW + gap) - scroll + cardW / 2, rowY + cardH / 2);
    }

    void DrawStore(SubImage[] ui)
    {
        float h = 330;
        var r = new Rectangle(12, Screen.Height - h - 12, Screen.Width - 24, h);
        Widgets.Panel(r, new Color(28, 24, 22, 240));
        // tabs along the top edge
        float tx = r.X + 20;
        foreach (int t in UiSprites.TabOrder)
        {
            var (on, off) = UiSprites.Tab(t);
            var img = ui[t == tab ? on : off];
            float w = img.FullWidth * 1.2f;
            if (Widgets.ImageButton(img, tx + w / 2, r.Y + 24, 1.2f) && t != tab)
            {
                tab = t;
                scroll = 0;
                game.Sound(36);
                game.Hint(0x2d, true);
            }
            tx += w + 12;
        }
        if (Widgets.ImageButton(ui[UiSprites.Back], r.X + r.Width - 60, r.Y + 34, 0.7f))
        {
            StoreOpen = false;
            game.Sound(36);
        }
        // the items as paper cards in a scrolling row
        var items = Items(tab);
        hoverDef = null;
        const float cardW = 170, cardH = 250, gap = 12;
        float rowX = r.X + 20, rowY = r.Y + 60, rowW = r.Width - 40;
        float contentW = items.Count * (cardW + gap);
        float maxScroll = MathF.Max(0, contentW - rowW);
        if (Widgets.Hover(r))
            scroll = Math.Clamp(scroll - Input.GetMouseWheelMove() * 120, 0, maxScroll);
        if (scroll > 0 && Widgets.ImageButton(ui[UiSprites.ArrowLeft], r.X + 22, rowY + cardH / 2, 0.9f))
            scroll = MathF.Max(0, scroll - (cardW + gap) * 3);
        if (scroll < maxScroll && Widgets.ImageButton(ui[UiSprites.ArrowRight], r.X + r.Width - 22, rowY + cardH / 2, 0.9f))
            scroll = MathF.Min(maxScroll, scroll + (cardW + gap) * 3);
        var m = Widgets.Mouse;
        // clip the card row to the panel (scissor works in window pixels)
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginScissorMode((int)(rowX * Screen.Scale), (int)(rowY * Screen.Scale - 4), (int)(rowW * Screen.Scale), (int)((cardH + 8) * Screen.Scale));
        for (int i = 0; i < items.Count; i++)
        {
            float x = rowX + i * (cardW + gap) - scroll;
            if (x + cardW < rowX || x > rowX + rowW)
                continue;
            var d = items[i];
            var card = new Rectangle(x, rowY, cardW, cardH);
            bool locked = d.Theme > game.Player.Level;
            bool hover = Raylib.CheckCollisionPointRec(m, card) && x >= rowX - 1 && x + cardW <= rowX + rowW + 1;
            var paper = ui[UiSprites.Paper];
            paper.Draw(x, rowY, Anchor.Top | Anchor.Left, hover && !locked ? Color.White : new Color(225, 220, 210, 255), Flip.None,
                cardW / paper.FullWidth, cardH / paper.FullHeight);
            DrawItemPicture(d, x + cardW / 2, rowY + 112, 140, 110, locked);
            Text.DrawAnchored(Shorten(d.Name, 18), x + cardW / 2, rowY + 128, 17, Anchor.Top | Anchor.HCenter, new Color(40, 30, 25, 255));
            if (locked)
            {
                Widgets.Sprite(ui[UiSprites.Lock], x + cardW / 2, rowY + 60, 1f);
                Text.DrawAnchored(data.Strings[587] + d.Theme, x + cardW / 2, rowY + 160, 15, Anchor.Top | Anchor.HCenter, new Color(150, 30, 30, 255));
            }
            else
            {
                var icon = ui[d.Currency == 1 ? UiSprites.VialSmall : UiSprites.CashSmall];
                string price = d.Category == FurnitureCategory.Cash ? $"{d.Price}" : $"{d.Price}";
                var (pw, _) = Text.Measure(price, 22);
                float px = x + cardW / 2 - (pw + 24) / 2;
                Widgets.Sprite(icon, px + 10, rowY + 176, 0.7f);
                Text.Draw(price, px + 24, rowY + 164, 22, new Color(40, 90, 30, 255));
                if (d.F3C > 0)
                    Text.DrawAnchored($"+{d.F3C:0} XP", x + cardW / 2, rowY + 196, 15, Anchor.Top | Anchor.HCenter, new Color(40, 90, 160, 255));
                if (d.F2C != 0)
                    Text.DrawAnchored($"{(d.F2C > 0 ? "+" : "")}{d.F2C * 100:0.#}% rating/h", x + cardW / 2, rowY + 214, 14, Anchor.Top | Anchor.HCenter, new Color(110, 60, 140, 255));
            }
            if (hover)
            {
                Widgets.Hover(card);
                hoverDef = d;
                if (!locked && Widgets.Clicked(card))
                    Choose(d);
            }
        }
        Rlgl.DrawRenderBatchActive();
        Raylib.EndScissorMode();
        if (hoverDef != null)
            Widgets.Label(hoverDef.Description, Screen.Width / 2, r.Y - 22, 18, Anchor.Center, Widgets.Gold);
    }

    FurnitureDef? hoverDef;

    /// <summary>Test scripts: the STORE button of the move bar, and the storage box's first card.</summary>
    public Rectangle? StoreButtonRect { get; private set; }
    public Rectangle? FirstStoredRect { get; private set; }

    /// <summary>
    /// The storage box (Storage::vf02 / FUN_001b3050): the store's frame and tabs, a card for each kind of piece put
    /// away on the tab (with how many), string 644 when there is none; a card picks the piece up to place.
    /// </summary>
    void DrawStorage(SubImage[] ui)
    {
        float h = 330;
        var r = new Rectangle(12, Screen.Height - h - 12, Screen.Width - 24, h);
        Widgets.Panel(r, new Color(28, 24, 22, 240));
        float tx = r.X + 20;
        foreach (int t in UiSprites.TabOrder)
        {
            var (on, off) = UiSprites.Tab(t);
            var img = ui[t == tab ? on : off];
            float w = img.FullWidth * 1.2f;
            if (Widgets.ImageButton(img, tx + w / 2, r.Y + 24, 1.2f) && t != tab)
            {
                tab = t;
                scroll = 0;
                game.Sound(36);
            }
            tx += w + 12;
        }
        ui[UiSprites.Storage].Draw(tx + 20, r.Y + 24, Anchor.Left | Anchor.VCenter, Color.White, Flip.None, 0.7f, 0.7f);
        if (Widgets.ImageButton(ui[UiSprites.Back], r.X + r.Width - 60, r.Y + 34, 0.7f))
        {
            StoreOpen = false;
            storage = false;
            game.Sound(36);
        }
        var kinds = cafe.Stored.Where(id => data.Furniture[id].ShopTab == tab)
            .GroupBy(id => id).Select(g => (Def: data.Furniture[g.Key], Count: g.Count())).ToList();
        const float cardW = 170, cardH = 250, gap = 12;
        float rowX = r.X + 20, rowY = r.Y + 60, rowW = r.Width - 40;
        if (kinds.Count == 0)
        {
            var lines = Widgets.WrapText(data.Strings[644], rowW * 0.7f, 22);
            for (int i = 0; i < lines.Count; i++)
                Text.DrawAnchored(lines[i], r.X + r.Width / 2, rowY + cardH / 2 - lines.Count * 13 + i * 26, 22,
                    Anchor.Top | Anchor.HCenter, new Color(230, 230, 230, 255));
            return;
        }
        float maxScroll = MathF.Max(0, kinds.Count * (cardW + gap) - rowW);
        if (Widgets.Hover(r))
            scroll = Math.Clamp(scroll - Input.GetMouseWheelMove() * 120, 0, maxScroll);
        if (scroll > 0 && Widgets.ImageButton(ui[UiSprites.ArrowLeft], r.X + 22, rowY + cardH / 2, 0.9f))
            scroll = MathF.Max(0, scroll - (cardW + gap) * 3);
        if (scroll < maxScroll && Widgets.ImageButton(ui[UiSprites.ArrowRight], r.X + r.Width - 22, rowY + cardH / 2, 0.9f))
            scroll = MathF.Min(maxScroll, scroll + (cardW + gap) * 3);
        var m = Widgets.Mouse;
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginScissorMode((int)(rowX * Screen.Scale), (int)(rowY * Screen.Scale - 4), (int)(rowW * Screen.Scale), (int)((cardH + 8) * Screen.Scale));
        for (int i = 0; i < kinds.Count; i++)
        {
            float x = rowX + i * (cardW + gap) - scroll;
            if (x + cardW < rowX || x > rowX + rowW)
                continue;
            var (d, count) = kinds[i];
            var card = new Rectangle(x, rowY, cardW, cardH);
            if (i == 0)
                FirstStoredRect = card;
            bool hover = Raylib.CheckCollisionPointRec(m, card) && x >= rowX - 1 && x + cardW <= rowX + rowW + 1;
            var paper = ui[UiSprites.Paper];
            paper.Draw(x, rowY, Anchor.Top | Anchor.Left, hover ? Color.White : new Color(225, 220, 210, 255), Flip.None,
                cardW / paper.FullWidth, cardH / paper.FullHeight);
            DrawItemPicture(d, x + cardW / 2, rowY + 112, 140, 110, false);
            Text.DrawAnchored(Shorten(d.Name, 18), x + cardW / 2, rowY + 128, 17, Anchor.Top | Anchor.HCenter, new Color(40, 30, 25, 255));
            Text.DrawAnchored("x" + count, x + cardW / 2, rowY + 164, 26, Anchor.Top | Anchor.HCenter, new Color(40, 90, 30, 255));
            if (hover)
            {
                Widgets.Hover(card);
                if (Widgets.Clicked(card))
                {
                    game.Sound(36);
                    StartPlacing(d, stored: true);
                }
            }
        }
        Rlgl.DrawRenderBatchActive();
        Raylib.EndScissorMode();
    }

    static string Shorten(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + ".";

    void DrawItemPicture(FurnitureDef d, float cx, float bottom, float maxW, float maxH, bool locked)
    {
        SubImage? img = d.Category == FurnitureCategory.Floor
            ? (d.ImageIndex >= 0 && d.ImageIndex < data.Tiles.Length ? data.Tiles[d.ImageIndex] : null)
            : data.Sheet(d) is { Length: > 0 } s ? s[0] : null;
        if (img == null)
            return;
        float s2 = MathF.Min(1f, MathF.Min(maxW / Math.Max(1, img.FullWidth), maxH / Math.Max(1, img.FullHeight)));
        var tint = locked ? new Color(80, 80, 80, 255) : Colors.FromArgb(d.Tint);
        img.Draw(cx, bottom, Anchor.Bottom | Anchor.HCenter, tint, Flip.None, s2, s2);
    }

    void Choose(FurnitureDef d)
    {
        game.Sound(36);
        var why = game.CannotBuy(d);
        if (why != null)
        {
            Say(why);
            return;
        }
        switch (d.Category)
        {
            case FurnitureCategory.Cash:
                // toxin for cash (I30 = the amount)
                if (game.Buy(d))
                {
                    game.Player.Money += d.I30;
                    Say($"+${d.I30}");
                }
                break;
            case FurnitureCategory.Expansion:
                // FUN_000c0ef4: paid for, the room grows at once
                if (d.Width <= cafe.RoomWidth && d.Depth <= cafe.RoomDepth)
                    break;
                if (game.Buy(d))
                {
                    game.Expand(d);
                    // the walls are new: what was done to the old ones can't be undone
                    history.Clear();
                }
                break;
            default:
                StartPlacing(d);
                break;
        }
    }
}
