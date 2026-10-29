# Zombie Café PC

A Windows port of Zombie Café 1.7.0 for iOS, with some extras borrowed from the Two Point games (see below). It runs the game from your own copy of the iOS app: all pictures, sounds, text and game data are read from the `.ipa` file each time it starts, so nothing from the original is included here.

## Running it

1. Put your `ZombieCafe 1.7.0.ipa` in the same folder as `ZombieCafe.exe` (a folder above it works too; the file name must contain "zombie").
2. Start `ZombieCafe.exe`. To keep the .ipa somewhere else, start it with `ZombieCafe.exe --ipa "C:\Games\ZombieCafe 1.7.0.ipa"`.

The window can be resized; the interface keeps its layout at any size.

## Controls

| Action | How |
| --- | --- |
| Tap | Left click |
| Move the view | Right-drag, drag an empty spot, or WASD / arrow keys |
| Zoom | Mouse wheel |
| Rotate an item while placing it | R |
| Keep placing copies of an item | Hold Shift when you place it |
| Let go of the picked character (or the item in hand while decorating) | Right-click, click an empty spot, or Esc |
| Close a box or go back | Esc |
| Save now | F5 (the game also saves every minute and when you leave) |
| HUD size (60% to 140%, also on the Options screen) | Ctrl + = bigger, Ctrl + - smaller, Ctrl + 0 back to 100% |
| Staff panel: everyone's energy and job, with SERVE / COOK / REST orders, auto break and FIND | J, or the STAFF button at the bottom right |
| Pick out several pieces while decorating | Ctrl+click them, or Ctrl+drag a box over them |
| Move the picked-out pieces together | Click one of them (or MOVE) |
| Copy the picked-out pieces / paste them as new purchases | Ctrl+C / Ctrl+V (or COPY / PASTE); hold Shift when placing to keep pasting |
| Salvage the picked-out pieces | Delete (or the SALVAGE button) |
| Undo what you placed, moved, stored, salvaged or painted | Ctrl+Z (or UNDO), until you leave decorating |
| Cheat menu (game speed, toxin, cash, level up, finish cooking, rest the staff, all recipes, skip the tutorial, a Time page to skip hours or days, and a Send customer page to pick which customers come in) | 0 (top row or number pad) |

## Two Point extras

- **Staff panel.** It lists the chef and every zombie with level, energy and current job.
  - SERVE puts someone on serving duty.
  - COOK sends them to the nearest stove that needs someone: a burnt pot, a finished dish, or cooking with nobody at it.
  - REST and WORK send a zombie out back to rest or back to work.
  - FIND picks them out in the café.
  - AUTO BREAK makes a zombie rest by itself when its energy drops to a quarter, and go back to serving when it's full.
- **Several pieces at once.** While decorating, pick out pieces with Ctrl+click or a Ctrl+drag box. Then move, copy, put in storage or salvage them together.
- **Copy and paste.** A copied layout, such as a table with its chairs, is pasted as new purchases. The bar shows the total price.
- **Undo.** Undo works on everything you placed, moved, stored, salvaged or painted since you started decorating.
  - Undoing a purchase refunds it in full.
  - If its experience already pushed the café up a level, you get the salvage price instead.

- **Bigger cafés.** The store's Special tab goes past the original 17x16, with six more sizes up to 26x25 ($300,000 to $800,000, or 90 to 175 toxin). The map gains extra rows at the bottom to make room; existing saves are unaffected.
- **Getting toxin.** The original sold toxin for real money; this version has two in-game ways instead:
  - **Toxin exchange.** The + next to your toxin count sells vials for cash, a few a day.
  - **Toxin Generator.** Sold in the store for cash from level 5. It sits in the yard, fills up with vials over the day (it holds 5), and you click it to collect them.

  You can change the price, the daily limit and the generator settings in `settings.json` (next to the save; see below). The game writes the file when it starts. Set a limit to 0 for no limit.

  | Setting | Default | What it does |
  | --- | --- | --- |
  | `ToxinPrice` | 2000 | Dollars per vial at the exchange (the original's own rate) |
  | `ToxinDailyLimit` | 15 | Vials the exchange sells per day; it resets at midnight on the game clock |
  | `ToxinGeneratorPrice` | 40000 | The generator's price in the store |
  | `ToxinGeneratorLimit` | 6 | How many generators you can own, counting ones in storage |
  | `ToxinGeneratorPerDay` | 5 | Vials each generator makes per day |

## Where things are kept

- Saved game: `%APPDATA%\ZombieCafePC-TwoPoint\save.json`; options, including the toxin settings, are in `settings.json` in the same folder (the classic version keeps its own in `%APPDATA%\ZombieCafePC`)
- Photos taken with the chef's PHOTO button: `Pictures\ZombieCafePC`

## Not included

The online parts of the original need Capcom's servers or the App Store, so they are left out: friends and visiting their cafés, Facebook, buying toxin or boosters with real money, and video ads. The daily Frequent Fryer bonus uses your computer's clock instead of the game's server.

## Building it

With the .NET 8 SDK:

```
dotnet publish src/ZombieCafe -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

This makes `dist/ZombieCafe.exe`, a single file that needs nothing else installed. Put the .ipa next to it.

For development, `dotnet build src/ZombieCafe` makes a Debug build with a console window, and `tests/run.sh` replays the scripted tests in `tests/` (each writes a screenshot to `shots/tests/`).
