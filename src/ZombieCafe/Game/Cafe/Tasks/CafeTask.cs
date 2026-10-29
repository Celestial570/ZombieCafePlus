using ZombieCafe.Data;

namespace ZombieCafe.Game.Cafe.Tasks;

/// <summary>
/// Something a character does over time (original Task, vtable 0x42a630). The owner runs the head of its
/// queue every frame; when <see cref="Done"/> is set the task is dropped and the next one starts.
/// Listeners hear about the start and the end (FUN_000131b0 fires the "finished" ones exactly once).
/// </summary>
public abstract class CafeTask
{
    /// <summary>+4.</summary>
    public bool Done { get; protected set; }
    /// <summary>+5: the task achieved what it set out to do.</summary>
    public bool Success { get; protected set; }
    /// <summary>+8.</summary>
    public CharacterInstance Owner { get; }

    public event Action<CafeTask>? Started;
    public event Action<CafeTask>? Finished;

    protected CafeTask(CharacterInstance owner) => Owner = owner;

    /// <summary>vf02.</summary>
    public virtual void Start() => Started?.Invoke(this);

    /// <summary>vf03: wrap up as soon as it is safe (default: stop now, quietly).</summary>
    public virtual void StopSoon() => Done = true;

    /// <summary>vf04: abandon immediately.</summary>
    public virtual void Cancel() => Done = true;

    /// <summary>vf05.</summary>
    public abstract void Update(int dtMs);

    /// <summary>vf07: the owner is walking (so it is not really standing on its cell).</summary>
    public virtual bool IsMoving => false;

    /// <summary>vf08: shown as the job of characters doing something unusual.</summary>
    public virtual string Name => "";

    /// <summary>The destructor: called once when the task leaves its owner's queue.</summary>
    public virtual void Release()
    {
    }

    /// <summary>FUN_000131b0.</summary>
    protected void Complete()
    {
        if (Done)
            return;
        Done = true;
        Finished?.Invoke(this);
    }

    protected void Finish(bool success)
    {
        Success = success;
        Complete();
    }
}

/// <summary>Stand still for a while, or play one of the idle fidgets (WaitTask, 0x28 bytes).</summary>
public sealed class WaitTask : CafeTask
{
    int remaining;          // +0x20
    bool fidget;            // +0x24

    /// <summary>ms left (for a plain wait).</summary>
    public int Remaining => remaining;

    public WaitTask(CharacterInstance owner, int ms, bool fidget = false) : base(owner)
    {
        remaining = ms;
        this.fidget = fidget;
    }

    public override void Start()
    {
        base.Start();
        if (Owner.IsPet)
        {
            // FUN_0009cb18: a pet sits down or stands about (half and half) and simply waits its time out
            Owner.SetPetAnimation(Owner.Cafe.Rng.Next(2) == 0 ? PetAnim.Sit : PetAnim.Idle, Owner.Facing);
            fidget = false;
            return;
        }
        if (!fidget)
        {
            Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
            return;
        }
        // staff zombies have three fidgets (14..16), everybody else two (15, 16)
        var rng = Owner.Cafe.Rng;
        int anim = Owner.Character.Role == Role.Staff ? rng.Next(3) + 14 : rng.Next(2) + 15;
        Owner.SetAnimation((CharAnim)anim, Owner.Facing);
    }

    public override void StopSoon()
    {
        Success = false;
        Complete();
    }

    public override void Update(int dtMs)
    {
        if (!fidget)
        {
            if (Done)
                return;
            remaining -= dtMs;
            if (remaining > 0)
                return;
        }
        else if (!Owner.AnimationFinished)
        {
            return;
        }
        Finish(true);
    }
}

/// <summary>
/// Walk (or run) along an A* path to a cell (MoveTask, 0x44 bytes). Faces the way it is going, keeps
/// count of characters in the doorway, and can be told to re-plan from the next node.
/// </summary>
public class MoveTask : CafeTask
{
    protected readonly Cafe cafe;   // +0x3c
    protected int startCell;        // +0x20
    protected int targetCell;       // +0x24
    protected int trimEnd;          // +0x28
    PathTween? tween;               // +0x2c
    bool repath = true;             // +0x30
    CharAnim anim;                  // +0x31
    float speedFactor = 1f;         // +0x34
    protected bool stopped;         // +0x38
    readonly bool run;              // +0x39
    bool inDoorway;                 // +0x40

    /// <param name="anim">Walk animation; null picks the character's own walk.</param>
    public MoveTask(CharacterInstance owner, Cafe cafe, int target, CharAnim? anim = null, bool run = false) : base(owner)
    {
        this.cafe = cafe;
        targetCell = target;
        this.run = run;
        if (anim == null)
        {
            var c = owner.Character;
            if (c.WalkAnim != 0 && c.WalkAnim != 18)
                c.WalkAnim = (byte)(cafe.Rng.Next(2) == 0 ? 0 : 18);
            SetWalkAnimation((CharAnim)c.WalkAnim);
        }
        else
        {
            SetWalkAnimation(anim.Value);
        }
    }

    /// <summary>vf14: running (or a run task) covers ground in 60% of the time.</summary>
    void SetWalkAnimation(CharAnim a)
    {
        anim = a;
        speedFactor = a == CharAnim.Run || run ? 0.6f : 1f;
    }

    public int TargetCell => targetCell;

    public override void Start()
    {
        base.Start();
        startCell = Owner.Cell;
    }

    /// <summary>vf03: finish at the next node; the task then counts as failed.</summary>
    public override void StopSoon()
    {
        stopped = true;
        repath = false;
        tween?.StopSoon();
    }

    /// <summary>vf06: re-plan once the next node is reached (the target moved).</summary>
    protected void Repath()
    {
        repath = true;
        tween?.StopSoon();
    }

    public override bool IsMoving => true;

    /// <summary>vf13: plan a path from the current cell; failing ends the task.</summary>
    protected virtual bool FindPath()
    {
        tween = null;
        var nodes = cafe.Nav.FindPath(startCell, targetCell);
        if (nodes != null)
            tween = new PathTween(cafe.Nav, nodes, Owner.MoveTimePerUnit * speedFactor, trimEnd);
        if (tween == null)
            Finish(false);
        repath = false;
        return tween != null;
    }

    /// <summary>The path planned on start (FollowTask callers check this right after starting).</summary>
    public bool PlanNow() => FindPath();

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        if (tween == null)
        {
            if (!FindPath())
                return;
        }
        else
        {
            if (!tween.IsFinished)
                tween.Update(dtMs);
            float ox = Owner.X, oy = Owner.Y;
            var p = tween.Position;
            Owner.SetPosition(cafe, p.X, p.Y);
            startCell = tween.CurrentCell;
            Owner.Cell = startCell;
            Owner.NextCell = tween.NextCell;
            // characters crossing the room boundary hold the door open (cafe +0xe0)
            bool a = cafe.IsInside(Owner.Cell), b = cafe.IsInside(Owner.NextCell);
            if (a != b && !inDoorway)
            {
                inDoorway = true;
                cafe.DoorTraffic++;
            }
            else if (inDoorway && a == b)
            {
                inDoorway = false;
                cafe.DoorTraffic--;
            }
            // MoveTask::vf05: up the screen (or level) faces NE / NW by the step's x (none when it is still), down it
            // faces SE to the right, else SW
            Facing? f;
            if (p.Y <= oy)
                f = p.X > ox ? Data.Facing.NE : p.X < ox ? Data.Facing.NW : null;
            else
                f = p.X > ox ? Data.Facing.SE : Data.Facing.SW;
            if (f != null)
                Owner.SetAnimation(anim, f.Value);
            if (run)
                Owner.SetAnimationRate(Owner.Character.SpeedFactor(Owner.Data) * 2f);
            if (repath && tween.IsFinished)
            {
                if (!FindPath())
                    return;
            }
        }
        if (tween == null || !tween.IsFinished)
            return;
        Finish(!stopped);
    }
}

/// <summary>
/// Walk up to another object and stop next to it (FollowTask, 0x4c bytes): the target's cell is made
/// passable just for planning and the last step is trimmed off; re-plans if the target moves.
/// </summary>
public sealed class FollowTask : MoveTask
{
    CafeObject? target; // +0x44

    public FollowTask(CharacterInstance owner, CafeObject target, Cafe cafe, CharAnim? anim = null, bool run = false)
        : base(owner, cafe, CellOf(target), anim, run)
    {
        this.target = target;
        trimEnd = 1;
        target.Removed += OnTargetRemoved;
    }

    static int CellOf(CafeObject o) => o switch
    {
        CharacterInstance c => c.Cell,
        Furniture f => f.Cell,
        _ => -1,
    };

    void OnTargetRemoved(CafeObject o) => target = null;

    public CafeObject? Target => target;

    public override void Release()
    {
        if (target != null)
            target.Removed -= OnTargetRemoved;
    }

    public override void Start()
    {
        base.Start();
        Owner.Interacting = target;
    }

    public override void Update(int dtMs)
    {
        if (target == null)
        {
            StopSoon();
            return;
        }
        if (Done)
            return;
        if (Owner.Interacting == target)
        {
            if (!stopped && targetCell != CellOf(target))
                Repath();
        }
        else
        {
            StopSoon();
        }
        base.Update(dtMs);
    }

    protected override bool FindPath()
    {
        if (target == null)
        {
            Finish(false);
            return false;
        }
        targetCell = target is CharacterInstance c ? c.NextCell : CellOf(target);
        bool arrived = Owner.Cell == targetCell;
        if (!arrived && cafe.AreNeighbours(Owner.Cell, targetCell)
            && cafe.IsInside(Owner.Cell) == cafe.IsInside(targetCell))
            arrived = true;
        if (arrived)
        {
            Owner.Interacting = null;
            Finish(true);
            return false;
        }
        if (cafe.Nav.IsPassable(targetCell))
            return base.FindPath();
        cafe.Nav.SetPassable(targetCell, true);
        bool ok = base.FindPath();
        cafe.Nav.SetPassable(targetCell, false);
        return ok;
    }
}
