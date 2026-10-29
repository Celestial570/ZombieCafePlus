using ZombieCafe.Data;

namespace ZombieCafe.Game.Cafe.Tasks;

/// <summary>
/// A boss chef holds its ground (GuardTask, 0x3c bytes, vf05 00036e44): it stays on its own cell and strikes
/// whoever it is fighting once they stand next to it, every 1000 / speed ms for its strength, until they drop
/// or leave the room.
/// </summary>
public sealed class GuardTask : CafeTask
{
    readonly Cafe cafe;            // +0x34
    readonly int home;             // the cell it keeps to
    int strikeTimer;               // +0x2c
    bool striking;                 // +0x30 == 2

    /// <summary>+0x20.</summary>
    public CharacterInstance Target { get; }

    public GuardTask(CharacterInstance owner, CharacterInstance target, Cafe cafe) : base(owner)
    {
        this.cafe = cafe;
        Target = target;
        home = owner.Cell;
    }

    int StrikeInterval() => (int)(cafe.Data.K.Int(0x42cdc0) / Owner.Character.SpeedFactor(cafe.Data));

    public override void Start()
    {
        base.Start();
        Owner.Character.Job = Job.Attacking;
    }

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        cafe.PlaceOnCell(Owner, home);
        if (Target.IsKnockedOut)
        {
            Owner.Interacting = null;
            Finish(true);
            return;
        }
        if (Owner.Interacting != Target || !cafe.IsInside(Target.Cell))
        {
            Finish(false);
            return;
        }
        bool reach = cafe.AreNeighbours(Owner.Cell, Target.Cell) || Owner.Cell == Target.Cell;
        if (!reach)
        {
            striking = false;
            Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
            return;
        }
        if (!striking)
        {
            striking = true;
            strikeTimer = StrikeInterval();
            Owner.FaceTowards(Target.X, Target.Y);
            Owner.SetAnimation(CharAnim.Attack, Owner.Facing);
            return;
        }
        strikeTimer -= dtMs;
        if (strikeTimer >= 1)
            return;
        Owner.SetAnimation(CharAnim.Attack, Owner.Facing, true);
        Owner.FaceTowards(Target.X, Target.Y);
        Owner.Grunt();
        Target.TakeHit(Owner, Owner.Character.Strength);
        strikeTimer = StrikeInterval();
        if (!Target.IsKnockedOut)
            return;
        cafe.Events.Sound((Target.Def.IsFemale ? 94 : 92) + cafe.Rng.Next(2));
        Target.KnockDown();
        Owner.Interacting = null;
        Finish(true);
    }
}

/// <summary>
/// A raider helps itself to the counter (StealFoodTask, 0x48 bytes, vf02 00188224 / vf05 001883bc): it walks
/// up to the nearest counter with food on it, works at it for two seconds (K 0x42d560) and makes off with
/// the dish on top - the whole batch (FUN_001c20d0).
/// </summary>
public sealed class StealFoodTask : CafeTask
{
    readonly Cafe cafe;            // +0x28
    ServingCounter? counter;       // +0x2c
    FollowTask? follow;            // +0x40
    int state;                     // +0x34
    int clock;                     // +0x30

    /// <summary>+0x24: what it took.</summary>
    public Dish? Stolen { get; private set; }

    public StealFoodTask(CharacterInstance owner, Cafe cafe) : base(owner) => this.cafe = cafe;

    public override void Start()
    {
        base.Start();
        if (cafe.TotalServings() > 0)
        {
            foreach (var f in cafe.FindFurniture(FurnitureCategory.Counter, Owner.Cell))
            {
                if (f is not ServingCounter c || c.TotalServings == 0)
                    continue;
                var go = new FollowTask(Owner, c, cafe);
                go.Start();
                if (go.PlanNow() || go.Success)
                {
                    counter = c;
                    follow = go;
                    break;
                }
                go.Release();
            }
        }
        if (follow == null)
            Finish(false);
    }

    public override void Release() => follow?.Release();

    public override bool IsMoving => follow?.IsMoving ?? false;

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        if (state == 0)
        {
            follow!.Update(dtMs);
            if (!follow.Done)
                return;
            bool reached = follow.Success;
            follow.Release();
            follow = null;
            if (!reached)
            {
                Finish(false);
                return;
            }
            Owner.SetAnimation(CharAnim.Operate, Owner.Facing);
            Owner.FaceTowards(counter!.X, counter.Y);
            state = 1;
            return;
        }
        if (counter == null || counter.TotalServings <= 0)
        {
            Finish(false);
            return;
        }
        clock += dtMs;
        if (clock < cafe.Data.K.Int(0x42d560))
            return;
        Stolen = counter.TakeTopDish(cafe);
        Finish(Stolen != null);
    }
}
