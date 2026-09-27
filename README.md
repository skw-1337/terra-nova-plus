# Terra Nova Plus

**An all-in-one quality-of-life pack for *Terra Nova: Strike Force Centauri*** (Looking Glass Technologies, 1996), running in DOSBox — **Steam, GOG or standalone**.

One small external tool. Pick your options in a menu, press Enter, and the game starts with them:

| Option | Key in game | What it does |
|---|---|---|
| **Mouse freelook** | `Y` | Mouse left/right turns your PBA, up/down looks up/down, the aiming reticle stays centred: you fire where you look. Smooth (~500 updates per second), no screen shake, no reticle trails on the cockpit. |
| **Noclip** | `U` | Free flight: your usual move keys, `Space` / `Left Ctrl` up/down, `Left Shift` ×4 speed. Great to explore the maps and look at the scenery. |
| **View distance** | `I` | `NORMAL` / `FAR` / `MAX`: the fog is pushed back, the horizon opens up. |
| **Force 320×400** | — | The engine's best resolution, applied automatically in every mission (the game normally only keeps it in save files). |
| **Widescreen 16:9** | — | DOSBox stretches the picture to 16:9 and the tool widens the engine's field of view by 33 % to match: correct proportions, more of the battlefield on the sides. |

**No game file is modified**: everything happens in memory while you play, and stops when you quit.

## Download & use

1. Download `TerraNovaPlus_v1.0.1.zip` from the [Releases](../../releases) page and unzip it anywhere.
2. Run **`TNPlus.exe`**. A menu appears:

   ```
     1  Mouse freelook (key Y)                            [ON]
     2  Noclip (key U)                                    [ON]
     3  View distance at start (key I)                    [MAX]
     4  Force 320x400 (the engine's best resolution)      [ON]
     5  Widescreen 16:9 (game launched from here)         [off]

     G  Game: C:\...\Terra Nova Strike Force Centauri

     ENTER  launch the game      A  attach to a game started elsewhere      Q  quit
   ```

   Press the numbers to switch options, **Enter** to launch the game. Your choices are saved in `TNPlus.ini`.
3. The tool minimizes itself and waits in the taskbar. Play! When the game closes, the menu comes back.

The game folder is detected automatically (**GOG** registry and **Steam** libraries). If you have both, **G** switches between them. For any other install, start the game yourself and choose **A**: everything works except widescreen, which needs the tool to start DOSBox.

SHA-256 of `TNPlus.exe` v1.0.1: `D49ED4F7ECF3ADF69DD5C0F3A14F199BE185459E0BCE8C6192E13B04E99D0FDC`

v1.0.1 only adds version info and an icon to the exe (fewer antivirus false alarms). The tool itself is unchanged.

## Tips

- **Freelook** switches off by itself in the options screen (`O`), with `Esc` and when the mission ends; press `Y` again when you are back. Switch it off to click the cockpit buttons with the mouse.
- **Noclip**: land before switching it off — switching it off in mid-air means a free fall.
- **Widescreen** is anamorphic: the engine can only draw 320 pixels across, so the cockpit and the menus are stretched a little. The 3D view itself keeps correct proportions.
- Keys are **physical key positions**: `Y`, `U`, `I` are the same keys on QWERTY and AZERTY. They can be changed in `TNPlus.ini`.

## Settings (`TNPlus.ini`)

| Key | Default | Meaning |
|---|---|---|
| `freelook`, `noclip`, `force_320x400`, `widescreen` | `1`, `1`, `1`, `0` | the menu options |
| `view_distance` | `MAX` | `NORMAL`, `FAR` or `MAX` at start |
| `sensitivity_x`, `sensitivity_y` | `12`, `8` | mouse speed (heading / pitch units per mouse count) |
| `invert_y` | `0` | `1` = inverted vertical look |
| `noclip_speed` | `15` | game units per second (a walking PBA does about 2.5) |
| `key_freelook`, `key_noclip`, `key_distance` | `15`, `16`, `17` | **physical key scancodes** (hex): `15` = Y, `16` = U, `17` = I, `29` = key left of 1, `3B`–`44` = F1–F10 |
| `sound` | `1` | `0` = no beeps |
| `game_dir` | *(auto)* | game folder, if auto-detection does not find it |

## Compatibility

No fixed memory addresses: the game is located inside the emulator through **code signatures** (instruction patterns with wildcarded addresses) and the real addresses are read from the game's own instructions. It works with any DOSBox-family emulator and any memory setting; launching (and therefore widescreen) uses the DOSBox Staging bundled with the Steam and GOG releases.

- **Tested live:** GOG "Nightdive" build (DOSBox Staging), every option including widescreen.
- **All signatures verified** (exactly one match each, consistent with each other) in: English v1.09 (Steam and GOG executables), French v1.09, English v1.08 (CD image).
- The freelook core is the same as [Terra Nova Mouse Freelook](https://github.com/skw-1337/terra-nova-freelook) v1.1, tested live on the Steam version.

Feedback welcome, especially from Steam players and DOSBox-X users.

## Safety

- It only changes, in the running game, values located from the game's own code: heading, head pitch, mouse cursor, the player's physics state (noclip), the fog table, the resolution setting and the 3D view scale.
- Freelook and noclip only start inside the 3D view of a mission and switch off by themselves when you leave it, so nothing is written into stale memory.
- Because it reads/writes another program's memory, some antivirus software may flag it, like any game trainer: see below.

## Antivirus warnings

A few antivirus programs may flag the exe, mostly with "AI" or generic detections (Microsoft Defender finds nothing). It's a false positive: the tool reads and writes the game's memory and reads your keyboard and mouse, which is also what cheats and keyloggers do, and it's a small unsigned program that few people have run yet.

The full source is in `src/`: you can read it and build the exe yourself with `build.bat` (nothing to install).

## Anti-cheat note

The tool only opens DOSBox processes and never touches any other program. Still, it edits another process's memory, which is what game trainers do, and some anti-cheat systems (especially kernel-level ones) watch the whole PC. **Quit Terra Nova Plus (`Q` in its menu, or close its window) before playing online games protected by an anti-cheat.**

## Build from source

No install needed — Windows ships the C# compiler (.NET Framework 4):

```bat
cd src
build.bat
```

or manually:

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize /win32manifest:app.manifest /win32icon:icon.ico /out:TNPlus.exe TNPlus.cs
```

## How it works (short)

| Feature | What the tool does |
|---|---|
| Freelook | writes the player's heading and head pitch, keeps the game's cursor centred and frozen (the game's own "cursor frozen" flag: no reticle trails) |
| Noclip | finds the player's physics state (position ×6 in 16.16 fixed point, matched against the object table without writing anything) and drives it directly |
| View distance | rewrites the 1024-entry fog table and its check copy in one go (the game compares them) with a larger fog radius |
| 320×400 | goes through the same path as the options screen's "Accept" button; the game applies it on the next mission frame |
| Widescreen | the 3D library recomputes its horizontal view scale every frame from the pixel ratio, before clipping: ratio ×0.75 = a 33 % wider view with no empty borders. The terrain engine keeps two values of its own computed once from that scale; the tool realigns them, otherwise objects would "slide" on the ground when you turn. DOSBox is started with `aspect = stretch`. |

## Credits

Made by skw-1337. The game is © its respective owners (Looking Glass Technologies / Night Dive Studios); this tool contains no game data.
Released under the MIT license.
