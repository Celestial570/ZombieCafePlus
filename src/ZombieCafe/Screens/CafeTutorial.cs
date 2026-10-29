using System.Numerics;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Data;
using ZombieCafe.Game;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Screens;

/// <summary>The cafe's side of the tutorial: what each hint does (FUN_000b6814) and the HUD buttons it reveals.</summary>
public sealed partial class CafeScreen
{
    ChefMessage chefMessage = null!;
    TutorialArrow arrow = null!;

    // HudElement +0x80: the HUD buttons, by the original's numbering, and which are showing
    const int HudButtons = 28;
    readonly bool[] hudVisible = new bool[HudButtons];
    readonly Rectangle?[] hudRects = new Rectangle?[HudButtons];
    static readonly int[] HudSprites =
        [7, 19, 8, 112, 12, 16, 195, 186, 25, 207, 24, 208, 208, 160, 196, 198, 178, 177, 179, 209, 10, 13, 210, 213, 10, 10, 10, 110];
    /// <summary>
    /// FUN_0010d3e0: the order the grid at the top left fills in. The logo (button 0, always shown by
    /// FUN_0011064c; "tap the Zombie Café logo to return to the Main Menu") leads it here.
    /// </summary>
    static readonly int[] HudGridOrder = [0, 21, 1, 3, 4, 2, 5, 8, 9, 22, 23];
    /// <summary>
    /// FUN_001102e4: the column down the right edge the rest stand in, in this order (objectives, sale, meat locker,
    /// ZombiePedia, storage, toxin drop).
    /// </summary>
    static readonly int[] HudSideOrder = [13, 6, 14, 15, 7, 12];
    /// <summary>The left grid as on an iPod / iPhone: one column (DAT_003750e4[2]), 10 points into the corner (DAT_003750fc[2]).</summary>
    const int HudColumns = 1, HudGridShift = -10;
    const int HudLogo = 0, HudStore = 1, HudMap = 4, HudCookbook = 5, HudStorage = 7, HudZombies = 8, HudPets = 9, HudQuests = 13,
        HudLocker = 14, HudPedia = 15;
    /// <summary>HudElement +0x90: a button's "!" badge (FUN_0011029c; shown only while its button is).</summary>
    readonly bool[] hudBadge = new bool[HudButtons];

    // the last frame's rectangles of things the arrow can point at
    Rectangle? infectButtonRect, restButtonRect, energizeButtonRect, reviveButtonRect;

    void SetupTutorial()
    {
        chefMessage = new ChefMessage(data, game.Sound);
        arrow = new TutorialArrow(data);
        var hints = game.Hints;
        hints.Check = CafeHint;
        hints.Show = text => chefMessage.Show(text);
        hints.Busy = () => chefMessage.Visible;
        game.MusicCue += id => audio?.PlayMusicStinger(id);
        game.HintArrowDone += () => arrow.Hide();
        RefreshHud();
    }

    /// <summary>FUN_0011064c: which HUD buttons the player has got so far.</summary>
    void RefreshHud()
    {
        var h = game.Hints;
        Array.Clear(hudVisible);
        hudVisible[HudLogo] = Quit != null;
        hudVisible[HudStore] = h.IsShown(0xe) || h.IsShown(0xf);
        hudVisible[HudMap] = h.IsShown(0x22) && OpenMap != null;
        hudVisible[HudCookbook] = h.TutorialDone;
        // the zombie list once a zombie has been made (hint 0xb), the storage box from level 2
        hudVisible[HudZombies] = h.IsShown(0xb);
        hudVisible[HudStorage] = game.Player.Level > 1;
        // the bonus quests once their tip has been shown
        hudVisible[HudQuests] = h.IsShown(0x53);
        // the pets from level 5, the ZombiePedia from level 8 (FUN_0011064c)
        hudVisible[HudPets] = game.Player.Level > 4;
        hudVisible[HudPedia] = game.Player.Level > 7 && OpenPedia != null;
        hudVisible[HudLocker] = game.Player.Level > 8 && OpenLocker != null;
        // the others (fridge, zombie list, storage, ...) open screens the port does not have yet
    }

    /// <summary>The virtual-screen rectangle of a HUD button while it shows.</summary>
    Rectangle? HudButton(int i) => hudVisible[i] ? hudRects[i] : null;

    /// <summary>
    /// FUN_000b6814: the cafe's say over each hint as it comes up (true: show it now). Most also move the
    /// tutorial arrow. Hints about parts of the game the port does not have yet are let through silently,
    /// so that nothing waits for them.
    /// </summary>
    bool CafeHint(int hint)
    {
        var h = game.Hints;
        var chefActor = game.ChefActor;
        Furniture? First(FurnitureCategory c) => cafe.FindFurniture(c, -1).FirstOrDefault();
        void PointAt(CafeObject? o)
        {
            if (o != null)
                arrow.PointAt(o);
            else
                arrow.Hide();
        }

        switch (hint)
        {
            case 0:  // welcome: the chef
            case 5:  // the dish is on its way: pick the chef again
                PointAt(chefActor);
                return true;
            case 1:  // chef picked: a stove
            case 4:
                PointAt(First(FurnitureCategory.Stove));
                return true;
            case 3:  // the food is ready: the first customer comes in
                h.MarkShown(0x37);
                h.MarkShown(0x38);
                PointAt(chefActor);
                if (!cafe.IsOpen)
                {
                    cafe.SetOpen(true);
                    game.SpawnTutorialCustomer();
                }
                return true;
            case 0x3c: // chef picked again: the serving counter
                PointAt(First(FurnitureCategory.Counter));
                return true;
            case 6:
            case 0xa:
            case 0xd:
            case 0x10:
            case 0x18:
            case 0x20:
            case 0x23:
            case 0x2e:
            case 0x38:
            case 0x49:
            case 0x54:
            case 0x57:
            case 0x59:
                arrow.Hide();
                return true;
            case 7:  // the recipe list is open: its first recipe
                arrow.PointAt(() => recipeBook.CookButton);
                return true;
            case 8:  // time to get help: the customer
                PointAt(game.TutorialCustomer);
                return true;
            case 9:  // the customer is picked: the infect button
                game.TutorialCustomer = null;
                if (game.Selected?.Character.Role != Role.Customer)
                    return false;
                arrow.PointAt(() => infectButtonRect);
                return true;
            case 0xb:  // the first zombie: a music cue
                audio?.PlayMusicStinger(2);
                return true;
            case 0xe:  // it worked: the store button appears
            case 0xf:
                if (h.IsShown(hint == 0xe ? 0xf : 0xe))
                    return false;
                hudVisible[HudStore] = true;
                arrow.PointAt(() => HudButton(HudStore), flipped: true);
                return true;
            case 0x13:
                return !h.IsShown(0x14);
            case 0x16: // open for business: another customer
                game.SpawnTutorialCustomer();
                arrow.Hide();
                return true;
            case 0x17: // a zombie is daydreaming
            {
                if (edit.Active || RecipesOpen)
                    return false;
                var dreamer = game.Staff.FirstOrDefault(z => z.Character.Job == Job.Daydreaming);
                if (dreamer != null)
                    arrow.PointAt(dreamer);
                return true;
            }
            case 0x1d: // a zombie went for a customer: pick it, point at REST
                if (game.LastAttacker is { } attacker && game.Staff.Contains(attacker))
                {
                    game.Selected = attacker;
                    arrow.PointAt(() => restButtonRect);
                }
                return true;
            case 0x1f: // try toxin: one on the house, point at ENERGIZE
                game.Player.Toxin += 1;
                arrow.PointAt(() => energizeButtonRect);
                return true;
            case 0x21: // cook another dish
                if (game.Player.Level > 1)
                    PointAt(chefActor);
                return true;
            case 0x22: // "we're not the only restaurant in town": the map button appears
                if (OpenMap == null)
                    return SilentlyShown(hint);
                hudVisible[HudMap] = true;
                arrow.PointAt(() => HudButton(HudMap), flipped: true);
                return true;
            case 0x2c: // food from a raid: the fridge (none in the cafe: not now)
            {
                var fridgeFurniture = First(FurnitureCategory.Fridge);
                if (fridgeFurniture == null)
                    return false;
                arrow.PointAt(fridgeFurniture);
                return true;
            }
            case 0x2d: // the fridge is open: turn to the first dish whose recipe is new, and point at its UNLOCK
            {
                if (!fridge.Visible)
                {
                    h.MarkShown(0x2e);
                    return true;
                }
                var stock = game.Player.Fridge;
                int first = stock.FindIndex(d => d.OrderedBy.Length == 0 && !game.Player.HasRecipe(d.Food, d.Modifier));
                if (first < 0)
                {
                    arrow.Hide();
                    return false;
                }
                fridge.ShowPageOf(first);
                int slot = first & 1;
                arrow.PointAt(() => fridge.Visible ? fridge.UnlockRects[slot] : null);
                return true;
            }
            case 0x36: // finishing a dish with toxin: a stove
                PointAt(First(FurnitureCategory.Stove));
                return true;
            case 0x37: // the cook command bar is up: its FINISH button
                arrow.PointAt(() => finishButtonRect);
                return true;
            case 0x48: // a dead plant
                if (game.Player.Level > 3)
                {
                    h.MarkShown(0x48);
                    h.MarkShown(0x49);
                }
                return false;
            case 0x4a:
                return game.Player.Level >= 5;
            case 0x4d: // an empty zombie slot: a customer inside
            {
                var inside = game.Customers.FirstOrDefault(c => cafe.IsInside(c.Cell) && !c.Character.IsZombie)
                    ?? game.Customers.LastOrDefault();
                if (inside != null)
                    arrow.PointAt(inside);
                return true;
            }
            case 0x4e:
                // level 7: the store opens on its SPECIAL tab, where the expansions are (FUN_0012a1a4(store, 5))
                if (game.Player.Level < 7)
                    return false;
                edit.OpenStore(5);
                return true;
            case 0x4f:
                arrow.PointAt(() => infectButtonRect);
                return true;
            case 0x50:
                if (h.IsShown(0x4f))
                {
                    arrow.Hide();
                    // the map button and "we're not the only restaurant in town"
                    game.Hint(0x19, false);
                }
                return true;
            case 0x51: // time to open: the doorway (over the middle of the door cell's outer edge)
                h.MarkShown(0x52);
                arrow.PointAt(cafe.CellCenter(cafe.DoorCell) + new Vector2(40, -25 - 130));
                return true;
            case 0x53: // the bonus quests, from level 6: their HUD button appears with the arrow on it
                if (game.Player.Level < 6)
                    return false;
                hudVisible[HudQuests] = true;
                arrow.PointAt(() => HudButton(HudQuests), flipped: true);
                return true;
            case 0x55:
                if (game.Player.Level == 2 && !h.IsShown(0x16))
                    PointAt(First(FurnitureCategory.Stove));
                return true;
            case 0x56: // the meat locker, from level 9: its HUD button with the arrow on it
                if (game.Player.Level < 9 || OpenLocker == null)
                    return false;
                hudVisible[HudLocker] = true;
                arrow.PointAt(() => HudButton(HudLocker), flipped: true);
                return true;
            case 0x58: // the ZombiePedia, from level 8: its HUD button with the arrow on it
                if (game.Player.Level < 8 || OpenPedia == null)
                    return false;
                hudVisible[HudPedia] = true;
                arrow.PointAt(() => HudButton(HudPedia), flipped: true);
                return true;
            // friends and the fridge's orders are not in the port yet
            case 0x27:
            case 0x28:
            case 0x29:
            case 0x2a:
            case 0x2b:
            case 0x2f:
            case 0x30:
            case 0x31:
            case 0x33:
            case 0x34:
            case 0x35:
            case 0x39:
            case 0x3a:
            case 0x3b:
            case 0x42:
            case 0x43:
            case 0x45:
                return SilentlyShown(hint);
            default:
                return true;
        }
    }

    /// <summary>
    /// Tips about screens the port does not have yet - friends (0x30 .. 0x34), the meat locker (0x56), the
    /// ZombiePedia (0x58) - which any screen marks read without showing when they come up.
    /// </summary>
    public static bool IsUnported(int hint) => hint is 0x30 or 0x31 or 0x32 or 0x33 or 0x34;

    /// <summary>A hint about something the port does not have: mark it read without showing it.</summary>
    bool SilentlyShown(int hint)
    {
        game.Hints.MarkShown(hint);
        return false;
    }

    /// <summary>Virtual-screen point of a cafe point.</summary>
    Vector2 WorldToScreen(Vector2 world) => Screen.FromPixels(Vector2.Transform(world, WorldToWindow));

    /// <summary>The level-up box was put away: that level's tip (LevelUpDialog::vf02).</summary>
    void LevelUpDismissed(int level)
    {
        switch (level)
        {
            case 2: game.Hint(0x4f, true); break;
            case 4: game.Hint(0x50, false); break;
            case 5: game.Hint(0x51, false); break;
            case 6: game.Hint(0x52, false); break;
            case 7: game.Hint(0x54, false); break;
        }
    }

    /// <summary>GameStateCafe::update: the tips that come with levels, every frame.</summary>
    void LevelTips()
    {
        var h = game.Hints;
        int level = game.Player.Level;
        if (level > 5 && !h.IsShown(0x53))
            game.Hint(0x52, false);
        if (level > 7 && !h.IsShown(0x58))
            game.Hint(0x56, false);
        if (level > 8 && !h.IsShown(0x56))
            game.Hint(0x58, false);
    }
}
