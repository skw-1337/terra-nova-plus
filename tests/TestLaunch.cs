// Game folder detection (IsInstall, HasDemo, InstallKind, ExeLanguage, HdAvailable, PrepareExeForHd), the DOSBox launch
// conf (CpuSection, WriteLaunchConf, the key map copy of TNPlusMapper.cs) and the music settings (ApplyMusic, AWE32).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ---------------------------------------------------------------- helpers (prefix Launch)
        static class LaunchKit
        {
            // the sections WriteLaunchConf puts before the edition's launch file (defaults: 1800000 cycles, SHARP)
            internal const string CPU = "[cpu]\r\ncpu_cycles = 1800000\r\ncpu_cycles_protected = auto\r\ncpu_throttle = true\r\n";
            internal const string SHARP = "[sdl]\r\noutput = opengl\r\n[render]\r\nglshader = sharp\r\n";
            internal const string MID = "[mouse]\r\nmouse_middle_release = false\r\n";
            internal const string MIXER = "mixer sb reverse /noshow\r\n";
            // a made-up edition: its DOSBox config (Steam's cycles), its launch file, the game's TN.CFG
            internal const string EDITION = "[sdl]\r\nfullscreen = true\r\n[cpu]\r\ncpu_cycles = 115000\r\n";
            internal const string STOCK = "[autoexec]\r\n# Lines in this section will be run at startup.\r\n@echo off\r\n" +
                "mount C \"..\"\r\nC:\r\ncd TNOVA\r\ncall TN.BAT\r\nexit\r\n";
            internal const string TNCFG = "digi_num 7\r\ndigi_io 544\r\nmidi_num 3\r\nmidi_io 544\r\n";

            // an install in ExeDir\game (_DOSBOX with an empty dosbox-staging.exe and the configs given, TNOVA\TN.CFG),
            // GameDir set to it; returns its _DOSBOX folder
            internal static string Game(string conf, string stock)
            {
                string root = TestDir("game"), db = TestDir("game", "_DOSBOX");
                PutFile(Path.Combine(db, "dosbox-staging.exe"), new byte[0]);
                if (conf != null) PutFile(Path.Combine(db, "dosbox_terranova_windows.conf"), conf);
                if (stock != null) PutFile(Path.Combine(db, "dosbox_terranova_windows_launch.conf"), stock);
                PutFile(Path.Combine(root, "TNOVA", "TN.CFG"), TNCFG);
                GameDir = root;
                return db;
            }

            internal static string Game() { return Game(EDITION, STOCK); }

            // the edition's config with this mapperfile (an absolute path: the %LOCALAPPDATA% fallback never decides)
            internal static void UseMap(string db, string map)
            {
                PutFile(Path.Combine(db, "dosbox_terranova_windows.conf"), EDITION + "[sdl]\r\nmapperfile = " + map + "\r\n");
            }

            // a DOSBox key map made of these lines (CRLF)
            internal static string Map(string path, params string[] lines)
            {
                return PutFile(path, string.Join("\r\n", lines) + "\r\n");
            }

            // a map in the AZERTY style: Q and A swapped, W and Z swapped, gamepad bindings on Q and Space
            internal static string Azerty(string db)
            {
                return Map(Path.Combine(db, "azerty.map"),
                    "# DOSBox Staging key map (made up)",
                    "hand_shutdown \"key 69 mod1\"",
                    "key_esc \"key 41\"",
                    "key_q \"key 4\" \"stick_0 button 2\"",
                    "key_a \"key 20\"",
                    "key_w \"key 29\"",
                    "key_z \"key 26\"",
                    "key_space \"key 44\" \"jbutton_0_0\"");
            }

            // every DOS key DOSBox knows on its own host key, a gamepad binding on Space
            internal static string Neutral(string path)
            {
                List<string> l = new List<string> { "# every key in its place" };
                foreach (KeyValuePair<string, int> kv in MAPPER_KEYS)
                    l.Add("key_" + kv.Key + " \"key " + kv.Value + "\"" + (kv.Key == "space" ? " \"jbutton_0_1\"" : ""));
                return Map(path, l.ToArray());
            }

            // what the tool prints while a runs (Console.WriteLine), also passed on to the test's console
            internal static string Said(Action a)
            {
                TextWriter old = Console.Out;
                StringWriter sw = new StringWriter();
                Console.SetOut(sw);
                try { a(); }
                finally { Console.SetOut(old); }
                old.Write(sw.ToString());
                return sw.ToString();
            }

            internal static string Copy { get { return Path.Combine(ExeDir, "TNPlus_keys.map"); } }
            internal static string ConfPath { get { return Path.Combine(ExeDir, "TNPlus_launch.conf"); } }
            internal static string Cfg(string dirName) { return Path.Combine(GameDir, dirName, "TN.CFG"); }

            // lines of a text whatever its line ends, without the empty one after a last line end (keep_dos: the
            // replaced EXIT lines lose their CR, the last one its line end: fine for DOSBox, not what is tested)
            internal static List<string> Lines(string text)
            {
                List<string> l = new List<string>();
                foreach (string s in text.Split('\n')) l.Add(s.TrimEnd('\r'));
                if (l.Count > 0 && l[l.Count - 1] == "") l.RemoveAt(l.Count - 1);
                return l;
            }

            // a file of exactly n bytes (zeros)
            internal static string Sized(string path, long n)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write)) f.SetLength(n);
                return path;
            }

            // a name no DOSBox config folder has (ResolveMapper looks in %LOCALAPPDATA%\DOSBox first)
            internal static string Unique() { return "TNPlusTest-" + Guid.NewGuid().ToString("N") + ".map"; }

            internal static bool DosboxRunning() { return Process.GetProcessesByName("dosbox-staging").Length > 0; }
        }

        // ---------------------------------------------------------------- game folder
        [Test] static void Launch_IsInstall_TheTwoDosboxFiles()
        {
            string dir = TestDir("game");
            string exe = Path.Combine(dir, "_DOSBOX", "dosbox-staging.exe"), conf = Path.Combine(dir, "_DOSBOX", "dosbox_terranova_windows.conf");
            No(IsInstall(dir), "an empty folder");
            PutFile(exe, new byte[0]);
            No(IsInstall(dir), "dosbox-staging.exe without the edition's config");
            File.Delete(exe);
            PutFile(conf, LaunchKit.EDITION);
            No(IsInstall(dir), "the edition's config without dosbox-staging.exe");
            PutFile(exe, new byte[0]);
            PutFile(Path.Combine(dir, "_DOSBOX", "dosbox_terranova_windows_launch.conf"), LaunchKit.STOCK);
            Ok(IsInstall(dir), "an edition's _DOSBOX (the TNOVA folder is not looked at)");
            No(IsInstall(""), "game_dir not set");
            No(IsInstall(Path.Combine(ExeDir, "nowhere")), "a missing folder");
            No(IsInstall(Path.Combine(dir, "_DOSBOX")), "the _DOSBOX folder itself");
        }

        [Test] static void Launch_HasDemo_NeedsBatAndCfg()
        {
            string dir = TestDir("game");
            for (int n = 1; n <= 2; n++)
            {
                string d = Path.Combine(dir, TARGET_DIRS[n]), bat = Path.Combine(d, "TNDEMO.BAT"), cfg = Path.Combine(d, "TN.CFG");
                No(HasDemo(dir, n), "demo " + n + ": no folder (the other demo installed: " + (n == 2) + ")");
                PutFile(bat, "@echo off\r\n");
                No(HasDemo(dir, n), "demo " + n + ": TNDEMO.BAT without TN.CFG");
                File.Delete(bat);
                PutFile(cfg, LaunchKit.TNCFG);
                No(HasDemo(dir, n), "demo " + n + ": TN.CFG without TNDEMO.BAT");
                PutFile(bat, "@echo off\r\n");
                Ok(HasDemo(dir, n), "demo " + n + ": TNDEMO.BAT and TN.CFG");
            }
            Eq("TNDEMO1", TARGET_DIRS[1], "demo 1 folder");
            Eq("TNDEMO2", TARGET_DIRS[2], "demo 2 folder");
            No(HasDemo("", 1), "game_dir not set");
        }

        [Test] static void Launch_InstallKind_Steam()
        {
            Eq("STEAM", InstallKind(Path.Combine(ExeDir, "Steam", "steamapps", "common", "Terra Nova Strike Force Centauri")), "a Steam library");
            Eq("STEAM", InstallKind(Path.Combine(ExeDir, "Games", "SteamApps", "common", "TN")), "any case");
            Eq("OTHER", InstallKind(Path.Combine(ExeDir, "mysteamapps", "common", "TN")), "a folder only ending in steamapps");
            Eq("OTHER", InstallKind(Path.Combine(ExeDir, "steamapps2", "TN")), "a folder only starting with steamapps");
            Eq("OTHER", InstallKind(Path.Combine(ExeDir, "steamapps")), "steamapps as the last folder (no separator after it)");
            // Steam is told first: a GOG info file or a GOG Galaxy folder above it changes nothing
            string lib = TestDir("GOG Galaxy", "steamapps", "common", "TN");
            PutFile(Path.Combine(lib, "goggame-1434984562.info"), "{}");
            Eq("STEAM", InstallKind(lib), "Steam before GOG");
        }

        [Test] static void Launch_InstallKind_GogAndOther()
        {
            string dir = TestDir("game");
            Eq("OTHER", InstallKind(dir), "a plain folder");
            PutFile(Path.Combine(dir, "goggame-1207658691.info"), "{}");
            Eq("OTHER", InstallKind(dir), "another GOG game's info file");
            PutFile(Path.Combine(dir, "goggame-1434984562.info"), "{}");
            Eq("GOG", InstallKind(dir), "Terra Nova's GOG info file (game id 1434984562)");
            Eq("GOG", InstallKind(Path.Combine(ExeDir, "GOG Galaxy", "Games", "Terra Nova")), "a GOG Galaxy folder, no info file");
            Eq("GOG", InstallKind(Path.Combine(ExeDir, "gog galaxy", "Games", "TN")), "GOG Galaxy in any case");
            Eq("OTHER", InstallKind(Path.Combine(ExeDir, "GOGGalaxy", "TN")), "GOGGalaxy without the space");
        }

        [Test] static void Launch_ExeLanguage_MissingOrUnknownExe()
        {
            string none = TestDir("none"), junk = TestDir("junk"), empty = TestDir("empty");
            Eq("", ExeLanguage(none), "no TNOVA\\__FF.EXE");
            PutFile(Path.Combine(junk, "TNOVA", "__FF.EXE"), Bytes("4D5A90000300000004000000FFFF0000B8000000"));
            Eq("", ExeLanguage(junk), "an executable none of the known SHA-256");
            PutFile(Path.Combine(empty, "TNOVA", "__FF.EXE"), new byte[0]);
            Eq("", ExeLanguage(empty), "an empty __FF.EXE");
        }

        [Test] static void Launch_ExeLanguage_CachedPerFolder()
        {
            string a = TestDir("a"), b = TestDir("b");
            Eq("", ExeLanguage(a), "a: no exe");
            Eq(a, exeLangDir, "the folder read is remembered");
            exeLang = "French";                         // as if a held the French exe
            Eq("French", ExeLanguage(a), "same folder: the remembered answer (the exe is not read again)");
            Eq("", ExeLanguage(b), "another folder: read");
            Eq(b, exeLangDir, "b remembered now");
            Eq("", ExeLanguage(a), "back to a: read again (one folder remembered)");
        }

        // ---------------------------------------------------------------- SHA-256 of the executables
        [Test] static void Launch_Sha256_TestVectors()
        {
            // FIPS 180-2 vectors: lower-case hex, as the constants are written
            Eq("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Sha256(Encoding.ASCII.GetBytes("abc")), "abc");
            Eq("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Sha256(new byte[0]), "empty");
            Eq("248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1",
                Sha256(Encoding.ASCII.GetBytes("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq")), "448 bits");
            byte[] million = new byte[1000000];
            for (int i = 0; i < million.Length; i++) million[i] = (byte)'a';
            Eq("cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0", Sha256(million), "a million a");
        }

        static readonly string[] LaunchFrenchShas = { HD_SHA_ORIGINAL, HD_SHA_READY, HD_SHA_READY2, HD_SHA_READY3, HD_SHA_READY4, HD_SHA_READY5 };
        static readonly string[] LaunchEnglishShas = { EN_SHA, EN_SHA_READY, EN_SHA_READY2, EN_SHA_READY3, EN_SHA_READY4, EN_SHA_READY5 };

        [Test] static void Launch_KnownSha_TheTwelveExecutables()
        {
            HashSet<string> all = new HashSet<string>();
            for (int e = 0; e < 2; e++)
                foreach (string s in e == 0 ? LaunchFrenchShas : LaunchEnglishShas)
                {
                    Eq(64, s.Length, "length of " + s);
                    foreach (char c in s) Ok((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), "lower-case hex like Sha256 gives: " + s);
                    Ok(KnownSha(s), "known: " + s);
                    Eq(e == 1, EnglishSha(s), "English: " + s);
                    all.Add(s);
                }
            Eq(12, all.Count, "12 different executables (French and English, original and the 5 prepared states)");
        }

        [Test] static void Launch_KnownSha_RejectsOthers()
        {
            No(KnownSha(Sha256(Encoding.ASCII.GetBytes("abc"))), "the SHA-256 of abc");
            No(KnownSha(Sha256(new byte[0])), "an empty file");
            No(KnownSha(EN_SHA.ToUpperInvariant()), "upper case (Sha256 gives lower case)");
            No(KnownSha(HD_SHA_ORIGINAL.Substring(1)), "one digit short");
            No(KnownSha(" " + HD_SHA_ORIGINAL), "a space before");
            No(KnownSha(""), "empty");
            No(KnownSha(null), "null");
            No(EnglishSha(HD_SHA_ORIGINAL), "French original");
            No(EnglishSha(null), "null");
        }

        // ---------------------------------------------------------------- HD available
        [Test] static void Launch_HdAvailable_Demos()
        {
            GameDir = TestDir("game");
            for (int t = 1; t <= 2; t++)
            {
                LaunchTarget = t;
                string why;
                No(HdAvailable(out why), "demo " + t);
                Eq("not available for the demos yet", why, "demo " + t + ": why");
            }
            Ok(hdCheckedDir == null, "for a demo the game folder is not looked at (nothing remembered)");
            LaunchTarget = 0;
            string w;
            No(HdAvailable(out w), "the game, folder without __FF.EXE");
            Eq("game folder not found", w, "the game: why");
        }

        [Test] static void Launch_HdAvailable_CachedPerGameDir()
        {
            string a = TestDir("a"), b = TestDir("b"), why;
            PutFile(Path.Combine(b, "TNOVA", "__FF.EXE"), Bytes("4D5A9000"));
            GameDir = Path.Combine(ExeDir, "nowhere");
            No(HdAvailable(out why), "a missing folder");
            Eq("game folder not found", why, "a missing folder: why");
            GameDir = a;
            No(HdAvailable(out why), "a: no exe");
            Eq("game folder not found", why, "a: why");
            PutFile(Path.Combine(a, "TNOVA", "__FF.EXE"), Bytes("4D5A9000"));
            No(HdAvailable(out why), "a, exe added");
            Eq("game folder not found", why, "same GameDir: the answer is kept, the exe is not read again");
            GameDir = b;
            No(HdAvailable(out why), "b: unknown exe");
            Eq("not available for this version of the game", why, "b: why");
            GameDir = a;
            No(HdAvailable(out why), "back to a");
            Eq("not available for this version of the game", why, "back to a: read again (one folder remembered)");
        }

        [Test] static void Launch_HdAvailable_UnreadablePath()
        {
            GameDir = "bad\0dir";                      // a NUL: Path.Combine throws (Windows and mono)
            string why;
            No(HdAvailable(out why), "HdAvailable");
            Eq("cannot read __FF.EXE", why, "why");
        }

        // ---------------------------------------------------------------- PrepareExeForHd: refusals
        [Test] static void Launch_PrepareExeForHd_MissingExe()
        {
            LaunchKit.Game();
            string msg;
            No(PrepareExeForHd(out msg), "no TNOVA\\__FF.EXE");
            Eq("TNOVA\\__FF.EXE not found", msg, "msg");
            No(File.Exists(Path.Combine(GameDir, "TNOVA", "__FF.EXE")), "nothing created");
            No(File.Exists(Path.Combine(GameDir, "TNOVA", "__FF.EXE.tnplus-original")), "no backup");
        }

        [Test] static void Launch_PrepareExeForHd_UnknownExe()
        {
            LaunchKit.Game();
            string exe = Path.Combine(GameDir, "TNOVA", "__FF.EXE");
            // an LE executable of a size near the game's, none of the known SHA-256: never touched
            byte[] junk = new byte[0x20000];
            for (int i = 0; i < junk.Length; i++) junk[i] = (byte)(i * 7 + 3);
            Bytes("4D5A").CopyTo(junk, 0); Bytes("4C450000").CopyTo(junk, 0x1000);
            foreach (byte[] d in new[] { junk, new byte[0] })
            {
                PutFile(exe, d);
                string msg;
                No(PrepareExeForHd(out msg), d.Length + " bytes");
                Eq("unknown __FF.EXE (GOG French, GOG English and Steam are supported)", msg, d.Length + " bytes: msg");
                EqBytes(d, File.ReadAllBytes(exe), d.Length + " bytes: __FF.EXE untouched");
                No(File.Exists(exe + ".tnplus-original"), d.Length + " bytes: no backup");
            }
        }

        // ---------------------------------------------------------------- DOSBox CPU cycles
        [Test] static void Launch_CpuSection_ImposesCycles()
        {
            string db = LaunchKit.Game();
            string r = null;
            string said = LaunchKit.Said(() => r = CpuSection(db));
            Eq(LaunchKit.CPU, r, "the [cpu] section");
            Has(said, "DOSBox CPU cycles: 1800000 (the game's own config says 115000; cpu_cycles in TNPlus.ini, 0 = leave it)", "message");
            CpuCycles = 700000;
            said = LaunchKit.Said(() => r = CpuSection(db));
            Eq("[cpu]\r\ncpu_cycles = 700000\r\ncpu_cycles_protected = auto\r\ncpu_throttle = true\r\n", r, "700000");
            Has(said, "DOSBox CPU cycles: 700000 (the game's own config says 115000;", "message 700000");
        }

        [Test] static void Launch_CpuSection_ZeroLeavesTheEditionsCycles()
        {
            string db = LaunchKit.Game();
            foreach (int c in new[] { 0, -1 })
            {
                CpuCycles = c;
                string r = null;
                string said = LaunchKit.Said(() => r = CpuSection(db));
                Eq("", r, "cpu_cycles = " + c + ": no [cpu] section");
                Eq("", said, "cpu_cycles = " + c + ": no message");
            }
        }

        [Test] static void Launch_CpuSection_SameValueNoMessage()
        {
            string db = LaunchKit.Game("[cpu]\r\ncpu_cycles = 1800000\r\n", LaunchKit.STOCK);
            string r = null;
            string said = LaunchKit.Said(() => r = CpuSection(db));
            Eq(LaunchKit.CPU, r, "still written (cpu_cycles_protected, cpu_throttle)");
            HasNot(said, "DOSBox CPU cycles", "no message when the edition says the same");
        }

        [Test] static void Launch_CpuSection_ReadsTheEditionsValue()
        {
            // no config: "?"
            string db = LaunchKit.Game(null, LaunchKit.STOCK);
            string said = LaunchKit.Said(() => CpuSection(db));
            Has(said, "(the game's own config says ?;", "no edition config");
            // the first cpu_cycles line, indented or not, spaces around = optional; comments and cpu_cycles_protected
            // are not it
            PutFile(Path.Combine(db, "dosbox_terranova_windows.conf"),
                "[cpu]\r\n# cpu_cycles = 1\r\ncpu_cycles_protected = 50000\r\n   cpu_cycles=300000\r\ncpu_cycles = 400000\r\n");
            said = LaunchKit.Said(() => CpuSection(db));
            Has(said, "(the game's own config says 300000;", "first cpu_cycles line");
            PutFile(Path.Combine(db, "dosbox_terranova_windows.conf"), "[cpu]\ncpu_cycles = max\n");
            said = LaunchKit.Said(() => CpuSection(db));
            Has(said, "(the game's own config says max;", "a word, LF line ends");
            PutFile(Path.Combine(db, "dosbox_terranova_windows.conf"), "[cpu]\r\ncycles = 3000\r\n");
            said = LaunchKit.Said(() => CpuSection(db));
            Has(said, "(the game's own config says ?;", "the old cycles setting is not cpu_cycles");
        }

        // ---------------------------------------------------------------- the launch conf: the game
        [Test] static void Launch_Conf_GameDefaults()
        {
            string db = LaunchKit.Game();
            string path = null;
            string said = LaunchKit.Said(() => path = WriteLaunchConf(db, 0));
            Eq(LaunchKit.ConfPath, path, "TNPlus_launch.conf next to TNPlus.exe");
            Eq(LaunchKit.CPU + LaunchKit.SHARP + LaunchKit.STOCK, File.ReadAllText(path),
                "defaults: cycles, SHARP scaling, then the edition's launch file as it is (no stereo fix, no key map, no mouse)");
            Has(said, "the game's own config says 115000", "cycles message");
            HasNot(said, "DOSBox key map", "no key map message");
            No(File.Exists(LaunchKit.Copy), "no key map copy");
        }

        [Test] static void Launch_Conf_StereoFixRightAfterAutoexec()
        {
            string stock = "[sdl]\r\nfullscreen = true\r\n[AutoExec]\r\n@echo off\r\nmount C \"..\"\r\n[autoexec]\r\nexit\r\n";
            string db = LaunchKit.Game(LaunchKit.EDITION, stock);
            OptStereoFix = true;
            WriteLaunchConf(db, 0);
            Eq(LaunchKit.CPU + LaunchKit.SHARP + "[sdl]\r\nfullscreen = true\r\n[AutoExec]\r\n" + LaunchKit.MIXER +
                "@echo off\r\nmount C \"..\"\r\n[autoexec]\r\nexit\r\n", File.ReadAllText(LaunchKit.ConfPath),
                "the mixer command on the line after the first [autoexec], any case, before the game starts");
            OptStereoFix = false;
            WriteLaunchConf(db, 0);
            Eq(LaunchKit.CPU + LaunchKit.SHARP + stock, File.ReadAllText(LaunchKit.ConfPath), "stereo fix off: nothing inserted (file rewritten)");
        }

        [Test] static void Launch_Conf_StereoFixLineEnds()
        {
            OptStereoFix = true;
            string db = LaunchKit.Game(LaunchKit.EDITION, "[autoexec]\n@echo off\nexit\n");
            WriteLaunchConf(db, 0);
            Eq(LaunchKit.CPU + LaunchKit.SHARP + "[autoexec]\n" + LaunchKit.MIXER + "@echo off\nexit\n", File.ReadAllText(LaunchKit.ConfPath),
                "LF line ends: after the LF");
            PutFile(Path.Combine(db, "dosbox_terranova_windows_launch.conf"), "[sdl]\r\nfullscreen = true\r\n[autoexec]");
            WriteLaunchConf(db, 0);
            Eq(LaunchKit.CPU + LaunchKit.SHARP + "[sdl]\r\nfullscreen = true\r\n[autoexec]\r\n" + LaunchKit.MIXER, File.ReadAllText(LaunchKit.ConfPath),
                "[autoexec] as the last line, no line end: one added");
            PutFile(Path.Combine(db, "dosbox_terranova_windows_launch.conf"), "[sdl]\r\nfullscreen = true\r\n");
            WriteLaunchConf(db, 0);
            Eq(LaunchKit.CPU + LaunchKit.SHARP + "[sdl]\r\nfullscreen = true\r\n", File.ReadAllText(LaunchKit.ConfPath),
                "no [autoexec]: the file as it is");
        }

        [Test] static void Launch_Conf_ScalingAndCyclesOff()
        {
            string db = LaunchKit.Game();
            OptScaling = 0; CpuCycles = 0;
            string said = LaunchKit.Said(() => WriteLaunchConf(db, 0));
            Eq(LaunchKit.STOCK, File.ReadAllText(LaunchKit.ConfPath), "GAME'S OWN scaling, cycles 0: the edition's launch file only");
            Eq("", said, "nothing said");
            OptScaling = 1;
            WriteLaunchConf(db, 0);
            Eq(LaunchKit.SHARP + LaunchKit.STOCK, File.ReadAllText(LaunchKit.ConfPath), "SHARP only");
            OptScaling = 0; CpuCycles = 1800000;
            LaunchKit.Said(() => WriteLaunchConf(db, 0));
            Eq(LaunchKit.CPU + LaunchKit.STOCK, File.ReadAllText(LaunchKit.ConfPath), "cycles only");
        }

        [Test] static void Launch_Conf_KeepDosGame()
        {
            string stock = "[autoexec]\r\n@echo off\r\nexit\r\n  EXIT  \r\nEXITING\r\necho EXIT now\r\nREM EXIT\r\ncall TN.BAT\r\nExit\r\n";
            string db = LaunchKit.Game(LaunchKit.EDITION, stock);
            OptScaling = 0; CpuCycles = 0;
            WriteLaunchConf(db, 0);
            Eq(stock, File.ReadAllText(LaunchKit.ConfPath), "keep_dos off: EXIT lines kept");
            OptKeepDos = true;
            WriteLaunchConf(db, 0);
            EqList(new[] { "[autoexec]", "@echo off", "REM EXIT", "REM EXIT", "EXITING", "echo EXIT now", "REM EXIT", "call TN.BAT", "REM EXIT" },
                LaunchKit.Lines(File.ReadAllText(LaunchKit.ConfPath)), "keep_dos: every line that is only EXIT (any case, spaces) becomes REM EXIT");
        }

        // ---------------------------------------------------------------- the launch conf: the demos
        [Test] static void Launch_Conf_Demos()
        {
            string db = LaunchKit.Game(LaunchKit.EDITION, null);   // the edition's launch file is not needed for a demo
            for (int t = 1; t <= 2; t++)
                foreach (bool fix in new[] { false, true })
                {
                    OptStereoFix = fix;
                    string path = WriteLaunchConf(db, t);
                    Eq(LaunchKit.ConfPath, path, "path");
                    Eq(LaunchKit.CPU + LaunchKit.SHARP + "# Terra Nova Plus: DEMO " + t + "\r\n[autoexec]\r\n" + (fix ? LaunchKit.MIXER : "") +
                        "mount C \"..\"\r\nC:\r\nCD \\TNDEMO" + t + "\r\nCLS\r\nCALL TNDEMO.BAT\r\nEXIT\r\n", File.ReadAllText(path),
                        "demo " + t + ", stereo fix " + fix + ": generated autoexec (C: = the game folder, DOSBox runs in _DOSBOX)");
                }
        }

        [Test] static void Launch_Conf_KeepDosDemo()
        {
            string db = LaunchKit.Game();
            OptKeepDos = true; OptScaling = 0; CpuCycles = 0;
            WriteLaunchConf(db, 2);
            EqList(new[] { "# Terra Nova Plus: DEMO 2", "[autoexec]", "mount C \"..\"", "C:", "CD \\TNDEMO2", "CLS", "CALL TNDEMO.BAT", "REM EXIT" },
                LaunchKit.Lines(File.ReadAllText(LaunchKit.ConfPath)), "keep_dos: the DOS prompt stays after the demo");
        }

        // ---------------------------------------------------------------- the launch conf: mouse and key map
        [Test] static void Launch_Conf_MiddleButtonBind()
        {
            string db = LaunchKit.Game();
            GameKey jump = FindGameKey("jump");
            for (int m = 2; m < MOUSE_INI.Length; m++)
            {
                jump.Mouse = m;
                WriteLaunchConf(db, 0);
                Eq(LaunchKit.CPU + LaunchKit.SHARP + LaunchKit.STOCK, File.ReadAllText(LaunchKit.ConfPath), "jump on " + MOUSE_INI[m] + ": no [mouse]");
            }
            jump.Mouse = 1;
            WriteLaunchConf(db, 0);
            Eq(LaunchKit.CPU + LaunchKit.SHARP + LaunchKit.MID + LaunchKit.STOCK, File.ReadAllText(LaunchKit.ConfPath),
                "a game command on the middle button: DOSBox keeps the mouse on a middle click");
            No(File.Exists(LaunchKit.Copy), "a mouse bind is not a key change: no key map copy");
            jump.Mouse = 0;
            for (int t = 0; t < ToolMouse.Length; t++)
            {
                ToolMouse[t] = 1;
                WriteLaunchConf(db, 1);
                Has(File.ReadAllText(LaunchKit.ConfPath), LaunchKit.SHARP + LaunchKit.MID + "# Terra Nova Plus: DEMO 1\r\n", "tool action " + TOOL_IDS[t] + " on the middle button (demo too)");
                ToolMouse[t] = 3;
                WriteLaunchConf(db, 1);
                HasNot(File.ReadAllText(LaunchKit.ConfPath), "[mouse]", "tool action " + TOOL_IDS[t] + " on M5");
                ToolMouse[t] = 0;
            }
        }

        [Test] static void Launch_Conf_EverythingInOrder()
        {
            string db = LaunchKit.Game();
            LaunchKit.UseMap(db, LaunchKit.Azerty(db));
            FindGameKey("fwd").Sc = 0x48;
            FindGameKey("lookup").Mouse = 1;
            OptStereoFix = true;
            string said = LaunchKit.Said(() => WriteLaunchConf(db, 0));
            Eq(LaunchKit.CPU + LaunchKit.SHARP + "[sdl]\r\nmapperfile = " + LaunchKit.Copy + "\r\n" + LaunchKit.MID +
                LaunchKit.STOCK.Replace("[autoexec]\r\n", "[autoexec]\r\n" + LaunchKit.MIXER), File.ReadAllText(LaunchKit.ConfPath),
                "[cpu], scaling, key map, [mouse], then the edition's launch file with the stereo fix");
            Ok(File.Exists(LaunchKit.Copy), "the key map copy");
            Has(said, "DOSBox CPU cycles: 1800000", "cycles message");
            Has(said, "DOSBox key map azerty.map moves", "key map message");
        }

        [Test] static void Launch_Conf_NoKeyMapWhenKeysUnchanged()
        {
            string db = LaunchKit.Game();
            LaunchKit.UseMap(db, LaunchKit.Azerty(db));
            FindGameKey("options").Sc = 0x3B;          // a fixed command (Options): not a change the KEYBOARD tab makes
            string said = LaunchKit.Said(() => WriteLaunchConf(db, 0));
            Eq(LaunchKit.CPU + LaunchKit.SHARP + LaunchKit.STOCK, File.ReadAllText(LaunchKit.ConfPath), "the map moves keys but the game keys are the game's");
            No(File.Exists(LaunchKit.Copy), "no copy");
            HasNot(said, "DOSBox key map", "no message");
        }

        [Test] static void Launch_Conf_NoKeyMapWhenTheMapMovesNoKey()
        {
            string db = LaunchKit.Game();
            LaunchKit.UseMap(db, LaunchKit.Neutral(Path.Combine(db, "pad.map")));
            FindGameKey("fwd").Sc = 0x48;
            string said = LaunchKit.Said(() => WriteLaunchConf(db, 0));
            Eq(LaunchKit.CPU + LaunchKit.SHARP + LaunchKit.STOCK, File.ReadAllText(LaunchKit.ConfPath), "keys changed, the map keeps every key in place");
            No(File.Exists(LaunchKit.Copy), "no copy");
            HasNot(said, "DOSBox key map", "no message");
        }

        // ---------------------------------------------------------------- LaunchMapper
        [Test] static void Launch_KeysChanged_GameKeysOnly()
        {
            No(KeysChanged(), "defaults");
            FindGameKey("jump").Mouse = 1; ToolMouse[0] = 2;
            No(KeysChanged(), "mouse binds are not key changes");
            ScanFreelook = 0x3B;
            No(KeysChanged(), "a tool key (freelook) is not a game key");
            GameKey opt = FindGameKey("options"), abort = FindGameKey("abort");
            opt.Sc = 0x3B; abort.Mod = 0;
            Ok(opt.Fixed && opt.Changed && abort.Fixed && abort.Changed, "fixtures: fixed commands changed");
            No(KeysChanged(), "fixed commands (Options, Abort mission) do not count");
            GameKey fwd = FindGameKey("fwd");
            fwd.Mod = 1;
            Ok(KeysChanged(), "Forward with Shift");
            fwd.Mod = 0;
            No(KeysChanged(), "back to its default");
            fwd.Sc = 0x48;
            Ok(KeysChanged(), "Forward on another key");
        }

        [Test] static void Launch_LaunchMapper_MovingMap()
        {
            string db = LaunchKit.Game();
            LaunchKit.UseMap(db, LaunchKit.Azerty(db));
            Eq("", LaunchMapper(db), "game keys unchanged: nothing");
            FindGameKey("turnl").Sc = 0x10;
            string r = null;
            string said = LaunchKit.Said(() => r = LaunchMapper(db));
            Eq("[sdl]\r\nmapperfile = " + LaunchKit.Copy + "\r\n", r, "the [sdl] section with the copy");
            Has(said, "DOSBox key map azerty.map moves Q A W Z: the game starts with a copy that leaves the keyboard to the KEYBOARD tab " +
                "(TNPlus_keys.map, gamepad bindings kept).", "message");
            Eq(0, MapperMoves(LaunchKit.Copy).Count, "the copy moves no key");
            Has(File.ReadAllText(LaunchKit.Copy), "key_q \"key 20\" \"stick_0 button 2\" \r\n", "Q back on its key, its gamepad binding kept");
        }

        [Test] static void Launch_LaunchMapper_MapNotFound()
        {
            string db = LaunchKit.Game();
            LaunchKit.UseMap(db, Path.Combine(db, "gone.map"));
            FindGameKey("fwd").Sc = 0x48;
            string r = null;
            string said = LaunchKit.Said(() => r = LaunchMapper(db));
            Eq("", r, "the edition's mapperfile does not exist: nothing");
            Eq("", said, "nothing said");
            No(File.Exists(LaunchKit.Copy), "no copy");
        }

        [Test] static void Launch_LaunchMapper_CopyFails()
        {
            string db = LaunchKit.Game();
            LaunchKit.UseMap(db, LaunchKit.Azerty(db));
            FindGameKey("fwd").Sc = 0x48;
            Directory.CreateDirectory(LaunchKit.Copy); // a folder in the way of TNPlus_keys.map
            string r = null;
            string said = LaunchKit.Said(() => r = LaunchMapper(db));
            Eq("", r, "no mapperfile when the copy cannot be written");
            Has(said, "DOSBox key map azerty.map moves keys and could not be copied (", "message");
            Has(said, "): choose the GAME layout in the KEYBOARD tab.", "advice");
        }

        // ---------------------------------------------------------------- MAPPER_KEYS
        [Test] static void Launch_MapperKeys_SdlScancodes()
        {
            Eq(105, MAPPER_KEYS.Count, "DOS keys of DOSBox Staging's map");
            Eq(105, new HashSet<int>(MAPPER_KEYS.Values).Count, "each on a host key of its own (a neutral map binds no host key twice)");
            // SDL scancodes (USB HID usages): letters 4-29, digits 30-39, F1-F12 58-69, keypad 1-9 89-97
            for (int i = 0; i < 26; i++) Eq(4 + i, MAPPER_KEYS[((char)('a' + i)).ToString()], "letter " + (char)('a' + i));
            for (int i = 1; i <= 9; i++) Eq(29 + i, MAPPER_KEYS[i.ToString()], "digit " + i);
            Eq(39, MAPPER_KEYS["0"], "digit 0");
            for (int i = 1; i <= 12; i++) Eq(57 + i, MAPPER_KEYS["f" + i], "F" + i);
            for (int i = 1; i <= 9; i++) Eq(88 + i, MAPPER_KEYS["kp_" + i], "keypad " + i);
            string[] names = { "enter", "esc", "bspace", "tab", "space", "right", "left", "down", "up", "lctrl", "lshift", "lalt", "rctrl", "rshift", "ralt", "kp_0", "kp_period", "oem102", "abnt1" };
            int[] codes = { 40, 41, 42, 43, 44, 79, 80, 81, 82, 224, 225, 226, 228, 229, 230, 98, 99, 100, 135 };
            for (int i = 0; i < names.Length; i++) Eq(codes[i], MAPPER_KEYS[names[i]], names[i]);
        }

        [Test] static void Launch_ParseMapperDefaults_Pairs()
        {
            Dictionary<string, int> d = ParseMapperDefaults("a 4 b 5");
            Eq(2, d.Count, "two pairs");
            Eq(4, d["a"], "a"); Eq(5, d["b"], "b");
            d = ParseMapperDefaults("a 4 b");
            Eq(1, d.Count, "a name without value at the end is left out");
            d = ParseMapperDefaults("a 4 a 7");
            Eq(7, d["a"], "the same name twice: the last");
            Eq(0, ParseMapperDefaults("").Count, "empty");
            Throws<FormatException>(() => ParseMapperDefaults("a x"), "a value that is not a number");
        }

        // ---------------------------------------------------------------- ConfMapper
        [Test] static void Launch_ConfMapper_SdlSectionOnly()
        {
            string spaced = Path.Combine(ExeDir, "my maps", "azerty.map");
            string conf = PutFile(Path.Combine(ExeDir, "a.conf"),
                "mapperfile = before.map\r\n" +
                "[dosbox]\r\nmapperfile = dosbox.map\r\n" +
                "  [SDL]  \r\nfullscreen = true\r\n  MapperFile=\"" + spaced + "\"  \r\n" +
                "[render]\r\nmapperfile = render.map\r\n");
            Eq(spaced, ConfMapper(conf), "the [sdl] one (any case, spaces, quotes taken off), not those before or after");
            conf = PutFile(Path.Combine(ExeDir, "b.conf"), "[dosbox]\r\nmapperfile = dosbox.map\r\n[render]\r\nmapperfile = render.map\r\n");
            Ok(ConfMapper(conf) == null, "only in other sections: null");
            conf = PutFile(Path.Combine(ExeDir, "c.conf"), "[sdl]\nmapperfile = lf.map\n");
            Eq("lf.map", ConfMapper(conf), "LF line ends, a bare name");
        }

        [Test] static void Launch_ConfMapper_LastOneWinsEmptyIsNull()
        {
            Func<string, string> of = text => ConfMapper(PutFile(Path.Combine(ExeDir, "x.conf"), text));
            Eq("second.map", of("[sdl]\r\nmapperfile = first.map\r\n[render]\r\naspect = true\r\n[sdl]\r\nmapperfile = second.map\r\n"),
                "two [sdl] sections: the last value");
            Ok(of("[sdl]\r\nmapperfile = first.map\r\nmapperfile =\r\n") == null, "an empty value last: null");
            Ok(of("[sdl]\r\nmapperfile = \"\"\r\n") == null, "empty quotes: null");
            Ok(of("[sdl]\r\n# mapperfile = x.map\r\nmapperfiles = y.map\r\nfullscreen = true\r\n") == null, "a comment, another setting: null");
            Ok(of("") == null, "an empty file");
            Ok(ConfMapper(Path.Combine(ExeDir, "missing.conf")) == null, "a missing file: null (no exception)");
        }

        // ---------------------------------------------------------------- ResolveMapper / EffectiveMapper
        [Test] static void Launch_ResolveMapper_FullPath()
        {
            string db = TestDir("dosbox");
            string abs = PutFile(Path.Combine(TestDir("maps"), "x.map"), "");
            Eq(abs, ResolveMapper(abs, db), "an existing full path");
            Eq(abs, ResolveMapper(abs, null), "an existing full path, no DOSBox folder");
            string name = LaunchKit.Unique();
            PutFile(Path.Combine(db, name), "");
            PutFile(Path.Combine(db, "resources", "mapperfiles", name), "");
            Ok(ResolveMapper(Path.Combine(ExeDir, "maps", name), db) == null, "a full path is looked for there only (not next to DOSBox)");
            Ok(ResolveMapper(null, db) == null, "no name");
        }

        [Test] static void Launch_ResolveMapper_NameNextToDosbox()
        {
            string db = TestDir("dosbox"), name = LaunchKit.Unique();
            Ok(ResolveMapper(name, db) == null, "nowhere");
            string bundled = PutFile(Path.Combine(db, "resources", "mapperfiles", name), "");
            Eq(bundled, ResolveMapper(name, db), "DOSBox's bundled maps");
            string next = PutFile(Path.Combine(db, name), "");
            Eq(next, ResolveMapper(name, db), "next to DOSBox before its bundled maps");
            Ok(ResolveMapper(name, null) == null, "no DOSBox folder: its config folder only");
        }

        [Test] static void Launch_EffectiveMapper_LastConfWins()
        {
            string db = TestDir("dosbox");
            string a = PutFile(Path.Combine(db, "a.map"), ""), b = PutFile(Path.Combine(db, "b.map"), "");
            string c1 = PutFile(Path.Combine(db, "1.conf"), "[sdl]\r\nmapperfile = " + a + "\r\n");
            string c2 = PutFile(Path.Combine(db, "2.conf"), "[sdl]\r\nmapperfile = " + b + "\r\n");
            string c3 = PutFile(Path.Combine(db, "3.conf"), "[sdl]\r\nfullscreen = true\r\n");
            string gone = PutFile(Path.Combine(db, "4.conf"), "[sdl]\r\nmapperfile = " + Path.Combine(db, "gone.map") + "\r\n");
            string missing = Path.Combine(db, "missing.conf");
            Eq(b, EffectiveMapper(new List<string> { c1, c2 }, db), "1 then 2");
            Eq(a, EffectiveMapper(new List<string> { c2, c1 }, db), "2 then 1");
            Eq(b, EffectiveMapper(new List<string> { c1, c2, c3, missing }, db), "later configs without mapperfile, or missing, change nothing");
            Ok(EffectiveMapper(new List<string> { c1, gone }, db) == null, "the last setting wins even when its map does not exist");
        }

        [Test] static void Launch_EffectiveMapper_NameResolved()
        {
            string db = TestDir("game", "_DOSBOX"), name = LaunchKit.Unique();
            string bundled = PutFile(Path.Combine(db, "resources", "mapperfiles", name), "");
            string conf = PutFile(Path.Combine(db, "x.conf"), "[sdl]\r\nmapperfile = \"" + name + "\"\r\n");
            Eq(bundled, EffectiveMapper(new List<string> { conf }, db), "a name in a config: found with DOSBox's bundled maps");
        }

        // ---------------------------------------------------------------- MapperMoves / NeutralMapper
        static string LaunchMovesMap()
        {
            return LaunchKit.Map(Path.Combine(TestDir("maps"), "moves.map"),
                "# made up",
                "hand_shutdown \"key 69 mod1\"",
                "key_esc \"key 41\"",                         // in place
                "key_q \"key 4\" \"stick_0 button 2\"",       // moved, a gamepad binding too
                "  key_a   \"key 20\"  ",                    // moved, spaces around
                "key_w \"key 26\" \"key 29\"",               // a second keyboard key added
                "key_z",                                     // no binding: removed
                "key_x \"stick_0 axis 1 1\"",                // the joystick only: removed from the keyboard
                "key_c \"key 6 mod1\"",                      // with a modifier: not the plain key
                "key_space \"key 44\" \"jbutton_0_0\"",      // in place, gamepad kept
                "key_kp_period \"key 99 \"",                 // in place (spaces in the quotes)
                "key_foo \"key 1\"",                         // not a DOS key DOSBox knows: ignored
                "KEY_E \"key 26\"",                          // upper case: not a key line
                "mod_1 \"key 224\" \"key 228\"");
        }

        [Test] static void Launch_MapperMoves_WhatCounts()
        {
            EqList(new[] { "Q", "A", "W", "Z", "X", "C" }, MapperMoves(LaunchMovesMap()),
                "moved, added, removed or with a modifier, in the file's order, upper case");
        }

        [Test] static void Launch_MapperMoves_NeutralOrMissing()
        {
            string map = LaunchKit.Neutral(Path.Combine(TestDir("maps"), "pad.map"));
            Eq(0, MapperMoves(map).Count, "the 105 keys in place, a gamepad binding on Space");
            File.AppendAllText(map, "key_slash \"key 36\"\r\n");
            EqList(new[] { "SLASH" }, MapperMoves(map), "slash moved by a later line too");
            Eq(0, MapperMoves(Path.Combine(ExeDir, "missing.map")).Count, "a missing map: nothing (no exception)");
            Eq(0, MapperMoves(PutFile(Path.Combine(ExeDir, "empty.map"), "")).Count, "an empty map");
        }

        [Test] static void Launch_NeutralMapper_TheCopy()
        {
            string path = NeutralMapper(LaunchMovesMap());
            Eq(LaunchKit.Copy, path, "TNPlus_keys.map next to TNPlus.exe");
            Eq("# made up\r\n" +
               "hand_shutdown \"key 69 mod1\"\r\n" +
               "key_esc \"key 41\" \r\n" +
               "key_q \"key 20\" \"stick_0 button 2\" \r\n" +
               "key_a \"key 4\" \r\n" +
               "key_w \"key 26\" \r\n" +
               "key_z \"key 29\" \r\n" +
               "key_x \"key 27\" \"stick_0 axis 1 1\" \r\n" +
               "key_c \"key 6\" \r\n" +
               "key_space \"key 44\" \"jbutton_0_0\" \r\n" +
               "key_kp_period \"key 99\" \r\n" +
               "key_foo \"key 1\"\r\n" +
               "KEY_E \"key 26\"\r\n" +
               "mod_1 \"key 224\" \"key 228\"\r\n", File.ReadAllText(path),
               "every DOS key on its own host key, its other bindings kept; other lines as they were");
            Eq(0, MapperMoves(path).Count, "the copy moves nothing");
        }

        [Test] static void Launch_NeutralMapper_Overwritten()
        {
            string dir = TestDir("maps");
            NeutralMapper(LaunchMovesMap());
            string path = NeutralMapper(LaunchKit.Map(Path.Combine(dir, "small.map"), "key_q \"key 4\""));
            Eq("key_q \"key 20\" \r\n", File.ReadAllText(path), "a second launch: the copy of the new map only");
            Throws<FileNotFoundException>(() => NeutralMapper(Path.Combine(dir, "missing.map")), "a missing map (LaunchMapper only copies a map it found)");
        }

        // ---------------------------------------------------------------- music: TN.CFG
        [Test] static void Launch_Music_RolandGeneralMidi()
        {
            LaunchKit.Game();
            string cfg = LaunchKit.Cfg("TNOVA");
            byte[] before = File.ReadAllBytes(cfg);
            Eq(0, OptMusic, "default music: ROLAND");
            string said = LaunchKit.Said(() => ApplyMusic("TNOVA"));
            EqList(new[] { "digi_num 7", "digi_io 544", "midi_num 12", "midi_io 816", "midi_extra 1" }, File.ReadAllLines(cfg),
                "General MIDI on the MPU-401 (816 = 0x330), midi_extra added at the end");
            EqBytes(before, File.ReadAllBytes(cfg + ".tnplus-original"), "the original kept");
            Has(said, "Music set to General MIDI (TNOVA\\TN.CFG, original kept as TN.CFG.tnplus-original).", "message");
        }

        [Test] static void Launch_Music_FmMidiNumOnly()
        {
            LaunchKit.Game();
            string cfg = LaunchKit.Cfg("TNOVA");
            PutFile(cfg, "digi_num 7\r\nmidi_num 12\r\nmidi_io 816\r\nmidi_extra 1\r\n");
            OptMusic = 1;
            string said = LaunchKit.Said(() => ApplyMusic("TNOVA"));
            EqList(new[] { "digi_num 7", "midi_num 3", "midi_io 816", "midi_extra 1" }, File.ReadAllLines(cfg), "FM: midi_num 3, the rest left");
            Has(said, "Music set to FM (TNOVA\\TN.CFG, original kept as TN.CFG.tnplus-original).", "message");
        }

        [Test] static void Launch_Music_GameOwnLeavesTnCfg()
        {
            LaunchKit.Game();
            string cfg = LaunchKit.Cfg("TNOVA");
            byte[] before = File.ReadAllBytes(cfg);
            OptMusic = 2;
            string said = LaunchKit.Said(() => ApplyMusic("TNOVA"));
            EqBytes(before, File.ReadAllBytes(cfg), "TN.CFG untouched");
            No(File.Exists(cfg + ".tnplus-original"), "no backup");
            Eq("", said, "nothing said");
        }

        [Test] static void Launch_Music_Awe32IsGeneralMidi()
        {
            string[] gm = { "digi_num 7", "digi_io 544", "midi_num 12", "midi_io 816", "midi_extra 1" };
            LaunchKit.Game();
            OptMusic = 3;
            string said = LaunchKit.Said(() => ApplyMusic("TNOVA"));
            EqList(gm, File.ReadAllLines(LaunchKit.Cfg("TNOVA")), "AWE32 without awe32.raw: Roland");
            Has(said, "Music set to General MIDI (TNOVA", "message without the ROM");
            LaunchKit.Sized(Path.Combine(ExeDir, "awe32.raw"), AweBank.ROM_SIZE);
            PutFile(LaunchKit.Cfg("TNDEMO1"), LaunchKit.TNCFG);
            said = LaunchKit.Said(() => ApplyMusic("TNDEMO1"));
            EqList(gm, File.ReadAllLines(LaunchKit.Cfg("TNDEMO1")), "AWE32 with the ROM: General MIDI too (FluidSynth plays it)");
            Has(said, "Music set to General MIDI (TNDEMO1\\TN.CFG", "message with the ROM");
        }

        [Test] static void Launch_Music_AlreadyRightNotWritten()
        {
            LaunchKit.Game();
            string cfg = LaunchKit.Cfg("TNOVA");
            // LF line ends and spaces around the lines: a rewrite would change the bytes on any system
            PutFile(cfg, "digi_num 7\n  midi_num 12\nmidi_io 816  \n\tmidi_extra 1\n");
            byte[] before = File.ReadAllBytes(cfg);
            string said = LaunchKit.Said(() => ApplyMusic("TNOVA"));
            EqBytes(before, File.ReadAllBytes(cfg), "already General MIDI: not written");
            No(File.Exists(cfg + ".tnplus-original"), "no backup");
            Eq("", said, "nothing said");
            PutFile(cfg, "midi_num 3\r\n");
            before = File.ReadAllBytes(cfg);
            OptMusic = 1;
            LaunchKit.Said(() => ApplyMusic("TNOVA"));
            EqBytes(before, File.ReadAllBytes(cfg), "already FM: not written");
            No(File.Exists(cfg + ".tnplus-original"), "FM: no backup");
        }

        [Test] static void Launch_Music_BackupKeptOnce()
        {
            LaunchKit.Game();
            string cfg = LaunchKit.Cfg("TNOVA");
            byte[] orig = File.ReadAllBytes(cfg);
            ApplyMusic("TNOVA");
            OptMusic = 1;
            ApplyMusic("TNOVA");
            EqList(new[] { "digi_num 7", "digi_io 544", "midi_num 3", "midi_io 816", "midi_extra 1" }, File.ReadAllLines(cfg), "Roland then FM");
            OptMusic = 0;
            ApplyMusic("TNOVA");
            EqList(new[] { "digi_num 7", "digi_io 544", "midi_num 12", "midi_io 816", "midi_extra 1" }, File.ReadAllLines(cfg), "and Roland again");
            EqBytes(orig, File.ReadAllBytes(cfg + ".tnplus-original"), "the backup is the very first TN.CFG");
        }

        [Test] static void Launch_Music_LinesAddedOrReplaced()
        {
            LaunchKit.Game();
            string cfg = LaunchKit.Cfg("TNOVA");
            PutFile(cfg, "digi_num 7\r\nmidi_numx 5\r\n   midi_io    330  \r\n");
            ApplyMusic("TNOVA");
            EqList(new[] { "digi_num 7", "midi_numx 5", "midi_io 816", "midi_num 12", "midi_extra 1" }, File.ReadAllLines(cfg),
                "a line is the name and a space (midi_numx is not midi_num); found: replaced whole; missing: added in order");
            PutFile(cfg, "");
            ApplyMusic("TNOVA");
            EqList(new[] { "midi_num 12", "midi_io 816", "midi_extra 1" }, File.ReadAllLines(cfg), "an empty TN.CFG");
        }

        [Test] static void Launch_Music_NoTnCfg()
        {
            LaunchKit.Game();
            File.Delete(LaunchKit.Cfg("TNOVA"));
            string said = LaunchKit.Said(delegate { ApplyMusic("TNOVA"); ApplyMusic("TNDEMO1"); });
            No(File.Exists(LaunchKit.Cfg("TNOVA")), "no TN.CFG made");
            No(Directory.Exists(Path.Combine(GameDir, "TNDEMO1")), "no demo folder made");
            Eq("", said, "nothing said");
        }

        [Test] static void Launch_Music_TheTargetsTnCfgOnly()
        {
            LaunchKit.Game();
            byte[] game = File.ReadAllBytes(LaunchKit.Cfg("TNOVA"));
            PutFile(LaunchKit.Cfg("TNDEMO2"), "midi_num 3\r\n");
            string said = LaunchKit.Said(() => ApplyMusic("TNDEMO2"));
            EqList(new[] { "midi_num 12", "midi_io 816", "midi_extra 1" }, File.ReadAllLines(LaunchKit.Cfg("TNDEMO2")), "demo 2's TN.CFG");
            Ok(File.Exists(LaunchKit.Cfg("TNDEMO2") + ".tnplus-original"), "demo 2's backup");
            EqBytes(game, File.ReadAllBytes(LaunchKit.Cfg("TNOVA")), "the game's TN.CFG untouched");
            No(File.Exists(LaunchKit.Cfg("TNOVA") + ".tnplus-original"), "no backup for the game");
            Has(said, "(TNDEMO2\\TN.CFG,", "message names the demo");
        }

        // ---------------------------------------------------------------- AWE32
        [Test] static void Launch_AweRom_ExactSizeInExeDir()
        {
            Eq(1048576, AweBank.ROM_SIZE, "ROM size: 1 MB");
            Ok(AweRom() == null, "no awe32.raw");
            string rom = Path.Combine(ExeDir, "awe32.raw");
            foreach (long n in new long[] { 0, 1048575, 1048577, 2097152 })
            {
                LaunchKit.Sized(rom, n);
                Ok(AweRom() == null, n + " bytes: not the ROM");
            }
            LaunchKit.Sized(rom, 1048576);
            Eq(rom, AweRom(), "exactly 1048576 bytes");
            File.Delete(rom);
            Directory.CreateDirectory(rom);
            Ok(AweRom() == null, "a folder named awe32.raw");
            Directory.Delete(rom);
            LaunchKit.Sized(Path.Combine(TestDir("game"), "awe32.raw"), 1048576);
            LaunchKit.Sized(Path.Combine(ExeDir, "AWE32.ROM"), 1048576);
            Ok(AweRom() == null, "elsewhere or by another name: not looked for");
        }

        [Test] static void Launch_EffectiveMusic_Awe32NeedsTheRom()
        {
            for (int m = 0; m < 4; m++) { OptMusic = m; Eq(m == 3 ? 0 : m, EffectiveMusic(), MUSIC_NAMES[m] + " without the ROM"); }
            LaunchKit.Sized(Path.Combine(ExeDir, "awe32.raw"), 1048576);
            for (int m = 0; m < 4; m++) { OptMusic = m; Eq(m, EffectiveMusic(), MUSIC_NAMES[m] + " with the ROM"); }
            Eq(3, OptMusic, "the choice itself stays AWE32");
        }

        [Test] static void Launch_PrepareAwe_NoGameBank()
        {
            LaunchKit.Game();
            LaunchKit.Sized(Path.Combine(ExeDir, "awe32.raw"), 1048576);
            string r = "unset";
            string said = LaunchKit.Said(() => r = PrepareAwe("TNDEMO1"));
            Ok(r == null, "no FF.SBK in TNDEMO1\\SOUND nor TNOVA\\SOUND: no music conf");
            Has(said, "AWE32 music: FF.SBK not found, the Windows MIDI synthesizer is used.", "message");
            No(File.Exists(Path.Combine(ExeDir, "TNPlus_music.conf")), "no TNPlus_music.conf");
            No(File.Exists(Path.Combine(ExeDir, "TNPlus_awe32.sf2")), "no soundfont");
        }

        [Test] static void Launch_PrepareAwe_GameBankForTheDemos()
        {
            // a demo without its own bank uses the game's: past "FF.SBK not found" (it then stops on the missing ROM,
            // or on Windows' gm.dls, whichever comes first: no music conf either way)
            LaunchKit.Game();
            PutFile(Path.Combine(GameDir, "TNOVA", "SOUND", "FF.SBK"), Bytes("52494646040000007366626B"));
            string r = "unset";
            string said = LaunchKit.Said(() => r = PrepareAwe("TNDEMO2"));
            Ok(r == null, "no music conf");
            HasNot(said, "FF.SBK not found", "the game's TNOVA\\SOUND\\FF.SBK found for demo 2");
            Has(said, "the Windows MIDI synthesizer is used", "falls back to the Windows synthesizer");
            No(File.Exists(Path.Combine(ExeDir, "TNPlus_music.conf")), "no TNPlus_music.conf");
        }

        // ---------------------------------------------------------------- Launch: what stops it before DOSBox
        [Test] static void Launch_Launch_NoGameFolder()
        {
            string tnova = TestDir("game", "TNOVA");
            PutFile(Path.Combine(tnova, "TN.CFG"), LaunchKit.TNCFG);
            foreach (string dir in new[] { "", Path.Combine(ExeDir, "game"), Path.Combine(ExeDir, "nowhere") })
            {
                GameDir = dir;
                bool r = true;
                string said = LaunchKit.Said(() => r = Launch());
                No(r, "game_dir \"" + dir + "\": not launched");
                Has(said, "Game folder not found. Set game_dir in TNPlus.ini, or start the game yourself and choose A.", "message");
            }
            Eq(LaunchKit.TNCFG, File.ReadAllText(Path.Combine(tnova, "TN.CFG")), "TN.CFG untouched (music not applied)");
            No(File.Exists(LaunchKit.ConfPath), "no launch conf written");
        }

        [Test] static void Launch_Launch_DemoNotFound()
        {
            LaunchKit.Game();
            PutFile(Path.Combine(GameDir, "TNDEMO2", "TN.CFG"), LaunchKit.TNCFG);   // demo 2 without TNDEMO.BAT
            for (int t = 1; t <= 2; t++)
            {
                LaunchTarget = t;
                bool r = false, running = LaunchKit.DosboxRunning();
                string said = LaunchKit.Said(() => r = Launch());
                if (running)                            // a DOSBox of this machine: the tool attaches to it (nothing started)
                {
                    Ok(r, "DOSBox running: attach");
                    Has(said, "DOSBox is already running", "attach message");
                    continue;
                }
                No(r, "demo " + t + " missing: not launched");
                Has(said, "DEMO " + t + " not found in " + Path.Combine(GameDir, "TNDEMO" + t) + ": see README (demos).", "message");
            }
            Eq(LaunchKit.TNCFG, File.ReadAllText(LaunchKit.Cfg("TNOVA")), "the game's TN.CFG untouched");
            Eq(LaunchKit.TNCFG, File.ReadAllText(LaunchKit.Cfg("TNDEMO2")), "demo 2's TN.CFG untouched");
            No(File.Exists(LaunchKit.ConfPath), "no launch conf written");
        }

        // ---------------------------------------------------------------- fixed bugs
        [Test] static void Launch_Conf_DemoKeepsThePlayersMap()
        {
            // the KEYBOARD tab works in the full game only (prepared exe): a demo keeps the player's own DOSBox key map
            string db = LaunchKit.Game();
            for (int n = 1; n <= 2; n++)
            {
                PutFile(Path.Combine(GameDir, TARGET_DIRS[n], "TNDEMO.BAT"), "@echo off\r\n");
                PutFile(Path.Combine(GameDir, TARGET_DIRS[n], "TN.CFG"), LaunchKit.TNCFG);
            }
            LaunchKit.UseMap(db, LaunchKit.Azerty(db));
            FindGameKey("fwd").Sc = 0x48;
            for (int t = 1; t <= 2; t++)
            {
                int target = t;
                string said = LaunchKit.Said(() => WriteLaunchConf(db, target));
                HasNot(File.ReadAllText(LaunchKit.ConfPath), "mapperfile", TARGET_NAMES[t] + ": no key map in the launch conf");
                No(File.Exists(LaunchKit.Copy), TARGET_NAMES[t] + ": no TNPlus_keys.map");
                HasNot(said, "DOSBox key map", TARGET_NAMES[t] + ": nothing said about the key map");
            }
            LaunchKit.Said(() => WriteLaunchConf(db, 0));
            Has(File.ReadAllText(LaunchKit.ConfPath), "[sdl]\r\nmapperfile = " + LaunchKit.Copy + "\r\n", "the full game: the neutral copy");
        }
    }
}
