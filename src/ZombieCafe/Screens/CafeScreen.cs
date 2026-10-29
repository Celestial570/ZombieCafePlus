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
/// The player's cafe (original GameStateCafe): the isometric world with a pannable, zoomable camera, the
/// running simulation, and the controls: pick a cook, tap a stove to cook / fetch / clean, tap the
/// doorway to open or close, and a recipe list for empty stoves.
/// </summary>
public sealed partial class CafeScreen : GameScreen
{
    readonly GameData data;
    readonly CafeController game;
    Cafe cafe => game.Cafe;
    Vector2 camera;     // world point shown at the window centre
    float zoom = 0.75f; // window pixels per world pixel
    /// <summary>How far out the wheel zooms (the town carries on past the map's edges, so further than the map).</summary>
    const float MinZoom = 0.1f;
    Vector2 lastMouse, pressMouse;
    bool dragging, pressed, rightPressed;
    // the mouse is over something a click would act on (the hand cursor)
    bool overClickable;

    public override bool OverClickable => overClickable;
    public override bool ScalesHud => true;
    float msCarry;
    // how many times as fast the cafe runs (the cheat menu's speed; ZC_SPEED for tests)
    static float speed => Cheats.Speed;
    readonly bool autoplay;

    readonly EditMode edit;

    // message boxes waiting to be shown (welcome back, level up), one at a time, and what to do once read
    readonly Queue<(string Title, List<string> Lines, Action? Done)> messages = new();
    // the picture a box shows on its pinned paper (the dish learned, the prize), by the box's lines
    readonly Dictionary<List<string>, SubImage> pictures = new();

    /// <summary>A box with a picture (a new recipe's dish, the fridge's gift, the Frequent Fryer's prize).</summary>
    void Notice(string title, List<string> lines, SubImage? picture)
    {
        if (picture != null)
            pictures[lines] = picture;
        messages.Enqueue((title, lines, null));
    }
    // a yes/no question (the original's ConfirmDialog): text, the yes button, what yes does
    (string Text, string Yes, Action OnYes)? question;

    // the recipe book an empty stove opens
    readonly RecipeBook recipeBook;
    readonly FridgeDialog fridge;
    // a cooking stove tapped with nobody picked (the original's CookCommandBar)
    Stove? stoveBar;
    // an idle special stove tapped (StoveCommandBar: RECIPES, UPGRADE)
    Stove? specialBar;
    Rectangle? recipesButtonRect, upgradeButtonRect, specialCloseRect;
    Rectangle? finishButtonRect, chefButtonRect;
    bool RecipesOpen => recipeBook.IsOpen;

    readonly Audio.AudioSystem? audio;

    /// <summary>Save every minute and when leaving (off for test runs).</summary>
    public bool Autosave { get; init; } = true;
    /// <summary>Also write the game here when leaving (tests of the save format).</summary>
    public string? SaveTo { get; init; }

    /// <summary>The HUD's logo: leave for the title screen (none: the logo is not shown).</summary>
    public Action? Quit { get; init; }
    /// <summary>The HUD's map button: leave for the neighbourhood map (none: the button is not shown).</summary>
    public Action? OpenMap { get; init; }
    /// <summary>GameStateZombiePedia (HUD button 15).</summary>
    public Action? OpenPedia { get; init; }
    /// <summary>GameStateMeatLocker (HUD button 14).</summary>
    public Action? OpenLocker { get; init; }
    /// <summary>The cheat menu's time skip: this many ms pass at once (the cafe is caught up as after time away).</summary>
    public Action<long>? SkipTime { get; init; }
    float sinceSave;

    public CafeScreen(GameData data, CafeController game, Audio.AudioSystem? audio = null)
    {
        this.data = data;
        this.game = game;
        this.audio = audio;
        edit = new EditMode(data, game);
        recipeBook = new RecipeBook(data, game, (text, yes, onYes) => question = (text, yes, onYes),
            text => messages.Enqueue(("", new List<string> { text }, null)));
        fridge = new FridgeDialog(data);
        SetupTutorial();
        Input.CellToPixels = (du, dv) =>
        {
            int cell = cafe.IndexOf(cafe.OriginU + du, cafe.OriginV + dv);
            return cell < 0 ? null : Vector2.Transform(cafe.CellCenter(cell), WorldToWindow);
        };
        Input.TargetToPixels = ScriptTarget;
        // ZC_STORE=tab: start with the store open (screenshots)
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_STORE"), out int storeTab))
            edit.OpenStore(storeTab);
        if (audio != null)
        {
            game.SoundRequested += audio.Play;
            // GameStateCafe::update: the groove cues (music 16..18) one after another
            audio.PlayPlaylist(16, 17, 18);
            game.LevelUp += _ => audio.PlayMusicStinger(14);
        }
        game.LevelUp += level =>
        {
            // FUN_000d1f20: the HUD shows what the new level brings (FUN_0011064c)
            RefreshHud();
            // LevelUpDialog: the level, what it brought, and where to look for more
            var lines = new List<string> { data.Strings[78], data.Strings[79] + level };
            if (game.LastLevelReward is { } reward)
                lines.Add("New recipe: " + game.RecipeName(reward.Food, reward.Variant));
            lines.Add(data.Strings[80]);
            messages.Enqueue((data.Strings[77], lines, () => LevelUpDismissed(level)));
        };
        game.Message += text => messages.Enqueue(("", new List<string> { text }, null));
        game.HudBadge += (button, on) => hudBadge[button] = on;
        game.HudButtonShown += button => hudVisible[button] = true;
        // RecipeUnlockedDialog: "NEW RECIPE DISCOVERED!", the recipe, "CHECK THE RECIPE BOOK FOR DETAILS"
        // (from the fridge, with the line on the unlocking milestones)
        game.RecipeDiscovered += (food, variant, note) =>
            Notice(data.Strings[90], note.Length > 0
                ? new List<string> { game.RecipeName(food, variant), data.Strings[450], note }
                : new List<string> { game.RecipeName(food, variant), data.Strings[450] }, data.FoodImage(food));
        // GameStateCafe::load (FUN_000b867c): the special stoves' recipes
        game.LearnStoveRecipes();
        // FUN_000d9bc4: back the day after, the Frequent Fryer's reward
        // (the prize on its paper: the dish, the money pile ui 108 or the vial ui 34)
        if (game.CheckDailyBonus(GameClock.Now) is { } bonus)
            Notice(data.Strings[365], DailyBonusLines(bonus), bonus.Food >= 0 ? data.FoodImage(bonus.Food)
                : data.IngameUi[bonus.Money > 0 ? UiSprites.MoneyPile : UiSprites.Vial]);
        if (game.Away is { } away)
        {
            long min = away.ElapsedMs / 60000;
            string time = min >= 60 ? $"{min / 60}h {min % 60}m" : $"{min}m";
            var lines = new List<string> { $"You were away for {time}.", $"{away.Served} customers were served: ${away.Income}" + (away.Tips > 0 ? $" + ${away.Tips} tips" : "") };
            if (away.Hungry > 0)
                lines.Add($"{away.Hungry} customers left hungry.");
            messages.Enqueue(("Welcome back!", lines, null));
            game.Away = null;
        }
        var r = cafe.RoomBounds();
        camera = new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2);
        // ZC_SELECT=n: start with staff member n selected (-1 = the chef), for screenshots of the command bar
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_SELECT"), out int sel))
            game.Selected = sel < 0 ? game.ChefActor : sel < game.Staff.Count ? game.Staff[sel] : null;
        autoplay = Environment.GetEnvironmentVariable("ZC_AUTOPLAY") == "1";
    }

    (int, int)? laidOutFor;

    public override void Layout()
    {
        // on entry from another screen: its script targets and the HUD's logo (Quit is set after construction)
        Input.TargetToPixels = ScriptTarget;
        RefreshHud();
        // back from another screen in a window of the same size: the camera stays where the player left it
        var size = (Screen.PixelWidth, Screen.PixelHeight);
        if (laidOutFor == size)
            return;
        laidOutFor = size;
        var r = cafe.RoomBounds();
        float fit = MathF.Min(Screen.PixelWidth / r.Width, Screen.PixelHeight / r.Height) * 0.9f;
        zoom = Math.Clamp(fit, MinZoom, 2f);
        // ZC_CAM="du,dv,zoom": look at a cell relative to the cafe origin (screenshots of details)
        var cam = Environment.GetEnvironmentVariable("ZC_CAM")?.Split(',');
        if (cam is { Length: 3 } && int.TryParse(cam[0], out int du) && int.TryParse(cam[1], out int dv)
            && float.TryParse(cam[2], System.Globalization.CultureInfo.InvariantCulture, out float z))
        {
            camera = cafe.CellCenter(cafe.IndexOf(cafe.OriginU + du, cafe.OriginV + dv)) - new Vector2(0, 60);
            zoom = z;
        }
    }

    Matrix3x2 WorldToWindow =>
        Matrix3x2.CreateTranslation(-camera) * Matrix3x2.CreateScale(zoom) *
        Matrix3x2.CreateTranslation(Screen.PixelWidth / 2f, Screen.PixelHeight / 2f);

    Vector2 MouseWorld()
    {
        Matrix3x2.Invert(WorldToWindow, out var inv);
        return Vector2.Transform(Input.MousePosition, inv);
    }

    public override void Update(float dt)
    {
        int frameMs = Math.Max(1, (int)MathF.Round(dt * 1000f * speed));
        // ZombieCafe::update: the hint queue, every frame; GameStateCafe::update: the welcome, every frame
        game.Hints.Update(frameMs, data);
        game.Hint(0, true);
        // GameStateCafe::update mode 8: two frames without the HUD, then the shutter (sound 197) and the picture;
        // the frame after, the chef picked again
        if (photo >= 0 && ++photo >= 3)
        {
            if (photo == 3)
            {
                game.Sound(197);
                App.Current.CapturePhoto(PhotoPath());
            }
            else
            {
                photo = -1;
                game.Selected = game.ChefActor;
            }
        }
        chefMessage.Update(frameMs);
        arrow.Update(frameMs);
        game.TutorialArrowUp = arrow.Visible;
        // ZC_AUTOPLAY: nobody is there to read the boxes
        if (autoplay)
        {
            if (chefMessage.Visible)
                chefMessage.Hide();
            while (messages.Count > 0)
                messages.Dequeue().Done?.Invoke();
            pictures.Clear();
            question = null;
        }
        // the cafe stands still while the chef talks, a box is up, or the recipes or the store are open
        bool paused = chefMessage.Visible || messages.Count > 0 || question != null || RecipesOpen || edit.StoreOpen || bio != null
            || competitor != null || fridge.Visible || questsOpen || reviewOpen || Cheats.FreezeCafe;
        if (!paused)
        {
            LevelTips();
            WatchRivals(frameMs);
            // the simulation runs in whole milliseconds like the original (60 Hz frames, longer ones split up)
            msCarry += dt * 1000f * speed;
            while (msCarry >= 1f)
            {
                int step = (int)MathF.Min(msCarry, 50f);
                game.Update(step);
                msCarry -= step;
                if (autoplay)
                    AutoPlay();
            }
        }
        overClickable = false;
        // the cheat menu comes and goes with 0 (either one; with Ctrl, 0 puts the HUD back to its usual size)
        bool ctrl = Input.IsKeyDown(KeyboardKey.LeftControl) || Input.IsKeyDown(KeyboardKey.RightControl);
        if (!ctrl && (Input.IsKeyPressed(KeyboardKey.Zero) || Input.IsKeyPressed(KeyboardKey.Kp0)))
            ToggleCheats();
        StaffKeys(dt);
        if (!chefMessage.Visible)
            HandleInput(dt);
        else
            pressed = false;
        FridgePicked();
        // GameStateCafe::update: the quests' button wears its badge while there is something to tick off or a review to get
        hudBadge[HudQuests] = game.Player.Objectives.Ready;
        sinceSave += dt;
        if (Autosave && (sinceSave >= 60f || Input.IsKeyPressed(KeyboardKey.F5)))
        {
            SaveGame.Save(game);
            sinceSave = 0;
        }
    }

    public override void Leave()
    {
        if (Autosave)
            SaveGame.Save(game);
        if (SaveTo != null)
            SaveGame.Save(game, SaveTo);
        if (Environment.GetEnvironmentVariable("ZC_DUMP") == "1")
            DumpState();
    }

    /// <summary>ZC_DUMP=1: everyone and every seat at exit (test runs).</summary>
    void DumpState()
    {
        string Cell(int cell) { var (u, v) = cafe.Diamond(cell); return $"({u - cafe.OriginU},{v - cafe.OriginV})"; }
        foreach (var o in cafe.Objects)
        {
            if (o is CharacterInstance c)
            {
                var ch = c.Character;
                Console.WriteLine($"char  {ch.Name,-12} {c.Def.Name,-14} {ch.Role,-8} zombie={ch.IsZombie} at {Cell(c.Cell)} anim={(CharAnim)c.AnimId} job={ch.Job} " +
                    $"energy={ch.Energy:0.#} tasks=[{string.Join(",", c.Tasks.Select(t => t.GetType().Name))}]");
            }
            else if (o is Furniture f && f.Def.Category is FurnitureCategory.Chair or FurnitureCategory.Table)
            {
                Console.WriteLine($"furn  {f.Def.Category,-6} def={f.Def.Id} at {Cell(f.Cell)} o={f.Orientation} visible={f.Visible} busy={f.Busy} attached={f.Attached?.GetType().Name}");
            }
        }
    }

    void HandleInput(float dt)
    {
        var mouse = Input.MousePosition;
        if (messages.Count > 0 || question != null || bio != null || competitor != null || fridge.Visible || questsOpen || reviewOpen
            || toxinShopOpen)
        {
            pressed = false;
            if (toxinShopOpen && Input.IsKeyPressed(KeyboardKey.Escape))
                toxinShopOpen = false;
            return;
        }
        if (Input.IsKeyPressed(KeyboardKey.Escape) && !edit.Active)
        {
            if (RecipesOpen)
                CloseRecipes();
            else
                Deselect();
        }
        if (RecipesOpen)
        {
            // the book handles its own clicks; a press it takes must not end as a click on the cafe
            pressed = false;
            return;
        }
        if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right))
        {
            pressed = Input.IsMouseButtonPressed(MouseButton.Left);
            rightPressed = Input.IsMouseButtonPressed(MouseButton.Right);
            dragging = false;
            lastMouse = pressMouse = mouse;
        }
        bool leftPans = !(edit.Active && edit.UsesLeftDrag);
        if ((leftPans && Input.IsMouseButtonDown(MouseButton.Left)) || Input.IsMouseButtonDown(MouseButton.Right))
        {
            if (Vector2.Distance(mouse, pressMouse) > 6)
                dragging = true;
            if (dragging)
                camera -= (mouse - lastMouse) / zoom;
            lastMouse = mouse;
        }
        bool click = Input.IsMouseButtonReleased(MouseButton.Left) && pressed && !dragging && !Widgets.MouseOverUi;
        // a right click (not a drag) lets go: of the piece in hand when decorating, else of whoever is picked
        if (Input.IsMouseButtonReleased(MouseButton.Right) && rightPressed && !dragging)
        {
            rightPressed = false;
            if (edit.Active)
                edit.CancelHeld();
            else
                Deselect();
        }
        if (edit.Active)
            edit.Update(MouseWorld(), click, dt);
        else if (click)
            Click(MouseWorld());
        overClickable = !Widgets.MouseOverUi && !dragging
            && (edit.Active ? edit.OverPiece(MouseWorld()) : ClickableAt(MouseWorld()));
        float wheel = Widgets.MouseOverUi ? 0 : Input.GetMouseWheelMove();
        if (wheel != 0)
        {
            // zoom about the cursor
            Matrix3x2.Invert(WorldToWindow, out var inv);
            var before = Vector2.Transform(mouse, inv);
            zoom = Math.Clamp(zoom * MathF.Pow(1.15f, wheel), MinZoom, 2.5f);
            Matrix3x2.Invert(WorldToWindow, out inv);
            var after = Vector2.Transform(mouse, inv);
            camera += before - after;
        }
        float pan = 600 * dt / zoom;
        if (Input.IsKeyDown(KeyboardKey.Left) || Input.IsKeyDown(KeyboardKey.A)) camera.X -= pan;
        if (Input.IsKeyDown(KeyboardKey.Right) || Input.IsKeyDown(KeyboardKey.D)) camera.X += pan;
        if (Input.IsKeyDown(KeyboardKey.Up) || Input.IsKeyDown(KeyboardKey.W)) camera.Y -= pan;
        if (Input.IsKeyDown(KeyboardKey.Down) || Input.IsKeyDown(KeyboardKey.S)) camera.Y += pan;
    }

    /// <summary>Test scripts: where to click a named thing ("chef", "stove", "counter", "door", "tutorialcustomer", "staff0".., "ok").</summary>
    Vector2? ScriptTarget(string name)
    {
        Vector2 Body(CharacterInstance c) => Vector2.Transform(new Vector2(c.X, c.Y - 60), WorldToWindow);
        Vector2? Furn(FurnitureCategory cat) => cafe.FindFurniture(cat, -1).FirstOrDefault() is { } f
            ? Vector2.Transform(f.SolidPoint(), WorldToWindow)
            : null;
        Vector2? Ui(Rectangle? r) => r is { } x ? Screen.ToPixelsPoint(new Vector2(x.X + x.Width / 2, x.Y + x.Height / 2)) : null;
        if (name.StartsWith("toxin:"))
            return name == "toxin:plus" ? Ui(toxinPlusRect) : toxinRects.TryGetValue(name[6..], out var toxinAt) ? Ui(toxinAt) : null;
        if (name.StartsWith("panel:"))
            return staffRects.TryGetValue(name[6..], out var staffAt) ? Ui(staffAt) : null;
        if (name.StartsWith("edit:"))
            return edit.Buttons.TryGetValue(name[5..], out var editAt) ? Ui(editAt) : null;
        if (name.StartsWith("cheat:"))
            return cheatRects.TryGetValue(name[6..], out var cheatAt) ? Ui(cheatAt) : null;
        if (name.StartsWith("tab") && int.TryParse(name[3..], out int tabIndex))
            return edit.TabCenter(tabIndex) is { } tabAt ? Screen.ToPixelsPoint(tabAt) : null;
        if (name.StartsWith("item") && int.TryParse(name[4..], out int itemId))
            return edit.ItemCard(itemId) is { } cardAt ? Screen.ToPixelsPoint(cardAt) : null;
        if (name.StartsWith("staff") && int.TryParse(name[5..], out int i))
            return i < game.Staff.Count ? Body(game.Staff[i]) : null;
        if (name.StartsWith("pet") && int.TryParse(name[3..], out int pi))
            return pi < game.Pets.Count && game.Pets[pi].Visible
                ? Vector2.Transform(new Vector2(game.Pets[pi].X, game.Pets[pi].BodyBounds.Y + game.Pets[pi].BodyBounds.Height / 2), WorldToWindow)
                : null;
        return name switch
        {
            "chef" => Body(game.ChefActor),
            "tutorialcustomer" => game.TutorialCustomer is { } c ? Body(c) : null,
            "stove" => Furn(FurnitureCategory.Stove),
            "specialstove" => cafe.Objects.OfType<Stove>().FirstOrDefault(st => st.Def.StoveFamily > 0) is { } sp
                ? Vector2.Transform(sp.SolidPoint(), WorldToWindow)
                : null,
            "recipes" => Ui(recipesButtonRect),
            "upgrade" => Ui(upgradeButtonRect),
            "stoveclose" => Ui(specialCloseRect),
            "counter" => Furn(FurnitureCategory.Counter),
            "door" => Vector2.Transform(cafe.CellCenter(cafe.DoorCell), WorldToWindow),
            "infect" => Ui(infectButtonRect),
            "rest" => Ui(restButtonRect),
            "energize" => Ui(energizeButtonRect),
            "revive" => Ui(reviveButtonRect),
            "storage" => Ui(HudButton(HudStorage)),
            "zombies" => Ui(HudButton(HudZombies)),
            "tombstone" => Furn(FurnitureCategory.Tombstone),
            "generator" => Furn(FurnitureCategory.ToxinGenerator),
            "habitat" => Furn(FurnitureCategory.Habitat),
            "pets" => Ui(HudButton(HudPets)),
            "pedia" => Ui(HudButton(HudPedia)),
            "locker" => Ui(HudButton(HudLocker)),
            "photo" => Ui(photoButtonRect),
            "freeze" => Ui(freezeButtonRect),
            "petinfo" => Ui(petInfoRect),
            "petnext" => Ui(petNextRect),
            "petprev" => Ui(petPrevRect),
            "recharge" => Ui(rechargeRect),
            "activate" => Ui(activateRect),
            "quests" => Ui(HudButton(HudQuests)),
            "questbutton" => Ui(questButtonRect),
            "questfinish0" => Ui(questFinishRects[0]),
            "questfinish1" => Ui(questFinishRects[1]),
            "questfinish2" => Ui(questFinishRects[2]),
            "questfinish3" => Ui(questFinishRects[3]),
            "storeheld" => Ui(edit.StoreButtonRect),
            "stored0" => Ui(edit.FirstStoredRect),
            "chair" => Furn(FurnitureCategory.Chair),
            "decor" => Furn(FurnitureCategory.Decor),
            "table" => Furn(FurnitureCategory.Table),
            "fridge" => Furn(FurnitureCategory.Fridge),
            "magicfridge" => cafe.Objects.OfType<Furniture>().FirstOrDefault(game.IsMagicFridge) is { } mf
                ? Vector2.Transform(mf.SolidPoint(), WorldToWindow)
                : null,
            "fridgeclose" => Ui(fridge.CloseRect),
            "fridgeprev" => Ui(fridge.PrevRect),
            "fridgenext" => Ui(fridge.NextRect),
            "fridgeserve0" => Ui(fridge.ServeRects[0]),
            "fridgeserve1" => Ui(fridge.ServeRects[1]),
            "fridgeunlock0" => Ui(fridge.UnlockRects[0]),
            "fridgeunlock1" => Ui(fridge.UnlockRects[1]),
            "recipe0" => Ui(recipeBook.CookButton),
            "star" => Ui(recipeBook.StarButton),
            "buybook" => Ui(recipeBook.BuyButton),
            "book0" => Ui(recipeBook.BookCover(0)),
            "book1" => Ui(recipeBook.BookCover(1)),
            "book2" => Ui(recipeBook.BookCover(2)),
            "book3" => Ui(recipeBook.BookCover(3)),
            "book4" => Ui(recipeBook.BookCover(4)),
            "store" => Ui(HudButton(HudStore)),
            "logo" => Ui(HudButton(HudLogo)),
            "cash" => cafe.Objects.OfType<Furniture>().FirstOrDefault(f => f.EarnsOverTime) is { } item
                ? Vector2.Transform(item.SolidPoint(), WorldToWindow)
                : null,
            "cookbook" => Ui(HudButton(HudCookbook)),
            "map" => Ui(HudButton(HudMap)),
            "raidnow" => Ui(raidNowRect),
            "finish" => Ui(finishButtonRect),
            "chefstore" => Ui(chefButtonRect),
            "info" => Ui(infoButtonRect),
            "fire" => Ui(bioButtonRect),
            "ok" => chefMessage.ButtonCenter is { } okAt ? Screen.ToPixelsPoint(okAt) : null,
            "msgok" => Ui(Widgets.LastMessageButton),
            _ => null,
        };
    }

    static bool HitCharacter(CharacterInstance c, Vector2 p) =>
        p.X >= c.X - 45 && p.X <= c.X + 45 && p.Y >= c.Y - 160 && p.Y <= c.Y + 5;

    static bool HitFurniture(Furniture f, Vector2 p) => f.HitTest(p.X, p.Y);

    void Click(Vector2 p)
    {
        // topmost first: characters, then furniture, in reverse draw order
        var objects = cafe.Objects;
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            // GameStateCafe::update case 3: a pet out in the yard
            if (objects[i] is CharacterInstance pet && pet.IsPet && HitPet(pet, p))
            {
                CloseSpecialBar();
                game.Selected = game.Selected == pet ? null : pet;
                stoveBar = null;
                tombstoneBar = null;
                return;
            }
            if (objects[i] is CharacterInstance c && !c.IsPet && HitCharacter(c, p)
                && (c == game.ChefActor || c.Character.Role == Role.Staff
                    || (c.Character.Role == Role.Customer && !c.Character.IsZombie && cafe.IsInside(c.Cell))))
            {
                CloseSpecialBar();
                bool again = game.Selected == c;
                game.Selected = again ? null : c;
                if (c.Character.Job == Job.Daydreaming)
                    game.WakeUp(c);
                if (!again && c.Character.Role == Role.Customer && game.Hints.TutorialDone)
                    game.Hint(0x4c, true);
                game.Sound(72 + cafe.Rng.Next(14));
                return;
            }
        }
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            // a cash item with something in it: empty it
            if (objects[i] is Furniture cash && cash.Visible && cash.Stored > 0 && HitFurniture(cash, p))
            {
                game.Collect(cash);
                return;
            }
            // a tombstone: its bar (TombstoneCommandBar)
            if (objects[i] is Furniture ts && ts.Visible && ts.Cell >= 0 && ts.Def.Category == FurnitureCategory.Tombstone && HitFurniture(ts, p))
            {
                game.Selected = null;
                stoveBar = null;
                tombstoneBar = tombstoneBar == ts ? null : ts;
                game.Sound(40);
                return;
            }
            // GameStateCafe::update, a fridge: what is in it (FUN_000bd7b0 panel 2)
            if (objects[i] is Furniture fr && fr.Visible && fr.Def.Category == FurnitureCategory.Fridge && HitFurniture(fr, p))
            {
                // case 8: a magic fridge's free dish of the day first ("A GIFT FROM MAGIC FRIDGE!", strings 537/538)
                if (game.MagicFridgeGift(fr, GameClock.Now) is { } gift)
                    Notice(data.Strings[537], new List<string> { data.Strings[538], gift.DisplayName(data) }, data.FoodImage(gift.Food));
                else
                    OpenFridge();
                return;
            }
            if (objects[i] is Stove s && HitFurniture(s, p))
            {
                // the tutorial keeps the stoves until its table and chair are bought: the original does nothing
                // here; the port says why, with the tutorial's own tip for the step (string 16)
                if (game.StovesHeld && game.WaitingForTableAndChair)
                {
                    chefMessage.Show(data.Strings[16]);
                    return;
                }
                bool nobody = game.Selected == null;
                if (specialBar != null && specialBar != s)
                    CloseSpecialBar();
                if (game.TapStove(s))
                {
                    // GameStateCafe::update: a special stove (furniture +0x41) has its own bar first
                    if (s.Def.StoveFamily > 0)
                    {
                        specialBar = s;
                        stoveBar = null;
                        tombstoneBar = null;
                    }
                    else
                        OpenRecipes(s);
                }
                else if (nobody && s.Busy && !s.IsDone && !s.IsBurnt(cafe))
                {
                    stoveBar = stoveBar == s ? null : s;
                    // GameStateCafe::update: the cook command bar is up (the finish tip points at FINISH)
                    if (stoveBar != null)
                        game.Hint(0x31, false);
                }
                return;
            }
            // a counter, table or sink with a cook or zombie picked: back to serving
            if (game.Selected != null && objects[i] is Furniture f && f.Visible
                && f.Def.Category is FurnitureCategory.Counter or FurnitureCategory.Table or FurnitureCategory.Sink
                && HitFurniture(f, p))
            {
                game.TapServingArea(f);
                return;
            }
        }
        if (OnDoor(p))
        {
            game.TapDoor();
            return;
        }
        Deselect();
    }

    /// <summary>Nobody picked and no bar up (a click on the floor, a right click, Esc).</summary>
    void Deselect()
    {
        CloseSpecialBar();
        game.Selected = null;
        stoveBar = null;
        tombstoneBar = null;
    }

    /// <summary>
    /// Something under the mouse that <see cref="Click"/> would act on - a character that can be picked, a stove,
    /// fridge, tombstone or full cash item, a counter, table or sink while someone is picked, or the doorway.
    /// </summary>
    bool ClickableAt(Vector2 p)
    {
        foreach (var o in cafe.Objects)
        {
            if (o is CharacterInstance pet && pet.IsPet)
            {
                if (HitPet(pet, p))
                    return true;
                continue;
            }
            if (o is CharacterInstance c && HitCharacter(c, p)
                && (c == game.ChefActor || c.Character.Role == Role.Staff
                    || (c.Character.Role == Role.Customer && !c.Character.IsZombie && cafe.IsInside(c.Cell))))
                return true;
            if (o is Furniture f && f.Visible && f.Cell >= 0
                && (f.Stored > 0 || f is Stove || f.Def.Category is FurnitureCategory.Tombstone or FurnitureCategory.Fridge
                    || game.Selected != null && f.Def.Category is FurnitureCategory.Counter or FurnitureCategory.Table or FurnitureCategory.Sink)
                && HitFurniture(f, p))
                return true;
        }
        return OnDoor(p);
    }

    /// <summary>
    /// The mouse on the door: on the doorway in the wall (where the door is drawn), or on the floor just inside or
    /// outside it (the original's touch test, by cell).
    /// </summary>
    bool OnDoor(Vector2 p)
    {
        foreach (var o in cafe.Objects)
            if (o is CafeWall { IsDoor: true } w && w.Contains(p.X, p.Y))
                return true;
        var (u, v) = cafe.ScreenToDiamond(p.X, p.Y);
        int cell = cafe.IndexOf(u, v);
        return cell >= 0 && (cell == cafe.DoorCell || cell == cafe.Neighbour(cafe.DoorCell, 3));
    }

    // ---------------------------------------------------------------- Frequent Fryer

    /// <summary>
    /// FrequentFryerDialog (FUN_0016fa58, FUN_00172cd0): "You have returned to cook for:", the days in a row (strings
    /// 366-369), the reward - "a reward of:" (372) cash or toxin (374), "a prepared meal of:" (370) a dish - and
    /// "Tomorrow's Bonus:" (371), a dish as the "Mystery Dish" (373).
    /// </summary>
    List<string> DailyBonusLines(CafeController.DailyBonus b)
    {
        string Reward(int money, int toxin) => money > 0 ? "$" + money : toxin + data.Strings[374];
        var lines = new List<string>
        {
            data.Strings[366],
            $"{b.Days} {data.Strings[b.Days == 1 ? 367 : 368]} {data.Strings[369]}",
            data.Strings[b.Food >= 0 ? 370 : 372],
            b.Food >= 0 ? data.Foods[b.Food].Name : Reward(b.Money, b.Toxin),
        };
        var (money, toxin, dish) = game.BonusFor(game.BonusDay(b.Days + 1));
        lines.Add($"{data.Strings[371]} {(dish ? data.Strings[373] : Reward(money, toxin))}");
        return lines;
    }

    // ---------------------------------------------------------------- the special stoves

    /// <summary>The special stove's bar goes without a recipe picked: a cook sent to the stove stops waiting.</summary>
    void CloseSpecialBar()
    {
        if (specialBar == null)
            return;
        specialBar = null;
        game.RecipeCancelled();
    }

    /// <summary>
    /// StoveCommandBar (FUN_001d2014, FUN_001d2ce0): an idle special stove's picture, name and description, RECIPES (ui
    /// 191) for its recipe book, UPGRADE (ui 190) while a better stove of its line is open to the player, and the X
    /// (each to sound 36). It goes once the stove is cooking.
    /// </summary>
    void DrawSpecialStoveBar(Stove s)
    {
        recipesButtonRect = upgradeButtonRect = specialCloseRect = null;
        if (s.Busy || s.Burnt || !cafe.Objects.Contains(s))
        {
            specialBar = null;
            return;
        }
        var ui = data.IngameUi;
        var up = game.StoveUpgrade(s.Def);
        float bw = 700, bh = 124;
        var r = new Rectangle((Screen.Width - bw) / 2, Screen.Height - bh - 40, bw, bh);
        Widgets.Panel(r);
        if (data.Sheet(s.Def) is { Length: > 0 } sheet)
        {
            var img = sheet[0];
            float k = MathF.Min(1f, MathF.Min(96f / Math.Max(1, img.FullWidth), 104f / Math.Max(1, img.FullHeight)));
            img.Draw(r.X + 62, r.Y + bh - 10, Anchor.Bottom | Anchor.HCenter, Colors.FromArgb(s.Def.Tint), Flip.None, k, k);
        }
        float tx = r.X + 124, textW = bw - 124 - (up != null ? 250 : 140);
        Widgets.Label(s.Def.Name, tx, r.Y + 12, 24, Anchor.Top | Anchor.Left, Widgets.Gold);
        var lines = Widgets.WrapText(s.Def.Description, textW, 16);
        for (int i = 0; i < Math.Min(3, lines.Count); i++)
            Widgets.Label(lines[i], tx, r.Y + 46 + i * 21, 16);
        float bx = r.X + bw - 74, by = r.Y + bh / 2 + 6;
        var recipes = ui[UiSprites.Recipes];
        recipesButtonRect = new Rectangle(bx - recipes.FullWidth * 0.4f, by - recipes.FullHeight * 0.4f, recipes.FullWidth * 0.8f, recipes.FullHeight * 0.8f);
        if (Widgets.ImageButton(recipes, bx, by, 0.8f))
        {
            // the cook sent to the stove (if any) keeps waiting, now for a recipe from the book
            game.Sound(36);
            specialBar = null;
            OpenRecipes(s);
            return;
        }
        if (up != null)
        {
            bx -= 112;
            var upgrade = ui[UiSprites.Upgrade];
            upgradeButtonRect = new Rectangle(bx - upgrade.FullWidth * 0.4f, by - upgrade.FullHeight * 0.4f, upgrade.FullWidth * 0.8f, upgrade.FullHeight * 0.8f);
            if (Widgets.ImageButton(upgrade, bx, by, 0.8f))
            {
                game.Sound(36);
                CloseSpecialBar();
                AskUpgrade(s, up);
                return;
            }
        }
        var close = ui[UiSprites.CloseX];
        float cx = r.X + bw - 16, cy = r.Y + 16;
        specialCloseRect = new Rectangle(cx - close.FullWidth * 0.25f, cy - close.FullHeight * 0.25f, close.FullWidth * 0.5f, close.FullHeight * 0.5f);
        if (Widgets.ImageButton(close, cx, cy, 0.5f))
        {
            game.Sound(36);
            CloseSpecialBar();
        }
    }

    /// <summary>
    /// FUN_000b4a78, UPGRADE: the better stove offered in the old one's place - its name and description, the price
    /// (string 314) and the experience it brings; Purchase (315) buys it. (The original stands it on the old stove's
    /// cell with the store's buy bar to confirm.)
    /// </summary>
    void AskUpgrade(Stove s, FurnitureDef up)
    {
        string price = up.Currency == 1 ? $"{up.Price} toxin" : $"${up.Price}";
        question = ($"{up.Name}\n{up.Description}\n{data.Strings[314]}{price}   +{(int)up.F3C} XP", data.Strings[315], () =>
        {
            if (game.CannotBuy(up) is { } why)
                messages.Enqueue(("", new List<string> { why }, null));
            else
                game.UpgradeStove(s, up);
        });
    }

    // ---------------------------------------------------------------- the fridge

    void OpenFridge()
    {
        game.Selected = null;
        fridge.Open();
        game.FridgeOpened();
    }

    /// <summary>
    /// FUN_000d5bb8, once the fridge has shut on a pick: serve the dish, learn its recipe, or say why it cannot be
    /// learned and open the fridge again.
    /// </summary>
    void FridgePicked()
    {
        if (fridge.Visible || fridge.Picked is not { } pick)
            return;
        fridge.ClearPick();
        switch (pick.Kind)
        {
            case FridgeDialog.Choice.Serve:
                game.ServeFromFridge(pick.Item);
                break;
            case FridgeDialog.Choice.Unlock:
                game.UnlockFromFridge(pick.Item);
                break;
            case FridgeDialog.Choice.Locked:
                messages.Enqueue(("", new List<string> { game.WhyLocked(pick.Item) }, null));
                fridge.Open();
                break;
        }
    }

    // ---------------------------------------------------------------- recipe book

    void OpenRecipes(Stove st)
    {
        // FUN_000b5428: the cookbook's badge goes
        hudBadge[HudCookbook] = false;
        recipeBook.Open(st);
        game.Hint(0xb, false);
    }

    /// <summary>The recipe book shut without a recipe picked.</summary>
    void CloseRecipes()
    {
        recipeBook.Close();
        game.RecipeCancelled();
    }

    static string FormatTime(int seconds)
    {
        if (seconds >= 3600)
            return $"{seconds / 3600}h {seconds % 3600 / 60:00}m";
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    // ---------------------------------------------------------------- test driver (ZC_AUTOPLAY=1)

    static readonly bool autoInfect = Environment.GetEnvironmentVariable("ZC_AUTOINFECT") == "1";

    void AutoPlay()
    {
        if (autoInfect && !game.ChefActor.Tasks.Any(t => t is Game.Cafe.Tasks.InfectTask))
        {
            var sitting = game.Customers.FirstOrDefault(c => c.AnimId == (int)CharAnim.Sit && game.CannotInfect(c) == null);
            if (sitting != null)
                game.Infect(sitting);
        }
        foreach (var o in cafe.Objects)
        {
            if (o is not Stove s)
                continue;
            if (!s.Busy && !s.Burnt && game.Player.Money >= data.Foods[0].Cost)
                game.CookRecipe(s, 0);
            if (s.Busy && !s.IsDone && s.AssignedCooks == 0 && game.ChefActor.IsIdle)
            {
                game.Selected = game.ChefActor;
                game.TapStove(s);
            }
            if (s.IsDone && !game.Staff.Append(game.ChefActor).Any(z => z.Tasks.Any(t => t is Game.Cafe.Tasks.DishToCounterTask)))
                game.TapStove(s);
            if (s.IsBurnt(cafe) && game.ChefActor.IsIdle)
            {
                game.Selected = game.ChefActor;
                game.TapStove(s);
            }
        }
    }

    // ---------------------------------------------------------------- drawing

    public override void Draw()
    {
        Gfx.Draw.Reset(WorldToWindow);
        // the world the window shows, for the ground past the map's edges
        Matrix3x2.Invert(WorldToWindow, out var toWorld);
        var viewA = Vector2.Transform(Vector2.Zero, toWorld);
        var viewB = Vector2.Transform(new Vector2(Screen.PixelWidth, Screen.PixelHeight), toWorld);
        cafe.View = new Rectangle(viewA.X, viewA.Y, viewB.X - viewA.X, viewB.Y - viewA.Y);
        cafe.Draw();
        DrawWorldOverlays();
        if (edit.Active)
            edit.DrawWorld();
        Gfx.Draw.Reset(Screen.ToPixels);
        Widgets.BeginFrame();
        if (photo >= 0)
            return;
        DrawHud();
        infectButtonRect = null;
        freezeButtonRect = null;
        restButtonRect = null;
        energizeButtonRect = null;
        reviveButtonRect = null;
        chefButtonRect = null;
        infoButtonRect = null;
        bioButtonRect = null;
        photoButtonRect = null;

        if (edit.Active)
            edit.DrawUi();
        else if (RecipesOpen)
        {
            // under a question the book stays in view but does not answer
            Widgets.Blocked = question != null;
            recipeBook.Draw(cancelled: () => game.RecipeCancelled(), cooked: () => game.RecipeChosen());
            Widgets.Blocked = false;
        }
        else if (bio != null)
        {
            Widgets.Blocked = question != null;
            DrawBio();
            Widgets.Blocked = false;
        }
        else if (competitor != null)
        {
            Widgets.Blocked = chefMessage.Visible;
            DrawCompetitor();
            Widgets.Blocked = false;
        }
        else if (reviewOpen)
        {
            Widgets.Blocked = chefMessage.Visible;
            DrawReview();
            Widgets.Blocked = false;
        }
        else if (questsOpen)
        {
            Widgets.Blocked = chefMessage.Visible || question != null || messages.Count > 0;
            DrawQuests();
            Widgets.Blocked = false;
        }
        else if (fridge.Visible)
        {
            Widgets.Blocked = chefMessage.Visible || messages.Count > 0;
            fridge.Draw(game.Player.Fridge, game.FridgeButtons, game.Sound, Raylib.GetFrameTime());
            Widgets.Blocked = false;
        }
        else if (specialBar != null)
            DrawSpecialStoveBar(specialBar);
        else if (game.Selected != null)
            DrawCommandBar(game.Selected);
        else if (stoveBar != null)
            DrawStoveBar(stoveBar);
        else if (tombstoneBar != null)
            DrawTombstoneBar(tombstoneBar);
        if (toxinShopOpen)
        {
            Widgets.Blocked = messages.Count > 0 || question != null || chefMessage.Visible;
            DrawToxinShop();
            Widgets.Blocked = false;
        }
        staffRects.Clear();
        if (staffOpen)
        {
            Widgets.Blocked = messages.Count > 0 || question != null || chefMessage.Visible;
            DrawStaff();
            Widgets.Blocked = false;
        }
        DrawStaffButton();
        if (cheatsOpen)
        {
            // a box or question up front takes the clicks (the tutorial chef's tips do not, to be skipped)
            Widgets.Blocked = messages.Count > 0 || question != null;
            DrawCheats();
            Widgets.Blocked = false;
        }
        arrow.Draw(WorldToScreen);
        chefMessage.Draw();
        if (messages.Count > 0 && !chefMessage.Visible)
        {
            var (title, lines, done) = messages.Peek();
            pictures.TryGetValue(lines, out var picture);
            if (Widgets.MessageBox(title, lines, data.Strings[362], picture))
            {
                messages.Dequeue();
                pictures.Remove(lines);
                done?.Invoke();
            }
        }
        else if (question is { } q && !chefMessage.Visible)
        {
            int answer = Widgets.QuestionBox(q.Text, q.Yes, data.Strings[316]);
            if (answer != 0)
            {
                question = null;
                if (answer == 1)
                    q.OnYes();
            }
        }
    }

    /// <summary>The selected worker's details and orders (the original CommandBar: Work/Rest, energy, level).</summary>
    void DrawCommandBar(CharacterInstance w)
    {
        if (w.IsPet)
        {
            DrawPetBar(w);
            return;
        }
        var c = w.Character;
        bool staff = c.Role == Role.Staff;
        // a worker's job (CharacterInstance::vf38) gets a line of its own: "Reanimating" and the time still to go
        string job = staff ? game.JobText(w).Replace("\n", "") : "";
        // from level 9 a zombie's bar has MEATLOCKER too (command bar button 9, ui 197)
        bool freezer = staff && w != game.ChefActor && game.Player.Level > 8 && OpenLocker != null;
        float bw = freezer ? 620 : 520, bh = job.Length > 0 ? 116 : 96, jobRow = job.Length > 0 ? 20 : 0;
        var r = new Rectangle((Screen.Width - bw) / 2, Screen.Height - bh - 40, bw, bh);
        Widgets.Panel(r);
        if (c.Role == Role.Customer)
        {
            // a customer: who they are, and the chance to make them staff
            Widgets.Label(c.Name, r.X + 16, r.Y + 10, 26, Anchor.Top | Anchor.Left, Widgets.Gold);
            Widgets.Label(c.Def.Name, r.X + 16, r.Y + 42, 18);
            var (cost, toxin) = game.InfectCost(w);
            string price = cost == 0 ? "free" : toxin ? cost + " toxin" : "$" + cost;
            Widgets.Label($"{data.Strings[430]}: {price}    Staff {game.Staff.Count}/{game.StaffLimit}", r.X + 16, r.Y + 66, 16);
            var infect = data.IngameUi[UiSprites.Infect];
            infectButtonRect = new Rectangle(r.X + bw - 70 - infect.FullWidth * 0.425f, r.Y + bh / 2 - infect.FullHeight * 0.425f,
                infect.FullWidth * 0.85f, infect.FullHeight * 0.85f);
            if (Widgets.ImageButton(infect, r.X + bw - 70, r.Y + bh / 2, 0.85f, game.CannotInfect(w) == null))
                game.Infect(w);
            else if (game.CannotInfect(w) is { Length: > 0 } why && Widgets.Hover(new Rectangle(r.X + bw - 130, r.Y, 120, bh)))
                Widgets.Label(why, Screen.Width / 2, r.Y - 24, 18, Anchor.Center, Widgets.Gold);
            return;
        }
        Widgets.Label(c.Name, r.X + 16, r.Y + 10, 26, Anchor.Top | Anchor.Left, Widgets.Gold);
        string sub = staff ? $"Level {c.Level + 1}   Served {c.Served}" : c.Def.Name;
        Widgets.Label(sub, r.X + 16, r.Y + 42, 18);
        if (job.Length > 0)
            Widgets.Label(job, r.X + 16, r.Y + 64, 16, Anchor.Top | Anchor.Left, new Color(170, 230, 120, 255));
        if (staff)
        {
            float max = MathF.Max(1f, c.MaxEnergy(data));
            Widgets.Label(data.Strings[512].Trim(), r.X + 16, r.Y + 66 + jobRow, 16);
            Widgets.Bar(new Rectangle(r.X + 96, r.Y + 68 + jobRow, 112, 16), c.Energy / max, new Color(120, 220, 90, 255));
            var need = data.K.ZombieLevelXp;
            float xpFrac = c.Level < need.Length ? (float)c.Xp / need[c.Level] : 1f;
            Widgets.Bar(new Rectangle(r.X + 96, r.Y + 86 + jobRow, 112, 6), xpFrac, new Color(90, 160, 255, 255));
            bool resting = c.Job == Job.Resting;
            // REST / WORK (in-game UI 53 / 120) and ENERGIZE (14), as in the original command bar
            var restImg = data.IngameUi[resting ? UiSprites.Work : UiSprites.Rest];
            float s = 0.8f;
            float rx = r.X + bw - 60, ry = r.Y + bh / 2;
            restButtonRect = new Rectangle(rx - restImg.FullWidth * s / 2, ry - restImg.FullHeight * s / 2, restImg.FullWidth * s, restImg.FullHeight * s);
            if (Widgets.ImageButton(restImg, rx, ry, s, c.ReanimateMs < 1))
            {
                if (resting)
                    game.Work(w);
                else
                    game.Rest(w);
            }
            InfoButton(w, rx - 200, ry, s);
            if (freezer)
                FreezeButton(w, rx - 300, ry, s);
            float ex = rx - 100;
            if (c.ReanimateMs >= 1)
            {
                // knocked out: REVIVE (ui 15, command bar button 7) in ENERGIZE's place - a vial has it up now
                var revive = data.IngameUi[UiSprites.Revive];
                reviveButtonRect = new Rectangle(ex - revive.FullWidth * s / 2, ry - revive.FullHeight * s / 2, revive.FullWidth * s, revive.FullHeight * s);
                energizeButtonRect = null;
                if (Widgets.ImageButton(revive, ex, ry, s, game.CannotRevive(w) is null or { Length: > 0 }))
                {
                    if (game.CannotRevive(w) is { Length: > 0 } why)
                        messages.Enqueue(("", new List<string> { why }, null));
                    else
                        question = (game.EnergizeQuestion(w), data.Strings[319], () => game.Revive(w));
                }
                return;
            }
            reviveButtonRect = null;
            var energize = data.IngameUi[UiSprites.Energize];
            energizeButtonRect = new Rectangle(ex - energize.FullWidth * s / 2, ry - energize.FullHeight * s / 2, energize.FullWidth * s, energize.FullHeight * s);
            if (Widgets.ImageButton(energize, ex, ry, s, game.CannotEnergize(w) is null or { Length: > 0 }))
            {
                if (game.CannotEnergize(w) is { Length: > 0 } why)
                    messages.Enqueue(("", new List<string> { why }, null));
                else
                    question = (game.EnergizeQuestion(w), data.Strings[319], () => game.Energize(w));
            }
        }
        else
        {
            Widgets.Label("Click a stove to cook.", r.X + 16, r.Y + 66, 18, Anchor.Top | Anchor.Left, new Color(220, 220, 220, 255));
            // after the tutorial the chef's bar has CHEF (command bar button 10, ui 129): the chef store
            if (w == game.ChefActor && game.Hints.TutorialDone)
            {
                var chef = data.IngameUi[UiSprites.Chef];
                float s = 0.8f, cx = r.X + bw - 60, cy = r.Y + bh / 2;
                chefButtonRect = new Rectangle(cx - chef.FullWidth * s / 2, cy - chef.FullHeight * s / 2, chef.FullWidth * s, chef.FullHeight * s);
                if (Widgets.ImageButton(chef, cx, cy, s))
                    OpenChefStore();
                InfoButton(w, cx - 100, cy, s);
                // PHOTO (command bar button 2, ui 115; FUN_000b9b88): a picture of the cafe without the HUD
                var photoImg = data.IngameUi[UiSprites.Photo];
                float px = cx - 200;
                photoButtonRect = new Rectangle(px - photoImg.FullWidth * s / 2, cy - photoImg.FullHeight * s / 2,
                    photoImg.FullWidth * s, photoImg.FullHeight * s);
                if (Widgets.ImageButton(photoImg, px, cy, s) && photo < 0)
                {
                    game.Selected = null;
                    photo = 0;
                }
            }
        }
    }

    Rectangle? freezeButtonRect;

    /// <summary>
    /// MEATLOCKER (command bar button 9, FUN_000d51b0): into the meat locker - or, with it full, five more hooks for
    /// 5 toxin ("Your Meatlocker is full ...", up to sixty).
    /// </summary>
    void FreezeButton(CharacterInstance w, float x, float y, float s)
    {
        var img = data.IngameUi[197];
        freezeButtonRect = new Rectangle(x - img.FullWidth * s / 2, y - img.FullHeight * s / 2, img.FullWidth * s, img.FullHeight * s);
        if (!Widgets.ImageButton(img, x, y, s, w.Character.ReanimateMs < 1))
            return;
        game.Sound(36);
        if (!game.LockerFull)
        {
            game.Freeze(w);
            return;
        }
        if (!game.CanExpandLocker)
            messages.Enqueue(("", new List<string> { data.Exe.Get(0x341773) + CafeController.LockerMax + data.Exe.Get(0x34178c) }, null));
        else
            question = (data.Exe.Get(0x3414cf), data.Strings[319], () =>
            {
                if (!game.ExpandLocker())
                    messages.Enqueue(("", new List<string> { data.Strings[320] }, null));
            });
    }

    // PHOTO: frames since it was pressed (GameStateCafe +0x1dc while in mode 8), -1 when not taking one
    int photo = -1;
    Rectangle? photoButtonRect;

    /// <summary>
    /// Where a PHOTO goes - the original's photo album is the Pictures folder here (ZC_PHOTOS=dir for tests); its
    /// offer to put the picture on Facebook is left out, as the port has no online side.
    /// </summary>
    static string PhotoPath()
    {
        string dir = Environment.GetEnvironmentVariable("ZC_PHOTOS") is { Length: > 0 } d ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ZombieCafePlus");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"ZombieCafe-{DateTime.Now:yyyyMMdd-HHmmss}.png");
    }

    /// <summary>FUN_000b9e98: GameStateChooseChef as the chef store; back here when done, with the new chef if one was taken.</summary>
    void OpenChefStore()
    {
        game.Sound(40);
        game.Selected = null;
        App.Current.SetScreen(new ChooseChefScreen(data, audio, store: game.Player,
            chosen: def =>
            {
                game.ChangeChef(def);
                App.Current.SetScreen(this);
            },
            back: () => App.Current.SetScreen(this)));
    }

    /// <summary>CookCommandBar: what is cooking, how long it has to go, and FINISH for toxin.</summary>
    void DrawStoveBar(Stove s)
    {
        finishButtonRect = null;
        if (!s.Busy || s.IsDone || s.Dish == null || s.IsBurnt(cafe) || !cafe.Objects.Contains(s))
        {
            stoveBar = null;
            return;
        }
        float bw = 520, bh = 96;
        var r = new Rectangle((Screen.Width - bw) / 2, Screen.Height - bh - 40, bw, bh);
        Widgets.Panel(r);
        Widgets.Label(game.RecipeName(s.Dish.Food, s.Dish.Modifier), r.X + 16, r.Y + 10, 26, Anchor.Top | Anchor.Left, Widgets.Gold);
        int left = Math.Max(0, s.CookSeconds - s.Progress / 1000);
        Widgets.Label($"{data.Strings[377]} {FormatTime(left)}", r.X + 16, r.Y + 44, 20);
        int cost = game.FinishCost(s);
        var finish = data.IngameUi[UiSprites.Finish];
        float fx = r.X + bw - 70, fy = r.Y + bh / 2;
        data.IngameUi[UiSprites.VialMedium].Draw(fx - 90, fy, Anchor.Center, Color.White, Flip.None, 0.6f, 0.6f);
        Widgets.Label(cost.ToString(), fx - 75, fy, 22, Anchor.Left | Anchor.VCenter);
        finishButtonRect = new Rectangle(fx - finish.FullWidth * 0.4f, fy - finish.FullHeight * 0.4f, finish.FullWidth * 0.8f, finish.FullHeight * 0.8f);
        if (Widgets.ImageButton(finish, fx, fy, 0.8f))
        {
            // CookCommandBar::vf02: the tip's arrow can go
            game.Hint(0x32, true);
            question = (data.Strings[317] + cost + data.Strings[318], data.Strings[319], () => game.FinishWithToxin(s));
        }
    }

    void DrawWorldOverlays()
    {
        // cooking timers over busy stoves (the stove command bar of the original shows the same)
        foreach (var o in cafe.Objects)
        {
            if (o is Stove s && s.Busy && s.Dish != null && !s.IsDone)
            {
                int left = Math.Max(0, s.CookSeconds - s.Progress / 1000);
                Text.DrawAnchored(FormatTime(left), s.DrawX + s.Width / 2, s.DrawY - 50, 26, Anchor.Bottom | Anchor.HCenter,
                    Color.White, Color.Black, 3);
            }
        }
        if (game.Selected is { } sel)
        {
            string job = sel.Character.Job switch
            {
                Job.Serving => data.Strings[StringTable.StatusServing],
                Job.Cooking => data.Strings[StringTable.StatusCooking],
                Job.Resting => data.Strings[StringTable.StatusResting],
                Job.Attacking => data.Strings[StringTable.StatusAttacking],
                Job.Daydreaming => data.Strings[StringTable.StatusDaydreaming],
                _ => "",
            };
            string label = job.Length > 0 ? $"{sel.Character.Name} - {job}" : sel.Character.Name;
            float top = sel.IsPet ? sel.BodyBounds.Y - 10 : sel.Y - 175;
            Text.DrawAnchored(label, sel.X, top, 26, Anchor.Bottom | Anchor.HCenter, new Color(255, 230, 120, 255), Color.Black, 3);
            Gfx.Draw.Rect(new Rectangle(sel.X - 30, sel.Y - 4, 60, 8), new Color(255, 230, 120, 160));
        }
    }

    /// <summary>The top bar on the cafe's awning: cash, toxin, level and experience, rating; the store button.</summary>
    void DrawHud()
    {
        var ui = data.IngameUi;
        var p = game.Player;

        // HudElement (FUN_0010d3e0): the buttons the player has, at the top left - in the iPod/iPhone layout (device
        // types 2-5) one column down the left edge, pulled 10 points into the corner (DAT_003750e4 / DAT_003750fc;
        // the iPad's is three to a row)
        var shown = HudGridOrder.Where(i => hudVisible[i]).ToList();
        Array.Clear(hudRects);
        float gridRight = 0;
        Rectangle panel = default;
        float gh = ui[HudSprites[HudStore]].FullHeight;   // the grid is sized from the store button
        float stepX = gh + (int)gh / 7, stepY = (int)(gh * 0.9f) + (int)gh / 7, first = (int)(gh * 0.5f) + (int)gh / 7 + HudGridShift;
        if (shown.Count > 0)
        {
            int rows = (shown.Count + HudColumns - 1) / HudColumns;
            int cols = Math.Min(HudColumns, shown.Count);
            gridRight = first + (cols - 1) * stepX + first - HudGridShift;
            panel = new Rectangle(0, 0, gridRight, rows * stepY + (int)(gh * 0.5f) + HudGridShift);
        }

        // the striped awning across the rest of the top, with the cafe's numbers on it
        var awning = ui[UiSprites.Awning];
        float aw = awning.FullWidth, ah = awning.FullHeight;
        for (float x = Screen.Width; x > gridRight; x -= aw)
            awning.Draw(x, 0, Anchor.Top | Anchor.Right, Color.White);
        Gfx.Draw.Rect(new Rectangle(gridRight, 0, Screen.Width - gridRight, ah * 0.62f), new Color(0, 0, 0, 90));

        if (shown.Count > 0)
        {
            Widgets.Hover(panel);
            Gfx.Draw.Rect(panel, new Color(20, 14, 12, 255));
            Gfx.Draw.Rect(new Rectangle(panel.X + panel.Width - 3, 0, 3, panel.Height), new Color(140, 30, 30, 255));
            Gfx.Draw.Rect(new Rectangle(0, panel.Height - 3, panel.Width, 3), new Color(140, 30, 30, 255));
            for (int k = 0; k < shown.Count; k++)
            {
                int b = shown[k];
                var img = ui[HudSprites[b]];
                float cx = first + k % HudColumns * stepX, cy = first + k / HudColumns * stepY;
                hudRects[b] = new Rectangle(cx - img.FullWidth / 2f, cy - img.FullHeight / 2f, img.FullWidth, img.FullHeight);
                bool enabled = !edit.Active && !RecipesOpen && !chefMessage.Visible;
                if (Widgets.ImageButton(img, cx, cy, 1f, enabled))
                    HudButtonPressed(b);
            }
        }
        // FUN_001102e4: the rest in a column at the right edge (5/8 of the map button in), from below the top bar,
        // a store button's height and an eighth of the map button's width apart
        float mapW = ui[HudSprites[HudMap]].FullWidth, storeH = ui[HudSprites[HudStore]].FullHeight;
        float sideX = Screen.Width - (int)mapW / 8 - (int)mapW / 2;
        float sideY = ah + (int)mapW / 8 + storeH / 2;
        foreach (int b in HudSideOrder)
        {
            if (!hudVisible[b])
                continue;
            var img = ui[HudSprites[b]];
            hudRects[b] = new Rectangle(sideX - img.FullWidth / 2f, sideY - img.FullHeight / 2f, img.FullWidth, img.FullHeight);
            bool enabled = !edit.Active && !RecipesOpen && !chefMessage.Visible;
            if (Widgets.ImageButton(img, sideX, sideY, 1f, enabled))
                HudButtonPressed(b);
            sideY += storeH + (int)mapW / 8;
        }
        // the badges, at each showing button's bottom left corner
        for (int b = 0; b < HudButtons; b++)
            if (hudBadge[b] && hudRects[b] is { } br)
                ui[UiSprites.Alert].Draw(br.X, br.Y + br.Height, Anchor.Center, Color.White);

        float y = ah * 0.31f;
        float x0 = gridRight + 22;
        // cash
        Widgets.Sprite(ui[UiSprites.Cash], x0, y, 0.8f);
        Text.DrawAnchored(p.Money.ToString("N0"), x0 + 20, y, 26, Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        // toxin, with the "get more" button (the toxin store is not part of the port)
        float tx = x0 + 170;
        Widgets.Sprite(ui[UiSprites.VialMedium], tx, y, 0.7f);
        Text.DrawAnchored(p.Toxin.ToString(), tx + 18, y, 26, Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        toxinPlusRect = new Rectangle(tx + 60, y - 20, 40, 40);
        if (Widgets.ImageButton(ui[UiSprites.ToxinPlus], tx + 80, y, 0.55f, !chefMessage.Visible && !edit.Active))
            OpenToxinShop();
        // level star and experience bar
        float lx = MathF.Max(tx + 140, (gridRight + Screen.Width) / 2 - 150);
        Widgets.Sprite(ui[UiSprites.Star], lx, y, 1.05f);
        Text.DrawAnchored(p.Level.ToString(), lx, y + 2, 20, Anchor.Center, Color.White, Color.Black, 2);
        int need = p.XpForNextLevel(data);
        float frac = need == int.MaxValue || need <= 0 ? 1f : (float)p.Xp / need;
        float barW = MathF.Min(200, Screen.Width - lx - 380);
        Widgets.Bar(new Rectangle(lx + 26, y - 9, barW, 18), frac, new Color(80, 170, 255, 255));
        Text.DrawAnchored(p.Xp + " / " + need + " XP", lx + 26 + barW / 2, y, 15, Anchor.Center, Color.White, Color.Black, 2);
        // rating as five stars
        float rx = Screen.Width - 330;
        Text.DrawAnchored(data.Strings[658], rx, y, 16, Anchor.Left | Anchor.VCenter, Color.White, Color.Black, 2);
        float stars = p.EffectiveRating * 5f;
        var full = ui[172];
        var empty = ui[173];
        for (int i = 0; i < 5; i++)
        {
            float sx = rx + 118 + i * 24;
            Widgets.Sprite(empty, sx, y, 1.1f);
            float fill = Math.Clamp(stars - i, 0f, 1f);
            if (fill > 0)
                Widgets.Sprite(full, sx, y, 1.1f * MathF.Sqrt(fill));
        }
        // open / closed
        Text.DrawAnchored(cafe.IsOpen ? "OPEN" : "CLOSED", Screen.Width - 16, y, 22, Anchor.Right | Anchor.VCenter,
            cafe.IsOpen ? new Color(120, 255, 120, 255) : new Color(255, 120, 120, 255), Color.Black, 2);
        if (speed != 1f)
            Text.DrawAnchored("x" + speed.ToString("0.#"), Screen.Width - 16, y + 30, 20, Anchor.Right | Anchor.VCenter, Color.Yellow, Color.Black, 2);
        // a reminder of the controls, once the tutorial has taught them
        if (!edit.Active && !RecipesOpen && !chefMessage.Visible && game.Hints.TutorialDone)
        {
            string help = game.Selected == null
                ? "Click a zombie or your chef, then a stove. Click an empty stove to cook. Click the doorway to open/close."
                : game.Selected.IsPet
                    ? "REST sends the pet home for a nap. Esc to cancel."
                    : "Now click a stove to cook, fetch a dish or clean a pot, or a counter to serve. Esc to cancel.";
            Text.DrawAnchored(help, Screen.Width / 2, Screen.Height - 10, 18, Anchor.Bottom | Anchor.HCenter, new Color(230, 230, 230, 255), Color.Black, 2);
        }
    }

    /// <summary>A HUD button was pressed (HudElement's listeners in GameStateCafe).</summary>
    void HudButtonPressed(int button)
    {
        switch (button)
        {
            case HudLogo:
                // back to the title screen (the cafe is saved on the way out)
                game.Sound(36);
                Quit?.Invoke();
                break;
            case HudStore:
                hudBadge[HudStore] = false;
                edit.OpenStore();
                game.Sound(36);
                break;
            case HudLocker:
                // FUN_000bd7b0 button 14: once its tip has been shown (0x56), the meat locker, its tip ticked (0x59)
                if (!game.Hints.IsShown(0x56))
                    break;
                game.Sound(36);
                game.Hint(0x59, true);
                OpenLocker?.Invoke();
                break;
            case HudPedia:
                // FUN_000bd7b0 button 15: once its tip has been shown (0x58), the ZombiePedia, its tip ticked (0x57)
                if (!game.Hints.IsShown(0x58))
                    break;
                game.Sound(36);
                game.Hint(0x57, true);
                OpenPedia?.Invoke();
                break;
            case HudQuests:
                // FUN_000bd7b0 button 13: once the tip has been shown (0x53), the panel, and its tip ticked (0x55)
                if (!game.Hints.IsShown(0x53))
                    break;
                game.Sound(36);
                game.Hint(0x55, true);
                OpenQuests();
                break;
            case HudZombies:
                // FUN_000bd7b0 button 8: with its badge up, room for another zombie (string 408); else the bio of
                // the zombie picked, or of the first
                game.Sound(36);
                if (hudBadge[HudZombies])
                    messages.Enqueue(("", new List<string> { data.Strings[408] }, null));
                else if (game.Selected is { Character.Role: Role.Staff } picked)
                    OpenBio(picked);
                else if (game.Staff.Count > 0)
                    OpenBio(game.Staff[0]);
                break;
            case HudPets:
                // FUN_000bd7b0 button 9: with its badge up, room for another pet (the code's own words); else the
                // details of the first pet
                game.Sound(36);
                if (hudBadge[HudPets])
                    messages.Enqueue(("", new List<string> { data.Exe.Get(TextPetRoom) }, null));
                else if (game.Pets.Count > 0)
                    OpenBio(game.Pets[0]);
                break;
            case HudCookbook:
                recipeBook.OpenToBrowse();
                game.Hint(0x11, true);
                game.Sound(36);
                break;
            case HudStorage:
                // FUN_000bd7b0 button 7: the storage box
                game.Sound(36);
                edit.OpenStorage();
                break;
            case HudMap:
                // GameStateCafe: off to the map (the cafe is saved on the way out)
                game.Sound(33);
                game.Hint(0x1b, false);
                OpenMap?.Invoke();
                break;
        }
    }

}
