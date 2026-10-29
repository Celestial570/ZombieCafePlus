using ZombieCafe.Data;

namespace ZombieCafe.Game.Cafe.Tasks;

/// <summary>
/// A bored zombie stops and daydreams (DaydreamTask, a WaitTask of 0x38 bytes): ten seconds staring into
/// space, or until the player taps it awake. Afterwards it goes back to what it was doing (FUN_000d9620).
/// </summary>
public sealed class DaydreamTask : CafeTask
{
    readonly Cafe cafe;            // +0x30
    int remaining;                 // +0x20
    bool woken;                    // +0x2c
    /// <summary>+0x26: the job it had (a cook goes back to its stove).</summary>
    public Job PreviousJob { get; }
    /// <summary>+0x25.</summary>
    public CharAnim PreviousAnim { get; }
    /// <summary>+0x28: the cell it was facing (its stove, for a cook).</summary>
    public int FacingCell { get; }

    public DaydreamTask(CharacterInstance owner, Cafe cafe, int ms, Job job, CharAnim anim) : base(owner)
    {
        this.cafe = cafe;
        remaining = ms;
        PreviousJob = job;
        PreviousAnim = anim;
        FacingCell = cafe.Neighbour(owner.Cell, (int)owner.Facing);
    }

    public override void Start()
    {
        base.Start();
        cafe.Events.Sound(Owner.Def.IsFemale ? 199 : 198);
        Owner.Character.Job = Job.Daydreaming;
        Owner.SetAnimation(CharAnim.Bump2, Owner.Facing);
        cafe.AddNote(new StatusNote(cafe, Owner, StatusNote.Kind.K7, "", cafe.Data.K.Int(0x42cf14)));
        // "zombies like this one are prone to daydreaming" (only once the raid tutorial is through)
        if (cafe.Events.HintShown(0x2e))
            cafe.Events.Hint(0x16, false);
    }

    /// <summary>vf03: tapped awake.</summary>
    public override void StopSoon() => woken = true;

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        if (!woken)
        {
            remaining -= dtMs;
            if (remaining > 0)
                return;
            Finish(true);
            cafe.Events.Hint(0x3d, false);
            return;
        }
        // shaken out of it: a start, then back to work
        Owner.SetAnimation(CharAnim.Hit, Owner.Facing);
        if (Owner.AnimationFinished)
            Complete();
    }
}

/// <summary>
/// A zombie goes for someone (AttackTask, 0x40 bytes; here the cafe's case of one character attacking
/// another): it closes in, then strikes every 1000 / speed ms for its strength until the victim drops.
/// Listeners hear every blow (the cafe's FUN_000d927c) and the end (FUN_000d9378).
/// </summary>
public sealed class AttackTask : CafeTask
{
    readonly Cafe cafe;            // +0x34
    FollowTask? follow;            // +0x28
    byte state;                    // +0x30
    int strikeTimer;               // +0x2c
    bool stopping;                 // +0x25
    /// <summary>+0x24: a blow has landed (the cafe only counts the first against its rating).</summary>
    public bool HitBefore { get; private set; }
    /// <summary>+0x20.</summary>
    public CharacterInstance Target { get; }
    /// <summary>FUN_001e8d7c: next to its target and fighting (state 2), not still on its way.</summary>
    public bool Engaged => state == 2;
    // the frame a blow was last counted on, and how long it has shown
    int blowFrame = -1, blowMs;

    /// <summary>Each blow that lands (the original's type-2 listeners).</summary>
    public event Action<AttackTask>? Struck;

    public AttackTask(CharacterInstance owner, CharacterInstance target, Cafe cafe) : base(owner)
    {
        this.cafe = cafe;
        Target = target;
        strikeTimer = StrikeInterval();
    }

    int StrikeInterval() => (int)(cafe.Data.K.Int(0x42cdc0) / Owner.Character.SpeedFactor(cafe.Data));

    /// <summary>
    /// How many blows land this update on the frame showing. The yeti and the Amptron strike on set frames of their
    /// animations at every update that shows one, and the original updated 60 times a second (EAGLView's 1/60 s
    /// timer): a blow on reaching the frame, and another for each 1/60 s it stays, at any frame rate.
    /// </summary>
    int BlowsThisUpdate(int dtMs)
    {
        int frame = Owner.AnimFrame;
        if (frame != blowFrame)
        {
            blowFrame = frame;
            blowMs = 0;
            return 1;
        }
        int before = blowMs * 60 / 1000;
        blowMs += dtMs;
        return blowMs * 60 / 1000 - before;
    }

    /// <summary>
    /// AttackTask::vf05 (type 5): the yeti, in its yeti form, hits on frames 18, 24, 29 and 36 of its attack (a punch,
    /// sound 213, for its strength) and on frame 58 (a smash, 214, for double).
    /// </summary>
    void YetiBlows(int dtMs)
    {
        if (!Owner.YetiForm)
        {
            Owner.SetYetiForm(true);
            return;
        }
        int blows = BlowsThisUpdate(dtMs);
        int frame = Owner.AnimFrame;
        int times = frame is 18 or 24 or 29 or 36 ? 1 : frame == 58 ? 2 : 0;
        if (times == 0)
            return;
        for (int i = 0; i < blows && !Target.IsKnockedOut; i++)
        {
            cafe.Events.Sound(times == 1 ? 213 : 214);
            Target.TakeHit(Owner, Owner.Character.Strength * times);
            Owner.FaceTowards(Target.X, Target.Y);
            Struck?.Invoke(this);
            HitBefore = true;
        }
    }

    /// <summary>
    /// AttackTask::vf05 (type 6): the Amptron punches on frames 5, 13 and 21 of its attack (FUN_0018aaf0: sound 213,
    /// its tip the first time) for its strength; its special lands on frame 31 (FUN_0018ab28: one of the three used)
    /// for the rival's whole energy - half of it for a restaurant's chef - and it goes back to punching.
    /// </summary>
    void AmptronBlows(int dtMs)
    {
        int blows = BlowsThisUpdate(dtMs);
        int frame = Owner.AnimFrame;
        bool special = Owner.AnimId == CharacterInstance.AmptronSpecial;
        if (special ? frame != 31 : frame is not (5 or 13 or 21))
            return;
        for (int i = 0; i < blows && !Target.IsKnockedOut; i++)
        {
            int damage = Owner.Character.Strength;
            if (special)
            {
                damage = (int)Target.Character.MaxEnergy(cafe.Data);
                if (Target.IsRivalChef)
                    damage = (int)(damage * 0.5);
                Owner.SpecialsLeft--;
            }
            else
            {
                cafe.Events.Sound(213);
                cafe.Events.Hint(0x5a, true);
            }
            Target.TakeHit(Owner, damage);
            Owner.SetSummonAnimation(CharacterInstance.AmptronAttack, Owner.Facing);
            Owner.FaceTowards(Target.X, Target.Y);
            Struck?.Invoke(this);
            HitBefore = true;
            if (special)
                break;
        }
    }

    /// <summary>
    /// AttackTask::vf05 (type 7): the Big Bad Wolf's bite lands on frame 3 of its attack (FUN_0002ca40: sound 224) for its
    /// strength.
    /// </summary>
    void WolfBlows(int dtMs)
    {
        int blows = BlowsThisUpdate(dtMs);
        if (Owner.AnimFrame != 3)
            return;
        for (int i = 0; i < blows && !Target.IsKnockedOut; i++)
        {
            cafe.Events.Sound(224);
            Target.TakeHit(Owner, Owner.Character.Strength);
            Owner.SetSummonAnimation(CharacterInstance.WolfAttack, Owner.Facing);
            Owner.FaceTowards(Target.X, Target.Y);
            Struck?.Invoke(this);
            HitBefore = true;
        }
    }

    /// <summary>
    /// FUN_000ed028 (the command bar's SPECIAL): with specials left and fighting next to its target, the Amptron starts
    /// its special attack (sound 221).
    /// </summary>
    public bool StartSpecial()
    {
        if (Owner.SpecialsLeft < 1 || !Engaged || Owner.AnimId == CharacterInstance.AmptronSpecial)
            return false;
        Owner.SetSummonAnimation(CharacterInstance.AmptronSpecial, Owner.Facing, true);
        cafe.Events.Sound(221);
        return true;
    }

    /// <summary>
    /// FUN_001e8ce8: the blow's animation - a pet bites with its second or third attack at random (a critical bite
    /// has its own); anyone else swings, or stoops (animation 28) at a pet.
    /// </summary>
    void Swing(bool crit, bool again)
    {
        if (Owner.IsSummon)
            Owner.SetSummonAnimation(Owner.IsWolf ? CharacterInstance.WolfAttack : CharacterInstance.AmptronAttack, Owner.Facing);
        else if (Owner.IsPet)
            Owner.SetPetAnimation(crit ? PetAnim.CriticalAttack : (cafe.Rng.Next(2) == 0 ? PetAnim.Attack3 : PetAnim.Attack2), Owner.Facing, again);
        else
            Owner.SetAnimation(Target.IsPet ? CharAnim.Kick : CharAnim.Attack, Owner.Facing, again);
    }

    public override void Start()
    {
        base.Start();
        state = 0;
        Owner.Character.Job = Job.Attacking;
    }

    public override void StopSoon()
    {
        follow?.StopSoon();
        stopping = true;
    }

    public override void Release() => follow?.Release();

    public override bool IsMoving => follow?.IsMoving ?? false;

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        if (stopping)
        {
            if (follow != null)
            {
                follow.Update(dtMs);
                if (!follow.Done)
                    return;
                follow.Release();
                follow = null;
            }
            Finish(false);
            return;
        }
        if (Target.IsKnockedOut)
        {
            Owner.Interacting = null;
            StopSoon();
        }
        if (Owner.Interacting != Target)
        {
            Finish(false);
            return;
        }
        if (!cafe.IsInside(Target.Cell))
        {
            StopSoon();
            return;
        }
        switch (state)
        {
            case 0:
            {
                // close in (the victim's own cell is let through for planning, FUN_001e8c10/FUN_001e8cb8)
                var f = new FollowTask(Owner, Target, cafe);
                f.Start();
                if (!f.PlanNow() && !f.Success)
                {
                    f.Release();
                    Owner.Interacting = null;
                    Finish(false);
                    return;
                }
                follow = f;
                Owner.Interacting = Target;
                state = 1;
                return;
            }
            case 1:
                follow!.Update(dtMs);
                if (!follow.Done)
                    return;
                bool reached = follow.Success;
                follow.Release();
                follow = null;
                if (reached)
                {
                    Owner.FaceTowards(Target.X, Target.Y);
                    Owner.Interacting = Target;
                    strikeTimer = StrikeInterval();
                    Swing(false, false);
                    state = 2;
                }
                else
                {
                    Owner.Interacting = Target;
                    Owner.SetAnimation(CharAnim.Idle, Owner.Facing);
                    state = 0;
                }
                return;
            case 2:
                if (Owner.IsYeti)
                {
                    YetiBlows(dtMs);
                }
                else if (Owner.IsAmptron)
                {
                    AmptronBlows(dtMs);
                }
                else if (Owner.IsWolf)
                {
                    WolfBlows(dtMs);
                }
                else if ((strikeTimer -= dtMs) < 1)
                {
                    // AttackTask::vf05: a strength tombstone's boost doubles the blow (K 0x42cdd0)
                    float damage = Owner.BoostMs > 0 && Owner.BoostType == 1
                        ? Owner.Character.Strength * cafe.Data.K.Int(0x42cdd0)
                        : Owner.Character.Strength;
                    // a pet lands a critical bite at its odds (petData +0x30), worth its multiplier (+0x34)
                    bool crit = false;
                    if (Owner.Character.Pet is { } pet && cafe.Rng.Next(101) * 0.01f <= pet.CritChance)
                    {
                        damage *= pet.CritMultiplier;
                        crit = true;
                    }
                    Owner.Grunt();
                    Target.TakeHit(Owner, (int)damage);
                    Swing(crit, true);
                    Owner.FaceTowards(Target.X, Target.Y);
                    strikeTimer = StrikeInterval();
                    Struck?.Invoke(this);
                    HitBefore = true;
                }
                if (!Target.IsKnockedOut)
                {
                    // the victim got away from it: chase again
                    if (!cafe.AreNeighbours(Owner.Cell, Target.Cell) && Owner.Cell != Target.Cell)
                        state = 0;
                    return;
                }
                // down: the victim's last cry
                if (Owner.IsZombie)
                    Owner.Character.Kills++;
                if (Target.IsPet)
                    cafe.Events.Sound(Target.PetDeathSound());
                else if (!Target.IsZombie)
                    cafe.Events.Sound(Target.Def.IsFemale ? 168 + cafe.Rng.Next(5) : 152 + cafe.Rng.Next(7));
                else
                    cafe.Events.Sound((Target.Def.IsFemale ? 94 : 92) + cafe.Rng.Next(2));
                Target.KnockDown();
                Owner.Interacting = null;
                Finish(true);
                return;
        }
    }
}

/// <summary>
/// The zombie eats who it brought down (FeastTask, 0x3c bytes): it crouches over the body behind a
/// "censored" sign, takes a bite every 500 / speed ms (each worth energy) until there is nothing left,
/// and gets up; what remains is a pile of bones (BonePile).
/// </summary>
public sealed class FeastTask : CafeTask
{
    readonly Cafe cafe;            // +0x30
    int biteTimer;                 // +0x24
    int crunchTimer;               // +0x28
    int state;                     // +0x34
    bool ateItAll;                 // +0x38
    CensoredSign? sign;            // +0x2c
    /// <summary>+0x20.</summary>
    public CharacterInstance Victim { get; }

    /// <summary>Each bite (the original's type-2 listeners).</summary>
    public event Action<FeastTask>? Bite;

    public FeastTask(CharacterInstance owner, CharacterInstance victim, Cafe cafe) : base(owner)
    {
        this.cafe = cafe;
        Victim = victim;
        biteTimer = BiteInterval();
        crunchTimer = cafe.Data.K.Int(0x42cfd0);
        // FUN_00012838: a pet sets straight in, under the sign
        if (owner.IsPet)
        {
            state = 1;
            sign = new CensoredSign(victim);
            cafe.AddOverlay(sign);
        }
    }

    int BiteInterval() => (int)(cafe.Data.K.Int(0x42cfd0) / Owner.Character.SpeedFactor(cafe.Data));

    /// <summary>vf03: the meal is over early (what was eaten so far counts).</summary>
    public override void StopSoon()
    {
        RemoveSign();
        Finish(ateItAll);
    }

    /// <summary>Dropped from the queue before it got going: the body is let go all the same.</summary>
    public override void Release()
    {
        RemoveSign();
        if (!Done)
            Finish(ateItAll);
    }

    void RemoveSign()
    {
        if (sign == null)
            return;
        sign.Gone = true;
        sign = null;
    }

    public override void Update(int dtMs)
    {
        if (Done)
            return;
        if (!Victim.IsKnockedOut)
        {
            RemoveSign();
            Finish(false);
            return;
        }
        if (state == 1)
        {
            if (Victim.Bites <= 0)
            {
                RemoveSign();
                ateItAll = false;
                state = 2;
            }
            biteTimer -= dtMs;
            crunchTimer -= dtMs;
            if (crunchTimer < 1)
            {
                crunchTimer = cafe.Data.K.Int(0x42cfd0);
                cafe.Events.Sound(Owner.IsPet ? Owner.PetEatSound() : 96);
            }
            if (biteTimer < 1 && state == 1)
            {
                Victim.Bites--;
                var c = Owner.Character;
                c.Energy = Math.Min(c.MaxEnergy(cafe.Data), c.Energy + cafe.Data.K.Int(0x42cfd4));
                biteTimer = BiteInterval();
                Bite?.Invoke(this);
                if (Victim.Bites <= 0)
                {
                    // nothing left but bones
                    cafe.Add(new BonePile(cafe, Victim.X, Victim.Y));
                    cafe.Remove(Victim);
                    RemoveSign();
                    ateItAll = true;
                    state = 2;
                }
            }
        }
        if (Owner.IsPet)
        {
            // FeastTask::vf05 (type 4): it bites away (attack 1) until there is nothing left
            if (state >= 2)
            {
                Owner.Interacting = null;
                Finish(ateItAll);
                return;
            }
            Owner.SetPetAnimation(PetAnim.Attack1, Owner.Facing);
            Owner.FaceTowards(Victim.X, Victim.Y);
            return;
        }
        if (Owner.AnimationFinished)
        {
            state++;
            if (state == 1)
            {
                sign = new CensoredSign(Victim);
                cafe.AddOverlay(sign);
            }
        }
        if (state > 2)
        {
            Owner.Interacting = null;
            Finish(ateItAll);
            return;
        }
        // 21: down to the body, 22: eating, 23: wiping its mouth
        var anim = state switch { 0 => CharAnim.Crouch, 1 => CharAnim.Feast, _ => CharAnim.MouthWipe2 };
        Owner.SetAnimation(anim, Owner.Facing);
        Owner.FaceTowards(Victim.X, Victim.Y);
    }
}
