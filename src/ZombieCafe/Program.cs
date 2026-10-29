using ZombieCafe.Assets;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Screens;

namespace ZombieCafe;

static class Program
{
    static int Main(string[] args)
    {
        string? ipaPath = Arg(args, "--ipa") ?? FindIpa();
        var ipa = ipaPath != null ? IpaArchive.TryOpen(ipaPath) : null;
        if (ipa == null)
        {
            const string missing = "Zombie Cafe needs the original game: put your Zombie Cafe .ipa next to ZombieCafe.exe, or run it with --ipa <path to the .ipa>.";
            Console.Error.WriteLine(missing);
            // the release build has no console to print to
            if (OperatingSystem.IsWindows())
                MessageBoxW(IntPtr.Zero, missing, "Zombie Cafe", 0x10);
            return 1;
        }

        if (Environment.GetEnvironmentVariable("ZC_INPUT") is { Length: > 0 } inputScript)
            Core.Input.Load(inputScript);
        var app = new App(Arg(args, "--shot"), int.TryParse(Arg(args, "--frames"), out int f) ? f : 5);
        // screenshot/test runs neither load nor overwrite the player's save
        testRun = Arg(args, "--shot") != null || args.Contains("--nosave");
        using var data = new GameData(ipa);
        // --dump furniture|foods (development): a data table as text, then out
        if (Arg(args, "--dump") is { } table)
        {
            Dump(data, table);
            return 0;
        }
        Gfx.Text.Load(ipa.Read("A Love of Thunder.ttf"));
        // the boxes and questions in the original's art (the dialog sheet's chalkboards, the in-game sheet's bars)
        Ui.Widgets.Skin = (data.Sheet("ipad_dialogImages", "ipad_dialogOffsets.bin"), data.IngameUi);
        SetWindowIcon(ipa);
        // ZC_MEASURE=text (development): the text's width at the game's font sizes, against the original bitmap fonts
        if (Environment.GetEnvironmentVariable("ZC_MEASURE") is { Length: > 0 } measure)
        {
            string[] fonts = ["thunder_12pt", "thunder_14pt", "thunder_16pt", "thunder_20pt", "thunder_24pt", "thunder_28pt", "thunder_36pt"];
            for (int i = 0; i < fonts.Length; i++)
            {
                var bm = BmFont.Parse(ipa.Read(fonts[i] + ".fnt"));
                int bmWidth = measure.Sum(ch => bm.Glyphs.TryGetValue(ch, out var g) ? g.XAdvance : 0);
                Console.WriteLine($"{fonts[i]}: size {Gfx.Text.GameFont[i]}: ttf {Gfx.Text.Measure(measure, Gfx.Text.GameFont[i]).W:0.0}, bitmap {bmWidth}");
            }
        }
        using var audio = new Audio.AudioSystem(ipa);
        app.Audio = audio;
        settings = testRun ? new Settings { ReadOnly = true } : Settings.Load();
        // ZC_HUD=percent (tests): the HUD size
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_HUD"), out int hud))
            settings.HudSize = Math.Clamp(hud, Settings.HudSizeMin, Settings.HudSizeMax);
        Settings.Current = settings;
        // the settings file shows every option, the extras' new ones too
        settings.Save();
        // Two Point extras: the Toxin Generator in the store for cash (the original sold it for real money)
        foreach (var d in data.Furniture)
            if (d.Category == FurnitureCategory.ToxinGenerator && d.ForSale && d.Currency == 2)
            {
                d.Price = settings.ToxinGeneratorPrice;
                d.Currency = 0;
            }
        audio.MusicVolume = settings.MusicVolume / 100f;
        audio.EffectsVolume = settings.SfxVolume / 100f;
        GameScreen first = Arg(args, "--viewer") switch
        {
            "anim" => new AnimViewerScreen(data, args),
            "sheet" => new SheetViewerScreen(data, args),
            // test runs go straight to the cafe unless they ask for the title screen (--menu)
            _ => testRun && !args.Contains("--menu") ? CafeFor(data, audio, args, StartGame(data, args)) : Title(data, audio, args),
        };
        app.Run(first);
        return 0;
    }

    static bool testRun;
    static Settings settings = new();

    /// <param name="fromTitle">Reached from the title screen: the HUD's logo goes back to it.</param>
    static CafeScreen CafeFor(GameData data, Audio.AudioSystem audio, string[] args, CafeController game, bool fromTitle = false) =>
        new(data, game, audio)
        {
            Autosave = !testRun, SaveTo = Arg(args, "--saveto"),
            Quit = fromTitle ? () => App.Current.SetScreen(Title(data, audio, args)) : null,
            OpenMap = () => App.Current.SetScreen(MapFor(data, audio, args, game, fromTitle)),
            OpenPedia = () => App.Current.SetScreen(PediaFor(data, audio, args, game, fromTitle)),
            OpenLocker = () => App.Current.SetScreen(LockerFor(data, audio, args, game, fromTitle)),
            // the cheat menu's time skip: the game's clock moves on and the cafe is caught up for the time, as on
            // coming back to it ("Welcome back" and all)
            SkipTime = ms =>
            {
                GameClock.OffsetMs += ms;
                var save = SaveGame.Capture(game);
                save.SavedAtUnixMs -= ms;
                var (skipped, away) = SaveGame.Restore(data, save, catchUp: true, rivalsAway: true);
                skipped.Away = away;
                App.Current.SetScreen(CafeFor(data, audio, args, skipped, fromTitle));
            },
            Raid = (name, level, type) => StartRaid(data, audio, args, game, fromTitle, name, level, type, 0),
        };

    /// <summary>GameStateMap from the cafe's HUD: BACK (or the player's own cafe) goes back to the cafe.</summary>
    static MapScreen MapFor(GameData data, Audio.AudioSystem audio, string[] args, CafeController game, bool fromTitle)
    {
        MapScreen? map = null;
        map = new MapScreen(data, game, audio,
            back: () => App.Current.SetScreen(CafeFor(data, audio, args, Returned(data, game, map!.ElapsedMs), fromTitle)),
            raid: (name, level, type) => StartRaid(data, audio, args, game, fromTitle, name, level, type, map!.ElapsedMs));
        return map;
    }

    /// <summary>GameStateMeatLocker from the cafe's HUD: BACK goes back to the cafe.</summary>
    static MeatLockerScreen LockerFor(GameData data, Audio.AudioSystem audio, string[] args, CafeController game, bool fromTitle)
    {
        MeatLockerScreen? locker = null;
        locker = new MeatLockerScreen(data, game, audio,
            back: () => App.Current.SetScreen(CafeFor(data, audio, args, Returned(data, game, locker!.ElapsedMs), fromTitle)));
        return locker;
    }

    /// <summary>GameStateZombiePedia from the cafe's HUD: its X goes back to the cafe.</summary>
    static ZombiePediaScreen PediaFor(GameData data, Audio.AudioSystem audio, string[] args, CafeController game, bool fromTitle)
    {
        ZombiePediaScreen? pedia = null;
        pedia = new ZombiePediaScreen(data, game, audio,
            back: () => App.Current.SetScreen(CafeFor(data, audio, args, Returned(data, game, pedia!.ElapsedMs), fromTitle)));
        return pedia;
    }

    /// <summary>
    /// GameStateRaid, from the map or the competitor dialog. Its results' OK goes back to the cafe (which catches up
    /// on the time spent away); the logo leaves for the title screen, the cafe saved as the raid left it.
    /// </summary>
    static void StartRaid(GameData data, Audio.AudioSystem audio, string[] args, CafeController game, bool fromTitle,
        string name, int level, int type, long awayMs)
    {
        var raid = new RaidController(data, game, name, level, type);
        RaidScreen? screen = null;
        CafeController Home()
        {
            if (!raid.Ended)
                raid.ComeHome();
            var back = Returned(data, game, awayMs + screen!.ElapsedMs);
            if (!testRun)
                SaveGame.Save(back);
            return back;
        }
        screen = new RaidScreen(data, raid, audio,
            done: _ => App.Current.SetScreen(CafeFor(data, audio, args, Home(), fromTitle)),
            quit: fromTitle ? () =>
            {
                Home();
                App.Current.SetScreen(Title(data, audio, args));
            } : null);
        App.Current.SetScreen(screen);
    }

    /// <summary>
    /// GameStateCafe::load after another screen: the cafe as it was left, with the time spent away run through
    /// (FUN_000b55d0) - the rivals' clocks kept running meanwhile, so they are left alone.
    /// </summary>
    static CafeController Returned(GameData data, CafeController game, long awayMs)
    {
        var save = SaveGame.Capture(game);
        save.SavedAtUnixMs -= awayMs;
        return SaveGame.Restore(data, save, catchUp: true, rivalsAway: false).Game;
    }

    /// <summary>ZombieCafe::loadStep's last step: the title screen, with the theme playing.</summary>
    static MainMenuScreen Title(GameData data, Audio.AudioSystem audio, string[] args) =>
        new(data, audio, play: () => Play(data, audio, args),
            options: () => App.Current.SetScreen(new OptionsScreen(data, audio, settings, back: () => App.Current.SetScreen(Title(data, audio, args)))),
            help: () => App.Current.SetScreen(new HelpScreen(data, audio, back: () => App.Current.SetScreen(Title(data, audio, args)))));

    /// <summary>
    /// GameStateMainMenu::update after PLAY: a player with a cafe goes back to it; one who has not picked a chef
    /// yet (game +0x1c1 = -1, a new game) chooses one first (GameStateChooseChef), and BACK returns here.
    /// </summary>
    static void Play(GameData data, Audio.AudioSystem audio, string[] args)
    {
        if (Saved(data, args) is { } game)
        {
            App.Current.SetScreen(CafeFor(data, audio, args, game, fromTitle: true));
            return;
        }
        App.Current.SetScreen(new ChooseChefScreen(data, audio,
            chosen: chef => App.Current.SetScreen(CafeFor(data, audio, args, NewGame(data, chef), fromTitle: true)),
            back: () => App.Current.SetScreen(Title(data, audio, args))));
    }

    /// <summary>The saved game to continue, if there is one and a new game was not asked for.</summary>
    static CafeController? Saved(GameData data, string[] args)
    {
        if (Arg(args, "--load") is { } file && SaveGame.TryRead(file) is { } loaded)
            return Continue(SaveGame.Restore(data, loaded, !args.Contains("--noaway")));
        if (!testRun && !args.Contains("--new") && SaveGame.TryRead() is { } save)
            return Continue(SaveGame.Restore(data, save));
        return null;
    }

    /// <summary>Continues the saved game unless asked for a new one (--new) or running a test.</summary>
    static CafeController StartGame(GameData data, string[] args) => Saved(data, args) ?? NewGame(data, null);

    static CafeController Continue((CafeController Game, OfflineProgress.Summary? Away) r)
    {
        if (r.Away is { } a)
        {
            Console.WriteLine($"Welcome back: {a.ElapsedMs / 60000} min away, {a.Served} customers served (${a.Income} + ${a.Tips} tips), {a.Hungry} left hungry.");
            r.Game.Away = a;
        }
        return r.Game;
    }

    /// <summary>
    /// A new game: the dirty starting cafe with the chosen chef waiting by the door for the tutorial. Test runs
    /// that skip the title screen get the first of the free chefs (ZC_CHEF=n picks another character type).
    /// </summary>
    static CafeController NewGame(GameData data, CharacterDef? chef)
    {
        var cafe = Cafe.CreateDefault(data, new Random(1));
        cafe.AddStartingFurniture();
        if (chef == null && int.TryParse(Environment.GetEnvironmentVariable("ZC_CHEF"), out int chefIndex)
            && chefIndex >= 0 && chefIndex < data.Characters.Count)
            chef = data.Characters[chefIndex];
        chef ??= data.Characters.FirstOrDefault(c => c.B39 != 0 && c.I34 == 0) ?? data.Characters[0];
        var game = new CafeController(data, cafe, new PlayerState(data, chef));
        int u0 = cafe.OriginU, v0 = cafe.OriginV;
        if (Environment.GetEnvironmentVariable("ZC_SKIPTUTORIAL") == "1")
        {
            // tests: as the tutorial leaves things, with one infected customer on the staff
            foreach (int h in Enumerable.Range(0, 0x17).Concat([0x21, 0x3c, 0x4d, 0x4f, 0x50, 0x51, 0x52]))
                game.Hints.MarkShown(h);
            game.AddStaff(0, cafe.IndexOf(u0 - 4, v0 + 4));
        }
        else
        {
            // GameStateCafe::load before the tutorial is done: the chef waits by the door of the shut cafe
            cafe.PlaceOnCell(game.ChefActor, cafe.DoorCell);
            game.ChefActor.SetAnimation(CharAnim.Idle, (Facing)1, true);
        }
        // ZC_ZOMBIES=288,289 (tests): zombies of these types join the staff too
        foreach (var z in (Environment.GetEnvironmentVariable("ZC_ZOMBIES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(z, out int zombieDef) && zombieDef >= 0 && zombieDef < data.Characters.Count)
                game.AddStaff(zombieDef, cafe.IndexOf(u0 - 2 - game.Staff.Count % 4, v0 + 2));
        if (Environment.GetEnvironmentVariable("ZC_TEST_SEATS") == "1")
        {
            // test layout: a second table with chairs facing it from the other three sides
            var k = data.K;
            cafe.AddFurniture(k.Int(0x42d4f8), u0 - 2, v0 + 5);
            cafe.AddFurniture(k.Int(0x42d4fc), u0 - 1, v0 + 5).SetOrientation(1);
            cafe.AddFurniture(k.Int(0x42d4fc), u0 - 2, v0 + 6).SetOrientation(2);
            cafe.AddFurniture(k.Int(0x42d4fc), u0 - 3, v0 + 5).SetOrientation(3);
        }
        // ZC_ROOM=w,d (tests): the room expanded to this size, as the store's expansion would
        if (Environment.GetEnvironmentVariable("ZC_ROOM")?.Split(',') is [var rw, var rd]
            && int.TryParse(rw, out int roomW) && int.TryParse(rd, out int roomD)
            && data.Furniture.FirstOrDefault(d => d.Category == FurnitureCategory.Expansion && d.Width == roomW && d.Depth == roomD) is { } expansion)
            game.Expand(expansion);
        if (Environment.GetEnvironmentVariable("ZC_TEST_CASH") == "1")
        {
            // test layout: a vending machine with ten dollars in it
            var vending = cafe.AddFurniture(8, u0 - 2, v0 + 5);
            vending.Stored = 10;
        }
        if (game.Hints.TutorialDone)
            cafe.SetOpen(true);
        // ZC_STAFF_ENERGY=n (tests): the staff start with this much energy (a hungry zombie)
        if (float.TryParse(Environment.GetEnvironmentVariable("ZC_STAFF_ENERGY"), out float energy))
            foreach (var z in game.Staff)
                z.Character.Energy = energy;
        // ZC_HINTS=0x22,0x25 (tests): these hints already shown (the map unlocked, the first raid done, ...)
        foreach (var hint in (Environment.GetEnvironmentVariable("ZC_HINTS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (hint.Trim() is var h && (h.StartsWith("0x") ? int.TryParse(h[2..], System.Globalization.NumberStyles.HexNumber, null, out int id) : int.TryParse(h, out id))
                && id >= 0 && id < HintManager.Count)
                game.Hints.MarkShown(id);
        // ZC_STEALING=1 (tests): the rival across the street has started taking customers
        if (Environment.GetEnvironmentVariable("ZC_STEALING") == "1")
        {
            game.Player.Neighbours.Stealing = true;
            game.Player.Neighbours.Penalty = data.K.Float(0x42d5c0);
        }
        // ZC_BOSS=1 (tests): a boss restaurant is in town
        if (Environment.GetEnvironmentVariable("ZC_BOSS") == "1")
        {
            game.Player.Neighbours.BossTimer = data.K.Int(0x42d59c) - 1;
            game.Player.Neighbours.BossDay = GameClock.Now;
        }
        // ZC_SHUT=level (tests): that rival restaurant raided and shut for its four hours
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_SHUT"), out int shutLevel) && shutLevel > 0)
            game.Player.Neighbours.Timers[Neighbourhood.SlotOf(shutLevel)] = data.K.Ints(0x42d5b8)[0];
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_TOXIN"), out int toxin) && toxin >= 0)
            game.Player.Toxin = toxin; // test override
        // ZC_FRIDGE=food[:variant],... (tests): batches in the fridge to start with
        if (Environment.GetEnvironmentVariable("ZC_FRIDGE") is { Length: > 0 } stock)
            foreach (var item in stock.Split(','))
            {
                var p = item.Split(':');
                if (int.TryParse(p[0], out int food) && food >= 0 && food < data.Foods.Count)
                    game.Player.Fridge.Add(Dish.Cook(data, food, p.Length > 1 && byte.TryParse(p[1], out byte v) && v < 10 ? v : (byte)0));
            }
        // ZC_MONEY=n (tests): the cash to start with
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_MONEY"), out int money) && money >= 0)
            game.Player.Money = money;
        // ZC_DAYS=n (tests): the cafe last played yesterday, n days in a row (the Frequent Fryer pays the next day's)
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_DAYS"), out int days) && days >= 0)
        {
            game.Player.LastPlayDay = GameClock.Today.AddDays(-1);
            game.Player.StreakDays = days;
        }
        // ZC_AMPTRON=n (tests): the Amptron's summons left today (a new game has none until the next day)
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_AMPTRON"), out int amptron) && amptron >= 0)
            game.Player.AmptronUses = Math.Min(amptron, 3);
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_LEVEL"), out int level) && level > 0)
        {
            game.Player.Level = level; // test override
            game.Player.Neighbours.CurrentLevel = level;
            game.Player.GrantRecipesUpToLevel(data);
        }
        // ZC_XP=n: experience to start with (enough for the next level levels up on the first frame)
        if (int.TryParse(Environment.GetEnvironmentVariable("ZC_XP"), out int xp) && xp > 0)
            game.Player.Xp = xp;
        // ZC_PLACE=id:du:dv,...: furniture to stand in the cafe, cells relative to the room's origin
        if (Environment.GetEnvironmentVariable("ZC_PLACE") is { Length: > 0 } place)
        {
            foreach (var item in place.Split(','))
            {
                var f = item.Split(':');
                if (f.Length == 3 && int.TryParse(f[0], out int id) && int.TryParse(f[1], out int du) && int.TryParse(f[2], out int dv)
                    && id >= 0 && id < data.Furniture.Count)
                {
                    var placed = game.Cafe.AddFurniture(id, game.Cafe.OriginU + du, game.Cafe.OriginV + dv);
                    game.Player.Boosts.Refill(id);
                    // a habitat comes with its pet, as one bought does
                    if (placed is Game.Cafe.Habitat home)
                        game.PetPurchased(home);
                }
            }
        }
        return game;
    }

    /// <summary>Looks for a Zombie Cafe .ipa next to the exe and in the folders above it.</summary>
    static string? FindIpa()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            try
            {
                foreach (var f in dir.EnumerateFiles("*.ipa"))
                    if (f.Name.Contains("zombie", StringComparison.OrdinalIgnoreCase))
                        return f.FullName;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    static void Dump(GameData data, string table)
    {
        switch (table)
        {
            case "furniture":
                foreach (var d in data.Furniture)
                    Console.WriteLine($"{d.Id}\t{d.Category}\ttab {d.ShopTab}\tsale {d.ForSale}\tcur {d.Currency}\t{d.Price}\ttheme {d.Theme}\t{d.Width}x{d.Depth}\trot {d.B1C}\tpet {d.PetId}\tfam {d.StoveFamily}\tb42 {d.B42}\ts44 {d.S44}\tearns {d.B24}:{d.S26}/h cap {d.S28}\t{d.Name}");
                break;
            case "chars":
                foreach (var c in data.Characters)
                    Console.WriteLine($"{c.Index}	kind {c.Kind}	weight {c.B04}	lvl {c.B20}	rating {c.F00:0.##}	b50 {c.B50}	{c.Name}");
                break;
            case "foods":
                foreach (var f in data.Foods)
                    Console.WriteLine($"{f.Index}\tlvl {f.Level}\tkind {f.Kind}\tbook {f.Cookbook}\tb23 {f.B23}\tfam {f.StoveFamily}\ts20 {f.S20}\t{f.Name}");
                break;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    /// <summary>The window's icon: the app's own (its 76-point iPad icon), read from the .ipa like everything else.</summary>
    static unsafe void SetWindowIcon(IpaArchive ipa)
    {
        if (ipa.TryRead("AppIcon76x76~ipad.png") is not { } png)
            return;
        var icon = Raylib_cs.Raylib.LoadImageFromMemory(".png", png);
        if (icon.Data == null)
            return;
        Raylib_cs.Raylib.ImageFormat(ref icon, Raylib_cs.PixelFormat.UncompressedR8G8B8A8);
        Raylib_cs.Raylib.SetWindowIcon(icon);
        Raylib_cs.Raylib.UnloadImage(icon);
    }

    public static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
