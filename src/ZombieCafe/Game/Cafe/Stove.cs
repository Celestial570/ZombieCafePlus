using ZombieCafe.Data;

namespace ZombieCafe.Game.Cafe;

/// <summary>
/// A stove (original Stove : Furniture, 0xcc bytes). Cooking only advances while a cook works it
/// (<see cref="Work"/>, called by CookTask every frame); the burn clock runs regardless once the tutorial's
/// cooking step is done, so a neglected dish burns. The pot is the object on top.
/// </summary>
public sealed class Stove : Furniture
{
    /// <summary>+0xa8.</summary>
    public Dish? Dish { get; private set; }
    /// <summary>+0xac: ms since cooking started (held back while someone works the stove).</summary>
    public int BurnClock { get; private set; }
    /// <summary>+0xb0: ms of cooking done.</summary>
    public int Progress { get; private set; }
    /// <summary>+0xbc: who worked the stove in the last couple of frames.</summary>
    public CharacterInstance? Operator { get; private set; }
    byte operatorFrames = 1;   // +0xc0
    /// <summary>+0xc1: show the floating "Ready!" / "Burned" notes.</summary>
    public bool ShowNotes { get; set; } = true;
    /// <summary>+0xc2: cooks assigned to this stove (a stove with a cook coming does not ask for one).</summary>
    public int AssignedCooks { get; set; }
    /// <summary>+0xc4.</summary>
    public bool Burnt { get; private set; }
    StatusNote? note;          // +0xc8

    /// <summary>+0xb4: called when a dish finishes cooking.</summary>
    public event Action<Stove>? DishReady;

    public Stove(GameData data, FurnitureDef def) : base(data, def)
    {
    }

    /// <summary>FUN_00082614: put a pot on and start a dish (money has been taken by the caller).</summary>
    public void StartCooking(Cafe cafe, int food, byte modifier, FurnitureDef pot)
    {
        Dish = Dish.Cook(data, food, modifier);
        Busy = Attach(cafe, new Furniture(data, pot));
        BurnClock = 0;
        Progress = 0;
        Burnt = false;
    }

    /// <summary>Back from a save: the dish, how far along it is, and the right pot.</summary>
    public void Restore(Cafe cafe, Dish dish, int progress, int burnClock, bool burnt)
    {
        Dish = dish;
        Progress = progress;
        BurnClock = burnClock;
        Burnt = burnt;
        var pot = burnt ? data.BurntPotDef(Def.StoveFamily) : data.PotDef(Def.StoveFamily);
        Busy = Attach(cafe, new Furniture(data, pot));
        if (burnt && ShowNotes)
        {
            note = new StatusNote(cafe, this, StatusNote.Kind.Burnt, "", int.MaxValue);
            cafe.AddNote(note);
        }
        else if (IsDone && ShowNotes)
        {
            note = new StatusNote(cafe, this, StatusNote.Kind.Ready, "", int.MaxValue);
            cafe.AddNote(note);
        }
    }

    public int CookSeconds => Dish == null ? 0 : data.Foods[Dish.Food].CookSeconds(Dish.Modifier);

    /// <summary>FUN_00082f5c (cafe +0xc4 = 1 doubles it).</summary>
    public int BurnSeconds(Cafe cafe) => Dish == null ? 0 : (cafe.BurnMode == 1 ? 2 : 1) * data.Foods[Dish.Food].BurnSeconds(Dish.Modifier);

    /// <summary>FUN_00082f24.</summary>
    public bool IsDone => Dish != null && Busy && CookSeconds * 1000 <= Progress;

    /// <summary>FUN_00082f88: burnt once it has been left unattended for longer than the burn slack.</summary>
    public bool IsBurnt(Cafe cafe)
    {
        if (Dish == null)
            return false;
        if (Burnt)
            return true;
        if (cafe.BurnMode >= 2 || !Busy)
            return false;
        return (BurnSeconds(cafe) - CookSeconds) * 1000 < BurnClock - Progress;
    }

    /// <summary>FUN_000829a4: a cook works the stove for one frame.</summary>
    public void Work(Cafe cafe, CharacterInstance cook, int amountMs)
    {
        if (!Busy || IsDone)
            return;
        Alert = false;
        operatorFrames = 3;
        Operator = cook;
        Progress += amountMs;
        BurnClock = Math.Min(BurnClock, (BurnSeconds(cafe) - data.K.BurnMargin) * 1000);
        if (IsDone)
            OnDone(cafe);
    }

    /// <summary>FUN_000827c0: finish instantly (toxin).</summary>
    public void FinishNow(Cafe cafe)
    {
        BurnClock = 0;
        Progress = CookSeconds * 1000;
        OnDone(cafe);
    }

    void OnDone(Cafe cafe)
    {
        cafe.Events.Hint(10, true);
        ClearNote(cafe);
        cafe.Events.Sound(0xbf);
        if (ShowNotes)
        {
            note = new StatusNote(cafe, this, StatusNote.Kind.Ready, "", int.MaxValue);
            cafe.AddNote(note);
        }
        DishReady?.Invoke(this);
    }

    /// <summary>FUN_00082784.</summary>
    void ClearNote(Cafe cafe)
    {
        if (note == null)
            return;
        cafe.RemoveNote(note);
        note = null;
    }

    /// <summary>FUN_00082764: stop cooking and take the pot off (the caller gets it).</summary>
    public CafeObject? Clear(Cafe cafe)
    {
        Busy = false;
        ClearNote(cafe);
        return Detach();
    }

    /// <summary>The dish leaves the stove (DishToCounterTask): no dish, not cooking, pot removed.</summary>
    public Dish? TakeDish(Cafe cafe)
    {
        var d = Dish;
        Dish = null;
        Busy = false;
        Clear(cafe);
        return d;
    }

    /// <summary>CleanupTask on a burnt stove: the burnt pot goes, the stove is usable again.</summary>
    public void CleanBurnt(Cafe cafe)
    {
        Detach();
        Burnt = false;
        Busy = false;
        ClearNote(cafe);
    }

    /// <summary>FUN_00082bec.</summary>
    public override void Update(Cafe cafe, int dtMs)
    {
        operatorFrames--;
        if (operatorFrames < 2)
        {
            Operator = null;
            operatorFrames = 1;
        }
        Alert = ShowNotes && Busy && Dish != null && operatorFrames == 1 && AssignedCooks == 0;
        if (IsBurnt(cafe) || !cafe.Events.BurningEnabled)
            return;
        BurnClock += dtMs;
        // a HyperBooster on the stove keeps it from ever burning
        if (AttachedFurniture != null && AttachedFurniture.Def.Id == data.K.Int(0x42d740))
            return;
        if (!IsBurnt(cafe))
            return;
        ClearNote(cafe);
        Burnt = true;
        if (ShowNotes)
        {
            note = new StatusNote(cafe, this, StatusNote.Kind.Burnt, "", int.MaxValue);
            cafe.AddNote(note);
        }
        Detach();
        Attach(cafe, new Furniture(data, data.BurntPotDef(Def.StoveFamily)));
    }
}

/// <summary>
/// A serving counter (original ServingCounter : Furniture, 0xc4 bytes): a stack of dishes, the top one shown
/// as a picture with the total servings written above it.
/// </summary>
public sealed class ServingCounter : Furniture
{
    /// <summary>+0xa8.</summary>
    public int TotalServings { get; private set; }
    /// <summary>+0xb4: bottom to top.</summary>
    public List<Dish> Dishes { get; } = new();

    public ServingCounter(GameData data, FurnitureDef def) : base(data, def)
    {
    }

    /// <summary>FUN_001c1c2c: true when the servings joined a dish already here (the caller can drop its copy).</summary>
    public bool AddDish(Cafe cafe, Dish dish)
    {
        foreach (var d in Dishes)
        {
            if (d.Food == dish.Food && d.Modifier == dish.Modifier)
            {
                d.Servings += dish.Servings;
                TotalServings += dish.Servings;
                ShowTop(cafe);
                return true;
            }
        }
        Dishes.Add(dish);
        TotalServings += dish.Servings;
        ShowTop(cafe, dish);
        return false;
    }

    /// <summary>FUN_001c1e9c: one serving of the top dish (taken off the counter when <paramref name="take"/>).</summary>
    public Dish? TakeServing(Cafe cafe, bool take)
    {
        if (TotalServings == 0 || Dishes.Count == 0)
            return null;
        var top = Dishes[^1];
        var one = new Dish(top.Food, 1, top.Modifier);
        if (!take)
            return one;
        TotalServings--;
        int left = top.Servings;
        top.Servings = left - 1;
        if (top.Servings == 0 || left < 1)
        {
            Dishes.RemoveAt(Dishes.Count - 1);
            if (Attached is FoodItem shown && shown.Dish?.Food == one.Food)
            {
                if (TotalServings == 0)
                    Detach();
                else
                    ShowTop(cafe, Dishes[^1]);
            }
        }
        return one;
    }

    /// <summary>FUN_001c20d0(counter, 1): the whole dish on top, taken away (a raider stealing it).</summary>
    public Dish? TakeTopDish(Cafe cafe)
    {
        if (TotalServings == 0 || Dishes.Count == 0)
            return null;
        var top = Dishes[^1];
        Dishes.RemoveAt(Dishes.Count - 1);
        TotalServings -= top.Servings;
        if (TotalServings == 0)
            Detach();
        else
            ShowTop(cafe, Dishes[^1]);
        return top;
    }

    /// <summary>FUN_001c1ccc: the picture on the counter shows this dish.</summary>
    void ShowTop(Cafe cafe, Dish? dish = null)
    {
        dish ??= Dishes.Count > 0 ? Dishes[^1] : null;
        Detach();
        if (dish != null)
            Attach(cafe, new FoodItem(data, new Dish(dish.Food, 0, dish.Modifier), 0));
    }

    public override void Draw(Cafe cafe, GameData data)
    {
        base.Draw(cafe, data);
        if (TotalServings == 0 || Attached == null)
            return;
        // ServingCounter::draw: the count in A Love of Thunder, white with a 4px black outline, centred
        // 20px above the dish picture
        var text = TotalServings.ToString();
        float size = 16f;
        var (w, _) = Gfx.Text.Measure(text, size);
        Gfx.Text.Draw(text, Attached.X - w * 0.5f, Attached.Y - 20, size, Raylib_cs.Color.White, Raylib_cs.Color.Black, 4);
    }
}
