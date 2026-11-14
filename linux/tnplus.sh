#!/usr/bin/env bash
# Terra Nova Plus on Linux (EXPERIMENTAL).
#
# TN+ is a Windows program. This script runs it, and the game's own DOSBox, under Wine, in a Wine prefix of its own
# (your default prefix is left alone). Nothing is installed system-wide.
#
#   ./tnplus.sh                      start TN+ (its window), the game found in the usual Steam / GOG places
#   ./tnplus.sh "/path/to/the game"  the folder that contains TNOVA, when it is somewhere else
#   ./tnplus.sh ... --console        TN+'s text menu instead of its window
#
# Needs: wine (9.0 or later) with Wine Mono (Wine offers to install it the first time: say yes; some distributions
# have a wine-mono package), and at least one font (any desktop has).
# Put this file next to TNPlus.exe (the zip of a release), or keep it in the repository's linux/ folder.
#
# Settings: TNPLUS_EXE (another TNPlus.exe), TNPLUS_PREFIX (another Wine prefix), WINE (another wine).
set -u

say() { printf '%s\n' "$*"; }
die() { say "tnplus.sh: $*" >&2; exit 1; }

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WINE="${WINE:-wine}"
command -v "$WINE" > /dev/null 2>&1 || die "wine was not found. Install it first (Debian / Ubuntu: sudo apt install wine; Fedora: sudo dnf install wine; Arch: sudo pacman -S wine wine-mono)."

# TNPlus.exe: next to this script, or where the repository builds it
EXE="${TNPLUS_EXE:-}"
if [ -z "$EXE" ]; then
  for c in "$HERE/TNPlus.exe" "$HERE/../TNPlus.exe" "$HERE/../src/TNPlus.exe"; do
    [ -f "$c" ] && { EXE="$c"; break; }
  done
fi
[ -n "$EXE" ] && [ -f "$EXE" ] || die "TNPlus.exe was not found next to this script. Download a release (github.com/skw-1337/terra-nova-plus/releases) and put tnplus.sh in its folder, or set TNPLUS_EXE."
EXE_DIR="$(cd "$(dirname "$EXE")" && pwd)"

# the game: the folder given, or the usual places
is_game() { [ -d "$1/TNOVA" ] && [ -d "$1/_DOSBOX" ]; }
GAME=""
if [ $# -gt 0 ] && [ -d "$1" ]; then
  GAME="$(cd "$1" && pwd)"; shift
  is_game "$GAME" || die "'$GAME' is not the game's folder (it must contain TNOVA and _DOSBOX: the GOG or Steam edition)."
else
  N="Terra Nova Strike Force Centauri"
  for c in "$HOME/.steam/steam/steamapps/common/$N" "$HOME/.local/share/Steam/steamapps/common/$N" \
           "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/$N" \
           "$HOME/GOG Games/$N" "$HOME/GOG Games/Terra Nova - Strike Force Centauri" "$HOME/Games/Heroic/$N" "$HOME/Games/$N"; do
    is_game "$c" && { GAME="$c"; break; }
  done
fi

export WINEPREFIX="${TNPLUS_PREFIX:-${XDG_DATA_HOME:-$HOME/.local/share}/terra-nova-plus/wine}"
export WINEDEBUG="${WINEDEBUG:--all}"
if [ ! -d "$WINEPREFIX" ]; then
  say "First start: setting up a Wine prefix in $WINEPREFIX (if Wine offers to install Mono, say yes)..."
  mkdir -p "$(dirname "$WINEPREFIX")"
  "$WINE" wineboot -i > /dev/null 2>&1
  command -v wineserver > /dev/null 2>&1 && wineserver -w
fi

# the game's folder goes into TNPlus.ini as a Windows path (TN+ cannot look it up in a registry here)
INI="$EXE_DIR/TNPlus.ini"
if [ -n "$GAME" ]; then
  WGAME="$("$WINE" winepath -w "$GAME" 2> /dev/null | tr -d '\r')"
  [ -n "$WGAME" ] || die "could not turn '$GAME' into a Windows path (winepath)."
  if [ -f "$INI" ] && grep -q '^game_dir *=' "$INI"; then
    TMP="$(mktemp)"
    # (the path goes through the environment: awk -v would read its backslashes as escapes)
    WGAME="$WGAME" awk '/^game_dir *=/ { printf "game_dir = %s\r\n", ENVIRON["WGAME"]; next } { print }' "$INI" > "$TMP" && cat "$TMP" > "$INI"; rm -f "$TMP"
  else
    printf 'game_dir = %s\r\n' "$WGAME" >> "$INI"
  fi
  say "Game: $GAME"
  [ -w "$GAME/TNOVA" ] || say "Warning: $GAME/TNOVA is not writable. TN+ prepares the game's exe there (it keeps a copy of the original) and the game saves there."
else
  say "No game folder found in the usual places: pick it in TN+'s window, or give it: ./tnplus.sh \"/path/to/the game\""
fi

cd "$EXE_DIR" || die "cannot enter $EXE_DIR"
"$WINE" TNPlus.exe "$@"
RC=$?
if [ $RC -ne 0 ]; then
  say ""
  say "TN+ stopped with code $RC. If it never showed its window:"
  say "  - Wine Mono is missing: install your distribution's wine-mono package, or remove $WINEPREFIX and start again, then accept Wine's offer;"
  say "  - no font is installed (\"FontFamilyNotFound\"): install any font package (fonts-liberation for example)."
fi
exit $RC
