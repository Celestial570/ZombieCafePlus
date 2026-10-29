using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>
/// Two Point style building tools on top of the original decorating mode: several pieces picked out at once
/// (Ctrl+click, Ctrl+drag) and moved, stored or sold together; a layout copied (Ctrl+C) and pasted as new
/// purchases (Ctrl+V); and undo (Ctrl+Z) of what was placed, moved, stored, sold or painted since the
/// decorating began - a purchase undone is refunded in full.
/// </summary>
public sealed partial class EditMode
{
    // ---------------------------------------------------------------- picking out several pieces

    readonly HashSet<Furniture> selected = new();
    static readonly Color SelectedTint = new(140, 195, 255, 255);
    bool boxing;
    Vector2 boxFrom, boxTo;

    static bool Ctrl => Input.IsKeyDown(KeyboardKey.LeftControl) || Input.IsKeyDown(KeyboardKey.RightControl);

    /// <summary>Test scripts and the bars: the extras' buttons by name, as last drawn.</summary>
    public Dictionary<string, Rectangle> Buttons { get; } = new();

    void Select(Furniture f, bool on)
    {
        if (on ? selected.Add(f) : selected.Remove(f))
            f.TintOverride = on ? SelectedTint : null;
    }

    void ClearSelection()
    {
        foreach (var f in selected)
            f.TintOverride = null;
        selected.Clear();
        boxing = false;
    }

    /// <summary>Leaving decorating mode: the picked-out pieces let go, and what was done can no longer be undone.</summary>
    void EndSession()
    {
        ClearSelection();
        EndStroke();
        history.Clear();
    }

    List<Furniture> PlacedPieces() =>
        cafe.Objects.OfType<Furniture>().Where(f => f.Cell >= 0 && f.Visible).ToList();

    Furniture? PieceAt(Vector2 world)
    {
        var objs = cafe.Objects;
        for (int i = objs.Count - 1; i >= 0; i--)
            if (objs[i] is Furniture f && f.Cell >= 0 && f.Visible && Hit(f, world))
                return f;
        return null;
    }

    /// <summary>
    /// Ctrl+click puts a piece in or out of the picked-out set; Ctrl+drag adds every piece whose cell lies in the box.
    /// True while it has the mouse.
    /// </summary>
    bool UpdateSelection(Vector2 world, bool click)
    {
        if (!Editing)
            return false;
        if (Input.IsMouseButtonPressed(MouseButton.Left) && Ctrl && !Widgets.MouseOverUi)
        {
            boxing = true;
            boxFrom = boxTo = world;
            return true;
        }
        if (!boxing)
            return false;
        boxTo = world;
        if (Input.IsMouseButtonDown(MouseButton.Left))
            return true;
        boxing = false;
        if (Vector2.Distance(boxFrom, boxTo) < 12)
        {
            if (PieceAt(world) is { } f)
            {
                Select(f, !selected.Contains(f));
                game.Sound(36);
            }
            return true;
        }
        var box = BoxRect();
        int before = selected.Count;
        foreach (var f in PlacedPieces())
        {
            var c = cafe.CellCenter(f.Cell);
            if (Raylib.CheckCollisionPointRec(c, box))
                Select(f, true);
        }
        if (selected.Count != before)
            game.Sound(36);
        return true;
    }

    Rectangle BoxRect() => new(MathF.Min(boxFrom.X, boxTo.X), MathF.Min(boxFrom.Y, boxTo.Y),
        MathF.Abs(boxTo.X - boxFrom.X), MathF.Abs(boxTo.Y - boxFrom.Y));

    // ---------------------------------------------------------------- moving and pasting several at once

    /// <summary>A piece of a group in hand: where it sits from the group's anchor (diamond cells), and where it came from.</summary>
    sealed class Piece
    {
        public required Furniture F;
        public int Du, Dv;
        public int Cell = -1;
        public int FromCell = -1, FromOrientation;
    }

    List<Piece>? group;
    bool pasting;

    /// <summary>A copied layout: each piece's kind, turn and place from the middle piece.</summary>
    static readonly List<(FurnitureDef Def, int Du, int Dv, int Orientation)> clipboard = new();

    static bool InYard(FurnitureDef d) =>
        d.Category is FurnitureCategory.Tombstone or FurnitureCategory.ToxinGenerator or FurnitureCategory.Habitat;

    bool FitsAt(FurnitureDef d, int cell, int orientation) =>
        InYard(d) ? cafe.CanPlaceInYard(cell) : cafe.CanPlace(d, cell, orientation);

    static bool FoodOn(Furniture f) => f is Stove { Busy: true } || f is ServingCounter { Dishes.Count: > 0 } || f.Attached != null;

    /// <summary>Taking these away would leave the cafe without a stove or a serving counter (string 330).</summary>
    bool LeavesNone(ICollection<Furniture> pieces) =>
        pieces.Any(f => f.Def.Category is FurnitureCategory.Stove or FurnitureCategory.Counter
            && !cafe.FindFurniture(f.Def.Category, -1).Any(other => !pieces.Contains(other)));

    /// <summary>The picked-out pieces come up together, keeping their places around <paramref name="anchor"/>.</summary>
    void PickUpGroup(Furniture anchor)
    {
        var pieces = selected.ToList();
        ClearSelection();
        CancelTool();
        var (au, av) = cafe.Diamond(anchor.Cell);
        group = new List<Piece>();
        foreach (var f in pieces)
        {
            var (u, v) = cafe.Diamond(f.Cell);
            group.Add(new Piece { F = f, Du = u - au, Dv = v - av, FromCell = f.Cell, FromOrientation = f.Orientation });
            cafe.RemoveFurniture(f);
        }
        pasting = false;
        tool = Tool.Group;
        StoreOpen = false;
        game.Sound(36);
    }

    /// <summary>Can this piece be bought again from the store (and so be copied)?</summary>
    bool Copyable(FurnitureDef d) =>
        d.ForSale && d.Currency <= 1 && d.ShopTab != 255 && d.Theme != 255 && d.S44 < 1
        && d.Category is not (FurnitureCategory.Habitat or FurnitureCategory.Tombstone or FurnitureCategory.Expansion or FurnitureCategory.Cash
            or FurnitureCategory.ToxinGenerator);

    void Copy(Vector2 world)
    {
        var pieces = selected.Count > 0 ? selected.ToList() : PieceAt(world) is { } one ? [one] : [];
        if (pieces.Count == 0)
        {
            Say("Ctrl+click pieces to pick them out, then copy them.");
            return;
        }
        var ok = pieces.Where(f => Copyable(f.Def)).ToList();
        if (ok.Count == 0)
        {
            Say("Those can't be bought in the store, so they can't be copied.");
            return;
        }
        // the piece nearest the middle goes under the mouse
        var cells = ok.Select(f => cafe.Diamond(f.Cell)).ToList();
        float mu = (float)cells.Average(c => c.U), mv = (float)cells.Average(c => c.V);
        int mid = Enumerable.Range(0, ok.Count).MinBy(i => MathF.Abs(cells[i].U - mu) + MathF.Abs(cells[i].V - mv));
        clipboard.Clear();
        for (int i = 0; i < ok.Count; i++)
            clipboard.Add((ok[i].Def, cells[i].U - cells[mid].U, cells[i].V - cells[mid].V, ok[i].Orientation));
        game.Sound(36);
        int left = pieces.Count - ok.Count;
        Say($"Copied {Pieces(ok.Count)}" + (left > 0 ? $" ({left} that the store doesn't sell left out)" : "") + ". Ctrl+V to paste.");
    }

    static string Pieces(int n) => n == 1 ? "1 piece" : $"{n} pieces";

    void StartPaste()
    {
        if (clipboard.Count == 0)
        {
            Say("Nothing copied yet: pick out pieces and press Ctrl+C.");
            return;
        }
        ClearSelection();
        CancelTool();
        group = clipboard.Select(c =>
        {
            var f = Furniture.Create(data, c.Def);
            if (c.Orientation != 0)
                f.SetOrientation(c.Orientation);
            return new Piece { F = f, Du = c.Du, Dv = c.Dv };
        }).ToList();
        pasting = true;
        tool = Tool.Group;
        StoreOpen = false;
        game.Sound(36);
    }

    (int Cash, int Toxin) PasteCost() => group == null ? (0, 0)
        : (group.Where(p => p.F.Def.Currency != 1).Sum(p => p.F.Def.Price), group.Where(p => p.F.Def.Currency == 1).Sum(p => p.F.Def.Price));

    /// <summary>Why the layout in hand can't be bought (null: it can) - a piece above the player's level, or the money.</summary>
    string? PasteProblem()
    {
        if (group == null)
            return "";
        foreach (var p in group)
            if (p.F.Def.Theme > game.Player.Level)
                return data.Strings[587] + p.F.Def.Theme;
        var (cash, toxin) = PasteCost();
        if (game.Player.Money < cash)
            return data.Strings[441];
        if (game.Player.Toxin < toxin)
            return data.Strings[320].Split('.')[0] + ".";
        return null;
    }

    void UpdateGroup(bool click)
    {
        if (group == null)
            return;
        hoverOk = hoverCell >= 0;
        if (hoverCell >= 0)
        {
            var (u, v) = cafe.Diamond(hoverCell);
            foreach (var p in group)
            {
                p.Cell = cafe.IndexOf(u + p.Du, v + p.Dv);
                if (p.Cell < 0)
                {
                    hoverOk = false;
                    continue;
                }
                p.F.PlaceOn(cafe, p.Cell);
                if (!FitsAt(p.F.Def, p.Cell, p.F.Orientation))
                    hoverOk = false;
            }
        }
        if (click && hoverOk)
            DropGroup();
    }

    void DropGroup()
    {
        if (group == null)
            return;
        if (pasting && PasteProblem() is { } why)
        {
            Say(why);
            return;
        }
        foreach (var p in group)
        {
            p.F.TintOverride = null;
            if (pasting)
                game.Buy(p.F.Def);
            cafe.PlaceFurniture(p.F, p.Cell);
            if (pasting)
                Bought(p.F);
        }
        game.Sound(90);
        if (pasting)
        {
            RecordBought(group.Select(p => p.F).ToList());
            group = null;
            // Shift keeps pasting while the money lasts
            if (Input.IsKeyDown(KeyboardKey.LeftShift))
            {
                StartPaste();
                return;
            }
        }
        else
            RecordMoved(group.Select(p => (p.F, p.FromCell, p.FromOrientation)).ToList());
        group = null;
        pasting = false;
        tool = Tool.None;
    }

    /// <summary>The group in hand goes back: moved pieces to where they stood, a paste not bought.</summary>
    void CancelGroup()
    {
        if (group != null && !pasting)
            foreach (var p in group)
            {
                p.F.TintOverride = null;
                if (p.F.Orientation != p.FromOrientation)
                    p.F.SetOrientation(p.FromOrientation);
                cafe.PlaceFurniture(p.F, p.FromCell);
            }
        group = null;
        pasting = false;
    }

    List<(Furniture F, int From, int FromO)> GroupOrigins() =>
        group!.Select(p => (p.F, p.FromCell, p.FromOrientation)).ToList();

    /// <summary>The moved group into the storage box (as FUN_000cb270 does a piece at a time).</summary>
    bool StoreGroup()
    {
        if (group == null || pasting)
            return false;
        var pieces = group.Select(p => p.F).ToList();
        if (pieces.Any(f => f.Def.ShopTab == 255))
        {
            Say(data.Strings[647]);
            return false;
        }
        if (pieces.Any(FoodOn))
        {
            Say("There is food on one of them - serve it or let it finish first.");
            return false;
        }
        if (LeavesNone(pieces))
        {
            Say(data.Strings[330]);
            return false;
        }
        foreach (var f in pieces)
        {
            cafe.Stored.Add(f.Def.Id);
            if (f is Habitat home)
                game.PetStored(home);
        }
        RecordStored(GroupOrigins());
        game.Sound(64);
        group = null;
        tool = Tool.None;
        return true;
    }

    /// <summary>The moved group salvaged, each piece for what the store pays back (FUN_000c3118).</summary>
    bool SellGroup()
    {
        if (group == null || pasting)
            return false;
        var pieces = group.Select(p => p.F).ToList();
        if (LeavesNone(pieces))
        {
            Say(data.Strings[330]);
            return false;
        }
        int total = 0;
        bool pets = false;
        foreach (var f in pieces)
        {
            if (f is Habitat sold)
            {
                game.PetSold(sold);
                pets = true;
            }
            total += game.SellPrice(f.Def);
        }
        // a pet sold with its home is gone for good, so that can't be undone
        if (!pets)
            RecordSold(GroupOrigins());
        game.Player.Money += total;
        game.Sound(8);
        Say($"{data.Strings[329]}: ${total}");
        group = null;
        tool = Tool.None;
        return true;
    }

    /// <summary>Ctrl+Z, Ctrl+C, Ctrl+V and Delete. True when one was used.</summary>
    bool Shortcuts(Vector2 world)
    {
        if (!Editing || StoreOpen)
            return false;
        if (Ctrl && Input.IsKeyPressed(KeyboardKey.Z))
        {
            if (CanUndoNow)
                Undo();
            return true;
        }
        if (Ctrl && Input.IsKeyPressed(KeyboardKey.C))
        {
            if (tool == Tool.None)
                Copy(world);
            return true;
        }
        if (Ctrl && Input.IsKeyPressed(KeyboardKey.V))
        {
            if (tool is Tool.None or Tool.Group)
                StartPaste();
            return true;
        }
        if (Input.IsKeyPressed(KeyboardKey.Delete) && tool == Tool.None && selected.Count > 0)
        {
            PickUpGroup(selected.First());
            if (!SellGroup())
                CancelTool();
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- undo

    /// <summary>How an undo went: done, refused for now (the step stays to try again), or no longer possible.</summary>
    readonly record struct Outcome(bool Done, bool Keep, string? Why)
    {
        public static readonly Outcome Ok = new(true, false, null);
        /// <summary>Done, though not wholly: a purchase whose experience already went into a level up comes back at its salvage price.</summary>
        public static readonly Outcome Salvaged = new(true, false, "Undone - at the salvage price, as its XP already went into a level up.");
        public static Outcome Blocked(string why) => new(false, true, why);
        public static Outcome Gone(string why) => new(false, false, why);
    }

    readonly List<(string What, Func<Outcome> Undo)> history = new();
    const int HistoryMax = 50;

    /// <summary>Not while something is in hand that came out of the cafe (it would be lost track of).</summary>
    bool CanUndoNow => history.Count > 0 && tool is not (Tool.Move or Tool.Group) && heldDecor == null;

    void Record(string what, Func<Outcome> undo)
    {
        history.Add((what, undo));
        if (history.Count > HistoryMax)
            history.RemoveAt(0);
    }

    public void Undo()
    {
        if (history.Count == 0)
        {
            Say("Nothing to undo.");
            return;
        }
        var (what, undo) = history[^1];
        var r = undo();
        if (!r.Keep)
            history.RemoveAt(history.Count - 1);
        if (r.Done)
        {
            game.Sound(36);
            Say(r.Why ?? "Undone: " + what);
        }
        else
            Say(r.Why ?? "Can't undo that.");
    }

    bool InCafe(Furniture f) => f.Cell >= 0 && cafe.Objects.Contains(f);

    static string Name(IReadOnlyList<Furniture> pieces) => pieces.Count == 1 ? pieces[0].Def.Name : Pieces(pieces.Count);

    /// <summary>Why pieces standing in the cafe can't be taken up for an undo (null: they can).</summary>
    Outcome? CheckStanding(IEnumerable<Furniture> pieces, bool food)
    {
        foreach (var f in pieces)
        {
            if (!InCafe(f))
                return Outcome.Gone("Can't undo: it isn't where it was put any more.");
            if (!f.Visible)
                return Outcome.Blocked("Can't undo yet: someone is sitting there.");
            if (food && FoodOn(f))
                return Outcome.Blocked("Can't undo yet: there is food on it.");
        }
        return null;
    }

    /// <summary>Pieces out of the cafe stand again where they were (all or none).</summary>
    Outcome? PutBack(List<(Furniture F, int From, int FromO)> pieces)
    {
        foreach (var (f, from, o) in pieces)
            if (!FitsAt(f.Def, from, o))
                return Outcome.Blocked("Can't undo yet: something is in the way of where it stood.");
        foreach (var (f, from, o) in pieces)
        {
            f.TintOverride = null;
            if (f.Orientation != o)
                f.SetOrientation(o);
            cafe.PlaceFurniture(f, from);
        }
        return null;
    }

    /// <summary>
    /// Pieces bought and placed: undone, they go and their price comes back. Not for what brings something that stays
    /// - a pet, a tombstone's uses, a special stove's recipes.
    /// </summary>
    void RecordBought(List<Furniture> pieces)
    {
        if (pieces.Any(f => f.Def.Category is FurnitureCategory.Habitat or FurnitureCategory.Tombstone
            || (f.Def.Category == FurnitureCategory.Stove && f.Def.StoveFamily > 0)))
            return;
        Record("bought " + Name(pieces), () =>
        {
            if (CheckStanding(pieces, true) is { } no)
                return no;
            bool full = true;
            foreach (var f in pieces)
            {
                Select(f, false);
                cafe.RemoveFurniture(f);
                full &= game.Refund(f.Def);
            }
            return full ? Outcome.Ok : Outcome.Salvaged;
        });
    }

    void RecordMoved(List<(Furniture F, int From, int FromO)> moves)
    {
        if (moves.All(m => m.F.Cell == m.From && m.F.Orientation == m.FromO))
            return;
        var pieces = moves.Select(m => m.F).ToList();
        Record("moved " + Name(pieces), () =>
        {
            if (CheckStanding(pieces, false) is { } no)
                return no;
            var now = pieces.Select(f => (f, f.Cell, f.Orientation)).ToList();
            foreach (var f in pieces)
                cafe.RemoveFurniture(f);
            if (PutBack(moves) is { } blocked)
            {
                PutBack(now);
                return blocked;
            }
            return Outcome.Ok;
        });
    }

    void RecordFromStorage(Furniture f)
    {
        Record("placed " + f.Def.Name + " from storage", () =>
        {
            if (CheckStanding([f], true) is { } no)
                return no;
            Select(f, false);
            cafe.RemoveFurniture(f);
            cafe.Stored.Add(f.Def.Id);
            if (f is Habitat home)
                game.PetStored(home);
            return Outcome.Ok;
        });
    }

    void RecordStored(List<(Furniture F, int From, int FromO)> pieces)
    {
        Record("stored " + Name(pieces.Select(p => p.F).ToList()), () =>
        {
            foreach (var g in pieces.GroupBy(p => p.F.Def.Id))
                if (cafe.Stored.Count(id => id == g.Key) < g.Count())
                    return Outcome.Gone("Can't undo: it isn't in the storage box any more.");
            if (PutBack(pieces) is { } blocked)
                return blocked;
            foreach (var (f, _, _) in pieces)
            {
                cafe.Stored.Remove(f.Def.Id);
                if (f is Habitat home)
                    game.PetFromStorage(home);
            }
            return Outcome.Ok;
        });
    }

    void RecordSold(List<(Furniture F, int From, int FromO)> pieces)
    {
        int price = pieces.Sum(p => game.SellPrice(p.F.Def));
        Record("salvaged " + Name(pieces.Select(p => p.F).ToList()), () =>
        {
            if (game.Player.Money < price)
                return Outcome.Blocked($"Can't undo yet: buying it back takes ${price}.");
            if (PutBack(pieces) is { } blocked)
                return blocked;
            game.Player.Money -= price;
            return Outcome.Ok;
        });
    }

    void RecordDecorBought(Furniture f, CafeWall wall)
    {
        Record("bought " + f.Def.Name, () =>
        {
            if (wall.Decor != f)
                return Outcome.Gone("Can't undo: it isn't on that wall any more.");
            wall.Hang(cafe, null);
            return game.Refund(f.Def) ? Outcome.Ok : Outcome.Salvaged;
        });
    }

    void RecordDecorFromStorage(Furniture f, CafeWall wall)
    {
        Record("hung " + f.Def.Name + " from storage", () =>
        {
            if (wall.Decor != f)
                return Outcome.Gone("Can't undo: it isn't on that wall any more.");
            wall.Hang(cafe, null);
            cafe.Stored.Add(f.Def.Id);
            return Outcome.Ok;
        });
    }

    void RecordDecorMoved(Furniture f, CafeWall? from, CafeWall to)
    {
        if (from == null || from == to)
            return;
        Record("moved " + f.Def.Name, () =>
        {
            if (to.Decor != f)
                return Outcome.Gone("Can't undo: it isn't on that wall any more.");
            if (from.Decor != null)
                return Outcome.Blocked("Can't undo yet: something hangs where it was.");
            to.Hang(cafe, null);
            from.Hang(cafe, f);
            return Outcome.Ok;
        });
    }

    void RecordDecorStored(Furniture f, CafeWall? from)
    {
        if (from == null)
            return;
        Record("stored " + f.Def.Name, () =>
        {
            if (!cafe.Stored.Contains(f.Def.Id))
                return Outcome.Gone("Can't undo: it isn't in the storage box any more.");
            if (from.Decor != null)
                return Outcome.Blocked("Can't undo yet: something hangs where it was.");
            cafe.Stored.Remove(f.Def.Id);
            from.Hang(cafe, f);
            return Outcome.Ok;
        });
    }

    void RecordDecorSold(Furniture f, CafeWall? from)
    {
        if (from == null)
            return;
        int price = game.SellPrice(f.Def);
        Record("salvaged " + f.Def.Name, () =>
        {
            if (game.Player.Money < price)
                return Outcome.Blocked($"Can't undo yet: buying it back takes ${price}.");
            if (from.Decor != null)
                return Outcome.Blocked("Can't undo yet: something hangs where it was.");
            game.Player.Money -= price;
            from.Hang(cafe, f);
            return Outcome.Ok;
        });
    }

    // painting: a stroke (one press of the mouse) is undone as one
    readonly List<Func<bool>> stroke = new();
    string strokeWhat = "";

    void PaintedFloor(int cell, FurnitureDef def)
    {
        var c = cafe.Cells[cell];
        var (floor, tint, flip) = (c.Floor, c.Tint, c.Flip);
        strokeWhat = "floor";
        stroke.Add(() =>
        {
            c.Floor = floor;
            c.Tint = tint;
            c.Flip = flip;
            return game.Refund(def);
        });
    }

    void PaintedWall(CafeWall wall, FurnitureDef def)
    {
        var old = wall.Def;
        strokeWhat = "wall";
        stroke.Add(() =>
        {
            wall.SetDef(data, old);
            return game.Refund(def);
        });
    }

    void EndStroke()
    {
        if (stroke.Count == 0)
            return;
        var steps = stroke.ToList();
        stroke.Clear();
        Record($"painted {steps.Count} {strokeWhat}{(steps.Count == 1 ? "" : "s")}", () =>
        {
            bool full = true;
            for (int i = steps.Count - 1; i >= 0; i--)
                full &= steps[i]();
            return full ? Outcome.Ok : Outcome.Salvaged;
        });
    }

    // ---------------------------------------------------------------- drawing

    void DrawGroupWorld()
    {
        if (boxing)
        {
            var b = BoxRect();
            Gfx.Draw.Rect(b, new Color(120, 180, 255, 50));
            var edge = new Color(150, 200, 255, 220);
            Gfx.Draw.Rect(new Rectangle(b.X, b.Y, b.Width, 3), edge);
            Gfx.Draw.Rect(new Rectangle(b.X, b.Y + b.Height - 3, b.Width, 3), edge);
            Gfx.Draw.Rect(new Rectangle(b.X, b.Y, 3, b.Height), edge);
            Gfx.Draw.Rect(new Rectangle(b.X + b.Width - 3, b.Y, 3, b.Height), edge);
        }
        if (tool != Tool.Group || group == null || hoverCell < 0)
            return;
        var tint = hoverOk ? new Color(140, 255, 140, 220) : new Color(255, 110, 110, 200);
        foreach (var p in group.Where(p => p.Cell >= 0).OrderBy(p => p.F.Y))
        {
            p.F.TintOverride = tint;
            p.F.Draw(cafe, data);
        }
    }

    bool TextButton(string key, Rectangle r, string text, bool enabled = true)
    {
        Buttons[key] = r;
        return Widgets.Button(r, text, 18, enabled);
    }

    /// <summary>UNDO and PASTE, side by side from (x, y).</summary>
    void DrawUndoPaste(float x, float y)
    {
        if (TextButton("undo", new Rectangle(x, y, 80, 40), "UNDO", CanUndoNow))
            Undo();
        if (TextButton("paste", new Rectangle(x + 90, y, 80, 40), "PASTE", clipboard.Count > 0))
            StartPaste();
    }

    void DrawSelectionBar(SubImage[] ui)
    {
        var r = new Rectangle(Screen.Width / 2 - 430, Screen.Height - 92, 860, 64);
        Widgets.Panel(r);
        Widgets.Label($"{Pieces(selected.Count)} picked out", r.X + 16, r.Y + 8, 22, Anchor.Top | Anchor.Left, Widgets.Gold);
        Widgets.Label("Click one to move them all.", r.X + 16, r.Y + 38, 15, Anchor.Top | Anchor.Left, new Color(210, 210, 210, 255));
        float x = r.X + r.Width - 580;
        if (TextButton("move", new Rectangle(x, r.Y + 12, 80, 40), "MOVE"))
        {
            PickUpGroup(selected.First());
            return;
        }
        if (TextButton("copy", new Rectangle(x + 90, r.Y + 12, 80, 40), "COPY"))
            Copy(default);
        DrawUndoPaste(x + 180, r.Y + 12);
        float bx = r.X + r.Width - 46;
        Buttons["clear"] = new Rectangle(bx - 30, r.Y + 10, 60, 44);
        if (Widgets.ImageButton(ui[UiSprites.BigX], bx, r.Y + 32, 0.5f))
        {
            ClearSelection();
            return;
        }
        bx -= 70;
        Buttons["selstore"] = new Rectangle(bx - 30, r.Y + 4, 60, 56);
        if (Widgets.ImageButton(ui[UiSprites.Storage], bx, r.Y + 32, 0.65f))
        {
            PickUpGroup(selected.First());
            if (!StoreGroup())
                CancelTool();
            return;
        }
        bx -= 75;
        Buttons["selsell"] = new Rectangle(bx - 30, r.Y + 4, 60, 56);
        if (Widgets.ImageButton(ui[UiSprites.Salvage], bx, r.Y + 32, 0.55f))
        {
            PickUpGroup(selected.First());
            if (!SellGroup())
                CancelTool();
        }
    }

    void DrawGroupBar(SubImage[] ui)
    {
        if (group == null)
            return;
        var r = new Rectangle(Screen.Width / 2 - 340, Screen.Height - 110, 680, 84);
        Widgets.Panel(r);
        Widgets.Label((pasting ? "Pasting " : "Moving ") + Pieces(group.Count), r.X + 16, r.Y + 10, 24, Anchor.Top | Anchor.Left, Widgets.Gold);
        string line;
        if (pasting)
        {
            var (cash, toxin) = PasteCost();
            line = $"${cash}" + (toxin > 0 ? $" + {toxin} toxin" : "") + "  Click a free spot to buy and place them. Shift keeps pasting.";
        }
        else
            line = "Click a free spot to put them down.";
        Widgets.Label(line, r.X + 16, r.Y + 46, 17);
        float bx = r.X + r.Width - 46;
        Buttons["groupcancel"] = new Rectangle(bx - 30, r.Y + 12, 60, 60);
        if (Widgets.ImageButton(ui[UiSprites.BigX], bx, r.Y + 42, 0.6f))
        {
            CancelTool();
            return;
        }
        if (pasting)
            return;
        var pieces = group.Select(p => p.F).ToList();
        bx -= 95;
        Buttons["groupstore"] = new Rectangle(bx - 35, r.Y + 10, 70, 64);
        if (Widgets.ImageButton(ui[UiSprites.Storage], bx, r.Y + 42, 0.8f, !pieces.Any(FoodOn)))
        {
            StoreGroup();
            return;
        }
        bx -= 95;
        Buttons["groupsell"] = new Rectangle(bx - 35, r.Y + 10, 70, 64);
        bool last = LeavesNone(pieces);
        if (Widgets.ImageButton(ui[UiSprites.Salvage], bx, r.Y + 42, 0.7f, !last))
            SellGroup();
        if (last && Widgets.Hover(new Rectangle(bx - 40, r.Y + 10, 80, 64)))
            Widgets.Label(data.Strings[330], Screen.Width / 2, r.Y - 24, 18, Anchor.Center, Widgets.Gold);
    }
}
