# Modding notes: Terra Nova SFC, English v1.09

What Terra Nova Plus knows about the English v1.09 executable, for anyone patching it.
Steam and GOG ship the same `TNOVA\__FF.EXE`:
SHA-256 `B762C54509F1716282C4D357A72013B8D84BA25F17B0483B733678FF81E63DD1`, 1,856,641 bytes.

The list of everything Terra Nova Plus changes is in `tnplus_patches_en_v109.json`. It is checked against the
game's memory with and without the tool, in a mission.

## Addresses

All addresses are linear addresses in DOSBox's guest memory while the game runs (DOS/4GW, LE executable).
They stay the same from one run to the next with the usual DOSBox setup.

- Object 1 (code): **address = file offset + 0x1AAB5C**. Its pages start at file offset 0x64200,
  which gives 0x20ED5C.
- Object 3 (data) ends at 0x465024 in the original exe (virtual size 0x10F900). The game's heap starts right after.

## Game structures (English addresses)

| What | Address | Notes |
|---|---|---|
| Entity table | 0x3C3240 | 0x25 bytes per entry |
| Player's entity id | word at 0x441350 | entry = 0x3C3240 + 0x25 × id |
| Entity position | entry + 0x0F | x, y, z, 32-bit 16.16 |
| Entity heading | entry + 0x1B | 16 bits, 0x10000 = 360° |
| Main camera | 0x38FB24 | +0x04 vertical scale, +0x18 culling half-angle |
| Camera set-up | 0x29E830 | called when the canvas or the zoom changes |
| 3D library projection | 0x3622CE … | pixel ratio, half-width, centre |
| Palette | 0x44AE90 | 256 × RGB, 8-bit |
| Video driver mode | dword at 0x35F8D0 | |
| Projectile hit test | 0x311553 | step count rounded down: misses above ~30 fps |
| Physics clock | 0x311F5A, 0x311F77 | leftover time added again every frame |
| Entity's PBA number | entry + 0x03 (word) | index of its PBA struct |
| PBA structs | 0x3A5608 + 0x582 × n | n = entry + 0x03; a second view of the same data starts at 0x3A59D0 |
| AI soldier brains | 0x3832F0 + 0x14E × s | the player has none; word +0x0B = entity id |
| Physics objects | 0x44E0DD + 0x30 × i (position), 0x44F2EC + 0xA0 × i | up to 96 |

## Keyboard

The game reads the keyboard through its own IRQ1 handler at **0x31AAE0**.
- **Its structure (kb)**: pointer in the dword at 0x45D62C (the instruction at 0x31AAEE reads it).
- **Event queue**: 16-bit words `scancode | pressed << 8`, from kb+0x10 to kb+0x410. The write index is the dword at [kb].
- **Key state**: bytes at kb+0x410+scancode, bit 0 = down. E0 keys use 0x80 + scancode.
- **Reading**: 0x30504C reads the next event, 0x305020 peeks at it.
- **Alt / Ctrl / Shift**: the game follows them from the queue events (0x301CE0), not from the state array. Putting
  modifier events in the queue gives any combination. The address of its flag word isn't confirmed yet.
- **Key bitmap**: every frame 0x2F2B30 turns the key states into a 256-bit bitmap at 0x44C5B0. The game reads
  keys through the pointer at 0x44C5D0, not the bitmap directly. The game has an input recording / playback
  mode that just points it elsewhere (flags 0x35C02C / 0x35C02D).

## Controls

- 0x2F2C2C runs every frame: key bitmap (0x2F2B30), clear the control slots (0x2D7910), keyboard (0x2D7F2C),
  joystick (0x2D8138), then 0x2D828C.
- 3 control slots of 0x28 bytes at 0x446690 (a second group at 0x446618 is used with Ctrl). Fields, 16.16:
  +0x00 forward (W +1.0, S +0.5, X -1.0), +0x04 strafe (Z / C ±1.0), +0x08 turn (A / D ±0.5), +0x10 look up / down,
  +0x24 flags.
- 0x2D7960 adds the slots into 0x4465F0, clamps each value to ±1.0 and applies it to the player's PBA: heading,
  movement (0x28ABE0), head look. It is called once per frame, at 0x2D390B.
- Writing another PBA's entity id into 0x441350 moves the camera, the cockpit and the controls to it. The AI
  leaves alone whatever PBA is the player.
- AI: the loop at 0x249EA2 walks the squads and calls 0x24DF10 for each soldier (call at 0x249EFC).
- Firing: 0x292248 (eax = entity id) is shared by the player and the AI. It does nothing while the dword at
  PBA struct + 9 is 0.

## DOSBox pitfalls

- **Code that already ran is cached.** DOSBox's dynamic core keeps it compiled, so bytes written from outside into
  that code are not seen. Patch at the main menu, before the code first runs, or have the game write the patch itself.
  Terra Nova Plus does that for the keyboard handler: a hook in the camera set-up writes the call with `cli`.
- **Keep variables out of code pages.** Writing data into a 4 KB page that holds code makes DOSBox recompile it,
  and it can crash ("INT: Gate Selector points to illegal descriptor"). Keep data and code on separate pages.
- **Self-modifying texture loops.** The 3D lib's texture loops patch their own instructions on every scanline
  (destination pointer, texture steps): the sprite loop at 0x3316A0, the terrain tile loop at 0x3311F0, plus code the
  game generates at 0x34E300. DOSBox's dynamic core retranslates them each time. That's why the infrared view costs
  about a third of the frame rate (63 -> ~40 fps here). With core = normal it costs nothing extra, but that core is
  much slower overall.

## Memory used by Terra Nova Plus

Terra Nova Plus makes object 3 0x6000 bytes bigger in the exe file. The original file is kept as
`__FF.EXE.tnplus-original`. That gives a zero-filled area at **0x465024 – 0x46B000**. The game's heap starts at 0x46B024, its headers take
0x46B000 – 0x46B023.

| Range | Use |
|---|---|
| 0x465100 – 0x466100 | HD code |
| 0x466100 – 0x469390 | HD data and per-column tables |
| 0x469400 – 0x469408 | true 16:9 variables |
| 0x469410 – 0x469A00 | edge objects data |
| 0x469A10 – 0x469F30 | game keys data |
| 0x46A000 – 0x46A0A7 | field of view |
| 0x46A100 – 0x46A19E | terrain sectors |
| 0x46A1A0 – 0x46A1F7 | game keys: stuck key sweep |
| 0x46A200 – 0x46A2AB | true 16:9 camera |
| 0x46A300 – 0x46AA16 | edge objects code |
| 0x46AA20 – 0x46AE54 | game keys code |
| 0x46AE54 – 0x46B000 | free |
| 0x46B000 – 0x46B024 | heap headers (the game's, not ours) |

Patch sites in the game code are listed in the JSON, with the bytes before and after.

## Bugs

### Game bugs, fixed in Terra Nova Plus

- **Projectiles miss moving targets above ~30 fps.** A projectile tests the entity grid in steps over the distance it
  moved, and the step count was rounded down, so at high frame rates it tested nothing. Rounded up at 0x311553.
- **Walking, jumps and falls get faster with the frame rate** (+65 % at 76 fps). The physics kept a few ms of
  leftover time and added it again every frame. It also skipped frames shorter than 10 ms, so walking stuttered above
  ~80 fps. Fixed at 0x311F5A and 0x311F77.
- **Black screen forever at the end of a mission.** The game frees the cockpit's click zones but keeps testing the
  mouse against them for a moment and can loop forever. The zone walk now gives up after 255 steps (0x2F249C).
- **"ERROR: back_intersect: Too many temporary points!"** with far terrain: the clipping point buffer (800) overflows.
  Raised to 6400 in the exe.
- **Crash when zooming with far terrain.** Each zoom level multiplies the terrain rings (48 became 62 to 87). Capped
  at 48 in the exe (0x2A8990).
- **The per-frame object list overflows** with more objects: 400 entries, and two of its writers have no check.
  Raised to 1200.
- **Rain falls only on the left half** in HD: its column was not doubled. Fixed in the HD code.
- **Sound effects stereo is reversed under DOSBox** (the SB16 driver swaps left and right). Optional fix through
  DOSBox's mixer, F7 swaps it in game.
- **The ground stops short of the screen edges** with a view wider than about 90°: the terrain is only built in 3
  sectors of 45° around the heading. A hook on the builder (0x2AA3C2) and extra strips fix most of it.

- **Zoom lost after the Options screen.** Back in the mission, the view reset (0x26BB70) calls the camera init
  0x2AC7A0 with zoom level 0 while the zoom level (word at 0x38FBE2) keeps its value: the view looks unzoomed and
  the next zoom step jumps. The call at 0x26BB89 goes to a 12-byte cave at 0x46A0B0 that loads the current level
  first (`movsx ebx, word [0x38FBE2]` / `jmp 0x2AC7A0`).

### Traps we fell into while patching

- **A constant shifted like an address.** Our French-to-English address map also moved the cursor size 32x64
  (`0x400020`), so the cursor save-under got a width of -144: a huge copy, and DOSBox closed when a second mission
  started. Only map real addresses.
- **Objects sliding on the ground in a wider view.** The 3D library's horizontal object scale is half-width ×
  (height × pixel ratio / width), so the width cancels out: changing the pixel ratio when widening the view made
  buildings 0.70× or 1.42× too wide and they slid when turning. Leave the library's pixel ratio alone.
- **Target box off target after widening the view:** the library's half-width (0x3622E2) has to match the width the
  3D was drawn at.
- **Reading the camera scale at the wrong moment:** with a locked target the engine also renders the small target
  view, so a value read at a random time can come from that one. Check the canvas width first.
- **A key held while our key translation switched on stayed down for the game.** The press went through as it was,
  the release was translated, so the game never saw the original key go up (the player kept turning). A key pressed
  untranslated (outside a mission, with Alt held...) now stays untranslated until it is released.
- **DOSBox's dynamic core** (see above): patches written from outside into code that already ran are not seen, and
  variables written into a code page can crash it.

### Not solved

- **Noclip crash at high altitude**, seen once. Maybe the clipping point overflow above, not seen since.
- **Black screen after finishing a mission and starting Panama** on Steam, seen once. Probably the cursor constant
  bug above, not seen since.
- **A thin strip at one screen edge** can still show the far terrain renderer near the diagonals with a wide view
  (8° at most at 100°).
- **True 16:9**: the menus are stretched, only missions are drawn in 848x480. The full view (`G`) and the locked
  target box with the stretched cockpit still need checking in game.
- **AWE32 music**: a few instruments are slightly out of tune, and the percussion the game's bank doesn't define
  falls back to the Windows sound set.
- **Remapped keys**: extended keys (arrows, Page Up...) can't be picked as new keys yet (the E0 path of the keyboard
  handler isn't translated).
- **Shots go through turrets** (particle beam, mission 26 with the pirate one; seen once with the multipulsar). An
  original bug, also in the stock game. Cause found by Callemann: a flag that keeps a turret's own shots from
  hitting it is shared by incoming shots and not always cleared when our shots hit. The shot's ray test is
  0x2F074C -> 0x311283 -> 0x311848 (physics objects per grid cell).

## A shared patch format

One entry per patch: `address`, `old` (hex), `new` (hex), plus a `feature` name. A tool checks `old` before
writing `new`, so two tools can see when they touch the same bytes. Code caves are entries too, written before the
hook that calls them.

The JSON also has a `features` section: for each feature, what it `requires` (other features, the prepared exe,
HD in true 16:9), the `memory` it takes in our area, the write `order` (code before the hooks that call it) and where
its code bytes come from (`code_from`). Features with no requirements and no memory can be applied on their own.
Every file patch is labelled with its `feature` too.
