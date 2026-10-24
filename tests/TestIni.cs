// TNPlus.ini: LoadSettings / SaveSettings (TNPlus.cs) and what they call (ParseGameKey, ParseMouse, GameKeysIni, MouseIni,
// ResolveToolClashes, ApplyDisplay): defaults, round trips, parsing details, migrations of old inis, cultures.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ---------------------------------------------------------------- helpers (prefix Ini)
        // what a new ini holds (no TNPlus.ini at start-up): every "key = value" line SaveSettings writes, in its order.
        // The game's previous target (Y) is on K: freelook has Y (ResolveToolClashes, first spare key K).
        static readonly string[] IniDefaults = {
            "display = HD", "launch = GAME", "freelook = 1", "noclip = 0", "view_distance = FAR", "terrain_detail = SHARPER",
            "terrain_fps = 60", "widescreen = 3", "wide_hud = 1", "fps_counter = 1", "speed_fixes = 1", "small_views = 1",
            "update_check = 1", "stereo_fix = 0", "scaling = SHARP", "music = ROLAND", "hit_fix = 1", "phys_fix = 1",
            "thick_smoke = 0", "field_of_view = GAME", "object_distance = MAX", "edge_objects = 1",
            "sensitivity_x = 12", "sensitivity_y = 8", "invert_y = 0", "noclip_speed = 15", "cpu_cycles = 1800000",
            "speed_version = 6", "key_freelook = 15", "key_noclip = 16", "key_distance = 24", "key_stereo = 41",
            "keys_version = 3", "key_game_tprev = 25", "sound = 1", "game_dir = " };

        // every setting away from its default, in the order SaveSettings writes them (so a load + save gives them back).
        // The tool keys (F5, F6, L, 9) are on no game key: ResolveToolClashes moves nothing.
        static string[] IniFull(string gameDir)
        {
            return new[] {
                "display = 320X400", "launch = DEMO 2", "freelook = 0", "noclip = 1", "view_distance = NORMAL",
                "terrain_detail = SHARP", "terrain_fps = 45", "widescreen = 1", "wide_hud = 0", "fps_counter = 0",
                "speed_fixes = 0", "small_views = 0", "update_check = 0", "stereo_fix = 1", "scaling = GAME'S OWN",
                "music = AWE32", "hit_fix = 0", "phys_fix = 0", "thick_smoke = 1", "field_of_view = 110",
                "object_distance = GAME", "edge_objects = 0", "wide_attach = 1", "inplace_attach = 1", "keep_dos = 1",
                "smooth_cells = 40", "sensitivity_x = 20", "sensitivity_y = 3", "invert_y = 1", "noclip_speed = 2.5",
                "cpu_cycles = 2200000", "speed_version = 6", "key_freelook = 3F", "key_noclip = 40", "key_distance = 26",
                "key_stereo = 0A", "keys_version = 3", "keys_layout = DVORAK",
                "key_game_fwd = 48", "key_game_jump = SHIFT+39", "key_game_tprev = ALT+15", "key_game_o_attack = CTRL+1E",
                "key_game_d_up = CTRL+49",
                "mouse_jump = M4", "mouse_lookup = WHEELUP", "mouse_freelook = MID", "mouse_stereo = DEL",
                "sound = 0", "game_dir = " + gameDir };
        }

        // TNPlus.ini made of these lines (CRLF, UTF-8 without BOM, as SaveSettings writes it)
        static void IniPut(params string[] lines) { PutFile(IniPath, string.Join("\r\n", lines) + "\r\n"); }

        // the "key = value" lines of the ini as they are written (comments and blank lines left out), in their order
        static List<string> IniSaved()
        {
            List<string> l = new List<string>();
            foreach (string s in File.ReadAllLines(IniPath))
                if (s.Trim() != "" && !s.StartsWith(";", StringComparison.Ordinal)) l.Add(s);
            return l;
        }

        // the lines of the ini starting with this text
        static List<string> IniSavedStarting(string start)
        {
            return IniSaved().FindAll(s => s.StartsWith(start, StringComparison.Ordinal));
        }

        // the value written for a key, null when there is no such line (fails when there are two)
        static string IniSavedValue(string key)
        {
            string found = null;
            foreach (string s in IniSaved())
                if (s.StartsWith(key + " = ", StringComparison.Ordinal))
                {
                    if (found != null) Fail("two lines for " + key + " in the ini");
                    found = s.Substring(key.Length + 3);
                }
            return found;
        }

        // LoadSettings; returns what it printed (also passed on to the harness, shown when the test fails)
        static string IniLoad()
        {
            TextWriter old = Console.Out;
            StringWriter sw = new StringWriter();
            Console.SetOut(sw);
            try { LoadSettings(); }
            finally { Console.SetOut(old); old.Write(sw.ToString()); }
            return sw.ToString();
        }

        static int IniCount(string text, string part)
        {
            int n = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        // runs a under this culture (the thread's), then puts the previous one back
        static void IniInCulture(string name, Action a)
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
            try { a(); }
            finally { Thread.CurrentThread.CurrentCulture = before; }
        }

        static GameKey IniGame(string id)
        {
            GameKey g = FindGameKey(id);
            if (g == null) Fail("no game command " + id);
            return g;
        }

        static void IniKeyIs(string id, int sc, int mod, string what)
        {
            GameKey g = IniGame(id);
            Eq(sc.ToString("X2") + "/" + mod, g.Sc.ToString("X2") + "/" + g.Mod, what + " (" + id + ": scancode/modifier)");
        }

        // the ids of the game commands changed from their defaults, in their order
        static List<string> IniChangedKeys()
        {
            List<string> l = new List<string>();
            foreach (GameKey g in GameKeys) if (g.Changed) l.Add(g.Id);
            return l;
        }

        // the game commands back on their defaults, no mouse binds (the state at start-up)
        static void IniDefaultKeys()
        {
            foreach (GameKey g in GameKeys) { g.Sc = g.DefSc; g.Mod = g.DefMod; g.Mouse = 0; }
            for (int i = 0; i < ToolMouse.Length; i++) ToolMouse[i] = 0;
        }

        // ---------------------------------------------------------------- no ini: the defaults
        [Test] static void Ini_NoIni_WritesTheDefaults()
        {
            No(File.Exists(IniPath), "no TNPlus.ini in the test's folder to start with");
            string said = IniLoad();
            Ok(File.Exists(IniPath), "LoadSettings writes TNPlus.ini when there is none");
            EqList(IniDefaults, IniSaved(), "the settings of a new ini");
            Eq("", said.Trim(), "nothing printed");
        }

        [Test] static void Ini_NoIni_StateIsTheDefaults()
        {
            IniLoad();
            Eq(0x15, ScanFreelook, "freelook on Y");
            IniKeyIs("tprev", 0x25, 0, "the game's previous target moved from Y to K");
            EqList(new[] { "tprev" }, IniChangedKeys(), "the only game command changed");
            Eq(2, Display, "display HD");
            Eq(3, WideMode(), "true 16:9 in 1280x720");
            Eq(1800000, CpuCycles, "cpu cycles");
            Eq(-1, ChosenLayout, "no layout preset");
            No(OptHD, "OptHD: the defaults, ApplyDisplay is not run without an ini");
            Ok(OptForce400, "OptForce400: the default");
        }

        [Test] static void Ini_Defaults_ReadBackGiveTheSameFile()
        {
            IniLoad();
            byte[] first = File.ReadAllBytes(IniPath);
            IniLoad();
            EqBytes(first, File.ReadAllBytes(IniPath), "LoadSettings of an existing ini leaves the file as it is");
            SaveSettings();
            EqBytes(first, File.ReadAllBytes(IniPath), "the new ini read back and saved again: the same bytes");
        }

        [Test] static void Ini_NotWritable_NoCrash()
        {
            Directory.CreateDirectory(IniPath);     // a folder where the ini goes: File.Exists false, WriteAllText fails
            IniLoad();
            SaveSettings();
            Ok(Directory.Exists(IniPath), "the folder is still there");
            IniKeyIs("tprev", 0x25, 0, "the defaults still applied (previous target on K)");
        }

        // ---------------------------------------------------------------- the text SaveSettings writes
        [Test] static void Ini_Save_FileFormat()
        {
            SaveSettings();
            byte[] b = File.ReadAllBytes(IniPath);
            Eq((byte)';', b[0], "first byte: the comment, no BOM");
            foreach (byte x in b) if (x >= 0x80) Fail("non-ASCII byte 0x" + x.ToString("X2") + " in the default ini");
            string t = Encoding.UTF8.GetString(b);
            Ok(t.StartsWith("; Terra Nova Plus settings (the menu at start-up edits the first part)\r\n", StringComparison.Ordinal), "first line");
            Ok(t.EndsWith("\r\ngame_dir = \r\n", StringComparison.Ordinal), "last line: game_dir, empty, CRLF");
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '\n' && (i == 0 || t[i - 1] != '\r')) Fail("a line ends with LF alone at " + i);
                if (t[i] == '\r' && (i + 1 >= t.Length || t[i + 1] != '\n')) Fail("a line ends with CR alone at " + i);
            }
            foreach (string l in IniSaved())
            {
                int eq = l.IndexOf(" = ", StringComparison.Ordinal);
                Ok(eq > 0, "\"key = value\": " + l);
                foreach (char c in l.Substring(0, eq))
                    Ok((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_', "key in lower case letters, digits and _: " + l);
            }
        }

        [Test] static void Ini_Save_ExactLinesOfSomeSettings()
        {
            string t = null;
            SaveSettings();
            t = File.ReadAllText(IniPath);
            Has(t, "\r\ncpu_cycles = 1800000\r\nspeed_version = 6\r\n", "cpu cycles then the speed version");
            Has(t, "\r\nkey_freelook = 15\r\nkey_noclip = 16\r\nkey_distance = 24\r\nkey_stereo = 41\r\nkeys_version = 3\r\n", "the tool keys (hex, 2 digits) then the keys version");
            Has(t, "\r\nsensitivity_x = 12\r\nsensitivity_y = 8\r\ninvert_y = 0\r\n", "mouse sensitivity");
            Has(t, "\r\nnoclip_speed = 15\r\n", "noclip speed 15.0 written 15");
            Has(t, "\r\nscaling = SHARP\r\n", "scaling");
            Has(t, "; the game's commands changed in the KEYBOARD tab (missions only): [SHIFT+|CTRL+|ALT+]scancode\r\n" +
                "; mouse binds (middle button, side buttons, wheel), a second bind: MID, M4, M5, WHEELUP, WHEELDOWN\r\n" +
                "; 0 = no beeps\r\nsound = 1\r\n", "no game key changed, no mouse bind: the two comments one after the other");
            ScanStereo = 0x0A; ScanDistance = 0xC9;
            SaveSettings();
            t = File.ReadAllText(IniPath);
            Has(t, "\r\nkey_distance = C9\r\nkey_stereo = 0A\r\n", "upper case hex with a leading 0");
        }

        [Test] static void Ini_Save_VersionsAlwaysTheCurrentOnes()
        {
            IniPut("cpu_cycles = 2200000", "speed_version = 2", "keys_version = 1", "speed_version = 9");
            IniLoad();
            SaveSettings();
            Eq("6", IniSavedValue("speed_version"), "speed_version written (one line)");
            Eq("3", IniSavedValue("keys_version"), "keys_version written (one line)");
            Eq("2200000", IniSavedValue("cpu_cycles"), "cpu cycles kept");
        }

        [Test] static void Ini_Save_OptionalLinesOnlyWhenSet()
        {
            SaveSettings();
            foreach (string k in new[] { "wide_attach", "inplace_attach", "keep_dos", "smooth_cells", "keys_layout", "hd_720", "hd", "force_320x400" })
                Eq(null, IniSavedValue(k), k + " line in a default ini");
            Eq(0, IniSavedStarting("key_game_").Count, "key_game_ lines with the game's own keys (nothing loaded yet: Y not moved)");
            Eq(0, IniSavedStarting("mouse_").Count, "mouse_ lines without a bind");

            OptWideAttach = true; OptInPlaceAttach = true; OptKeepDos = true; OptSmoothCells = 36; ChosenLayout = 2;
            SaveSettings();
            List<string> l = IniSaved();
            int e = l.IndexOf("edge_objects = 1");
            Ok(e > 0 && e + 4 < l.Count, "edge_objects line");
            EqList(new[] { "wide_attach = 1", "inplace_attach = 1", "keep_dos = 1", "smooth_cells = 36" }, l.GetRange(e + 1, 4), "the test lines, after edge_objects");
            Has(File.ReadAllText(IniPath), "\r\n; tests: forced end of the smooth terrain (cells)\r\nsmooth_cells = 36\r\n", "smooth_cells and its comment");
            int kv = l.IndexOf("keys_version = 3");
            Eq("keys_layout = AZERTY", kv >= 0 && kv + 1 < l.Count ? l[kv + 1] : null, "keys_layout, after keys_version");

            OptSmoothCells = -5; ChosenLayout = -1; OptKeepDos = false;
            SaveSettings();
            Eq(null, IniSavedValue("smooth_cells"), "smooth_cells line with a value <= 0");
            Eq(null, IniSavedValue("keys_layout"), "keys_layout line without a preset (-1)");
            Eq(null, IniSavedValue("keep_dos"), "keep_dos line when off");
        }

        // ---------------------------------------------------------------- round trips
        [Test] static void Ini_RoundTrip_EverySettingRead()
        {
            string dir = Path.Combine(ExeDir, "Terra Nova = GOG");
            IniPut(IniFull(dir));
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed (no bad setting)");
            Eq(1, Display, "display"); Eq(2, LaunchTarget, "launch");
            No(OptFreelook, "freelook"); Ok(OptNoclip, "noclip");
            Eq(0, OptDistance, "view_distance"); Eq(1, OptDetail, "terrain_detail"); Eq(45, OptTerrainFps, "terrain_fps");
            Eq(1, WideMode(), "widescreen"); Ok(OptWide, "OptWide"); No(OptWideTrue, "OptWideTrue"); No(OptHd720, "OptHd720");
            No(OptWideHud, "wide_hud"); No(OptFpsCounter, "fps_counter"); No(OptSpeedFix, "speed_fixes");
            No(OptSmallViews, "small_views"); No(OptUpdateCheck, "update_check"); Ok(OptStereoFix, "stereo_fix");
            Eq(0, OptScaling, "scaling"); Eq(3, OptMusic, "music");
            No(OptHitFix, "hit_fix"); No(OptPhysFix, "phys_fix"); Ok(OptThickSmoke, "thick_smoke");
            Eq(3, OptFov, "field_of_view"); Eq(0, OptObjDist, "object_distance"); No(OptEdge, "edge_objects");
            Ok(OptWideAttach, "wide_attach"); Ok(OptInPlaceAttach, "inplace_attach"); Ok(OptKeepDos, "keep_dos");
            Eq(40, OptSmoothCells, "smooth_cells");
            Eq(20, SensX, "sensitivity_x"); Eq(3, SensY, "sensitivity_y"); Ok(InvertY, "invert_y");
            Eq(2.5, NoclipSpeed, "noclip_speed"); Eq(2200000, CpuCycles, "cpu_cycles");
            Eq(0x3F, ScanFreelook, "key_freelook"); Eq(0x40, ScanNoclip, "key_noclip");
            Eq(0x26, ScanDistance, "key_distance"); Eq(0x0A, ScanStereo, "key_stereo");
            Eq(4, ChosenLayout, "keys_layout");
            IniKeyIs("fwd", 0x48, 0, "key_game_fwd"); IniKeyIs("jump", 0x39, 1, "key_game_jump");
            IniKeyIs("tprev", 0x15, 3, "key_game_tprev"); IniKeyIs("o_attack", 0x1E, 2, "key_game_o_attack");
            IniKeyIs("d_up", 0x49, 2, "key_game_d_up");
            EqList(new[] { "fwd", "jump", "tprev", "o_attack", "d_up" }, IniChangedKeys(), "the game commands changed");
            Eq(2, IniGame("jump").Mouse, "mouse_jump M4"); Eq(4, IniGame("lookup").Mouse, "mouse_lookup WHEELUP");
            EqList(new[] { 1, 0, 0, 11 }, ToolMouse, "mouse binds of the tool (freelook MID, stereo DEL)");
            No(Sound, "sound"); Eq(dir, GameDir, "game_dir");
            No(OptHD, "ApplyDisplay: 320x400, no HD"); Ok(OptForce400, "ApplyDisplay: 320x400");
        }

        [Test] static void Ini_RoundTrip_SaveGivesTheSameLines()
        {
            string dir = Path.Combine(ExeDir, "Terra Nova = GOG");
            IniPut(IniFull(dir));
            IniLoad();
            SaveSettings();
            EqList(IniFull(dir), IniSaved(), "the settings read, then saved");
        }

        [Test] static void Ini_RoundTrip_StateSavedThenLoaded()
        {
            string dir = Path.Combine(ExeDir, "Spiel \u00e9t\u00e9 \u4e2d");
            Display = 0; LaunchTarget = 1; OptFreelook = false; SetWideMode(2); OptMusic = 1; OptFov = 1; OptObjDist = 1;
            ChosenLayout = 0; NoclipSpeed = 0.75; CpuCycles = 0; SensX = 1; SensY = 30; ScanFreelook = 0x2B;
            OptKeepDos = true; OptSmoothCells = 12; GameDir = dir;
            IniGame("fwd").Mod = 1; IniGame("lookl").Sc = 0x4B; IniGame("tnext").Mouse = 4; ToolMouse[1] = 3;
            SaveSettings();
            Eq("GAME", IniSavedValue("keys_layout"), "keys_layout of preset 0");
            Eq("0.75", IniSavedValue("noclip_speed"), "noclip_speed");
            Eq("2", IniSavedValue("widescreen"), "widescreen");
            EqList(new[] { "key_game_fwd = SHIFT+11", "key_game_lookl = 4B" }, IniSavedStarting("key_game_"), "game keys");
            EqList(new[] { "mouse_tnext = WHEELUP", "mouse_noclip = M5" }, IniSavedStarting("mouse_"), "mouse binds");

            // back to the defaults, then read
            Display = 2; LaunchTarget = 0; OptFreelook = true; SetWideMode(3); OptMusic = 0; OptFov = 0; OptObjDist = 2;
            ChosenLayout = -1; NoclipSpeed = 15; CpuCycles = 1800000; SensX = 12; SensY = 8; ScanFreelook = 0x15;
            OptKeepDos = false; OptSmoothCells = 0; GameDir = "";
            IniDefaultKeys();
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            Eq(0, Display, "display"); Eq(1, LaunchTarget, "launch"); No(OptFreelook, "freelook"); Eq(2, WideMode(), "widescreen");
            Eq(1, OptMusic, "music"); Eq(1, OptFov, "field_of_view"); Eq(1, OptObjDist, "object_distance");
            Eq(0, ChosenLayout, "keys_layout"); Eq(0.75, NoclipSpeed, "noclip_speed"); Eq(0, CpuCycles, "cpu_cycles 0 kept");
            Eq(1, SensX, "sensitivity_x"); Eq(30, SensY, "sensitivity_y"); Eq(0x2B, ScanFreelook, "key_freelook");
            Ok(OptKeepDos, "keep_dos"); Eq(12, OptSmoothCells, "smooth_cells"); Eq(dir, GameDir, "game_dir (UTF-8)");
            IniKeyIs("fwd", 0x11, 1, "fwd"); IniKeyIs("lookl", 0x4B, 0, "lookl");
            IniKeyIs("tprev", 0x15, 0, "previous target stays on Y (freelook is not there)");
            Eq(4, IniGame("tnext").Mouse, "mouse_tnext"); EqList(new[] { 0, 3, 0, 0 }, ToolMouse, "tool mouse binds");
            No(OptHD, "ORIGINAL: no HD"); No(OptForce400, "ORIGINAL: no 320x400");
        }

        // ---------------------------------------------------------------- parsing
        [Test] static void Ini_Parse_CommentsBlankLinesAndOtherLines()
        {
            IniPut("; display = ORIGINAL", "   ;freelook = 0", "", "   ", "\t", "[TNPlus]", "display = 320X400",
                "a line without an equals sign", ";noclip = 1", "music = FM");
            string said = IniLoad();
            Eq(1, Display, "display read, the comment before it ignored");
            Ok(OptFreelook, "freelook: commented out (after spaces)");
            No(OptNoclip, "noclip: commented out");
            Eq(1, OptMusic, "music: read after the other lines");
            Eq("", said.Trim(), "nothing printed");
        }

        [Test] static void Ini_Parse_SpacesAndTabsAroundEquals()
        {
            IniPut("display=ORIGINAL", "   launch   =   DEMO 1   ", "\tmusic\t=\tGAME'S OWN\t", "sensitivity_x=7", "key_noclip =2F");
            IniLoad();
            Eq(0, Display, "display=ORIGINAL");
            Eq(1, LaunchTarget, "launch with spaces around (and inside: DEMO 1)");
            Eq(2, OptMusic, "music with tabs around");
            Eq(7, SensX, "sensitivity_x=7");
            Eq(0x2F, ScanNoclip, "key_noclip =2F");
        }

        [Test] static void Ini_Parse_KeyNamesInAnyCase()
        {
            IniPut("DISPLAY = ORIGINAL", "Key_Freelook = 3F", "KEY_GAME_FWD = 48", "Mouse_Jump = M4", "NoClip_Speed = 3", "SPEED_VERSION = 6",
                "CPU_CYCLES = 1000000", "Game_Dir = X");
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            Eq(0, Display, "DISPLAY"); Eq(0x3F, ScanFreelook, "Key_Freelook"); IniKeyIs("fwd", 0x48, 0, "KEY_GAME_FWD");
            Eq(2, IniGame("jump").Mouse, "Mouse_Jump"); Eq(3.0, NoclipSpeed, "NoClip_Speed");
            Eq(1000000, CpuCycles, "SPEED_VERSION = 6 read: 1000000 kept"); Eq("X", GameDir, "Game_Dir");
        }

        [Test] static void Ini_Parse_ValueNamesInAnyCase()
        {
            IniPut("display = 320x400", "launch = demo 2", "view_distance = normal", "terrain_detail = sharp", "music = awe32",
                "scaling = game's own", "field_of_view = 100", "object_distance = far", "widescreen = true", "keys_layout = azerty",
                "key_noclip = 2f", "key_game_jump = shift+1e", "key_game_crouch = Alt+2b", "mouse_fwd = wheeldown");
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            Eq(1, Display, "display"); Eq(2, LaunchTarget, "launch"); Eq(0, OptDistance, "view_distance"); Eq(1, OptDetail, "terrain_detail");
            Eq(3, OptMusic, "music"); Eq(0, OptScaling, "scaling"); Eq(2, OptFov, "field_of_view"); Eq(1, OptObjDist, "object_distance");
            Eq(2, WideMode(), "widescreen = true (true 16:9 848x480)"); Eq(2, ChosenLayout, "keys_layout");
            Eq(0x2F, ScanNoclip, "key_noclip in lower case hex"); IniKeyIs("jump", 0x1E, 1, "shift+1e"); IniKeyIs("crouch", 0x2B, 3, "Alt+2b");
            Eq(5, IniGame("fwd").Mouse, "mouse_fwd = wheeldown");
        }

        [Test] static void Ini_Parse_HexWith0xPrefix()
        {
            // Convert.ToInt32(v, 16) takes a 0x / 0X prefix (documented .NET behaviour)
            IniPut("key_freelook = 0x3F", "key_stereo = 0X42", "key_game_fwd = CTRL+0x48");
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            Eq(0x3F, ScanFreelook, "key_freelook = 0x3F"); Eq(0x42, ScanStereo, "key_stereo = 0X42");
            IniKeyIs("fwd", 0x48, 2, "CTRL+0x48");
        }

        [Test] static void Ini_Parse_FirstEqualsSignSplits()
        {
            string dir = Path.Combine(ExeDir, "Terra=Nova = 2");
            IniPut("game_dir = " + dir, "sensitivity_y=5");
            IniLoad();
            Eq(dir, GameDir, "game_dir: everything after the first =");
            Eq(5, SensY, "the next line");
            SaveSettings();
            Eq(dir, IniSavedValue("game_dir"), "game_dir written back");
        }

        [Test] static void Ini_Parse_GameDirTrimmedOtherwiseVerbatim()
        {
            string dir = Path.Combine(ExeDir, "Jeux  \u00e9t\u00e9 (GOG)") + Path.DirectorySeparatorChar;
            IniPut("game_dir =  \t " + dir + "   \t");
            IniLoad();
            Eq(dir, GameDir, "spaces and tabs around trimmed, inner spaces, non-ASCII and the final separator kept");
            SaveSettings();
            Eq(dir, IniSavedValue("game_dir"), "written back (UTF-8)");
            GameDir = "";
            IniLoad();
            Eq(dir, GameDir, "read again");
            IniPut("game_dir =");
            IniLoad();
            Eq("", GameDir, "game_dir = (nothing): auto-detect");
        }

        [Test] static void Ini_Parse_UnknownKeysIgnoredQuietly()
        {
            IniPut("foo = bar", "hd_1080 = 1", "key_freelook_alt = 3B", "key_game_nothing = 20", "key_game_ = 20", "mouse_nothing = MID",
                "mouse_ = MID", "= 5", "sensitivity_y = 5");
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed for unknown keys");
            Eq(5, SensY, "the line after them read");
            Eq(0x15, ScanFreelook, "key_freelook untouched");
            EqList(new[] { "tprev" }, IniChangedKeys(), "game commands changed (only previous target, to K)");
            No(AnyMouseBind(), "no mouse bind");
        }

        [Test] static void Ini_Parse_BadValueIgnoredTheRestRead()
        {
            IniPut("sensitivity_x = abc", "sensitivity_y = 5", "key_freelook = ZZ", "key_noclip = 2F", "noclip_speed = fast",
                "cpu_cycles = 1.5", "key_distance = 1FFFFFFFF", "key_stereo =", "key_game_fwd = CTRL+XY", "key_game_back = SHIFT+",
                "  invert_y = 1  ", "terrain_detail = SHARP");
            string said = IniLoad();
            foreach (string l in new[] { "sensitivity_x = abc", "key_freelook = ZZ", "noclip_speed = fast", "cpu_cycles = 1.5",
                "key_distance = 1FFFFFFFF", "key_stereo =", "key_game_fwd = CTRL+XY", "key_game_back = SHIFT+" })
                Has(said, "Ignored bad setting: " + l + Environment.NewLine, "message for " + l);
            Eq(8, IniCount(said, "Ignored bad setting: "), "one message per bad line");
            Eq(12, SensX, "sensitivity_x kept"); Eq(0x15, ScanFreelook, "key_freelook kept"); Eq(15.0, NoclipSpeed, "noclip_speed kept");
            Eq(1800000, CpuCycles, "cpu_cycles kept"); Eq(0x24, ScanDistance, "key_distance kept (overflow)");
            Eq(0x41, ScanStereo, "key_stereo kept (empty)");
            IniKeyIs("fwd", 0x11, 0, "fwd kept"); IniKeyIs("back", 0x2D, 0, "back kept");
            Eq(5, SensY, "sensitivity_y read"); Eq(0x2F, ScanNoclip, "key_noclip read"); Ok(InvertY, "invert_y read");
            Eq(1, OptDetail, "the last line read");
        }

        [Test] static void Ini_Parse_EmptyNumberIgnored()
        {
            IniPut("sensitivity_x =", "sensitivity_y = ", "cpu_cycles =", "speed_version = 6");
            string said = IniLoad();
            Eq(3, IniCount(said, "Ignored bad setting: "), "one message each");
            Eq(12, SensX, "sensitivity_x"); Eq(8, SensY, "sensitivity_y"); Eq(1800000, CpuCycles, "cpu_cycles");
        }

        [Test] static void Ini_Parse_UnknownNameGivesTheFirstChoice()
        {
            // names are looked up in their lists; one not found gives the first entry, without a message
            IniPut("terrain_detail = ULTRA", "music = MT-32", "field_of_view = 120", "object_distance = HUGE", "launch = DEMO 3",
                "scaling = BILINEAR", "view_distance = NEAR");
            OptDetail = 2; OptMusic = 2; OptFov = 2; OptObjDist = 2; LaunchTarget = 2; OptScaling = 1; OptDistance = 1;
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            Eq(0, OptDetail, "terrain_detail GAME"); Eq(0, OptMusic, "music ROLAND"); Eq(0, OptFov, "field_of_view GAME");
            Eq(0, OptObjDist, "object_distance GAME"); Eq(0, LaunchTarget, "launch GAME"); Eq(0, OptScaling, "scaling GAME'S OWN");
            Eq(0, OptDistance, "view_distance NORMAL");
        }

        [Test] static void Ini_Parse_SwitchesZeroIsOff()
        {
            // the switches are on for anything but 0; invert_y only for 1
            IniPut("freelook = 0", "noclip = 1", "stereo_fix = 2", "thick_smoke = yes", "sound = 0", "edge_objects = 0",
                "hit_fix = 0", "invert_y = 2");
            IniLoad();
            No(OptFreelook, "freelook = 0"); Ok(OptNoclip, "noclip = 1"); Ok(OptStereoFix, "stereo_fix = 2");
            Ok(OptThickSmoke, "thick_smoke = yes"); No(Sound, "sound = 0"); No(OptEdge, "edge_objects = 0"); No(OptHitFix, "hit_fix = 0");
            No(InvertY, "invert_y = 2 (only 1 inverts)");
            IniPut("invert_y = 1");
            IniLoad();
            Ok(InvertY, "invert_y = 1");
        }

        [Test] static void Ini_Parse_LaterLineWins()
        {
            IniPut("freelook = 0", "cpu_cycles = 500000", "freelook = 1", "cpu_cycles = 2200000", "key_game_fwd = 48", "key_game_fwd = SHIFT+47");
            IniLoad();
            Ok(OptFreelook, "freelook: the last line");
            Eq(2200000, CpuCycles, "cpu_cycles: the last line");
            IniKeyIs("fwd", 0x47, 1, "key_game_fwd: the last line");
        }

        [Test] static void Ini_Parse_LineEndsAndEncodings()
        {
            string[] lines = { "display = ORIGINAL", "music = FM", "game_dir = C" + "\u00e9" };
            string text = string.Join("\n", lines) + "\n";
            Encoding[] encodings = { new UTF8Encoding(false), new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode };
            string[] names = { "UTF-8, LF only", "UTF-8 with BOM", "UTF-16 LE with BOM", "UTF-16 BE with BOM" };
            for (int i = 0; i < encodings.Length; i++)
            {
                Display = 2; OptMusic = 0; GameDir = "";
                File.WriteAllText(IniPath, i == 1 ? text.Replace("\n", "\r\n") : text, encodings[i]);
                string said = IniLoad();
                Eq("", said.Trim(), names[i] + ": nothing printed");
                Eq(0, Display, names[i] + ": the first line read (the BOM is not part of the key)");
                Eq(1, OptMusic, names[i] + ": music");
                Eq("C\u00e9", GameDir, names[i] + ": game_dir");
            }
            File.WriteAllText(IniPath, "display = 320X400\rmusic = AWE32\r", new UTF8Encoding(false));
            IniLoad();
            Eq(1, Display, "CR only: first line"); Eq(3, OptMusic, "CR only: second line");
        }

        [Test] static void Ini_Load_LeavesAnExistingFileAsItIs()
        {
            IniPut("; my own notes", "cpu_cycles = 700000", "key_freelook = 23", "view_distance = MAX", "widescreen = TRUE", "foo = bar");
            byte[] before = File.ReadAllBytes(IniPath);
            IniLoad();
            EqBytes(before, File.ReadAllBytes(IniPath), "the ini after LoadSettings (migrations only in memory)");
            Eq(1800000, CpuCycles, "cpu_cycles migrated in memory");
            Eq(0x15, ScanFreelook, "freelook migrated in memory");
        }

        // ---------------------------------------------------------------- game keys and mouse binds
        [Test] static void Ini_GameKeys_ModifiersReadAndWritten()
        {
            IniPut("key_game_fwd = 48", "key_game_back = shift+1f", "key_game_turnl = Ctrl+2C", "key_game_turnr = ALT+2E",
                "key_game_o_attack = 1E", "key_game_d_recall = CTRL+47");
            IniLoad();
            IniKeyIs("fwd", 0x48, 0, "plain"); IniKeyIs("back", 0x1F, 1, "SHIFT"); IniKeyIs("turnl", 0x2C, 2, "CTRL");
            IniKeyIs("turnr", 0x2E, 3, "ALT"); IniKeyIs("o_attack", 0x1E, 0, "Alt+A order on plain A");
            IniKeyIs("d_recall", 0x47, 2, "drone recall (default Ctrl+Home, E0) on Ctrl+keypad 7");
            SaveSettings();
            EqList(new[] { "key_game_fwd = 48", "key_game_back = SHIFT+1F", "key_game_turnl = CTRL+2C", "key_game_turnr = ALT+2E",
                "key_game_tprev = 25", "key_game_o_attack = 1E", "key_game_d_recall = CTRL+47" }, IniSavedStarting("key_game_"),
                "key_game_ lines, in the order of the key card");
        }

        [Test] static void Ini_GameKeys_OutOfRangeAndFixedIgnored()
        {
            IniPut("key_game_fwd = 00", "key_game_back = 80", "key_game_stop = C9", "key_game_options = 20", "key_game_abort = 20",
                "key_game_abort = 1E", "key_game_exit = 7F");
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            IniKeyIs("fwd", 0x11, 0, "00 ignored"); IniKeyIs("back", 0x2D, 0, "80 ignored (E0 keys can't be given)");
            IniKeyIs("stop", 0x1F, 0, "C9 ignored"); IniKeyIs("options", 0x18, 0, "options: fixed");
            IniKeyIs("abort", 0x01, 2, "abort: fixed"); IniKeyIs("exit", 0x7F, 0, "7F: the last scancode taken");
        }

        [Test] static void Ini_GameKeys_DefaultsNotWritten()
        {
            IniPut("key_freelook = 3F", "key_game_fwd = 11", "key_game_o_attack = ALT+1E", "key_game_o_cover = SHIFT+2E");
            IniLoad();
            EqList(new[] { "o_cover" }, IniChangedKeys(), "changed (fwd and o_attack on their own keys; Y free: tprev stays)");
            SaveSettings();
            EqList(new[] { "key_game_o_cover = SHIFT+2E" }, IniSavedStarting("key_game_"), "only the changed command written");
        }

        [Test] static void Ini_Mouse_EveryBindReadAndWritten()
        {
            IniPut("mouse_fwd = MID", "mouse_back = M4", "mouse_turnl = M5", "mouse_turnr = WHEELUP", "mouse_stop = WHEELDOWN",
                "mouse_sidel = PGUP", "mouse_sider = pgdn", "mouse_jump = HOME", "mouse_crouch = end", "mouse_freelook = INS",
                "mouse_distance = DEL");
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            string[] ids = { "fwd", "back", "turnl", "turnr", "stop", "sidel", "sider", "jump", "crouch" };
            for (int i = 0; i < ids.Length; i++) Eq(i + 1, IniGame(ids[i]).Mouse, "mouse_" + ids[i]);
            EqList(new[] { 10, 0, 11, 0 }, ToolMouse, "tool binds (freelook INS, distance DEL)");
            SaveSettings();
            EqList(new[] { "mouse_fwd = MID", "mouse_back = M4", "mouse_turnl = M5", "mouse_turnr = WHEELUP", "mouse_stop = WHEELDOWN",
                "mouse_sidel = PGUP", "mouse_sider = PGDN", "mouse_jump = HOME", "mouse_crouch = END", "mouse_freelook = INS",
                "mouse_distance = DEL" }, IniSavedStarting("mouse_"), "mouse_ lines: the game's commands, then the tool's");
        }

        [Test] static void Ini_Mouse_BadBindsIgnored()
        {
            IniPut("mouse_fwd = WH+", "mouse_back =", "mouse_turnl = MIDDLE", "mouse_options = MID", "mouse_abort = M4",
                "mouse_nothing = M5", "mouse_noclip = wheeldown");
            string said = IniLoad();
            Eq("", said.Trim(), "nothing printed");
            foreach (GameKey g in GameKeys) Eq(0, g.Mouse, "mouse bind of " + g.Id);
            EqList(new[] { 0, 5, 0, 0 }, ToolMouse, "the one good bind (noclip wheel down)");
        }

        // ---------------------------------------------------------------- the game's keys off the tool's (ResolveToolClashes)
        [Test] static void Ini_Clash_GameKeyLeavesAToolKey()
        {
            IniPut("key_freelook = 11");             // freelook on W: forward goes to K, Y stays the previous target
            IniLoad();
            IniKeyIs("fwd", 0x25, 0, "forward moved to K");
            IniKeyIs("tprev", 0x15, 0, "previous target on Y (free)");
            SaveSettings();
            EqList(new[] { "key_game_fwd = 25" }, IniSavedStarting("key_game_"), "key_game_ lines");
        }

        [Test] static void Ini_Clash_NextFreeSpareKey()
        {
            IniPut("key_noclip = 25");               // K the tool's: L next
            IniLoad();
            IniKeyIs("tprev", 0x26, 0, "previous target on L (K taken by noclip)");
            IniDefaultKeys();
            IniPut("key_noclip = 25", "key_game_v360 = 26");   // and L a game command's: ; next
            IniLoad();
            IniKeyIs("tprev", 0x27, 0, "previous target on ; (K and L taken)");
        }

        [Test] static void Ini_Clash_NoSpareLeftKeyStays()
        {
            IniPut("key_noclip = 25", "key_distance = 26", "key_stereo = 27", "key_game_w1 = 08", "key_game_w2 = 09",
                "key_game_w3 = 0A", "key_game_w4 = 0B");
            IniLoad();
            IniKeyIs("tprev", 0x15, 0, "previous target left on Y: K L ; 7 8 9 0 all taken");
        }

        [Test] static void Ini_Clash_ChordsAndFixedKeysNotMoved()
        {
            IniPut("key_game_tprev = ALT+15", "key_game_fwd = SHIFT+16", "key_stereo = 18");
            IniLoad();
            IniKeyIs("tprev", 0x15, 3, "Alt+Y stays (only plain keys clash)");
            IniKeyIs("fwd", 0x16, 1, "Shift+U stays");
            IniKeyIs("options", 0x18, 0, "the fixed options key stays");
        }

        // ---------------------------------------------------------------- old inis
        [Test] static void Ini_Cycles_OldIniWithoutSpeedVersion()
        {
            int[,] rows = { { 300000, 1800000 }, { 500000, 1800000 }, { 700000, 1800000 }, { 1000000, 1800000 },
                { 1400000, 1800000 }, { 2200000, 2200000 }, { 600000, 600000 }, { 0, 0 } };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                IniPut("cpu_cycles = " + rows[i, 0]);
                IniLoad();
                Eq(rows[i, 1], CpuCycles, "cpu_cycles " + rows[i, 0] + " without speed_version");
            }
        }

        [Test] static void Ini_Cycles_BySpeedVersion()
        {
            // speed_version, cpu_cycles read, cpu_cycles used
            int[,] rows = {
                { 2, 300000, 1800000 }, { 2, 500000, 1800000 },
                { 3, 300000, 300000 }, { 3, 500000, 500000 }, { 3, 700000, 1800000 }, { 3, 1000000, 1800000 },
                { 4, 700000, 700000 }, { 4, 1000000, 1800000 }, { 5, 700000, 700000 }, { 5, 1400000, 1800000 },
                { 6, 300000, 300000 }, { 6, 700000, 700000 }, { 6, 1000000, 1000000 }, { 6, 1400000, 1400000 },
                { 7, 1000000, 1000000 } };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                IniPut("cpu_cycles = " + rows[i, 1], "speed_version = " + rows[i, 0]);
                IniLoad();
                Eq(rows[i, 2], CpuCycles, "cpu_cycles " + rows[i, 1] + " with speed_version " + rows[i, 0]);
            }
            IniPut("speed_version = 6", "cpu_cycles = 700000");   // the version after the cycles: the same
            IniLoad();
            Eq(700000, CpuCycles, "speed_version before cpu_cycles");
        }

        [Test] static void Ini_Cycles_MovedOnceThenKept()
        {
            IniPut("cpu_cycles = 1000000");
            IniLoad();
            Eq(1800000, CpuCycles, "the old default moved");
            SaveSettings();
            CpuCycles = 1000000;                    // picked again in the launcher
            SaveSettings();
            CpuCycles = 5;
            IniLoad();
            Eq(1000000, CpuCycles, "a choice saved with speed_version 6 is kept");
        }

        [Test] static void Ini_KeyDistance_OldDefaultIMovesToJ()
        {
            IniPut("key_distance = 17");             // v1.0: view distance on I, the game's infrared
            IniLoad();
            Eq(0x24, ScanDistance, "view distance moved to J");
            IniKeyIs("ir", 0x17, 0, "infrared stays on I");
            IniPut("key_distance = 2B");
            IniLoad();
            Eq(0x2B, ScanDistance, "another key kept");
        }

        [Test] static void Ini_KeyFreelook_HMovesBackToYBeforeKeysVersion3()
        {
            // keys_version line (null: none), key_freelook, freelook used, previous target
            object[][] rows = {
                new object[] { null, "23", 0x15, 0x25 }, new object[] { "1", "23", 0x15, 0x25 }, new object[] { "2", "23", 0x15, 0x25 },
                new object[] { "3", "23", 0x23, 0x15 }, new object[] { "4", "23", 0x23, 0x15 }, new object[] { "1", "15", 0x15, 0x25 },
                new object[] { null, "3B", 0x3B, 0x15 } };
            foreach (object[] r in rows)
            {
                IniDefaultKeys();
                if (r[0] == null) IniPut("key_freelook = " + r[1]); else IniPut("key_freelook = " + r[1], "keys_version = " + r[0]);
                IniLoad();
                string what = "key_freelook = " + r[1] + (r[0] == null ? " without keys_version" : ", keys_version " + r[0]);
                Eq((int)r[2], ScanFreelook, what);
                IniKeyIs("tprev", (int)r[3], 0, what);
            }
        }

        [Test] static void Ini_ViewDistance_MaxReadAsFar()
        {
            string[] values = { "MAX", "max", "FAR", "NORMAL", "far" };
            int[] expected = { 1, 1, 1, 0, 1 };
            for (int i = 0; i < values.Length; i++)
            {
                OptDistance = 1 - expected[i];
                IniPut("view_distance = " + values[i]);
                IniLoad();
                Eq(expected[i], OptDistance, "view_distance = " + values[i]);
            }
            IniPut("view_distance = MAX");
            IniLoad();
            SaveSettings();
            Eq("FAR", IniSavedValue("view_distance"), "MAX written back as FAR");
        }

        [Test] static void Ini_Display_FromTheLinesOfV10()
        {
            string[][] rows = {
                new[] { "hd = 1", "force_320x400 = 0" }, new[] { "hd = 0", "force_320x400 = 1" }, new[] { "hd = 0", "force_320x400 = 0" },
                new[] { "force_320x400 = 1", "hd = 1" }, new[] { "force_320x400 = 1" }, new[] { "hd = 1" },
                new[] { "hd = 1", "display = ORIGINAL" }, new[] { "display = 320X400", "hd = 1" }, new[] { "force_320x400 = 1", "display = HD" } };
            int[] expected = { 2, 1, 0, 2, 1, 2, 0, 1, 2 };
            for (int i = 0; i < rows.Length; i++)
            {
                Display = (expected[i] + 1) % 3;
                IniPut(rows[i]);
                IniLoad();
                Eq(expected[i], Display, string.Join(" / ", rows[i]));
            }
            IniPut("hd = 1", "force_320x400 = 1");
            IniLoad();
            SaveSettings();
            Eq("HD", IniSavedValue("display"), "written as display");
            Eq(null, IniSavedValue("hd"), "no hd line");
            Eq(null, IniSavedValue("force_320x400"), "no force_320x400 line");
        }

        [Test] static void Ini_Widescreen_Values()
        {
            string[] values = { "0", "1", "2", "3", "TRUE", "true" };
            int[] expected = { 0, 1, 2, 3, 2, 2 };
            for (int i = 0; i < values.Length; i++)
            {
                SetWideMode(expected[i] == 3 ? 0 : 3);
                IniPut("widescreen = " + values[i]);
                IniLoad();
                int m = expected[i];
                string what = "widescreen = " + values[i];
                Eq(m, WideMode(), what);
                Eq(m > 0, OptWide, what + ": OptWide"); Eq(m >= 2, OptWideTrue, what + ": OptWideTrue"); Eq(m == 3, OptHd720, what + ": OptHd720");
                SaveSettings();
                Eq(m.ToString(), IniSavedValue("widescreen"), what + ": written");
            }
        }

        [Test] static void Ini_Hd720_OnlyAfterTrueWidescreen()
        {
            // hd_720 (first 1280x720 builds) gives 1280x720 only to a true 16:9 read before it; never written again
            string[][] rows = {
                new[] { "widescreen = 2", "hd_720 = 1" }, new[] { "widescreen = 1", "hd_720 = 1" }, new[] { "widescreen = 0", "hd_720 = 1" },
                new[] { "hd_720 = 1", "widescreen = 2" }, new[] { "widescreen = 3", "hd_720 = 0" }, new[] { "widescreen = 2", "hd_720 = 0" },
                new[] { "widescreen = TRUE", "hd_720 = 1" } };
            int[] expected = { 3, 1, 0, 2, 3, 2, 3 };
            for (int i = 0; i < rows.Length; i++)
            {
                SetWideMode(3);
                IniPut(rows[i]);
                IniLoad();
                Eq(expected[i], WideMode(), string.Join(" / ", rows[i]));
                SaveSettings();
                Eq(null, IniSavedValue("hd_720"), "no hd_720 line written");
            }
        }

        [Test] static void Ini_KeysLayout_Values()
        {
            string[] values = { "GAME", "QWERTY", "azerty", "QWERTZ", "Dvorak", "COLEMAK", "CUSTOM", "BEPO" };
            int[] expected = { 0, 1, 2, 3, 4, 5, -1, -1 };
            string[] written = { "GAME", "QWERTY", "AZERTY", "QWERTZ", "DVORAK", "COLEMAK", null, null };
            for (int i = 0; i < values.Length; i++)
            {
                ChosenLayout = 1;
                IniPut("keys_layout = " + values[i]);
                IniLoad();
                Eq(expected[i], ChosenLayout, "keys_layout = " + values[i]);
                SaveSettings();
                Eq(written[i], IniSavedValue("keys_layout"), "keys_layout = " + values[i] + ": written");
            }
        }

        // ---------------------------------------------------------------- ApplyDisplay after loading
        [Test] static void Ini_ApplyDisplay_HdWithoutTheGame()
        {
            string dir = TestDir("game");          // a folder without TNOVA\__FF.EXE
            IniPut("display = HD", "game_dir = " + dir);
            OptHD = true; OptForce400 = false;
            IniLoad();
            Eq(2, Display, "the choice stays HD");
            No(OptHD, "OptHD: HD not available");
            Ok(OptForce400, "OptForce400: 320x400 instead");
            string why;
            No(HdAvailable(out why), "HdAvailable");
            Eq("game folder not found", why, "why");
            SaveSettings();
            Eq("HD", IniSavedValue("display"), "HD still written");
        }

        [Test] static void Ini_ApplyDisplay_HdWithAnUnknownExe()
        {
            string dir = TestDir("game");
            PutFile(Path.Combine(Path.Combine(dir, "TNOVA"), "__FF.EXE"), Bytes("4D5A90000300000004000000FFFF0000"));
            IniPut("display = HD", "game_dir = " + dir);
            OptHD = true; OptForce400 = false;
            IniLoad();
            No(OptHD, "OptHD: unknown __FF.EXE");
            Ok(OptForce400, "OptForce400: 320x400 instead");
            string why;
            No(HdAvailable(out why), "HdAvailable");
            Eq("not available for this version of the game", why, "why");
        }

        [Test] static void Ini_ApplyDisplay_HdForADemo()
        {
            IniPut("display = HD", "launch = DEMO 1", "game_dir = " + TestDir("game"));
            OptHD = true; OptForce400 = false;
            IniLoad();
            No(OptHD, "OptHD: a demo"); Ok(OptForce400, "OptForce400");
            string why;
            No(HdAvailable(out why), "HdAvailable");
            Eq("not available for the demos yet", why, "why");
        }

        [Test] static void Ini_ApplyDisplay_OriginalAnd320x400()
        {
            IniPut("display = ORIGINAL");
            OptHD = true; OptForce400 = true;
            IniLoad();
            No(OptHD, "ORIGINAL: OptHD"); No(OptForce400, "ORIGINAL: OptForce400");
            IniPut("display = 320X400");
            OptHD = true; OptForce400 = false;
            IniLoad();
            No(OptHD, "320X400: OptHD"); Ok(OptForce400, "320X400: OptForce400");
        }

        // ---------------------------------------------------------------- cultures
        [Test] static void Ini_Culture_FrenchSaveAndLoad()
        {
            IniInCulture("fr-FR", delegate
            {
                Eq(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, "fr-FR has a decimal comma (the test's premise)");
                NoclipSpeed = 2.5; SensX = 20; CpuCycles = 2200000; ScanStereo = 0x3F; ScanFreelook = 0x2B; IniGame("fwd").Mod = 3;
                SaveSettings();
                Eq("2.5", IniSavedValue("noclip_speed"), "noclip_speed with a decimal point");
                Eq("2200000", IniSavedValue("cpu_cycles"), "cpu_cycles without a group separator");
                Eq("20", IniSavedValue("sensitivity_x"), "sensitivity_x");
                Eq("3F", IniSavedValue("key_stereo"), "key_stereo"); Eq("2B", IniSavedValue("key_freelook"), "key_freelook");
                Eq("ALT+11", IniSavedValue("key_game_fwd"), "key_game_fwd");
                NoclipSpeed = 15; SensX = 12; CpuCycles = 0; ScanStereo = 0x41; ScanFreelook = 0x15; IniDefaultKeys();
                string said = IniLoad();
                Eq("", said.Trim(), "nothing printed");
                Eq(2.5, NoclipSpeed, "noclip_speed read"); Eq(20, SensX, "sensitivity_x read"); Eq(2200000, CpuCycles, "cpu_cycles read");
                Eq(0x3F, ScanStereo, "key_stereo read"); Eq(0x2B, ScanFreelook, "key_freelook read");
                IniKeyIs("fwd", 0x11, 3, "key_game_fwd read");
            });
        }

        [Test] static void Ini_Culture_GermanReadsTheDecimalPoint()
        {
            IniInCulture("de-DE", delegate
            {
                IniPut("noclip_speed = 7.25", "key_freelook = 3b", "sensitivity_y = 4", "cpu_cycles = 1400000", "speed_version = 6");
                string said = IniLoad();
                Eq("", said.Trim(), "nothing printed");
                Eq(7.25, NoclipSpeed, "noclip_speed 7.25 (not 725)");
                Eq(0x3B, ScanFreelook, "key_freelook"); Eq(4, SensY, "sensitivity_y"); Eq(1400000, CpuCycles, "cpu_cycles");
                double[] speeds = { 7.25, 0.5, 1234.5, 15 };
                string[] texts = { "7.25", "0.5", "1234.5", "15" };
                for (int i = 0; i < speeds.Length; i++)
                {
                    NoclipSpeed = speeds[i];
                    SaveSettings();
                    Eq(texts[i], IniSavedValue("noclip_speed"), "noclip_speed " + texts[i] + " written");
                }
            });
        }

        [Test] static void Ini_Culture_TurkishCaseOfKeysAndValues()
        {
            IniInCulture("tr-TR", delegate
            {
                Eq("\u0130", "i".ToUpper(), "tr-TR: i in upper case is a dotted I (the test's premise)");
                IniPut("INVERT_Y = 1", "DISPLAY = 320x400", "THICK_SMOKE = 1", "EDGE_OBJECTS = 0", "SMALL_VIEWS = 0", "SPEED_FIXES = 0",
                    "TERRAIN_DETAIL = sharp", "FIELD_OF_VIEW = 110", "KEY_DISTANCE = 2b", "KEY_GAME_O_PICKUP = shift+1f",
                    "MOUSE_FREELOOK = mid", "MOUSE_NOCLIP = ins", "SENSITIVITY_X = 9");
                string said = IniLoad();
                Eq("", said.Trim(), "nothing printed");
                Ok(InvertY, "INVERT_Y"); Eq(1, Display, "DISPLAY"); Ok(OptThickSmoke, "THICK_SMOKE"); No(OptEdge, "EDGE_OBJECTS");
                No(OptSmallViews, "SMALL_VIEWS"); No(OptSpeedFix, "SPEED_FIXES"); Eq(1, OptDetail, "TERRAIN_DETAIL");
                Eq(3, OptFov, "FIELD_OF_VIEW"); Eq(0x2B, ScanDistance, "KEY_DISTANCE"); IniKeyIs("o_pickup", 0x1F, 1, "KEY_GAME_O_PICKUP = shift+1f");
                EqList(new[] { 1, 10, 0, 0 }, ToolMouse, "MOUSE_FREELOOK = mid, MOUSE_NOCLIP = ins"); Eq(9, SensX, "SENSITIVITY_X");
            });
        }

        // ---------------------------------------------------------------- fixed bugs
        [Test] static void Ini_KeyDistance_IChosenInTheKeyboardTabIsKept()
        {
            // v1.0's default I is moved to J once (no keys_version, or older than 3), a choice saved since stays on I
            IniPut("key_distance = 17", "key_game_ir = 24", "keys_version = 3");
            IniLoad();
            Eq(0x17, ScanDistance, "view distance on I (keys_version 3)");
            IniKeyIs("ir", 0x24, 0, "infrared on J, where the swap put it");
            IniDefaultKeys(); ScanDistance = 0x24;
            IniPut("keys_version = 3", "key_distance = 17");
            IniLoad();
            Eq(0x17, ScanDistance, "keys_version before key_distance: the same");
            foreach (string kv in new[] { null, "1", "2" })
            {
                IniDefaultKeys(); ScanDistance = 0x24;
                if (kv == null) IniPut("key_distance = 17"); else IniPut("key_distance = 17", "keys_version = " + kv);
                IniLoad();
                Eq(0x24, ScanDistance, "an old ini (keys_version " + (kv ?? "none") + "): I moved to J");
            }
        }

        [Test] static void Ini_KeyDistance_ChoiceSurvivesARestart()
        {
            IniLoad();                                // a first start (previous target on K)
            Eq("view distance: I, infrared moved to J", AssignToolKey(2, 0x17, false), "view distance put on I");
            SaveSettings();
            IniDefaultKeys(); ScanDistance = 0x24;    // the tool started again
            IniLoad();
            Eq(0x17, ScanDistance, "view distance still on I");
            IniKeyIs("ir", 0x24, 0, "infrared still on J");
            IniKeyIs("tprev", 0x25, 0, "previous target still on K");
        }

        [Test] static void Ini_Display_WithoutItsLinesKeepsTheDefault()
        {
            // only an ini of v1.0.x (force_320x400 / hd lines) decides the display without a display line
            string[] what = { "an empty TNPlus.ini", "an ini with other settings only", "an ini of NUL bytes (cut short)" };
            for (int i = 0; i < what.Length; i++)
            {
                Display = 2;
                if (i == 0) PutFile(IniPath, "");
                else if (i == 1) IniPut("freelook = 1", "game_dir = ");
                else PutFile(IniPath, new byte[64]);
                IniLoad();
                Eq(2, Display, what[i] + ": display HD (the default)");
            }
            IniPut("hd = 0", "force_320x400 = 0");
            IniLoad();
            Eq(0, Display, "v1.0.x with both off: ORIGINAL, as before");
        }

        [Test] static void Ini_Parse_BadTerrainFpsKeepsTheDefault()
        {
            foreach (string bad in new[] { "60fps", "6O", "60.0", "" })
            {
                OptTerrainFps = 60;
                IniPut("terrain_fps = " + bad);
                string said = IniLoad();
                Has(said, "Ignored bad setting: terrain_fps =" + (bad == "" ? "" : " " + bad), "message for terrain_fps = " + bad);
                Eq(60, OptTerrainFps, "terrain_fps = " + bad + ": 60 kept");
            }
            IniPut("terrain_fps = 45");
            IniLoad();
            Eq(45, OptTerrainFps, "45 read");
            IniPut("terrain_fps = 0");
            IniLoad();
            Eq(0, OptTerrainFps, "0 read (adaptation off)");
        }

        [Test] static void Ini_GameDir_QuotesRemoved()
        {
            // a path pasted with Explorer's "Copy as path" comes in quotes: .NET Framework refuses '"' in a path
            string dir = TestDir("GOG Games", "Terra Nova");
            PutFile(Path.Combine(dir, "_DOSBOX", "dosbox-staging.exe"), new byte[0]);
            PutFile(Path.Combine(dir, "_DOSBOX", "dosbox_terranova_windows.conf"), "");
            IniPut("game_dir = \"" + dir + "\"");
            IniLoad();
            Eq(dir, GameDir, "game_dir without its quotes");
            Ok(IsInstall(GameDir), "the install is found");
            SaveSettings();
            Eq(dir, IniSavedValue("game_dir"), "written back without quotes");
            IniPut("game_dir = \" " + dir + " \"");
            IniLoad();
            Eq(dir, GameDir, "spaces inside the quotes trimmed too");
        }
    }
}
