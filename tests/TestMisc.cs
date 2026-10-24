// Terra Nova Plus tests - presets, display choices (console menu and GUI), the option name tables and the small pure
// helpers (TNPlus.cs, TNPlusGui.cs); on made-up game memory: player entry, frame counter, speed fix report, adaptive terrain.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ================================================================ helpers (Misc*)
        // what the tool printed (Say...) while a runs; also passed on to the runner's console (shown on a failure)
        static string MiscSaid(Action a)
        {
            TextWriter old = Console.Out;
            StringWriter sw = new StringWriter();
            Console.SetOut(sw);
            try { a(); }
            finally { Console.SetOut(old); old.Write(sw.ToString()); }
            return sw.ToString();
        }

        // the settings a preset sets, as one comparable line
        static string MiscPresetState()
        {
            return "Display=" + Display + " OptWide=" + OptWide + " OptWideTrue=" + OptWideTrue + " OptHd720=" + OptHd720 +
                " OptDetail=" + OptDetail + " OptDistance=" + OptDistance + " OptFreelook=" + OptFreelook;
        }

        // the settings no preset touches, as one comparable line
        static string MiscOtherState()
        {
            return "noclip=" + OptNoclip + " stereo=" + OptStereoFix + " scaling=" + OptScaling + " hit=" + OptHitFix + " phys=" + OptPhysFix +
                " smoke=" + OptThickSmoke + " obj=" + OptObjDist + " fov=" + OptFov + " music=" + OptMusic + " launch=" + LaunchTarget +
                " cycles=" + CpuCycles + " widehud=" + OptWideHud + " tfps=" + OptTerrainFps + " cells=" + OptSmoothCells +
                " fpsc=" + OptFpsCounter + " speed=" + OptSpeedFix + " edge=" + OptEdge + " small=" + OptSmallViews +
                " keys=" + ScanFreelook + "," + ScanNoclip + "," + ScanDistance + "," + ScanStereo + " sens=" + SensX + "," + SensY +
                " inv=" + InvertY + " ncs=" + NoclipSpeed + " attach=" + OptWideAttach + " hd=" + OptHD + " f400=" + OptForce400;
        }

        // a palette entry (8-bit RGB) of the game's palette in made-up memory
        static void MiscPal(FakeGame g, uint pal, int i, int r, int gr, int b)
        {
            g.Put(pal + (uint)(3 * i), new byte[] { (byte)r, (byte)gr, (byte)b });
        }

        // AdaptTerrain's conditions all met: SHARPER, prepared exe (far terrain), smooth buffers, HD running, rings found
        static void MiscAdaptOn()
        {
            HdPayload.Use(true, true, true);
            OptDetail = 2; OptTerrainFps = 60; OptSmoothCells = 0;
            farTerrain = true; smoothState = 1; hdState = 1; haveDetail = true;
            aTerrain = 0x200000;                    // zeros: FixDetail finds no ring layout and writes nothing
            adaptCells = 0; adaptT = -1; adaptHold = 0; adaptNoGrow = 0; adaptFrames = 0; adaptGood = 0;
        }

        // one AdaptTerrain call at time now with the game's frame counter at frames
        static void MiscAdapt(FakeGame g, double now, int frames)
        {
            g.PutInt(HdPayload.Frames, frames);
            AdaptTerrain(now);
        }

        // the game's 6-ring layout of the terrain camera (as FixDetail expects it), ring ends 4 / 12 / 30 / 64 / 100 / 240
        static void MiscRings(FakeGame g, uint at)
        {
            int[] types = { 1, 2, 0, 0, 0, 0 }, steps = { 1, 1, 1, 2, 4, 8 }, ends = { 4, 12, 30, 64, 100, 240 };
            g.PutShort(at + 0x58, 2); g.PutShort(at + 0x5A, 6);
            int from = 0;
            for (int k = 0; k < 6; k++)
            {
                uint o = at + 0x5C + 12 * (uint)k;
                g.Put(o, new byte[] { (byte)types[k] });
                g.PutShort(o + 1, (short)from); g.PutShort(o + 3, (short)ends[k]); g.PutShort(o + 5, (short)steps[k]);
                g.PutShort(o + 7, (short)from); g.PutShort(o + 9, (short)ends[k]);
                from = ends[k];
            }
        }

        // ================================================================ presets
        [Test] static void Misc_Preset_DefaultsAreBest()
        {
            Eq(2, CurrentPreset(), "the defaults are the BEST preset (TNPlus.cs: \"defaults = preset BEST\")");
            Eq("BEST", PRESET_NAMES[2], "PRESET_NAMES[2]");
            Eq(3, WideMode(), "BEST: true 16:9 in 1280x720");
        }

        [Test] static void Misc_Preset_ValuesOfEachPreset()
        {
            string[] want = {
                "Display=0 OptWide=False OptWideTrue=False OptHd720=False OptDetail=0 OptDistance=0 OptFreelook=False",
                "Display=1 OptWide=False OptWideTrue=False OptHd720=False OptDetail=1 OptDistance=1 OptFreelook=True",
                "Display=2 OptWide=True OptWideTrue=True OptHd720=True OptDetail=2 OptDistance=1 OptFreelook=True" };
            for (int p = 0; p < 3; p++)
            {
                ApplyPreset(p);
                Eq(want[p], MiscPresetState(), PRESET_NAMES[p] + ": settings");
            }
            ApplyPreset(0); Eq(0, WideMode(), "ORIGINAL: widescreen OFF");
            ApplyPreset(1); Eq(0, WideMode(), "CLASSIC+: widescreen OFF");
            ApplyPreset(2); Eq(3, WideMode(), "BEST: widescreen TRUE 720");
        }

        [Test] static void Misc_Preset_EachRecognizedRightAfterApply()
        {
            // from the defaults, from each other preset and from a custom mix: the preset applied is the one recognized
            for (int from = -1; from < 4; from++)
                for (int p = 0; p < 3; p++)
                {
                    if (from >= 0 && from < 3) ApplyPreset(from);
                    else if (from == 3) { Display = 1; SetWideMode(2); OptDetail = 0; OptDistance = 1; OptFreelook = false; }
                    ApplyPreset(p);
                    Eq(p, CurrentPreset(), "ApplyPreset(" + p + ") after " + (from < 0 ? "the defaults" : from == 3 ? "a custom mix" : PRESET_NAMES[from]));
                }
        }

        [Test] static void Misc_Preset_AnyChangeOfItsSettingsGivesCustom()
        {
            string[] what = { "Display", "OptWide", "OptWideTrue", "OptHd720", "OptDetail", "OptDistance", "OptFreelook" };
            Action[] change = {
                delegate { Display = (Display + 1) % 3; },
                delegate { OptWide = !OptWide; },
                delegate { OptWideTrue = !OptWideTrue; },
                delegate { OptHd720 = !OptHd720; },
                delegate { OptDetail = (OptDetail + 1) % 3; },
                delegate { OptDistance = 1 - OptDistance; },
                delegate { OptFreelook = !OptFreelook; } };
            for (int p = 0; p < 3; p++)
                for (int c = 0; c < change.Length; c++)
                {
                    ApplyPreset(p);
                    change[c]();
                    Eq(-1, CurrentPreset(), PRESET_NAMES[p] + " then " + what[c] + " changed: CUSTOM");
                    if ((c >= 1 && c <= 3) || c == 6)
                    {
                        change[c]();                // a switch flipped back: the preset again
                        Eq(p, CurrentPreset(), PRESET_NAMES[p] + " then " + what[c] + " flipped twice");
                    }
                }
        }

        [Test] static void Misc_Preset_CustomMixes()
        {
            Display = 2; SetWideMode(0); OptDetail = 2; OptDistance = 1; OptFreelook = true;
            Eq(-1, CurrentPreset(), "HD 640x400 without 16:9 (BEST otherwise)");
            Display = 2; SetWideMode(2);
            Eq(-1, CurrentPreset(), "HD 848x480 (BEST has 1280x720)");
            Display = 2; SetWideMode(1);
            Eq(-1, CurrentPreset(), "HD with STRETCHED 16:9");
            ApplyPreset(1); OptDistance = 0;
            Eq(-1, CurrentPreset(), "CLASSIC+ with NORMAL view distance");
            ApplyPreset(0); OptDistance = 1;
            Eq(-1, CurrentPreset(), "ORIGINAL with FAR view distance");
        }

        [Test] static void Misc_Preset_OtherSettingsIgnoredAndKept()
        {
            OptNoclip = true; OptStereoFix = true; OptScaling = 0; OptHitFix = false; OptPhysFix = false; OptThickSmoke = true;
            OptObjDist = 0; OptFov = 3; OptMusic = 1; LaunchTarget = 2; CpuCycles = 700000; OptWideHud = false; OptTerrainFps = 0;
            OptSmoothCells = 30; OptFpsCounter = false; OptSpeedFix = false; OptEdge = false; OptSmallViews = false;
            ScanFreelook = 0x23; ScanNoclip = 0x31; ScanDistance = 0x25; ScanStereo = 0x42; SensX = 3; SensY = 4; InvertY = true;
            NoclipSpeed = 7.5; OptWideAttach = true; OptHD = true; OptForce400 = false;
            string others = MiscOtherState();
            Eq(2, CurrentPreset(), "the preset settings still at their defaults: BEST, whatever the rest");
            for (int p = 0; p < 3; p++)
            {
                ApplyPreset(p);
                Eq(p, CurrentPreset(), PRESET_NAMES[p]);
                Eq(others, MiscOtherState(), PRESET_NAMES[p] + ": the settings outside the presets are left alone (OptHD / OptForce400 included: ApplyDisplay sets them)");
            }
        }

        [Test] static void Misc_Preset_ShowsInEveryDisplayList()
        {
            int[] gui = { 0, 1, 4 };
            for (int p = 0; p < 3; p++)
            {
                ApplyPreset(p);
                Ok(Display < DISPLAY_NAMES.Length && OptDetail < DETAIL_NAMES.Length && OptDistance < DIST_NAMES.Length && WideMode() < WIDE_NAMES.Length,
                    PRESET_NAMES[p] + ": every value inside its name table");
                Eq(gui[p], GuiDisplay(), PRESET_NAMES[p] + ": GUI display entry");
            }
            Eq("HD 1280x720", GUI_DISPLAY[GuiDisplay()], "BEST in the GUI");
            Eq("HD 1280x720", HdName(), "BEST in the console menu");
            Has(PRESET_INFO[2], "1280x720", "PRESET_INFO of BEST says what it sets");
            Eq(PRESET_NAMES.Length, PRESET_INFO.Length, "a description per preset");
        }

        // ================================================================ widescreen modes
        [Test] static void Misc_WideMode_RoundTrip()
        {
            Eq(4, WIDE_NAMES.Length, "WIDE_NAMES: OFF, STRETCHED, TRUE, TRUE 720");
            for (int from = 0; from < 4; from++)
                for (int m = 0; m < 4; m++)
                {
                    SetWideMode(from); SetWideMode(m);
                    Eq(m, WideMode(), WIDE_NAMES[m] + " set after " + WIDE_NAMES[from]);
                }
            Eq("OFF", WIDE_NAMES[0], "0"); Eq("STRETCHED", WIDE_NAMES[1], "1"); Eq("TRUE", WIDE_NAMES[2], "2"); Eq("TRUE 720", WIDE_NAMES[3], "3");
        }

        [Test] static void Misc_WideMode_Flags()
        {
            bool[][] want = { new[] { false, false, false }, new[] { true, false, false }, new[] { true, true, false }, new[] { true, true, true } };
            for (int m = 0; m < 4; m++)
            {
                SetWideMode(m);
                Eq(want[m][0], OptWide, WIDE_NAMES[m] + ": OptWide");
                Eq(want[m][1], OptWideTrue, WIDE_NAMES[m] + ": OptWideTrue");
                Eq(want[m][2], OptHd720, WIDE_NAMES[m] + ": OptHd720");
            }
            // outside 0-3 (never asked by the menu, the GUI or the ini): below is OFF, above 3 is TRUE (m >= 2 but not 3)
            SetWideMode(-1); Eq(0, WideMode(), "SetWideMode(-1)");
            SetWideMode(4); Eq(2, WideMode(), "SetWideMode(4)");
        }

        [Test] static void Misc_WideMode_FromFlags()
        {
            OptWide = false; OptWideTrue = true; OptHd720 = true;
            Eq(0, WideMode(), "16:9 off: the true / 720 flags do not count");
            OptWide = true; OptWideTrue = false; OptHd720 = true;
            Eq(1, WideMode(), "stretched: the 720 flag does not count");
            OptWideTrue = true; OptHd720 = false;
            Eq(2, WideMode(), "true 848x480");
        }

        // ================================================================ GUI display list
        [Test] static void Misc_GuiDisplay_EachEntryRoundTrip()
        {
            Eq(5, GUI_DISPLAY.Length, "GUI_DISPLAY: the 3 displays + the two true 16:9 HD");
            for (int from = 0; from < 4; from++)
                for (int i = 0; i < 5; i++)
                {
                    SetWideMode(from);
                    SetGuiDisplay(i);
                    string what = GUI_DISPLAY[i] + " picked with widescreen " + WIDE_NAMES[from];
                    Eq(i, GuiDisplay(), what + ": GuiDisplay");
                    Eq(Math.Min(i, 2), Display, what + ": Display");
                    // 848x480 = TRUE, 1280x720 = TRUE 720; the others leave true 16:9 (to OFF) and keep OFF / STRETCHED
                    int wide = i == 3 ? 2 : i == 4 ? 3 : from >= 2 ? 0 : from;
                    Eq(wide, WideMode(), what + ": widescreen");
                }
        }

        [Test] static void Misc_GuiDisplay_LabelsMatchTheConsoleMenu()
        {
            Eq(DISPLAY_LABELS[0], GUI_DISPLAY[0], "Original");
            Eq(DISPLAY_LABELS[1], GUI_DISPLAY[1], "Sharp 320x400");
            for (int i = 2; i < 5; i++)
            {
                SetGuiDisplay(i);
                Eq(GUI_DISPLAY[i], HdName(), "the GUI's entry " + i + " is the menu's HD name for these settings");
                Eq(GUI_DISPLAY[i], DisplayLabels()[2], "DisplayLabels()[2] for entry " + i);
            }
        }

        [Test] static void Misc_GuiDisplay_FromSettings()
        {
            Display = 0; SetWideMode(3);
            Eq(0, GuiDisplay(), "ORIGINAL with TRUE 720 in the ini: Original");
            Display = 1; SetWideMode(2);
            Eq(1, GuiDisplay(), "320X400 with TRUE in the ini: Sharp 320x400");
            Display = 2; SetWideMode(1);
            Eq(2, GuiDisplay(), "HD with STRETCHED: HD 640x400");
            Display = 2; SetWideMode(0);
            Eq(2, GuiDisplay(), "HD without 16:9: HD 640x400");
        }

        // ================================================================ ApplyDisplay / EffectiveDisplay
        [Test] static void Misc_ApplyDisplay_OriginalAnd320x400()
        {
            bool[][] want = { new[] { false, false }, new[] { false, true } };   // OptHD, OptForce400
            for (int d = 0; d < 2; d++)
            {
                Display = d; OptHD = true; OptForce400 = !want[d][1];
                Eq(d, EffectiveDisplay(), DISPLAY_NAMES[d] + ": EffectiveDisplay");
                ApplyDisplay();
                Eq(want[d][0], OptHD, DISPLAY_NAMES[d] + ": OptHD");
                Eq(want[d][1], OptForce400, DISPLAY_NAMES[d] + ": OptForce400");
                Eq(d, Display, DISPLAY_NAMES[d] + ": Display kept");
            }
        }

        [Test] static void Misc_ApplyDisplay_HdUnavailableIs320x400()
        {
            GameDir = TestDir("game");              // no TNOVA\__FF.EXE: HD unavailable
            Display = 2; OptHD = true; OptForce400 = false;
            Eq(1, EffectiveDisplay(), "HD unavailable: 320x400");
            Eq("Sharp 320x400", DisplayLabels()[EffectiveDisplay()], "the console menu's Display line");
            ApplyDisplay();
            No(OptHD, "OptHD"); Ok(OptForce400, "OptForce400");
            Eq(2, Display, "the HD choice is kept (saved as HD)");
        }

        [Test] static void Misc_ApplyDisplay_HdAvailable()
        {
            // the check of __FF.EXE is cached per game folder: an already checked, supported folder
            GameDir = TestDir("game"); hdCheckedDir = GameDir; hdOk = true; hdWhy = "";
            Display = 2; OptHD = false; OptForce400 = false;
            Eq(2, EffectiveDisplay(), "HD available");
            ApplyDisplay();
            Ok(OptHD, "OptHD"); Ok(OptForce400, "OptForce400 (HD includes 320x400)");
            LaunchTarget = 1;
            Eq(1, EffectiveDisplay(), "a demo: never HD, even with the game's exe supported");
            ApplyDisplay();
            No(OptHD, "demo: OptHD"); Ok(OptForce400, "demo: OptForce400");
            LaunchTarget = 0; Display = 0;
            ApplyDisplay();
            No(OptHD, "back to ORIGINAL: OptHD"); No(OptForce400, "back to ORIGINAL: OptForce400");
        }

        // ================================================================ HD names
        [Test] static void Misc_HdName_PerWideMode()
        {
            string[] want = { "HD 640x400", "HD 640x400", "HD 848x480", "HD 1280x720" };
            for (int m = 0; m < 4; m++)
            {
                SetWideMode(m);
                Eq(want[m], HdName(), "HdName with widescreen " + WIDE_NAMES[m]);
                string[] l = DisplayLabels();
                Eq(3, l.Length, "DisplayLabels: one per display");
                Eq("Original", l[0], "label 0"); Eq("Sharp 320x400", l[1], "label 1"); Eq(want[m], l[2], "label 2");
            }
        }

        [Test] static void Misc_HdHelp_PerWideMode()
        {
            string[] start = { "HD 640x400: the 3D view drawn at twice the width", "HD 640x400: the 3D view drawn at twice the width",
                "HD 848x480 (true 16:9): the 3D view drawn 848 columns wide", "HD 1280x720 (true 16:9): the 3D view drawn 1280 columns wide and 1.5 x taller, needs a fast PC" };
            for (int m = 0; m < 4; m++)
            {
                SetWideMode(m);
                Eq(start[m] + ", GOG and Steam, French and English", HdHelp(), "HdHelp with widescreen " + WIDE_NAMES[m]);
                Ok(HdHelp().StartsWith(HdName()), "HdHelp starts with HdName (" + WIDE_NAMES[m] + ")");
            }
        }

        [Test] static void Misc_HdNow_SaysWhatRuns()
        {
            Eq("HD 1280x720", HdName(), "defaults: the setting is 1280x720");
            Eq("HD 640x400", HdNow(), "defaults: OptHD off (ApplyDisplay not run), no game: what runs is 640x400");
            OptHD = true; OptWideAttach = true;
            SetWideMode(3); Eq("HD 1280x720", HdNow(), "true 16:9 720 running");
            SetWideMode(2); Eq("HD 848x480", HdNow(), "true 16:9 running");
            SetWideMode(1); Eq("HD 640x400", HdNow(), "stretched 16:9: the HD view is 640x400");
            SetWideMode(0); Eq("HD 640x400", HdNow(), "no 16:9");
            SetWideMode(3); OptHD = false;
            Eq("HD 640x400", HdNow(), "TRUE 720 chosen but no HD: not true 16:9");
        }

        // ================================================================ TrueWideActive / WideActive
        [Test] static void Misc_TrueWideActive_AttachOption()
        {
            OptHD = true;
            for (int m = 0; m < 4; m++) { SetWideMode(m); No(TrueWideActive(), "no game launched, no wide_attach (" + WIDE_NAMES[m] + ")"); }
            OptWideAttach = true;
            bool[] want = { false, false, true, true };
            for (int m = 0; m < 4; m++) { SetWideMode(m); Eq(want[m], TrueWideActive(), "wide_attach, HD, " + WIDE_NAMES[m]); }
            OptHD = false;
            No(TrueWideActive(), "wide_attach, TRUE 720 without HD");
        }

        [Test] static void Misc_TrueWideActive_LaunchedGame()
        {
            Process me = Process.GetCurrentProcess();   // stands for the DOSBox started from the menu (nothing is started)
            OptHD = true; SetWideMode(2);
            launched = me; gamePid = me.Id; launchedTrue = true;
            Ok(TrueWideActive(), "DOSBox started by the tool for true 16:9, attached to it");
            gamePid = me.Id + 1;
            No(TrueWideActive(), "attached to another DOSBox");
            gamePid = me.Id; launchedTrue = false;
            No(TrueWideActive(), "DOSBox started without the VESA 848x480 overlay");
            launchedTrue = true; launched = null;
            No(TrueWideActive(), "the started DOSBox has closed");
        }

        [Test] static void Misc_WideActive_Stretched()
        {
            OptWideAttach = true;
            SetWideMode(1);
            No(WideActive(), "16:9 code not found in the game (haveWide false)");
            haveWide = true;
            Ok(WideActive(), "STRETCHED, wide_attach, found");
            SetWideMode(0);
            No(WideActive(), "widescreen OFF");
            SetWideMode(2); OptHD = true;
            No(WideActive(), "TRUE with HD: the true 16:9 takes over");
            OptHD = false;
            Ok(WideActive(), "TRUE without HD: the stretched camera correction instead");
            SetWideMode(1); OptWideAttach = false;
            No(WideActive(), "attached, no wide_attach: no 16:9 (DOSBox was not started stretched)");
        }

        [Test] static void Misc_WideActive_LaunchedGame()
        {
            Process me = Process.GetCurrentProcess();
            haveWide = true; SetWideMode(1);
            launched = me; gamePid = me.Id; launchedWide = true;
            Ok(WideActive(), "DOSBox started stretched by the tool");
            gamePid = me.Id + 1;
            No(WideActive(), "another DOSBox");
            gamePid = me.Id; launchedWide = false;
            No(WideActive(), "DOSBox started without the 16:9 overlay");
        }

        // ================================================================ field of view
        [Test] static void Misc_FovDegrees()
        {
            int[] want = { 0, 90, 100, 110 };
            Eq(want.Length, FOV_NAMES.Length, "FOV_NAMES");
            Eq("GAME", FOV_NAMES[0], "FOV_NAMES[0]");
            for (int i = 0; i < FOV_NAMES.Length; i++)
            {
                OptFov = i;
                Eq(want[i], FovDegrees(), "field_of_view = " + FOV_NAMES[i]);
                if (i > 0) Ok(FovDegrees() > 84.5, "wider than the game's 84.5 degrees");
            }
        }

        // ================================================================ smooth terrain
        [Test] static void Misc_SmoothCells_PerDetail()
        {
            int[] nearWant = { 0, 18, 24 }, farWant = { 0, 32, 48 };
            for (int d = 0; d < 3; d++)
            {
                OptDetail = d;
                farTerrain = false; Eq(nearWant[d], SmoothCells(), DETAIL_NAMES[d] + ", in-memory buffers (exe not prepared)");
                farTerrain = true; Eq(farWant[d], SmoothCells(), DETAIL_NAMES[d] + ", prepared exe");
            }
        }

        [Test] static void Misc_SmoothCells_ForcedByTheIni()
        {
            OptDetail = 2;
            int[] forced = { 10, 24, 30, 48, 100 }, nearWant = { 10, 24, 24, 24, 24 }, farWant = { 10, 24, 30, 48, 48 };
            for (int i = 0; i < forced.Length; i++)
            {
                OptSmoothCells = forced[i];
                farTerrain = false; Eq(nearWant[i], SmoothCells(), "smooth_cells = " + forced[i] + ", near: capped at 24");
                farTerrain = true; Eq(farWant[i], SmoothCells(), "smooth_cells = " + forced[i] + ", far: capped at 48");
            }
            OptSmoothCells = 30; adaptCells = 24; farTerrain = true;
            Eq(30, SmoothCells(), "smooth_cells wins over the adaptive ring");
            OptSmoothCells = 10; OptDetail = 0;
            Eq(10, SmoothCells(), "smooth_cells applies whatever the detail");
            OptSmoothCells = -5; OptDetail = 2; adaptCells = 0;
            Eq(48, SmoothCells(), "a negative smooth_cells is ignored");
        }

        [Test] static void Misc_SmoothCells_Adaptive()
        {
            OptDetail = 2; farTerrain = true;
            adaptCells = 40; Eq(40, SmoothCells(), "adaptive ring at 40");
            adaptCells = 24; Eq(24, SmoothCells(), "adaptive ring at 24");
            adaptCells = 56; Eq(48, SmoothCells(), "never past the detail's own end");
            farTerrain = false; adaptCells = 40;
            Eq(24, SmoothCells(), "in-memory buffers: 24 at most");
            OptDetail = 1; farTerrain = true;
            Eq(32, SmoothCells(), "SHARP far: 32 at most");
            adaptCells = 0; OptDetail = 2;
            Eq(48, SmoothCells(), "not adapting: the table");
        }

        [Test] static void Misc_Tables_DetailRingsAndSmoothEnds()
        {
            Eq(3, DETAIL_NAMES.Length, "DETAIL_NAMES");
            Eq(DETAIL_NAMES.Length, DETAIL_RINGS.Length, "DETAIL_RINGS: one per detail");
            Eq(DETAIL_NAMES.Length, SMOOTH_TO.Length, "SMOOTH_TO: one per detail");
            Eq(DETAIL_NAMES.Length, SMOOTH_FAR.Length, "SMOOTH_FAR: one per detail");
            Ok(DETAIL_RINGS[0] == null, "GAME: the game's own rings");
            Eq(0, SMOOTH_TO[0], "GAME: no smooth ring (near)"); Eq(0, SMOOTH_FAR[0], "GAME: no smooth ring (far)");
            for (int d = 1; d < 3; d++)
            {
                int[] r = DETAIL_RINGS[d];
                Eq(4, r.Length, DETAIL_NAMES[d] + ": 4 rings (full, 1 in 2, 4, 8)");
                for (int k = 1; k < 4; k++) Ok(r[k] > r[k - 1], DETAIL_NAMES[d] + ": ring ends increasing");
                Ok(r[3] < 32768, DETAIL_NAMES[d] + ": fits the game's 16-bit words");
                Ok(SMOOTH_TO[d] > 12 && SMOOTH_TO[d] < SMOOTH_FAR[d], DETAIL_NAMES[d] + ": the game 12 < near < far");
                Ok(SMOOTH_TO[d] <= 31, DETAIL_NAMES[d] + ": near ring within the 64x64 vertex map (31 cells)");
                Ok(SMOOTH_FAR[d] <= 48, DETAIL_NAMES[d] + ": far ring within the prepared buffers (48 cells)");
                // FixDetail chains the rings: the smooth ring must end before the first detail ring
                Ok(SMOOTH_FAR[d] < r[0], DETAIL_NAMES[d] + ": smooth ring (" + SMOOTH_FAR[d] + ") ends before the first ring (" + r[0] + ")");
            }
            Eq(24, SMOOTH_TO[2], "SHARPER near = the near cap of smooth_cells and the adaptive minimum");
            Eq(48, SMOOTH_FAR[2], "SHARPER far = the far cap of smooth_cells and the adaptive maximum");
            Eq(0, (SMOOTH_FAR[2] - SMOOTH_TO[2]) % 8, "the adaptive steps of 8 cells land on both ends");
        }

        [Test] static void Misc_Tables_ViewDistance()
        {
            Eq(2, DIST_NAMES.Length, "DIST_NAMES: NORMAL, FAR");
            Eq(DIST_NAMES.Length, DIST_R.Length, "DIST_R: one fog radius per view distance");
            Eq(0, DIST_R[0], "NORMAL: the game's own weather radius");
            Eq(180, DIST_R[1], "FAR");
            Ok(DIST_R[1] >= 20 && DIST_R[1] <= 2000, "FAR within the radii the fog fix accepts from the game");
        }

        // ================================================================ option name tables
        [Test] static void Misc_Names_LengthsMatchTheOptionRanges()
        {
            // the console menu cycles with literal moduli: '1' % 3, '2' % 4, '3' % 3, 'O' % 3, 'L' % 3, '8' % 4 (or 3)
            Eq(3, DISPLAY_NAMES.Length, "DISPLAY_NAMES"); Eq(3, DISPLAY_LABELS.Length, "DISPLAY_LABELS");
            Eq(4, WIDE_NAMES.Length, "WIDE_NAMES"); Eq(3, DETAIL_NAMES.Length, "DETAIL_NAMES");
            Eq(3, OBJDIST_NAMES.Length, "OBJDIST_NAMES"); Eq(3, TARGET_NAMES.Length, "TARGET_NAMES");
            Eq(4, MUSIC_NAMES.Length, "MUSIC_NAMES"); Eq(2, SCALING_NAMES.Length, "SCALING_NAMES");
            Eq(3, PRESET_NAMES.Length, "PRESET_NAMES (P cycles % 3)");
            Eq(MUSIC_NAMES.Length, MUSIC_INFO.Length, "MUSIC_INFO: one per music");
            Eq(TARGET_NAMES.Length, TARGET_DIRS.Length, "TARGET_DIRS: one per launch target");
            Eq(4, KEY_WHAT.Length, "KEY_WHAT: freelook, noclip, view distance, stereo swap");
            Eq("AWE32", MUSIC_NAMES[3], "the music only offered with awe32.raw is the last one");
            Eq("TNOVA", TARGET_DIRS[0], "the full game's folder");
        }

        [Test] static void Misc_Names_ReadBackByLoadSettings()
        {
            // LoadSettings looks the trimmed, upper-cased value up in these tables: each name must be found again
            string[] tables = { "DIST_NAMES", "DETAIL_NAMES", "DISPLAY_NAMES", "TARGET_NAMES", "SCALING_NAMES", "MUSIC_NAMES", "FOV_NAMES", "OBJDIST_NAMES" };
            string[][] names = { DIST_NAMES, DETAIL_NAMES, DISPLAY_NAMES, TARGET_NAMES, SCALING_NAMES, MUSIC_NAMES, FOV_NAMES, OBJDIST_NAMES };
            for (int t = 0; t < names.Length; t++)
                for (int i = 0; i < names[t].Length; i++)
                {
                    string n = names[t][i], what = tables[t] + "[" + i + "] " + Show(n);
                    Ok(n.Length > 0, what + ": not empty");
                    Eq(n.Trim(), n, what + ": no space at the ends (the ini value is trimmed)");
                    Eq(n.ToUpperInvariant(), n, what + ": upper case (the ini value is upper-cased)");
                    Ok(n.IndexOfAny(new[] { '\r', '\n', ';' }) < 0, what + ": one ini line");
                    Eq(i, Array.IndexOf(names[t], n.ToUpperInvariant()), what + ": found back at its own index");
                }
        }

        [Test] static void Misc_Names_DefaultsInRange()
        {
            Ok(Display >= 0 && Display < DISPLAY_NAMES.Length, "Display " + Display);
            Ok(LaunchTarget >= 0 && LaunchTarget < TARGET_NAMES.Length, "LaunchTarget " + LaunchTarget);
            Ok(OptDistance >= 0 && OptDistance < DIST_NAMES.Length, "OptDistance " + OptDistance);
            Ok(OptDetail >= 0 && OptDetail < DETAIL_NAMES.Length, "OptDetail " + OptDetail);
            Ok(OptScaling >= 0 && OptScaling < SCALING_NAMES.Length, "OptScaling " + OptScaling);
            Ok(OptMusic >= 0 && OptMusic < MUSIC_NAMES.Length, "OptMusic " + OptMusic);
            Ok(OptFov >= 0 && OptFov < FOV_NAMES.Length, "OptFov " + OptFov);
            Ok(OptObjDist >= 0 && OptObjDist < OBJDIST_NAMES.Length, "OptObjDist " + OptObjDist);
            Ok(WideMode() >= 0 && WideMode() < WIDE_NAMES.Length, "WideMode " + WideMode());
            Ok(GuiDisplay() >= 0 && GuiDisplay() < GUI_DISPLAY.Length, "GuiDisplay " + GuiDisplay());
            Eq(4, Array.IndexOf(CYCLE_CHOICES, CpuCycles), "the default CPU cycles (1800000) are a choice of the GUI");
        }

        [Test] static void Misc_Names_CycleChoicesAndSpeedNames()
        {
            Eq(CYCLE_CHOICES.Length, SPEED_NAMES.Length, "a GUI name per CPU speed choice");
            int last = CYCLE_CHOICES.Length - 1;
            for (int i = 0; i < last; i++)
            {
                Eq(CYCLE_CHOICES[i].ToString(), SPEED_NAMES[i], "SPEED_NAMES[" + i + "]");
                if (i > 0) Ok(CYCLE_CHOICES[i] > CYCLE_CHOICES[i - 1], "cycles increasing at " + i);
            }
            Eq(0, CYCLE_CHOICES[last], "the last choice: 0 = the edition's own setting");
            Eq("GAME'S OWN", SPEED_NAMES[last], "its name");
        }

        // ================================================================ fog table
        [Test] static void Misc_FogTable_ShapeAndBounds()
        {
            foreach (double r in new double[] { 20, 37.5, 60, 180, 600, 2000 })
            {
                byte[] t = FogTable(r);
                Eq(1024, t.Length, "r " + r + ": length");
                for (int i = 0; i <= 20; i++) Eq((byte)0, t[i], "r " + r + ": no fog up to index 20 (index " + i + ")");
                for (int i = 1; i < 1024; i++)
                {
                    Ok(t[i] >= t[i - 1], "r " + r + ": non-decreasing at " + i);
                    Ok(t[i] <= 15, "r " + r + ": at most 15 at " + i);
                }
                Ok(t[21] <= 1, "r " + r + ": starts gently");
            }
            Eq((byte)15, FogTable(20)[1023], "small radius: full fog at the end");
        }

        [Test] static void Misc_FogTable_FarValues()
        {
            // 16 (1 - exp(-(i - 20) / r)) truncated: level k first reached at i = 20 + ceil(r ln(16 / (16 - k)))
            byte[] t = FogTable(180);
            int[] level = { 1, 2, 8, 14, 15 }, first = { 32, 45, 145, 395, 520 };
            for (int k = 0; k < level.Length; k++)
            {
                Eq((byte)(level[k] - 1), t[first[k] - 1], "FAR (180): just before level " + level[k]);
                Eq((byte)level[k], t[first[k]], "FAR (180): level " + level[k] + " first at " + first[k]);
            }
            Eq((byte)15, t[1023], "FAR: full fog at the end");
            byte[] t2 = FogTable(2000);
            Eq((byte)0, t2[149], "r 2000: index 149"); Eq((byte)1, t2[150], "r 2000: level 1 at 150");
            Eq((byte)6, t2[1023], "r 2000 (largest weather radius used): 6 at the end");
        }

        [Test] static void Misc_FogTable_LargerRadiusPushesFogBack()
        {
            double[] radii = { 20, 40, 60, 120, 180, 400, 2000 };
            int prevSum = int.MaxValue;
            for (int k = 1; k < radii.Length; k++)
            {
                byte[] a = FogTable(radii[k - 1]), b = FogTable(radii[k]);
                int sum = 0;
                for (int i = 0; i < 1024; i++)
                {
                    Ok(b[i] <= a[i], "r " + radii[k] + " never foggier than r " + radii[k - 1] + " (index " + i + ")");
                    sum += b[i];
                }
                Ok(sum < prevSum, "r " + radii[k] + ": less fog overall");
                prevSum = sum;
            }
        }

        // ================================================================ small helpers
        [Test] static void Misc_Hex_Parses()
        {
            EqBytes(new byte[] { 0x8B, 0x15, 0xC1 }, Hex("8B15c1"), "upper and lower case");
            EqBytes(new byte[0], Hex(""), "empty");
            byte[] all = new byte[256];
            for (int i = 0; i < 256; i++) all[i] = (byte)i;
            EqBytes(all, Hex(HexOf(all)), "every byte value round trip");
            EqBytes(new byte[] { 0xAB }, Hex("ABC"), "odd length: the last digit is ignored");
            Throws<FormatException>(delegate { Hex("ZZ"); }, "not hex");
        }

        [Test] static void Misc_Same()
        {
            byte[] a = { 1, 2, 3 };
            Ok(Same(a, a), "same array");
            Ok(Same(a, new byte[] { 1, 2, 3 }), "same bytes");
            No(Same(a, new byte[] { 1, 2, 4 }), "last byte differs");
            No(Same(a, new byte[] { 0, 2, 3 }), "first byte differs");
            No(Same(a, new byte[] { 1, 2 }), "shorter");
            No(Same(a, new byte[] { 1, 2, 3, 0 }), "longer");
            Ok(Same(new byte[0], new byte[0]), "both empty");
        }

        [Test] static void Misc_Slice()
        {
            byte[] b = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            byte[] s = Slice(b, 2, 3);
            EqBytes(new byte[] { 2, 3, 4 }, s, "Slice(b, 2, 3)");
            s[0] = 99;
            Eq((byte)2, b[2], "a copy: the source is not changed");
            EqBytes(new byte[] { 8, 9 }, Slice(b, 8, 2), "up to the end");
            EqBytes(new byte[0], Slice(b, 10, 0), "empty at the end");
            EqBytes(b, Slice(b, 0, 10), "all");
            Throws<ArgumentException>(delegate { Slice(b, 8, 3); }, "past the end");
            Throws<ArgumentException>(delegate { Slice(b, -1, 2); }, "negative start");
        }

        [Test] static void Misc_Wrap_Basic()
        {
            EqList(new[] { "one two", "three" }, Wrap("one two three", 7), "width 7");
            EqList(new[] { "aaa bbb" }, Wrap("aaa bbb", 7), "exactly the width fits");
            EqList(new[] { "aaa", "bbb" }, Wrap("aaa bbb", 6), "one less: two lines");
            EqList(new[] { "P picks a preset again" }, Wrap("P picks a preset again", 36), "the CUSTOM help in the menu");
        }

        [Test] static void Misc_Wrap_Edges()
        {
            Eq(0, Wrap("", 10).Count, "empty text: no line");
            EqList(new[] { "supercalifragilistic", "is", "long" }, Wrap("supercalifragilistic is long", 5), "a word longer than the width stays whole, alone");
            EqList(new[] { "a", "b", "c" }, Wrap("a b c", 1), "width 1");
            EqList(new[] { "a  b" }, Wrap("a  b", 10), "a double space is kept");
        }

        [Test] static void Misc_Wrap_PresetInfoInTheMenu()
        {
            for (int p = 0; p < PRESET_INFO.Length; p++)
            {
                List<string> l = Wrap(PRESET_INFO[p], 36);
                Ok(l.Count >= 1, PRESET_NAMES[p] + ": at least a line");
                foreach (string s in l) Ok(s.Length <= 36, PRESET_NAMES[p] + ": line " + Show(s) + " fits 36 columns");
                Eq(PRESET_INFO[p], string.Join(" ", l.ToArray()), PRESET_NAMES[p] + ": nothing lost");
            }
            Ok(Wrap(PRESET_INFO[2], 36).Count >= 2, "BEST's description needs two lines");
        }

        [Test] static void Misc_Shorten()
        {
            Eq("abc", Shorten("abc", 5), "shorter: unchanged");
            Eq("abcde", Shorten("abcde", 5), "exactly n: unchanged");
            Eq("...ij", Shorten("abcdefghij", 5), "longer: ... and the end");
            Eq("...", Shorten("abcdef", 3), "n = 3: the dots only");
            string dir = Path.Combine("Program Files (x86)", "GOG Galaxy", "Games", "Terra Nova Strike Force Centauri");
            string s = Shorten(dir, 50);
            Eq(50, s.Length, "the menu's game line: 50 characters");
            Ok(s.StartsWith("..."), "starts with ...");
            Ok(dir.EndsWith(s.Substring(3)), "keeps the end of the path");
        }

        [Test] static void Misc_OnOffAndLine()
        {
            Eq("[ON]", OnOff(true), "on"); Eq("[off]", OnOff(false), "off");
            Eq("  1  Display" + new string(' ', 43) + "[ON]", Line("1", "Display", "[ON]"), "label padded to 50");
            Eq(59, Line("1", "Display", "[ON]").Length, "2 + key + 2 + 50 + value");
            string longLabel = new string('x', 60);
            Eq("  K  " + longLabel + "v", Line("K", longLabel, "v"), "a longer label is not cut");
            Eq("  A  " + new string(' ', 50), Line("A", "", ""), "empty label and value");
        }

        [Test] static void Misc_GetKeySetKey()
        {
            int[] defaults = { 0x15, 0x16, 0x24, 0x41 };   // Y, U, J, F7
            for (int i = 0; i < 4; i++) Eq(defaults[i], GetKey(i), "default " + KEY_WHAT[i] + " key");
            for (int i = 0; i < 4; i++) SetKey(i, 0x30 + i);
            Eq(0x30, ScanFreelook, "SetKey(0): freelook"); Eq(0x31, ScanNoclip, "SetKey(1): noclip");
            Eq(0x32, ScanDistance, "SetKey(2): view distance"); Eq(0x33, ScanStereo, "SetKey(3): stereo swap");
            for (int i = 0; i < 4; i++) Eq(0x30 + i, GetKey(i), "GetKey(" + i + ") after SetKey");
            // any other index is the stereo key (the last one)
            Eq(0x33, GetKey(4), "GetKey(4)"); Eq(0x33, GetKey(-1), "GetKey(-1)");
            SetKey(9, 0x50);
            Eq(0x50, ScanStereo, "SetKey(9): stereo"); Eq(0x30, ScanFreelook, "SetKey(9): freelook untouched");
        }

        // ================================================================ game helpers (made-up memory)
        [Test] static void Misc_PlayerEntry_FromIdAndTable()
        {
            using (FakeGame g = new FakeGame())
            {
                aPlayerId = 0x1000; aMaster = 0x20000;
                g.Put(0x1000, "FFFF0300");         // the id is the high word (aPlayerId + 2): 3
                Eq(0x20000u + 3 * 0x25, PlayerEntry(), "entry of player 3 (0x25 bytes each)");
                g.Put(0x1002, "0000");
                Eq(0x20000u, PlayerEntry(), "player 0: the table itself");
                g.Put(0x1002, "FFFF");
                Eq(0x20000u + 0xFFFFu * 0x25, PlayerEntry(), "id 65535: unsigned");
            }
        }

        [Test] static void Misc_InMission_MapBounds()
        {
            using (FakeGame g = new FakeGame())
            {
                uint e = 0x30000;
                int[][] xy = {
                    new[] { 0, 0 }, new[] { 2 << 16, 2 << 16 }, new[] { 510 << 16, 510 << 16 }, new[] { 256 << 16, 100 << 16 },
                    new[] { (2 << 16) - 1, 256 << 16 }, new[] { 256 << 16, (510 << 16) + 1 }, new[] { -(256 << 16), 256 << 16 }, new[] { 256 << 16, 600 << 16 } };
                bool[] want = { false, true, true, true, false, false, false, false };
                for (int i = 0; i < xy.Length; i++)
                {
                    g.PutInt(e + 0x0f, xy[i][0]); g.PutInt(e + 0x13, xy[i][1]);
                    Eq(want[i], InMission(e), "x " + xy[i][0] / 65536.0 + ", y " + xy[i][1] / 65536.0 + " (16.16)");
                }
            }
        }

        [Test] static void Misc_AddHeading_WrapsOnSixteenBits()
        {
            using (FakeGame g = new FakeGame())
            {
                uint e = 0x30000;
                g.Put(e + 0x1a, "AA F0FF BB");      // a byte before, the heading 0xFFF0, a byte after
                AddHeading(e, 0x20);
                Eq((short)0x0010, g.GetShort(e + 0x1b), "0xFFF0 + 0x20 wraps to 0x0010");
                AddHeading(e, -0x30);
                Eq(unchecked((short)0xFFE0), g.GetShort(e + 0x1b), "0x0010 - 0x30 wraps to 0xFFE0");
                AddHeading(e, 0x10000 + 5);
                Eq(unchecked((short)0xFFE5), g.GetShort(e + 0x1b), "a full turn more is the same heading");
                Eq((byte)0xAA, g.Mem[e + 0x1a], "byte before untouched"); Eq((byte)0xBB, g.Mem[e + 0x1d], "byte after untouched");
            }
        }

        [Test] static void Misc_CameraAlive_LiveCamera()
        {
            using (FakeGame g = new FakeGame())
            {
                uint e = 0x30000;
                aCamHeading = e + 0x1b;             // the camera follows the player's heading at once
                g.PutShort(e + 0x1b, 1000);
                Stopwatch t = Stopwatch.StartNew();
                Ok(CameraAlive(e), "the camera followed the nudge");
                Ok(t.Elapsed.TotalSeconds < 0.9, "found before the 1 s limit (" + t.Elapsed.TotalSeconds + " s)");
                Eq((short)1000, g.GetShort(e + 0x1b), "the heading is put back");
            }
        }

        // ================================================================ frame counter (HD 1280x720)
        [Test] static void Misc_FpsCounter_FpsAndClosestGreen()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                uint pal = 0x44AE90;                // the English game's palette
                MiscPal(g, pal, 0, 96, 255, 96);    // index 0 is never used
                MiscPal(g, pal, 37, 100, 250, 90);
                MiscPal(g, pal, 50, 96, 255, 98);   // distance 4, twice: the first one wins
                MiscPal(g, pal, 60, 96, 255, 98);
                g.Put(HdPayload.FpsColour, "00AAAAAA");
                g.PutInt(HdPayload.Frames, 1000);
                FpsCounter(10.0);
                Eq(0, g.GetInt(HdPayload.FpsValue), "first call: the window starts, nothing shown");
                g.PutInt(HdPayload.Frames, 1030);
                FpsCounter(10.5);
                Eq(60, g.GetInt(HdPayload.FpsValue), "30 frames in 0.5 s");
                Eq((byte)50, g.Mem[HdPayload.FpsColour], "closest palette colour to (96, 255, 96), index 0 left out, first of a tie");
                EqBytes(Bytes("AAAAAA"), g.Get(HdPayload.FpsColour + 1, 3), "one byte written for the colour");
                MiscPal(g, pal, 200, 96, 255, 96);
                g.PutInt(HdPayload.Frames, 1030 + 72);
                FpsCounter(11.5);
                Eq(72, g.GetInt(HdPayload.FpsValue), "72 frames in 1 s");
                Eq((byte)200, g.Mem[HdPayload.FpsColour], "an exact match found later (the palette changes in infrared)");
            }
        }

        [Test] static void Misc_FpsCounter_FrenchPalette()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true); hdState = 1;
                MiscPal(g, 0x44AE90, 9, 96, 255, 96);   // the English palette's place: not read
                MiscPal(g, 0x44AF40, 12, 90, 240, 100);
                g.PutInt(HdPayload.Frames, 0);
                FpsCounter(1.0);
                g.PutInt(HdPayload.Frames, 45);
                FpsCounter(2.0);
                Eq(45, g.GetInt(HdPayload.FpsValue), "45 fps");
                Eq((byte)12, g.Mem[HdPayload.FpsColour], "the French game's palette at 0x44AF40");
            }
        }

        [Test] static void Misc_FpsCounter_WindowTiming()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                g.PutInt(HdPayload.Frames, 0); FpsCounter(10.0);
                g.PutInt(HdPayload.FpsValue, 77);
                g.PutInt(HdPayload.Frames, 20); FpsCounter(10.45);
                Eq(77, g.GetInt(HdPayload.FpsValue), "under half a second: nothing written");
                g.PutInt(HdPayload.Frames, 50); FpsCounter(12.5);
                Eq(77, g.GetInt(HdPayload.FpsValue), "more than 2 s since the window started: it starts again");
                g.PutInt(HdPayload.Frames, 80); FpsCounter(13.0);
                Eq(60, g.GetInt(HdPayload.FpsValue), "30 frames since the restart, in 0.5 s");
                g.PutInt(HdPayload.Frames, 80 + 100); FpsCounter(15.0);
                Eq(50, g.GetInt(HdPayload.FpsValue), "exactly 2 s: still measured");
                g.PutInt(HdPayload.Frames, 180); FpsCounter(16.0);
                Eq(0, g.GetInt(HdPayload.FpsValue), "no frame (menu, pause): 0, the counter hidden");
                g.PutInt(HdPayload.Frames, 181); FpsCounter(17.5);
                Eq(0, g.GetInt(HdPayload.FpsValue), "under 1 fps (1 frame in 1.5 s): 0, not rounded up to 1");
            }
        }

        [Test] static void Misc_FpsCounter_TurnedOff()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                g.PutInt(HdPayload.Frames, 0); FpsCounter(10.0);
                g.PutInt(HdPayload.Frames, 60); FpsCounter(11.0);
                Eq(60, g.GetInt(HdPayload.FpsValue), "on: 60");
                OptFpsCounter = false;
                FpsCounter(11.5);
                Eq(0, g.GetInt(HdPayload.FpsValue), "turned off: hidden once");
                g.PutInt(HdPayload.FpsValue, 33);
                FpsCounter(12.0); FpsCounter(13.0);
                Eq(33, g.GetInt(HdPayload.FpsValue), "then nothing more is written");
            }
        }

        [Test] static void Misc_FpsCounter_Only1280x720WithHd()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true);
                hdState = 0;
                uint value = HdPayload.FpsValue, frames = HdPayload.Frames;
                g.PutInt(value, 77);
                g.PutInt(frames, 0); FpsCounter(10.0);
                g.PutInt(frames, 60); FpsCounter(11.0);
                Eq(77, g.GetInt(value), "HD not running yet: nothing written");
                HdPayload.Use(true, true, false); hdState = 1;
                Eq(0u, HdPayload.FpsValue, "HD 848x480 has no frame counter");
                g.PutInt(frames, 120); FpsCounter(12.0);
                g.PutInt(frames, 180); FpsCounter(13.0);
                Eq(77, g.GetInt(value), "HD 848x480: the 1280x720 counter's place untouched");
                Ok(g.AllZero(0, 4), "nothing written at guest 0 either");
            }
        }

        // ================================================================ speed fixes report (HD 1280x720)
        [Test] static void Misc_SpeedFixReport_AllOn()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                Eq(16, HdPayload.SpeedNames.Length, "16 speed fix groups");
                g.PutInt(HdPayload.SpeedDone, 1); g.PutUInt(HdPayload.SpeedOn, 0xFFFF);
                string said = MiscSaid(SpeedFixReport);
                Has(said, "Speed fixes ON (16 of 16)", "all on");
                HasNot(said, "unexpected", "nothing unexpected");
                Ok(speedSaid, "said once");
            }
        }

        [Test] static void Misc_SpeedFixReport_OptionalGroupIsQuiet()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                Eq(0x800u, HdPayload.SpeedOptional, "the optional group: bit 11");
                Eq("cloud span", HdPayload.SpeedNames[11], "its name");
                g.PutInt(HdPayload.SpeedDone, 1); g.PutUInt(HdPayload.SpeedOn, 0xFFFF & ~0x800u); g.PutUInt(HdPayload.SpeedBad, 0x800);
                Has(MiscSaid(SpeedFixReport), "Speed fixes ON (15 of 16)", "the optional group off: still ON, counted out");
            }
        }

        [Test] static void Misc_SpeedFixReport_UnexpectedGroups()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true); hdState = 1;
                uint bad = (1u << 1) | (1u << 4) | (1u << 11);
                g.PutInt(HdPayload.SpeedDone, 1); g.PutUInt(HdPayload.SpeedOn, 0xFFFF & ~bad); g.PutUInt(HdPayload.SpeedBad, bad);
                string said = MiscSaid(SpeedFixReport);
                Has(said, "Speed fixes: 13 of 16 on, unexpected game code for horizon test, map cell key", "the groups not on, the optional one left out");
                HasNot(said, "cloud span", "the optional group is not listed");
            }
        }

        [Test] static void Misc_SpeedFixReport_NoneOn()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                g.PutInt(HdPayload.SpeedDone, 1); g.PutUInt(HdPayload.SpeedOn, 0); g.PutUInt(HdPayload.SpeedBad, 0xFFFF);
                List<string> names = new List<string>(HdPayload.SpeedNames);
                names.RemoveAt(11);
                Has(MiscSaid(SpeedFixReport), "Speed fixes: 0 of 16 on, unexpected game code for " + string.Join(", ", names.ToArray()), "every group but the optional one listed");
            }
        }

        [Test] static void Misc_SpeedFixReport_BitsPastTheGroupsIgnored()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                g.PutInt(HdPayload.SpeedDone, 1); g.PutUInt(HdPayload.SpeedOn, 0xFFFFFFFF);
                Has(MiscSaid(SpeedFixReport), "Speed fixes ON (16 of 16)", "bits 16-31 do not count");
            }
        }

        [Test] static void Misc_SpeedFixReport_WaitsThenSaysOnce()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true); hdState = 1;
                g.PutUInt(HdPayload.SpeedOn, 0xFFFF);
                Eq("", MiscSaid(SpeedFixReport), "the game has not run its first 3D frame (done = 0)");
                No(speedSaid, "not said yet");
                g.PutInt(HdPayload.SpeedDone, 1); g.PutUInt(HdPayload.SpeedOn, 0); g.PutUInt(HdPayload.SpeedBad, 0);
                Eq("", MiscSaid(SpeedFixReport), "done but no bit at all: nothing to say yet");
                No(speedSaid, "still not said");
                g.PutUInt(HdPayload.SpeedOn, 0xFFFF);
                Has(MiscSaid(SpeedFixReport), "Speed fixes ON (16 of 16)", "said when the bits are there");
                g.PutUInt(HdPayload.SpeedOn, 1);
                Eq("", MiscSaid(SpeedFixReport), "said once only");
            }
        }

        [Test] static void Misc_SpeedFixReport_NotWhenOffOrNoHd()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true);
                g.PutInt(HdPayload.SpeedDone, 1); g.PutUInt(HdPayload.SpeedOn, 0xFFFF);
                hdState = 0;
                Eq("", MiscSaid(SpeedFixReport), "HD not running");
                hdState = 1; OptSpeedFix = false;
                Eq("", MiscSaid(SpeedFixReport), "speed_fixes = 0");
                OptSpeedFix = true;
                HdPayload.Use(true, true, false);
                Eq("", MiscSaid(SpeedFixReport), "HD 848x480: no speed fixes");
                No(speedSaid, "nothing said");
            }
        }

        // ================================================================ adaptive terrain (SHARPER in HD)
        [Test] static void Misc_AdaptTerrain_ShrinksBelowTheTarget()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn();
                MiscAdapt(g, 10, 0);
                Eq(48, SmoothCells(), "start: 48 cells");
                // 50 fps every second: -8 cells, then 2 s to settle, down to 24
                int[] want = { 40, 40, 32, 32, 24, 24, 24, 24 };
                for (int s = 1; s <= want.Length; s++)
                {
                    MiscAdapt(g, 10 + s, 50 * s);
                    Eq(want[s - 1], SmoothCells(), "50 fps, second " + s);
                }
            }
        }

        [Test] static void Misc_AdaptTerrain_GrowsAfterThreeGoodSeconds()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn();
                adaptCells = 24;                    // shrunk long ago
                MiscAdapt(g, 10, 0);
                // 100 fps (x 0.8 = 80 > 62): +8 cells after 3 good seconds; the second after a change does not count
                int[] want = { 24, 24, 32, 32, 32, 32, 40, 40, 40, 40, 48, 48, 48, 48, 48 };
                for (int s = 1; s <= want.Length; s++)
                {
                    MiscAdapt(g, 10 + s, 100 * s);
                    Eq(want[s - 1], SmoothCells(), "100 fps, second " + s);
                }
                Eq(48, adaptCells, "the ring stays at 48");
                MiscAdaptOn(); adaptCells = 44;     // off the 8-cell grid (not reached by adapting): never past 48
                for (int s = 0; s <= 4; s++) MiscAdapt(g, 10 + s, 100 * s);
                Eq(48, adaptCells, "44 + 8 is capped at 48");
            }
        }

        [Test] static void Misc_AdaptTerrain_NoGrowthWithin10sOfAShrink()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn();
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 11, 50);
                Eq(40, SmoothCells(), "shrunk at t = 11");
                int frames = 50;
                for (int t = 12; t <= 20; t++)
                {
                    frames += 100; MiscAdapt(g, t, frames);
                    Eq(40, SmoothCells(), "100 fps at t = " + t + ": no growth within 10 s of the shrink");
                }
                frames += 100; MiscAdapt(g, 21, frames);
                Eq(48, SmoothCells(), "t = 21: 10 s after the shrink, grows back");
            }
        }

        [Test] static void Misc_AdaptTerrain_Thresholds()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn();
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 11, 58);
                Eq(48, SmoothCells(), "58 fps = target - 2: kept");
                MiscAdapt(g, 12, 58 + 57);
                Eq(40, SmoothCells(), "57 fps: shrinks");
                MiscAdaptOn(); adaptCells = 24;
                int frames = 0; MiscAdapt(g, 10, frames);
                for (int t = 11; t <= 14; t++) { frames += 77; MiscAdapt(g, t, frames); }
                Eq(24, SmoothCells(), "77 fps (x 0.8 = 61.6, not above 62): never good enough to grow");
                for (int t = 15; t <= 17; t++) { frames += 78; MiscAdapt(g, t, frames); }
                Eq(32, SmoothCells(), "78 fps (x 0.8 = 62.4): grows after 3 s");
            }
        }

        [Test] static void Misc_AdaptTerrain_OtherTarget()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn(); OptTerrainFps = 30;
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 11, 40);
                Eq(48, SmoothCells(), "terrain_fps = 30: 40 fps is enough");
                MiscAdapt(g, 12, 40 + 27);
                Eq(40, SmoothCells(), "27 fps < 28: shrinks");
            }
        }

        [Test] static void Misc_AdaptTerrain_MeasureWindow()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn();
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 10.9, 0);              // under 1 s: neither measured nor restarted
                MiscAdapt(g, 11, 50);
                Eq(40, SmoothCells(), "50 frames over the second since t = 10");

                MiscAdaptOn();
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 13.5, 50);             // 3.5 s later (loading, alt-tab): the window starts again
                Eq(48, SmoothCells(), "a gap over 3 s is not measured");
                MiscAdapt(g, 14.5, 100);
                Eq(40, SmoothCells(), "measured from the restart");

                MiscAdaptOn();
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 13, 150);
                Eq(40, SmoothCells(), "exactly 3 s: measured (50 fps)");
            }
        }

        [Test] static void Misc_AdaptTerrain_PauseNeverShrinks()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn();
                MiscAdapt(g, 10, 0);
                for (int t = 11; t <= 15; t++) MiscAdapt(g, t, 4 * (t - 10));
                Eq(48, SmoothCells(), "4 fps (under 5: menus, pause): not a slow frame rate, kept");
                MiscAdapt(g, 16, 20);
                Eq(48, SmoothCells(), "0 fps: kept");
                MiscAdapt(g, 17, 20 + 5);
                Eq(40, SmoothCells(), "5 fps: a real (slow) frame rate, shrinks");
            }
        }

        [Test] static void Misc_AdaptTerrain_PauseResetsTheGoodSeconds()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn(); adaptCells = 24;
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 11, 100);              // good 1
                MiscAdapt(g, 12, 200);              // good 2
                MiscAdapt(g, 13, 204);              // 4 fps: menu or pause, no change, the count starts again
                Eq(24, SmoothCells(), "a paused second: kept");
                MiscAdapt(g, 14, 302); MiscAdapt(g, 15, 402);
                Eq(24, SmoothCells(), "2 good seconds since the pause");
                MiscAdapt(g, 16, 502);
                Eq(32, SmoothCells(), "the 3rd good second after the pause");
            }
        }

        [Test] static void Misc_AdaptTerrain_OnlyWhenEverythingIsThere()
        {
            using (FakeGame g = new FakeGame())
            {
                string[] what = { "terrain_fps = 0", "smooth_cells forced", "SHARP", "exe not prepared", "smooth buffers not done", "HD not running", "terrain rings not found" };
                Action[] off = {
                    delegate { OptTerrainFps = 0; }, delegate { OptSmoothCells = 40; }, delegate { OptDetail = 1; },
                    delegate { farTerrain = false; }, delegate { smoothState = -1; }, delegate { hdState = 0; }, delegate { haveDetail = false; } };
                for (int i = 0; i < off.Length; i++)
                {
                    MiscAdaptOn();
                    off[i]();
                    MiscAdapt(g, 10, 0); MiscAdapt(g, 11, 20); MiscAdapt(g, 12, 40); MiscAdapt(g, 13, 60);
                    Eq(0, adaptCells, what[i] + ": no adapting at 20 fps");
                }
                MiscAdaptOn();
                MiscAdapt(g, 10, 0); MiscAdapt(g, 11, 20);
                Eq(40, adaptCells, "everything there: adapting");
            }
        }

        [Test] static void Misc_AdaptTerrain_RewritesTheRings()
        {
            using (FakeGame g = new FakeGame())
            {
                MiscAdaptOn();
                MiscRings(g, aTerrain);
                MiscAdapt(g, 10, 0);
                MiscAdapt(g, 11, 50);
                Eq(40, SmoothCells(), "shrunk to 40");
                uint r = aTerrain + 0x5C;
                Eq((short)4, g.GetShort(r + 9), "ring 0 keeps the game's end");
                Eq((short)40, g.GetShort(r + 12 + 9), "ring 1 (smooth polygons) ends at the new 40 cells");
                Eq((short)40, g.GetShort(r + 24 + 7), "ring 2 starts there");
                int[] ends = DETAIL_RINGS[2];
                for (int k = 0; k < 4; k++) Eq((short)ends[k], g.GetShort(r + 12 * (uint)(k + 2) + 9), "ring " + (k + 2) + ": SHARPER's end");
                for (int k = 0; k < 3; k++) Eq((short)ends[k], g.GetShort(aTerrain + 0x30 + 2 * (uint)k), "ring distance " + k);
            }
        }
    }
}
