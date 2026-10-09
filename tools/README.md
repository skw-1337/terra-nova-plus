# Tools

The Python scripts that generate the machine code in `src/` (each C# payload says which one made it). Comments are in French.

- `hd_mod.py`: the HD mode (640x400, and true 16:9 at 848x480 with `--wide`). `python hd_mod.py export [--en] [--wide]` writes `src/HdPayload*.cs`; without `export` it injects into a running game (through `tnre.py`).
- `port_en.py`, `port_en.json`: the French to English address table used by `hd_mod.py --en`. The table is included. Rebuilding it needs RAM snapshots of both games (`tnre.py snap`) and a disassembly, which aren't shipped.
- `le.py`: reads the LE executable (`__FF.EXE`) and maps it to the emulated RAM.
- `kb_cave.py`: the key remapping routine (KEYBOARD tab). Assembles it, tests it in unicorn, writes `kb_cave.json`.
- `strips_cave.py`: extra terrain strips outside the game's 3 octants (edge objects, wider field of view).
- `tnre.py`: reads and writes DOSBox Staging's emulated RAM, sends keys, captures the window. Used for live tests.

Python 3 with `pip install keystone-engine numpy pillow psutil`, plus `unicorn` for the kb_cave tests and `capstone` for `port_en.py` / `le.py`.
