# Zombie Cafe Plus: classic version

A Windows port of Zombie Café 1.7.0 for iOS. It runs the game from your own copy of the iOS app: all pictures, sounds, text and game data are read from the `.ipa` file each time it starts, so nothing from the original is included here.

This is an unofficial fan project, not made or endorsed by Capcom. You need your own copy of the game's `.ipa`.

This branch is the classic version, kept as close to the original as it can be. Zombie Cafe Plus, with the extras (staff panel, undo, copy and paste, bigger cafes, in-game toxin, cheat menu), is on the `two-point` branch.

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
| Cheat menu (game speed, toxin, cash, level up, finish cooking, rest the staff, all recipes, skip the tutorial, and a Time page to skip hours or days) | F1 or the ` key |

## Where things are kept

- Saved game: `%APPDATA%\ZombieCafePC\save.json`
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
