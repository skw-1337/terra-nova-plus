// Terra Nova Plus tests - game keys and tool keys (src/TNPlusKeys.cs; GetKey / SetKey / KEY_RESERVED / KeyName of TNPlus.cs):
// the KEYBOARD tab's command table, its ini lines, key and mouse assignment, layout presets, the key map given to the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ================================================================== helpers (all named Keys*)
        const string KeysExtMsg = "PgUp / PgDn, Home / End, Ins / Del go on the green badge, as a second bind (the arrows are the game's own)";
        const string KeysBadMsg = "that key can't be used: pick another one";
        const string KeysReservedMsg = "that key is taken by the game or the tool (Esc, O, noclip up / down, Ctrl, Shift, Alt...): pick another one";

        static GameKey KeysG(string id)
        {
            GameKey g = FindGameKey(id);
            if (g == null) Fail("no game command \"" + id + "\"");
            return g;
        }

        // the state of a first run (LoadSettings: previous target moved off Y, freelook's key, to K), key names from
        // TN+'s own QWERTY table (not from the Windows keyboard)
        static void KeysStart() { ResetKeys(); ChosenLayout = 1; }

        // the program's start values (what LoadSettings starts from)
        static void KeysFresh()
        {
            foreach (GameKey g in GameKeys) { g.Sc = g.DefSc; g.Mod = g.DefMod; g.Mouse = 0; }
            for (int i = 0; i < ToolMouse.Length; i++) ToolMouse[i] = 0;
            ScanFreelook = 0x15; ScanNoclip = 0x16; ScanDistance = 0x24; ScanStereo = 0x41;
            ChosenLayout = -1;
        }

        static string KeysChord(int sc, int mod) { return sc.ToString("X2") + "/" + mod; }

        static int[,] KeysNow()
        {
            int[,] ch = new int[GameKeys.Count, 2];
            for (int i = 0; i < GameKeys.Count; i++) { ch[i, 0] = GameKeys[i].Sc; ch[i, 1] = GameKeys[i].Mod; }
            return ch;
        }

        // the commands of a chord table that differ from the game's own keys: "id=SC/mod ..." in table order
        static string KeysDiff(int[,] ch)
        {
            List<string> l = new List<string>();
            for (int i = 0; i < GameKeys.Count; i++)
                if (ch[i, 0] != GameKeys[i].DefSc || ch[i, 1] != GameKeys[i].DefMod) l.Add(GameKeys[i].Id + "=" + KeysChord(ch[i, 0], ch[i, 1]));
            return string.Join(" ", l.ToArray());
        }

        // everything the KEYBOARD tab keeps
        static string KeysSnap()
        {
            StringBuilder sb = new StringBuilder();
            foreach (GameKey g in GameKeys) sb.Append(g.Id).Append('=').Append(KeysChord(g.Sc, g.Mod)).Append(" m").Append(g.Mouse).Append(", ");
            for (int i = 0; i < 4; i++) sb.Append(TOOL_IDS[i]).Append('=').Append(GetKey(i).ToString("X2")).Append(" m").Append(ToolMouse[i]).Append(", ");
            return sb.Append("layout=").Append(ChosenLayout).ToString();
        }

        // TNPlus.ini written, the tool started again (start values), the ini read back
        static void KeysRestart()
        {
            Display = 0;                            // ORIGINAL: no HD check of a game folder on reading
            SaveSettings();
            KeysFresh();
            LoadSettings();
        }

        // LoadSettings on this TNPlus.ini; returns what it printed
        static string KeysLoad(params string[] lines)
        {
            File.WriteAllText(IniPath, string.Join("\r\n", lines) + "\r\n");
            TextWriter old = Console.Out;
            StringWriter said = new StringWriter();
            Console.SetOut(said);
            try { LoadSettings(); }
            finally { Console.SetOut(old); }
            return said.ToString();
        }

        // the rules the KEYBOARD tab keeps whatever the player does
        static void KeysCheck(string when)
        {
            for (int i = 0; i < 4; i++)
            {
                int k = GetKey(i);
                if (k <= 0 || k >= 0x80 || Array.IndexOf(KEY_RESERVED, k) >= 0) Fail(when + ": " + KEY_WHAT[i] + " on scancode 0x" + k.ToString("X2"));
                for (int j = 0; j < i; j++) if (GetKey(j) == k) Fail(when + ": " + KEY_WHAT[j] + " and " + KEY_WHAT[i] + " on one key 0x" + k.ToString("X2"));
            }
            Dictionary<int, string> seen = new Dictionary<int, string>();
            foreach (GameKey g in GameKeys)
            {
                if (g.Mod < 0 || g.Mod > 3 || g.Sc <= 0 || g.Sc > 0xFF) Fail(when + ": " + g.Id + " on " + KeysChord(g.Sc, g.Mod));
                int c = g.Sc * 4 + g.Mod;
                if (seen.ContainsKey(c)) Fail(when + ": " + seen[c] + " and " + g.Id + " both on " + KeysChord(g.Sc, g.Mod));
                seen[c] = g.Id;
                if (g.Mod == 0 && g.Sc < 0x80 && ToolKeyOwner(g.Sc, -1) >= 0) Fail(when + ": " + g.Id + " on the key of " + KEY_WHAT[ToolKeyOwner(g.Sc, -1)]);
                if (g.Fixed && g.Changed) Fail(when + ": fixed command " + g.Id + " moved to " + KeysChord(g.Sc, g.Mod));
                if (g.Sc >= 0x80 && !GameKeys.Exists(d => d.DefSc == g.Sc && d.DefMod == g.Mod)) Fail(when + ": " + g.Id + " on an extended key no command has: " + KeysChord(g.Sc, g.Mod));
            }
            for (int m = 1; m < MOUSE_INI.Length; m++)
            {
                int n = GameKeys.FindAll(g => g.Mouse == m).Count + Array.FindAll(ToolMouse, t => t == m).Length;
                if (n > 1) Fail(when + ": " + MOUSE_INI[m] + " bound " + n + " times");
            }
        }

        // the key map as 512 words (index scancode * 4 + modifier)
        static int[] KeysMap()
        {
            byte[] b = BuildKeyMap();
            Eq(1024, b.Length, "key map size");
            int[] m = new int[512];
            for (int i = 0; i < 512; i++) m[i] = b[2 * i] | (b[2 * i + 1] << 8);
            return m;
        }

        // the key map does what the badges say: a moved command's chord gives the game its own chord, a command left
        // in place gets nothing (the game reads it), the game's chord of a moved command left free is taken away
        static void KeysCheckMap(string when)
        {
            int[] map = KeysMap();
            bool[] used = new bool[512];
            foreach (GameKey g in GameKeys)
            {
                if (g.Sc >= 0x80) continue;         // extended keys: read by the game's handler apart, never translated
                int at = g.Sc * 4 + g.Mod;
                used[at] = true;
                int want = g.Changed && !g.Fixed ? 0x8000 | g.DefSc | (g.DefMod << 8) : 0;
                if (map[at] != want) Fail(when + ": key map at " + KeysChord(g.Sc, g.Mod) + " (" + g.Id + "): expected 0x" + want.ToString("X4") + ", got 0x" + map[at].ToString("X4"));
            }
            for (int i = 0; i < 512; i++)
            {
                if (used[i]) continue;
                int at = i;
                int want = GameKeys.Exists(g => g.Changed && g.DefSc < 0x80 && g.DefSc * 4 + g.DefMod == at) ? 0x80FF : 0;
                if (map[i] != want) Fail(when + ": key map at " + KeysChord(i / 4, i % 4) + " (no command): expected 0x" + want.ToString("X4") + ", got 0x" + map[i].ToString("X4"));
            }
        }

        // ================================================================== the command table
        [Test] static void Keys_Table_CommandsAndIds()
        {
            Eq(60, GameKeys.Count, "commands of the game's key card");
            List<string> ids = new List<string>(), names = new List<string>();
            foreach (GameKey g in GameKeys)
            {
                Ok(!ids.Contains(g.Id), "id used twice: " + g.Id);
                ids.Add(g.Id);
                // ini lines key_game_<id> / mouse_<id> are read lowercased, and mouse_ shares its names with the tool's
                Ok(Regex.IsMatch(g.Id, "^[a-z0-9_]+$"), "id fit for an ini key name: " + g.Id);
                Ok(Array.IndexOf(TOOL_IDS, g.Id) < 0, "id also a tool action of mouse_ lines: " + g.Id);
                string n = g.Name.ToLowerInvariant();  // messages: "<name>: ...", "taken from <name>"
                Ok(!names.Contains(n) && Array.IndexOf(KEY_WHAT, n) < 0, "name used twice: " + g.Name);
                names.Add(n);
                Ok(g.Sc == g.DefSc && g.Mod == g.DefMod && g.Mouse == 0 && !g.Changed, g.Id + " starts on its default, no mouse bind");
            }
            No(KeysChanged(), "nothing changed at start");
        }

        [Test] static void Keys_Table_DefaultChordsUnique()
        {
            Dictionary<int, string> seen = new Dictionary<int, string>();
            foreach (GameKey g in GameKeys)
            {
                int c = g.DefSc * 4 + g.DefMod;
                Ok(!seen.ContainsKey(c), g.Id + " and " + (seen.ContainsKey(c) ? seen[c] : "") + " share a default chord");
                seen[c] = g.Id;
                Ok(g.DefMod == 0 || g.DefMod == 2 || g.DefMod == 3, g.Id + ": the game uses no Shift chord (mod " + g.DefMod + ")");
                Ok(g.DefSc > 0 && g.DefSc < 0x100, g.Id + " scancode");
            }
            // the only extended (E0) keys are the drone's, with Ctrl
            List<string> ext = new List<string>();
            foreach (GameKey g in GameKeys) if (g.DefSc >= 0x80) ext.Add(g.Id + " " + ChordName(g.DefSc, g.DefMod));
            EqList(new[] { "d_up CTRL+PGUP", "d_down CTRL+PGDN", "d_recall CTRL+HOME" }, ext, "extended keys of the game");
            EqList(new[] { "", "SHIFT", "CTRL", "ALT" }, MOD_NAMES, "modifier names (ini and badges)");
        }

        [Test] static void Keys_Table_GroupsInOrder()
        {
            EqList(new[] { "MOVEMENT", "VIEW", "WEAPONS", "SQUAD", "MAP", "DRONE", "SYSTEM" }, KEY_GROUPS, "groups");
            int[] count = new int[KEY_GROUPS.Length];
            int last = 0;
            foreach (GameKey g in GameKeys)
            {
                int i = Array.IndexOf(KEY_GROUPS, g.Group);
                Ok(i >= 0, g.Id + ": unknown group " + g.Group);
                Ok(i >= last, g.Id + ": group " + g.Group + " after " + KEY_GROUPS[last] + " (the KEYBOARD tab lists them in table order)");
                last = i; count[i]++;
            }
            EqList(new[] { 9, 11, 7, 16, 8, 3, 6 }, count, "commands per group");
        }

        [Test] static void Keys_Table_FixedCommands()
        {
            List<string> fixedIds = new List<string>();
            foreach (GameKey g in GameKeys) if (g.Fixed) fixedIds.Add(g.Id + " " + ChordName(g.DefSc, g.DefMod));
            EqList(new[] { "options O", "abort CTRL+ESC" }, fixedIds, "commands that can't be changed");
            // a fixed command is on a reserved key or a chord: a tool key (plain, never reserved) never meets one, so
            // AssignToolKey's "is the game's ... key" refusal is only a safety net
            foreach (GameKey g in GameKeys)
                if (g.Fixed) Ok(g.DefMod != 0 || Array.IndexOf(KEY_RESERVED, g.DefSc) >= 0, g.Id + " reserved or with a modifier");
        }

        [Test] static void Keys_Table_ToolKeys()
        {
            EqList(new[] { "freelook", "noclip", "view distance", "stereo swap" }, KEY_WHAT, "tool actions");
            EqList(new[] { "freelook", "noclip", "distance", "stereo" }, TOOL_IDS, "their ini names (mouse_)");
            EqList(new[] { 0x15, 0x16, 0x24, 0x41 }, new[] { GetKey(0), GetKey(1), GetKey(2), GetKey(3) }, "default keys Y U J F7");
            for (int i = 0; i < 4; i++) Ok(Array.IndexOf(KEY_RESERVED, GetKey(i)) < 0, KEY_WHAT[i] + " default not reserved");
            SetKey(0, 0x30); SetKey(1, 0x31); SetKey(2, 0x32); SetKey(3, 0x33);
            EqList(new[] { 0x30, 0x31, 0x32, 0x33 }, new[] { ScanFreelook, ScanNoclip, ScanDistance, ScanStereo }, "SetKey writes the KEY_WHAT order");
            EqList(new[] { 0x30, 0x31, 0x32, 0x33 }, new[] { GetKey(0), GetKey(1), GetKey(2), GetKey(3) }, "GetKey reads it back");
            EqList(new[] { 0x01, 0x18, 0x39, 0x1D, 0x2A, 0x36, 0x38 }, KEY_RESERVED, "reserved: Esc O Space LCtrl LShift RShift Alt");
            foreach (int m in MODIFIER_SC) Ok(Array.IndexOf(KEY_RESERVED, m) >= 0, "modifier 0x" + m.ToString("X2") + " reserved for the tool too");
        }

        [Test] static void Keys_Table_OnlyPreviousTargetOnAToolKey()
        {
            List<string> clash = new List<string>();
            foreach (GameKey g in GameKeys) if (g.DefMod == 0 && ToolKeyOwner(g.DefSc, -1) >= 0) clash.Add(g.Id + " " + KEY_WHAT[ToolKeyOwner(g.DefSc, -1)]);
            EqList(new[] { "tprev freelook" }, clash, "the game's plain keys on the tool's default keys");
        }

        [Test] static void Keys_Table_SpareKeys()
        {
            ChosenLayout = 1;
            EqList(new[] { 0x25, 0x26, 0x27, 0x08, 0x09, 0x0A, 0x0B }, SPARE_KEYS, "spare keys");
            List<string> names = new List<string>();
            foreach (int s in SPARE_KEYS) names.Add(ScanName(s));
            EqList(new[] { "K", "L", ";", "7", "8", "9", "0" }, names, "spare key names");
            for (int i = 0; i < SPARE_KEYS.Length; i++)
            {
                int s = SPARE_KEYS[i];
                Ok(Array.IndexOf(SPARE_KEYS, s) == i, "spare listed twice: " + names[i]);
                Ok(Array.IndexOf(KEY_RESERVED, s) < 0, "spare reserved: " + names[i]);
                Ok(ToolKeyOwner(s, -1) < 0, "spare is a default tool key: " + names[i]);
                for (int m = 0; m < 4; m++) Ok(GameKeyOwner(s, m, null) == null, "spare " + names[i] + " is a default chord with mod " + m);
            }
        }

        [Test] static void Keys_Table_MouseLists()
        {
            int n = MOUSE_NAMES.Length;
            Eq(12, n, "mouse binds (none, 5 mouse inputs, 6 extended keys)");
            Eq(n, MOUSE_INI.Length, "MOUSE_INI length"); Eq(n, MOUSE_LONG.Length, "MOUSE_LONG length"); Eq(n, BIND_VK.Length, "BIND_VK length");
            Eq(TOOL_IDS.Length, ToolMouse.Length, "a mouse bind per tool action");
            Eq("", MOUSE_INI[0], "index 0 is no bind");
            for (int i = 1; i < n; i++)
            {
                Ok(MOUSE_NAMES[i].Length > 0 && MOUSE_NAMES[i].Length <= 3, "badge text fits: " + MOUSE_NAMES[i]);
                Ok(Array.IndexOf(MOUSE_INI, MOUSE_INI[i]) == i && MOUSE_INI[i] == MOUSE_INI[i].ToUpperInvariant(), "ini name unique, upper case: " + MOUSE_INI[i]);
                Eq(i >= BIND_KEYS, BIND_VK[i] != 0, "a virtual key for the extended keys only, " + MOUSE_INI[i]);
            }
            EqList(new[] { "PGUP", "PGDN", "HOME", "END", "INS", "DEL" }, new List<string>(MOUSE_INI).GetRange(BIND_KEYS, 6), "extended key binds");
            EqList(new[] { 0x21, 0x22, 0x24, 0x23, 0x2D, 0x2E }, new List<int>(BIND_VK).GetRange(BIND_KEYS, 6), "their VK_PRIOR / NEXT / HOME / END / INSERT / DELETE");
        }

        [Test] static void Keys_Table_LetterShortcuts()
        {
            int n = LETTER_KEYS.GetLength(0);
            Eq(20, n, "letter shortcuts");
            for (int i = 0; i < n; i++)
            {
                GameKey g = KeysG(LETTER_KEYS[i, 0]);
                string l = LETTER_KEYS[i, 1];
                Ok(!g.Fixed && Regex.IsMatch(l, "^[A-Z]$"), g.Id + ": one letter, not a fixed command");
                // the game's shortcuts are where QWERTY prints their letter: the QWERTY preset leaves them
                Eq(g.DefSc, LetterSc(1, l[0]), g.Id + " (" + l + ") default key is QWERTY's " + l);
                Ok(g.DefMod == 0 || g.DefMod == 3, g.Id + ": plain or Alt");
            }
        }

        // ================================================================== finding, names
        [Test] static void Keys_FindGameKey()
        {
            Eq("Forward", KeysG("fwd").Name, "fwd");
            Eq("Abort mission", KeysG("abort").Name, "abort");
            Ok(FindGameKey("FWD") == null, "ids are case-sensitive (LoadSettings lowercases the ini)");
            Ok(FindGameKey("") == null && FindGameKey("nosuch") == null && FindGameKey("freelook") == null, "unknown id: null");
            Ok(ReferenceEquals(GameKeys[0], FindGameKey("fwd")), "the table's own object");
        }

        [Test] static void Keys_PlainKey()
        {
            Eq(0x11, PlainKey("fwd", 0x99), "plain command: its key");
            Eq(0x99, PlainKey("o_attack", 0x99), "Alt chord: the default given");
            Eq(0x99, PlainKey("d_up", 0x99), "Ctrl + extended key: the default given");
            Eq(0x99, PlainKey("nosuch", 0x99), "unknown: the default given");
            KeysStart();
            AssignGameKey(KeysG("fwd"), 0x1A, 0, false);
            Eq(0x1A, PlainKey("fwd", 0x11), "follows a change");
            AssignGameKey(KeysG("fwd"), 0x1A, 2, false);
            Eq(0x11, PlainKey("fwd", 0x11), "moved to a chord: the default given");
            Eq(0x25, PlainKey("tprev", 0x15), "previous target on K after the start");
        }

        [Test] static void Keys_ScanName_SpecialKeys()
        {
            int[] sc = { 0xC9, 0xD1, 0xC7, 0xCF, 0xC8, 0xD0, 0xCB, 0xCD, 0xD2, 0xD3, 0x01, 0x39, 0x1C, 0x0F };
            string[] names = { "PGUP", "PGDN", "HOME", "END", "UP", "DOWN", "LEFT", "RIGHT", "INS", "DEL", "ESC", "SPACE", "ENTER", "TAB" };
            foreach (int l in new[] { -1, 0, 1, 2, 4 })
            {
                ChosenLayout = l;
                for (int i = 0; i < sc.Length; i++) Eq(names[i], ScanName(sc[i]), "ScanName(0x" + sc[i].ToString("X2") + "), layout " + l);
            }
        }

        [Test] static void Keys_ScanName_FromTheQwertyTable()
        {
            ChosenLayout = 1;
            int[] sc = { 0x10, 0x1E, 0x2C, 0x27, 0x02, 0x0A, 0x0B, 0x0C, 0x0D, 0x1A, 0x1B, 0x28, 0x29, 0x2B, 0x33, 0x34, 0x35 };
            string[] names = { "Q", "A", "Z", ";", "1", "9", "0", "-", "=", "[", "]", "'", "`", "\\", ",", ".", "/" };
            for (int i = 0; i < sc.Length; i++) Eq(names[i], ScanName(sc[i]), "ScanName(0x" + sc[i].ToString("X2") + ")");
            // not in the table: the Windows name (F keys from MapVirtualKey)
            Eq("F1", ScanName(0x3B), "F1"); Eq("F10", ScanName(0x44), "F10"); Eq("F11", ScanName(0x57), "F11"); Eq("F12", ScanName(0x58), "F12");
        }

        [Test] static void Keys_ChordName()
        {
            ChosenLayout = 1;
            Eq("W", ChordName(0x11, 0), "plain");
            Eq("SHIFT+W", ChordName(0x11, 1), "Shift");
            Eq("CTRL+F1", ChordName(0x3B, 2), "Ctrl");
            Eq("ALT+X", ChordName(0x2D, 3), "Alt (exit game)");
            Eq("CTRL+ESC", ChordName(0x01, 2), "abort mission");
            Eq("CTRL+PGUP", ChordName(0xC9, 2), "drone up");
            Eq("SPACE", ChordName(0x39, 0), "jump jets");
            List<string> card = new List<string>();
            foreach (string id in new[] { "o_attack", "o_follow", "d_recall", "objectives", "help" }) card.Add(ChordName(KeysG(id).Sc, KeysG(id).Mod));
            EqList(new[] { "ALT+A", "ALT+M", "CTRL+HOME", "F8", "/" }, card, "as on the game's key card");
        }

        [Test] static void Keys_KeyName_FollowsThePresetPicked()
        {
            ChosenLayout = 2;                       // AZERTY
            Eq("A", KeyName(0x10), "AZERTY 0x10"); Eq("W", KeyName(0x2C), "AZERTY 0x2C"); Eq("M", KeyName(0x27), "AZERTY 0x27");
            Eq(",", KeyName(0x32), "AZERTY 0x32"); Eq("1", KeyName(0x02), "AZERTY digits as printed shifted");
            Eq("F1", KeyName(0x3B), "not in the table: Windows' name");
            ChosenLayout = 3;                       // QWERTZ
            Eq("Z", KeyName(0x15), "QWERTZ 0x15"); Eq("Y", KeyName(0x2C), "QWERTZ 0x2C"); Eq("\u00df", KeyName(0x0C), "QWERTZ 0x0C");
            ChosenLayout = 4;                       // DVORAK
            Eq("'", KeyName(0x10), "DVORAK 0x10"); Eq("P", KeyName(0x13), "DVORAK 0x13"); Eq("Q", KeyName(0x2D), "DVORAK 0x2D");
            ChosenLayout = 5;                       // COLEMAK
            Eq("F", KeyName(0x12), "COLEMAK 0x12"); Eq(";", KeyName(0x19), "COLEMAK 0x19"); Eq("K", KeyName(0x31), "COLEMAK 0x31");
        }

        [Test] static void Keys_KeyName_GameAndCustomUseWindows()
        {
            // GAME, CUSTOM or nothing picked: the Windows keyboard's names (the CI runner and the local shim: US)
            foreach (int l in new[] { -1, 0, 6 })
            {
                ChosenLayout = l;
                Eq(-1, DisplayLayout(), "no table for layout " + l);
                Eq("Q", KeyName(0x10), "US Windows name of 0x10, layout " + l);
                Eq("F7", KeyName(0x41), "F7, layout " + l);
                Eq("5", KeyName(0x06), "digit, layout " + l);
            }
        }

        [Test] static void Keys_DisplayLayout()
        {
            int[] chosen = { -1, 0, 1, 2, 3, 4, 5, 6, 7 };
            int[] shown = { -1, -1, 1, 2, 3, 4, 5, -1, -1 };
            for (int i = 0; i < chosen.Length; i++) { ChosenLayout = chosen[i]; Eq(shown[i], DisplayLayout(), "ChosenLayout " + chosen[i]); }
        }

        // ================================================================== ini lines: key_game_<id>
        [Test] static void Keys_ParseGameKey_PlainAndModifiers()
        {
            ParseGameKey("jump", "3F"); Eq("3F/0", KeysChord(KeysG("jump").Sc, KeysG("jump").Mod), "plain hex");
            ParseGameKey("crouch", "3f"); Eq(0x3F, KeysG("crouch").Sc, "lower case hex");
            ParseGameKey("lookup", "7"); Eq("07/0", KeysChord(KeysG("lookup").Sc, KeysG("lookup").Mod), "one digit");
            ParseGameKey("lookdn", "shift+11"); Eq("11/1", KeysChord(KeysG("lookdn").Sc, KeysG("lookdn").Mod), "shift+");
            ParseGameKey("lookl", "Ctrl+12"); Eq("12/2", KeysChord(KeysG("lookl").Sc, KeysG("lookl").Mod), "Ctrl+");
            ParseGameKey("lookr", "ALT+7F"); Eq("7F/3", KeysChord(KeysG("lookr").Sc, KeysG("lookr").Mod), "ALT+, highest scancode");
            ParseGameKey("o_attack", "1E"); Eq("1E/0", KeysChord(KeysG("o_attack").Sc, KeysG("o_attack").Mod), "no prefix: the modifier goes");
            ParseGameKey("d_up", "CTRL+11"); Eq("11/2", KeysChord(KeysG("d_up").Sc, KeysG("d_up").Mod), "a drone command on a plain key + Ctrl");
            ParseGameKey("v360", "0x31"); Eq(0x31, KeysG("v360").Sc, "0x prefix taken by Convert.ToInt32(.., 16)");
        }

        [Test] static void Keys_ParseGameKey_IgnoredValues()
        {
            string before = KeysSnap();
            ParseGameKey("fwd", "0");                // no key
            ParseGameKey("fwd", "80");               // past the 128 keys of the key map
            ParseGameKey("fwd", "FF");
            ParseGameKey("d_up", "CTRL+C9");         // an extended key: even its own default
            ParseGameKey("fwd", "FFFFFFFF");         // -1 for Convert.ToInt32(.., 16)
            ParseGameKey("options", "10");           // fixed commands
            ParseGameKey("abort", "ALT+10");
            ParseGameKey("nosuch", "10");            // unknown command
            ParseGameKey("freelook", "10");          // a tool action is not a game command
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_ParseGameKey_BadValuesThrow()
        {
            string before = KeysSnap();
            foreach (string v in new[] { "ZZ", "", "ALT+", "ALT", "CTRL+SHIFT+10", "1F 2", "W" })
            {
                string val = v;
                Throws<Exception>(delegate { ParseGameKey("fwd", val); }, "ParseGameKey(fwd, \"" + v + "\")");
            }
            Eq(before, KeysSnap(), "nothing changed (LoadSettings ignores the line)");
            ParseGameKey("nosuch", "ZZ");            // unknown or fixed: ignored before the value is read
            ParseGameKey("options", "ZZ");
        }

        [Test] static void Keys_GameKeysIni_Format()
        {
            Eq("", GameKeysIni(), "nothing changed: no line");
            KeysStart();
            Eq("key_game_tprev = 25\r\n", GameKeysIni(), "first run: previous target on K");
            Has(AssignGameKey(KeysG("jump"), 0x3F, 2, false), "jump jets: CTRL+F5", "jump to Ctrl+F5");
            AssignGameKey(KeysG("w1"), 0x3B, 1, false);
            AssignGameKey(KeysG("o_attack"), 0x0B, 3, false);
            Eq("key_game_jump = CTRL+3F\r\nkey_game_w1 = SHIFT+3B\r\nkey_game_tprev = 25\r\nkey_game_o_attack = ALT+0B\r\n", GameKeysIni(),
                "table order, two upper case hex digits, modifier prefix");
            AssignGameKey(KeysG("jump"), 0x39, 0, false);
            Eq("key_game_w1 = SHIFT+3B\r\nkey_game_tprev = 25\r\nkey_game_o_attack = ALT+0B\r\n", GameKeysIni(), "back on its default: line gone");
        }

        [Test] static void Keys_GameKeysIni_ReadBackForEveryPreset()
        {
            for (int l = 0; l < LAYOUT_COUNT; l++)
            {
                KeysStart();
                ApplyLayout(l);
                AssignGameKey(KeysG("jump"), 0x3F, 1, false);
                string want = KeysDiff(KeysNow()), ini = GameKeysIni();
                KeysFresh();
                foreach (string line in ini.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Match m = Regex.Match(line, "^key_game_([a-z0-9_]+) = ((?:SHIFT|CTRL|ALT)\\+)?[0-9A-F]{2}$");
                    Ok(m.Success, LAYOUT_NAMES[l] + ": ini line " + line);
                    ParseGameKey(m.Groups[1].Value, line.Substring(line.IndexOf(" = ") + 3));
                }
                Eq(want, KeysDiff(KeysNow()), LAYOUT_NAMES[l] + ": keys read back");
            }
        }

        // ================================================================== TNPlus.ini (LoadSettings / SaveSettings), key lines
        [Test] static void Keys_Load_KeyAndMouseLines()
        {
            string said = KeysLoad(
                "; a comment",
                "KEY_GAME_JUMP = 3f",
                "key_game_o_attack = shift+1E",
                "key_game_options = 10",
                "key_game_nosuch = 10",
                "key_game_lookup = 80",
                "key_game_crouch = ZZ",
                "key_game_stop = 1A",
                "mouse_fwd = mid",
                "MOUSE_FREELOOK = WheelUp",
                "mouse_options = M4",
                "mouse_back = LEFT");
            Eq("3F/0", KeysChord(KeysG("jump").Sc, KeysG("jump").Mod), "key names and values in any case");
            Eq("1E/1", KeysChord(KeysG("o_attack").Sc, KeysG("o_attack").Mod), "modifier");
            Eq(0x18, KeysG("options").Sc, "fixed: ignored");
            Eq(0x13, KeysG("lookup").Sc, "out of range: ignored");
            Eq(0x29, KeysG("crouch").Sc, "bad value: ignored");
            Has(said, "Ignored bad setting: key_game_crouch = ZZ", "bad value reported");
            Eq(0x1A, KeysG("stop").Sc, "lines after a bad one still read");
            HasNot(said, "nosuch", "an unknown command is ignored silently");
            HasNot(said, "lookup", "an out of range key is ignored silently");
            Eq(1, KeysG("fwd").Mouse, "mouse_fwd = mid");
            Eq(4, ToolMouse[0], "mouse_freelook = WheelUp");
            Eq(0, KeysG("options").Mouse, "fixed command: no mouse bind");
            Eq(0, KeysG("back").Mouse, "unknown mouse input: ignored");
            Eq(0x25, KeysG("tprev").Sc, "previous target moved off freelook's Y at the end");
        }

        [Test] static void Keys_Load_CommandOnAToolKeyMoves()
        {
            KeysLoad("key_freelook = 11");
            Eq(0x11, ScanFreelook, "freelook on W");
            Eq("fwd=25/0", KeysDiff(KeysNow()), "forward to K; previous target stays on Y, no longer a tool key");
            KeysFresh();
            KeysLoad("key_freelook = 11", "key_game_jump = 25");
            Eq("fwd=26/0 jump=25/0", KeysDiff(KeysNow()), "K taken by jump jets: forward to L");
        }

        [Test] static void Keys_Load_OldKeyDefaults()
        {
            KeysLoad("key_freelook = 23");          // beta 5 had freelook on H (no keys_version)
            Eq(0x15, ScanFreelook, "freelook back on Y");
            Eq(0x25, KeysG("tprev").Sc, "previous target to K");
            KeysFresh();
            KeysLoad("key_freelook = 23", "keys_version = 3");
            Eq(0x23, ScanFreelook, "H chosen with the current keys: kept");
            Eq(0x15, KeysG("tprev").Sc, "previous target stays on Y");
            Eq("o_hold=23/3", "o_hold=" + KeysChord(KeysG("o_hold").Sc, KeysG("o_hold").Mod), "Alt+H untouched");
            KeysFresh();
            KeysLoad("key_distance = 17");          // old default of view distance: I, the game's infrared
            Eq(0x24, ScanDistance, "old ini: view distance from I to J");
        }

        [Test] static void Keys_Load_LayoutName()
        {
            string[] v = { "azerty", "Colemak", "GAME", "QWERTZ", "CUSTOM", "what" };
            int[] want = { 2, 5, 0, 3, -1, -1 };
            for (int i = 0; i < v.Length; i++)
            {
                KeysFresh();
                KeysLoad("keys_layout = " + v[i]);
                Eq(want[i], ChosenLayout, "keys_layout = " + v[i]);
            }
            KeysFresh();
            KeysLoad("; nothing");
            Eq(-1, ChosenLayout, "no keys_layout");
            Eq("tprev=25/0", KeysDiff(KeysNow()), "keys_layout only names the preset: the keys come from key_game_");
        }

        [Test] static void Keys_Load_FirstRunWritesTheKeys()
        {
            No(File.Exists(IniPath), "no TNPlus.ini yet");
            LoadSettings();
            Eq("tprev=25/0", KeysDiff(KeysNow()), "previous target moved off Y");
            string ini = File.ReadAllText(IniPath);
            Has(ini, "\r\nkey_freelook = 15\r\nkey_noclip = 16\r\nkey_distance = 24\r\nkey_stereo = 41\r\nkeys_version = 3\r\n", "tool keys");
            Has(ini, "\r\nkey_game_tprev = 25\r\n", "previous target's line");
            HasNot(ini, "keys_layout", "no preset picked");
            HasNot(ini, "\r\nmouse_", "no mouse bind");
        }

        [Test] static void Keys_SaveLoad_RoundTrip()
        {
            KeysStart();
            ApplyLayout(4);
            AssignGameKey(KeysG("jump"), 0x3F, 0, false);
            AssignGameKey(KeysG("o_attack"), 0x1E, 1, false);
            AssignToolKey(3, 0x40, false);
            // DVORAK names: 0x2F is printed K, 0x15 F
            Eq("freelook: K, look down moved to F", AssignToolKey(0, 0x2F, false), "freelook on look down's key");
            AssignMouse(KeysG("fwd"), -1, 3);
            AssignMouse(null, 1, 11);
            AssignMouse(KeysG("d_up"), -1, 6);
            KeysCheck("before saving");
            string before = KeysSnap(), lines = GameKeysIni(), mouse = MouseIni();
            KeysRestart();
            Eq(before, KeysSnap(), "everything read back");
            string ini = File.ReadAllText(IniPath);
            Has(ini, "\r\nkeys_layout = DVORAK\r\n", "preset name");
            Has(ini, "\r\nkey_freelook = 2F\r\n", "freelook on V");
            Has(ini, "\r\nkey_stereo = 40\r\n", "stereo swap on F6");
            Has(ini, "\r\n" + lines, "key_game_ lines");
            Has(ini, "\r\n" + mouse, "mouse_ lines");
            Has(mouse, "mouse_noclip = DEL\r\n", "noclip on Delete");
        }

        // ================================================================== reset, tool clashes
        [Test] static void Keys_ResetKeys_EverythingBack()
        {
            KeysStart();
            ApplyLayout(5);
            AssignToolKey(0, 0x3F, false);
            AssignGameKey(KeysG("jump"), 0x3E, 3, false);
            AssignMouse(KeysG("fwd"), -1, 1);
            AssignMouse(null, 2, 4);
            ResetKeys();
            Eq(-1, ChosenLayout, "no preset");
            EqList(new[] { 0x15, 0x16, 0x24, 0x41 }, new[] { GetKey(0), GetKey(1), GetKey(2), GetKey(3) }, "tool keys");
            No(AnyMouseBind(), "no mouse bind");
            Eq("tprev=25/0", KeysDiff(KeysNow()), "the game's keys, previous target on K");
            Eq(0, CurrentLayout(), "recognized as GAME");
        }

        [Test] static void Keys_Resolve_FirstFreeSpare()
        {
            ResolveToolClashes();
            Eq("tprev=25/0", KeysDiff(KeysNow()), "previous target from Y to K");
            string once = KeysSnap();
            ResolveToolClashes();
            Eq(once, KeysSnap(), "a second time: nothing moves");
        }

        [Test] static void Keys_Resolve_SkipsUsedSpares()
        {
            SetKey(2, 0x25);                         // view distance on K
            ResolveToolClashes();
            Eq("tprev=26/0", KeysDiff(KeysNow()), "K is a tool key: L");
            KeysFresh();
            KeysG("jump").Sc = 0x25; KeysG("crouch").Sc = 0x26;
            KeysG("o_attack").Sc = 0x27;             // ALT+; does not take ;
            ResolveToolClashes();
            Eq(0x27, KeysG("tprev").Sc, "K and L taken by commands: ;");
        }

        [Test] static void Keys_Resolve_SparesInOrder()
        {
            SetKey(0, 0x11); SetKey(1, 0x1F); SetKey(2, 0x1E); SetKey(3, 0x20);   // the tool on W S A D
            ResolveToolClashes();
            Eq("fwd=25/0 turnl=26/0 turnr=27/0 stop=08/0", KeysDiff(KeysNow()), "K L ; 7 in table order, previous target stays on Y");
            KeysCheck("tool on W S A D");
        }

        [Test] static void Keys_Resolve_NoSpareLeft()
        {
            string[] ids = { "sq1", "sq2", "sq3", "sqall", "objectives", "squadinfo", "sensor" };
            for (int i = 0; i < ids.Length; i++) KeysG(ids[i]).Sc = SPARE_KEYS[i];
            ResolveToolClashes();
            Eq(0x15, KeysG("tprev").Sc, "every spare taken: previous target left on Y");
        }

        [Test] static void Keys_Resolve_OnlyPlainCommands()
        {
            SetKey(0, 0x1E);                         // freelook on A: turn left (plain) and aggressive attack (ALT+A)
            ScanNoclip = 0x18;                       // noclip on O, the game's options (only a hand-written ini can do it)
            ResolveToolClashes();
            Eq("turnl=25/0", KeysDiff(KeysNow()), "turn left to K; ALT+A, options and previous target stay");
        }

        // ================================================================== owners
        [Test] static void Keys_ToolKeyOwner()
        {
            Eq(0, ToolKeyOwner(0x15, -1), "Y freelook"); Eq(1, ToolKeyOwner(0x16, -1), "U noclip");
            Eq(2, ToolKeyOwner(0x24, -1), "J view distance"); Eq(3, ToolKeyOwner(0x41, -1), "F7 stereo swap");
            Eq(-1, ToolKeyOwner(0x11, -1), "W: none"); Eq(-1, ToolKeyOwner(0, -1), "0: none");
            Eq(-1, ToolKeyOwner(0x15, 0), "freelook left out");
            Eq(0, ToolKeyOwner(0x15, 1), "another one left out");
            ScanNoclip = 0x15;                       // two on one key (hand-written ini): the first
            Eq(0, ToolKeyOwner(0x15, -1), "the first of two"); Eq(1, ToolKeyOwner(0x15, 0), "the other");
        }

        [Test] static void Keys_GameKeyOwner()
        {
            Eq("fwd", GameKeyOwner(0x11, 0, null).Id, "W");
            Eq("o_attack", GameKeyOwner(0x1E, 3, null).Id, "ALT+A");
            Eq("turnl", GameKeyOwner(0x1E, 0, null).Id, "A");
            Eq("d_up", GameKeyOwner(0xC9, 2, null).Id, "CTRL+PGUP");
            Ok(GameKeyOwner(0x1E, 1, null) == null, "SHIFT+A: none");
            Ok(GameKeyOwner(0x11, 0, KeysG("fwd")) == null, "the one left out");
            Eq("o_attack", GameKeyOwner(0x1E, 3, KeysG("fwd")).Id, "another left out");
        }

        // ================================================================== AssignGameKey
        [Test] static void Keys_AssignGame_ExtendedKey()
        {
            KeysStart();
            string before = KeysSnap();
            Eq(KeysExtMsg, AssignGameKey(KeysG("fwd"), 0x49, 0, true), "PgUp");
            Eq(KeysExtMsg, AssignGameKey(KeysG("fwd"), 0x11, 0, true), "the extended flag first, even on its own key");
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_AssignGame_KeysThatCantBeUsed()
        {
            KeysStart();
            string before = KeysSnap();
            foreach (int sc in new[] { 0, 1, 0x80, 0xC9, 0xFF, 0x1D, 0x2A, 0x36, 0x38 })
                foreach (int mod in new[] { 0, 2 })
                    Eq(KeysBadMsg, AssignGameKey(KeysG("fwd"), sc, mod, false), "scancode 0x" + sc.ToString("X2") + " mod " + mod);
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_AssignGame_Unchanged()
        {
            KeysStart();
            string before = KeysSnap();
            Eq("forward: unchanged", AssignGameKey(KeysG("fwd"), 0x11, 0, false), "W");
            Eq("aggressive attack: unchanged", AssignGameKey(KeysG("o_attack"), 0x1E, 3, false), "ALT+A");
            Eq("previous target: unchanged", AssignGameKey(KeysG("tprev"), 0x25, 0, false), "K");
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_AssignGame_FreeChord()
        {
            KeysStart();
            GameKey fwd = KeysG("fwd");
            Eq("forward: [", AssignGameKey(fwd, 0x1A, 0, false), "free key");
            Eq("forward: SHIFT+W", AssignGameKey(fwd, 0x11, 1, false), "Shift");
            Eq("forward: CTRL+W", AssignGameKey(fwd, 0x11, 2, false), "Ctrl");
            Eq("forward: ALT+W", AssignGameKey(fwd, 0x11, 3, false), "Alt");
            Eq("forward: SHIFT+O", AssignGameKey(fwd, 0x18, 1, false), "Shift + the options key");
            Eq("fwd=18/1 tprev=25/0", KeysDiff(KeysNow()), "only forward moved");
            Eq("drone up: CTRL+W", AssignGameKey(KeysG("d_up"), 0x11, 2, false), "a drone command off its extended key");
            KeysCheck("free chords");
        }

        [Test] static void Keys_AssignGame_SwapsWithAnotherCommand()
        {
            KeysStart();
            Eq("forward: S, stop moved to W", AssignGameKey(KeysG("fwd"), 0x1F, 0, false), "plain with plain");
            Eq("fwd=1F/0 stop=11/0 tprev=25/0", KeysDiff(KeysNow()), "the two swapped");
            KeysStart();
            Eq("aggressive attack: ALT+S, status check moved to ALT+A", AssignGameKey(KeysG("o_attack"), 0x1F, 3, false), "Alt with Alt");
            KeysStart();
            Eq("forward: ALT+A, aggressive attack moved to W", AssignGameKey(KeysG("fwd"), 0x1E, 3, false), "plain takes a chord");
            // aggressive attack is on W now: the command on A takes W
            Eq("aggressive attack: A, turn left moved to W", AssignGameKey(KeysG("o_attack"), 0x1E, 0, false), "then on a plain key");
            Eq("fwd=1E/3 turnl=11/0 tprev=25/0 o_attack=1E/0", KeysDiff(KeysNow()), "W -> ALT+A -> A -> W");
            KeysCheck("swaps");
        }

        [Test] static void Keys_AssignGame_RefusedOnAFixedCommand()
        {
            KeysStart();
            string before = KeysSnap();
            Eq("O is the game's options key: pick another one", AssignGameKey(KeysG("fwd"), 0x18, 0, false), "options' O");
            Eq("O is the game's options key: pick another one", AssignGameKey(KeysG("o_attack"), 0x18, 0, false), "from a chord too");
            Eq(KeysBadMsg, AssignGameKey(KeysG("fwd"), 0x01, 2, false), "abort's CTRL+ESC: Esc never");
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_AssignGame_ToolKeyRefused()
        {
            KeysStart();
            string before = KeysSnap();
            // the tool key would have to take the command's old key: not a chord, an extended or a reserved key
            Eq("Y is Terra Nova Plus freelook: change that one first", AssignGameKey(KeysG("o_attack"), 0x15, 0, false), "from ALT+A");
            Eq("U is Terra Nova Plus noclip: change that one first", AssignGameKey(KeysG("d_up"), 0x16, 0, false), "from CTRL+PGUP");
            Eq("J is Terra Nova Plus view distance: change that one first", AssignGameKey(KeysG("jump"), 0x24, 0, false), "from Space (reserved)");
            Eq("F7 is Terra Nova Plus stereo swap: change that one first", AssignGameKey(KeysG("o_hold"), 0x41, 0, false), "F7 from ALT+H");
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_AssignGame_ToolKeyMoved()
        {
            KeysStart();
            Eq("forward: Y, freelook moved to W", AssignGameKey(KeysG("fwd"), 0x15, 0, false), "freelook's Y");
            Eq(0x11, ScanFreelook, "freelook on W");
            Eq("next target: F7, stereo swap moved to T", AssignGameKey(KeysG("tnext"), 0x41, 0, false), "stereo swap's F7");
            Eq(0x14, ScanStereo, "stereo swap on T");
            Eq("fwd=15/0 tnext=41/0 tprev=25/0", KeysDiff(KeysNow()), "commands");
            KeysCheck("tool keys moved");
            string before = KeysSnap();
            ResolveToolClashes();
            Eq(before, KeysSnap(), "no clash left");
        }

        [Test] static void Keys_AssignGame_ChordOnAToolKeyAllowed()
        {
            KeysStart();
            Eq("aggressive attack: ALT+Y", AssignGameKey(KeysG("o_attack"), 0x15, 3, false), "ALT+Y");
            Eq("drone up: CTRL+U", AssignGameKey(KeysG("d_up"), 0x16, 2, false), "CTRL+U");
            Eq(0x15, ScanFreelook, "freelook stays on Y");
            Eq(0x16, ScanNoclip, "noclip stays on U");
        }

        // ================================================================== AssignToolKey
        [Test] static void Keys_AssignTool_ExtendedKey()
        {
            KeysStart();
            string before = KeysSnap();
            Eq(KeysExtMsg, AssignToolKey(0, 0x49, true), "PgUp");
            Eq(KeysExtMsg, AssignToolKey(0, 0x15, true), "the extended flag first");
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_AssignTool_ReservedKeys()
        {
            KeysStart();
            string before = KeysSnap();
            Eq(KeysReservedMsg, AssignToolKey(0, 0, false), "no scancode");
            foreach (int sc in KEY_RESERVED)
                for (int k = 0; k < 4; k++) Eq(KeysReservedMsg, AssignToolKey(k, sc, false), KEY_WHAT[k] + " on 0x" + sc.ToString("X2"));
            Eq(before, KeysSnap(), "nothing changed");
        }

        [Test] static void Keys_AssignTool_Unchanged()
        {
            KeysStart();
            Eq("freelook: unchanged", AssignToolKey(0, 0x15, false), "freelook");
            Eq("view distance: unchanged", AssignToolKey(2, 0x24, false), "view distance");
        }

        [Test] static void Keys_AssignTool_FreeKey()
        {
            KeysStart();
            Eq("stereo swap: F5", AssignToolKey(3, 0x3F, false), "F5");
            Eq("freelook: [", AssignToolKey(0, 0x1A, false), "[");
            Eq("freelook: H", AssignToolKey(0, 0x23, false), "H (hold fire is ALT+H: not H's owner)");
            EqList(new[] { 0x23, 0x16, 0x24, 0x3F }, new[] { GetKey(0), GetKey(1), GetKey(2), GetKey(3) }, "tool keys");
            Eq("tprev=25/0", KeysDiff(KeysNow()), "no command moved (ALT+H kept)");
        }

        [Test] static void Keys_AssignTool_SwapsWithAnotherToolKey()
        {
            KeysStart();
            Eq("freelook: U, noclip moved to Y", AssignToolKey(0, 0x16, false), "U");
            Eq(0x16, ScanFreelook, "freelook"); Eq(0x15, ScanNoclip, "noclip");
            Eq("stereo swap: Y, noclip moved to F7", AssignToolKey(3, 0x15, false), "Y");
            KeysCheck("tool keys swapped");
        }

        [Test] static void Keys_AssignTool_MovesAPlainCommand()
        {
            KeysStart();
            Eq("freelook: W, forward moved to Y", AssignToolKey(0, 0x11, false), "W");
            Eq("fwd=15/0 tprev=25/0", KeysDiff(KeysNow()), "forward on Y");
            Eq("noclip: K, previous target moved to U", AssignToolKey(1, 0x25, false), "K");
            Eq("view distance: A, turn left moved to J", AssignToolKey(2, 0x1E, false), "A: the plain command, not ALT+A");
            Eq("fwd=15/0 turnl=24/0 tprev=16/0", KeysDiff(KeysNow()), "commands");
            KeysCheck("commands moved by the tool");
        }

        // ================================================================== mouse binds
        [Test] static void Keys_Mouse_AssignMessages()
        {
            KeysStart();
            Eq("forward: middle click (and still W)", AssignMouse(KeysG("fwd"), -1, 1), "middle click");
            Eq("aggressive attack: side button 4 (and still ALT+A)", AssignMouse(KeysG("o_attack"), -1, 2), "side button");
            Eq("stereo swap: wheel down (and still F7)", AssignMouse(null, 3, 5), "tool action");
            Eq("drone up: Page Up (and still CTRL+PGUP)", AssignMouse(KeysG("d_up"), -1, 6), "extended key");
            Eq("previous target: Delete (and still K)", AssignMouse(KeysG("tprev"), -1, 11), "last bind");
            EqList(new[] { 1, 2, 6, 11 }, new[] { KeysG("fwd").Mouse, KeysG("o_attack").Mouse, KeysG("d_up").Mouse, KeysG("tprev").Mouse }, "binds");
            Eq(5, ToolMouse[3], "stereo swap's bind");
            Eq("tprev=25/0", KeysDiff(KeysNow()), "keys untouched");
        }

        [Test] static void Keys_Mouse_TakenFromAnother()
        {
            KeysStart();
            AssignMouse(KeysG("fwd"), -1, 1);
            Eq("back: middle click (and still X), taken from forward", AssignMouse(KeysG("back"), -1, 1), "from a command");
            Eq(0, KeysG("fwd").Mouse, "forward lost it");
            Eq("freelook: middle click (and still Y), taken from back", AssignMouse(null, 0, 1), "a tool action from a command");
            Eq(0, KeysG("back").Mouse, "back lost it");
            Eq("forward: middle click (and still W), taken from freelook", AssignMouse(KeysG("fwd"), -1, 1), "a command from a tool action");
            Eq(0, ToolMouse[0], "freelook lost it");
            Eq("forward: middle click (and still W)", AssignMouse(KeysG("fwd"), -1, 1), "its own bind again: not taken");
            AssignMouse(KeysG("fwd"), -1, 2);
            Eq(2, KeysG("fwd").Mouse, "one bind per command: the new one");
            Ok(MouseOwner(1) == null, "middle click free again");
            KeysCheck("mouse binds");
        }

        [Test] static void Keys_Mouse_Remove()
        {
            KeysStart();
            AssignMouse(KeysG("fwd"), -1, 1);
            AssignMouse(KeysG("back"), -1, 3);
            AssignMouse(null, 2, 4);
            Eq("forward: second bind removed", AssignMouse(KeysG("fwd"), -1, 0), "command");
            Eq("view distance: second bind removed", AssignMouse(null, 2, 0), "tool action");
            Eq("back: second bind removed", AssignMouse(KeysG("back"), -1, 0), "the other");
            Eq("forward: second bind removed", AssignMouse(KeysG("fwd"), -1, 0), "none to remove: same text");
            No(AnyMouseBind(), "all removed");
        }

        [Test] static void Keys_Mouse_RemoveKeepsTheOthers()
        {
            KeysStart();
            AssignMouse(KeysG("back"), -1, 3);
            AssignMouse(null, 2, 4);
            AssignMouse(KeysG("fwd"), -1, 0);
            Eq(3, KeysG("back").Mouse, "back keeps M5");
            Eq(4, ToolMouse[2], "view distance keeps the wheel");
        }

        [Test] static void Keys_MouseOwner()
        {
            Ok(MouseOwner(0) == null, "0: nobody, even with no bind");
            Ok(MouseOwner(1) == null, "free");
            KeysG("o_attack").Mouse = 2; ToolMouse[1] = 4;
            Eq("aggressive attack", MouseOwner(2), "a command: lower case name");
            Eq("noclip", MouseOwner(4), "a tool action");
            Ok(MouseOwner(0) == null, "0 still nobody");
        }

        [Test] static void Keys_ParseMouse()
        {
            ParseMouse("fwd", "mid"); Eq(1, KeysG("fwd").Mouse, "fwd mid");
            ParseMouse("back", "WheelDown"); Eq(5, KeysG("back").Mouse, "back WheelDown");
            ParseMouse("freelook", "M4"); ParseMouse("noclip", "pgup"); ParseMouse("distance", "Home"); ParseMouse("stereo", "DEL");
            EqList(new[] { 2, 6, 8, 11 }, ToolMouse, "tool actions by their ini names");
            string before = KeysSnap();
            ParseMouse("options", "M5"); ParseMouse("abort", "M5");   // fixed
            ParseMouse("nosuch", "M5"); ParseMouse("view distance", "M5");
            ParseMouse("jump", ""); ParseMouse("jump", "LEFT"); ParseMouse("jump", "WHEEL");
            Eq(before, KeysSnap(), "ignored");
            ParseMouse("jump", "MID");
            Eq(1, KeysG("jump").Mouse, "no check against the others (a hand-written ini can bind one input twice)");
        }

        [Test] static void Keys_MouseIni_FormatAndReadBack()
        {
            Eq("", MouseIni(), "no bind: no line");
            KeysG("fwd").Mouse = 1; KeysG("o_attack").Mouse = 2; ToolMouse[0] = 4; ToolMouse[3] = 11;
            Eq("mouse_fwd = MID\r\nmouse_o_attack = M4\r\nmouse_freelook = WHEELUP\r\nmouse_stereo = DEL\r\n", MouseIni(), "commands, then tool actions");
            // every input, read back
            KeysFresh();
            List<GameKey> free = GameKeys.FindAll(g => !g.Fixed);
            for (int m = 1; m < MOUSE_INI.Length; m++) if (m <= 4) ToolMouse[m - 1] = m; else free[m * 3].Mouse = m;
            string want = KeysSnap(), ini = MouseIni();
            KeysFresh();
            foreach (string line in ini.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match x = Regex.Match(line, "^mouse_([a-z0-9_]+) = ([A-Z0-9]+)$");
                Ok(x.Success, "ini line " + line);
                ParseMouse(x.Groups[1].Value, x.Groups[2].Value);
            }
            Eq(want, KeysSnap(), "read back");
        }

        [Test] static void Keys_AnyMouseBind()
        {
            No(AnyMouseBind(), "none at start");
            KeysG("fwd").Mouse = 3; Ok(AnyMouseBind(), "a command's");
            KeysG("fwd").Mouse = 0; ToolMouse[2] = 1; Ok(AnyMouseBind(), "a tool action's");
            ToolMouse[2] = 0; No(AnyMouseBind(), "none again");
        }

        // ================================================================== keyboard layouts: tables
        [Test] static void Keys_Layout_TableShapes()
        {
            EqList(new[] { "GAME", "QWERTY", "AZERTY", "QWERTZ", "DVORAK", "COLEMAK", "CUSTOM" }, LAYOUT_NAMES, "layout names");
            Eq(6, LAYOUT_COUNT, "presets"); Eq(LAYOUT_COUNT, Array.IndexOf(LAYOUT_NAMES, "CUSTOM"), "CUSTOM after the presets");
            Eq(LAYOUT_COUNT - 1, LAYOUT_ROWS.Length, "letter rows per keyboard"); Eq(LAYOUT_COUNT - 1, LAYOUT_PUNCT.Length, "punctuation per keyboard");
            int[] start = { 0x10, 0x1E, 0x2C };
            for (int l = 1; l < LAYOUT_COUNT; l++)
            {
                string[] rows = LAYOUT_ROWS[l - 1];
                Eq(3, rows.Length, LAYOUT_NAMES[l] + " rows");
                Eq(10, rows[0].Length, LAYOUT_NAMES[l] + " top row Q..P");
                foreach (string r in rows) Ok(r.Length <= 10, LAYOUT_NAMES[l] + " row " + r);
                Eq(PUNCT_SC.Length, LAYOUT_PUNCT[l - 1].Length, LAYOUT_NAMES[l] + " punctuation entries");
                string letters = string.Join("", rows);
                for (char c = 'A'; c <= 'Z'; c++)
                    Ok(letters.IndexOf(c) >= 0 && letters.IndexOf(c) == letters.LastIndexOf(c), LAYOUT_NAMES[l] + ": letter " + c + " once");
                // a punctuation entry is "" exactly where a letter row covers the key (rows come first)
                for (int i = 0; i < PUNCT_SC.Length; i++)
                {
                    bool inRow = false;
                    for (int r = 0; r < 3; r++) inRow |= PUNCT_SC[i] >= start[r] && PUNCT_SC[i] < start[r] + rows[r].Length;
                    Eq(inRow, LAYOUT_PUNCT[l - 1][i] == "", LAYOUT_NAMES[l] + ": entry of 0x" + PUNCT_SC[i].ToString("X2") + " empty");
                }
            }
        }

        [Test] static void Keys_LayoutKeyName_NamesUniquePerKeyboard()
        {
            for (int l = 1; l < LAYOUT_COUNT; l++)
            {
                Dictionary<string, int> seen = new Dictionary<string, int>();
                for (int sc = 0; sc < 0x100; sc++)
                {
                    string n = LayoutKeyName(l, sc);
                    if (n == null) continue;
                    Ok(n.Length == 1, LAYOUT_NAMES[l] + " 0x" + sc.ToString("X2") + ": one character, " + n);
                    Ok(!seen.ContainsKey(n), LAYOUT_NAMES[l] + ": " + n + " on 0x" + sc.ToString("X2") + " and 0x" + (seen.ContainsKey(n) ? seen[n] : 0).ToString("X2"));
                    seen[n] = sc;
                }
                Eq(47, seen.Count, LAYOUT_NAMES[l] + ": named keys");
            }
        }

        [Test] static void Keys_LayoutKeyName_Digits()
        {
            for (int l = 1; l < LAYOUT_COUNT; l++)
                for (int sc = 0x02; sc <= 0x0B; sc++)
                    Eq(sc == 0x0B ? "0" : ((char)('1' + sc - 2)).ToString(), LayoutKeyName(l, sc), LAYOUT_NAMES[l] + " 0x" + sc.ToString("X2"));
        }

        [Test] static void Keys_LayoutKeyName_QwertyAzerty()
        {
            int[] sc = { 0x10, 0x11, 0x19, 0x1E, 0x26, 0x27, 0x2C, 0x31, 0x32, 0x33, 0x34, 0x35, 0x0C, 0x0D, 0x1A, 0x1B, 0x28, 0x29, 0x2B };
            string[] qwerty = { "Q", "W", "P", "A", "L", ";", "Z", "N", "M", ",", ".", "/", "-", "=", "[", "]", "'", "`", "\\" };
            string[] azerty = { "A", "Z", "P", "Q", "L", "M", "W", "N", ",", ";", ":", "!", ")", "=", "^", "$", "\u00d9", "\u00b2", "*" };
            for (int i = 0; i < sc.Length; i++)
            {
                Eq(qwerty[i], LayoutKeyName(1, sc[i]), "QWERTY 0x" + sc[i].ToString("X2"));
                Eq(azerty[i], LayoutKeyName(2, sc[i]), "AZERTY 0x" + sc[i].ToString("X2"));
            }
        }

        [Test] static void Keys_LayoutKeyName_QwertzDvorakColemak()
        {
            int[] sc = { 0x10, 0x13, 0x15, 0x1E, 0x1F, 0x26, 0x2C, 0x2D, 0x32, 0x33, 0x35, 0x0C, 0x0D, 0x1A, 0x1B, 0x28, 0x29, 0x2B };
            string[] qwertz = { "Q", "R", "Z", "A", "S", "L", "Y", "X", "M", ",", "-", "\u00df", "\u00b4", "\u00dc", "+", "\u00c4", "^", "#" };
            string[] dvorak = { "'", "P", "F", "A", "O", "N", ";", "Q", "M", "W", "Z", "[", "]", "/", "=", "-", "`", "\\" };
            string[] colemak = { "Q", "P", "J", "A", "R", "I", "Z", "X", "M", ",", "/", "-", "=", "[", "]", "'", "`", "\\" };
            for (int i = 0; i < sc.Length; i++)
            {
                Eq(qwertz[i], LayoutKeyName(3, sc[i]), "QWERTZ 0x" + sc[i].ToString("X2"));
                Eq(dvorak[i], LayoutKeyName(4, sc[i]), "DVORAK 0x" + sc[i].ToString("X2"));
                Eq(colemak[i], LayoutKeyName(5, sc[i]), "COLEMAK 0x" + sc[i].ToString("X2"));
            }
        }

        [Test] static void Keys_LayoutKeyName_OtherKeysAreNull()
        {
            int[] other = { 0x00, 0x01, 0x0E, 0x0F, 0x1C, 0x1D, 0x2A, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x41, 0x45, 0x47, 0x56, 0x57, 0x58, 0x7F, 0x80, 0xC9, 0xD3 };
            for (int l = 1; l < LAYOUT_COUNT; l++)
                foreach (int sc in other) Ok(LayoutKeyName(l, sc) == null, LAYOUT_NAMES[l] + " 0x" + sc.ToString("X2") + ": no table name (Windows' then)");
        }

        [Test] static void Keys_LetterSc()
        {
            Eq(0x1E, LetterSc(1, 'A'), "QWERTY A"); Eq(0x27, LetterSc(1, ';'), "QWERTY ;"); Eq(0x32, LetterSc(1, 'M'), "QWERTY M");
            Eq(0x10, LetterSc(2, 'A'), "AZERTY A"); Eq(0x1E, LetterSc(2, 'Q'), "AZERTY Q"); Eq(0x27, LetterSc(2, 'M'), "AZERTY M"); Eq(0x2C, LetterSc(2, 'W'), "AZERTY W");
            Eq(0x2C, LetterSc(3, 'Y'), "QWERTZ Y"); Eq(0x15, LetterSc(3, 'Z'), "QWERTZ Z");
            Eq(0x13, LetterSc(4, 'P'), "DVORAK P"); Eq(0x35, LetterSc(4, 'Z'), "DVORAK Z"); Eq(0x2C, LetterSc(4, ';'), "DVORAK ;");
            Eq(0x12, LetterSc(5, 'F'), "COLEMAK F"); Eq(0x31, LetterSc(5, 'K'), "COLEMAK K");
            Eq(0, LetterSc(1, '1'), "digit: not in the letter rows"); Eq(0, LetterSc(2, ';'), "AZERTY ; is punctuation"); Eq(0, LetterSc(1, 'a'), "lower case");
            for (int l = 1; l < LAYOUT_COUNT; l++)
                for (char c = 'A'; c <= 'Z'; c++) Eq(c.ToString(), LayoutKeyName(l, LetterSc(l, c)), LAYOUT_NAMES[l] + ": LetterSc and LayoutKeyName agree on " + c);
        }

        [Test] static void Keys_DetectLayout_ProbesMatchTheTables()
        {
            // DetectLayout asks Windows where A, Z, P, F are: each probe must be where that keyboard has the letter and
            // not where QWERTY has it (the keyboard of the machine is not tested: MapVirtualKey)
            Eq(0x10, LetterSc(2, 'A'), "AZERTY probe A"); Eq(0x15, LetterSc(3, 'Z'), "QWERTZ probe Z");
            // (COLEMAK has its P at 0x13 too: DetectLayout tests F first, see Keys_DetectLayout_EachKeyboard)
            Eq(0x13, LetterSc(4, 'P'), "DVORAK probe P"); Eq(0x12, LetterSc(5, 'F'), "COLEMAK probe F");
            No(LetterSc(1, 'A') == 0x10 || LetterSc(1, 'Z') == 0x15 || LetterSc(1, 'P') == 0x13 || LetterSc(1, 'F') == 0x12, "QWERTY matches none");
            for (int l = 1; l < LAYOUT_COUNT; l++) if (l != 2) Ok(LetterSc(l, 'A') != 0x10, LAYOUT_NAMES[l] + " not taken for AZERTY");
            for (int l = 1; l < LAYOUT_COUNT; l++) if (l != 3) Ok(LetterSc(l, 'Z') != 0x15, LAYOUT_NAMES[l] + " not taken for QWERTZ");
            int d = DetectLayout();
            Ok(d >= 1 && d < LAYOUT_COUNT, "DetectLayout gives a preset (not GAME, not CUSTOM): " + d);
        }

        // ================================================================== keyboard layouts: presets
        [Test] static void Keys_LayoutChords_EveryPreset()
        {
            string moves = "back=1F/0 turnl=2C/0 turnr=2E/0 stop=2D/0 sidel=1E/0 sider=20/0 ";
            string[] want = {
                "tprev=25/0",
                moves + "tprev=25/0",
                moves + "tprev=25/0 o_attack=10/3 o_follow=27/3 maphud=27/0",
                moves + "tprev=25/0",
                moves + "v360=31/0 tnext=25/0 tprev=14/0 o_cover=17/3 o_caution=23/3 o_form=15/3 o_hold=24/3 o_nav=26/3 o_pickup=13/3 o_retreat=18/3 o_status=27/3 o_target=25/3 recentre=26/0 exit=30/3",
                moves + "ir=26/0 tprev=25/0 o_caution=22/3 o_form=12/3 o_nav=24/3 o_pickup=13/3 o_retreat=1F/3 o_status=20/3 o_target=21/3" };
            for (int l = 0; l < LAYOUT_COUNT; l++) Eq(want[l], KeysDiff(LayoutChords(l)), LAYOUT_NAMES[l] + " (default tool keys)");
        }

        [Test] static void Keys_LayoutChords_QwertzGivesQwertysKeys()
        {
            // the only letter shortcut with a Y or a Z is previous target (Y), and QWERTZ's Y is on turn left
            Eq(KeysDiff(LayoutChords(1)), KeysDiff(LayoutChords(3)), "QWERTZ = QWERTY");
            for (int a = 0; a < LAYOUT_COUNT; a++)
                for (int b = a + 1; b < LAYOUT_COUNT; b++)
                    if (!(a == 1 && b == 3)) Ok(KeysDiff(LayoutChords(a)) != KeysDiff(LayoutChords(b)), LAYOUT_NAMES[a] + " and " + LAYOUT_NAMES[b] + " differ");
        }

        [Test] static void Keys_LayoutChords_FollowTheToolKeys()
        {
            SetKey(0, 0x32);                         // freelook on M: map to HUD goes, previous target stays
            Eq("maphud=25/0", KeysDiff(LayoutChords(0)), "GAME, freelook on M");
            SetKey(0, 0x27);                         // freelook where AZERTY prints M
            Eq("back=1F/0 turnl=2C/0 turnr=2E/0 stop=2D/0 sidel=1E/0 sider=20/0 o_attack=10/3 o_follow=27/3", KeysDiff(LayoutChords(2)),
                "AZERTY: map to HUD keeps its place, ALT+M follows the letter");
            SetKey(1, 0x25);                         // noclip on K
            SetKey(0, 0x15);
            Eq("tprev=26/0", KeysDiff(LayoutChords(0)), "K taken by the tool: L");
        }

        [Test] static void Keys_LayoutChords_RulesForAnyToolKeys()
        {
            int[][] tools = { new[] { 0x15, 0x16, 0x24, 0x41 }, new[] { 0x22, 0x32, 0x25, 0x3F }, new[] { 0x11, 0x1F, 0x1E, 0x20 },
                new[] { 0x26, 0x27, 0x08, 0x09 }, new[] { 0x2C, 0x2D, 0x2E, 0x14 } };
            string[] moveIds = { "fwd", "back", "stop", "sidel", "sider", "turnl", "turnr" };
            int[] moveSc = { 0x11, 0x1F, 0x2D, 0x1E, 0x20, 0x2C, 0x2E };
            foreach (int[] t in tools)
            {
                for (int i = 0; i < 4; i++) SetKey(i, t[i]);
                for (int l = 0; l < LAYOUT_COUNT; l++)
                {
                    string when = LAYOUT_NAMES[l] + ", tool keys " + t[0].ToString("X2") + " " + t[1].ToString("X2") + " " + t[2].ToString("X2") + " " + t[3].ToString("X2");
                    ApplyLayout(l);
                    KeysCheck(when);
                    KeysCheckMap(when);
                    for (int m = 0; m < moveIds.Length; m++)
                    {
                        GameKey g = KeysG(moveIds[m]);
                        int sc = l == 0 ? g.DefSc : moveSc[m];
                        if (ToolKeyOwner(sc, -1) >= 0) Ok(Array.IndexOf(SPARE_KEYS, g.Sc) >= 0 && g.Mod == 0, when + ": " + g.Id + " off the tool key, on a spare");
                        else Eq(KeysChord(sc, 0), KeysChord(g.Sc, g.Mod), when + ": " + g.Id + (l == 0 ? " on the game's key" : " on W A S D / X Z C"));
                    }
                    string before = KeysSnap();
                    ResolveToolClashes();
                    Eq(before, KeysSnap(), when + ": nothing left for ResolveToolClashes");
                    Eq(l, CurrentLayout(), when + ": recognized");
                }
            }
        }

        [Test] static void Keys_ApplyLayout()
        {
            KeysStart();
            AssignMouse(KeysG("fwd"), -1, 1);
            AssignToolKey(3, 0x3F, false);
            for (int l = 0; l < LAYOUT_COUNT; l++)
            {
                ApplyLayout(l);
                Eq(l, ChosenLayout, "chosen " + LAYOUT_NAMES[l]);
                Eq(l >= 1 ? l : -1, DisplayLayout(), "names shown for " + LAYOUT_NAMES[l]);
                Eq(KeysDiff(LayoutChords(l)), KeysDiff(KeysNow()), "keys of " + LAYOUT_NAMES[l]);
                Eq(1, KeysG("fwd").Mouse, "mouse bind kept");
                Eq(0x3F, ScanStereo, "tool keys kept");
            }
        }

        [Test] static void Keys_CurrentLayout_GameOnceResolved()
        {
            Eq(LAYOUT_COUNT, CurrentLayout(), "before ResolveToolClashes (previous target on freelook's Y): CUSTOM");
            ResolveToolClashes();
            Eq(0, CurrentLayout(), "then GAME");
            ChosenLayout = 2;
            Eq(0, CurrentLayout(), "GAME's keys with AZERTY picked last: GAME");
        }

        [Test] static void Keys_CurrentLayout_EachPreset()
        {
            for (int l = 0; l < LAYOUT_COUNT; l++)
            {
                KeysStart();
                ApplyLayout(l);
                Eq(l, CurrentLayout(), LAYOUT_NAMES[l] + " picked");
                ChosenLayout = -1;                   // the same keys from an ini without keys_layout
                int c = CurrentLayout();
                if (l == 1 || l == 3) Ok(c == 1 || c == 3, LAYOUT_NAMES[l] + " keys, nothing picked: QWERTY or QWERTZ (the Windows keyboard decides), got " + c);
                else Eq(l, c, LAYOUT_NAMES[l] + " keys, nothing picked");
            }
            ApplyLayout(3);
            Eq(3, CurrentLayout(), "QWERTZ picked: QWERTZ, though QWERTY has the same keys");
            ApplyLayout(1);
            Eq(1, CurrentLayout(), "QWERTY picked: QWERTY");
        }

        [Test] static void Keys_CurrentLayout_CustomAfterAChange()
        {
            KeysStart();
            ApplyLayout(2);
            AssignGameKey(KeysG("jump"), 0x3F, 0, false);
            Eq(LAYOUT_COUNT, CurrentLayout(), "a command changed: CUSTOM");
            Eq(2, ChosenLayout, "the preset picked stays AZERTY (names, ini)");
            AssignGameKey(KeysG("jump"), 0x39, 0, false);
            Eq(2, CurrentLayout(), "put back: AZERTY again");
            AssignToolKey(0, 0x3F, false);
            Eq(LAYOUT_COUNT, CurrentLayout(), "freelook off Y: AZERTY would leave previous target on Y, it is on K: CUSTOM");
        }

        [Test] static void Keys_Layout_LettersFollowThePrintedKey()
        {
            // the letter shortcuts that can't show their letter (the key is the tool's or a command's): the rest do
            string[] blocked = { "tprev", "tprev", "tprev", "full ir pause", "full recentre pause tnext tprev" };
            for (int l = 1; l < LAYOUT_COUNT; l++)
            {
                KeysStart();
                ApplyLayout(l);
                List<string> off = new List<string>();
                for (int i = 0; i < LETTER_KEYS.GetLength(0); i++)
                {
                    GameKey g = KeysG(LETTER_KEYS[i, 0]);
                    if (ScanName(g.Sc) != LETTER_KEYS[i, 1]) { off.Add(g.Id); Eq(0, g.Mod, LAYOUT_NAMES[l] + ": " + g.Id + " (orders on Alt always find their letter)"); }
                }
                Eq(blocked[l - 1], string.Join(" ", off.ToArray()), LAYOUT_NAMES[l] + ": shortcuts not on their letter");
            }
        }

        // ================================================================== the key map given to the game, its hook
        [Test] static void Keys_BuildKeyMap_NothingChanged()
        {
            EqBytes(new byte[1024], BuildKeyMap(), "start values: empty map");
            SetKey(0, 0x3F);                         // freelook off Y: nothing to move
            ResolveToolClashes();
            EqBytes(new byte[1024], BuildKeyMap(), "freelook on F5: still empty");
            No(KeysChanged(), "and no key changed");
        }

        [Test] static void Keys_BuildKeyMap_PreviousTargetOnK()
        {
            KeysStart();
            byte[] want = new byte[1024];
            want[0x25 * 8] = 0x15; want[0x25 * 8 + 1] = 0x80;   // K -> the game's Y (little-endian 0x8015)
            want[0x15 * 8] = 0xFF; want[0x15 * 8 + 1] = 0x80;   // Y alone -> taken away (0x80FF)
            EqBytes(want, BuildKeyMap(), "first-run map");
        }

        [Test] static void Keys_BuildKeyMap_Swap()
        {
            KeysStart();
            AssignGameKey(KeysG("fwd"), 0x1F, 0, false);
            int[] map = KeysMap();
            Eq(0x8011, map[0x1F * 4], "S -> W (forward)");
            Eq(0x801F, map[0x11 * 4], "W -> S (stop)");
            int n = 0;
            foreach (int w in map) if (w != 0) n++;
            Eq(4, n, "plus previous target's two words, nothing taken away");
            KeysCheckMap("swap");
        }

        [Test] static void Keys_BuildKeyMap_ModifiersAndDrone()
        {
            KeysStart();
            AssignGameKey(KeysG("o_attack"), 0x1E, 1, false);
            AssignGameKey(KeysG("d_up"), 0x11, 2, false);
            int[] map = KeysMap();
            Eq(0x831E, map[0x1E * 4 + 1], "SHIFT+A -> A with Alt (0x8000 | 0x1E | 3 << 8)");
            Eq(0x80FF, map[0x1E * 4 + 3], "ALT+A taken away");
            Eq(0, map[0x1E * 4], "A (turn left) untouched");
            Eq(0x82C9, map[0x11 * 4 + 2], "CTRL+W -> PgUp with Ctrl");
            Eq(0, map[0x11 * 4], "W untouched");
            KeysCheckMap("modifiers and drone");
        }

        [Test] static void Keys_BuildKeyMap_EveryPreset()
        {
            // a word per moved command, plus 0x80FF on each of the game's chords left free (QWERTY: the six moves
            // take each other's keys, previous target leaves Y)
            int[] words = { 2, 8, 14, 8, 30, 20 }, away = { 1, 1, 4, 1, 10, 5 };
            for (int l = 0; l < LAYOUT_COUNT; l++)
            {
                KeysStart();
                ApplyLayout(l);
                KeysCheckMap(LAYOUT_NAMES[l]);
                int n = 0, ff = 0;
                foreach (int w in KeysMap()) { if (w != 0) n++; if (w == 0x80FF) ff++; }
                Eq(words[l], n, LAYOUT_NAMES[l] + ": words in the map");
                Eq(away[l], ff, LAYOUT_NAMES[l] + ": keys taken away");
            }
        }

        [Test] static void Keys_Rel32Call()
        {
            EqBytes(Bytes("E8FB0F0000"), Rel32Call(0x1000, 0x2000, 5), "forward call, no padding");
            EqBytes(Bytes("E8FBEFFFFF909090"), Rel32Call(0x2000, 0x1000, 8), "backward call, 3 NOPs");
            EqBytes(Bytes("E848871500909090909090"), Rel32Call(0x312345, KB_MAP_FN, 11), "the 11 bytes at the keyboard handler's site");
            foreach (uint at in new uint[] { 0, 0x29E828, 0x33FFF0, 0x46AA20 })
                foreach (uint target in new uint[] { 0x1000, 0x46AA92, 0x300000 })
                {
                    byte[] b = Rel32Call(at, target, 9);
                    Eq(9, b.Length, "length");
                    Eq(target, (uint)(at + 5 + BitConverter.ToInt32(b, 1)), "target of the call from 0x" + at.ToString("X"));
                }
        }

        [Test] static void Keys_Rel32Call_KeyboardHook()
        {
            // the camera set-up hook of TryKeys: its call replaces exactly the two movs of KB_HOOK_OLD
            Eq(8, Bytes(KB_HOOK_OLD).Length, "two movs");
            EqBytes(Bytes("E8F3C11C00909090"), Rel32Call(KB_HOOK[0], KB_INSTALL, 8), "hook 0x29E828");
            EqBytes(Bytes("E8E3C11C00909090"), Rel32Call(KB_HOOK[1], KB_INSTALL, 8), "hook 0x29E838");
        }

        [Test] static void Keys_KeysChanged()
        {
            No(KeysChanged(), "start values");
            ResolveToolClashes();
            Ok(KeysChanged(), "previous target moved to K: the key translation is needed with the default keys");
            KeysFresh();
            SetKey(0, 0x3F); ResolveToolClashes();
            No(KeysChanged(), "freelook off Y: nothing moved");
            AssignGameKey(KeysG("o_attack"), 0x1E, 1, false);
            Ok(KeysChanged(), "a chord changed");
            AssignGameKey(KeysG("o_attack"), 0x1E, 3, false);
            No(KeysChanged(), "put back");
            AssignMouse(KeysG("fwd"), -1, 1);
            No(KeysChanged(), "a mouse bind alone changes no key");
            KeysG("options").Sc = 0x3E;
            No(KeysChanged(), "a fixed command is never counted");
        }

        // ================================================================== random edits: the rules always hold
        [Test] static void Keys_RandomEdits_KeepTheRules()
        {
            KeysStart();
            uint seed = 20261024;
            Func<int, int> rnd = n => { seed = seed * 1664525u + 1013904223u; return (int)((seed >> 8) % (uint)n); };
            List<GameKey> free = GameKeys.FindAll(g => !g.Fixed);
            int restarts = 0, accepted = 0;
            for (int step = 0; step < 600; step++)
            {
                int op = rnd(100);
                string what, msg = "", start = KeysSnap();
                if (op < 45)
                {
                    GameKey g = free[rnd(free.Count)];
                    int sc = rnd(10) == 0 ? 0x80 + rnd(0x80) : rnd(0x80), mod = rnd(4);
                    bool ext = rnd(15) == 0;
                    what = "AssignGameKey(" + g.Id + ", 0x" + sc.ToString("X2") + ", " + mod + ", " + ext + ")";
                    msg = AssignGameKey(g, sc, mod, ext);
                }
                else if (op < 70)
                {
                    int k = rnd(4), sc = rnd(0x80);
                    bool ext = rnd(15) == 0;
                    what = "AssignToolKey(" + k + ", 0x" + sc.ToString("X2") + ", " + ext + ")";
                    msg = AssignToolKey(k, sc, ext);
                }
                else if (op < 85)
                {
                    int m = rnd(MOUSE_INI.Length);
                    if (rnd(3) == 0) { int t = rnd(4); what = "AssignMouse(tool " + t + ", " + m + ")"; msg = AssignMouse(null, t, m); }
                    else { GameKey g = free[rnd(free.Count)]; what = "AssignMouse(" + g.Id + ", " + m + ")"; msg = AssignMouse(g, -1, m); }
                }
                else if (op < 98) { int l = rnd(LAYOUT_COUNT); what = "ApplyLayout(" + l + ")"; ApplyLayout(l); Eq(l, CurrentLayout(), what + ": recognized"); }
                else { what = "ResetKeys()"; KeysStart(); }
                string when = "step " + step + " " + what + " -> \"" + msg + "\"";
                Ok(msg != null, when + ": a message");
                if (KeysSnap() != start) accepted++;
                KeysCheck(when);
                KeysCheckMap(when);
                int cur = CurrentLayout();
                if (cur < LAYOUT_COUNT) Eq(KeysDiff(LayoutChords(cur)), KeysDiff(KeysNow()), when + ": keys of " + LAYOUT_NAMES[cur]);
                // through TNPlus.ini and back; not in the two states of the bugs reported with this file (a command
                // swapped onto a drone key, view distance on I)
                bool known = ScanDistance == 0x17 || GameKeys.Exists(g => g.Changed && g.Sc >= 0x80);
                if (step % 20 == 19 && !known)
                {
                    string before = KeysSnap();
                    KeysRestart();
                    Eq(before, KeysSnap(), when + ": read back from TNPlus.ini");
                    restarts++;
                }
            }
            Ok(restarts >= 15, "restarts checked: " + restarts);
            Ok(accepted >= 100, "edits accepted: " + accepted);
        }

        // ================================================================== fixed bugs
        [Test] static void Keys_DetectLayout_EachKeyboard()
        {
            // the letters where each keyboard has them (TN+'s own tables standing for Windows' MapVirtualKey)
            for (int l = 1; l < LAYOUT_COUNT; l++)
            {
                int layout = l;
                Eq(l, DetectLayout(c => (uint)LetterSc(layout, c)), LAYOUT_NAMES[l] + " recognized (Colemak has its P where Dvorak has it)");
            }
        }

        [Test] static void Keys_LayoutKeyName_QwertzRightOfL()
        {
            Eq("Ö", LayoutKeyName(3, 0x27), "QWERTZ: the key right of L");
            for (int l = 1; l < LAYOUT_COUNT; l++)
            {
                int n = 0;
                for (int sc = 0; sc < 0x100; sc++) if (LayoutKeyName(l, sc) != null) n++;
                Eq(47, n, LAYOUT_NAMES[l] + ": named keys");
            }
        }

        [Test] static void Keys_Layout_ToolKeyOnOLeavesTheFixedOptions()
        {
            // a tool key written as O by hand in TNPlus.ini: no preset moves the game's fixed Options command off O
            KeysLoad("key_freelook = 18", "keys_version = 3");
            Eq(0x18, ScanFreelook, "freelook on O (hand-written)");
            for (int l = 0; l < LAYOUT_COUNT; l++)
            {
                ApplyLayout(l);
                Eq(0x18, KeysG("options").Sc, LAYOUT_NAMES[l] + ": Options stays on O");
                Eq(0, KeysMap()[0x18 * 4], LAYOUT_NAMES[l] + ": O not taken away from the game in missions");
                Eq(l, CurrentLayout(), LAYOUT_NAMES[l] + ": the preset still recognized");
            }
        }
    }
}
