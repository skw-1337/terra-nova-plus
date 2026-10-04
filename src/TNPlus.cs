// ============================================================================
//  Terra Nova Plus - quality-of-life pack for Terra Nova: Strike Force Centauri
//  (DOS, 1996) running in DOSBox: Steam, GOG or standalone.
//
//  One small external tool, options chosen in a menu before launching the game:
//    - mouse freelook (Y)       : mouse turns / looks up-down, reticle centred
//    - noclip (U)               : free flight (move keys, Space / Left Ctrl, Shift x4)
//    - view distance (J)        : NORMAL / FAR / MAX (fog pushed back)
//    - force 320x400            : the engine's best resolution in every mission
//    - widescreen 16:9          : DOSBox stretches the picture, the tool widens the
//                                 engine's field of view to match (no distortion)
//    - HD 640x400 (F12)         : the 3D view is rendered with twice the columns (596x199)
//                                 on a VESA 640x400 screen; F12 toggles the smoothing
//
//  Every address is found through CODE SIGNATURES (no hard-coded addresses): English /
//  French executables, Steam, GOG and CD versions, any DOSBox memory size. Exception: the
//  HD mode (HdPayload.cs, generated from the reverse-engineering scripts) only supports the
//  GOG French executable, and it is the only option that touches a game file: one header
//  field of TNOVA\__FF.EXE (16 KB more memory for its code), original kept as
//  __FF.EXE.tnplus-original.
//
//  Build (no install needed, uses the C# compiler shipped with Windows):
//    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize
//        /win32manifest:app.manifest /win32icon:icon.ico /out:TNPlus.exe TNPlus.cs HdPayload.cs   (or run build.bat)
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using System.Reflection;

[assembly: AssemblyTitle("Terra Nova Plus")]
[assembly: AssemblyDescription("Quality-of-life pack for Terra Nova: Strike Force Centauri (mouse freelook, noclip, view distance, widescreen)")]
[assembly: AssemblyCompany("skw-1337")]
[assembly: AssemblyProduct("Terra Nova Plus")]
[assembly: AssemblyCopyright("Copyright (c) 2026 skw-1337 - MIT License - github.com/skw-1337/terra-nova-plus")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]
[assembly: ComVisible(false)]

static class TNPlus
{
    const string VERSION = "1.1.0-beta1";
    const string TITLE = "Terra Nova Plus";

    // ------------------------------------------------------------------ options (TNPlus.ini)
    static bool OptFreelook = true, OptNoclip = true, OptForce400 = true, OptWide = false, OptHD = false;
    static int OptDistance = 2;                     // 0 NORMAL, 1 FAR, 2 MAX
    static bool OptStereoFix = false;                // DOSBox: swap the Sound Blaster stereo (the game's SB16 driver reverses it)
    static int OptMusic = 0;                        // 0 Roland (General MIDI), 1 FM, 2 the game's own setting
    static readonly string[] MUSIC_NAMES = { "ROLAND", "FM", "GAME'S OWN" };
    static readonly string[] MUSIC_INFO = { "Roland / General MIDI music (played by the Windows MIDI synthesizer)",
        "Sound Blaster FM synthesis, the default of the digital editions", "the game's TN.CFG is left as it is" };
    static int LaunchTarget = 0;                    // 0 game, 1 demo 1, 2 demo 2
    static readonly string[] TARGET_NAMES = { "GAME", "DEMO 1", "DEMO 2" };
    static readonly string[] TARGET_DIRS = { "TNOVA", "TNDEMO1", "TNDEMO2" };
    static int OptDetail = 2;                       // terrain detail: 0 GAME, 1 SHARP, 2 SHARPER
    static int SensX = 12, SensY = 8;               // heading / pitch units per mouse count
    static bool InvertY = false, Sound = true, HdSmoothing = true, HdHudFilter = true;
    static int ScanFreelook = 0x15, ScanNoclip = 0x16, ScanDistance = 0x24;   // Y U J (physical keys; I is the game's infrared)
    static int ScanSmoothing = 0x58;                // F12: HD smoothing on / off
    static int ScanStereo = 0x41;                   // F7: swap the stereo of the sound effects in game
    static int ScanHudFilter = 0x57;                // F11: HD sharp HUD (Scale2x) on / off
    static double NoclipSpeed = 15.0;               // game units per second (a walking PBA ~2.5)
    static string GameDir = "";

    static readonly string[] DIST_NAMES = { "NORMAL", "FAR", "MAX" };
    static readonly string[] DETAIL_NAMES = { "GAME", "SHARP", "SHARPER" };
    // terrain rings (full mesh, 1 point in 2, in 4, in 8) up to these distances; the game's MEDIUM is 30/64/100/240.
    // ULTRA (60/120/240/480) froze the game after a few minutes: the far ring stays close to the original.
    static readonly int[][] DETAIL_RINGS = { null, new[] { 48, 96, 140, 240 }, new[] { 64, 128, 180, 280 } };
    static readonly int[] DIST_R = { 0, 180, 600 }; // fog radius (0 = the game's own weather value)

    // ------------------------------------------------------------------ game constants
    const int PITCH_MIN = -7187, PITCH_MAX = 6127;  // the game's own head pitch limits
    const double CENTRE_Y = 0.36;                   // centre of the 3D view = 36% of the cursor frame
    const int NUDGE = 32;                           // activation test heading offset (camera ignores < 16)
    const double BLIND = 1.5;                       // turned but camera frozen this long -> not in 3D view
    const int PHYS_SCALE = 6 * 65536;               // physics state: x6, 16.16 fixed point
    const double MAP_MIN = 8, MAP_MAX = 504;        // noclip limits (the terrain is 512 wide)
    const double WIDE = 0.75;                       // (4/3) / (16/9)
    static readonly int[] RATIO_STOCK = { 39321, 78643 };   // 0.6 (320x400) and 1.2 (320x200), 16.16

    // ------------------------------------------------------------------ signatures
    // "(....)" = captured absolute address, "...." = wildcard. Literal bytes are hex.
    static readonly Dictionary<string, string> SIGS = new Dictionary<string, string> {
        // camera update from the player: player id, object table, head yaw, camera heading, head pitch, camera pitch
        { "camera",   "8b15 (....) c1fa10 8d04d500000000 01d0 c1e002 01d0 05 (....) 668b15 (....) 668b401b 01d0 66a3 (....) 66a1 (....) 66a3 (....)" },
        // checkHazingOffset: fog table copy, fog table, pushed text "Memory trash: hazeRadius..."
        { "haze",     "5231d2 8a82 (....) 3a82 (....) 740f 68 (....) 6a01 e8" },
        // mouse library: "push cursor to driver" flag, cursor (x,y)
        { "mouse",    "84d2 0f85 .... 803d (....) 00 7423 8b15 (....) a1 (....) c1fa10" },
        // mouse library: where the cursor is drawn
        { "draw",     "8b15 (....) ff15 .... 30c9 880d ...." },
        // mouse library: cursor frame (width, height)
        { "frame",    "8b0424 8b15 (....) a3 (....) a1 ...." },
        // mouse event handler: "cursor frozen" flag (motion events ignored, reticle not redrawn)
        { "freeze",   "f6400401 0f84 .... 803d (....) 00 0f85 .... c605 .... 01" },
        // options 320x200 / 320x400 radio button callback: state byte (bit0 pending, bit1 400 lines), mode
        { "res",      "5352 8a15 (....) 8b1d (....) f6c201 7419 80e2fe" },
        // fog radius imposed by the weather
        { "weather",  "5352 31db 8b15 (....) 89d8 e8" },
        // 3D library: horizontal view scale = canvas height x pixel ratio / canvas width, every frame
        { "aspect",   "a1 (....) f72d (....) f73d (....) 3d00000100" },
        { "scale",    "c705 (....) 00000100 c705 (....) 00000100 c705 (....) 00000100 8b1d" },
        { "ratioref", "8b1d (....) 8b03 a3 (....) 8b1d (....) 0fb74308" },
        // terrain engine camera (its own copy of 1/scale and 99/scale, computed once)
        { "terrain",  "b8 (....) 83c222 31db 8915 (....)" },
        // sound engine: "Stereo Sound: Reversed" byte of the options screen (pan = 127 - pan when set)
        { "stereo",   "803d (....) 00 740d b87f000000 8b55fc 29d0" },
    };
    static readonly byte[] HAZE_TEXT = Encoding.ASCII.GetBytes("Memory trash: hazeRadius");

    // HD mode: GOG French __FF.EXE, original and with object 3 enlarged by 16 KB (room for the HD code)
    const string HD_SHA_ORIGINAL = "5fec09ddcb5803f587b048e9d62ae1f948e69d7137612459a9660ac31e0f82b3";
    const string EN_SHA = "b762c54509f1716282c4d357a72013b8d84ba25f17b0483b733678ff81e63dd1";   // English v1.09, GOG and Steam
    const string HD_SHA_READY = "4e2aa4851bf8cd4832e19660bfcd333fcc9735e94e8b4b6c24b73e0223ccd97a";
    const uint OBJ3_SIZE = 0x10F9B0, OBJ3_HD = 0x10F9B0 + 0x4000;
    static int hdState = 0;                         // 0 waiting, 1 active, -1 unavailable (reason said)
    static bool hdExeReady = false;

    // ------------------------------------------------------------------ Win32
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MBI info, IntPtr len);
    [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(CtrlHandler h, bool add);
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [StructLayout(LayoutKind.Sequential)]
    struct MBI { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public UIntPtr RegionSize; public uint State, Protect, Type; }
    delegate bool CtrlHandler(int ev);

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] static extern bool ClipCursor(ref RECT r);
    [DllImport("user32.dll", EntryPoint = "ClipCursor")] static extern bool ClipCursorOff(IntPtr none);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint n, uint size);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRaw, uint cmd, byte[] data, ref uint size, uint headerSize);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    // ------------------------------------------------------------------ game state
    static IntPtr hProc = IntPtr.Zero;
    static int gamePid = 0;
    static long guestBase, regStart, regSize;       // host address of guest 0, guest RAM region
    static uint aHazeText, aPlayerId, aMaster, aCamHeading, aPitch, aWarp, aCursor, aDraw, aFrame, aFreeze;
    static uint aFogTable, aWeather, aResFlags, aResMode, aResCallback, aResButton;
    static uint aRatio, aRatioRef, aScaleX, aTerrain, aG3Width;
    static bool haveFog, have400, haveWide, haveDetail, haveStereo;
    static uint aStereo;
    static bool launchedWide = false;               // DOSBox started by us with the 16:9 overlay
    static bool launchedDemo = false;               // DOSBox started by us on one of the 1996 demos
    static Process launched = null;                 // the DOSBox process started from the menu
    static bool frozen = false, clipped = false;
    static CtrlHandler onClose;

    static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    static string IniPath { get { return Path.Combine(ExeDir, "TNPlus.ini"); } }

    // ================================================================== entry point
    static void Main(string[] args)
    {
        Console.Title = TITLE + " " + VERSION;
        LoadSettings();
        onClose = ev => { Release(); return false; };
        SetConsoleCtrlHandler(onClose, true);
        while (Menu()) Run();                        // back to the menu when a game started from it closes
    }

    // ------------------------------------------------------------------ pre-launch menu
    // Display is ONE choice (the original resolution, 320x400 or HD 640x400): HD already includes 320x400, so
    // the two can never be combined by mistake. When HD cannot be used (demo, other edition of the game) the
    // menu says why and 320x400 is used instead. Presets set everything at once; any change shows CUSTOM.
    static readonly string[] DISPLAY_NAMES = { "ORIGINAL", "320X400", "HD" };
    static readonly string[] DISPLAY_LABELS = { "Original", "Sharp 320x400", "HD 640x400" };
    static readonly string[] PRESET_NAMES = { "ORIGINAL", "CLASSIC+", "BEST" };
    static readonly string[] PRESET_INFO = {
        "the game as it was",
        "sharper 320x400, freelook, a bit more view and detail",
        "everything on: HD 640x400, 16:9, max view and detail" };
    static int Display = 2;                         // 0 original, 1 320x400, 2 HD 640x400
    static string hdCheckedDir = null, hdWhy = "";
    static bool hdOk = false;

    // HD needs the full game's French GOG executable (the demos and other editions are not supported yet)
    static bool HdAvailable(out string why)
    {
        if (LaunchTarget > 0) { why = "not available for the demos yet"; return false; }
        if (GameDir != hdCheckedDir)
        {
            hdCheckedDir = GameDir;
            hdOk = false; hdWhy = "game folder not found";
            try
            {
                string exe = Path.Combine(GameDir, "TNOVA", "__FF.EXE");
                if (File.Exists(exe))
                {
                    string sha = Sha256(File.ReadAllBytes(exe));
                    hdOk = sha == HD_SHA_ORIGINAL || sha == HD_SHA_READY;
                    hdWhy = hdOk ? "" : "only for the GOG version for now";
                }
            }
            catch { hdWhy = "cannot read __FF.EXE"; }
        }
        why = hdWhy;
        return hdOk;
    }

    static int EffectiveDisplay()
    {
        string why;
        return Display == 2 && !HdAvailable(out why) ? 1 : Display;
    }

    static void ApplyDisplay()
    {
        int d = EffectiveDisplay();
        OptHD = d == 2;
        OptForce400 = d >= 1;
    }

    static void ApplyPreset(int p)
    {
        Display = p;                                // 0 original, 1 320x400, 2 HD (falls back to 320x400 by itself)
        OptWide = p == 2;
        OptDetail = p;                              // GAME / SHARP / SHARPER
        OptDistance = p;                            // NORMAL / FAR / MAX
        OptFreelook = p > 0;
        OptNoclip = p > 0;
    }

    static int CurrentPreset()
    {
        for (int p = 0; p < 3; p++)
            if (Display == p && OptWide == (p == 2) && OptDetail == p && OptDistance == p
                && OptFreelook == (p > 0) && OptNoclip == (p > 0))
                return p;
        return -1;
    }

    static bool Menu()
    {
        List<string> installs = FindInstalls();
        if (GameDir == "" || !IsInstall(GameDir)) GameDir = installs.Count > 0 ? installs[0] : "";
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        while (true)
        {
            string why;
            bool hdOkNow = HdAvailable(out why);
            int preset = CurrentPreset();
            bool demoMissing = LaunchTarget > 0 && !HasDemo(GameDir, LaunchTarget);
            Console.ResetColor();
            Console.Clear();
            // header
            Console.WriteLine();
            C("  TERRA NOVA PLUS  ", ConsoleColor.Black, ConsoleColor.DarkCyan);
            C(" " + VERSION, ConsoleColor.Cyan);
            C("    Strike Force Centauri quality-of-life pack\n", ConsoleColor.Gray);
            C("  made by ", ConsoleColor.DarkGray); C("skw-1337", ConsoleColor.White);
            C("  ·  github.com/skw-1337/terra-nova-plus\n", ConsoleColor.DarkGray);
            Rule('═');
            // game, launch, preset
            C("  GAME     ", ConsoleColor.DarkGray);
            if (GameDir == "") C("not found: start the game yourself and press A\n", ConsoleColor.Yellow);
            else
            {
                string kind = InstallKind(GameDir), lang = ExeLanguage(GameDir);
                ConsoleColor bg = kind == "STEAM" ? ConsoleColor.DarkBlue : kind == "GOG" ? ConsoleColor.DarkMagenta : ConsoleColor.DarkGray;
                C(" " + kind + " ", ConsoleColor.White, bg);
                if (lang != "") C(" " + lang, ConsoleColor.Gray);
                C("  " + Shorten(GameDir, 44), ConsoleColor.Gray);
                if (installs.Count > 1) { C("   G ", ConsoleColor.Cyan); C("switch (" + installs.Count + ")", ConsoleColor.DarkGray); }
                Console.WriteLine();
            }
            C("  LAUNCH  ", ConsoleColor.DarkGray); Key("L"); Choices(TARGET_NAMES, LaunchTarget);
            if (demoMissing) C("  not found, see README", ConsoleColor.Yellow);
            Console.WriteLine();
            if (LaunchTarget > 0) Note("demos: HD 640x400 does not work with them yet, 320x400 is used");
            C("  PRESET  ", ConsoleColor.DarkGray); Key("P"); Choices(PRESET_NAMES, preset);
            if (preset < 0) C("  CUSTOM", ConsoleColor.Yellow);
            Console.WriteLine();
            Note(preset < 0 ? "your own settings (P picks a preset again)" : PRESET_INFO[preset]);
            Rule('─');
            // picture
            Section("PICTURE");
            Row("1", "Display"); Choices(DISPLAY_LABELS, EffectiveDisplay()); Console.WriteLine();
            if (Display == 2 && !hdOkNow) Warn("HD 640x400 " + why + ": 320x400 is used");
            else if (Display == 2) InGame(ScanSmoothing, "3D smoothing", ScanHudFilter, "sharp HUD");
            Row("2", "Widescreen 16:9"); Toggle(OptWide);
            Row("3", "Terrain detail far away"); Choices(DETAIL_NAMES, OptDetail); Console.WriteLine();
            Row("4", "View distance at start", ScanDistance); Choices(DIST_NAMES, OptDistance); Console.WriteLine();
            // controls
            Section("CONTROLS");
            Row("5", "Mouse freelook", ScanFreelook); Toggle(OptFreelook);
            Row("6", "Noclip", ScanNoclip); Toggle(OptNoclip);
            // sound
            Section("SOUND");
            Row("7", "Fix reversed stereo"); Toggle(OptStereoFix);
            InGame(ScanStereo, "swaps the sound effects left/right (to compare)", 0, null);
            Row("8", "Music"); Choices(MUSIC_NAMES, OptMusic); Console.WriteLine();
            Note(MUSIC_INFO[OptMusic]);
            Note("stereo fix and music are not part of the presets");
            C("        ", ConsoleColor.Gray); Bind("KEY"); C(" = key to press in game\n", ConsoleColor.DarkGray);
            Rule('─');
            C("  ENTER ", ConsoleColor.Black, ConsoleColor.Green); C(" launch      ", ConsoleColor.Gray);
            Key("A"); C("attach to a running game      ", ConsoleColor.Gray);
            Key("Q"); C("quit\n", ConsoleColor.Gray);
            Rule('═');
            C("  Anti-cheat: this tool edits DOSBox's memory like a game trainer. It never touches other\n", ConsoleColor.DarkGray);
            C("  programs, but QUIT IT (Q) before playing online games protected by an anti-cheat.\n", ConsoleColor.DarkGray);
            Console.ResetColor();
            ConsoleKeyInfo k = Console.ReadKey(true);
            switch (char.ToUpperInvariant(k.KeyChar))
            {
                case 'P': ApplyPreset(preset < 0 ? 2 : (preset + 1) % 3); break;
                case 'L': LaunchTarget = (LaunchTarget + 1) % 3; break;
                case '1': Display = (Display + 1) % 3; break;
                case '2': OptWide = !OptWide; break;
                case '3': OptDetail = (OptDetail + 1) % 3; break;
                case '4': OptDistance = (OptDistance + 1) % 3; break;
                case '5': OptFreelook = !OptFreelook; break;
                case '6': OptNoclip = !OptNoclip; break;
                case '7': OptStereoFix = !OptStereoFix; break;
                case '8': OptMusic = (OptMusic + 1) % 3; break;
                case 'G':
                    if (installs.Count > 0) GameDir = installs[(installs.IndexOf(GameDir) + 1) % installs.Count];
                    break;
                case 'A': ApplyDisplay(); SaveSettings(); Console.Clear(); return true;
                case 'Q': SaveSettings(); return false;
                default:
                    if (k.Key == ConsoleKey.Enter)
                    {
                        if (demoMissing) break;
                        ApplyDisplay();
                        SaveSettings();
                        Console.Clear();
                        if (!Launch()) { Console.WriteLine("Press a key..."); Console.ReadKey(true); continue; }
                        return true;
                    }
                    break;
            }
            SaveSettings();
        }
    }

    // ---- menu drawing helpers
    static void C(string s, ConsoleColor fg) { Console.ForegroundColor = fg; Console.Write(s); Console.ResetColor(); }
    static void C(string s, ConsoleColor fg, ConsoleColor bg)
    {
        Console.ForegroundColor = fg; Console.BackgroundColor = bg; Console.Write(s); Console.ResetColor();
    }
    static void Rule(char c) { C("  " + new string(c, 76) + "\n", ConsoleColor.DarkCyan); }
    static void Section(string name) { C("  " + name + "\n", ConsoleColor.DarkCyan); }
    static void Key(string k) { C(" " + k + " ", ConsoleColor.Black, ConsoleColor.Cyan); Console.Write(" "); }
    static void Row(string k, string label) { Console.Write("   "); Key(k); C(label.PadRight(34), ConsoleColor.Gray); }
    static void Row(string k, string label, int scan)
    {
        Console.Write("   "); Key(k); C(label + " ", ConsoleColor.Gray);
        string b = KeyName(scan);
        Bind(b);
        Console.Write(new string(' ', Math.Max(1, 34 - label.Length - 1 - (b.Length + 2))));
    }
    // in-game key: magenta, so it is not confused with the menu keys (cyan)
    static void Bind(string key) { C(" " + key + " ", ConsoleColor.White, ConsoleColor.DarkMagenta); }
    static void InGame(int scan1, string what1, int scan2, string what2)
    {
        C("        in game: ", ConsoleColor.DarkGray); Bind(KeyName(scan1)); C(" " + what1, ConsoleColor.DarkGray);
        if (what2 != null) { C(",  ", ConsoleColor.DarkGray); Bind(KeyName(scan2)); C(" " + what2, ConsoleColor.DarkGray); }
        Console.WriteLine();
    }
    static void Note(string s) { C("        " + s + "\n", ConsoleColor.DarkGray); }
    static void Warn(string s) { C("        " + s + "\n", ConsoleColor.Yellow); }
    static void Toggle(bool on)
    {
        if (on) C(" ON ", ConsoleColor.Black, ConsoleColor.Green); else C(" off ", ConsoleColor.DarkGray);
        Console.WriteLine();
    }
    static void Choices(string[] names, int cur)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (i == cur) C(" " + names[i] + " ", ConsoleColor.Black, ConsoleColor.Yellow);
            else C(" " + names[i] + " ", ConsoleColor.DarkGray);
            if (i < names.Length - 1) C("·", ConsoleColor.DarkGray);
        }
    }
    static string Shorten(string s, int n) { return s.Length <= n ? s : "..." + s.Substring(s.Length - n + 3); }

    static string InstallKind(string dir)
    {
        string d = dir.ToLowerInvariant();
        if (d.Contains(Path.DirectorySeparatorChar + "steamapps" + Path.DirectorySeparatorChar)) return "STEAM";
        if (File.Exists(Path.Combine(dir, "goggame-1434984562.info")) || d.Contains("gog galaxy")) return "GOG";
        return "OTHER";
    }

    static string exeLangDir = null, exeLang = "";
    static string ExeLanguage(string dir)
    {
        if (dir == exeLangDir) return exeLang;
        exeLangDir = dir; exeLang = "";
        try
        {
            string sha = Sha256(File.ReadAllBytes(Path.Combine(dir, "TNOVA", "__FF.EXE")));
            if (sha == HD_SHA_ORIGINAL || sha == HD_SHA_READY) exeLang = "French";
            else if (sha == EN_SHA) exeLang = "English";
        }
        catch { }
        return exeLang;
    }

    // Music: Roland = General MIDI on the MPU-401 (DOSBox plays it with the system synthesizer), FM = the original
    // Sound Blaster synthesis. Written in the TN.CFG of the game or demo being launched (original kept once).
    static void ApplyMusic(string dirName)
    {
        if (OptMusic == 2) return;
        try
        {
            string cfg = Path.Combine(GameDir, dirName, "TN.CFG");
            if (!File.Exists(cfg)) return;
            string[][] want = OptMusic == 0
                ? new[] { new[] { "midi_num", "12" }, new[] { "midi_io", "816" }, new[] { "midi_extra", "1" } }
                : new[] { new[] { "midi_num", "3" } };
            List<string> lines = new List<string>(File.ReadAllLines(cfg));
            bool changed = false;
            foreach (string[] w in want)
            {
                int i = lines.FindIndex(l => l.Trim().StartsWith(w[0] + " "));
                string line = w[0] + " " + w[1];
                if (i < 0) { lines.Add(line); changed = true; }
                else if (lines[i].Trim() != line) { lines[i] = line; changed = true; }
            }
            if (!changed) return;
            if (!File.Exists(cfg + ".tnplus-original")) File.Copy(cfg, cfg + ".tnplus-original");
            File.WriteAllLines(cfg, lines.ToArray());
            Console.WriteLine("Music set to " + MUSIC_NAMES[OptMusic] + " (" + dirName + "\\TN.CFG, original kept as TN.CFG.tnplus-original).");
        }
        catch { }
    }

    static string OnOff(bool b) { return b ? "[ON]" : "[off]"; }
    static string Line(string key, string label, string value) { return "  " + key + "  " + label.PadRight(50) + value; }

    static bool IsInstall(string dir)
    {
        return dir != "" && File.Exists(Path.Combine(dir, "_DOSBOX", "dosbox-staging.exe"))
            && File.Exists(Path.Combine(dir, "_DOSBOX", "dosbox_terranova_windows.conf"));
    }

    static List<string> FindInstalls()
    {
        List<string> res = new List<string>();
        Action<string> add = d => { if (d != null && IsInstall(d) && !res.Exists(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase))) res.Add(d); };
        try   // GOG
        {
            foreach (string root in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games\1434984562", @"SOFTWARE\GOG.com\Games\1434984562" })
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(root))
                    if (k != null) add(k.GetValue("path") as string);
        }
        catch { }
        try   // Steam libraries
        {
            string steam = null;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (k != null) steam = k.GetValue("SteamPath") as string;
            if (steam != null)
            {
                List<string> libs = new List<string> { steam };
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                foreach (string lib in libs)
                    add(Path.Combine(lib.Replace('/', '\\'), "steamapps", "common", "Terra Nova Strike Force Centauri"));
            }
        }
        catch { }
        if (GameDir != "") add(GameDir);
        return res;
    }

    // The 1996 demos ship ready to run with the GOG and Steam editions (TNDEMO1 / TNDEMO2 next to TNOVA).
    static bool HasDemo(string dir, int n)
    {
        return dir != "" && File.Exists(Path.Combine(dir, TARGET_DIRS[n], "TNDEMO.BAT")) && File.Exists(Path.Combine(dir, TARGET_DIRS[n], "TN.CFG"));
    }

    // DOSBox [autoexec] for the chosen target: the game keeps the edition's own launch file, with the stereo
    // fix inserted before the game starts (an [autoexec] in a later -conf would only run after EXIT).
    static string WriteLaunchConf(string db, int target)
    {
        string mixer = OptStereoFix ? "mixer sb reverse /noshow\r\n" : "";
        string text;
        string stock = Path.Combine(db, "dosbox_terranova_windows_launch.conf");
        if (target == 0)
        {
            text = File.ReadAllText(stock);
            int i = text.IndexOf("[autoexec]", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                int eol = text.IndexOf('\n', i);
                text = eol < 0 ? text + "\r\n" + mixer : text.Insert(eol + 1, mixer);
            }
        }
        else
            text = "# Terra Nova Plus: " + TARGET_NAMES[target] + "\r\n[autoexec]\r\n" + mixer +
                "mount C \"..\"\r\nC:\r\nCD \\" + TARGET_DIRS[target] + "\r\nCLS\r\nCALL TNDEMO.BAT\r\nEXIT\r\n";
        string path = Path.Combine(ExeDir, "TNPlus_launch.conf");
        File.WriteAllText(path, text);
        return path;
    }

    static bool Launch()
    {
        if (!IsInstall(GameDir))
        {
            Console.WriteLine("Game folder not found. Set game_dir in TNPlus.ini, or start the game yourself and choose A.");
            return false;
        }
        if (Process.GetProcessesByName("dosbox-staging").Length > 0)
        {
            Console.WriteLine("DOSBox is already running: attaching to it" +
                (OptWide ? " (widescreen needs the game to be started from this tool: restart it from here)." : "."));
            return true;
        }
        bool demo = LaunchTarget > 0;
        if (demo && !HasDemo(GameDir, LaunchTarget))
        {
            Console.WriteLine(TARGET_NAMES[LaunchTarget] + " not found in " + Path.Combine(GameDir, TARGET_DIRS[LaunchTarget]) + ": see README (demos).");
            return false;
        }
        ApplyMusic(TARGET_DIRS[LaunchTarget]);
        hdState = demo ? -1 : 0;                    // HD only knows the full game's executable
        hdExeReady = false;
        if (OptHD && !demo)
        {
            string why;
            hdExeReady = PrepareExeForHd(out why);
            Console.WriteLine(hdExeReady ? "HD 640x400: " + why : "HD 640x400 unavailable: " + why);
        }
        string db = Path.Combine(GameDir, "_DOSBOX");
        string args = "-conf dosbox_terranova_windows.conf -conf \"" + WriteLaunchConf(db, LaunchTarget) + "\"";
        if (OptWide)
        {
            string overlay = Path.Combine(ExeDir, "TNPlus_16x9.conf");
            File.WriteAllText(overlay,
                "# Terra Nova Plus: stretch the 4:3 picture to 16:9 (the tool widens the field of view to match)\r\n" +
                "[render]\r\naspect = stretch\r\ninteger_scaling = off\r\n");
            args += " -conf \"" + overlay + "\"";
        }
        args += " -noconsole";
        ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(db, "dosbox-staging.exe"), args);
        psi.WorkingDirectory = db;
        psi.UseShellExecute = false;
        launched = Process.Start(psi);
        launchedWide = OptWide;
        launchedDemo = demo;
        Console.WriteLine((demo ? TARGET_NAMES[LaunchTarget] : "Game") + " started" + (OptWide ? " in widescreen 16:9." : "."));
        return true;
    }

    // ------------------------------------------------------------------ main loop
    static void Run()
    {
        Console.WriteLine(TITLE + " " + VERSION + " running - leave this window open (it can stay minimized).");
        Console.WriteLine("In a mission: " +
            (OptFreelook ? KeyName(ScanFreelook) + " freelook   " : "") +
            (OptNoclip ? KeyName(ScanNoclip) + " noclip   " : "") + KeyName(ScanDistance) + " view distance");
        if (OptFreelook) Console.WriteLine("Freelook switches off in menus (O / Esc) and when the mission ends.");
        if (OptNoclip) Console.WriteLine("Noclip: move keys, Space / Left Ctrl up / down, Left Shift x4. Land before switching it off!");
        if (OptHD) Console.WriteLine("HD 640x400: missions in 320x400 are shown in HD, " + KeyName(ScanSmoothing) + " toggles the 3D smoothing, " +
            KeyName(ScanHudFilter) + " the sharp HUD.");
        Console.WriteLine("When you are done playing, close this window (anti-cheat note: see README).");
        Console.WriteLine();
        attached = false;
        if (launched != null)
            ShowWindow(GetConsoleWindow(), 6);      // minimize: never steal the game's focus
        else                                        // attach mode: stay visible until the game is found
            Say("Waiting for Terra Nova: start the game (this window minimizes once it is found).", 0);

        int vkFree = (int)MapVirtualKey((uint)ScanFreelook, 1), vkClip = (int)MapVirtualKey((uint)ScanNoclip, 1);
        int vkDist = (int)MapVirtualKey((uint)ScanDistance, 1), vkOptions = (int)MapVirtualKey(0x18, 1);
        int vkSmooth = (int)MapVirtualKey((uint)ScanSmoothing, 1), vkHud = (int)MapVirtualKey((uint)ScanHudFilter, 1);
        bool prevS = false, prevH = false, prevSt = false;
        int vkStereo = (int)MapVirtualKey((uint)ScanStereo, 1);
        double lastHd = -10;
        int[] vkFwd = { (int)MapVirtualKey(0x11, 1) }, vkBack = { (int)MapVirtualKey(0x1F, 1) };
        int[] vkLeft = { (int)MapVirtualKey(0x1E, 1) }, vkRight = { (int)MapVirtualKey(0x20, 1) };
        const int VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_LCONTROL = 0xA2, VK_LSHIFT = 0xA0;
        RawMouse mouse = new RawMouse();
        timeBeginPeriod(1);                         // 1-2 ms sleeps instead of ~15.6 ms: smoother

        int distMode = OptDistance;
        bool freelook = false, noclip = false, prevF = false, prevN = false, prevD = false, gapPrev = false;
        uint entry = 0;
        List<uint> blocks = new List<uint>();
        double x = 0, y = 0, z = 0;
        double lastAttach = -10, lastFix = 0, lastCheck = 0, pendingT = 0, tPrev = 0;
        int pending = 0;
        byte[] camPrev = null;
        Stopwatch clock = Stopwatch.StartNew();

        while (true)
        {
            mouse.Pump();
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - tPrev, 0.05); tPrev = now;
            try
            {
                if (launched != null && launched.HasExited)
                {   // the game started from the menu is closed: back to the menu
                    Release(); Detach(); launched = null; launchedWide = false;
                    ShowWindow(GetConsoleWindow(), 9);
                    return;
                }
                if (hProc == IntPtr.Zero || !GameStillLoaded())
                {
                    if (freelook || noclip) Say("Freelook / noclip OFF (game left)", 500);
                    if (attached && launched == null)
                    {   // attach mode: the game was closed, wait for the next one (no focus stealing)
                        attached = false;
                        ShowWindow(GetConsoleWindow(), 4);
                        Say("Game closed. Waiting for Terra Nova again (close this window to quit).", 0);
                    }
                    freelook = noclip = false; blocks.Clear(); frozen = false; Unclip();
                    if (hdState == 1 || launched == null) hdState = 0;   // game left (back to the GOG launcher too):
                                                                          // new attempt when it starts again
                    if (now - lastAttach > 2) { lastAttach = now; TryAttach(); }
                    mouse.Take(); Thread.Sleep(50); continue;
                }
                bool fg = ForegroundIsGame();

                // --- HD: injected as soon as the game is loaded, before its first mission
                if (OptHD && hdState == 0 && now - lastHd > 0.5) { lastHd = now; TryHdInject(); }
                bool s = hdState == 1 && fg && Down(vkSmooth);
                if (s && !prevS)
                {
                    HdSmoothing = ReadInt(HdPayload.Smoothing) == 0;
                    WriteInt(HdPayload.Smoothing, HdSmoothing ? 1 : 0);
                    Say("HD smoothing " + (HdSmoothing ? "ON" : "OFF"), HdSmoothing ? 1000 : 600);
                }
                prevS = s;
                bool hk = hdState == 1 && fg && Down(vkHud);
                if (hk && !prevH)
                {
                    HdHudFilter = ReadInt(HdPayload.HudFilter) == 0;
                    WriteInt(HdPayload.HudFilter, HdHudFilter ? 1 : 0);
                    Say("HD sharp HUD " + (HdHudFilter ? "ON" : "OFF"), HdHudFilter ? 1000 : 600);
                }
                prevH = hk;
                bool stk = haveStereo && fg && Down(vkStereo);
                if (stk && !prevSt)
                {
                    bool rev = Read(aStereo, 1)[0] == 0;
                    Write(aStereo, new byte[] { (byte)(rev ? 1 : 0) });
                    Say("Sound effects stereo: " + (rev ? "swapped" : "normal"), rev ? 600 : 1000);
                }
                prevSt = stk;

                // --- view distance key + periodic fixes (fog, 320x400, widescreen)
                bool d = fg && Down(vkDist);
                bool changed = d && !prevD; prevD = d;
                if (changed) { distMode = (distMode + 1) % 3; Say("View distance: " + DIST_NAMES[distMode], 700 + 250 * distMode, 90); }
                if (changed || now - lastFix > 1.0) { lastFix = now; PeriodicFixes(distMode); }

                // --- toggles (rising edges, game in the foreground only)
                bool f = OptFreelook && fg && Down(vkFree), n = OptNoclip && fg && Down(vkClip);
                if (f && !prevF)
                {
                    if (freelook) { freelook = false; Say("Freelook OFF", 500); }
                    else
                    {
                        entry = PlayerEntry();
                        if (InMission(entry) && CameraAlive(entry))
                        {
                            freelook = true; mouse.Take(); camPrev = Read(aCamHeading, 2); pending = 0;
                            Say("Freelook ON", 1200);
                        }
                        else Say("Not in a mission (3D view) - freelook refused", 300, 300);
                    }
                }
                if (n && !prevN)
                {
                    if (noclip) { noclip = false; Say("Noclip OFF", 500); }
                    else
                    {
                        entry = PlayerEntry();
                        blocks = InMission(entry) ? PhysicsBlocks(entry) : new List<uint>();
                        if (blocks.Count > 0 && CameraAlive(entry))
                        {
                            x = ReadInt(blocks[0]) / (double)PHYS_SCALE;
                            y = ReadInt(blocks[0] + 16) / (double)PHYS_SCALE;
                            z = ReadInt(blocks[0] + 32) / (double)PHYS_SCALE;
                            noclip = true; gapPrev = false;
                            Say(string.Format("Noclip ON ({0:0}, {1:0}, altitude {2:0.0})", x, y, z), 1200);
                        }
                        else Say("Not in a mission (3D view) - noclip refused", 300, 300);
                    }
                }
                prevF = f; prevN = n;

                // --- safety: mission end (checked 4 times a second)
                if ((freelook || noclip) && now - lastCheck > 0.25)
                {
                    lastCheck = now;
                    if (!InMission(entry)) { freelook = noclip = false; Say("Freelook / noclip OFF (mission ended)", 500); }
                    else if (noclip)
                    {   // the object table copies OUR position; it drops to 0 when the mission ends
                        double mx = ReadInt(entry + 0x0f) / 65536.0, my = ReadInt(entry + 0x13) / 65536.0;
                        double bx = ReadInt(blocks[0]) / (double)PHYS_SCALE, by = ReadInt(blocks[0] + 16) / (double)PHYS_SCALE;
                        bool gap = Math.Abs(mx - x) > 5 || Math.Abs(my - y) > 5 || Math.Abs(bx - x) > 5 || Math.Abs(by - y) > 5;
                        if (gap && gapPrev) { noclip = false; freelook = false; Say("Noclip OFF (mission ended)", 500); }
                        gapPrev = gap;
                    }
                }
                // --- freelook passive safety (no periodic nudge: it made the view shake)
                if (freelook)
                {
                    if (fg && (Down(vkOptions) || Down(VK_ESCAPE))) { freelook = false; Say("Freelook OFF (menu)", 500); }
                    else
                    {
                        byte[] cam = Read(aCamHeading, 2);
                        if (cam[0] != camPrev[0] || cam[1] != camPrev[1]) { camPrev = cam; pending = 0; }
                        else if (pending != 0 && now - pendingT > BLIND && Math.Abs(pending) > 64)
                        {
                            AddHeading(entry, -pending); pending = 0;
                            freelook = false; Say("Freelook OFF (left the 3D view)", 500);
                        }
                    }
                }

                // --- freelook
                int mdx, mdy; mouse.Take(out mdx, out mdy);
                if (freelook && fg)
                {
                    if (mdx != 0)
                    {
                        AddHeading(entry, mdx * SensX);
                        if (pending == 0) pendingT = now;
                        pending += mdx * SensX;
                    }
                    if (mdy != 0)
                    {
                        int p = BitConverter.ToInt16(Read(aPitch, 2), 0) + (InvertY ? -mdy : mdy) * SensY;
                        Write(aPitch, BitConverter.GetBytes((short)Math.Max(PITCH_MIN, Math.Min(PITCH_MAX, p))));
                    }
                    byte[] fr = Read(aFrame, 4);
                    int w = BitConverter.ToInt16(fr, 0), h = BitConverter.ToInt16(fr, 2);
                    if (w >= 200 && w <= 640 && h >= 200 && h <= 480)
                    {
                        byte[] c = new byte[4];
                        BitConverter.GetBytes((short)(w / 2)).CopyTo(c, 0);
                        BitConverter.GetBytes((short)Math.Round(h * CENTRE_Y)).CopyTo(c, 2);
                        Write(aCursor, c);                      // cursor used for aiming
                        Write(aDraw, c);                        // where the reticle is drawn
                        Write(aWarp, new byte[] { 1 });         // mouse lib: push cursor to driver, skip reading
                    }
                    Write(aFreeze, new byte[] { 1 }); frozen = true;   // no reticle trails on the cockpit
                    Clip();                                     // keep the Windows pointer in the game window
                }
                else
                {
                    if (frozen) { Write(aFreeze, new byte[] { 0 }); frozen = false; }
                    Unclip();
                }

                // --- noclip
                if (noclip)
                {
                    if (fg)
                    {
                        double hd = BitConverter.ToUInt16(Read(entry + 0x1b, 2), 0) / 65536.0 * 2 * Math.PI;
                        double v = NoclipSpeed * (Down(VK_LSHIFT) ? 4 : 1) * dt;
                        int av = (DownAny(vkFwd) ? 1 : 0) - (DownAny(vkBack) ? 1 : 0);
                        int lat = (DownAny(vkRight) ? 1 : 0) - (DownAny(vkLeft) ? 1 : 0);
                        int mt = (Down(VK_SPACE) ? 1 : 0) - (Down(VK_LCONTROL) ? 1 : 0);
                        x += (av * Math.Cos(hd) - lat * Math.Sin(hd)) * v;   // engine Y axis points south
                        y += (av * Math.Sin(hd) + lat * Math.Cos(hd)) * v;
                        z += mt * v;
                        x = Math.Max(MAP_MIN, Math.Min(MAP_MAX, x));
                        y = Math.Max(MAP_MIN, Math.Min(MAP_MAX, y));
                    }
                    byte[] st = new byte[40];
                    BitConverter.GetBytes((int)(x * PHYS_SCALE)).CopyTo(st, 0);     // +4, +8 speeds stay 0
                    BitConverter.GetBytes((int)(y * PHYS_SCALE)).CopyTo(st, 16);
                    BitConverter.GetBytes((int)(z * PHYS_SCALE)).CopyTo(st, 32);
                    foreach (uint b in blocks)
                    {   // position + zero speed only (x,+4 / y,+20 / z,+36), the rest untouched
                        Write(b, Slice(st, 0, 8)); Write(b + 16, Slice(st, 16, 8)); Write(b + 32, Slice(st, 32, 8));
                    }
                }
            }
            catch (Exception e)
            {
                if (freelook || noclip) Say("Freelook / noclip OFF (" + e.Message + ")", 500);
                freelook = noclip = false; blocks.Clear(); frozen = false; Unclip(); Detach(); Thread.Sleep(500);
            }
            Thread.Sleep(2);
        }
    }

    static byte[] Slice(byte[] b, int from, int n) { byte[] r = new byte[n]; Array.Copy(b, from, r, 0, n); return r; }
    static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
    static bool DownAny(int[] vks) { foreach (int v in vks) if (Down(v)) return true; return false; }

    // ------------------------------------------------------------------ periodic fixes
    // Rings of the terrain camera (copied there from RESSIM.RES 925/926 by the game at each mission start):
    // +0x30 7 words (first three = ring distances), +0x58 index of the first type-0 ring, +0x5A ring count,
    // +0x5C rings of 12 bytes: type, (from, to), step, (from, to). Only the usual 6-ring layout is touched.
    static void FixDetail(int[] d)
    {
        byte[] b = Read(aTerrain, 0xA8);
        if (BitConverter.ToInt16(b, 0x5A) != 6 || BitConverter.ToInt16(b, 0x58) != 2) return;
        int[] types = { 1, 2, 0, 0, 0, 0 }, steps = { 1, 1, 1, 2, 4, 8 };
        for (int k = 0; k < 6; k++)
            if (b[0x5C + 12 * k] != types[k] || BitConverter.ToInt16(b, 0x5C + 12 * k + 5) != steps[k]) return;
        int[] to = { BitConverter.ToInt16(b, 0x5C + 9), BitConverter.ToInt16(b, 0x5C + 12 + 9), d[0], d[1], d[2], d[3] };
        bool same = true;
        for (int k = 2; k < 6 && same; k++) same = BitConverter.ToInt16(b, 0x5C + 12 * k + 9) == to[k];
        if (same) return;
        byte[] r = new byte[72];
        int from = 0;
        for (int k = 0; k < 6; k++)
        {
            r[12 * k] = (byte)types[k];
            BitConverter.GetBytes((short)from).CopyTo(r, 12 * k + 1);
            BitConverter.GetBytes((short)to[k]).CopyTo(r, 12 * k + 3);
            BitConverter.GetBytes((short)steps[k]).CopyTo(r, 12 * k + 5);
            BitConverter.GetBytes((short)from).CopyTo(r, 12 * k + 7);
            BitConverter.GetBytes((short)to[k]).CopyTo(r, 12 * k + 9);
            from = to[k];
        }
        Write(aTerrain + 0x30, new byte[] { (byte)d[0], (byte)(d[0] >> 8), (byte)d[1], (byte)(d[1] >> 8), (byte)d[2], (byte)(d[2] >> 8) });
        Write(aTerrain + 0x5C, r);
    }

    static void PeriodicFixes(int distMode)
    {
        // view distance: rewrite the fog table AND its check copy in one write (they are contiguous;
        // the game compares them: "Memory trash" if they differ). The weather radius itself is not
        // touched (the game re-imposes it), only the table used for drawing.
        if (haveFog)
        {
            int r = ReadInt(aWeather);
            if (r >= 20 && r <= 2000)
            {
                byte[] t = FogTable(DIST_R[distMode] > 0 ? DIST_R[distMode] : r);
                byte[] cur = Read(aFogTable, 2048);
                bool same = true;
                for (int i = 0; i < 2048 && same; i++) same = cur[i] == t[i & 1023];
                if (!same) { byte[] both = new byte[2048]; t.CopyTo(both, 0); t.CopyTo(both, 1024); Write(aFogTable, both); }
            }
        }
        // terrain detail: rewrite the main view's rings when the game has (re)loaded its own preset
        if (OptDetail > 0 && haveDetail) FixDetail(DETAIL_RINGS[OptDetail]);
        // 320x400: same path as the options "Accept" button; applied by the game on the next mission frame
        // (the HD mode only kicks in for 320x400 missions)
        if ((OptForce400 || OptHD) && have400)
        {
            byte fl = Read(aResFlags, 1)[0];
            if ((fl & 2) == 0)
            {
                WriteInt(aResMode, 1);
                Write(aResFlags, new byte[] { (byte)(fl | 3) });
                if (aResButton != 0 && ReadUInt(aResButton) == aResCallback)
                {
                    byte cur = Read(aResButton + 8, 1)[0];
                    if (cur == 1 || cur == 2) Write(aResButton + 8, new byte[] { 2 });   // keep the radio button consistent
                }
            }
        }
        // widescreen: pixel ratio x0.75 (the 3D library rescales the view every frame, before clipping),
        // and the terrain engine's two cached values realigned (otherwise objects slide on the ground)
        if (OptWide && launchedWide && haveWide && launched != null && gamePid == launched.Id)
        {
            uint rp = ReadUInt(aRatioRef);
            foreach (uint a in (rp > 0x1000 && rp + 4 < regStart + regSize - guestBase) ? new[] { aRatio, rp } : new[] { aRatio })
            {
                int v = ReadInt(a);
                if (v == RATIO_STOCK[0] || v == RATIO_STOCK[1]) WriteInt(a, (int)(v * WIDE));
            }
            // the 3D library may be rendering another view (e.g. the locked target in a cockpit screen):
            // only trust its scale while it is drawing the main view (same canvas width before and after)
            int w1 = ReadInt(aG3Width);
            int sx = ReadInt(aScaleX), v4 = ReadInt(aTerrain + 4), vc = ReadInt(aTerrain + 0x0C);
            int w2 = ReadInt(aG3Width);
            uint canvas = ReadUInt(aTerrain + 0x1C);
            int mainW = Inside(canvas, 0x10) ? BitConverter.ToInt16(Read(canvas + 8, 2), 0) : -1;
            if (w1 == w2 && w1 == mainW && sx > 0 && v4 > 0 && vc > 0)
            {
                double k = 65536.0 * 65536.0 / sx / v4;   // (1 / scale) / terrain value
                if (Math.Abs(k - 1) > 0.02 && k > 0.5 && k < 2)
                {
                    WriteInt(aTerrain + 4, (int)(v4 * k));
                    WriteInt(aTerrain + 0x0C, (int)(vc * k));
                }
            }
        }
    }

    // ------------------------------------------------------------------ HD 640x400
    // __FF.EXE: object 3 (data + BSS + stack) gets 16 KB more virtual size. The loader zero-fills them
    // above the stack top (the stack grows down from the old end), the heap starts 16 KB higher, and
    // nothing in the game ever writes there: that is where the HD code and its tables live.
    static bool PrepareExeForHd(out string msg)
    {
        string exe = Path.Combine(GameDir, "TNOVA", "__FF.EXE");
        if (!File.Exists(exe)) { msg = "TNOVA\\__FF.EXE not found"; return false; }
        byte[] d = File.ReadAllBytes(exe);
        string sha = Sha256(d);
        if (sha == HD_SHA_READY) { msg = "ready"; return true; }
        if (sha != HD_SHA_ORIGINAL) { msg = "it needs the GOG version of the game (unknown __FF.EXE)"; return false; }
        int le = IndexOf(d, new byte[] { (byte)'L', (byte)'E', 0, 0 }, 0);
        int at = le + BitConverter.ToInt32(d, le + 0x40) + 24 * 2;        // object table, object 3: virtual size
        if (le < 0 || BitConverter.ToUInt32(d, at) != OBJ3_SIZE) { msg = "unexpected executable layout"; return false; }
        string backup = exe + ".tnplus-original";
        try
        {
            if (!File.Exists(backup)) File.Copy(exe, backup);
            BitConverter.GetBytes(OBJ3_HD).CopyTo(d, at);
            File.WriteAllBytes(exe, d);
        }
        catch (Exception e) { msg = "cannot update __FF.EXE (" + e.Message + ")"; return false; }
        if (Sha256(File.ReadAllBytes(exe)) != HD_SHA_READY) { msg = "__FF.EXE update failed"; return false; }
        msg = "__FF.EXE prepared (16 KB more memory; original kept as __FF.EXE.tnplus-original)";
        return true;
    }

    static string Sha256(byte[] d)
    {
        using (var h = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(h.ComputeHash(d)).Replace("-", "").ToLowerInvariant();
    }

    // the HD code must be in place before the first mission runs: DOSBox caches the game code it has
    // translated, later writes to that code would be ignored (the temporary pool is still unused until then)
    static void TryHdInject()
    {
        int n = HdPayload.PatchAt.Length;
        bool allNew = true, allOld = true;
        for (int i = 0; i < n; i++)
        {
            byte[] cur = Read(HdPayload.PatchAt[i], HdPayload.PatchOld[i].Length);
            allNew &= Same(cur, HdPayload.PatchNew[i]);
            allOld &= Same(cur, HdPayload.PatchOld[i]);
        }
        if (allNew && Same(Read(HdPayload.Code, HdPayload.CodeBytes.Length), HdPayload.CodeBytes))
        {
            hdState = 1; Say("HD 640x400 already active in this game", 0); return;
        }
        string why = null;
        uint pool = allOld ? ReadUInt(0x43e360) : 0;
        if (allOld && pool == 0) return;               // the game has not allocated its pool yet: wait
        if (!allOld) why = "unsupported game version (GOG only)";
        else if (pool != HdPayload.PoolBase)
            why = "__FF.EXE not prepared (start the game from this tool with option 6 on)";
        else if (ReadUInt(HdPayload.PoolMax) != 0)
            why = "a mission already ran in this game: restart the game from this tool";
        else
        {
            byte[] zone = Read(HdPayload.Zone, (int)(HdPayload.End - HdPayload.Zone));
            foreach (byte b in zone) if (b != 0) { why = "its memory area is not free"; break; }
        }
        if (why != null) { hdState = -1; Say("HD 640x400 unavailable: " + why, 300, 300); return; }
        Write(HdPayload.Data, HdPayload.DataInit);
        Write(HdPayload.Code, HdPayload.CodeBytes);
        for (int i = 0; i < n; i++) Write(HdPayload.PatchAt[i], HdPayload.PatchNew[i]);
        WriteInt(HdPayload.Smoothing, HdSmoothing ? 1 : 0);
        WriteInt(HdPayload.HudFilter, HdHudFilter ? 1 : 0);
        hdState = 1;
        Say("HD 640x400 ready: missions in 320x400 will be in HD (" + KeyName(ScanSmoothing) + " smoothing " +
            (HdSmoothing ? "ON" : "OFF") + ", " + KeyName(ScanHudFilter) + " sharp HUD " + (HdHudFilter ? "ON" : "OFF") + ")", 1000);
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static byte[] FogTable(double r)
    {
        byte[] t = new byte[1024];
        for (int i = 0; i < 1024; i++)
        {
            double v = 1 - Math.Exp(-(i - 20) / r);
            t[i] = v < 0 ? (byte)0 : (byte)Math.Min(15, (int)(v * 16));
        }
        return t;
    }

    // ------------------------------------------------------------------ game helpers
    static uint PlayerEntry()
    {
        ushort id = BitConverter.ToUInt16(Read(aPlayerId + 2, 2), 0);
        return aMaster + (uint)id * 0x25;
    }

    static bool InMission(uint entry)
    {
        double x = ReadInt(entry + 0x0f) / 65536.0, y = ReadInt(entry + 0x13) / 65536.0;
        return x >= 2 && x <= 510 && y >= 2 && y <= 510;      // 0 outside a mission
    }

    static void AddHeading(uint entry, int delta)
    {
        ushort cap = BitConverter.ToUInt16(Read(entry + 0x1b, 2), 0);
        Write(entry + 0x1b, BitConverter.GetBytes((ushort)((cap + delta) & 0xFFFF)));
    }

    static bool CameraAlive(uint entry)
    {   // one-time check that the 3D view is live: the camera must follow a tiny heading nudge
        byte[] cam0 = Read(aCamHeading, 2);
        AddHeading(entry, NUDGE);
        bool ok = false;
        Stopwatch t = Stopwatch.StartNew();
        while (t.Elapsed.TotalSeconds < 1.0)
        {
            Thread.Sleep(10);
            byte[] cam = Read(aCamHeading, 2);
            if (cam[0] != cam0[0] || cam[1] != cam0[1]) { ok = true; break; }
        }
        AddHeading(entry, -NUDGE);
        return ok;
    }

    // the player's physics state (the real position: the object table is a copy of it), found
    // WITHOUT writing: x, y AND altitude must match the object table, speeds must be small
    static List<uint> PhysicsBlocks(uint entry)
    {
        List<uint> res = new List<uint>();
        byte[] ram = RawRead(regStart, (int)regSize);
        if (ram == null) return res;
        long e = guestBase + entry - regStart;
        int mx = BitConverter.ToInt32(ram, (int)e + 0x0f), my = BitConverter.ToInt32(ram, (int)e + 0x13), mz = BitConverter.ToInt32(ram, (int)e + 0x17);
        long tx = 6L * mx, ty = 6L * my;
        for (int i = 0; i + 48 <= ram.Length; i++)
        {
            int vx = BitConverter.ToInt32(ram, i);
            if (Math.Abs(vx - tx) >= PHYS_SCALE / 2) continue;
            int vy = BitConverter.ToInt32(ram, i + 16);
            if (Math.Abs(vy - ty) >= PHYS_SCALE) continue;
            int vz = BitConverter.ToInt32(ram, i + 32);
            if (Math.Abs(vx / (double)PHYS_SCALE - mx / 65536.0) >= 0.15) continue;
            if (Math.Abs(vy / (double)PHYS_SCALE - my / 65536.0) >= 0.15) continue;
            if (Math.Abs(vz / (double)PHYS_SCALE - mz / 65536.0) >= 12) continue;
            bool slow = true;
            foreach (int o in new[] { 4, 8, 20, 24, 36, 40 })
                if (Math.Abs(BitConverter.ToInt32(ram, i + o) / 65536.0) >= 100) { slow = false; break; }
            if (slow) res.Add((uint)(regStart + i - guestBase));
        }
        return res;
    }

    static bool GameStillLoaded()
    {
        try
        {
            Process p = Process.GetProcessById(gamePid);
            if (p.HasExited) { Detach(); return false; }
            byte[] b = Read(aHazeText, HAZE_TEXT.Length);
            for (int i = 0; i < b.Length; i++) if (b[i] != HAZE_TEXT[i]) { Detach(); return false; }
            return true;
        }
        catch { Detach(); return false; }
    }

    static bool ForegroundIsGame()
    {
        int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        return pid == gamePid;
    }

    // ------------------------------------------------------------------ cursor helpers
    static void Clip()
    {
        IntPtr hwnd = GetForegroundWindow();        // the game window (checked by the caller)
        RECT r; POINT p = new POINT();
        if (!GetClientRect(hwnd, out r) || !ClientToScreen(hwnd, ref p)) return;
        RECT zone = new RECT { Left = p.X, Top = p.Y, Right = p.X + r.Right, Bottom = p.Y + r.Bottom };
        ClipCursor(ref zone); clipped = true;       // re-applied every loop (Windows may drop it)
    }

    static void Unclip()
    {
        if (clipped) { ClipCursorOff(IntPtr.Zero); clipped = false; }
    }

    static void Release()
    {
        try { if (frozen && hProc != IntPtr.Zero) Write(aFreeze, new byte[] { 0 }); } catch { }
        frozen = false; ClipCursorOff(IntPtr.Zero); clipped = false;
    }

    // ------------------------------------------------------------------ attach / signature scan
    static bool attached;                          // a game was found (attach mode messages)

    static void TryAttach()
    {
        foreach (Process p in Process.GetProcesses())
        {
            if (!p.ProcessName.ToLowerInvariant().Contains("dosbox")) continue;
            IntPtr h = OpenProcess(0x0010 | 0x0020 | 0x0008 | 0x0400, false, p.Id);
            if (h == IntPtr.Zero) continue;
            hProc = h; gamePid = p.Id;
            if (Scan())
            {
                Say("Game found in " + p.ProcessName + " (pid " + p.Id + ")" +
                    (haveFog ? "" : " - view distance unavailable") +
                    (have400 ? "" : " - 320x400 unavailable") +
                    (OptWide && !haveWide ? " - widescreen unavailable" : "") +
                    (OptWide && haveWide && (launched == null || p.Id != launched.Id) ? " - widescreen off (game not started from this tool)" : ""), 0);
                attached = true;
                ShowWindow(GetConsoleWindow(), 6);  // minimize: never steal the game's focus
                return;
            }
            CloseHandle(h); hProc = IntPtr.Zero; gamePid = 0;
        }
    }

    static void Detach()
    {
        if (hProc != IntPtr.Zero) CloseHandle(hProc);
        hProc = IntPtr.Zero; gamePid = 0;
    }

    static Regex Sig(string s)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string tok in s.Split(' '))
        {
            if (tok == "(....)") sb.Append("(....)");
            else if (tok == "....") sb.Append("....");
            else for (int i = 0; i < tok.Length; i += 2)
                    sb.Append(Regex.Escape(((char)Convert.ToByte(tok.Substring(i, 2), 16)).ToString()));
        }
        return new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }

    static string Latin1(byte[] b) { return Encoding.GetEncoding(28591).GetString(b); }

    static uint Cap(Match m, int i)
    {
        string g = m.Groups[i].Value;
        return (uint)(g[0] | (g[1] << 8) | (g[2] << 16) | (g[3] << 24));
    }

    // best = highest captured addresses (relocated code, not a raw copy of the exe left in memory)
    static Match Best(string name, string mem)
    {
        Regex r = Sig(SIGS[name]);
        Match best = null; uint bestMin = 0;
        for (Match m = r.Match(mem); m.Success; m = m.NextMatch())
        {
            uint mn = uint.MaxValue;
            for (int i = 1; i < m.Groups.Count; i++) mn = Math.Min(mn, Cap(m, i));
            if (best == null || mn > bestMin) { best = m; bestMin = mn; }
        }
        return best;
    }

    static bool Scan()
    {
        long addr = 0;
        MBI mbi;
        while (VirtualQueryEx(hProc, new IntPtr(addr), out mbi, new IntPtr(Marshal.SizeOf(typeof(MBI)))) != IntPtr.Zero)
        {
            long size = (long)mbi.RegionSize.ToUInt64();
            long start = mbi.BaseAddress.ToInt64();
            bool rw = (mbi.Protect & 0x04) != 0 || (mbi.Protect & 0x40) != 0;
            if (mbi.State == 0x1000 && rw && size >= 4L << 20 && size <= 1L << 30 && ScanRegion(start, size)) return true;
            addr = start + size;
            if (addr <= start) break;
        }
        return false;
    }

    static bool ScanRegion(long start, long size)
    {
        byte[] buf = RawRead(start, (int)size);
        if (buf == null) return false;
        string mem = Latin1(buf);
        Regex haze = Sig(SIGS["haze"]);
        for (Match m = haze.Match(mem); m.Success; m = m.NextMatch())
        {
            uint strGuest = Cap(m, 3);
            if (strGuest < 0x100000) continue;                  // raw exe copy: not relocated
            int idx = IndexOf(buf, HAZE_TEXT, 0);
            while (idx >= 0)
            {
                long baseCand = start + idx - strGuest;
                if (baseCand >= start - 0x100000 && PlausibleIvt(baseCand))
                {
                    guestBase = baseCand; regStart = start; regSize = size;
                    if (ResolveAll(buf, mem, m)) return true;
                }
                idx = IndexOf(buf, HAZE_TEXT, idx + 1);
            }
        }
        return false;
    }

    // DOSBox fills the real-mode interrupt table with F000:xxxx (BIOS) vectors
    static bool PlausibleIvt(long baseHost)
    {
        byte[] ivt = RawRead(baseHost, 1024);
        if (ivt == null) return false;
        int bios = 0;
        for (int i = 0; i < 256; i++) if (BitConverter.ToUInt16(ivt, i * 4 + 2) == 0xF000) bios++;
        return bios >= 32;
    }

    static bool Inside(uint a, int n) { long host = guestBase + a; return host >= regStart && host + n <= regStart + regSize; }

    static bool ResolveAll(byte[] buf, string mem, Match hazeM)
    {
        Match cam = Best("camera", mem), mou = Best("mouse", mem), drw = Best("draw", mem);
        Match frm = Best("frame", mem), frz = Best("freeze", mem);
        if (cam == null || mou == null || drw == null || frm == null || frz == null) return false;
        aHazeText = Cap(hazeM, 3);
        aPlayerId = Cap(cam, 1); aMaster = Cap(cam, 2); aCamHeading = Cap(cam, 4); aPitch = Cap(cam, 5);
        aWarp = Cap(mou, 1); aCursor = Cap(mou, 2);
        aDraw = Cap(drw, 1);
        aFrame = Cap(frm, 2);
        aFreeze = Cap(frz, 1);
        if (aFreeze != aWarp - 1) return false;         // same layout in every known version
        foreach (uint a in new[] { aPlayerId, aMaster, aCamHeading, aPitch, aWarp, aCursor, aDraw, aFrame, aFreeze })
            if (!Inside(a, 64)) return false;

        // view distance: fog table + its check copy right after it, weather radius
        uint copy = Cap(hazeM, 1), table = Cap(hazeM, 2);
        Match wea = Best("weather", mem);
        haveFog = copy == table + 1024 && wea != null && Inside(table, 2048) && Inside(Cap(wea, 1), 4);
        if (haveFog) { aFogTable = table; aWeather = Cap(wea, 1); }

        // 320x400: radio button callback, its state byte and mode; the button data points to the callback
        Match res = Best("res", mem);
        have400 = res != null && Inside(Cap(res, 1), 1) && Inside(Cap(res, 2), 4);
        if (have400)
        {
            aResFlags = Cap(res, 1); aResMode = Cap(res, 2);
            aResCallback = (uint)(regStart + res.Index - guestBase);
            byte[] ptr = BitConverter.GetBytes(aResCallback);
            List<uint> found = new List<uint>();
            for (int i = IndexOf(buf, ptr, 0); i >= 0 && i + 9 < buf.Length; i = IndexOf(buf, ptr, i + 1))
            {
                uint g = (uint)(regStart + i - guestBase);
                if (g > aResCallback && (buf[i + 8] == 1 || buf[i + 8] == 2)) found.Add(g);
            }
            aResButton = found.Count == 1 ? found[0] : 0;
        }

        // widescreen: pixel ratio (+ its reference), horizontal view scale, terrain camera
        Match asp = Best("aspect", mem), sca = Best("scale", mem), rrf = Best("ratioref", mem), ter = Best("terrain", mem);
        haveWide = asp != null && sca != null && rrf != null && ter != null
            && Cap(rrf, 2) == Cap(asp, 2)                                   // same pixel ratio variable
            && Cap(sca, 2) == Cap(sca, 1) + 4 && Cap(sca, 3) == Cap(sca, 1) + 8
            && Cap(ter, 2) == Cap(ter, 1) + 0x1c;                            // terrain camera layout
        Match ste = Best("stereo", mem);
        haveStereo = ste != null && Inside(Cap(ste, 1), 1);
        if (haveStereo) aStereo = Cap(ste, 1);
        haveDetail = ter != null && Cap(ter, 2) == Cap(ter, 1) + 0x1c && Inside(Cap(ter, 1), 0xA8);
        if (haveDetail) aTerrain = Cap(ter, 1);
        if (haveWide)
        {
            aRatio = Cap(asp, 2); aRatioRef = Cap(rrf, 1); aScaleX = Cap(sca, 1); aTerrain = Cap(ter, 1);
            aG3Width = Cap(asp, 3);                                         // canvas width of the current 3D frame
            haveWide = Inside(aRatio, 4) && Inside(aRatioRef, 4) && Inside(aScaleX, 4) && Inside(aTerrain, 0x20)
                && Inside(aG3Width, 4);
        }
        return true;
    }

    static int IndexOf(byte[] hay, byte[] needle, int from)
    {
        for (int i = from; i <= hay.Length - needle.Length; i++)
        {
            if (hay[i] != needle[0]) continue;
            int j = 1;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ memory
    static byte[] RawRead(long host, int n)
    {
        byte[] b = new byte[n];
        IntPtr got;
        if (!ReadProcessMemory(hProc, new IntPtr(host), b, new IntPtr(n), out got) || got.ToInt64() != n) return null;
        return b;
    }

    static byte[] Read(uint guest, int n)
    {
        byte[] b = RawRead(guestBase + guest, n);
        if (b == null) throw new Exception("read failed");
        return b;
    }

    static int ReadInt(uint guest) { return BitConverter.ToInt32(Read(guest, 4), 0); }
    static uint ReadUInt(uint guest) { return BitConverter.ToUInt32(Read(guest, 4), 0); }
    static void WriteInt(uint guest, int v) { Write(guest, BitConverter.GetBytes(v)); }

    static void Write(uint guest, byte[] data)
    {
        IntPtr w;
        if (!WriteProcessMemory(hProc, new IntPtr(guestBase + guest), data, new IntPtr(data.Length), out w))
            throw new Exception("write failed");
    }

    // ------------------------------------------------------------------ raw mouse
    class RawMouse
    {
        IntPtr hwnd;
        int dx, dy;
        byte[] buf = new byte[64];

        public RawMouse()
        {
            hwnd = CreateWindowEx(0, "STATIC", "TNPlusRaw", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            RAWINPUTDEVICE[] d = { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = 0x100, Target = hwnd } };  // RIDEV_INPUTSINK
            if (!RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                throw new Exception("RegisterRawInputDevices failed");
        }

        public void Pump()
        {
            MSG m;
            int header = IntPtr.Size == 8 ? 24 : 16;
            while (PeekMessage(out m, hwnd, 0, 0, 1))
            {
                if (m.message == 0x00FF)          // WM_INPUT
                {
                    uint size = (uint)buf.Length;
                    if (GetRawInputData(m.lParam, 0x10000003, buf, ref size, (uint)header) != 0xFFFFFFFF
                        && BitConverter.ToUInt32(buf, 0) == 0)                  // RIM_TYPEMOUSE
                    {
                        ushort flags = BitConverter.ToUInt16(buf, header);
                        if ((flags & 1) == 0)                                   // relative movement
                        {
                            dx += BitConverter.ToInt32(buf, header + 12);
                            dy += BitConverter.ToInt32(buf, header + 16);
                        }
                    }
                }
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }

        public void Take() { dx = dy = 0; }
        public void Take(out int x, out int y) { x = dx; y = dy; dx = dy = 0; }
    }

    // ------------------------------------------------------------------ misc
    static void Say(string msg, int freq, int dur = 100)
    {
        Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + msg);
        if (Sound && freq > 0) { try { Console.Beep(freq, dur); } catch { } }
    }

    static string KeyName(int scan)
    {
        uint vk = MapVirtualKey((uint)scan, 1);
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        return vk >= 0x30 && vk <= 0x5A ? ((char)vk).ToString() : "scancode 0x" + scan.ToString("X2");
    }

    static void LoadSettings()
    {
        if (!File.Exists(IniPath)) { SaveSettings(); return; }
        bool haveDisplay = false, oldForce = false, oldHd = false;
        foreach (string line in File.ReadAllLines(IniPath))
        {
            string l = line.Trim();
            if (l.StartsWith(";") || !l.Contains("=")) continue;
            string k = l.Substring(0, l.IndexOf('=')).Trim().ToLowerInvariant();
            string v = l.Substring(l.IndexOf('=') + 1).Trim();
            try
            {
                switch (k)
                {
                    case "freelook": OptFreelook = v != "0"; break;
                    case "noclip": OptNoclip = v != "0"; break;
                    case "view_distance": OptDistance = Math.Max(0, Array.IndexOf(DIST_NAMES, v.ToUpperInvariant())); break;
                    case "terrain_detail": OptDetail = Math.Max(0, Array.IndexOf(DETAIL_NAMES, v.ToUpperInvariant())); break;
                    case "force_320x400": oldForce = v != "0"; break;
                    case "display": Display = Math.Max(0, Array.IndexOf(DISPLAY_NAMES, v.ToUpperInvariant())); haveDisplay = true; break;
                    case "launch": LaunchTarget = Math.Max(0, Array.IndexOf(TARGET_NAMES, v.ToUpperInvariant())); break;
                    case "widescreen": OptWide = v != "0"; break;
                    case "stereo_fix": OptStereoFix = v != "0"; break;
                    case "music": OptMusic = Math.Max(0, Array.IndexOf(MUSIC_NAMES, v.ToUpperInvariant())); break;
                    case "hd": oldHd = v != "0"; break;
                    case "hd_smoothing": HdSmoothing = v != "0"; break;
                    case "key_smoothing": ScanSmoothing = Convert.ToInt32(v, 16); break;
                    case "hd_sharp_hud": HdHudFilter = v != "0"; break;
                    case "key_sharp_hud": ScanHudFilter = Convert.ToInt32(v, 16); break;
                    case "key_stereo": ScanStereo = Convert.ToInt32(v, 16); break;
                    case "sensitivity_x": SensX = int.Parse(v); break;
                    case "sensitivity_y": SensY = int.Parse(v); break;
                    case "invert_y": InvertY = v == "1"; break;
                    case "noclip_speed": NoclipSpeed = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "key_freelook": ScanFreelook = Convert.ToInt32(v, 16); break;
                    case "key_noclip": ScanNoclip = Convert.ToInt32(v, 16); break;
                    case "key_distance": ScanDistance = Convert.ToInt32(v, 16); if (ScanDistance == 0x17) ScanDistance = 0x24; break;   // old default I = infrared
                    case "sound": Sound = v != "0"; break;
                    case "game_dir": GameDir = v; break;
                }
            }
            catch { Console.WriteLine("Ignored bad setting: " + l); }
        }
        if (!haveDisplay) Display = oldHd ? 2 : oldForce ? 1 : 0;   // ini of v1.0.x
        ApplyDisplay();
    }

    static void SaveSettings()
    {
        try
        {
            File.WriteAllText(IniPath,
                "; Terra Nova Plus settings (the menu at start-up edits the first part)\r\n" +
                "; ORIGINAL, 320X400 or HD (HD 640x400 falls back to 320x400 where it is not available)\r\ndisplay = " + DISPLAY_NAMES[Display] + "\r\n" +
                "; GAME, DEMO 1 or DEMO 2\r\nlaunch = " + TARGET_NAMES[LaunchTarget] + "\r\n" +
                "freelook = " + (OptFreelook ? 1 : 0) + "\r\n" +
                "noclip = " + (OptNoclip ? 1 : 0) + "\r\n" +
                "; NORMAL, FAR or MAX\r\nview_distance = " + DIST_NAMES[OptDistance] + "\r\n" +
                "; GAME, SHARP or SHARPER (more terrain detail in the distance)\r\nterrain_detail = " + DETAIL_NAMES[OptDetail] + "\r\n" +
                "widescreen = " + (OptWide ? 1 : 0) + "\r\n" +
                "; 1 = swap the Sound Blaster stereo in DOSBox (the game's SB16 driver reverses left and right)\r\nstereo_fix = " + (OptStereoFix ? 1 : 0) + "\r\n" +
                "; ROLAND, FM or GAME'S OWN (music of the game and demos, written in their TN.CFG at launch)\r\nmusic = " + MUSIC_NAMES[OptMusic] + "\r\n" +
                "; HD smoothing at start (toggled in game with key_smoothing)\r\nhd_smoothing = " + (HdSmoothing ? 1 : 0) + "\r\n" +
                "; HD sharp HUD at start: Scale2x on the cockpit art (toggled in game with key_sharp_hud)\r\nhd_sharp_hud = " + (HdHudFilter ? 1 : 0) + "\r\n" +
                "; mouse sensitivity (heading / pitch units per mouse count), 1 = inverted vertical look\r\n" +
                "sensitivity_x = " + SensX + "\r\nsensitivity_y = " + SensY + "\r\ninvert_y = " + (InvertY ? 1 : 0) + "\r\n" +
                "; noclip speed in game units per second\r\nnoclip_speed = " + NoclipSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                "; keys as PHYSICAL key scancodes (hex): 15 = Y, 16 = U, 24 = J (QWERTY/AZERTY),\r\n" +
                "; 29 = key left of 1, 3B..44 = F1..F10 (41 = F7), 57/58 = F11/F12\r\n" +
                "key_freelook = " + ScanFreelook.ToString("X2") + "\r\nkey_noclip = " + ScanNoclip.ToString("X2") + "\r\n" +
                "key_distance = " + ScanDistance.ToString("X2") + "\r\nkey_smoothing = " + ScanSmoothing.ToString("X2") + "\r\n" +
                "key_sharp_hud = " + ScanHudFilter.ToString("X2") + "\r\nkey_stereo = " + ScanStereo.ToString("X2") + "\r\n" +
                "; 0 = no beeps\r\nsound = " + (Sound ? 1 : 0) + "\r\n" +
                "; game folder (empty = auto-detect GOG / Steam)\r\ngame_dir = " + GameDir + "\r\n");
        }
        catch { }
    }
}
