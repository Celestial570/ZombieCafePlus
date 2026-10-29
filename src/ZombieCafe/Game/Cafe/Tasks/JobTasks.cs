using Raylib_cs;
using ZombieCafe.Data;

namespace ZombieCafe.Game.Cafe.Tasks;

/// <summary>
/// A customer picks the nearest free chair that faces a usable table and walks there (FindSeatTask,
/// 0x38 bytes), then sits down on it.
/// </summary>
public sealed class FindSeatTask : CafeTask
{
    readonly Cafe cafe;      // +0x20
    FollowTask? follow;      // +0x28
    /// <summary>+0x2c.</summary>
    public Furniture? Chair { get; private set; }
    /// <summary>+0x30.</summary>
    public Furniture? Table { get; private set; }

    public FindSeatTask(CharacterInstance owner, Cafe cafe) : base(owner) => this.cafe = cafe;

    public override void Start()
    {
        base.Start();
        var chairs = cafe.FindFurniture(FurnitureCategory.Chair, Owner.Cell);
        cafe.Events.Hint(0x4b, false);
        foreach (var chair in chairs)
        {
            if (chair.Busy)
                continue;
            var table = cafe.TableInFrontOf(chair);
            if (table == null || cafe.ChairBlocked(chair))
                continue;
            // the table must be clear, or hold a meal (not a dirty plate)
            if (table.Busy && !(table.Attached is Furniture top && top.Def.Id != cafe.Data.K.DirtyPlateDef))
                continue;
            var f = new FollowTask(Owner, chair, cafe);
            f.Start();
            if (f.PlanNow() || f.Success)
            {
                follow = f;
                Chair = chair;
                Table = table;
                break;
            }
            f.Release();
        }
        if (Chair == null)
            Complete();
        else
            Chair.Busy = true;
    }

    public override void StopSoon()
    {
        follow?.StopSoon();
        if (Chair != null)
            Chair.Busy = false;
    }

    public override void Cancel()
    {
        follow?.Cancel();
        if (Chair != null)
            Chair.Busy = false;
        Complete();
    }

    public override void Release() => follow?.Release();

    public override void Update(int dtMs)
    {
        if (Done || follow == null || Chair == null)
            return;
        follow.Update(dtMs);
        if (!follow.Done)
            return;
        if (!follow.Success)
        {
            Chair.Busy = false;
        }
        else
        {
            // sit on the chair, facing the way it faces, at the chair's seat offset
            Owner.Cell = Chair.Cell;
            int o = Chair.Orientation;
            Owner.SetAnimation(CharAnim.Sit, (Facing)o);
            var furn = FurnitureOffsets.Get((int)FurnitureCategory.Chair, Chair.Def.ImageIndex);
            var seat = AttachOffsets.Get(AttachOffsets.Seat, Chair.Def.ImageIndex);
            int j = o is 2 or 3 ? 4 : 0;
            float m = o is 1 or 3 ? -1f : 1f;
            Owner.SetPosition(cafe, Chair.X + m * (seat[j] + furn[j]), Chair.Y + furn[j + 1] + seat[j + 1]);
            // the chair hides and the sitter draws it: whole, behind the body (part 14) when it faces us; split
            // when it faces away: the seat in front of the body (part 14), the backrest further back (part 15)
            Owner.ClearOverride(14);
            Owner.ClearOverride(15);
            var (img, sx, sy, mirrored) = Chair.SpriteAt();
            var tint = Raylib_cs.Color.White;
            var sheet = cafe.Data.Sheet(Chair.Def);
            if (img != null)
            {
                if (o is 2 or 3 && sheet.Length == 4 && ChairBackRecords.TryGet(Chair.Def.ImageIndex, out var rec))
                {
                    // DAT_0042d788: the seat piece's top-left from the sitter, the backrest's from the seat
                    var seatPiece = sheet[3];
                    var backPiece = sheet[2];
                    float sx2 = Owner.X + rec.X1, sy2 = Owner.Y + rec.Y1;
                    float bx = sx2 + rec.X2, by = sy2 + rec.Y2;
                    if (o == 3)
                    {
                        sx2 = Owner.X - rec.X1 - seatPiece.FullWidth;
                        bx = Owner.X - rec.X1 - rec.X2 - backPiece.FullWidth;
                    }
                    if (rec.HasBack)
                        Owner.SetSceneryPart(15, backPiece, bx, by, o == 3, tint);
                    Owner.SetSceneryPart(14, seatPiece, sx2, sy2, o == 3, tint);
                }
                else
                {
                    Owner.SetSceneryPart(o is 2 or 3 ? 14 : 14, img, sx, sy, mirrored, tint);
                }
                Chair.Visible = false;
            }
        }
        Finish(follow.Success);
    }
}

/// <summary>
/// A customer's visit (EatTask, 0x44 bytes): find a seat, wait (up to a minute) for a zombie to serve
/// something, pause, eat for 30 seconds, leave a dirty plate. The cafe's payment handler listens to the end.
/// </summary>
public sealed class EatTask : CafeTask
{
    readonly Cafe cafe;           // +0x20
    CafeTask sub;                 // +0x24
    byte state = 5;               // +0x28
    /// <summary>+0x2c.</summary>
    public Furniture? Chair { get; private set; }
    /// <summary>+0x30.</summary>
    public Furniture? Table { get; private set; }
    bool impatientShown;          // +0x34
    /// <summary>+0x35: a raiding zombie, which never gives up waiting.</summary>
    public bool Raider { get; }
    bool stopping;                // +0x36
    readonly int eatTime;         // +0x38
    /// <summary>+0x40.</summary>
    public bool Cancelled { get; private set; }

    public EatTask(CharacterInstance owner, Cafe cafe, bool raider = false, int eatTime = 0) : base(owner)
    {
        this.cafe = cafe;
        Raider = raider;
        this.eatTime = eatTime != 0 ? eatTime : cafe.Data.K.EatTime;
        sub = new WaitTask(owner, 1);
    }

    public override void Start()
    {
        base.Start();
        sub.Start();
    }

    void ReleaseChair()
    {
        if (Chair == null)
            return;
        Chair.Busy = false;
        Chair.Visible = true;
    }

    /// <summary>The meal (or what is left of it) becomes a dirty plate that a zombie must clear.</summary>
    void LeaveDirtyPlate()
    {
        if (Table == null)
            return;
        Table.Detach();
        Table.Attach(cafe, FoodItem.DirtyPlate(cafe.Data));
        ReleaseChair();
        cafe.DirtyTables.Add(Table);
    }

    void Replace(CafeTask next, bool start)
    {
        sub.Release();
        sub = next;
        if (start)
            sub.Start();
    }

    public override void StopSoon()
    {
        stopping = true;
        sub.StopSoon();
        ReleaseChair();
    }

    public override void Cancel()
    {
        Cancelled = true;
        if (state is 2 or 3)
        {
            Success = true;
            Complete();
            if (Table != null)
            {
                Table.Detach();
                Table.Attach(cafe, FoodItem.DirtyPlate(cafe.Data));
                cafe.DirtyTables.Add(Table);
            }
        }
        else
        {
            sub.Cancel();
        }
        ReleaseChair();
    }

    public override void Release() => sub.Release();

    /// <summary>Waiting for food: how long until the customer gives up.</summary>
    public int PatienceLeft => state == 1 && sub is WaitTask w ? w.Remaining : int.MaxValue;
    public bool IsWaitingForFood => state == 1;
    public bool IsEating => state is 2 or 3;

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        sub.Update(dtMs);
        if (!sub.Done)
        {
            if (state != 1)
                return;
            var t = Chair == null ? null : cafe.TableInFrontOf(Chair);
            if (t != Table || Table == null)
            {
                sub.StopSoon();
                return;
            }
            if (!Table.Busy)
            {
                if (((WaitTask)sub).Remaining < 5001 && !impatientShown)
                {
                    impatientShown = true;
                    cafe.AddNote(new StatusNote(cafe, Owner, StatusNote.Kind.Impatient, "", cafe.Data.K.NoteTime));
                }
            }
            else
            {
                // served: a short pause, then eat
                Replace(new WaitTask(Owner, cafe.Data.K.PreEatPause), false);
                cafe.Waiting.Remove(Owner);
                cafe.Unreachable.Remove(Owner);
                state = 3;
            }
            return;
        }
        if (stopping)
            Finish(false);
        switch (state)
        {
            case 0:
                if (sub.Success)
                {
                    state = 1;
                    Replace(new WaitTask(Owner, Raider || Cheats.PatientCustomers ? int.MaxValue : cafe.Data.K.CustomerPatience), false);
                    cafe.Waiting.Add(Owner);
                    return;
                }
                break;
            case 1:
                // gave up waiting
                cafe.Waiting.Remove(Owner);
                cafe.Unreachable.Remove(Owner);
                ReleaseChair();
                if (Raider && Chair != null)
                {
                    state = 5;
                    Replace(new MoveTask(Owner, cafe, cafe.Neighbour(Owner.Cell, Chair.Orientation)), true);
                    return;
                }
                if (cafe.TotalServings() == 0)
                    cafe.Events.Hint(0x17, true);
                break;
            case 2:
                Finish(sub.Success);
                LeaveDirtyPlate();
                if (sub.Success)
                    cafe.Events.Hint(0xc, false);
                return;
            case 3:
                if (sub.Success)
                {
                    Replace(new WaitTask(Owner, eatTime), false);
                    Owner.SetAnimation(CharAnim.Eat, Owner.Facing);
                    state = 2;
                }
                else
                {
                    LeaveDirtyPlate();
                }
                return;
            case 4:
                return;
            case 5:
            {
                var seat = new FindSeatTask(Owner, cafe);
                Replace(seat, true);
                Chair = seat.Chair;
                if (Chair != null)
                {
                    Table = seat.Table;
                    state = 0;
                    return;
                }
                if (Raider)
                {
                    Replace(new WaitTask(Owner, 1), true);
                    return;
                }
                if (cafe.IsInside(Owner.Cell))
                    cafe.Events.Hint(0x3b, false);
                break;
            }
            default:
                return;
        }
        Finish(false);
    }
}

/// <summary>
/// A cook goes to a stove and works it until the dish is done (CookTask, 0x34 bytes). Cooking speed is
/// the chef's cook speed times the stove's.
/// </summary>
public sealed class CookTask : CafeTask
{
    readonly Cafe cafe;        // +0x20
    readonly Stove stove;      // +0x24
    FollowTask? follow;        // +0x28
    byte state;                // +0x2c
    bool released;

    public Stove Stove => stove;

    public CookTask(CharacterInstance owner, Stove stove, Cafe cafe) : base(owner)
    {
        this.cafe = cafe;
        this.stove = stove;
        follow = new FollowTask(owner, stove, cafe);
        stove.AssignedCooks++;
    }

    public override void Start()
    {
        base.Start();
        follow?.Start();
        cafe.Events.Hint(2, false);
        Owner.Character.Job = Job.Cooking;
    }

    void ResetJob()
    {
        if (Owner.Character.Job == Job.Cooking)
            Owner.Character.Job = Job.None;
    }

    public override void StopSoon()
    {
        if (follow == null)
        {
            ResetJob();
            Complete();
            return;
        }
        follow.StopSoon();
    }

    public override void Cancel()
    {
        if (follow == null)
            return;
        follow.Cancel();
        ResetJob();
        Complete();
    }

    public override void Release()
    {
        if (released)
            return;
        released = true;
        stove.AssignedCooks--;
        follow?.Release();
    }

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        Owner.Character.Drain(cafe.Data, dtMs);
        switch (state)
        {
            case 0:
                follow!.Update(dtMs);
                if (!follow.Done)
                    return;
                bool ok = follow.Success;
                follow.Release();
                follow = null;
                if (!ok)
                {
                    ResetJob();
                    Finish(false);
                    return;
                }
                state++;
                return;
            case 1:
                Owner.FaceTowards(stove.X, stove.Y);
                Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
                state++;
                return;
            case 2:
                if (!stove.Busy || stove.Operator != null || stove.IsBurnt(cafe) || stove.IsDone)
                {
                    StopSoon();
                    return;
                }
                cafe.Events.Hint(0x4e, false);
                stove.Work(cafe, Owner, 0);
                cafe.Events.Sound(0xe);
                cafe.Events.Hint(0x30, false);
                Owner.SetAnimation(CharAnim.Operate, Owner.Facing);
                state++;
                return;
            case 3:
                if (!stove.Busy)
                {
                    StopSoon();
                    return;
                }
                var chef = cafe.Events.Chef;
                int amount = (int)(dtMs * chef.F40 * stove.Def.F34);
                stove.Work(cafe, Owner, amount);
                if (stove.IsDone)
                {
                    Owner.Character.Job = Job.None;
                    Finish(true);
                    cafe.Events.Hint(10, true);
                }
                return;
        }
    }
}

/// <summary>
/// Carry a finished dish from the stove to the best serving counter (DishToCounterTask, 0x48 bytes).
/// Orders for friends (the delivery box) are not part of the port yet.
/// </summary>
public sealed class DishToCounterTask : CafeTask
{
    readonly Cafe cafe;           // +0x24
    readonly Stove stove;         // +0x28
    ServingCounter? counter;      // +0x30
    FollowTask? follow;           // +0x38
    Dish? dish;                   // +0x3c
    bool stopping;                // +0x40
    int timer;                    // +0x42
    byte state;                   // +0x20

    public DishToCounterTask(CharacterInstance owner, Cafe cafe, Stove stove) : base(owner)
    {
        this.cafe = cafe;
        this.stove = stove;
    }

    void SetFollow(FollowTask? f)
    {
        follow?.Release();
        follow = f;
    }

    public override void Start()
    {
        base.Start();
        state = 0;
        SetFollow(new FollowTask(Owner, stove, cafe));
        follow!.Start();
        stopping = false;
    }

    public override void StopSoon()
    {
        if (follow == null || state != 0)
            return;
        follow.StopSoon();
        stopping = true;
    }

    public override void Cancel()
    {
        follow?.Cancel();
        if (dish == null)
        {
            Finish(false);
            return;
        }
        if (state is >= 3 and <= 6)
        {
            // still holding the dish: put it on a counter straight away
            counter = cafe.BestCounterFor(dish.Food);
            if (counter != null)
            {
                Owner.CarryFood(null);
                counter.AddDish(cafe, dish);
                cafe.Events.DishPlaced(dish, Owner);
            }
        }
        Owner.CarryFood(null);
        dish = null;
        Finish(true);
    }

    public override void Release() => SetFollow(null);

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        switch (state)
        {
            case 0:
                follow!.Update(dtMs);
                if (follow.Done)
                {
                    if (!follow.Success || stopping)
                        Finish(false);
                    else
                        state = 1;
                    SetFollow(null);
                    return;
                }
                if (stove.Busy)
                    return;
                Finish(false);
                return;
            case 1:
                if (!stove.Busy)
                {
                    Finish(false);
                    return;
                }
                Owner.SetAnimation(CharAnim.Serve, Owner.Facing);
                Owner.FaceTowards(stove.X, stove.Y);
                timer = cafe.Data.K.HandOverTime;
                state = 2;
                return;
            case 2:
                if (!stove.Busy)
                {
                    Finish(false);
                    return;
                }
                timer -= dtMs;
                if (timer >= 0)
                    return;
                if (!stove.IsDone)
                {
                    Finish(false);
                    return;
                }
                dish = stove.TakeDish(cafe);
                if (dish != null && cafe.BurnMode == 3)
                    dish.Servings = (int)(dish.Servings * 1.1);
                cafe.Events.Hint(0x2c, true);
                state = string.IsNullOrEmpty(dish?.OrderedBy) ? (byte)3 : (byte)7;
                return;
            case 3:
            {
                if (dish == null)
                {
                    Finish(false);
                    return;
                }
                counter = null;
                foreach (var c in cafe.CountersFor(dish.Food, Owner.Cell))
                {
                    var f = new FollowTask(Owner, c, cafe, CharAnim.Carry);
                    SetFollow(f);
                    f.Start();
                    if (f.PlanNow() || f.Success)
                    {
                        counter = c;
                        cafe.Events.Sound(cafe.Events.RandomServeSound());
                        Owner.CarryFood(cafe.Data.FoodImage(dish.Food));
                        break;
                    }
                    SetFollow(null);
                }
                if (follow == null)
                    return;
                state = 4;
                return;
            }
            case 4:
                follow!.Update(dtMs);
                if (!follow.Done)
                    return;
                Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
                state = follow.Success ? (byte)5 : (byte)3;
                SetFollow(null);
                return;
            case 5:
                Owner.SetAnimation(CharAnim.Serve, Owner.Facing);
                Owner.FaceTowards(counter!.X, counter.Y);
                timer = cafe.Data.K.HandOverTime;
                state = 6;
                return;
            case 6:
                timer -= dtMs;
                if (timer >= 0)
                    return;
                cafe.Events.Sound(cafe.Events.RandomServeSound());
                counter!.AddDish(cafe, dish!);
                Owner.CarryFood(null);
                cafe.Events.DishPlaced(dish!, Owner);
                dish = null;
                Finish(true);
                return;
            default:
                // delivery orders (states 7..10) are not supported: put the dish on a counter instead
                state = 3;
                return;
        }
    }
}

/// <summary>
/// A staff zombie takes one serving from a counter to a waiting customer's table (ServeTask, 0x4c bytes),
/// or puts it back if the customer leaves on the way.
/// </summary>
public sealed class ServeTask : CafeTask
{
    readonly Cafe cafe;              // +0x24
    Furniture? table;                // +0x28
    ServingCounter? counter;         // +0x2c
    FollowTask? follow;              // +0x30
    Dish? dish;                      // +0x34
    bool stopping;                   // +0x38
    CharacterInstance? customer;     // +0x3c
    int timer;                       // +0x44
    bool customerGone;               // +0x46
    byte state;                      // +0x20

    public ServeTask(CharacterInstance owner, Cafe cafe, CharacterInstance customer) : base(owner)
    {
        this.cafe = cafe;
        this.customer = customer;
        cafe.Waiting.Remove(customer);
    }

    void SetFollow(FollowTask? f)
    {
        follow?.Release();
        follow = f;
    }

    public override void Start()
    {
        base.Start();
        stopping = false;
        state = 11;
    }

    public override void StopSoon()
    {
        if (follow == null || state != 0)
            return;
        follow.StopSoon();
        stopping = true;
    }

    public override void Cancel()
    {
        follow?.Cancel();
        if (dish != null)
        {
            var c = cafe.BestCounterFor(dish.Food);
            if (c != null)
            {
                c.AddDish(cafe, new Dish(dish.Food, 1, dish.Modifier));
                dish = null;
                Owner.ClearOverride(0);
            }
        }
        Complete();
    }

    public override void Release() => SetFollow(null);

    /// <summary>FUN_001cd838: the customer got up (or turned into a zombie).</summary>
    void CheckCustomer()
    {
        if (customer == null)
            return;
        if (customer.AnimId == (int)CharAnim.Sit)
        {
            customerGone = customer.Character.IsZombie;
            if (!customerGone)
                return;
        }
        else
        {
            customerGone = true;
        }
        customer = null;
    }

    /// <summary>FUN_001cd870: forget the customer and take the dish back.</summary>
    void GiveUp()
    {
        if (customer != null)
            cafe.Waiting.Remove(customer);
        state = 8;
    }

    bool StartFollow(Furniture target, CharAnim? anim)
    {
        var f = new FollowTask(Owner, target, cafe, anim);
        SetFollow(f);
        f.Start();
        if (f.PlanNow() || f.Success)
            return true;
        SetFollow(null);
        return false;
    }

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        CheckCustomer();
        Owner.Character.Drain(cafe.Data, dtMs);
        switch (state)
        {
            case 11:
                // to the nearest counter with something on it
                counter = null;
                foreach (var f in cafe.FindFurniture(FurnitureCategory.Counter, Owner.Cell))
                {
                    if (f is ServingCounter c && c.TotalServings != 0 && StartFollow(c, null))
                    {
                        counter = c;
                        break;
                    }
                }
                if (follow == null)
                {
                    cafe.Events.Hint(0x43, false);
                    Finish(false);
                }
                state = 0;
                return;
            case 0:
                follow!.Update(dtMs);
                if (customerGone)
                {
                    Finish(false);
                    return;
                }
                if (!follow.Done)
                    return;
                Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
                if (!stopping && follow.Success)
                    state = 1;
                else
                    Finish(false);
                SetFollow(null);
                return;
            case 1:
                if (customerGone)
                    Finish(false);
                Owner.SetAnimation(CharAnim.Serve, Owner.Facing);
                Owner.FaceTowards(counter!.X, counter.Y);
                timer = cafe.Data.K.HandOverTime;
                state = 2;
                return;
            case 2:
                if (customerGone)
                    Finish(false);
                timer -= dtMs;
                if (timer >= 0)
                    return;
                if (cafe.TotalServings() <= 0)
                {
                    Finish(false);
                    return;
                }
                cafe.Events.Sound(cafe.Events.RandomServeSound());
                dish = counter!.TakeServing(cafe, true);
                state = dish != null ? (byte)3 : (byte)11;
                return;
            case 3:
                if (customer != null && customer.AnimId == (int)CharAnim.Sit)
                {
                    table = cafe.TableAtSeat(customer.Cell);
                    if (table != null && !table.Busy)
                    {
                        if (StartFollow(table, CharAnim.Carry))
                        {
                            Owner.CarryFood(cafe.Data.FoodImage(dish!.Food));
                            state = 4;
                        }
                        else
                        {
                            state = 8;
                        }
                    }
                    return;
                }
                state = 8;
                return;
            case 4:
                if (customerGone)
                {
                    GiveUp();
                    SetFollow(null);
                    state = 8;
                    return;
                }
                follow!.Update(dtMs);
                if (!follow.Done)
                    return;
                Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
                state = follow.Success ? (byte)5 : (byte)3;
                SetFollow(null);
                return;
            case 5:
                if (customerGone)
                {
                    GiveUp();
                    return;
                }
                if (customer != null && customer.AnimId == (int)CharAnim.Sit && table != null && !table.Busy)
                {
                    Owner.SetAnimation(CharAnim.Serve, Owner.Facing);
                    Owner.FaceTowards(table.X, table.Y);
                    Owner.Character.Served++;
                    timer = cafe.Data.K.HandOverTime;
                    state = 6;
                }
                else
                {
                    state = 3;
                }
                return;
            case 6:
                if (customerGone)
                {
                    GiveUp();
                    return;
                }
                timer -= dtMs;
                if (timer >= 0)
                    return;
                Owner.ClearOverride(0);
                table!.Attach(cafe, new FoodItem(cafe.Data, dish, Owner.Character.TipRating));
                table.Busy = true;
                dish = null;
                cafe.Events.Sound(cafe.Events.RandomServeSound());
                state = 7;
                goto case 7;
            case 7:
                if (!Owner.AnimationFinished)
                    return;
                if (Owner.Character.Role == Role.Staff)
                {
                    int mult = 1;
                    if (!cafe.NoServeBonus && cafe.Events.DoubleXpEvent)
                    {
                        mult = 2;
                        cafe.AddNote(new StatusNote(cafe, Owner, StatusNote.Kind.Xp, "+ XP", cafe.Data.K.NoteTime, new Color(0x19, 0xd5, 0x8a, 0xff), iconMode: false));
                    }
                    Owner.Character.Xp += cafe.Data.K.ServeXp * mult;
                }
                Finish(true);
                return;
            case 8:
                if (follow == null)
                {
                    counter = dish == null ? null : cafe.BestCounterFor(dish.Food);
                    if (counter != null)
                        StartFollow(counter, CharAnim.Carry);
                    return;
                }
                follow.Update(dtMs);
                if (!follow.Done)
                    return;
                Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
                if (follow.Success)
                    state = 9;
                SetFollow(null);
                return;
            case 9:
                Owner.SetAnimation(CharAnim.Serve, Owner.Facing);
                Owner.FaceTowards(counter!.X, counter.Y);
                timer = cafe.Data.K.HandOverTime;
                state = 10;
                return;
            case 10:
                timer -= dtMs;
                if (timer >= 0)
                    return;
                Owner.ClearOverride(0);
                cafe.Events.Sound(cafe.Events.RandomServeSound());
                counter!.AddDish(cafe, dish!);
                dish = null;
                Finish(false);
                return;
            default:
                Finish(false);
                return;
        }
    }
}

/// <summary>
/// Clear a dirty plate off a table (or the burnt pot off a stove) and take it to the nearest sink
/// (CleanupTask, 0x44 bytes).
/// </summary>
public sealed class CleanupTask : CafeTask
{
    readonly Cafe cafe;           // +0x24
    Furniture? target;            // +0x28
    Furniture? sink;              // +0x2c
    FollowTask? follow;           // +0x30
    bool stopping;                // +0x34
    int timer;                    // +0x3c
    byte state;                   // +0x20

    public CleanupTask(CharacterInstance owner, Cafe cafe, Furniture target) : base(owner)
    {
        this.cafe = cafe;
        this.target = target;
        cafe.DirtyTables.Remove(target);
    }

    void SetFollow(FollowTask? f)
    {
        follow?.Release();
        follow = f;
    }

    public override void Start()
    {
        base.Start();
        stopping = false;
        state = 0;
        var f = new FollowTask(Owner, target!, cafe);
        SetFollow(f);
        f.Start();
        if (!f.PlanNow() && !f.Success)
        {
            SetFollow(null);
            Finish(false);
        }
    }

    public override void StopSoon()
    {
        if (follow == null || state != 0)
            return;
        follow.StopSoon();
        stopping = true;
    }

    public override void Cancel()
    {
        follow?.Cancel();
        Owner.ClearOverride(0);
        Finish(state is >= 3 and <= 6);
    }

    public override void Release() => SetFollow(null);

    /// <summary>A stove whose dish turned out fine after all needs no cleaning.</summary>
    bool StoveTakenOver() => target is Stove s && s.IsDone && !s.IsBurnt(cafe);

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        if (Owner.Character.Role == Role.Staff)
            Owner.Character.Drain(cafe.Data, dtMs);
        switch (state)
        {
            case 0:
                if (target == null || !target.Busy)
                    break;
                if (StoveTakenOver())
                    StopSoon();
                follow!.Update(dtMs);
                if (!follow.Done)
                    return;
                if (!follow.Success || stopping)
                    Finish(false);
                else
                    state = 1;
                SetFollow(null);
                return;
            case 1:
                if (target == null || !target.Busy)
                    break;
                if (StoveTakenOver())
                    StopSoon();
                Owner.SetAnimation(CharAnim.Serve, Owner.Facing);
                Owner.FaceTowards(target.X, target.Y);
                timer = cafe.Data.K.HandOverTime;
                state = 2;
                return;
            case 2:
                if (target == null || !target.Busy)
                    break;
                if (StoveTakenOver())
                {
                    StopSoon();
                    return;
                }
                timer -= dtMs;
                if (timer >= 0)
                    return;
                cafe.Events.Sound(cafe.Events.RandomServeSound());
                if (target is Stove stove)
                {
                    stove.CleanBurnt(cafe);
                }
                else
                {
                    target.Detach();
                    target.Busy = false;
                }
                state = 3;
                target = null;
                return;
            case 3:
                sink = null;
                foreach (var s in cafe.FindFurniture(FurnitureCategory.Sink, Owner.Cell))
                {
                    var f = new FollowTask(Owner, s, cafe, CharAnim.Carry);
                    SetFollow(f);
                    f.Start();
                    if (f.PlanNow() || f.Success)
                    {
                        sink = s;
                        var plate = cafe.Data.Sheet(cafe.Data.Furniture[cafe.Data.K.DirtyPlateDef]);
                        Owner.ClearOverride(0);
                        if (plate.Length > 0)
                            Owner.SetOverride(0, plate[0], 0, 0, 1f);
                        break;
                    }
                    SetFollow(null);
                    cafe.Events.Hint(0x43, false);
                }
                if (follow != null)
                    state = 4;
                return;
            case 4:
                follow!.Update(dtMs);
                if (!follow.Done)
                    return;
                Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
                state = follow.Success ? (byte)5 : (byte)3;
                SetFollow(null);
                return;
            case 5:
                Owner.SetAnimation(CharAnim.Serve, Owner.Facing);
                Owner.FaceTowards(sink!.X, sink.Y);
                timer = cafe.Data.K.HandOverTime;
                state = 6;
                return;
            case 6:
                timer -= dtMs;
                if (timer >= 0)
                    return;
                cafe.Events.Sound(cafe.Events.RandomServeSound());
                Owner.ClearOverride(0);
                Finish(true);
                return;
        }
        Finish(false);
    }
}

/// <summary>
/// A tired zombie goes out to a free spot in the yard behind the cafe and naps there, getting its energy
/// back, until it is sent back to work (RestTask, 0x44 bytes).
/// </summary>
public sealed class RestTask : CafeTask
{
    readonly Cafe cafe;       // +0x24
    MoveTask? move;           // +0x20
    int noteTimer;            // +0x28
    byte state;               // +0x2c
    int spot = -1;            // +0x30/+0x34 (as a cell)
    bool stopping;            // +0x38

    public RestTask(CharacterInstance owner, Cafe cafe) : base(owner) => this.cafe = cafe;

    public override void Start()
    {
        base.Start();
        state = 0;
        Owner.Character.Job = Job.Resting;
    }

    public override void StopSoon()
    {
        move?.StopSoon();
        stopping = true;
    }

    public override void Cancel()
    {
        move?.Cancel();
        Complete();
    }

    public override void Release()
    {
        move?.Release();
        Unreserve();
    }

    void Unreserve()
    {
        if (spot >= 0)
            cafe.RestSpots.Remove(spot);
        spot = -1;
    }

    public override void Update(int dtMs)
    {
        if (move == null && stopping)
            Finish(false);
        if (Done)
            return;
        switch (state)
        {
            case 0:
            {
                // a free spot in the yard below the room (cafe +0xcc keeps the taken ones)
                var k = cafe.Data.K;
                int cell;
                int tries = 0;
                do
                {
                    int u = k.PlayerOriginU - 1 - (cafe.RoomWidth == 0 ? 0 : cafe.Rng.Next(cafe.RoomWidth));
                    int v = k.PlayerOriginV + cafe.RoomDepth + 1 + cafe.Rng.Next(5);
                    cell = cafe.IndexOf(u, v);
                }
                while (cafe.RestSpots.Contains(cell) && ++tries < 50);
                spot = cell;
                cafe.RestSpots.Add(cell);
                var m = new MoveTask(Owner, cafe, cell);
                m.Start();
                if (m.PlanNow() || m.Success)
                {
                    move = m;
                    state = 1;
                }
                else
                {
                    m.Release();
                    Unreserve();
                }
                return;
            }
            case 1:
                move!.Update(dtMs);
                if (!move.Done)
                    return;
                if (stopping)
                    Finish(false);
                if (move.Success)
                {
                    Owner.Character.Facing = 0;
                    Owner.SetAnimation(CharAnim.Idle, Facing.SE);
                    cafe.AddNote(new StatusNote(cafe, Owner, StatusNote.Kind.K7, "", cafe.Data.K.NoteTime));
                    noteTimer = cafe.Data.K.Int(0x42d53c);
                    state = 2;
                    cafe.Events.Sound(Owner.Def.IsFemale ? 199 : 198);
                }
                else
                {
                    state = 0;
                }
                move.Release();
                move = null;
                return;
            case 2:
                if (cafe.IsInside(Owner.Cell))
                {
                    // somebody moved it indoors: go back out
                    Unreserve();
                    state = 0;
                }
                noteTimer -= dtMs;
                if (noteTimer < 1)
                {
                    noteTimer = cafe.Data.K.Int(0x42d53c);
                    cafe.AddNote(new StatusNote(cafe, Owner, StatusNote.Kind.K7, "", cafe.Data.K.NoteTime));
                }
                // RestTask::vf05 -> FUN_000342c8: energy comes back at the chef's zombie-regen rate, one more
                // for every toxin barrel in the cafe (cafe +0xc2), to the full amount
                var c = Owner.Character;
                int restMs = (int)(dtMs * (cafe.ToxinBarrels + cafe.Events.Chef.F48 + cafe.Events.RestBonus));
                c.Energy = Math.Min(MathF.Floor(c.MaxEnergy(cafe.Data)), c.Energy + restMs * cafe.Data.K.EnergyDrainPerMs);
                return;
        }
    }
}

/// <summary>
/// The chef runs up to a customer and bites: the customer turns into a zombie (InfectTask, 0x2c bytes).
/// Fails if the customer leaves the room first or is already a zombie.
/// </summary>
public sealed class InfectTask : CafeTask
{
    readonly Cafe cafe;              // +0x28
    readonly CharacterInstance victim; // +0x20
    FollowTask? follow;              // +0x24

    public CharacterInstance Victim => victim;

    public InfectTask(CharacterInstance owner, CharacterInstance victim, Cafe cafe) : base(owner)
    {
        this.cafe = cafe;
        this.victim = victim;
    }

    public override void Start()
    {
        base.Start();
        follow = new FollowTask(Owner, victim, cafe, CharAnim.Walk2, run: true);
        follow.Start();
    }

    public override void StopSoon() => follow?.StopSoon();

    public override void Release() => follow?.Release();

    public override bool IsMoving => true;

    public override void Update(int dtMs)
    {
        if (Done || follow == null)
            return;
        if (victim.Character.IsZombie)
            Finish(false);
        follow.Update(dtMs);
        if (!follow.Done)
        {
            if (!cafe.IsInside(victim.Cell))
                Finish(false);
            return;
        }
        if (!follow.Success)
        {
            Finish(false);
            return;
        }
        // "Brains!" in the infector's voice
        int[] voice = Owner.Def.IsFemale ? [4, 6, 6] : [2, 3, 5];
        cafe.Events.Sound(voice[cafe.Rng.Next(3)]);
        var c = victim.Character;
        c.IsZombie = true;
        c.Role = Role.Staff;
        c.Energy = Math.Clamp(cafe.Data.K.Int(0x42d3d8), 0, c.MaxEnergy(cafe.Data));
        victim.RefreshParts();
        c.WalkAnim = (byte)(cafe.Rng.Next(2) == 1 ? 18 : 0);
        Finish(true);
    }
}

/// <summary>
/// A zombie brought down for good (in a raid, or found that way when the cafe loads) lies in the yard behind
/// the cafe until it reanimates (ReanimateTask, 0x30 bytes, vf02 0009e6ac / vf05 0009e6f8): the fall's last frame,
/// its spot kept, until <see cref="Character.ReanimateMs"/> runs out (or toxin revives it).
/// </summary>
public sealed class ReanimateTask : CafeTask
{
    readonly Cafe cafe;   // +0x24
    int spot;             // +0x2c

    public ReanimateTask(CharacterInstance owner, Cafe cafe) : base(owner)
    {
        this.cafe = cafe;
        // FUN_0009e388: a zombie (type 3) keeps its spot in the yard; a pet (type 4) lies where it is
        spot = owner.IsPet ? -1 : owner.Cell;
        if (spot >= 0)
            cafe.RestSpots.Add(spot);
    }

    public override void Start()
    {
        base.Start();
        Owner.LieDead();
    }

    public override void Release() => cafe.RestSpots.Remove(spot);

    /// <summary>vf08: the time left, as the job line shows it (" 7h").</summary>
    public override string Name => Durations.Short(Owner.Character.ReanimateMs / 1000);

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        if (spot >= 0 && Owner.Cell != spot)
        {
            cafe.RestSpots.Remove(spot);
            spot = Owner.Cell;
            cafe.RestSpots.Add(spot);
        }
        if (Owner.Character.ReanimateMs != 0)
            return;
        // up again: the next "hungry?" check a minute away (K 0x42d114)
        Owner.AttackCheck = cafe.Data.K.Int(0x42d114);
        Owner.SetAnimation(CharAnim.Idle, Owner.Facing, true);
        Finish(true);
    }
}
