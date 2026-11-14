#!/usr/bin/env bash
# Terra Nova Plus on Linux (EXPERIMENTAL)
#
# TN+ is a Windows program. This script does everything needed to run it on Linux:
#   1. checks Wine (or uses Steam's Proton when there is no Wine, as on a Steam Deck),
#   2. downloads Terra Nova Plus if it is not next to this script,
#   3. finds your copy of Terra Nova (Steam or GOG),
#   4. starts TN+ in a Wine prefix of its own (your other Wine programs are left alone).
#
# Just run it:   bash tnplus.sh
# Game somewhere unusual:   bash tnplus.sh "/path/to/Terra Nova Strike Force Centauri"
# What follows a folder (or comes alone) goes to TNPlus.exe, for example --console.
#
# Settings (environment): WINE (another wine program), TNPLUS_EXE (another TNPlus.exe), TNPLUS_PREFIX (another prefix).
set -u

REPO="skw-1337/terra-nova-plus"
FALLBACK_ZIP="https://github.com/$REPO/releases/download/v1.2.6/TerraNovaPlus_v1.2.6.zip"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

say()  { printf '%s\n' "$*"; }
step() { printf '\n[%s] %s\n' "$1" "$2"; }
die()  { printf '\nSorry, it cannot go on: %s\n' "$*" >&2; exit 1; }

say "Terra Nova Plus for Linux (experimental)"

# ---------------------------------------------------------------- 1. Wine
step "1/4" "Looking for Wine..."
WINE="${WINE:-}"
if [ -z "$WINE" ]; then
  if command -v wine > /dev/null 2>&1; then
    WINE="wine"
  else                                    # no Wine: Steam's Proton has one (Steam Deck)
    for p in "$HOME/.steam/steam/steamapps/common"/Proton*/files/bin/wine \
             "$HOME/.local/share/Steam/steamapps/common"/Proton*/files/bin/wine \
             "$HOME/.steam/steam/compatibilitytools.d"/*/files/bin/wine; do
      [ -x "$p" ] && WINE="$p"
    done
    [ -n "$WINE" ] && say "      No Wine installed: using Steam's Proton ($WINE). This way is not tested yet."
  fi
fi
if [ -z "$WINE" ]; then
  ID_LIKE=""; ID=""
  [ -r /etc/os-release ] && . /etc/os-release
  case " ${ID:-} ${ID_LIKE:-} " in
    *" debian "*|*" ubuntu "*) HOW="sudo apt install wine" ;;
    *" fedora "*|*" rhel "*)   HOW="sudo dnf install wine" ;;
    *" arch "*)                HOW="sudo pacman -S wine wine-mono" ;;
    *" suse "*|*" opensuse "*) HOW="sudo zypper install wine" ;;
    *)                         HOW="install the package named wine with your software manager" ;;
  esac
  die "Wine is not installed. Install it with:

    $HOW

then run this script again."
fi
say "      ok: $("$WINE" --version 2> /dev/null | head -1)"
if command -v fc-list > /dev/null 2>&1 && [ -z "$(fc-list 2> /dev/null | head -1)" ]; then
  say "      Note: no font is installed, TN+'s window needs one (for example: sudo apt install fonts-liberation)."
fi

# ---------------------------------------------------------------- 2. Terra Nova Plus
step "2/4" "Looking for Terra Nova Plus..."
find_exe() {
  local c
  for c in "${TNPLUS_EXE:-}" "$HERE/TNPlus.exe" "$HERE"/TerraNovaPlus*/TNPlus.exe "$HERE/../TNPlus.exe" "$HERE/../src/TNPlus.exe"; do
    [ -n "$c" ] && [ -f "$c" ] && { printf '%s' "$c"; return 0; }
  done
  return 1
}
fetch() {                                 # fetch URL FILE
  if command -v curl > /dev/null 2>&1; then curl -fsSL -o "$2" "$1"
  elif command -v wget > /dev/null 2>&1; then wget -q -O "$2" "$1"
  else return 1; fi
}
EXE="$(find_exe)"
if [ -z "$EXE" ]; then
  say "      Not here yet: downloading the latest release (less than 1 MB)..."
  URL=""
  if command -v curl > /dev/null 2>&1; then
    URL="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" 2> /dev/null | grep -o 'https://[^"]*TerraNovaPlus[^"]*\.zip' | head -1)"
  fi
  [ -n "$URL" ] || URL="$FALLBACK_ZIP"
  ZIP="$HERE/TerraNovaPlus.zip"
  fetch "$URL" "$ZIP" || die "the download failed (no internet, or neither curl nor wget is installed). You can also download the zip yourself from https://github.com/$REPO/releases and unzip it next to this script."
  if command -v unzip > /dev/null 2>&1; then unzip -q -o "$ZIP" -d "$HERE"
  elif command -v python3 > /dev/null 2>&1; then python3 -m zipfile -e "$ZIP" "$HERE"
  else die "the zip is downloaded ($ZIP) but neither unzip nor python3 is installed to open it. Unzip it next to this script and start again."
  fi
  rm -f "$ZIP"
  EXE="$(find_exe)" || die "TNPlus.exe was not in the downloaded zip."
fi
EXE_DIR="$(cd "$(dirname "$EXE")" && pwd)"
say "      ok: $EXE_DIR"

# ---------------------------------------------------------------- 3. the game
step "3/4" "Looking for your Terra Nova..."
is_game() { [ -d "$1/TNOVA" ] && [ -d "$1/_DOSBOX" ]; }
GAME=""
if [ $# -gt 0 ] && [ -d "$1" ]; then
  GAME="$(cd "$1" && pwd)"; shift
  is_game "$GAME" || die "'$GAME' is not the game's folder. The right one contains two folders named TNOVA and _DOSBOX (the Steam or GOG edition)."
else
  N="Terra Nova Strike Force Centauri"
  LIBS=("$HOME/.steam/steam" "$HOME/.local/share/Steam" "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam")
  for v in "$HOME/.steam/steam/steamapps/libraryfolders.vdf" "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf"; do
    [ -r "$v" ] || continue                 # the other Steam libraries (another disk, a Steam Deck's SD card)
    while IFS= read -r l; do LIBS+=("$l"); done < <(grep '"path"' "$v" | sed 's/.*"path"[^"]*"\([^"]*\)".*/\1/')
  done
  for c in "${LIBS[@]/%//steamapps/common/$N}" "$HOME/GOG Games/$N" "$HOME/GOG Games/Terra Nova - Strike Force Centauri" \
           "$HOME/Games/Heroic/$N" "$HOME/Games/Heroic/Terra Nova - Strike Force Centauri" "$HOME/Games/$N" "$HOME/Games/terra-nova-strike-force-centauri"; do
    is_game "$c" && { GAME="$c"; break; }
  done
  if [ -z "$GAME" ] && [ -t 0 ]; then
    say "      Not found in the usual Steam and GOG places."
    say "      Type the game's folder (or drag it from your file manager into this window), then press Enter:"
    read -r -e -p "      > " ANSWER
    ANSWER="${ANSWER%\"}"; ANSWER="${ANSWER#\"}"; ANSWER="${ANSWER%\'}"; ANSWER="${ANSWER#\'}"; ANSWER="${ANSWER% }"
    [ -n "$ANSWER" ] && is_game "$ANSWER" && GAME="$(cd "$ANSWER" && pwd)"
    [ -n "$ANSWER" ] && [ -z "$GAME" ] && die "'$ANSWER' is not the game's folder. The right one contains two folders named TNOVA and _DOSBOX."
  fi
fi
if [ -n "$GAME" ]; then
  say "      ok: $GAME"
  [ -w "$GAME/TNOVA" ] || say "      Warning: this folder cannot be written to. TN+ prepares the game there (it keeps a copy of the original) and the game saves there."
else
  say "      Not found. TN+ will start anyway; to tell it where the game is, run:  bash tnplus.sh \"/path/to/the game\""
fi

# ---------------------------------------------------------------- 4. start
step "4/4" "Starting Terra Nova Plus..."
export WINEPREFIX="${TNPLUS_PREFIX:-${XDG_DATA_HOME:-$HOME/.local/share}/terra-nova-plus/wine}"
export WINEDEBUG="${WINEDEBUG:--all}"
if [ ! -d "$WINEPREFIX" ]; then
  say "      First start: preparing Wine (about a minute)."
  say "      >>> If a window asks to install \"Wine Mono\", click Install. TN+ needs it. <<<"
  mkdir -p "$(dirname "$WINEPREFIX")"
  "$WINE" wineboot -i > /dev/null 2>&1
  command -v wineserver > /dev/null 2>&1 && wineserver -w
fi
# the game's folder goes into TNPlus.ini as a Windows path (TN+ cannot look it up in a registry here)
INI="$EXE_DIR/TNPlus.ini"
if [ -n "$GAME" ]; then
  WGAME="$("$WINE" winepath -w "$GAME" 2> /dev/null | tr -d '\r' | tail -1)"
  [ -n "$WGAME" ] || die "Wine could not make a Windows path of '$GAME'."
  if [ -f "$INI" ] && grep -q '^game_dir *=' "$INI"; then
    TMP="$(mktemp)"                         # (through the environment: awk -v would read the backslashes as escapes)
    WGAME="$WGAME" awk '/^game_dir *=/ { printf "game_dir = %s\r\n", ENVIRON["WGAME"]; next } { print }' "$INI" > "$TMP" && cat "$TMP" > "$INI"; rm -f "$TMP"
  else
    printf 'game_dir = %s\r\n' "$WGAME" >> "$INI"
  fi
fi
say "      In TN+'s window: pick your settings, then click Launch."
say "      The game then shows a small text menu: press 1 to play."
cd "$EXE_DIR" || die "cannot enter $EXE_DIR"
"$WINE" TNPlus.exe "$@"
RC=$?
if [ $RC -ne 0 ]; then
  say ""
  say "TN+ stopped (code $RC). If its window never showed up, it is one of these two:"
  say "  - Wine Mono is missing. Delete the folder $WINEPREFIX, run this script again and click Install"
  say "    when Wine offers Mono (or install your system's wine-mono package)."
  say "  - No font is installed (\"FontFamilyNotFound\"). Install one, for example: sudo apt install fonts-liberation"
  say "Still stuck? Tell me what you see: https://github.com/$REPO/issues"
fi
exit $RC
