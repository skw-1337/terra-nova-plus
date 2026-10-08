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
//  HD mode (HdPayloadFr.cs / HdPayloadEn.cs, generated from the reverse-engineering scripts)
//  carries the addresses of the French (GOG) and English (GOG, Steam) executables. HD and the
//  terrain detail are the only options that touch a game file: TNOVA\__FF.EXE gets 16 KB more
//  memory for the HD code and the far smooth terrain patches (TERRAIN_EXE), original kept as
//  __FF.EXE.tnplus-original.
//
//  Build (no install needed, uses the C# compiler shipped with Windows):
//    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize
//        /win32manifest:app.manifest /win32icon:icon.ico /out:TNPlus.exe TNPlus.cs HdPayload.cs HdPayloadFr.cs HdPayloadEn.cs AweBank.cs   (or run build.bat)
//
//  AweBank.cs (AWE32 music) is under the GPL v2 or later, the rest under the MIT license.
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

static partial class TNPlus
{
    const string VERSION = "1.1.0-beta4";
    const string TITLE = "Terra Nova Plus";

    // ------------------------------------------------------------------ options (TNPlus.ini)
    static bool OptFreelook = true, OptNoclip = true, OptForce400 = true, OptWide = false, OptHD = false;
    static int OptDistance = 2;                     // 0 NORMAL, 1 FAR, 2 MAX
    static bool OptStereoFix = false;                // DOSBox: swap the Sound Blaster stereo (the game's SB16 driver reverses it)
    static bool OptHitFix = true;                   // projectile hit test fixed for high frame rates (see TryHitFix)
    static bool OptPhysFix = true;                  // physics clock fixed for high frame rates (see PHYSFIX_AT)
    static int OptObjDist = 2;                      // object draw distance: 0 GAME, 1 FAR, 2 MAX (see OBJ_*)
    static readonly string[] OBJDIST_NAMES = { "GAME", "FAR", "MAX" };
    static int OptFov = 0;                          // field of view of the main 3D view: index in FOV_NAMES (see FOV_*)
    static readonly string[] FOV_NAMES = { "GAME", "90", "100", "110" };
    static int OptMusic = 0;                        // 0 Roland GS (General MIDI), 1 FM, 2 the game's own setting, 3 AWE32 (needs awe32.raw)
    static readonly string[] MUSIC_NAMES = { "ROLAND", "FM", "GAME'S OWN", "AWE32" };
    static readonly string[] MUSIC_INFO = { "Roland GS sounds of the Windows MIDI synthesizer (General MIDI)",
        "Sound Blaster FM synthesis, the default of the digital editions", "the game's TN.CFG is left as it is",
        "the game's own AWE32 bank by Eric Brosius (FF.SBK) with the AWE32 ROM samples, played by DOSBox" };
    static int LaunchTarget = 0;                    // 0 game, 1 demo 1, 2 demo 2
    static readonly string[] TARGET_NAMES = { "GAME", "DEMO 1", "DEMO 2" };
    static readonly string[] TARGET_DIRS = { "TNOVA", "TNDEMO1", "TNDEMO2" };
    static int OptDetail = 2;                       // terrain detail: 0 GAME, 1 SHARP, 2 SHARPER
    static int SensX = 12, SensY = 8;               // heading / pitch units per mouse count
    static bool InvertY = false, Sound = true, HdSmoothing = true;
    static int ScanFreelook = 0x15, ScanNoclip = 0x16, ScanDistance = 0x24;   // Y U J (physical keys; I is the game's infrared)
    static int ScanSmoothing = 0x58;                // F12: HD smoothing on / off
    static int ScanStereo = 0x41;                   // F7: swap the stereo of the sound effects in game
    static double NoclipSpeed = 15.0;               // game units per second (a walking PBA ~2.5)
    static int CpuCycles = 1000000;                 // DOSBox CPU cycles imposed at launch (0 = the edition's own setting)
    static readonly int[] CYCLE_CHOICES = { 500000, 700000, 1000000, 1200000, 1400000, 0 };
    static string GameDir = "";

    static readonly string[] DIST_NAMES = { "NORMAL", "FAR", "MAX" };
    static readonly string[] DETAIL_NAMES = { "GAME", "SHARP", "SHARPER" };
    // terrain rings (full mesh, 1 point in 2, in 4, in 8) up to these distances; the game's MEDIUM is 30/64/100/240.
    // ULTRA (60/120/240/480) froze the game after a few minutes: the far ring stays close to the original.
    static readonly int[][] DETAIL_RINGS = { null, new[] { 48, 96, 140, 240 }, new[] { 64, 128, 180, 280 } };
    // Smooth terrain: near the camera the game draws the ground as textured polygons (up to 12 cells), farther
    // as columns that turn slopes and cliff edges into stair steps. Its polygon buffers are allocated every
    // frame with fixed sizes (500 quads, 500 quad pairs of triangles, 750 vertices) that a longer polygon ring
    // overflows (crash), so they are enlarged to 768 quads / 1000 vertices first (+35 KB of the render pool,
    // which has 53 KB free at its peak). SHARP: polygons up to 18 cells, SHARPER: 24 (about -10 % fps; the
    // 64x64 vertex map of the engine caps it at 31). Measured turning on mission 12: 484 quads at 24 cells.
    // This in-memory way is the fallback when __FF.EXE could not be prepared.
    static readonly int[] SMOOTH_TO = { 0, 18, 24 };
    // With __FF.EXE prepared (see TERRAIN_EXE) the vertex map is 128x128 and the buffers hold 48 cells (the
    // engine's outline table stops at 49). Mission 12, HD, 500000 cycles, same view: 24 cells 64.7 fps,
    // 32: 59.6, 40: 54.2, 48: 49.1; at most 1870 quads turning at 48 cells, 16:9 included.
    static readonly int[] SMOOTH_FAR = { 0, 32, 48 };
    static int OptSmoothCells = 0;                  // ini only (tests): forced end of the smooth ring, 0 = per detail
    static bool farTerrain = false;
    static readonly uint[][] SMOOTH_AT = { new uint[] { 0x2AB0C5, 0x2ADF0F, 0x2ADF00 }, new uint[] { 0x2AB0D5, 0x2ADF1F, 0x2ADF10 } };
    static readonly byte[][] SMOOTH_OLD = { new byte[] { 0xC0, 0x3E, 0, 0 }, new byte[] { 0, 0x7D, 0, 0 }, new byte[] { 0x42, 0x72, 0, 0 } };
    static readonly byte[][] SMOOTH_NEW = { new byte[] { 0x40, 0x60, 0, 0 }, new byte[] { 0, 0xC0, 0, 0 }, new byte[] { 0x58, 0x98, 0, 0 } };
    static int smoothState = 0;                     // 0 to do, 1 buffers enlarged, -1 unavailable

    // Field of view of the main 3D view (cockpit, full view, every zoom level); the small cockpit cameras keep
    // theirs. 0x29E820 sets a camera up from its zoom whenever the canvas or the zoom changes: everything follows
    // from that zoom (focal lengths, culling half-angles, view matrix, the 3D library's projection), and the stock
    // view is 2 atan(1 / (1.1 zoom)) = 84.5 deg wide. The hook gives the main camera (0x38FBD4 / 0x38FB24) an
    // effective zoom = zoom x K, K = 1 / (1.1 tan(FOV / 2)); the ring distances keep the raw zoom. Code in the
    // free end of the HD code area, written at the main menu like the other code patches.
    const uint FOV_CAVE = 0x46A000;                 // data: K, then 12 bytes, code at +0x10 (after the HD zone)
    static readonly string[] FOV_CODE = {
        "51B80000010029C8BA40AF2F00FFD2BF0000020029C7598B1E81FED4FB3800752689C8F72D00A046000FACD0108944240485DB741289D889C2C1FA10C1E010F73D00A0460089C385DB7506BD00000100C3B80000010029D8BA40AF2F00FFD2BD0000020029C5C3F72D04A046000FACD01089C2894604C3B8A5C62E00FFD05052A176233600F72D08A046000FACD010A3762336005A58C3",
        "51B80000010029C8BAB0AC2F00FFD2BF0000020029C7598B1E81FE24FB3800752689C8F72D00A046000FACD0108944240485DB741289D889C2C1FA10C1E010F73D00A0460089C385DB7506BD00000100C3B80000010029D8BAB0AC2F00FFD2BD0000020029C5C3F72D04A046000FACD01089C2894604C3B8C5C42E00FFD05052A1CE223600F72D08A046000FACD010A3CE2236005A58C3" };
    static readonly uint[] FOV_HOOK = { 0x29E833, 0x29E843 };
    static readonly byte[][] FOV_HOOK_NEW = { new byte[] { 0xE8, 0xD8, 0xB7, 0x1C, 0x00, 0xEB, 0x2C }, new byte[] { 0xE8, 0xC8, 0xB7, 0x1C, 0x00, 0xEB, 0x2C } };
    static readonly byte[] FOV_HOOK_OLD = { 0xB8, 0x00, 0x00, 0x01, 0x00, 0x29, 0xC8 };
    // 16:9 (the picture is stretched x4/3 by DOSBox): every camera's vertical scale (camera+4, 0x29EA61) x4/3 and
    // the 3D library's pixel ratio x3/4 right after the renderer loads it (call at 0x29E751): proportions stay right
    // on screen in every view, small cockpit cameras included. The main camera's wider view then comes from its
    // effective zoom like any field of view, so the terrain's culling angles follow it (with the old global pixel
    // ratio the polygon terrain stopped at 45 deg, short of the screen edges).
    static readonly uint[] WIDE_HOOK_B = { 0x29EA61, 0x29EA71 }, WIDE_HOOK_C = { 0x29E751, 0x29E761 };
    static readonly byte[] WIDE_B_OLD = { 0x89, 0xC2, 0x89, 0x46, 0x04 };
    static readonly byte[][] WIDE_B_NEW = { new byte[] { 0xE8, 0x11, 0xB6, 0x1C, 0x00 }, new byte[] { 0xE8, 0x01, 0xB6, 0x1C, 0x00 } };
    static readonly byte[][] WIDE_C_OLD = { new byte[] { 0xE8, 0x4F, 0xDF, 0x04, 0x00 }, new byte[] { 0xE8, 0x5F, 0xDD, 0x04, 0x00 } };
    static readonly byte[][] WIDE_C_NEW = { new byte[] { 0xE8, 0x31, 0xB9, 0x1C, 0x00 }, new byte[] { 0xE8, 0x21, 0xB9, 0x1C, 0x00 } };
    // True 16:9 (HD): the main view is drawn 848 columns wide, and the engine scales its projection on the canvas
    // width (the screen centre doubles as the focal length), so the picture came out stretched x848/(2 x canvas)
    // with the same field of view, while the 3D library (buildings, units, target box) kept its own scale: they
    // slid on the ground. Same cure as stretched 16:9, for the main camera only, with F = 2 x canvas width / 848
    // (298 in the cockpit, 320 in the full view) read by the game code itself: effective zoom x F and vertical
    // scale x1/F at its set-up, the library's pixel ratio x 1/F while the HD view is drawn (its horizontal scale is ratio x canvas height / 2, the canvas width cancels out: objects then match the terrain, measured k = 1 as in 4:3). The middle is the 4:3
    // view again and the sides show more of the world. Code at 0x46A200 (set-up), 0x46A290 (scale), 0x46A2B0 (ratio);
    // 0x469400 = zoom factor of the last set-up, 0x469404 = 2 x its canvas width (a page without code: DOSBox's
    // dynamic core recompiles a code page that gets written, which can crash).
    const uint TRUE_A = 0x46A200, TRUE_B = 0x46A290, TRUE_C = 0x46A2B0;
    static readonly string[] TRUE_A_CODE = {
        "50528B44241001C0A304944600F72D00A04600BB50030000F7FBA3009446005A5851B80000010029C8BA40AF2F00FFD2BF0000020029C7598B1E81FED4FB3800752689C8F72D009446000FACD0108944240485DB741289D889C2C1FA10C1E010F73D0094460089C385DB7506BD00000100C3B80000010029D8BA40AF2F00FFD2BD0000020029C5C3",
        "50528B44241001C0A304944600F72D00A04600BB50030000F7FBA3009446005A5851B80000010029C8BAB0AC2F00FFD2BF0000020029C7598B1E81FE24FB3800752689C8F72D009446000FACD0108944240485DB741289D889C2C1FA10C1E010F73D0094460089C385DB7506BD00000100C3B80000010029D8BAB0AC2F00FFD2BD0000020029C5C3" };
    static readonly string[] TRUE_B_CODE = { "81FED4FB3800750DBA50030000F7EAF73D0494460089C2894604C3", "81FE24FB3800750DBA50030000F7EAF73D0494460089C2894604C3" };
    static readonly string[] TRUE_C_CODE = {
        "B8A5C62E00FFD081FDD4FB38007529833D04614600007420505251A176233600F72D846146008B0D7861460001C9F7F9A376233600595A58C3",
        "B8C5C42E00FFD081FD24FB38007529833D04614600007420505251A1CE223600F72D846146008B0D7861460001C9F7F9A3CE223600595A58C3" };
    static readonly byte[][] TRUE_A_NEW = { new byte[] { 0xE8, 0xC8, 0xB9, 0x1C, 0x00, 0xEB, 0x2C }, new byte[] { 0xE8, 0xB8, 0xB9, 0x1C, 0x00, 0xEB, 0x2C } };
    static readonly byte[][] TRUE_B_NEW = { new byte[] { 0xE8, 0x2A, 0xB8, 0x1C, 0x00 }, new byte[] { 0xE8, 0x1A, 0xB8, 0x1C, 0x00 } };
    static readonly byte[][] TRUE_C_NEW = { new byte[] { 0xE8, 0x5A, 0xBB, 0x1C, 0x00 }, new byte[] { 0xE8, 0x4A, 0xBB, 0x1C, 0x00 } };
    static bool wideHooks = false;                  // 16:9 done by the hooks (else the old pixel ratio way)
    static int fovState = 0;                        // 0 to do, 1 on, -1 off or unavailable

    // The polygon terrain is built in 3 sectors of 45 deg around the heading (a 135 deg window that moves by 45 deg
    // steps), from the culling half-angle at camera+0x18. A wider view facing a diagonal needs a 4th sector: the
    // builder then gave up after a few rows (about 350 polygons, voxel walls on screen). The hook on its call
    // (0x2AA3B2 / 0x2AA3C2) lowers the half-angle it sees to what the window covers at that heading, then puts it
    // back; the strip left at one screen edge near a diagonal (8 deg at most at 100 deg) is drawn by the voxel.
    const uint WEDGE_CAVE = 0x46A100;
    static readonly string[] WEDGE_CODE = {
        "5051560FB74818510FB7720CB900F0000081FE00E000007753B90010000081FE002000007646B90030000081FE004000007239B90050000081FE00600000762CB90070000081FE00800000721FB90090000081FE00A000007612B900B0000081FE00C000007205B900D0000029CE0FBFF685F67D02F7DEF7DE81C600300000393424760466897018B968892A00FFD1598B74240866894E185E5983C404C3",
        "5051560FB74818510FB7720CB900F0000081FE00E000007753B90010000081FE002000007646B90030000081FE004000007239B90050000081FE00600000762CB90070000081FE00800000721FB90090000081FE00A000007612B900B0000081FE00C000007205B900D0000029CE0FBFF685F67D02F7DEF7DE81C600300000393424760466897018B978892A00FFD1598B74240866894E185E5983C404C3" };
    static readonly uint[] WEDGE_HOOK = { 0x2AA3B2, 0x2AA3C2 };
    static readonly byte[] WEDGE_OLD = { 0xE8, 0xB1, 0xE5, 0xFF, 0xFF };
    static readonly byte[][] WEDGE_NEW = { new byte[] { 0xE8, 0x49, 0xFD, 0x1B, 0x00 }, new byte[] { 0xE8, 0x39, 0xFD, 0x1B, 0x00 } };

    // Objects at the screen edges with a field of view wider than the game's (true 16:9, 90-110 degrees): the
    // polygon terrain and every object on it (buildings, units, trees) only exist in the cells the builder makes,
    // in its window of 3 sectors of 45 degrees, so near a diagonal the edge of a wider view had no cells: walls of
    // far terrain and buildings popping in and out. After the game's pass, the builder runs again for the two
    // side strips (a virtual heading, its direction vector, the strip's half-angle); their new cells (not already
    // built, in front, inside the strip, every corner computed) are merged into the list by ring so the near-to-far
    // order of the game's pass is kept, and the vertex numbering goes on from the game's pass (it restarted at 0 and
    // overwrote it). Code at 0x46A300-0x46AA16, data 0x469410-0x469A00. Generated by _MODS/re/strips_cave.py:
    // the English executable has the same routines 0x10 bytes further and its variables 0xB0 bytes lower.
    const uint EDGE_SIN = 0x469500, EDGE_MODE = 0x4694F0;
    static readonly string[][][] EDGE_CAVES = {
        new[] {   // French
            new[] { "0x46A300",
            "5155565789C589D689DFC7051094460000000000B988A32A00FFD1A114944600A3109446008935B8944600833DF0944600000F84EE0100000FB7460CA3809446000FB74D18890D8494460025FF1F00002D001000007D02F7D8F7D8058030000039C10F86" +
            "BE0100002D80000000A3D4944600813D10944600001900000F83A40100000FB715947E420081FA540B00000F839101000089158C94460089159894460089CA01C2D1EA89159094460029C1D1E981C100010000890D949446008B15907E42008915889446" +
            "008B560E8915BC9446008B56128915C0944600565789FEBF40944600B910000000F3A55F5EB800080000B9D04F2C00FFD1A3E494460085C0743F5789C731C0B900020000F3AB5F8B1D889446008B0D9894460085C974220FB7431283E07FC1E0070FB753" +
            "1083E27F09D08B15E49446000FAB0283C3204975DEA1809446000305D49446002D00030000A3CC944600A1809446000305849446000500020000A3D0944600E8C5000000833DF094460005742FA1809446002B05849446002D00020000A3CC944600A180" +
            "9446002B05D49446000500030000A3D0944600E88D000000A1E494460085C07407B974502C00FFD1A1809446006689460CA1BC94460089460EA1C0944600894612A18494460066894518A188944600A3907E4200A18C944600833DF0944600017409833D" +
            "F0944600057505A19894460066A3947E4200833DF0944600077407B980A74600FFD15657BE40944600B910000000F3A55F5EC70510944600000000005F5E5D59C3A1D09446002B05CC944600D1E889C1030DCC9446000500020000A39494460089C825FF" +
            "FF0000A39C9446006689460CBBC4944600E8C7010000A1C494460089460EA1C8944600894612A19494460066894518A18C9446003D540B00000F839D010000C1E005030588944600A3907E4200833DF0944600040F848201000089E889F289FBB988A32A" +
            "00FFD1A114944600A3109446008B06A3B09446008B4604A3B49446000FB705947E4200A3D8944600833DF0944600050F8443010000833DF094460003750E0FB705947E420001058C944600C3A1CC944600BBA0944600E81E010000A1D0944600BBA89446" +
            "00E80F01000055570FB70D947E42008B1D907E420089DF85C90F84E40000008B15E494460085D2741C0FB7431283E07FC1E0070FB76B1083E57F09E80FA3020F82B40000000FBF4310C1E01005008000002B06C1F8080FBF5312C1E21081C2008000002B" +
            "5604C1FA0850525189C10FAF0DBC9446000FAF15C094460001D181F900000002595A587C7050520FAF15A09446000FAF05A494460039C25A587C5A0FAF05AC9446000FAF15A894460039D07C488B15E494460085D274160FB7431283E07FC1E0070FB76B" +
            "1083E57F09E80FAB0289F82B0588944600C1E8053D100E0000731639DF740F565189DEB90800000057F3A55F595E83C72083C320490F851CFFFFFF2B3D907E4200C1EF05013D8C9446005F5DC389C1C1E90881E1FF0000008B148D0095460089530483C1" +
            "4081E1FF0000008B148D009546008913C3" },
            new[] { "0x46A780",
            "60833DF09446000875328B3598944600C1E6050335907E42008B3D907E42008B0D8C9446002B0D9894460066890D947E4200C1E103F3A5E99E010000A18C9446002B05989446000F868D010000BF0099460031C0B940000000F3AB8B2DB89446008B1D98" +
            "944600C1E305031D907E42008B0D8C9446002B0D98944600E85B010000FF04850099460083C3204975EE31C031C98B148D0099460089048D0099460001D04183F94072EAA18C944600C1E006B9D04F2C00FFD185C00F841B010000A3E09446008B1D9894" +
            "4600C1E305031D907E42008B0D8C9446002B0D9894460051E8F70000008B3C8500994600FF048500994600C1E705033DE094460089DEB908000000F3A55983C3204975D38B3D8C944600C1E705033DE0944600893DE89446008B15E0944600A18C944600" +
            "2B0598944600C1E00501D0A3EC9446008B1D907E42008B0D98944600C705F89446000000000085C9745251E8800000003B05F89446007605A3F894460089D889D33B1DEC944600731F50E86100000089C1583B0DF8944600770E89DEB908000000F3A583" +
            "C320EBD989DA89C389DEB908000000F3A583C3205949EBAA89D68B0DEC94460029D1C1E902F3A58B35E89446008B3D907E42008B0D8C944600C1E103F3A5A1E0944600B974502C00FFD161C352560FBF43100FBF550229D07D02F7D80FBF73120FBF5506" +
            "29D67D02F7DE39F07D0289F05E5A83F83F7605B83F000000C3" },
            new[] { "0x46A9C0",
            "8B1D109446006BDB27031DE88C4300C3" },
            new[] { "0x46A9D8",
            "8B0D1094460080FC030F94C0C3" },
            new[] { "0x46A9F0",
            "50515289D82B05E88C430031D2B927000000F7F1A3149446005A595881C4300100005D5F5EC3" }
        },
        new[] {   // English (GOG, Steam)
            new[] { "0x46A300",
            "5155565789C589D689DFC7051094460000000000B998A32A00FFD1A114944600A3109446008935B8944600833DF0944600000F84EE0100000FB7460CA3809446000FB74D18890D8494460025FF1F00002D001000007D02F7D8F7D8058030000039C10F86" +
            "BE0100002D80000000A3D4944600813D10944600001900000F83A40100000FB715E47D420081FA540B00000F839101000089158C94460089159894460089CA01C2D1EA89159094460029C1D1E981C100010000890D949446008B15E07D42008915889446" +
            "008B560E8915BC9446008B56128915C0944600565789FEBF40944600B910000000F3A55F5EB800080000B9404F2C00FFD1A3E494460085C0743F5789C731C0B900020000F3AB5F8B1D889446008B0D9894460085C974220FB7431283E07FC1E0070FB753" +
            "1083E27F09D08B15E49446000FAB0283C3204975DEA1809446000305D49446002D00030000A3CC944600A1809446000305849446000500020000A3D0944600E8C5000000833DF094460005742FA1809446002B05849446002D00020000A3CC944600A180" +
            "9446002B05D49446000500030000A3D0944600E88D000000A1E494460085C07407B9E44F2C00FFD1A1809446006689460CA1BC94460089460EA1C0944600894612A18494460066894518A188944600A3E07D4200A18C944600833DF0944600017409833D" +
            "F0944600057505A19894460066A3E47D4200833DF0944600077407B980A74600FFD15657BE40944600B910000000F3A55F5EC70510944600000000005F5E5D59C3A1D09446002B05CC944600D1E889C1030DCC9446000500020000A39494460089C825FF" +
            "FF0000A39C9446006689460CBBC4944600E8C7010000A1C494460089460EA1C8944600894612A19494460066894518A18C9446003D540B00000F839D010000C1E005030588944600A3E07D4200833DF0944600040F848201000089E889F289FBB998A32A" +
            "00FFD1A114944600A3109446008B06A3B09446008B4604A3B49446000FB705E47D4200A3D8944600833DF0944600050F8443010000833DF094460003750E0FB705E47D420001058C944600C3A1CC944600BBA0944600E81E010000A1D0944600BBA89446" +
            "00E80F01000055570FB70DE47D42008B1DE07D420089DF85C90F84E40000008B15E494460085D2741C0FB7431283E07FC1E0070FB76B1083E57F09E80FA3020F82B40000000FBF4310C1E01005008000002B06C1F8080FBF5312C1E21081C2008000002B" +
            "5604C1FA0850525189C10FAF0DBC9446000FAF15C094460001D181F900000002595A587C7050520FAF15A09446000FAF05A494460039C25A587C5A0FAF05AC9446000FAF15A894460039D07C488B15E494460085D274160FB7431283E07FC1E0070FB76B" +
            "1083E57F09E80FAB0289F82B0588944600C1E8053D100E0000731639DF740F565189DEB90800000057F3A55F595E83C72083C320490F851CFFFFFF2B3DE07D4200C1EF05013D8C9446005F5DC389C1C1E90881E1FF0000008B148D0095460089530483C1" +
            "4081E1FF0000008B148D009546008913C3" },
            new[] { "0x46A780",
            "60833DF09446000875328B3598944600C1E6050335E07D42008B3DE07D42008B0D8C9446002B0D9894460066890DE47D4200C1E103F3A5E99E010000A18C9446002B05989446000F868D010000BF0099460031C0B940000000F3AB8B2DB89446008B1D98" +
            "944600C1E305031DE07D42008B0D8C9446002B0D98944600E85B010000FF04850099460083C3204975EE31C031C98B148D0099460089048D0099460001D04183F94072EAA18C944600C1E006B9404F2C00FFD185C00F841B010000A3E09446008B1D9894" +
            "4600C1E305031DE07D42008B0D8C9446002B0D9894460051E8F70000008B3C8500994600FF048500994600C1E705033DE094460089DEB908000000F3A55983C3204975D38B3D8C944600C1E705033DE0944600893DE89446008B15E0944600A18C944600" +
            "2B0598944600C1E00501D0A3EC9446008B1DE07D42008B0D98944600C705F89446000000000085C9745251E8800000003B05F89446007605A3F894460089D889D33B1DEC944600731F50E86100000089C1583B0DF8944600770E89DEB908000000F3A583" +
            "C320EBD989DA89C389DEB908000000F3A583C3205949EBAA89D68B0DEC94460029D1C1E902F3A58B35E89446008B3DE07D42008B0D8C944600C1E103F3A5A1E0944600B9E44F2C00FFD161C352560FBF43100FBF550229D07D02F7D80FBF73120FBF5506" +
            "29D67D02F7DE39F07D0289F05E5A83F83F7605B83F000000C3" },
            new[] { "0x46A9C0",
            "8B1D109446006BDB27031D388C4300C3" },
            new[] { "0x46A9D8",
            "8B0D1094460080FC030F94C0C3" },
            new[] { "0x46A9F0",
            "50515289D82B05388C430031D2B927000000F7F1A3149446005A595881C4300100005D5F5EC3" }
        },
    };
    static readonly string EDGE_SINTAB =
        "0000000048060000900C0000D512000018190000561F000090250000C42B0000F131000017380000343E000047440000504A00004D5000003E560000225C0000F8610000BE670000746D00001A730000AD7800002F7E00009C830000F68800003A8E0000" +
        "6893000080980000809D000068A2000036A70000EBAB000086B0000005B5000068B90000AFBD0000D8C10000E4C50000D1C900009FCD00004DD10000DBD4000048D8000094DB0000BEDE0000C6E10000AAE400006CE700000AEA000083EC0000D9EE0000" +
        "09F1000014F30000FAF40000BAF6000054F80000C8F9000015FB00003BFC00003BFD000013FE0000C4FE00004EFF0000B1FF0000ECFF000000000100ECFF0000B1FF00004EFF0000C4FE000013FE00003BFD00003BFC000015FB0000C8F9000054F80000" +
        "BAF60000FAF4000014F3000009F10000D9EE000083EC00000AEA00006CE70000AAE40000C6E10000BEDE000094DB000048D80000DBD400004DD100009FCD0000D1C90000E4C50000D8C10000AFBD000068B9000005B5000086B00000EBAB000036A70000" +
        "68A20000809D000080980000689300003A8E0000F68800009C8300002F7E0000AD7800001A730000746D0000BE670000F8610000225C00003E5600004D500000504A000047440000343E000017380000F1310000C42B000090250000561F000018190000" +
        "D5120000900C00004806000000000000B8F9FFFF70F3FFFF2BEDFFFFE8E6FFFFAAE0FFFF70DAFFFF3CD4FFFF0FCEFFFFE9C7FFFFCCC1FFFFB9BBFFFFB0B5FFFFB3AFFFFFC2A9FFFFDEA3FFFF089EFFFF4298FFFF8C92FFFFE68CFFFF5387FFFFD181FFFF" +
        "647CFFFF0A77FFFFC671FFFF986CFFFF8067FFFF8062FFFF985DFFFFCA58FFFF1554FFFF7A4FFFFFFB4AFFFF9846FFFF5142FFFF283EFFFF1C3AFFFF2F36FFFF6132FFFFB32EFFFF252BFFFFB827FFFF6C24FFFF4221FFFF3A1EFFFF561BFFFF9418FFFF" +
        "F615FFFF7D13FFFF2711FFFFF70EFFFFEC0CFFFF060BFFFF4609FFFFAC07FFFF3806FFFFEB04FFFFC503FFFFC502FFFFED01FFFF3C01FFFFB200FFFF4F00FFFF1400FFFF0000FFFF1400FFFF4F00FFFFB200FFFF3C01FFFFED01FFFFC502FFFFC503FFFF" +
        "EB04FFFF3806FFFFAC07FFFF4609FFFF060BFFFFEC0CFFFFF70EFFFF2711FFFF7D13FFFFF615FFFF9418FFFF561BFFFF3A1EFFFF4221FFFF6C24FFFFB827FFFF252BFFFFB32EFFFF6132FFFF2F36FFFF1C3AFFFF283EFFFF5142FFFF9846FFFFFB4AFFFF" +
        "7A4FFFFF1554FFFFCA58FFFF985DFFFF8062FFFF8067FFFF986CFFFFC671FFFF0A77FFFF647CFFFFD181FFFF5387FFFFE68CFFFF8C92FFFF4298FFFF089EFFFFDEA3FFFFC2A9FFFFB3AFFFFFB0B5FFFFB9BBFFFFCCC1FFFFE9C7FFFF0FCEFFFF3CD4FFFF" +
        "70DAFFFFAAE0FFFFE8E6FFFF2BEDFFFF70F3FFFFB8F9FFFF";
    // vertex numbering (3 hooks), vertex buffer 4608 -> 8192 entries, then the call to the builder
    static readonly string[][][] EDGE_HOOKS = {
        new[] {
            new[] { "0x2AE01B", "8B1DE88C4300", "E8A0C91B0090" },
            new[] { "0x2AE033", "31C980FC030F94C0", "E8A0C91B00909090" },
            new[] { "0x2AF4B9", "81C4300100005D5F5EC3", "E932B51B009090909090" },
            new[] { "0x2ADEFF", "B800BE0200", "B800E00400" },
            new[] { "0x29E2F0", "E893C00000", "E80BC01C00" }
        },
        new[] {
            new[] { "0x2AE02B", "8B1D388C4300", "E890C91B0090" },
            new[] { "0x2AE043", "31C980FC030F94C0", "E890C91B00909090" },
            new[] { "0x2AF4C9", "81C4300100005D5F5EC3", "E922B51B009090909090" },
            new[] { "0x2ADF0F", "B800BE0200", "B800E00400" },
            new[] { "0x29E300", "E893C00000", "E8FBBF1C00" }
        },
    };
    static bool OptEdge = true;                     // ini edge_objects
    static int edgeState = 0;                       // 1 on, -1 off or unavailable

    static void TryEdge(int lang)
    {
        edgeState = -1;
        if (!OptEdge) return;
        foreach (string[] h in EDGE_HOOKS[lang])
        {
            byte[] cur = Read(Convert.ToUInt32(h[0], 16), h[1].Length / 2);
            if (!Same(cur, Hex(h[1])) && !Same(cur, Hex(h[2]))) { Say("Edge objects unavailable: unexpected game code", 300, 300); return; }
        }
        foreach (string[] c in EDGE_CAVES[lang])
        {
            uint at = Convert.ToUInt32(c[0], 16); byte[] code = Hex(c[1]), cur = Read(at, code.Length);
            bool free = true;
            foreach (byte x in cur) if (x != 0) { free = false; break; }
            if (!free && !Same(cur, code)) { Say("Edge objects unavailable: its memory area is not free", 300, 300); return; }
        }
        Write(EDGE_SIN, Hex(EDGE_SINTAB));
        WriteInt(EDGE_MODE, 2);
        foreach (string[] c in EDGE_CAVES[lang]) Write(Convert.ToUInt32(c[0], 16), Hex(c[1]));   // code first, then the calls
        foreach (string[] h in EDGE_HOOKS[lang]) Write(Convert.ToUInt32(h[0], 16), Hex(h[2]));
        edgeState = 1;
    }

    static int FovDegrees() { int d; return OptFov > 0 && int.TryParse(FOV_NAMES[OptFov], out d) ? d : 0; }

    static void TryFov()
    {
        bool wide = WideActive(), trueWide = TrueWideActive();
        if (trueWide && hdState == 0) return;      // true 16:9 needs the HD view: wait for it
        if (hdState != 1) trueWide = false;
        fovState = -1; wideHooks = false;
        int deg = FovDegrees();
        if (deg == 0 && !wide && !trueWide) return;
        // GAME in stretched 16:9 = the view widened x4/3 as before (100.9 deg); in true 16:9 the HD view is simply
        // wider than the cockpit window, the centre keeps the chosen field of view
        double k = deg > 0 ? 1 / (1.1 * Math.Tan(deg * Math.PI / 360)) : (wide ? 0.75 : 1);
        for (int lang = 0; lang < 2; lang++)
        {
            HdPayload.Use(lang == 1, false);
            if (ReadUInt(HdPayload.PoolVar) != HdPayload.PoolBase) continue;   // __FF.EXE not prepared (24 KB)
            byte[] code = Hex(FOV_CODE[lang]);
            byte[] h = Read(FOV_HOOK[lang], 7), c = Read(FOV_CAVE + 0x10, code.Length);
            bool free = true;
            foreach (byte x in c) if (x != 0) { free = false; break; }
            if (!(Same(h, FOV_HOOK_OLD) || Same(h, FOV_HOOK_NEW[lang]) || Same(h, TRUE_A_NEW[lang])) || !(free || Same(c, code))) continue;
            byte[] hb = Read(WIDE_HOOK_B[lang], 5), hc = Read(WIDE_HOOK_C[lang], 5);
            bool wideOk = wide && (Same(hb, WIDE_B_OLD) || Same(hb, WIDE_B_NEW[lang])) && (Same(hc, WIDE_C_OLD[lang]) || Same(hc, WIDE_C_NEW[lang]));
            if (wide && !wideOk) { if (deg == 0) return; k /= 0.75; }   // 16:9 left to the old way: plain field of view
            bool trueOk = trueWide && (Same(hb, WIDE_B_OLD) || Same(hb, TRUE_B_NEW[lang])) && (Same(hc, WIDE_C_OLD[lang]) || Same(hc, TRUE_C_NEW[lang]));
            if (trueOk)
            {
                byte[] tc = Read(TRUE_A, 0x100);
                bool tfree = true;
                foreach (byte x in tc) if (x != 0) { tfree = false; break; }
                trueOk = tfree || Same(Read(TRUE_A, TRUE_A_CODE[lang].Length / 2), Hex(TRUE_A_CODE[lang]));
            }
            // HUD stretched: the cockpit window is 848/640 wider on screen and shows that much more of the 3D, the
            // chosen field of view is the window's (GAME stays the game's zoom: 100.6 deg across, like STRETCHED)
            if (trueOk && deg > 0 && OptWideHud) k *= 848.0 / 640;
            byte[] data = new byte[16];
            BitConverter.GetBytes((int)Math.Round(65536 * (wideOk || trueOk ? k : (deg > 0 ? k : 1)))).CopyTo(data, 0);
            BitConverter.GetBytes(wideOk ? 0x15555 : 0x10000).CopyTo(data, 4);
            BitConverter.GetBytes(wideOk ? 0xC000 : 0x10000).CopyTo(data, 8);
            Write(FOV_CAVE, data);
            Write(FOV_CAVE + 0x10, code);           // the code first, then the call to it
            if (trueOk) Write(TRUE_A, Hex(TRUE_A_CODE[lang]));
            Write(FOV_HOOK[lang], trueOk ? TRUE_A_NEW[lang] : FOV_HOOK_NEW[lang]);
            if (wideOk) { Write(WIDE_HOOK_B[lang], WIDE_B_NEW[lang]); Write(WIDE_HOOK_C[lang], WIDE_C_NEW[lang]); wideHooks = true; }
            if (trueOk)
            {
                // the 3D library's pixel ratio stays the game's: its horizontal object scale is half-width x
                // (height x ratio / width), the canvas width cancels out, so objects already match the terrain
                Write(TRUE_B, Hex(TRUE_B_CODE[lang]));   // before the call to it
                Write(WIDE_HOOK_B[lang], TRUE_B_NEW[lang]);
            }
            if (wideOk)
            {   // the old way may already have changed the global pixel ratio: back to stock, the hooks do it now
                uint rp = ReadUInt(aRatioRef);
                foreach (uint ra in (rp > 0x1000 && rp + 4 < regStart + regSize - guestBase) ? new[] { aRatio, rp } : new[] { aRatio })
                {
                    int v = ReadInt(ra);
                    for (int r = 0; r < 2; r++) if (v == (int)(RATIO_STOCK[r] * WIDE)) WriteInt(ra, RATIO_STOCK[r]);
                }
            }
            byte[] wc = Hex(WEDGE_CODE[lang]), w = Read(WEDGE_HOOK[lang], 5), wcur = Read(WEDGE_CAVE, wc.Length);
            bool wfree = true;
            foreach (byte x in wcur) if (x != 0) { wfree = false; break; }
            if ((Same(w, WEDGE_OLD) || Same(w, WEDGE_NEW[lang])) && (wfree || Same(wcur, wc)))
            {
                Write(WEDGE_CAVE, wc);
                Write(WEDGE_HOOK[lang], WEDGE_NEW[lang]);
            }
            fovState = 1;
            if (deg > 0 || wide || trueWide) TryEdge(lang);
            Say("Field of view " + (deg > 0 ? deg + " degrees" : (wide ? "100.9 degrees (16:9)" : "84.5 degrees")) + (wideHooks ? ", 16:9 camera" : "") +
                (trueWide ? ", true 16:9" : "") + (edgeState == 1 ? ", edge objects" : "") + " (the game: 84.5)", 0);
            return;
        }
        Say("Field of view unavailable: start the game from this tool (it prepares __FF.EXE)", 300, 300);
    }

    static int SmoothCells()
    {
        int n = farTerrain ? SMOOTH_FAR[OptDetail] : SMOOTH_TO[OptDetail];
        if (OptSmoothCells > 0) n = Math.Min(OptSmoothCells, farTerrain ? 48 : 24);
        return n;
    }

    static void TrySmooth()
    {
        // prepared exe: map shift "shl ebx,7" at the first key of the quad builder
        farTerrain = false;
        for (int lang = 0; lang < 2; lang++)
            if (Same(Read(TERRAIN_SHIFT[lang], 3), new byte[] { 0xC1, 0xE3, 0x07 })) farTerrain = true;
        if (farTerrain)
        {
            smoothState = 1;
            Say("Smooth terrain up to " + SmoothCells() + " cells (the game: 12)", 0);
            return;
        }
        for (int lang = 0; lang < 2; lang++)
        {
            bool ok = true;
            for (int i = 0; i < 3 && ok; i++)
            {
                byte[] cur = Read(SMOOTH_AT[lang][i], 4);
                ok = Same(cur, SMOOTH_OLD[i]) || Same(cur, SMOOTH_NEW[i]);
            }
            if (!ok) continue;
            for (int i = 0; i < 3; i++) Write(SMOOTH_AT[lang][i], SMOOTH_NEW[i]);
            smoothState = 1;
            Say("Smooth terrain up to " + SmoothCells() + " cells (the game: 12)", 0);
            return;
        }
        smoothState = -1;
        Say("Smooth terrain unavailable: unsupported game version", 300, 300);
    }
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

    // HD mode: __FF.EXE French (GOG) and English (GOG / Steam), original and with object 3 enlarged by 16 KB (room for the HD code)
    const string HD_SHA_ORIGINAL = "5fec09ddcb5803f587b048e9d62ae1f948e69d7137612459a9660ac31e0f82b3";
    const string EN_SHA = "b762c54509f1716282c4d357a72013b8d84ba25f17b0483b733678ff81e63dd1";   // English v1.09, GOG and Steam
    const string HD_SHA_READY = "4e2aa4851bf8cd4832e19660bfcd333fcc9735e94e8b4b6c24b73e0223ccd97a";
    const string EN_SHA_READY = "86fd95cc3ab30929cfaac50b96c1462e331089a931bd4239448e9dbb3c81fbbd";   // English, 16 KB more memory
    // ... plus the far smooth terrain (TERRAIN_EXE); beta 3 left the exe in the READY state above, it is upgraded
    const string HD_SHA_READY2 = "f22a68b97146595fa93ae14aef064654d4b1dd43fc5eac01dd01b85e7380553b";   // first beta 4 builds
    const string EN_SHA_READY2 = "e32cab7c164e1b5334c85e1c2504580856735647e183f9d7b7798794c31e9607";
    const string HD_SHA_READY3 = "7118722688f6df83bbd49e84bfae7c52f906f540b7e76e007830834216ecdafd";   // + zoom clamp
    const string EN_SHA_READY3 = "da65ac1bc7542b4819edb48ed52de4cf23d460072ae5dd7bd8a7bd46d9b1a9c6";
    const string HD_SHA_READY4 = "fcf516dc17b13ff0c5c1931b6a8953d189e80bd49f3f56de5cc2f02ae82e1af9";   // object 3 +24 KB
    const string EN_SHA_READY4 = "63aa6a1d72ed81b18be91e8869b69cdef303e7d0021153f54b21dd953b5764c2";
    const string HD_SHA_READY5 = "40622bfcf3cc486917cd0416ec4c83d96dfbd958ad48be842ce78532415f87b5";   // + 6400 clipping points
    const string EN_SHA_READY5 = "0d3a21ad4159fa86673e66a21d0d34b0b3dd3ae87e6cbb71410dc083c9f36dca";
    static bool KnownSha(string sha)
    {
        return sha == HD_SHA_ORIGINAL || sha == HD_SHA_READY || sha == HD_SHA_READY2 || sha == HD_SHA_READY3 || sha == HD_SHA_READY4 || sha == HD_SHA_READY5 ||
               sha == EN_SHA || sha == EN_SHA_READY || sha == EN_SHA_READY2 || sha == EN_SHA_READY3 || sha == EN_SHA_READY4 || sha == EN_SHA_READY5;
    }
    static bool EnglishSha(string sha) { return sha == EN_SHA || sha == EN_SHA_READY || sha == EN_SHA_READY2 || sha == EN_SHA_READY3 || sha == EN_SHA_READY4 || sha == EN_SHA_READY5; }

    // Far smooth terrain. The polygons of the near ground are joined through a map of shared vertices that the
    // engine indexes with 6 bits per axis (64x64, so 31 cells at most), and its buffers are allocated every
    // frame from a 312 KB render pool. In the exe: the map goes to 7 bits per axis (128x128: masks 0x3F -> 0x7F,
    // shifts 6 -> 7, at all its writers and the quad builder; the rasterizer reads it unmasked), the buffers
    // are sized for 48 cells with a 2x margin (4096 quads, 4608 vertices, 6400 clipping points) and the pool to 1.5 MB (the game
    // runs with 30 MB). The pool has to be set before the game allocates it at start-up, hence the file.
    // Addresses are the game's (file offset = address - 0x1AAB5C), French then English.
    struct ExePatch
    {
        public uint Fr, En; public string Old, New, Prev;   // Prev: what an earlier TNPlus wrote there (upgrade)
        public ExePatch(uint fr, uint en, string o, string n) { Fr = fr; En = en; Old = o; New = n; Prev = null; }
        public ExePatch(uint fr, uint en, string o, string p, string n) { Fr = fr; En = en; Old = o; New = n; Prev = p; }
    }
    static readonly ExePatch[] TERRAIN_EXE = {
        new ExePatch(0x2AA2EB, 0x2AA2FB, "80E33F", "80E37F"), new ExePatch(0x2AA2EE, 0x2AA2FE, "80E13F", "80E17F"), new ExePatch(0x2AA2F5, 0x2AA305, "C1E306", "C1E307"),
        new ExePatch(0x2AA308, 0x2AA318, "80E13F", "80E17F"), new ExePatch(0x2AA30E, 0x2AA31E, "C1E106", "C1E107"), new ExePatch(0x2AA311, 0x2AA321, "80E33F", "80E37F"),
        new ExePatch(0x2AA327, 0x2AA337, "80E33F", "80E37F"), new ExePatch(0x2AA32A, 0x2AA33A, "80E13F", "80E17F"), new ExePatch(0x2AA331, 0x2AA341, "C1E106", "C1E107"),
        new ExePatch(0x2AA346, 0x2AA356, "80E13F", "80E17F"), new ExePatch(0x2AA349, 0x2AA359, "80E33F", "80E37F"), new ExePatch(0x2AA350, 0x2AA360, "C1E106", "C1E107"),
        new ExePatch(0x2AE676, 0x2AE686, "83E23F", "83E27F"), new ExePatch(0x2AE679, 0x2AE689, "83E03F", "83E07F"), new ExePatch(0x2AE67C, 0x2AE68C, "C1E206", "C1E207"),
        new ExePatch(0x2AE788, 0x2AE798, "83E53F", "83E57F"), new ExePatch(0x2AE78B, 0x2AE79B, "83E23F", "83E27F"), new ExePatch(0x2AE792, 0x2AE7A2, "C1E206", "C1E207"),
        new ExePatch(0x2AE9B6, 0x2AE9C6, "83E23F", "83E27F"), new ExePatch(0x2AE9B9, 0x2AE9C9, "83E03F", "83E07F"), new ExePatch(0x2AE9BC, 0x2AE9CC, "C1E206", "C1E207"),
        new ExePatch(0x2AEB7E, 0x2AEB8E, "83E73F", "83E77F"), new ExePatch(0x2AEB83, 0x2AEB93, "C1E706", "C1E707"), new ExePatch(0x2AEB86, 0x2AEB96, "83E03F", "83E07F"),
        new ExePatch(0x2AED99, 0x2AEDA9, "83E03F", "83E07F"), new ExePatch(0x2AED9C, 0x2AEDAC, "C1E006", "C1E007"), new ExePatch(0x2AEDB6, 0x2AEDC6, "83E03F", "83E07F"),
        new ExePatch(0x2AF058, 0x2AF068, "83E03F", "83E07F"), new ExePatch(0x2AF05B, 0x2AF06B, "83E23F", "83E27F"), new ExePatch(0x2AF05E, 0x2AF06E, "C1E006", "C1E007"),
        new ExePatch(0x2AF1A2, 0x2AF1B2, "83E73F", "83E77F"), new ExePatch(0x2AF1AE, 0x2AF1BE, "C1E706", "C1E707"), new ExePatch(0x2AF1B1, 0x2AF1C1, "83E03F", "83E07F"),
        new ExePatch(0x2AF38F, 0x2AF39F, "83E73F", "83E77F"), new ExePatch(0x2AF394, 0x2AF3A4, "C1E706", "C1E707"), new ExePatch(0x2AF397, 0x2AF3A7, "83E03F", "83E07F"),
        new ExePatch(0x2ADEF0, 0x2ADF00, "B800400000", "B800000100"),   // vertex map 16 KB -> 64 KB
        new ExePatch(0x2AB0C4, 0x2AB0D4, "B8C03E0000", "B840000200"),   // quads 500 -> 4096
        new ExePatch(0x2ADEFF, 0x2ADF0F, "B842720000", "B800BE0200"),   // vertices 750 -> 4608
        new ExePatch(0x2ADF0E, 0x2ADF1E, "B8007D0000", "B800000400"),   // triangles 1000 -> 8192
        new ExePatch(0x2B0511, 0x2B0521, "B8E0790000", "B8C0F30000", "B800CF0300"),   // clipping points 800 -> 6400 (*)
        new ExePatch(0x2B0C7C, 0x2B0C8C, "81FB20030000", "81FB40060000", "81FB00190000"),   // its limit
        new ExePatch(0x2B0DC1, 0x2B0DD1, "81FB20030000", "81FB40060000", "81FB00190000"),   // its limit
        new ExePatch(0x2B0EE5, 0x2B0EF5, "81FB20030000", "81FB40060000", "81FB00190000"),   // its limit
        new ExePatch(0x2B1012, 0x2B1022, "81FB20030000", "81FB40060000", "81FB00190000"),   // its limit
        new ExePatch(0x2B1136, 0x2B1146, "81FB20030000", "81FB40060000", "81FB00190000"),   // its limit
        // N (end of the polygon ring) read where the builder takes it, clamped to 48: each zoom step multiplies
        // all the ring distances (x1.29, x1.58, x1.81), 48 became 62-87 and overran the engine's tables at once
        new ExePatch(0x2A8980, 0x2A8990, "8D14850000000029C28B5C243CC1E20201DA31C0668B425F89453C",
                                         "6BD00C0354243C0FB7425F83F8307605B83000000089453C909090"),
        new ExePatch(0x2C4F71, 0x2C4EE1, "B800E00400", "B800001000", "B800001800"),   // render pool 312 KB -> 1.5 MB (malloc)
        new ExePatch(0x2C4F76, 0x2C4EE6, "BA00E00400", "BA00001000", "BA00001800"),   // and its recorded size
    };
    static readonly uint[] TERRAIN_SHIFT = { 0x2AA2F5, 0x2AA305 };   // its first "shl ebx,6", French / English
    const uint EXE_CODE_DELTA = 0x1AAB5C;
    const uint OBJ3_SIZE = 0x10F9B0, OBJ3_HD = 0x10F9B0 + 0x6000;             // French: object 3 virtual size
    const uint OBJ3_SIZE_EN = 0x10F900, OBJ3_HD_EN = 0x10F900 + 0x6000;       // English
    // (24 KB since the true 16:9: the HD tables for 848 columns, then the field of view hooks; 16 KB before)
    static int hdState = 0;                         // 0 waiting, 1 active, -1 unavailable (reason said)
    static int hdEarly = 0;                         // checks that found the code still loading (attach right at start-up)

    // Projectile hits above ~30 fps. Every frame the game casts a ray from each projectile over the distance it
    // travels in that frame, walked in steps of 2.0 (engine units) through the entity grid, and the number of
    // steps is TRUNCATED: distance / 2.0. The pulsar travels less than 2.0 per frame above ~30 fps, so the
    // count is 0, no grid cell is looked at and nothing can be hit - the "multipulsar / drones cannot be hit
    // at high CPU cycles" bug (the game only hits the ground, which uses another test). Rounding the step
    // count UP instead ((distance + step - 1) / step) restores the hits at any frame rate.
    //   __FF.EXE 0x3117D3 (French) / 0x311553 (English): mov edx,eax / sar edx,16 / shl eax,16 / idiv ebx / sar eax,16
    //   becomes                                             lea eax,[eax+ebx-1] / cdq / idiv ebx / nop x6
    static readonly uint[] HITFIX_AT = { 0x3117D3, 0x311553 };
    static readonly byte[] HITFIX_OLD = { 0x89, 0xC2, 0xC1, 0xFA, 0x10, 0xC1, 0xE0, 0x10, 0xF7, 0xFB, 0xC1, 0xF8, 0x10 };
    static readonly byte[] HITFIX_NEW = { 0x8D, 0x44, 0x18, 0xFF, 0x99, 0xF7, 0xFB, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
    static int hitFixState = 0;                     // 0 to do, 1 applied, -1 unavailable (said once)

    // Walking and every physics body ran faster at high frame rates (measured: 2.0 game units per second at
    // 37 fps, 3.3 at 76 fps). The rigid-body engine steps in 30 ms slices and keeps the remainder of each
    // frame below 10 ms for the next one, but after a frame where that remainder was simulated anyway it never
    // cleared it: from then on every frame simulated its own time PLUS the old remainder. The fix clears it.
    //   __FF.EXE 0x3121F7 (French) / 0x311F77 (English): jmp +6 (EB 06) becomes xor ebp,ebp (31 ED), which falls
    //   into the "mov [remainder], ebp" that follows. Measured with the fix: 1.9 units/s at 37 and at 76 fps.
    static readonly uint[] PHYSFIX_AT = { 0x3121F7, 0x311F77 };
    static readonly byte[] PHYSFIX_OLD = { 0xEB, 0x06, 0x89, 0x2D }, PHYSFIX_NEW = { 0x31, 0xED, 0x89, 0x2D };
    // Second part: the engine skipped the physics of any frame shorter than 10 ms (carried to the next one), and
    // above ~80 fps DOSBox gives 8 ms frames: 1 frame in 6 without movement = stutter when walking. Every frame's
    // remainder is now simulated: cmp ebp,0x28F becomes cmp ebp,0 (0x3121DA French / 0x311F5A English).
    static readonly uint[] PHYSFIX2_AT = { 0x3121DA, 0x311F5A };
    static readonly byte[] PHYSFIX2_OLD = { 0x81, 0xFD, 0x8F, 0x02, 0x00, 0x00 }, PHYSFIX2_NEW = { 0x81, 0xFD, 0x00, 0x00, 0x00, 0x00 };
    static int physFixState = 0;

    // Black screen at the end of a mission: the game frees the cockpit's click zones but keeps testing the
    // mouse against them for a moment; the routine that walks a zone's shape (a list of spans per row) then
    // follows freed memory and can loop forever. The routine is rewritten in place, same size, to stop after
    // 255 spans of one row (a real shape has 1 to 3). Same bytes and same place in the French and English exe.
    static readonly uint[] UIFIX_AT = { 0x2F268C, 0x2F249C };
    static readonly byte[] UIFIX_OLD = { 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x04, 0x89, 0x5D, 0xFC, 0x8B, 0x5D, 0xFE, 0x66, 0x85, 0xDB, 0x7C, 0x2F, 0x66, 0x39, 0xDA, 0x7E, 0x2A, 0x8B, 0x55, 0xFC, 0xC1, 0xFA, 0x10, 0x8B, 0x00, 0xC1, 0xE2, 0x03, 0x01, 0xD0, 0x74, 0x1B, 0x8B, 0x55, 0xFC, 0x66, 0x3B, 0x10, 0x7C, 0x0C, 0x66, 0x3B, 0x50, 0x02, 0x7F, 0x06, 0xB0, 0x01, 0x89, 0xEC, 0x5D, 0xC3, 0x8B, 0x40, 0x04, 0x85, 0xC0, 0x75, 0xE8, 0x30, 0xC0, 0x89, 0xEC, 0x5D, 0xC3, 0x8B, 0xC0 };
    static readonly byte[] UIFIX_NEW = { 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x04, 0x89, 0x5D, 0xFC, 0x8B, 0x5D, 0xFE, 0x66, 0x85, 0xDB, 0x7C, 0x33, 0x66, 0x39, 0xDA, 0x7E, 0x2E, 0x8B, 0x55, 0xFC, 0xC1, 0xFA, 0x10, 0x8B, 0x00, 0xC1, 0xE2, 0x03, 0x01, 0xD0, 0x74, 0x1F, 0x8B, 0x55, 0xFC, 0xB3, 0xFF, 0x66, 0x3B, 0x10, 0x7C, 0x0A, 0x66, 0x3B, 0x50, 0x02, 0x7F, 0x04, 0xB0, 0x01, 0xEB, 0x0D, 0x8B, 0x40, 0x04, 0x85, 0xC0, 0x74, 0x04, 0xFE, 0xCB, 0x75, 0xE6, 0x30, 0xC0, 0xC9, 0xC3 };
    static int uiFixState = 0;

    // Object draw distance. The engine has no model LOD: objects simply pop in and out. Scenery (bushes, trees,
    // rocks) is only collected within 20 terrain cells, whatever the game's detail setting, and units and
    // buildings have a range per type loaded with each mission (20 cells for PBAs and units, up to 90 for some
    // buildings). Before raising them, the fixed limits that more objects would hit are lifted: the per-frame
    // object list (400 -> 1200 entries, two of its writers have no overflow check), the side buckets of the
    // entity walk (emptied up to 128 instead of the 3rd terrain ring) and the 120-row cap of that walk.
    // Ranges are kept at 120 cells at most (the side buckets hold 128). FAR: scenery 30, ranges x2; MAX: 40, x3.
    // Measured (HD, 500000 cycles): 17 -> 48 objects drawn on the same view, 66 -> 63.5 fps.
    struct CodePatch { public uint Fr, En; public string Old, OldEn, New; }
    static readonly CodePatch[] OBJ_PATCHES = {
        new CodePatch { Fr = 0x2A0F1C, En = 0x2A0F2C, Old = "B8C0120000", OldEn = "B8C0120000", New = "B840380000" },
        new CodePatch { Fr = 0x2A14E2, En = 0x2A14F2, Old = "663D9001", OldEn = "663D9001", New = "663DB004" },
        new CodePatch { Fr = 0x2A16CC, En = 0x2A16DC, Old = "6681F99001", OldEn = "6681F99001", New = "6681F9B004" },
        new CodePatch { Fr = 0x2B3B16, En = 0x2B3B26, Old = "3B0570CB4300", OldEn = "3B05C0CA4300", New = "3D8000000090" },
        new CodePatch { Fr = 0x2B584C, En = 0x2B585C, Old = "83F978", OldEn = "83F978", New = "83F97F" },
        new CodePatch { Fr = 0x2B55F7, En = 0x2B5607, Old = "66A138CC4300", OldEn = "66A188CB4300", New = "" },   // scenery radius
    };
    static readonly uint[] OBJ_TABLE = { 0x41ED70, 0x41ECC0 };   // ranges, 9 classes x 128 types, French / English
    static int objState = 0, objLang = -1;          // 0 to do, 1 applied, -1 unavailable
    static byte[] objWritten = null;

    static byte[] Hex(string h)
    {
        byte[] b = new byte[h.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
        return b;
    }

    // code part, at the main menu (like the other fixes)
    static void TryObjDist()
    {
        int radius = OptObjDist == 1 ? 30 : 40;
        for (int lang = 0; lang < 2; lang++)
        {
            bool all = true, done = true;
            foreach (CodePatch p in OBJ_PATCHES)
            {
                byte[] old = Hex(lang == 0 ? p.Old : p.OldEn);
                byte[] nw = p.New != "" ? Hex(p.New) : new byte[] { 0xB8, (byte)radius, 0, 0, 0, 0x90 };
                byte[] cur = Read(lang == 0 ? p.Fr : p.En, old.Length);
                if (!Same(cur, nw)) done = false;
                if (!Same(cur, old) && !Same(cur, nw)) all = false;
            }
            if (!all) continue;
            objLang = lang;
            if (!done)
                foreach (CodePatch p in OBJ_PATCHES)
                    Write(lang == 0 ? p.Fr : p.En, p.New != "" ? Hex(p.New) : new byte[] { 0xB8, (byte)radius, 0, 0, 0, 0x90 });
            objState = 1; objWritten = null;
            Say("Object distance " + OBJDIST_NAMES[OptObjDist] + ": scenery up to " + radius + " cells, unit and building ranges x" + (OptObjDist + 1), 0);
            return;
        }
        objState = -1;
        Say("Object distance unavailable: unsupported game version", 300, 300);
    }

    // data part: each mission loads its own range table; it is scaled once, when it shows up
    static void ObjDistTable()
    {
        uint at = OBJ_TABLE[objLang];
        byte[] cur = Read(at, 9 * 128);
        if (objWritten != null && Same(cur, objWritten)) return;
        bool any = false;
        foreach (byte b in cur) if (b != 0) { any = true; break; }
        if (!any) return;                           // no mission loaded yet
        int f = OptObjDist + 1;
        byte[] nw = new byte[cur.Length];
        for (int i = 0; i < cur.Length; i++) nw[i] = (byte)Math.Min(120, cur[i] * f);
        Write(at, nw);
        objWritten = nw;
    }
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
    // The game's clock in ms (advances once per mission frame, stops at the end of the mission and in menus).
    // Freelook only writes the cursor once per new game frame: when a mission ends the game frees the cockpit's
    // click zones, and a cursor pushed after that (warp) made the game test a mouse event against freed memory
    // and loop forever (black screen at the end of a mission). Known for the GOG / Steam executables only.
    static uint aClock = 0;
    static int lastClock = int.MinValue;
    static uint aFogTable, aWeather, aResFlags, aResMode, aResCallback, aResButton;
    static uint aRatio, aRatioRef, aScaleX, aTerrain, aG3Width;
    static bool haveFog, have400, haveWide, haveDetail, haveStereo;
    static uint aStereo;
    static bool launchedWide = false;               // DOSBox started by us with the 16:9 overlay
    static bool OptWideAttach = false;              // ini only (tests): 16:9 also on a game we attached to
    static bool OptWideTrue = false;                // 16:9: false STRETCHED (DOSBox stretches, camera corrected), true TRUE
    static bool launchedTrue = false;               // DOSBox started by us for the true 16:9 (VESA 848x480)
    static readonly string[] WIDE_NAMES = { "OFF", "STRETCHED", "TRUE" };
    static int WideMode() { return OptWide ? (OptWideTrue ? 2 : 1) : 0; }
    static void SetWideMode(int m) { OptWide = m > 0; OptWideTrue = m == 2; }

    // true 16:9 (HD only): the game's HD screen is a VESA 848x480 mode of DOSBox, the 3D view drawn across the whole
    // width in its own columns (HdPayload*Wide); the cockpit and HUD stretched to the full width (wide_hud = 1) or
    // centred at their own proportions, black above and below the 3D on the sides (wide_hud = 0)
    static bool OptWideHud = true;                  // ini wide_hud
    static bool TrueWideActive()
    {
        return OptWide && OptWideTrue && OptHD && ((launchedTrue && launched != null && gamePid == launched.Id) || OptWideAttach);
    }

    // the stretched 16:9 camera correction is running on this game
    static bool WideActive()
    {
        return OptWide && haveWide && !TrueWideActive() && ((launchedWide && launched != null && gamePid == launched.Id) || OptWideAttach);
    }
    static bool launchedDemo = false;               // DOSBox started by us on one of the 1996 demos
    static Process launched = null;                 // the DOSBox process started from the menu
    static bool frozen = false, clipped = false;
    static CtrlHandler onClose;

    static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    static string IniPath { get { return Path.Combine(ExeDir, "TNPlus.ini"); } }

    // ================================================================== entry point
    [STAThread]
    static void Main(string[] args)
    {
        Console.Title = TITLE + " " + VERSION;
        HdPayload.Use(false, false);
        LoadSettings();
        onClose = ev => { Release(); return false; };
        SetConsoleCtrlHandler(onClose, true);
        bool consoleMenu = Array.IndexOf(args, "--console") >= 0;   // the old text menu
        int ip = Array.IndexOf(args, "--pid");                      // tests: attach to this DOSBox only
        if (ip >= 0 && ip + 1 < args.Length) int.TryParse(args[ip + 1], out OnlyPid);
        while (consoleMenu ? Menu() : GuiMenu()) Run();   // back to the menu when a game started from it closes
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

    // HD needs the full game's GOG or Steam executable, French or English (the demos are not supported yet)
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
                    hdOk = KnownSha(sha);
                    hdWhy = hdOk ? "" : "not available for this version of the game";
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
        OptWideTrue = p == 2;
        OptDetail = p;                              // GAME / SHARP / SHARPER
        OptDistance = p;                            // NORMAL / FAR / MAX
        OptFreelook = p > 0;
        OptNoclip = p > 0;
    }

    static int CurrentPreset()
    {
        for (int p = 0; p < 3; p++)
            if (Display == p && OptWide == (p == 2) && OptWideTrue == (p == 2) && OptDetail == p && OptDistance == p
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
            try     // the menu is taller than the classic 30-line console
            {
                if (Console.BufferWidth < 92) Console.SetBufferSize(92, Console.BufferHeight);
                if (Console.WindowWidth < 92 || Console.WindowHeight < 30)
                    Console.SetWindowSize(Math.Min(Math.Max(Console.WindowWidth, 92), Console.LargestWindowWidth), Math.Min(Math.Max(Console.WindowHeight, 30), Console.LargestWindowHeight));
            }
            catch { }
            Console.Clear();
            // header
            Console.WriteLine();
            C("  TERRA NOVA PLUS  ", ConsoleColor.Black, ConsoleColor.DarkCyan);
            C(" " + VERSION, ConsoleColor.Cyan);
            C("    Strike Force Centauri quality-of-life pack\n", ConsoleColor.Gray);
            C("  made by ", ConsoleColor.DarkGray); C("skw-1337", ConsoleColor.White);
            C("  ·  github.com/skw-1337/terra-nova-plus\n", ConsoleColor.DarkGray);
            Rule('═');
            // game line
            C("  GAME  ", ConsoleColor.DarkGray);
            if (GameDir == "") C("not found: start the game yourself and press A\n", ConsoleColor.Yellow);
            else
            {
                string kind = InstallKind(GameDir), lang = ExeLanguage(GameDir);
                ConsoleColor bg = kind == "STEAM" ? ConsoleColor.DarkBlue : kind == "GOG" ? ConsoleColor.DarkMagenta : ConsoleColor.DarkGray;
                C(" " + kind + " ", ConsoleColor.White, bg);
                if (lang != "") C(" " + lang, ConsoleColor.Gray);
                C("  " + Shorten(GameDir, 50), ConsoleColor.Gray);
                if (installs.Count > 1) { C("   G ", ConsoleColor.Cyan); C("switch (" + installs.Count + ")", ConsoleColor.DarkGray); }
                Console.WriteLine();
            }
            Rule('─');
            // two columns: the settings on the left (one line each, the chosen value only), what will be
            // launched and the in-game keys on the right; the help of an option is shown under the table
            // for the key pressed last
            bool awe = AweRom() != null;
            int music = EffectiveMusic();
            List<List<Seg>> left = new List<List<Seg>>(), right = new List<List<Seg>>();
            left.Add(Sec("PICTURE"));
            left.Add(Opt("1", "Display", Val(DISPLAY_LABELS[EffectiveDisplay()]), "BETA", 0));
            left.Add(Opt("2", "Widescreen 16:9", Val(WIDE_NAMES[WideMode()]), OptWideTrue ? "BETA" : null, 0));
            left.Add(Opt("3", "Terrain detail", Val(DETAIL_NAMES[OptDetail]), "BETA", 0));
            left.Add(Opt("4", "View distance", Val(DIST_NAMES[OptDistance]), null, ScanDistance));
            left.Add(Opt("O", "Object distance", Val(OBJDIST_NAMES[OptObjDist]), "BETA", 0));
            left.Add(Opt("V", "Field of view", Val(FOV_NAMES[OptFov]), "BETA", 0));
            left.Add(Sec("CONTROLS"));
            left.Add(Opt("5", "Mouse freelook", Sw(OptFreelook), null, ScanFreelook));
            left.Add(Opt("6", "Noclip", Sw(OptNoclip), null, ScanNoclip));
            left.Add(Sec("SOUND"));
            left.Add(Opt("7", "Stereo fix", Sw(OptStereoFix), "BETA", ScanStereo));
            left.Add(Opt("8", "Music", Val(MUSIC_NAMES[music]), music == 3 ? "EXPERIMENTAL" : "BETA", 0));
            left.Add(Sec("GAMEPLAY"));
            left.Add(Opt("9", "Projectile hit fix", Sw(OptHitFix), "BETA", 0));
            left.Add(Opt("0", "Physics speed fix", Sw(OptPhysFix), "BETA", 0));
            right.Add(Sec("LAUNCH"));
            right.Add(Line(S("  "), S(" L ", ConsoleColor.Black, ConsoleColor.Cyan), S(" "), S(" " + TARGET_NAMES[LaunchTarget] + " ", ConsoleColor.Black, ConsoleColor.Yellow),
                S(LaunchTarget > 0 ? "  320x400, no HD" : "  the full game", ConsoleColor.DarkGray)));
            right.Add(Line(S("  "), S(" P ", ConsoleColor.Black, ConsoleColor.Cyan), S(" "),
                preset < 0 ? S(" CUSTOM ", ConsoleColor.Black, ConsoleColor.Yellow) : S(" " + PRESET_NAMES[preset] + " ", ConsoleColor.Black, ConsoleColor.Yellow),
                S(preset < 0 ? "  your own settings" : "  preset", ConsoleColor.DarkGray)));
            foreach (string w in Wrap(preset < 0 ? "P picks a preset again" : PRESET_INFO[preset], 36)) right.Add(Line(S("     " + w, ConsoleColor.DarkGray)));
            right.Add(Line());
            right.Add(Sec("IN GAME"));
            right.Add(Line(S("  "), B(ScanFreelook), S(" freelook  ", ConsoleColor.DarkGray), B(ScanNoclip), S(" noclip  ", ConsoleColor.DarkGray), B(ScanDistance), S(" view", ConsoleColor.DarkGray)));
            right.Add(Line(S("  "), B(ScanStereo), S(" stereo swap  ", ConsoleColor.DarkGray), B(ScanSmoothing), S(" HD smoothing", ConsoleColor.DarkGray)));
            right.Add(Line());
            right.Add(Line(S("  "), S(" BETA ", ConsoleColor.Black, ConsoleColor.DarkYellow), S(" new in 1.1.0  ", ConsoleColor.DarkGray), S(" EXPERIMENTAL ", ConsoleColor.White, ConsoleColor.DarkRed), S(" unfinished", ConsoleColor.DarkGray)));
            for (int i = 0; i < Math.Max(left.Count, right.Count); i++)
            {
                int n = i < left.Count ? Put(left[i]) : 0;
                Console.Write(new string(' ', Math.Max(0, 42 - n)));
                if (i < right.Count) Put(right[i]);
                Console.WriteLine();
            }
            Rule('─');
            // help for the key pressed last, and the warnings that matter
            if (Display == 2 && !hdOkNow) Warn("HD 640x400 " + why + ": 320x400 is used");
            if (demoMissing) Warn("demo " + LaunchTarget + " not found in the game folder (see README)");
            switch (lastKey)
            {
                case '1': Note("HD 640x400: the 3D view drawn at twice the width, GOG and Steam, French and English; " + KeyName(ScanSmoothing) + " toggles the smoothing"); break;
                case '2': Note("widescreen: the camera is corrected for a 16:9 DOSBox window (needs launching from here)"); break;
                case '3': Note("more ground detail far away (steep walls stop looking like a saw); costs 10-20 % fps"); break;
                case '4': Note("view distance at mission start; " + KeyName(ScanDistance) + " cycles it in game"); break;
                case '5': Note("mouse freelook: " + KeyName(ScanFreelook) + " in game; sensitivity and inverted look in TNPlus.ini"); break;
                case '6': Note("noclip: " + KeyName(ScanNoclip) + " in game, fly through everything; speed in TNPlus.ini"); break;
                case '7': Note("DOSBox and the game's SB16 driver swap left and right; " + KeyName(ScanStereo) + " swaps the effects to compare"); break;
                case '8':
                    Note(MUSIC_INFO[music]);
                    if (!awe) Note("optional AWE32 music: put awe32.raw (AWE32 ROM, 1 MB, not included) next to TNPlus.exe, see README");
                    else if (music == 3) Note("AWE32: barely tested, some instruments may sound off");
                    break;
                case '9': Note("above ~30 fps the game cannot hit moving targets (multipulsar, drones): fixed at any speed"); break;
                case 'L': Note("the two 1996 demos shipped with GOG and Steam have missions the full game hasn't; no HD there yet"); break;
                default: Note("presets set 1 to 6 at once; stereo fix, music and the hit fix are yours to choose"); break;
            }
            Rule('─');
            C("  ENTER ", ConsoleColor.Black, ConsoleColor.Green); C(" launch      ", ConsoleColor.Gray);
            Key("A"); C("attach to a running game      ", ConsoleColor.Gray);
            Key("Q"); C("quit\n", ConsoleColor.Gray);
            Rule('═');
            C("  Anti-cheat: this tool edits DOSBox's memory like a game trainer. It never touches other\n", ConsoleColor.DarkGray);
            C("  programs, but QUIT IT (Q) before playing online games protected by an anti-cheat.\n", ConsoleColor.DarkGray);
            Console.ResetColor();
            ConsoleKeyInfo k = Console.ReadKey(true);
            lastKey = char.ToUpperInvariant(k.KeyChar);
            switch (lastKey)
            {
                case 'P': ApplyPreset(preset < 0 ? 2 : (preset + 1) % 3); break;
                case 'L': LaunchTarget = (LaunchTarget + 1) % 3; break;
                case '1': Display = (Display + 1) % 3; break;
                case '2': SetWideMode((WideMode() + 1) % 3); break;
                case '3': OptDetail = (OptDetail + 1) % 3; break;
                case '4': OptDistance = (OptDistance + 1) % 3; break;
                case 'O': OptObjDist = (OptObjDist + 1) % 3; break;
                case 'V': OptFov = (OptFov + 1) % FOV_NAMES.Length; break;
                case '5': OptFreelook = !OptFreelook; break;
                case '6': OptNoclip = !OptNoclip; break;
                case '7': OptStereoFix = !OptStereoFix; break;
                case '8': OptMusic = (EffectiveMusic() + 1) % (AweRom() != null ? 4 : 3); break;
                case '9': OptHitFix = !OptHitFix; break;
                case '0': OptPhysFix = !OptPhysFix; break;
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
    static char lastKey = 'P';                      // the help line under the table follows the last key
    struct Seg { public string T; public ConsoleColor Fg, Bg; public bool HasBg; }
    static Seg S(string t) { return new Seg { T = t, Fg = ConsoleColor.Gray }; }
    static Seg S(string t, ConsoleColor fg) { return new Seg { T = t, Fg = fg }; }
    static Seg S(string t, ConsoleColor fg, ConsoleColor bg) { return new Seg { T = t, Fg = fg, Bg = bg, HasBg = true }; }
    static Seg B(int scan) { return S(" " + KeyName(scan) + " ", ConsoleColor.White, ConsoleColor.DarkMagenta); }
    static Seg Val(string v) { return S(" " + v + " ", ConsoleColor.Black, ConsoleColor.Yellow); }
    static Seg Sw(bool on) { return on ? S(" ON ", ConsoleColor.Black, ConsoleColor.Green) : S(" off ", ConsoleColor.DarkGray); }
    static List<Seg> Line(params Seg[] segs) { return new List<Seg>(segs); }
    static List<Seg> Sec(string name) { return Line(S("  " + name, ConsoleColor.DarkCyan)); }
    // one setting: key, label, value, optional badge and in-game key
    static List<Seg> Opt(string key, string label, Seg value, string tag, int scan)
    {
        List<Seg> l = Line(S("   "), S(" " + key + " ", ConsoleColor.Black, ConsoleColor.Cyan), S(" " + label.PadRight(19)), value);
        if (tag != null) { l.Add(S(" ")); l.Add(tag == "EXPERIMENTAL" ? S(" " + tag + " ", ConsoleColor.White, ConsoleColor.DarkRed) : S(" " + tag + " ", ConsoleColor.Black, ConsoleColor.DarkYellow)); }
        if (scan != 0) { l.Add(S(" ")); l.Add(B(scan)); }
        return l;
    }
    static int Put(List<Seg> l)
    {
        int n = 0;
        foreach (Seg g in l) { if (g.HasBg) C(g.T, g.Fg, g.Bg); else C(g.T, g.Fg); n += g.T.Length; }
        return n;
    }
    static List<string> Wrap(string s, int width)
    {
        List<string> o = new List<string>(); string cur = "";
        foreach (string w in s.Split(' '))
        {
            if (cur.Length + w.Length + 1 > width && cur != "") { o.Add(cur); cur = ""; }
            cur = cur == "" ? w : cur + " " + w;
        }
        if (cur != "") o.Add(cur);
        return o;
    }
    static void C(string s, ConsoleColor fg) { Console.ForegroundColor = fg; Console.Write(s); Console.ResetColor(); }
    static void C(string s, ConsoleColor fg, ConsoleColor bg)
    {
        Console.ForegroundColor = fg; Console.BackgroundColor = bg; Console.Write(s); Console.ResetColor();
    }
    static void Rule(char c) { C("  " + new string(c, 88) + "\n", ConsoleColor.DarkCyan); }
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
    // row with a status badge after the label (and a dim word saying what the badge is about)
    static void Row(string k, string label, string tag, string about)
    {
        Console.Write("   "); Key(k); C(label + " ", ConsoleColor.Gray);
        Tag(tag);
        if (about != null) C(" " + about, ConsoleColor.DarkGray);
        Console.Write(new string(' ', Math.Max(1, 34 - label.Length - 1 - (tag.Length + 2) - (about != null ? about.Length + 1 : 0))));
    }
    // status badges: BETA = new in this version (dark yellow), EXPERIMENTAL = unfinished (red)
    static void Tag(string t)
    {
        if (t == "EXPERIMENTAL") C(" " + t + " ", ConsoleColor.White, ConsoleColor.DarkRed);
        else C(" " + t + " ", ConsoleColor.Black, ConsoleColor.DarkYellow);
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
            if (KnownSha(sha)) exeLang = EnglishSha(sha) ? "English" : "French";
        }
        catch { }
        return exeLang;
    }

    // Music: AWE32 and Roland = General MIDI on the MPU-401 (DOSBox plays it with FluidSynth and the AWE32 bank,
    // or with the system synthesizer), FM = the original Sound Blaster synthesis. Written in the TN.CFG of the game
    // or demo being launched (original kept once).
    static void ApplyMusic(string dirName)
    {
        int music = EffectiveMusic();
        if (music == 2) return;
        try
        {
            string cfg = Path.Combine(GameDir, dirName, "TN.CFG");
            if (!File.Exists(cfg)) return;
            string[][] want = music != 1
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
            Console.WriteLine("Music set to " + (music == 1 ? "FM" : "General MIDI") + " (" + dirName + "\\TN.CFG, original kept as TN.CFG.tnplus-original).");
        }
        catch { }
    }

    // AWE32: the game's FF.SBK and the AWE32 ROM turned into a SoundFont 2 for DOSBox's FluidSynth (see AweBank.cs),
    // rebuilt at each launch. Without the ROM (awe32.raw next to TNPlus.exe) the choice does not exist.
    static string AweRom()
    {
        string rom = Path.Combine(ExeDir, "awe32.raw");
        try { return File.Exists(rom) && new FileInfo(rom).Length == AweBank.ROM_SIZE ? rom : null; }
        catch { return null; }
    }
    static int EffectiveMusic() { return OptMusic == 3 && AweRom() == null ? 0 : OptMusic; }

    static string PrepareAwe(string dirName)
    {
        try
        {
            string sbk = Path.Combine(GameDir, dirName, "SOUND", "FF.SBK");
            if (!File.Exists(sbk)) sbk = Path.Combine(GameDir, "TNOVA", "SOUND", "FF.SBK");
            if (!File.Exists(sbk)) { Console.WriteLine("AWE32 music: FF.SBK not found, the Windows MIDI synthesizer is used."); return null; }
            if (!File.Exists(AweBank.DlsPath())) { Console.WriteLine("AWE32 music: " + AweBank.DlsPath() + " not found, the Windows MIDI synthesizer is used."); return null; }
            byte[] sf2 = AweBank.Build(sbk, AweBank.DlsPath(), File.ReadAllBytes(AweRom()));
            string path = Path.Combine(ExeDir, "TNPlus_awe32.sf2");
            File.WriteAllBytes(path, sf2);
            string conf = Path.Combine(ExeDir, "TNPlus_music.conf");
            File.WriteAllText(conf, "# Terra Nova Plus: AWE32 music (the game's FF.SBK bank) played by FluidSynth\r\n" +
                "[midi]\r\nmididevice = fluidsynth\r\n[fluidsynth]\r\nsoundfont = " + path + "\r\n");
            Console.WriteLine("Music: AWE32 bank with the AWE32 ROM samples.");
            return conf;
        }
        catch (Exception e)
        {
            Console.WriteLine("AWE32 music unavailable (" + e.Message + "), the Windows MIDI synthesizer is used.");
            return null;
        }
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
    // The editions ship DOSBox with few CPU cycles (Steam: 115000) and the game is CPU bound in the emulator, HD
    // even more (measured on the same mission in HD: 14.5 fps at 115000 cycles, 36.5 at 300000). The launch
    // conf imposes cpu_cycles unless the setting is 0, with cpu_throttle: some views cost the host far more per
    // emulated cycle (infrared: 0.55 -> 0.70 core at 700000 in the same scene, the game then runs code it
    // generates in memory), and when the host cannot keep up DOSBox lowers the cycles for a moment instead of
    // falling behind (lag and choppy sound). The game copes with a varying frame rate (hit and physics fixes).
    static string CpuSection(string db)
    {
        if (CpuCycles <= 0) return "";
        string cur = "?";
        try
        {
            Match m = Regex.Match(File.ReadAllText(Path.Combine(db, "dosbox_terranova_windows.conf")), @"(?m)^\s*cpu_cycles\s*=\s*(\S+)");
            if (m.Success) cur = m.Groups[1].Value;
        }
        catch { }
        if (cur != CpuCycles.ToString()) Console.WriteLine("DOSBox CPU cycles: " + CpuCycles + " (the game's own config says " + cur + "; cpu_cycles in TNPlus.ini, 0 = leave it)");
        return "[cpu]\r\ncpu_cycles = " + CpuCycles + "\r\ncpu_throttle = true\r\n";
    }

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
        if (OptKeepDos)                             // tests: the DOS window stays after the game, with its last message
            text = Regex.Replace(text, @"(?im)^\s*EXIT\s*$", "REM EXIT");
        string path = Path.Combine(ExeDir, "TNPlus_launch.conf");
        File.WriteAllText(path, CpuSection(db) + text);
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
        if ((OptHD || OptDetail > 0 || OptFov > 0 || OptWide) && !demo)
        {
            string why;
            hdExeReady = PrepareExeForHd(out why);
            Console.WriteLine(hdExeReady ? "__FF.EXE: " + why : "HD 640x400 and far smooth terrain unavailable: " + why);
            if (!OptHD) hdExeReady = false;
        }
        string db = Path.Combine(GameDir, "_DOSBOX");
        string args = "-conf dosbox_terranova_windows.conf -conf \"" + WriteLaunchConf(db, LaunchTarget) + "\"";
        if (OptMusic == 3)
            Console.WriteLine(AweRom() != null ? "AWE32: awe32.raw found."
                : "AWE32: awe32.raw (AWE32 ROM, 1 MB) not found next to TNPlus.exe, Roland GS music instead (see README).");
        if (EffectiveMusic() == 3)
        {
            string music = PrepareAwe(TARGET_DIRS[LaunchTarget]);
            if (music != null) args += " -conf \"" + music + "\"";
        }
        if (OptWide)
        {
            string overlay = Path.Combine(ExeDir, "TNPlus_16x9.conf");
            bool trueWide = OptWideTrue && OptHD && hdExeReady;
            File.WriteAllText(overlay, trueWide
                ? "# Terra Nova Plus: true 16:9 (missions in DOSBox's VESA 848x480; the mouse is captured on click so that it\r\n" +
                  "# stays on the game's cursor over the centred cockpit)\r\n" +
                  "[dosbox]\r\nvesa_modes = all\r\n[render]\r\naspect = stretch\r\ninteger_scaling = off\r\n" +
                  "[mouse]\r\nmouse_capture = onclick\r\n"
                : "# Terra Nova Plus: stretch the 4:3 picture to 16:9 (the tool widens the field of view to match)\r\n" +
                  "[render]\r\naspect = stretch\r\ninteger_scaling = off\r\n");
            launchedTrue = trueWide;
            args += " -conf \"" + overlay + "\"";
        }
        args += " -noconsole";
        ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(db, "dosbox-staging.exe"), args);
        psi.WorkingDirectory = db;
        psi.UseShellExecute = false;
        launched = Process.Start(psi);
        launchedWide = OptWide;
        if (!OptWide) launchedTrue = false;
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
        if (OptHD) Console.WriteLine("HD 640x400: missions in 320x400 are shown in HD, " + KeyName(ScanSmoothing) + " toggles the 3D smoothing.");
        Console.WriteLine("When you are done playing, close this window (anti-cheat note: see README).");
        Console.WriteLine();
        attached = false;
        if (launched != null)
            ShowWindow(GetConsoleWindow(), 6);      // minimize: never steal the game's focus
        else                                        // attach mode: stay visible until the game is found
            Say("Waiting for Terra Nova: start the game (this window minimizes once it is found).", 0);

        int vkFree = (int)MapVirtualKey((uint)ScanFreelook, 1), vkClip = (int)MapVirtualKey((uint)ScanNoclip, 1);
        int vkDist = (int)MapVirtualKey((uint)ScanDistance, 1), vkOptions = (int)MapVirtualKey(0x18, 1);
        int vkSmooth = (int)MapVirtualKey((uint)ScanSmoothing, 1);
        bool prevS = false, prevSt = false;
        int vkStereo = (int)MapVirtualKey((uint)ScanStereo, 1);
        double lastHd = -10, lastHit = -10, lastPhys = -10, lastObj = -10;
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
                    Release(); Detach(); launched = null; launchedWide = false; launchedTrue = false;
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
                    hitFixState = 0; physFixState = 0; uiFixState = 0; objState = 0; objWritten = null; smoothState = 0; fovState = 0;
                    hdEarly = 0;
                    if (hdState == 1 || launched == null) hdState = 0;   // game left (back to the GOG launcher too):
                                                                          // new attempt when it starts again
                    if (now - lastAttach > 2) { lastAttach = now; TryAttach(); }
                    mouse.Take(); Thread.Sleep(50); continue;
                }
                bool fg = ForegroundIsGame();

                // --- HD: injected as soon as the game is loaded, before its first mission
                if (OptHD && hdState == 0 && now - lastHd > 0.5) { lastHd = now; TryHdInject(); }
                if (OptHitFix && hitFixState == 0 && now - lastHit > 0.5) { lastHit = now; TryHitFix(); }
                if (OptPhysFix && physFixState == 0 && now - lastPhys > 0.5) { lastPhys = now; TryPhysFix(); }
                if (OptObjDist > 0 && objState == 0 && now - lastPhys > 0.5) TryObjDist();
                if (OptDetail > 0 && smoothState == 0 && now - lastPhys > 0.5) TrySmooth();
                if ((OptFov > 0 || WideActive() || TrueWideActive()) && fovState == 0 && now - lastPhys > 0.5) TryFov();
                if (objState == 1 && now - lastObj > 0.5) { lastObj = now; ObjDistTable(); }
                if (uiFixState == 0 && now - lastPhys > 0.5)
                    uiFixState = TryFix(UIFIX_AT, UIFIX_OLD, UIFIX_NEW, "End-of-mission freeze guard", "no more black screen when a mission ends");
                bool s = hdState == 1 && fg && Down(vkSmooth);
                if (s && !prevS)
                {
                    HdSmoothing = ReadInt(HdPayload.Smoothing) == 0;
                    WriteInt(HdPayload.Smoothing, HdSmoothing ? 1 : 0);
                    Say("HD smoothing " + (HdSmoothing ? "ON" : "OFF"), HdSmoothing ? 1000 : 600);
                }
                prevS = s;
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
                bool newFrame = true;                       // a game frame went by since the last cursor write
                if (aClock != 0)
                {
                    int clk = ReadInt(aClock);
                    newFrame = clk != lastClock;
                    if (newFrame) lastClock = clk;
                }
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
                    if (newFrame && w >= 200 && w <= 640 && h >= 200 && h <= 480)
                    {
                        byte[] c = new byte[4];
                        BitConverter.GetBytes((short)(w / 2)).CopyTo(c, 0);
                        BitConverter.GetBytes((short)Math.Round(h * CENTRE_Y)).CopyTo(c, 2);
                        Write(aCursor, c);                      // cursor used for aiming
                        Write(aDraw, c);                        // where the reticle is drawn
                        Write(aWarp, new byte[] { 1 });         // mouse lib: push cursor to driver, skip reading
                    }
                    if (newFrame) { Write(aFreeze, new byte[] { 1 }); frozen = true; }   // no reticle trails on the cockpit
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
        int near = smoothState == 1 ? SmoothCells() : BitConverter.ToInt16(b, 0x5C + 12 + 9);
        int[] to = { BitConverter.ToInt16(b, 0x5C + 9), near, d[0], d[1], d[2], d[3] };
        bool same = true;
        for (int k = 1; k < 6 && same; k++) same = BitConverter.ToInt16(b, 0x5C + 12 * k + 9) == to[k];
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
        if (WideActive() && !wideHooks)
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
        if (sha == HD_SHA_READY5 || sha == EN_SHA_READY5) { msg = "ready"; return true; }
        if (!KnownSha(sha)) { msg = "unknown __FF.EXE (GOG French, GOG English and Steam are supported)"; return false; }
        bool english = EnglishSha(sha);
        uint size = english ? OBJ3_SIZE_EN : OBJ3_SIZE, hd = english ? OBJ3_HD_EN : OBJ3_HD;
        int le = IndexOf(d, new byte[] { (byte)'L', (byte)'E', 0, 0 }, 0);
        int at = le + BitConverter.ToInt32(d, le + 0x40) + 24 * 2;        // object table, object 3: virtual size
        uint cur = le < 0 ? 0 : BitConverter.ToUInt32(d, at);
        if (cur != size && cur != size + 0x4000 && cur != hd) { msg = "unexpected executable layout"; return false; }
        // the backup is always the untouched exe (a beta 3 READY exe without backup is turned back first)
        byte[] orig = (byte[])d.Clone();
        BitConverter.GetBytes(size).CopyTo(orig, at);
        BitConverter.GetBytes(hd).CopyTo(d, at);
        foreach (ExePatch p in TERRAIN_EXE)
        {
            byte[] old = Hex(p.Old), nw = Hex(p.New);
            int o = (int)((english ? p.En : p.Fr) - EXE_CODE_DELTA);
            byte[] c = new byte[old.Length];
            Array.Copy(d, o, c, 0, c.Length);
            if (!Same(c, old) && !Same(c, nw) && !(p.Prev != null && Same(c, Hex(p.Prev))))
            { msg = "unexpected bytes in __FF.EXE"; return false; }   // new / Prev: earlier betas
            nw.CopyTo(d, o);
            old.CopyTo(orig, o);
        }
        if (Sha256(orig) != (english ? EN_SHA : HD_SHA_ORIGINAL)) { msg = "unexpected __FF.EXE content"; return false; }
        if (Sha256(d) != (english ? EN_SHA_READY5 : HD_SHA_READY5)) { msg = "__FF.EXE update failed"; return false; }
        string backup = exe + ".tnplus-original";
        try
        {
            if (!File.Exists(backup)) File.WriteAllBytes(backup, orig);
            File.WriteAllBytes(exe, d);
        }
        catch (Exception e) { msg = "cannot update __FF.EXE (" + e.Message + ")"; return false; }
        if (Sha256(File.ReadAllBytes(exe)) != (english ? EN_SHA_READY5 : HD_SHA_READY5)) { msg = "__FF.EXE update failed"; return false; }
        msg = "prepared (HD memory, far smooth terrain; original kept as __FF.EXE.tnplus-original)";
        return true;
    }

    static string Sha256(byte[] d)
    {
        using (var h = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(h.ComputeHash(d)).Replace("-", "").ToLowerInvariant();
    }

    // as early as possible too (same DOSBox code cache remark as the HD below); the French and the English
    // executables are told apart by the bytes found at the two addresses
    static void TryHitFix()
    {
        hitFixState = TryFix(HITFIX_AT, HITFIX_OLD, HITFIX_NEW, "Projectile hit fix", "weapons hit at any frame rate");
    }

    static void TryPhysFix()
    {
        physFixState = TryFix(PHYSFIX_AT, PHYSFIX_OLD, PHYSFIX_NEW, "Physics speed fix", "walking speed no longer depends on the frame rate");
        if (physFixState == 1) TryFix(PHYSFIX2_AT, PHYSFIX2_OLD, PHYSFIX2_NEW, "Physics smoothing", "movement on every frame");
    }

    static int TryFix(uint[] ats, byte[] old, byte[] nw, string name, string what)
    {
        foreach (uint at in ats)
        {
            byte[] cur = Read(at, old.Length);
            if (Same(cur, nw)) return 1;
            if (!Same(cur, old)) continue;
            Write(at, nw);
            if (!Same(Read(at, nw.Length), nw)) break;
            Say(name + " ON: " + what, 0);              // silent: one beep for the whole start-up is enough (HD)
            return 1;
        }
        Say(name + " unavailable: unsupported game version", 300, 300);
        return -1;
    }

    // the HD code must be in place before the first mission runs: DOSBox caches the game code it has
    // translated, later writes to that code would be ignored (the temporary pool is still unused until then)
    static void TryHdInject()
    {
        HdPayload.Use(ExeLanguage(GameDir) == "English", TrueWideActive());   // the code and its addresses depend on the executable
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
        uint pool = allOld ? ReadUInt(HdPayload.PoolVar) : 0;
        if (allOld && pool == 0) return;               // the game has not allocated its pool yet: wait
        if (!allOld && ++hdEarly < 20) return;         // just loaded: DOS/4GW may still be relocating it, look again
        if (!allOld) why = "unsupported game version";
        else if (pool != HdPayload.PoolBase)
            why = "__FF.EXE not prepared (start the game from this tool with option 6 on)";
        else if (ReadUInt(HdPayload.PoolMax) != 0)
            why = "a mission already ran in this game: restart the game from this tool";
        else
        {
            byte[] zone = Read(HdPayload.Zone, (int)(HdPayload.End - HdPayload.Zone));
            foreach (byte zb in zone) if (zb != 0) { why = "its memory area is not free"; break; }
        }
        if (why != null) { hdState = -1; Say("HD 640x400 unavailable: " + why, 300, 300); return; }
        Write(HdPayload.Data, HdPayload.DataInit);
        Write(HdPayload.Code, HdPayload.CodeBytes);
        for (int i = 0; i < n; i++) Write(HdPayload.PatchAt[i], HdPayload.PatchNew[i]);
        WriteInt(HdPayload.Smoothing, HdSmoothing ? 1 : 0);
        WriteInt(HdPayload.HudFilter, 0);              // Scale2x HUD: left in the payload, no longer offered
        if (HdPayload.HudStretch != 0) WriteInt(HdPayload.HudStretch, OptWideHud ? 1 : 0);
        hdState = 1;
        Say("HD 640x400 ready: missions in 320x400 will be in HD (" + KeyName(ScanSmoothing) + " smoothing " +
            (HdSmoothing ? "ON" : "OFF") + ")", 1000);
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

    static bool OptKeepDos = false;                // ini keep_dos (tests): no EXIT after the game
    static int OnlyPid = 0;                         // --pid N: attach to that DOSBox only (tests next to a game)

    static void TryAttach()
    {
        foreach (Process p in Process.GetProcesses())
        {
            if (!p.ProcessName.ToLowerInvariant().Contains("dosbox")) continue;
            if (launched != null && p.Id != launched.Id) continue;   // started from here: that DOSBox only
            if (OnlyPid != 0 && p.Id != OnlyPid) continue;
            IntPtr h = OpenProcess(0x0010 | 0x0020 | 0x0008 | 0x0400, false, p.Id);
            if (h == IntPtr.Zero) continue;
            hProc = h; gamePid = p.Id;
            if (Scan())
            {
                string lang = ExeLanguage(GameDir);
                aClock = lang == "French" ? 0x446B98u : lang == "English" ? 0x446AE8u : 0;
                lastClock = int.MinValue;
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
        int speedVer = 0;
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
                    case "widescreen": SetWideMode(v == "2" || v.ToUpperInvariant() == "TRUE" ? 2 : v == "0" ? 0 : 1); break;
                    case "stereo_fix": OptStereoFix = v != "0"; break;
                    case "music": OptMusic = Math.Max(0, Array.IndexOf(MUSIC_NAMES, v.ToUpperInvariant())); break;
                    case "hd": oldHd = v != "0"; break;
                    case "hd_smoothing": HdSmoothing = v != "0"; break;
                    case "hit_fix": OptHitFix = v != "0"; break;
                    case "phys_fix": OptPhysFix = v != "0"; break;
                    case "smooth_cells": int.TryParse(v, out OptSmoothCells); break;
                    case "wide_attach": OptWideAttach = v != "0"; break;
                    case "wide_hud": OptWideHud = v != "0"; break;
                    case "keep_dos": OptKeepDos = v != "0"; break;
                    case "edge_objects": OptEdge = v != "0"; break;
                    case "field_of_view": OptFov = Math.Max(0, Array.IndexOf(FOV_NAMES, v.ToUpperInvariant())); break;
                    case "object_distance": OptObjDist = Math.Max(0, Array.IndexOf(OBJDIST_NAMES, v.ToUpperInvariant())); break;
                    case "key_smoothing": ScanSmoothing = Convert.ToInt32(v, 16); break;
                    case "key_stereo": ScanStereo = Convert.ToInt32(v, 16); break;
                    case "sensitivity_x": SensX = int.Parse(v); break;
                    case "sensitivity_y": SensY = int.Parse(v); break;
                    case "invert_y": InvertY = v == "1"; break;
                    case "noclip_speed": NoclipSpeed = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "cpu_cycles": CpuCycles = int.Parse(v); break;
                    case "speed_version": int.TryParse(v, out speedVer); break;
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
        // old defaults (300000 before beta 3, 500000 before the 48-cell terrain): the game is limited by the
        // cycles, not by the PC (DOSBox used 0.6 core at 700000 in HD on the test PC)
        if (speedVer < 3 && (CpuCycles == 300000 || CpuCycles == 500000)) CpuCycles = 700000;
        if (speedVer < 4 && CpuCycles == 700000) CpuCycles = 1000000;   // the default before the true 16:9 measurements
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
                "; 0 off, 1 stretched (DOSBox stretches the picture), 2 true 16:9 (HD only: the 3D drawn in 848 columns)\r\nwidescreen = " + WideMode() + "\r\n" +
                "; true 16:9: 1 = cockpit and HUD stretched to the full width, 0 = centred at their proportions (black on the sides)\r\nwide_hud = " + (OptWideHud ? 1 : 0) + "\r\n" +
                "; 1 = swap the Sound Blaster stereo in DOSBox (the game's SB16 driver reverses left and right)\r\nstereo_fix = " + (OptStereoFix ? 1 : 0) + "\r\n" +
                "; ROLAND, FM, GAME'S OWN or AWE32 (music of the game and demos; AWE32 needs awe32.raw, an AWE32 ROM dump,\r\n" +
                "; next to TNPlus.exe, otherwise ROLAND is used)\r\nmusic = " + MUSIC_NAMES[OptMusic] + "\r\n" +
                "; HD smoothing at start (toggled in game with key_smoothing)\r\nhd_smoothing = " + (HdSmoothing ? 1 : 0) + "\r\n" +
                "; 1 = projectiles hit at any frame rate (the game misses moving targets above ~30 fps: multipulsar, drones)\r\nhit_fix = " + (OptHitFix ? 1 : 0) + "\r\n" +
                "; 1 = physics (walking, jumps, falls) at the same speed whatever the frame rate\r\nphys_fix = " + (OptPhysFix ? 1 : 0) + "\r\n" +
                "; GAME, 90, 100 or 110: horizontal field of view of the 3D view in degrees (the game: 84.5)\r\nfield_of_view = " + FOV_NAMES[OptFov] + "\r\n" +
                "; GAME, FAR or MAX: how far bushes, trees, units and buildings are drawn\r\nobject_distance = " + OBJDIST_NAMES[OptObjDist] + "\r\n" +
                "; 1 = terrain and objects up to the screen edges with a wider field of view or true 16:9 (beta)\r\nedge_objects = " + (OptEdge ? 1 : 0) + "\r\n" +
                (OptWideAttach ? "wide_attach = 1\r\n" : "") +
                (OptKeepDos ? "keep_dos = 1\r\n" : "") +
                (OptSmoothCells > 0 ? "; tests: forced end of the smooth terrain (cells)\r\nsmooth_cells = " + OptSmoothCells + "\r\n" : "") +
                "; mouse sensitivity (heading / pitch units per mouse count), 1 = inverted vertical look\r\n" +
                "sensitivity_x = " + SensX + "\r\nsensitivity_y = " + SensY + "\r\ninvert_y = " + (InvertY ? 1 : 0) + "\r\n" +
                "; noclip speed in game units per second\r\nnoclip_speed = " + NoclipSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                "; DOSBox CPU cycles imposed at launch (the editions ship 115000, too slow for HD), 0 = leave the game's own setting.\r\n" +
                "; true 16:9 HD, SHARPER terrain: 700000 = ~40 fps, 1000000 = ~57, 1200000 = ~70 (about one full CPU core).\r\n; DOSBox lowers them when your PC cannot keep up\r\ncpu_cycles = " + CpuCycles + "\r\nspeed_version = 4\r\n" +
                "; keys as PHYSICAL key scancodes (hex): 15 = Y, 16 = U, 24 = J (QWERTY/AZERTY),\r\n" +
                "; 29 = key left of 1, 3B..44 = F1..F10 (41 = F7), 58 = F12\r\n" +
                "key_freelook = " + ScanFreelook.ToString("X2") + "\r\nkey_noclip = " + ScanNoclip.ToString("X2") + "\r\n" +
                "key_distance = " + ScanDistance.ToString("X2") + "\r\nkey_smoothing = " + ScanSmoothing.ToString("X2") + "\r\n" +
                "key_stereo = " + ScanStereo.ToString("X2") + "\r\n" +
                "; 0 = no beeps\r\nsound = " + (Sound ? 1 : 0) + "\r\n" +
                "; game folder (empty = auto-detect GOG / Steam)\r\ngame_dir = " + GameDir + "\r\n");
        }
        catch { }
    }
}
