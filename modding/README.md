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

## Keyboard

The game reads the keyboard through its own IRQ1 handler at **0x31AAE0**.
- **Its structure (kb)**: the instruction at 0x31AAEE loads a pointer to it.
- **Event queue**: 16-bit words `scancode | pressed << 8`, from kb+0x10 to kb+0x410. The write index is the dword at [kb].
- **Key state**: bytes at kb+0x410+scancode, bit 0 = down. E0 keys use 0x80 + scancode.
- **Reading**: 0x30504C reads the next event, 0x305020 peeks at it.
- **Alt / Ctrl / Shift**: the game follows them from the queue events (0x301CE0), not from the state array. Putting
  modifier events in the queue gives any combination. The address of its flag word isn't confirmed yet.

## DOSBox pitfalls

- **Code that already ran is cached.** DOSBox's dynamic core keeps it compiled, so bytes written from outside into
  that code are not seen. Patch at the main menu, before the code first runs, or have the game write the patch itself.
  Terra Nova Plus does that for the keyboard handler: a hook in the camera set-up writes the call with `cli`.
- **Keep variables out of code pages.** Writing data into a 4 KB page that holds code makes DOSBox recompile it,
  and it can crash ("INT: Gate Selector points to illegal descriptor"). Keep data and code on separate pages.

## Memory used by Terra Nova Plus

Terra Nova Plus makes object 3 0x6000 bytes bigger in the exe file. The original file is kept as
`__FF.EXE.tnplus-original`. That gives a zero-filled area at **0x465024 – 0x46B024**, and the heap starts after it.

| Range | Use |
|---|---|
| 0x465100 – 0x466100 | HD code |
| 0x466100 – 0x469390 | HD data and per-column tables |
| 0x469400 – 0x469408 | true 16:9 variables |
| 0x469410 – 0x469A00 | edge objects data |
| 0x469A10 – 0x469F30 | game keys data |
| 0x46A000 – 0x46A0A7 | field of view |
| 0x46A100 – 0x46A19E | terrain sectors |
| 0x46A200 – 0x46A2AB | true 16:9 camera |
| 0x46A300 – 0x46AA16 | edge objects code |
| 0x46AA20 – 0x46AE34 | game keys code |
| 0x46AE34 – 0x46B024 | free |

Patch sites in the game code are listed in the JSON, with the bytes before and after.

## A shared patch format

One entry per patch: `address`, `old` (hex), `new` (hex), plus a `feature` name. A tool checks `old` before
writing `new`, so two tools can see when they touch the same bytes. Code caves are entries too, written before the
hook that calls them.
