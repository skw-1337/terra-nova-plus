#!/usr/bin/env bash
# Terra Nova Plus on Linux (EXPERIMENTAL)
#
# TN+ is a Windows program. This script does everything needed to run it on Linux:
#   1. checks Wine (or uses Steam's Proton when there is no Wine, as on a Steam Deck),
#   2. downloads Terra Nova Plus if it is not next to this script,
#   3. finds your copy of Terra Nova (Steam or GOG) and checks it is one TN+ can use,
#   4. asks for your settings and starts the game, in a Wine prefix of its own (your other Wine programs are left alone).
#
# Just run it:   bash tnplus.sh
# Game somewhere unusual:   bash tnplus.sh "/path/to/Terra Nova Strike Force Centauri"
# --window    always TN+'s own settings window (it is about 880 pixels tall)
# --terminal  always the few questions in the terminal, then straight into the game
# Anything else goes to TNPlus.exe as it is, for example --console.
#
# Settings (environment): WINE (another wine program), TNPLUS_EXE (another TNPlus.exe), TNPLUS_PREFIX (another prefix).
set -u

REPO="skw-1337/terra-nova-plus"
FALLBACK_ZIP="https://github.com/$REPO/releases/download/v1.2.6/TerraNovaPlus_v1.2.6.zip"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WINDOW_H=900                              # TN+'s window with its title bar, plus a little room for a panel

say()  { printf '%s\n' "$*"; }
step() { printf '\n[%s] %s\n' "$1" "$2"; }
die()  { printf '\nSorry, it cannot go on: %s\n' "$*" >&2; exit 1; }

say "Terra Nova Plus for Linux (experimental)"

# what is for this script, what is the game's folder, what goes to TNPlus.exe
UI="auto"; GAME_ARG=""; ARGS=()
for a in "$@"; do
  case "$a" in
    --window)   UI="window" ;;
    --terminal) UI="terminal" ;;
    *) if [ -z "$GAME_ARG" ] && [ ${#ARGS[@]} -eq 0 ] && [ "${a#-}" = "$a" ]; then GAME_ARG="$a"; else ARGS+=("$a"); fi ;;
  esac
done

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
N="Terra Nova Strike Force Centauri"

# ci DIR NAME: the entry of DIR called NAME, whatever its letter case (Wine does not mind the case, Linux does)
ci() {
  local e b n="${2,,}"
  [ -e "$1/$2" ] && { printf '%s' "$1/$2"; return 0; }
  for e in "$1"/* "$1"/.[!.]*; do
    b="${e##*/}"
    [ "${b,,}" = "$n" ] && [ -e "$e" ] && { printf '%s' "$e"; return 0; }
  done
  return 1
}
listing() { ls -A "$1" 2> /dev/null | head -12 | tr '\n' ' '; }

# check_game DIR: is it a game folder TN+ can use? If not, WHY says exactly what is missing and where it looked,
# and KIND names the two cases with a known way out (linux, oldgog).
WHY=""; KIND=""
check_game() {
  local d="$1" t db f
  WHY=""; KIND=""
  [ -d "$d" ] || { WHY="there is no folder '$d'."; return 1; }
  if ! t="$(ci "$d" TNOVA)" || [ ! -d "$t" ]; then
    if [ -n "$(ci "$d" TN)" ] || [ -n "$(ci "$d" dosboxTerraNova.conf)" ]; then
      KIND="oldgog"
      WHY="'$d' is GOG's default build (1.08): the game is in a folder named TN and there is no TNOVA folder.
      TN+ needs GOG's Beta version of the game: in Heroic or Lutris, choose the Beta branch and let it update."
    else
      WHY="there is no folder named TNOVA in '$d'.
      What is there: $(listing "$d")"
    fi
    return 1
  fi
  ci "$t" __FF.EXE > /dev/null || { WHY="the game's program TNOVA/__FF.EXE is missing in '$d'.
      What is in TNOVA: $(listing "$t")"; return 1; }
  if ! db="$(ci "$d" _DOSBOX)" || [ ! -d "$db" ]; then
    WHY="there is no folder named _DOSBOX in '$d' (TNOVA is there).
      What is there: $(listing "$d")"
    return 1
  fi
  if ! ci "$db" dosbox-staging.exe > /dev/null; then
    if [ -n "$(ci "$db" TerraNova.sh)" ] || [ -n "$(ci "$db" dosbox)" ] || [ -n "$(ci "$db" dosbox-staging)" ] || [ -n "$(ci "$db" dosbox_terranova_linux.conf)" ]; then
      KIND="linux"
      WHY="'$d' holds the LINUX edition of the Steam game: _DOSBOX/dosbox-staging.exe is missing there.
      TN+ is a Windows program and needs the Windows edition (same game, same folder, same saves). To get it:
        in Steam, right-click Terra Nova > Properties > Compatibility,
        tick \"Force the use of a specific Steam Play compatibility tool\" and choose a Proton.
      Steam then downloads the Windows files (about 20 MB). When it is done, run this script again.
      What is in _DOSBOX: $(listing "$db")"
    else
      WHY="the Windows DOSBox of the game, _DOSBOX/dosbox-staging.exe, is missing in '$d'.
      What is in _DOSBOX: $(listing "$db")"
    fi
    return 1
  fi
  for f in dosbox_terranova_windows.conf dosbox_terranova_windows_launch.conf; do
    ci "$db" "$f" > /dev/null || { WHY="_DOSBOX/$f is missing in '$d'.
      What is in _DOSBOX: $(listing "$db")"; return 1; }
  done
  return 0
}

# the folder a user gives may be TNOVA or _DOSBOX themselves, or end with a slash
tidy() {
  local d="${1%/}" b
  [ -d "$d" ] && d="$(cd "$d" && pwd)"
  b="${d##*/}"; b="${b,,}"
  if [ "$b" = "tnova" ] || [ "$b" = "_dosbox" ]; then [ -n "$(ci "${d%/*}" TNOVA)" ] && d="${d%/*}"; fi
  printf '%s' "$d"
}

GAME=""; REFUSED=""
if [ -n "$GAME_ARG" ]; then
  GAME="$(tidy "$GAME_ARG")"
  check_game "$GAME" || die "$WHY"
else
  # every Steam there can be (the usual one, Flatpak, Snap), then the libraries each one lists (another disk, an SD card)
  ROOTS=("$HOME/.steam/steam" "$HOME/.steam/root" "$HOME/.local/share/Steam" "$HOME/.steam/debian-installation"
         "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" "$HOME/.var/app/com.valvesoftware.Steam/.steam/steam"
         "$HOME/snap/steam/common/.local/share/Steam")
  LIBS=("${ROOTS[@]}")
  for r in "${ROOTS[@]}"; do
    for v in "$r/steamapps/libraryfolders.vdf" "$r/config/libraryfolders.vdf"; do
      [ -r "$v" ] || continue
      while IFS= read -r l; do [ -n "$l" ] && LIBS+=("$l"); done < <(grep '"path"' "$v" | sed 's/.*"path"[^"]*"\([^"]*\)".*/\1/')
    done
  done
  for m in /run/media/*/* /run/media/* /media/*/* /mnt/*; do      # SD cards and other disks, listed or not
    [ -d "$m/steamapps" ] && LIBS+=("$m")
    [ -d "$m/SteamLibrary/steamapps" ] && LIBS+=("$m/SteamLibrary")
  done
  CANDS=(); SEEN=""
  for l in "${LIBS[@]}"; do
    [ -d "$l/steamapps" ] || continue
    case "$SEEN" in *"
      $l"*) ;; *) SEEN="$SEEN
      $l" ;; esac
    acf="$l/steamapps/appmanifest_368600.acf"               # Steam's own note of where it put the game
    if [ -r "$acf" ]; then
      dir="$(sed -n 's/.*"installdir"[^"]*"\([^"]*\)".*/\1/p' "$acf" | head -1)"
      [ -n "$dir" ] && CANDS+=("$l/steamapps/common/$dir")
    fi
    c="$(ci "$l/steamapps/common" "$N")" && CANDS+=("$c")
  done
  CANDS+=("$HOME/GOG Games/$N" "$HOME/GOG Games/Terra Nova - Strike Force Centauri" "$HOME/Games/Heroic/$N"
          "$HOME/Games/Heroic/Terra Nova - Strike Force Centauri" "$HOME/Games/$N" "$HOME/Games/terra-nova-strike-force-centauri")
  for c in "${CANDS[@]}"; do
    [ -d "$c" ] || continue
    if check_game "$c"; then GAME="$(cd "$c" && pwd)"; break; fi
    [ -z "$REFUSED" ] && REFUSED="$WHY"
  done
  if [ -z "$GAME" ]; then
    if [ -n "$REFUSED" ]; then
      say "      Found, but TN+ cannot use it: $REFUSED"
      say ""
    else
      say "      Not found in the usual Steam and GOG places. Steam libraries looked into:${SEEN:- none found}"
    fi
    [ -t 0 ] || die "the game was not found and there is no terminal to ask for its folder. Give it like this:  bash tnplus.sh \"/path/to/$N\""
    while [ -z "$GAME" ]; do
      [ -n "$REFUSED" ] && say "      Is the game somewhere else as well?"
      say "      Type the game's folder (or drag it from your file manager into this window), then press Enter."
      say "      It is the folder that contains TNOVA and _DOSBOX. Just Enter to stop."
      read -r -e -p "      > " ANSWER || ANSWER=""
      ANSWER="${ANSWER%"${ANSWER##*[! ]}"}"; ANSWER="${ANSWER#"${ANSWER%%[! ]*}"}"
      ANSWER="${ANSWER%\"}"; ANSWER="${ANSWER#\"}"; ANSWER="${ANSWER%\'}"; ANSWER="${ANSWER#\'}"
      case "$ANSWER" in "~"|"~/"*) ANSWER="$HOME${ANSWER#\~}" ;; esac
      [ -n "$ANSWER" ] || die "no game folder. Nothing was changed."
      ANSWER="$(tidy "$ANSWER")"
      if check_game "$ANSWER"; then GAME="$ANSWER"; else say "      No: $WHY"; say ""; fi
    done
  fi
fi
say "      ok: $GAME"
ci "$GAME" TNOVA.BAT > /dev/null || say "      Note: there is no TNOVA.BAT in that folder, the file the game's DOSBox starts. If the game does not start, tell me what is in the folder."
[ -w "$(ci "$GAME" TNOVA)" ] || say "      Warning: this folder cannot be written to. TN+ prepares the game there (it keeps a copy of the original) and the game saves there."

# ---------------------------------------------------------------- 4. settings and start
export WINEPREFIX="${TNPLUS_PREFIX:-${XDG_DATA_HOME:-$HOME/.local/share}/terra-nova-plus/wine}"
export WINEDEBUG="${WINEDEBUG:--all}"
if [ ! -d "$WINEPREFIX" ]; then
  say ""
  say "      First start: preparing Wine (about a minute)."
  say "      >>> If a window asks to install \"Wine Mono\", click Install. TN+ needs it. <<<"
  mkdir -p "$(dirname "$WINEPREFIX")"
  "$WINE" wineboot -i > /dev/null 2>&1
  command -v wineserver > /dev/null 2>&1 && wineserver -w
fi

# The game's folder goes into TNPlus.ini as a Windows path (TN+ cannot look it up in a registry here). The path is
# built from the prefix's own drive letters: the one that shows the whole Linux disk (Z: as a rule), made if missing.
DEV="$WINEPREFIX/dosdevices"
[ -d "$DEV" ] || die "the Wine prefix $WINEPREFIX has no dosdevices folder: Wine could not prepare it. Delete that folder and run the script again."
DRIVE=""
for l in z y x w v u t s r q p o n m l k j i h g f e d; do
  [ -L "$DEV/$l:" ] && [ "$(readlink -f "$DEV/$l:")" = "/" ] && { DRIVE="$l"; break; }
done
if [ -z "$DRIVE" ]; then
  for l in z y x w v u t s r q p o n m l k j i h g; do
    [ -e "$DEV/$l:" ] || [ -L "$DEV/$l:" ] || { ln -s / "$DEV/$l:" && DRIVE="$l"; break; }
  done
  [ -n "$DRIVE" ] || die "no free drive letter in the Wine prefix $WINEPREFIX."
  say "      (drive ${DRIVE^^}: added to the Wine prefix so that TN+ can see the game's folder)"
fi
WGAME="${DRIVE^^}:${GAME//\//\\}"
[ -e "$DEV/$DRIVE:$GAME/_DOSBOX" ] || [ -n "$(ci "$DEV/$DRIVE:$GAME" _DOSBOX)" ] || die "Wine cannot see the game's folder as $WGAME."

INI="$EXE_DIR/TNPlus.ini"
[ ! -e "$INI" ] || [ -w "$INI" ] || die "$INI is read-only: the script must write the game's folder and your settings there. Make it writable (chmod u+w) and start again."
ini_get() { [ -f "$INI" ] && sed -n "s/^$1 *= *//p" "$INI" | tr -d '\r' | tail -1; }
ini_set() {                               # ini_set KEY VALUE (through the environment: awk -v would read the backslashes as escapes)
  local tmp
  if [ -f "$INI" ] && grep -q "^$1 *=" "$INI"; then
    tmp="$(mktemp)"
    K="$1" V="$2" awk 'BEGIN { k = ENVIRON["K"] } index($0, k) == 1 && substr($0, length(k) + 1) ~ /^ *=/ { printf "%s = %s\r\n", k, ENVIRON["V"]; next } { print }' "$INI" > "$tmp" && cat "$tmp" > "$INI"
    rm -f "$tmp"
  else
    [ -s "$INI" ] && [ -n "$(tail -c 1 "$INI")" ] && printf '\r\n' >> "$INI"
    printf '%s = %s\r\n' "$1" "$2" >> "$INI"
  fi
}
ini_set game_dir "$WGAME"

# TN+'s window is taller than a small screen (a Steam Deck is 800 pixels high) and cannot be resized: there, the
# script asks the main choice itself and starts the game without the window.
screen_h() {
  local h=""
  command -v xrandr > /dev/null 2>&1 && h="$(xrandr --current 2> /dev/null | sed -n 's/.* connected[a-z ]* [0-9]*x\([0-9]*\)+.*/\1/p' | sort -n | head -1)"
  [ -z "$h" ] && command -v xdpyinfo > /dev/null 2>&1 && h="$(xdpyinfo 2> /dev/null | sed -n 's/.*dimensions: *[0-9]*x\([0-9]*\) pixels.*/\1/p' | head -1)"
  printf '%s' "$h"
}
if [ ${#ARGS[@]} -gt 0 ]; then
  UI="pass"                               # the caller's own TNPlus.exe options: nothing is asked
elif [ "$UI" = "auto" ]; then
  H="$(screen_h)"
  if [ -n "$H" ] && [ "$H" -ge "$WINDOW_H" ] 2> /dev/null; then UI="window"; else UI="terminal"; fi
  [ "$UI" = "terminal" ] && [ -n "$H" ] && SMALL="your screen is $H pixels high, TN+'s window needs about $WINDOW_H"
fi

if [ "$UI" = "terminal" ]; then
  step "4/4" "Settings"
  [ -n "${SMALL:-}" ] && say "      ($SMALL: the questions are asked here instead.)"
  D="$(ini_get display)"; W="$(ini_get widescreen)"; TD="$(ini_get terrain_detail)"; VD="$(ini_get view_distance)"
  case "${W:-3}" in 0) W="off" ;; 1) W="stretched" ;; 2) W="true 16:9 (848x480)" ;; *) W="true 16:9 (1280x720)" ;; esac
  say "      Now: display ${D:-HD}, widescreen $W, terrain detail ${TD:-SHARPER}, view distance ${VD:-FAR}"
  if [ -t 0 ]; then
    say "        1  BEST       HD 1280x720 in true 16:9, far view, max detail (wants a fast machine)"
    say "        2  CLASSIC+   sharper 320x400, freelook, a bit more view and detail"
    say "        3  ORIGINAL   the game as it was"
    say "        Enter         keep the settings as they are"
    say "        w             TN+'s own window with every setting (if its buttons are off screen: Enter is Launch)"
    say "        q             stop here"
    read -r -p "      > " CH || CH=""
    case "${CH,,}" in
      1) ini_set display HD;       ini_set widescreen 3; ini_set terrain_detail SHARPER; ini_set view_distance FAR;    ini_set freelook 1 ;;
      2) ini_set display 320X400;  ini_set widescreen 0; ini_set terrain_detail SHARP;   ini_set view_distance FAR;    ini_set freelook 1 ;;
      3) ini_set display ORIGINAL; ini_set widescreen 0; ini_set terrain_detail GAME;    ini_set view_distance NORMAL; ini_set freelook 0 ;;
      w) UI="window" ;;
      q) say "      Stopped. Nothing was started."; exit 0 ;;
      "") ;;
      *) say "      '$CH' is not one of the choices: the settings stay as they are." ;;
    esac
  else
    say "      (no terminal to ask in: the settings stay as they are)"
  fi
  say "      The other settings (keys, music, fixes, field of view...) are lines of $INI"
fi

cd "$EXE_DIR" || die "cannot enter $EXE_DIR"
if [ "$UI" = "terminal" ]; then
  say ""
  say "      Starting the game. If it shows a small text menu first, press 1 to play."
  say "      Leave this terminal open while you play: TN+ works from here."
  "$WINE" TNPlus.exe --launch
else
  [ "$UI" = "window" ] && step "4/4" "Starting Terra Nova Plus..."
  [ "$UI" = "window" ] && say "      In TN+'s window: pick your settings, then click Launch (or press Enter)."
  [ "$UI" = "window" ] && say "      If the game shows a small text menu first, press 1 to play."
  "$WINE" TNPlus.exe ${ARGS[@]+"${ARGS[@]}"}
fi
RC=$?
if [ $RC -ne 0 ]; then
  say ""
  say "TN+ stopped (code $RC). If nothing ever showed up, it is one of these two:"
  say "  - Wine Mono is missing. Delete the folder $WINEPREFIX, run this script again and click Install"
  say "    when Wine offers Mono (or install your system's wine-mono package)."
  say "  - No font is installed (\"FontFamilyNotFound\"). Install one, for example: sudo apt install fonts-liberation"
  say "Still stuck? Tell me what you see: https://github.com/$REPO/issues"
fi
exit $RC
