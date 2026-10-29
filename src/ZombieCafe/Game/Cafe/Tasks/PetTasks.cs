using ZombieCafe.Data;

namespace ZombieCafe.Game.Cafe.Tasks;

/// <summary>
/// TransformTask (0x24 bytes, vtable 0x3dd340): seen, the owner plays one of its own animations through - a robot pet
/// folding into the Amptron or out of it again (11 / 10), the Amptron assembling or coming apart (4 / 5), a fairy-tale
/// zombie turning into the Big Bad Wolf or back (29 / 30) while the wolf appears or goes (3 / 4).
/// </summary>
public sealed class TransformTask : CafeTask
{
    readonly Facing facing; // +0x20
    readonly int anim;      // +0x21

    public TransformTask(CharacterInstance owner, int anim, Facing facing) : base(owner)
    {
        this.anim = anim;
        this.facing = facing;
    }

    public override void Start()
    {
        base.Start();
        Owner.Visible = true;
        if (Owner.IsPet)
            Owner.SetPetAnimation(anim, facing, true);
        else if (Owner.IsSummon)
            Owner.SetSummonAnimation(anim, facing, true);
        else
            Owner.SetAnimation((CharAnim)anim, facing, true);
    }

    /// <summary>vf03: over at once.</summary>
    public override void StopSoon()
    {
        Success = false;
        Complete();
    }

    public override void Update(int dtMs)
    {
        if (Done || !Owner.AnimationFinished)
            return;
        Finish(true);
    }
}

/// <summary>AppearTask (0x24 bytes, vtable 0x3daea0): a pet steps out of its habitat - seen again, playing its coming out.</summary>
public sealed class AppearTask : CafeTask
{
    readonly Facing facing; // +0x20

    public AppearTask(CharacterInstance owner, Facing facing) : base(owner) => this.facing = facing;

    public override void Start()
    {
        base.Start();
        if (!Owner.IsPet)
            return;
        Owner.Visible = true;
        Owner.SetPetAnimation(PetAnim.Appear, facing, true);
    }

    /// <summary>vf03: over at once.</summary>
    public override void StopSoon()
    {
        Success = false;
        Complete();
    }

    public override void Update(int dtMs)
    {
        if (Done || !Owner.AnimationFinished)
            return;
        Finish(true);
    }
}

/// <summary>
/// DisappearTask (0x28 bytes, vtable 0x3db2f0): a pet in front of its habitat goes in - its going-in animation, then it
/// is out of sight on the habitat's own cell and the habitat shows it inside.
/// </summary>
public sealed class DisappearTask : CafeTask
{
    readonly Facing facing; // +0x20
    readonly Cafe cafe;     // +0x24

    public DisappearTask(CharacterInstance owner, Facing facing, Cafe cafe) : base(owner)
    {
        this.facing = facing;
        this.cafe = cafe;
    }

    public override void Start()
    {
        base.Start();
        if (Owner.IsPet)
            Owner.SetPetAnimation(PetAnim.Disappear, facing, true);
    }

    public override void StopSoon()
    {
        Success = false;
        Complete();
    }

    public override void Update(int dtMs)
    {
        if (Done || !Owner.AnimationFinished)
            return;
        if (Owner.IsPet && Owner.Home is { } home && cafe.Objects.Contains(home))
        {
            home.TakeIn();
            cafe.PlaceOnCell(Owner, home.Cell);
            Owner.Visible = false;
        }
        Finish(true);
    }
}
