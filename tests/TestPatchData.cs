// Terra Nova Plus tests - the patch data, without the game: hex, pieces and their sizes, hooks and the caves they reach,
// the HD payloads, the generated SHA-256s, and the map of the free area (also with TN+'s own writers run on fake memory).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ================================================================== kit (Data*)
        // Language indexes: TNPlus.cs's own tables and HdPayloadFr / HdPayloadEn go French (0) then English (1); the tables
        // printed by tools/*.py (TurretFix, DescramblerFix, AshfordFix, FillerFix, NativeFixes, IrFix, IrInPlace, AffineFix,
        // ViewFix, SpanInPlace) and SMOKE_AT go English then French (Data_Tables_GeneratedOnesAreEnglishFirst). Each writer
        // only loops over its own table, so the two orders never meet in TN+; DataKit.GenOf maps one to the other here.
        static class DataKit
        {
            internal static readonly string[] LANG = { "French", "English" };
            internal static readonly string[] VARIANT = { "HD 640x400", "true 16:9 848x480", "true 16:9 1280x720" };
            internal static int GenOf(int lang) { return 1 - lang; }

            // the 24 KB the prepared __FF.EXE adds after object 3, up to the game's pool (FreeAreaReady: pool at PoolBase)
            internal static uint FreeStart { get { return HdPayloadFr.Zone; } }
            internal static uint FreeEnd { get { return HdPayloadFr.PoolBase; } }

            internal static void Use(int lang, int variant) { HdPayload.Use(lang == 1, variant > 0, variant == 2); }
            internal static string Of(int lang, int variant) { return LANG[lang] + " " + VARIANT[variant]; }
            internal static string X(uint a) { return "0x" + a.ToString("X"); }

            internal static bool IsDigit(char c) { return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'); }
            internal static bool IsDigits(string s)
            {
                if (string.IsNullOrEmpty(s)) return false;
                foreach (char c in s) if (!IsDigit(c)) return false;
                return true;
            }
            // what Hex() needs: an even number of hex digits (an odd last one would be dropped without a word)
            internal static bool IsHex(string s) { return IsDigits(s) && s.Length % 2 == 0; }
            // what Sha256() gives and every check compares with: 64 lower-case hex digits
            internal static bool IsSha(string s)
            {
                if (s == null || s.Length != 64) return false;
                foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
                return true;
            }
            internal static bool AllZeroHex(string s) { foreach (char c in s) if (c != '0') return false; return true; }

            // pieces "B:hex" (ours) and "C:address:length" (copied from the game's memory), as TryIrFix & co read them
            internal static int PieceLen(string p) { return p[0] == 'C' ? Convert.ToInt32(p.Split(':')[2], 16) : (p.Length - 2) / 2; }
            internal static uint CopyAt(string p) { return Convert.ToUInt32(p.Split(':')[1], 16); }
            internal static int Sum(string[] pieces) { int n = 0; foreach (string p in pieces) n += PieceLen(p); return n; }
            // the block put together, the bytes copied from the game left at 0
            internal static byte[] Assemble(string[] pieces)
            {
                List<byte> b = new List<byte>();
                foreach (string p in pieces)
                    if (p[0] == 'C') b.AddRange(new byte[PieceLen(p)]); else b.AddRange(Hex(p.Substring(2)));
                return b.ToArray();
            }
            internal static int PieceAt(string[] pieces, int off)
            {
                int o = 0;
                for (int i = 0; i < pieces.Length; i++) { int n = PieceLen(pieces[i]); if (off >= o && off < o + n) return i; o += n; }
                return -1;
            }
            internal static int PieceStart(string[] pieces, int index) { int o = 0; for (int i = 0; i < index; i++) o += PieceLen(pieces[i]); return o; }

            // target of the E8 / E9 rel32 at code[off], the code being at guest address at
            internal static uint Rel(uint at, byte[] code, int off) { return (uint)((int)at + off + 5 + BitConverter.ToInt32(code, off + 1)); }
            internal static bool Reaches(uint at, byte[] code, byte op, uint target)
            {
                for (int i = 0; i + 5 <= code.Length; i++) if (code[i] == op && Rel(at, code, i) == target) return true;
                return false;
            }
            // dwords of the code that are addresses in the free area (absolute operands)
            internal static List<uint> Refs(byte[] code)
            {
                List<uint> r = new List<uint>();
                for (int i = 0; i + 4 <= code.Length; i++) { uint d = BitConverter.ToUInt32(code, i); if (d >= FreeStart && d < FreeEnd) r.Add(d); }
                return r;
            }
            internal static bool Contains(byte[] hay, byte[] needle) { return IndexOf(hay, needle, 0) >= 0; }
            internal static byte[] Cat(params byte[][] parts) { List<byte> b = new List<byte>(); foreach (byte[] p in parts) b.AddRange(p); return b.ToArray(); }
            internal static byte[] Le(uint v) { return BitConverter.GetBytes(v); }
            internal static void SetInt(byte[] b, uint off, int v) { BitConverter.GetBytes(v).CopyTo(b, (int)off); }
            // [a, b) and [c, d) share a byte
            internal static bool Overlap(uint a, uint b, uint c, uint d) { return a < d && c < b; }

            // ---- a place TN+ writes into (or reads from), and whose fix it belongs to
            internal sealed class Area
            {
                public readonly string Feature, Name; public readonly uint A, B;
                public Area(string feature, string name, uint a, int len) { Feature = feature; Name = name; A = a; B = a + (uint)len; }
                public bool Hits(Area o) { return A < o.B && o.A < B; }
                public bool Holds(uint a, int n) { return a >= A && a + (uint)n <= B; }
                public override string ToString() { return Feature + " " + Name + " [" + X(A) + ", " + X(B) + ")"; }
            }

            // Every area of the free area TN+ puts code or data into for one game (French or English): the code and data
            // of the writers, and the variables their code uses at run time (zero when written, found in their code).
            // HD: its code from Code up to End of the largest variant ([Zone, Code) is only checked free, see
            // Data_FreeArea_OnlyTurretCavesAndIrStubInTheHdZone). TRUE_C_CODE is never written.
            internal static List<Area> FreeMap(int lang)
            {
                int g = GenOf(lang);
                List<Area> m = new List<Area>();
                uint code = uint.MaxValue, end = 0;
                for (int v = 0; v < 3; v++) { Use(lang, v); code = Math.Min(code, HdPayload.Code); end = Math.Max(end, HdPayload.End); }
                m.Add(new Area("HD", "code, data and tables", code, (int)(end - code)));
                for (int i = 0; i < TurretFix.Zones; i++) m.Add(new Area("Turret", "cave " + i, TurretFix.At[g][i], TurretFix.New[g][i].Length / 2));
                m.Add(new Area("Turret", "flag", 0x469390, 4));
                for (int i = 0; i < DescramblerFix.Zones; i++) m.Add(new Area("Descrambler", "cave " + i, DescramblerFix.At[g][i], DescramblerFix.New[g][i].Length / 2));
                m.Add(new Area("Descrambler", "timers", 0x4693A0, 8));
                m.Add(new Area("Ashford", "cave", AshfordFix.At[g][0], AshfordFix.New[g][0].Length / 2));
                m.Add(new Area("Ashford", "text", AshfordFix.At[g][1], AshfordFix.New[g][1].Length / 2));
                m.Add(new Area("Filler", "cave", FillerFix.At[g][0], FillerFix.New[g][0].Length / 2));
                m.Add(new Area("Filler", "opcode and displacement", 0x4693F8, 8));
                m.Add(new Area("IR", "span stub", IrFix.StubSpan, IrFix.CodeStubSpan[g].Length / 2));
                m.Add(new Area("IR", "affine stub", IrFix.StubAffine, IrFix.CodeStubAffine[g].Length / 2));
                m.Add(new Area("IR", "span pointer", IrFix.PtrSpan, 4));
                m.Add(new Area("IR", "affine pointer", IrFix.PtrAffine, 4));
                for (int r = 0; r < ViewFix.Stub.Length; r++)
                {
                    m.Add(new Area("View", "stub " + r, ViewFix.Stub[r], ViewFix.CodeStub[g][r].Length / 2));
                    m.Add(new Area("View", "pointer " + r, ViewFix.Ptr[r], 4));
                }
                m.Add(new Area("FOV", "data", FOV_CAVE, 16));
                m.Add(new Area("FOV", "code", FOV_CAVE + 0x10, FOV_CODE[lang].Length / 2));
                m.Add(new Area("FOV", "wedge", WEDGE_CAVE, WEDGE_CODE[lang].Length / 2));
                m.Add(new Area("FOV", "true 16:9 set-up", TRUE_A, TRUE_A_CODE[lang].Length / 2));
                m.Add(new Area("FOV", "true 16:9 scale", TRUE_B, TRUE_B_CODE[lang].Length / 2));
                m.Add(new Area("FOV", "true 16:9 data", 0x469400, 8));
                m.Add(new Area("Zoom", "cave", ZOOMFIX_CAVE, 12));
                foreach (string[] c in EDGE_CAVES[lang]) m.Add(new Area("Edge", "cave " + c[0], Convert.ToUInt32(c[0], 16), c[1].Length / 2));
                m.Add(new Area("Edge", "data", 0x469410, 0x469A00 - 0x469410));
                m.Add(new Area("Keys", "data", KB_DATA, (int)(KB_DATA_END - KB_DATA)));
                m.Add(new Area("Keys", "code", KB_CODE, KB_CODE_HEX.Length / 2));
                m.Add(new Area("Keys", "sweep", KB_SWEEP, KB_SWEEP_HEX.Length / 2));
                return m;
            }

            // what each fix writes that may hold free-area addresses (its caves, and its hooks and rewrites in the game code)
            internal sealed class Blob
            {
                public readonly string Feature, Name; public readonly uint At; public readonly byte[] Code;
                public Blob(string feature, string name, uint at, byte[] code) { Feature = feature; Name = name; At = at; Code = code; }
            }
            internal static List<Blob> FreeCode(int lang)
            {
                int g = GenOf(lang);
                List<Blob> b = new List<Blob>();
                for (int i = 0; i < TurretFix.At[g].Length; i++) b.Add(new Blob("Turret", "piece " + i, TurretFix.At[g][i], Hex(TurretFix.New[g][i])));
                for (int i = 0; i < DescramblerFix.At[g].Length; i++) b.Add(new Blob("Descrambler", "piece " + i, DescramblerFix.At[g][i], Hex(DescramblerFix.New[g][i])));
                for (int i = 0; i < AshfordFix.At[g].Length; i++) b.Add(new Blob("Ashford", "piece " + i, AshfordFix.At[g][i], Hex(AshfordFix.New[g][i])));
                for (int i = 0; i < FillerFix.At[g].Length; i++) b.Add(new Blob("Filler", "piece " + i, FillerFix.At[g][i], Hex(FillerFix.New[g][i])));
                b.Add(new Blob("IR", "span stub", IrFix.StubSpan, Hex(IrFix.CodeStubSpan[g])));
                b.Add(new Blob("IR", "affine stub", IrFix.StubAffine, Hex(IrFix.CodeStubAffine[g])));
                b.Add(new Blob("IR", "span entry", IrFix.SiteSpan[g], Hex(IrFix.NewSpan[g])));
                b.Add(new Blob("IR", "affine entry", IrFix.SiteAffine[g], Hex(IrFix.NewAffine[g])));
                for (int r = 0; r < ViewFix.Stub.Length; r++)
                {
                    b.Add(new Blob("View", "stub " + r, ViewFix.Stub[r], Hex(ViewFix.CodeStub[g][r])));
                    b.Add(new Blob("View", "entry " + r, ViewFix.Site[g][r], Hex(ViewFix.New[r])));
                }
                b.Add(new Blob("FOV", "code", FOV_CAVE + 0x10, Hex(FOV_CODE[lang])));
                b.Add(new Blob("FOV", "wedge", WEDGE_CAVE, Hex(WEDGE_CODE[lang])));
                b.Add(new Blob("FOV", "true 16:9 set-up", TRUE_A, Hex(TRUE_A_CODE[lang])));
                b.Add(new Blob("FOV", "true 16:9 scale", TRUE_B, Hex(TRUE_B_CODE[lang])));
                foreach (string[] c in EDGE_CAVES[lang]) b.Add(new Blob("Edge", "cave " + c[0], Convert.ToUInt32(c[0], 16), Hex(c[1])));
                b.Add(new Blob("Keys", "code", KB_CODE, Hex(KB_CODE_HEX)));
                b.Add(new Blob("Keys", "sweep", KB_SWEEP, Hex(KB_SWEEP_HEX)));
                return b;
            }

            // Where each fix writes in the game code (outside the free area) for one game: hooks, rewrites in place, data
            // records; and, for GameReads, the game bytes copied (C pieces) or checked (SHA-256) before anything is written.
            internal static List<Area> GameWrites(int lang)
            {
                int g = GenOf(lang);
                List<Area> w = new List<Area>();
                for (int v = 0; v < 3; v++)
                {
                    Use(lang, v);
                    for (int i = 0; i < HdPayload.PatchAt.Length; i++) w.Add(new Area("HD", VARIANT[v] + " patch " + i, HdPayload.PatchAt[i], HdPayload.PatchOld[i].Length));
                }
                w.Add(new Area("FOV", "hook", FOV_HOOK[lang], 7));
                w.Add(new Area("FOV", "scale hook", WIDE_HOOK_B[lang], 5));
                w.Add(new Area("FOV", "ratio hook", WIDE_HOOK_C[lang], 5));
                w.Add(new Area("FOV", "wedge hook", WEDGE_HOOK[lang], 5));
                foreach (string[] h in EDGE_HOOKS[lang]) w.Add(new Area("Edge", "hook " + h[0], Convert.ToUInt32(h[0], 16), h[1].Length / 2));
                w.Add(new Area("Keys", "hook", KB_HOOK[lang], 8));
                w.Add(new Area("HitFix", "step count", HITFIX_AT[lang], HITFIX_OLD.Length));
                w.Add(new Area("PhysFix", "remainder", PHYSFIX_AT[lang], PHYSFIX_OLD.Length));
                w.Add(new Area("PhysFix", "short frames", PHYSFIX2_AT[lang], PHYSFIX2_OLD.Length));
                w.Add(new Area("PhysFix", "idle chance", IDLEFIX_AT[0], IDLEFIX_OLD.Length));
                w.Add(new Area("PhysFix", "idle turn", IDLEFIX2_AT[0], IDLEFIX2_OLD.Length));
                w.Add(new Area("UiFix", "span walk", UIFIX_AT[lang], UIFIX_OLD.Length));
                for (int i = 0; i < 3; i++) w.Add(new Area("Smooth", "buffer " + i, SMOOTH_AT[lang][i], SMOOTH_OLD[i].Length));
                foreach (CodePatch p in OBJ_PATCHES) w.Add(new Area("ObjDist", "patch", lang == 0 ? p.Fr : p.En, (lang == 0 ? p.Old : p.OldEn).Length / 2));
                w.Add(new Area("ObjDist", "range table", OBJ_TABLE[lang], 9 * 128));
                w.Add(new Area("Smoke", "record 11", SMOKE_AT[g][0], SMOKE_REC_OLD.Length / 2));
                for (int i = TurretFix.Zones; i < TurretFix.At[g].Length; i++) w.Add(new Area("Turret", "hook " + i, TurretFix.At[g][i], TurretFix.Old[g][i].Length / 2));
                for (int i = DescramblerFix.Zones; i < DescramblerFix.At[g].Length; i++) w.Add(new Area("Descrambler", "hook " + i, DescramblerFix.At[g][i], DescramblerFix.Old[g][i].Length / 2));
                w.Add(new Area("Ashford", "hook", AshfordFix.At[g][2], AshfordFix.Old[g][2].Length / 2));
                for (int i = 1; i < FillerFix.At[g].Length; i++) w.Add(new Area("Filler", "piece " + i, FillerFix.At[g][i], FillerFix.Old[g][i].Length / 2));
                w.Add(new Area("Sky", "in place", NativeFixes.SkyAt[g][0], NativeFixes.SkyOld[g][0].Length / 2));
                w.Add(new Area("Drone", "in place", NativeFixes.DroneAt[g][0], NativeFixes.DroneOld[g][0].Length / 2));
                w.Add(new Area("IR", "span entry", IrFix.SiteSpan[g], 6));
                w.Add(new Area("IR", "affine entry", IrFix.SiteAffine[g], 6));
                for (int e = 0; e < IrInPlace.At[g].Length; e++) w.Add(new Area("IrInPlace", "entry " + e, IrInPlace.At[g][e], IrInPlace.Size[g][e]));
                for (int r = 0; r < ViewFix.Site[g].Length; r++) w.Add(new Area("View", "entry " + r, ViewFix.Site[g][r], 6));
                w.Add(new Area("Affine", "renderer table dword", AffineFix.Table[g], 4));
                w.Add(new Area("Span", "module", SpanInPlace.At[g][0], SpanInPlace.Size[g][0]));
                foreach (ExePatch p in TERRAIN_EXE) w.Add(new Area("TerrainExe", "patch", lang == 0 ? p.Fr : p.En, p.Old.Length / 2));
                return w;
            }

            internal static List<Area> GameReads(int lang)
            {
                int g = GenOf(lang);
                List<Area> r = new List<Area>();
                Action<string, string[]> copies = (f, pieces) =>
                {
                    foreach (string p in pieces) if (p[0] == 'C') r.Add(new Area(f, "copied " + p, CopyAt(p), PieceLen(p)));
                };
                copies("IR", IrFix.Pieces[g]);
                copies("View", ViewFix.Pieces[g]);
                copies("Affine", AffineFix.Pieces[g]);
                for (int e = 0; e < IrInPlace.Pieces[g].Length; e++) copies("IrInPlace", IrInPlace.Pieces[g][e]);
                for (int i = 0; i < IrInPlace.CheckAt[g].Length; i++) r.Add(new Area("IrInPlace", "checked", IrInPlace.CheckAt[g][i], IrInPlace.CheckSize[g][i]));
                copies("Span", SpanInPlace.Pieces[g][0]);
                for (int i = 0; i < SpanInPlace.CheckAt[g].Length; i++) r.Add(new Area("Span", "checked", SpanInPlace.CheckAt[g][i], SpanInPlace.CheckSize[g][i]));
                return r;
            }

            // ---- made-up game memory
            // the game's own bytes (from TN+'s tables) where a fix expects them: the place must still be empty
            internal static void Place(FakeGame m, uint at, byte[] b, string what)
            {
                Ok(m.AllZero(at, b.Length) || Same(m.Get(at, b.Length), b), what + ": " + X(at) + " already holds other bytes (two fixes on the same place)");
                m.Put(at, b);
            }
            internal static void PlaceHdOld(FakeGame m, string what)
            {
                for (int i = 0; i < HdPayload.PatchAt.Length; i++) Place(m, HdPayload.PatchAt[i], HdPayload.PatchOld[i], what + " patch " + i);
            }
            // the game has allocated its pool where the prepared exe puts it (FreeAreaReady)
            internal static void PoolReady(FakeGame m, int lang)
            {
                m.PutUInt(lang == 1 ? HdPayloadEn.PoolVar : HdPayloadFr.PoolVar, lang == 1 ? HdPayloadEn.PoolBase : HdPayloadFr.PoolBase);
            }
            // HD on, the variant chosen; the executable's language without a game folder (ExeLanguage's cache)
            internal static void HdOptions(int lang, int variant)
            {
                OptHD = true; OptWide = variant > 0; OptWideTrue = variant > 0; OptHd720 = variant == 2; OptWideAttach = variant > 0;
                GameDir = ""; exeLangDir = ""; exeLang = LANG[lang];
            }
            // the HD data as TryHdInject leaves it
            internal static byte[] HdData()
            {
                byte[] d = (byte[])HdPayload.DataInit.Clone();
                SetInt(d, HdPayload.Smoothing - HdPayload.Data, 0);
                SetInt(d, HdPayload.HudFilter - HdPayload.Data, 0);
                if (HdPayload.HudStretch != 0) SetInt(d, HdPayload.HudStretch - HdPayload.Data, OptWideHud ? 1 : 0);
                return d;
            }
            // every area written holds what was written there, and no other byte of the free area is set
            internal static void CheckFree(FakeGame m, List<KeyValuePair<uint, byte[]>> written, string what)
            {
                foreach (KeyValuePair<uint, byte[]> w in written) EqBytes(w.Value, m.Get(w.Key, w.Value.Length), what + ": what was written at " + X(w.Key));
                for (uint a = FreeStart; a < FreeEnd; a++)
                {
                    if (m.Mem[a] == 0) continue;
                    bool known = false;
                    foreach (KeyValuePair<uint, byte[]> w in written) if (a >= w.Key && a < w.Key + (uint)w.Value.Length) { known = true; break; }
                    if (!known) Fail(what + ": byte " + X(a) + " of the free area written outside the areas expected");
                }
            }
            internal static void Wrote(List<KeyValuePair<uint, byte[]>> l, uint at, byte[] b) { l.Add(new KeyValuePair<uint, byte[]>(at, b)); }
        }

        // ================================================================== hex and pieces
        [Test] static void Data_Hex_HandWrittenStrings()
        {
            List<KeyValuePair<string, string>> all = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (n, s) => all.Add(new KeyValuePair<string, string>(n, s));
            for (int l = 0; l < 2; l++)
            {
                string L = " [" + DataKit.LANG[l] + "]";
                add("FOV_CODE" + L, FOV_CODE[l]); add("TRUE_A_CODE" + L, TRUE_A_CODE[l]); add("TRUE_B_CODE" + L, TRUE_B_CODE[l]);
                add("TRUE_C_CODE" + L, TRUE_C_CODE[l]); add("WEDGE_CODE" + L, WEDGE_CODE[l]);
                foreach (string[] c in EDGE_CAVES[l]) add("EDGE_CAVES " + c[0] + L, c[1]);
                foreach (string[] h in EDGE_HOOKS[l]) { add("EDGE_HOOKS " + h[0] + " old" + L, h[1]); add("EDGE_HOOKS " + h[0] + " new" + L, h[2]); }
            }
            add("EDGE_SINTAB", EDGE_SINTAB); add("SMOKE_REC_OLD", SMOKE_REC_OLD); add("SMOKE_REC_NEW", SMOKE_REC_NEW);
            foreach (CodePatch p in OBJ_PATCHES)
            {
                add("OBJ_PATCHES " + DataKit.X(p.Fr) + " Old", p.Old); add("OBJ_PATCHES " + DataKit.X(p.Fr) + " OldEn", p.OldEn);
                if (p.New != "") add("OBJ_PATCHES " + DataKit.X(p.Fr) + " New", p.New);
            }
            foreach (ExePatch p in TERRAIN_EXE)
            {
                add("TERRAIN_EXE " + DataKit.X(p.Fr) + " Old", p.Old); add("TERRAIN_EXE " + DataKit.X(p.Fr) + " New", p.New);
                if (p.Prev != null) add("TERRAIN_EXE " + DataKit.X(p.Fr) + " Prev", p.Prev);
            }
            add("KB_CODE_HEX", KB_CODE_HEX); add("KB_SWEEP_HEX", KB_SWEEP_HEX); add("KB_HOOK_OLD", KB_HOOK_OLD);
            add("KB_SITE_SIG", KB_SITE_SIG); add("KB_RESUME_SIG", KB_RESUME_SIG); add("KB_LIB_SIG", KB_LIB_SIG);
            foreach (KeyValuePair<string, string> e in all)
                Ok(DataKit.IsHex(e.Value), e.Key + ": an even number of hex digits (Hex() drops an odd last digit silently)");
            // the addresses in EDGE_CAVES / EDGE_HOOKS are read with Convert.ToUInt32(.., 16)
            for (int l = 0; l < 2; l++)
            {
                foreach (string[] c in EDGE_CAVES[l]) Ok(c[0].StartsWith("0x") && DataKit.IsDigits(c[0].Substring(2)), "EDGE_CAVES address " + c[0]);
                foreach (string[] h in EDGE_HOOKS[l]) Ok(h[0].StartsWith("0x") && DataKit.IsDigits(h[0].Substring(2)), "EDGE_HOOKS address " + h[0]);
            }
            // signatures: "(....)" capture, "...." wildcard, the rest whole bytes (Sig reads them two digits at a time)
            foreach (KeyValuePair<string, string> s in SIGS)
                foreach (string tok in s.Value.Split(' '))
                    Ok(tok == "(....)" || tok == "...." || DataKit.IsHex(tok), "SIGS " + s.Key + ": token \"" + tok + "\"");
        }

        [Test] static void Data_Hex_GeneratedTables()
        {
            List<KeyValuePair<string, string>> all = new List<KeyValuePair<string, string>>();
            Action<string, string[][]> add = (n, t) =>
            {
                for (int l = 0; l < t.Length; l++) for (int i = 0; i < t[l].Length; i++) all.Add(new KeyValuePair<string, string>(n + "[" + l + "][" + i + "]", t[l][i]));
            };
            add("TurretFix.Old", TurretFix.Old); add("TurretFix.New", TurretFix.New);
            add("DescramblerFix.Old", DescramblerFix.Old); add("DescramblerFix.New", DescramblerFix.New);
            add("AshfordFix.Old", AshfordFix.Old); add("AshfordFix.New", AshfordFix.New);
            add("FillerFix.Old", FillerFix.Old); add("FillerFix.New", FillerFix.New);
            add("NativeFixes.SkyOld", NativeFixes.SkyOld); add("NativeFixes.SkyNew", NativeFixes.SkyNew);
            add("NativeFixes.DroneOld", NativeFixes.DroneOld); add("NativeFixes.DroneNew", NativeFixes.DroneNew);
            add("SMOKE_OLD", SMOKE_OLD); add("SMOKE_NEW", SMOKE_NEW);
            add("IrFix stubs and entries", new string[][] { IrFix.OldSpan, IrFix.OldAffine, IrFix.NewSpan, IrFix.NewAffine, IrFix.CodeStubSpan, IrFix.CodeStubAffine });
            add("ViewFix.Old", ViewFix.Old); add("ViewFix.CodeStub", ViewFix.CodeStub); add("ViewFix.New", new string[][] { ViewFix.New });
            foreach (KeyValuePair<string, string> e in all)
                Ok(DataKit.IsHex(e.Value), e.Key + ": an even number of hex digits (Hex() drops an odd last digit silently)");
        }

        [Test] static void Data_Pieces_WellFormed()
        {
            List<KeyValuePair<string, string[]>> sets = new List<KeyValuePair<string, string[]>>();
            for (int g = 0; g < 2; g++)
            {
                sets.Add(new KeyValuePair<string, string[]>("IrFix.Pieces[" + g + "]", IrFix.Pieces[g]));
                sets.Add(new KeyValuePair<string, string[]>("ViewFix.Pieces[" + g + "]", ViewFix.Pieces[g]));
                sets.Add(new KeyValuePair<string, string[]>("AffineFix.Pieces[" + g + "]", AffineFix.Pieces[g]));
                for (int e = 0; e < IrInPlace.Pieces[g].Length; e++) sets.Add(new KeyValuePair<string, string[]>("IrInPlace.Pieces[" + g + "][" + e + "]", IrInPlace.Pieces[g][e]));
                for (int e = 0; e < SpanInPlace.Pieces[g].Length; e++) sets.Add(new KeyValuePair<string, string[]>("SpanInPlace.Pieces[" + g + "][" + e + "]", SpanInPlace.Pieces[g][e]));
            }
            int copies = 0;
            foreach (KeyValuePair<string, string[]> s in sets)
            {
                Ok(s.Value.Length > 0, s.Key + ": pieces");
                foreach (string p in s.Value)
                {
                    string what = s.Key + " \"" + (p.Length > 40 ? p.Substring(0, 40) + "..." : p) + "\"";
                    // the writers take 'C' for a copy and anything else for "B:" + hex (its first two characters skipped)
                    if (p[0] == 'C')
                    {
                        string[] f = p.Split(':');
                        Eq(3, f.Length, what + ": C:address:length");
                        Ok(f[0] == "C" && DataKit.IsDigits(f[1]) && DataKit.IsDigits(f[2]), what + ": hex address and length");
                        uint a = Convert.ToUInt32(f[1], 16); int n = Convert.ToInt32(f[2], 16);
                        Ok(n > 0, what + ": length");
                        // copied from the game's relocated code (ScanRegion: below 0x100000 is a raw copy of the exe), never from ours
                        Ok(a >= 0x100000 && a + (uint)n <= DataKit.FreeStart, what + ": copied from the game's code, below the free area");
                        copies++;
                    }
                    else Ok(p.StartsWith("B:") && DataKit.IsHex(p.Substring(2)), what + ": B: and an even number of hex digits");
                }
            }
            Ok(copies > 100, "C pieces seen: " + copies);
        }

        // the writers refuse a block whose length is not Size (TryIrFix, TryViewFix, TryAffineFix, TryInPlace)
        [Test] static void Data_Pieces_AddUpToTheirSize()
        {
            for (int g = 0; g < 2; g++)
            {
                Eq(IrFix.Size[g], DataKit.Sum(IrFix.Pieces[g]), "IrFix[" + g + "]");
                Eq(ViewFix.Size[g], DataKit.Sum(ViewFix.Pieces[g]), "ViewFix[" + g + "]");
                Eq(AffineFix.Size[g], DataKit.Sum(AffineFix.Pieces[g]), "AffineFix[" + g + "]");
                for (int e = 0; e < IrInPlace.At[g].Length; e++) Eq(IrInPlace.Size[g][e], DataKit.Sum(IrInPlace.Pieces[g][e]), "IrInPlace[" + g + "] entry " + e + " at " + DataKit.X(IrInPlace.At[g][e]));
                for (int e = 0; e < SpanInPlace.At[g].Length; e++) Eq(SpanInPlace.Size[g][e], DataKit.Sum(SpanInPlace.Pieces[g][e]), "SpanInPlace[" + g + "] entry " + e);
            }
        }

        [Test] static void Data_InPlace_Shapes()
        {
            for (int t = 0; t < 2; t++)
            {
                string n = t == 0 ? "IrInPlace" : "SpanInPlace";
                uint[][] at = t == 0 ? IrInPlace.At : SpanInPlace.At, checkAt = t == 0 ? IrInPlace.CheckAt : SpanInPlace.CheckAt;
                int[][] size = t == 0 ? IrInPlace.Size : SpanInPlace.Size, checkSize = t == 0 ? IrInPlace.CheckSize : SpanInPlace.CheckSize;
                string[][][] pieces = t == 0 ? IrInPlace.Pieces : SpanInPlace.Pieces;
                string[] sha = t == 0 ? IrInPlace.Sha : SpanInPlace.Sha;
                Eq(2, sha.Length, n + ": one SHA-256 per executable");
                foreach (Array a in new Array[] { at, size, pieces, checkAt, checkSize }) Eq(sha.Length, a.Length, n + ": tables per executable");
                for (int g = 0; g < 2; g++)
                {
                    string w = n + "[" + g + "]";
                    Ok(at[g].Length > 0, w + ": entries");
                    Eq(at[g].Length, size[g].Length, w + ": At / Size");
                    Eq(at[g].Length, pieces[g].Length, w + ": At / Pieces");
                    Eq(checkAt[g].Length, checkSize[g].Length, w + ": CheckAt / CheckSize");
                    for (int e = 0; e < at[g].Length; e++)
                    {
                        Ok(size[g][e] > 0, w + " entry " + e + ": size");
                        for (int f = e + 1; f < at[g].Length; f++)
                            No(DataKit.Overlap(at[g][e], at[g][e] + (uint)size[g][e], at[g][f], at[g][f] + (uint)size[g][f]), w + ": entries " + e + " and " + f + " overlap");
                    }
                    for (int i = 0; i < checkAt[g].Length; i++)
                    {
                        Ok(checkSize[g][i] > 0, w + " check " + i + ": size");
                        for (int f = i + 1; f < checkAt[g].Length; f++)
                            No(DataKit.Overlap(checkAt[g][i], checkAt[g][i] + (uint)checkSize[g][i], checkAt[g][f], checkAt[g][f] + (uint)checkSize[g][f]), w + ": checks " + i + " and " + f + " overlap");
                    }
                }
            }
        }

        // TN+ ships no game code: what it rewrites, and every instruction it moves (C pieces, read before anything is
        // written), lies in the bytes whose SHA-256 says the code is the pristine one
        [Test] static void Data_InPlace_RewritesOnlyBytesTheShaChecked()
        {
            for (int t = 0; t < 2; t++)
            {
                string n = t == 0 ? "IrInPlace" : "SpanInPlace";
                uint[][] at = t == 0 ? IrInPlace.At : SpanInPlace.At, checkAt = t == 0 ? IrInPlace.CheckAt : SpanInPlace.CheckAt;
                int[][] size = t == 0 ? IrInPlace.Size : SpanInPlace.Size, checkSize = t == 0 ? IrInPlace.CheckSize : SpanInPlace.CheckSize;
                string[][][] pieces = t == 0 ? IrInPlace.Pieces : SpanInPlace.Pieces;
                for (int g = 0; g < 2; g++)
                {
                    Func<uint, int, bool> checkedBytes = (a, len) =>
                    {
                        for (int i = 0; i < checkAt[g].Length; i++) if (a >= checkAt[g][i] && a + (uint)len <= checkAt[g][i] + (uint)checkSize[g][i]) return true;
                        return false;
                    };
                    for (int e = 0; e < at[g].Length; e++)
                    {
                        Ok(checkedBytes(at[g][e], size[g][e]), n + "[" + g + "] entry " + e + " at " + DataKit.X(at[g][e]) + ": rewritten bytes inside the checked ones");
                        foreach (string p in pieces[g][e])
                            if (p[0] == 'C') Ok(checkedBytes(DataKit.CopyAt(p), DataKit.PieceLen(p)), n + "[" + g + "] entry " + e + " " + p + ": copied from checked bytes");
                    }
                }
            }
        }

        // the in-place infrared fix checks exactly what it rewrites plus the two entries of the rebuilt infrared fix (IrFix),
        // which must still be the game's: once IrFix has put its jmp there, the in-place way is not taken
        [Test] static void Data_IrInPlace_ChecksItsEntriesAndTheIrFixSites()
        {
            for (int g = 0; g < 2; g++)
            {
                List<KeyValuePair<uint, int>> want = new List<KeyValuePair<uint, int>>();
                for (int e = 0; e < IrInPlace.At[g].Length; e++) want.Add(new KeyValuePair<uint, int>(IrInPlace.At[g][e], IrInPlace.Size[g][e]));
                want.Add(new KeyValuePair<uint, int>(IrFix.SiteSpan[g], 6));
                want.Add(new KeyValuePair<uint, int>(IrFix.SiteAffine[g], 6));
                List<KeyValuePair<uint, int>> have = new List<KeyValuePair<uint, int>>();
                for (int i = 0; i < IrInPlace.CheckAt[g].Length; i++) have.Add(new KeyValuePair<uint, int>(IrInPlace.CheckAt[g][i], IrInPlace.CheckSize[g][i]));
                Comparison<KeyValuePair<uint, int>> byAddress = (a, b) => a.Key.CompareTo(b.Key);
                want.Sort(byAddress); have.Sort(byAddress);
                EqList(want, have, "IrInPlace[" + g + "]: the checked bytes = the entries rewritten + IrFix's two sites (6 bytes each)");
                foreach (uint a in IrInPlace.At[g])
                {
                    No(a == IrFix.SiteSpan[g] || a == IrFix.SiteAffine[g], "IrInPlace[" + g + "]: an IrFix site rewritten in place " + DataKit.X(a));
                }
            }
        }

        [Test] static void Data_SpanInPlace_MasksAndCallSites()
        {
            for (int g = 0; g < 2; g++)
            {
                string w = "SpanInPlace[" + g + "]";
                Eq(1, SpanInPlace.At[g].Length, w + ": one module");
                // SpanMatches hashes the module, then 5 bytes at each call site; the pristine check is the call sites + the module
                Eq(3, SpanInPlace.OnCallAt[g].Length, w + ": call sites");
                Eq(4, SpanInPlace.CheckAt[g].Length, w + ": checks");
                for (int i = 0; i < 3; i++)
                {
                    Eq(SpanInPlace.OnCallAt[g][i], SpanInPlace.CheckAt[g][i], w + ": call site " + i + " checked");
                    Eq(5, SpanInPlace.CheckSize[g][i], w + ": call site " + i + " size (SpanMatches reads 5)");
                    No(SpanInPlace.OnCallAt[g][i] >= SpanInPlace.At[g][0] && SpanInPlace.OnCallAt[g][i] < SpanInPlace.At[g][0] + (uint)SpanInPlace.Size[g][0], w + ": call site " + i + " outside the module");
                }
                Eq(SpanInPlace.At[g][0], SpanInPlace.CheckAt[g][3], w + ": the module checked");
                Eq(SpanInPlace.Size[g][0], SpanInPlace.CheckSize[g][3], w + ": all of it");
                // offset, length pairs inside the module (Array.Clear on the module read back), in order, not overlapping
                foreach (int[] mask in new int[][] { SpanInPlace.OnMask[g], SpanInPlace.RanMask[g] })
                {
                    string mw = w + (mask == SpanInPlace.OnMask[g] ? " OnMask" : " RanMask");
                    Eq(0, mask.Length % 2, mw + ": pairs");
                    Ok(mask.Length > 0, mw + ": fields");
                    int prev = 0;
                    for (int i = 0; i < mask.Length; i += 2)
                    {
                        Ok(mask[i] >= prev, mw + ": field " + i / 2 + " at " + mask[i] + " in order, after the previous one");
                        Ok(mask[i + 1] >= 1 && mask[i + 1] <= 4, mw + ": field " + i / 2 + " length " + mask[i + 1] + " (a byte to a dword)");
                        prev = mask[i] + mask[i + 1];
                        Ok(prev <= SpanInPlace.Size[g][0], mw + ": field " + i / 2 + " inside the module (" + SpanInPlace.Size[g][0] + " bytes)");
                    }
                }
                No(SpanInPlace.Sha[g] == SpanInPlace.OnSha[g] || SpanInPlace.Sha[g] == SpanInPlace.RanSha[g] || SpanInPlace.OnSha[g] == SpanInPlace.RanSha[g], w + ": pristine, ours and the game's after it ran are three hashes");
            }
        }

        [Test] static void Data_Affine_AbsoluteDwordsAndEntry()
        {
            Eq(0, AffineFix.Off % 0x1000, "AffineFix.Off: a page of its own from the page-aligned XCODE base");
            foreach (Array a in new Array[] { AffineFix.Old, AffineFix.Size, AffineFix.Entry, AffineFix.Sha, AffineFix.Abs, AffineFix.Pieces })
                Eq(AffineFix.Table.Length, a.Length, "AffineFix: tables per executable");
            for (int g = 0; g < 2; g++)
            {
                string w = "AffineFix[" + g + "]";
                byte[] block = DataKit.Assemble(AffineFix.Pieces[g]);
                int prev = -4;
                foreach (int o in AffineFix.Abs[g])
                {
                    Ok(o >= prev + 4, w + ": dword at " + o + " after the previous one (each gets the block address added once)");
                    Ok(o >= 0 && o + 4 <= AffineFix.Size[g], w + ": dword at " + o + " inside the block");
                    int p = DataKit.PieceAt(AffineFix.Pieces[g], o);
                    Ok(p >= 0 && p == DataKit.PieceAt(AffineFix.Pieces[g], o + 3) && AffineFix.Pieces[g][p][0] == 'B', w + ": dword at " + o + " in one of our B pieces (never in game bytes copied)");
                    // a block offset before relocation: the address it becomes lies in the block
                    Ok(BitConverter.ToUInt32(block, o) < (uint)AffineFix.Size[g], w + ": dword at " + o + " = " + BitConverter.ToUInt32(block, o) + ", an offset in the block");
                    prev = o;
                }
                Ok(AffineFix.Entry[g] >= 0 && AffineFix.Entry[g] < AffineFix.Size[g], w + ": entry inside the block");
                Ok(AffineFix.Table[g] < DataKit.FreeStart && AffineFix.Old[g] < DataKit.FreeStart, w + ": the renderer table and the routine it points to are the game's");
            }
        }

        // The rebuilt blocks (IrFix, ViewFix) and the stubs that run the original routine until the block is there: the
        // pointer first goes to the stub, then to the block's entry. Both start with the same instructions (the site's,
        // re-encoded), the stub then jumps back to the site right after them.
        [Test] static void Data_Stubs_JumpBackRightAfterWhatTheyRedo()
        {
            for (int g = 0; g < 2; g++)
            {
                Action<string, uint, string, uint, string[], int> check = (what, stubAt, stubHex, site, pieces, entry) =>
                {
                    byte[] stub = Hex(stubHex);
                    int n = stub.Length - 5;
                    Eq((byte)0xE9, stub[n], what + ": the stub ends with a jmp");
                    Eq(site + (uint)n, DataKit.Rel(stubAt, stub, n), what + ": back to the site after the " + n + " bytes it redoes");
                    Ok(n >= 6, what + ": the stub redoes at least the 6 bytes of the entry (jmp [pointer]) written over the site");
                    int p = DataKit.PieceAt(pieces, entry);
                    Ok(p >= 0 && DataKit.PieceStart(pieces, p) == entry && pieces[p][0] == 'B', what + ": the block's entry starts one of our pieces");
                    byte[] head = Hex(pieces[p].Substring(2));
                    Ok(head.Length >= n, what + ": entry piece long enough");
                    EqBytes(Slice(stub, 0, n), Slice(head, 0, n), what + ": the block's entry starts like the stub");
                };
                string L = "[" + g + "]";
                check("IrFix span" + L, IrFix.StubSpan, IrFix.CodeStubSpan[g], IrFix.SiteSpan[g], IrFix.Pieces[g], IrFix.EntrySpan[g]);
                check("IrFix affine" + L, IrFix.StubAffine, IrFix.CodeStubAffine[g], IrFix.SiteAffine[g], IrFix.Pieces[g], IrFix.EntryAffine[g]);
                for (int r = 0; r < ViewFix.Stub.Length; r++)
                    check("ViewFix routine " + r + L, ViewFix.Stub[r], ViewFix.CodeStub[g][r], ViewFix.Site[g][r], ViewFix.Pieces[g], ViewFix.Entry[g][r]);
            }
        }

        [Test] static void Data_Entries_JumpThroughTheirPointers()
        {
            Func<uint, byte[]> jmpPtr = p => DataKit.Cat(Bytes("FF25"), DataKit.Le(p));   // jmp dword [p]
            Eq(ViewFix.Stub.Length, ViewFix.Ptr.Length, "ViewFix: a pointer per routine");
            Eq(ViewFix.Stub.Length, ViewFix.New.Length, "ViewFix: an entry per routine");
            for (int g = 0; g < 2; g++)
            {
                EqBytes(jmpPtr(IrFix.PtrSpan), Hex(IrFix.NewSpan[g]), "IrFix span entry [" + g + "]");
                EqBytes(jmpPtr(IrFix.PtrAffine), Hex(IrFix.NewAffine[g]), "IrFix affine entry [" + g + "]");
                Eq(6, Hex(IrFix.OldSpan[g]).Length, "IrFix: the span entry replaces 6 bytes [" + g + "]");
                Eq(6, Hex(IrFix.OldAffine[g]).Length, "IrFix: the affine entry replaces 6 bytes [" + g + "]");
                foreach (Array a in new Array[] { ViewFix.Site[g], ViewFix.Old[g], ViewFix.CodeStub[g], ViewFix.Entry[g] })
                    Eq(ViewFix.Stub.Length, a.Length, "ViewFix[" + g + "]: one per routine");
                for (int r = 0; r < ViewFix.Stub.Length; r++)
                {
                    EqBytes(jmpPtr(ViewFix.Ptr[r]), Hex(ViewFix.New[r]), "ViewFix entry " + r);
                    Eq(6, Hex(ViewFix.Old[g][r]).Length, "ViewFix[" + g + "]: entry " + r + " replaces 6 bytes");
                    Ok(ViewFix.Entry[g][r] >= 0 && ViewFix.Entry[g][r] < ViewFix.Size[g], "ViewFix[" + g + "]: entry " + r + " inside the block");
                }
                Ok(IrFix.EntrySpan[g] < IrFix.Size[g] && IrFix.EntryAffine[g] < IrFix.Size[g], "IrFix[" + g + "]: entries inside the block");
            }
            // ViewFix.Ptr[0] is the one TryViewFix reads back to see the block in place
            No(ViewFix.Ptr[0] == ViewFix.Ptr[1] || ViewFix.Ptr[0] == ViewFix.Ptr[2] || ViewFix.Ptr[1] == ViewFix.Ptr[2], "ViewFix: three pointers");
        }

        // Stage 1 of TryIrFix / TryViewFix copies the C pieces from memory AFTER stage 0 wrote the entries (jmp [pointer])
        // over the sites: a copy of those 6 bytes would read ours, the SHA-256 would never match
        [Test] static void Data_Rebuilt_CopiesNeverReadTheEntries()
        {
            for (int g = 0; g < 2; g++)
            {
                List<KeyValuePair<string, uint>> sites = new List<KeyValuePair<string, uint>>();
                sites.Add(new KeyValuePair<string, uint>("IrFix span", IrFix.SiteSpan[g]));
                sites.Add(new KeyValuePair<string, uint>("IrFix affine", IrFix.SiteAffine[g]));
                foreach (string p in IrFix.Pieces[g])
                    if (p[0] == 'C')
                        foreach (KeyValuePair<string, uint> s in sites)
                            No(DataKit.Overlap(DataKit.CopyAt(p), DataKit.CopyAt(p) + (uint)DataKit.PieceLen(p), s.Value, s.Value + 6), "IrFix[" + g + "] " + p + " copies the " + s.Key + " entry");
                foreach (string p in ViewFix.Pieces[g])
                    if (p[0] == 'C')
                        for (int r = 0; r < ViewFix.Site[g].Length; r++)
                            No(DataKit.Overlap(DataKit.CopyAt(p), DataKit.CopyAt(p) + (uint)DataKit.PieceLen(p), ViewFix.Site[g][r], ViewFix.Site[g][r] + 6), "ViewFix[" + g + "] " + p + " copies entry " + r);
            }
        }

        // ================================================================== the cave pieces (TryPieces tables)
        [Test] static void Data_Pieces_ShapesPerLanguage()
        {
            string[] names = { "TurretFix", "DescramblerFix", "AshfordFix", "FillerFix", "NativeFixes.Sky", "NativeFixes.Drone", "SMOKE" };
            uint[][][] ats = { TurretFix.At, DescramblerFix.At, AshfordFix.At, FillerFix.At, NativeFixes.SkyAt, NativeFixes.DroneAt, SMOKE_AT };
            string[][][] olds = { TurretFix.Old, DescramblerFix.Old, AshfordFix.Old, FillerFix.Old, NativeFixes.SkyOld, NativeFixes.DroneOld, SMOKE_OLD };
            string[][][] news = { TurretFix.New, DescramblerFix.New, AshfordFix.New, FillerFix.New, NativeFixes.SkyNew, NativeFixes.DroneNew, SMOKE_NEW };
            for (int t = 0; t < names.Length; t++)
            {
                Eq(2, ats[t].Length, names[t] + ": two executables");
                Eq(2, olds[t].Length, names[t] + ": Old per executable");
                Eq(2, news[t].Length, names[t] + ": New per executable");
                bool differ = false;
                for (int l = 0; l < 2; l++)
                {
                    string w = names[t] + "[" + l + "]";
                    Ok(ats[t][l].Length > 0, w + ": pieces");
                    Eq(ats[t][l].Length, olds[t][l].Length, w + ": At / Old");
                    Eq(ats[t][l].Length, news[t][l].Length, w + ": At / New");
                    for (int i = 0; i < ats[t][l].Length; i++)
                    {
                        // TryPieces reads New's length at At and compares it with Old, then writes New there
                        Eq(olds[t][l][i].Length, news[t][l][i].Length, w + " piece " + i + " at " + DataKit.X(ats[t][l][i]) + ": Old and New the same length");
                        Ok(news[t][l][i].Length > 0, w + " piece " + i + ": not empty");
                        No(olds[t][l][i] == news[t][l][i], w + " piece " + i + ": New changes something");
                        for (int j = i + 1; j < ats[t][l].Length; j++)
                            No(DataKit.Overlap(ats[t][l][i], ats[t][l][i] + (uint)news[t][l][i].Length / 2, ats[t][l][j], ats[t][l][j] + (uint)news[t][l][j].Length / 2), w + ": pieces " + i + " and " + j + " overlap");
                        if (ats[t][0].Length == ats[t][1].Length && (ats[t][0][i] != ats[t][1][i] || olds[t][0][i] != olds[t][1][i])) differ = true;
                    }
                }
                Ok(differ, names[t] + ": the two executables are told apart by their places or bytes");
            }
        }

        // TryPieces writes in array order: the code in the free area first, the hooks that go there last; the free area is
        // expected empty (Old = zeros), the game code as the game has it
        [Test] static void Data_Pieces_FreeAreaFirstThenHooks()
        {
            string[] names = { "TurretFix", "DescramblerFix", "AshfordFix", "FillerFix", "NativeFixes.Sky", "NativeFixes.Drone", "SMOKE" };
            uint[][][] ats = { TurretFix.At, DescramblerFix.At, AshfordFix.At, FillerFix.At, NativeFixes.SkyAt, NativeFixes.DroneAt, SMOKE_AT };
            string[][][] olds = { TurretFix.Old, DescramblerFix.Old, AshfordFix.Old, FillerFix.Old, NativeFixes.SkyOld, NativeFixes.DroneOld, SMOKE_OLD };
            int[] zones = { TurretFix.Zones, DescramblerFix.Zones, 2, 1, 0, 0, 0 };   // Ashford: code and text; Filler: the pixel loop
            for (int t = 0; t < names.Length; t++)
                for (int l = 0; l < 2; l++)
                    for (int i = 0; i < ats[t][l].Length; i++)
                    {
                        string w = names[t] + "[" + l + "] piece " + i + " at " + DataKit.X(ats[t][l][i]);
                        uint end = ats[t][l][i] + (uint)olds[t][l][i].Length / 2;
                        if (i < zones[t])
                        {
                            Ok(ats[t][l][i] >= DataKit.FreeStart && end <= DataKit.FreeEnd, w + ": in the free area");
                            Ok(DataKit.AllZeroHex(olds[t][l][i]), w + ": expected empty");
                        }
                        else
                        {
                            Ok(end <= DataKit.FreeStart, w + ": in the game, below the free area");
                            No(DataKit.AllZeroHex(olds[t][l][i]), w + ": the game's own bytes");
                        }
                    }
            Eq(TurretFix.At[0].Length - 3, TurretFix.Zones, "TurretFix: 3 hooks after its caves");
            Eq(DescramblerFix.At[0].Length - 2, DescramblerFix.Zones, "DescramblerFix: 2 hooks after its caves");
            Eq(3, AshfordFix.At[0].Length, "AshfordFix: code, text, then the hook TryAshfordFix looks at (the last piece)");
        }

        // every jump or call that a hook writes goes to the start of one of the fix's own caves
        [Test] static void Data_Pieces_HooksLandOnTheirOwnCaves()
        {
            string[] names = { "TurretFix", "DescramblerFix", "AshfordFix", "FillerFix" };
            uint[][][] ats = { TurretFix.At, DescramblerFix.At, AshfordFix.At, FillerFix.At };
            string[][][] news = { TurretFix.New, DescramblerFix.New, AshfordFix.New, FillerFix.New };
            int[] hooks = { 3, 2, 1, 1 };
            for (int t = 0; t < names.Length; t++)
                for (int l = 0; l < 2; l++)
                {
                    List<uint> caves = new List<uint>(), entered = new List<uint>();
                    for (int i = 0; i < ats[t][l].Length; i++) if (ats[t][l][i] >= DataKit.FreeStart) caves.Add(ats[t][l][i]);
                    int n = 0;
                    for (int i = 0; i < ats[t][l].Length; i++)
                    {
                        byte[] b = Hex(news[t][l][i]);
                        if (ats[t][l][i] >= DataKit.FreeStart || (b[0] != 0xE8 && b[0] != 0xE9)) continue;
                        uint to = DataKit.Rel(ats[t][l][i], b, 0);
                        Ok(caves.Contains(to), names[t] + "[" + l + "] hook at " + DataKit.X(ats[t][l][i]) + " -> " + DataKit.X(to) + ": the start of one of its caves");
                        No(entered.Contains(to), names[t] + "[" + l + "]: two hooks into " + DataKit.X(to));
                        entered.Add(to);
                        n++;
                    }
                    Eq(hooks[t], n, names[t] + "[" + l + "]: hooks that jump into a cave");
                }
        }

        // and the caves come back right behind the bytes their hook replaced (or go on where the replaced call went)
        [Test] static void Data_Pieces_CavesComeBackBehindTheirHooks()
        {
            for (int g = 0; g < 2; g++)
            {
                foreach (int t in new[] { 0, 1 })
                {
                    uint[] at = t == 0 ? TurretFix.At[g] : DescramblerFix.At[g];
                    string[] nw = t == 0 ? TurretFix.New[g] : DescramblerFix.New[g], old = t == 0 ? TurretFix.Old[g] : DescramblerFix.Old[g];
                    int zones = t == 0 ? TurretFix.Zones : DescramblerFix.Zones;
                    string name = t == 0 ? "TurretFix" : "DescramblerFix";
                    for (int i = zones; i < at.Length; i++)
                    {
                        byte[] hook = Hex(nw[i]);
                        int cave = Array.IndexOf(at, DataKit.Rel(at[i], hook, 0));
                        uint back = at[i] + (uint)old[i].Length / 2;
                        Ok(cave >= 0 && DataKit.Reaches(at[cave], Hex(nw[cave]), 0xE9, back), name + "[" + g + "]: the cave of the hook at " + DataKit.X(at[i]) + " jumps back to " + DataKit.X(back));
                    }
                }
                // Ashford: the hook takes the place of a call; its cave ends by jumping where that call went
                byte[] ashOld = Hex(AshfordFix.Old[g][2]);
                Eq((byte)0xE8, ashOld[0], "AshfordFix[" + g + "]: the hooked instruction is a call");
                uint callee = DataKit.Rel(AshfordFix.At[g][2], ashOld, 0);
                Ok(DataKit.Reaches(AshfordFix.At[g][0], Hex(AshfordFix.New[g][0]), 0xE9, callee), "AshfordFix[" + g + "]: the cave goes on to the original callee " + DataKit.X(callee));
                // Filler: the 5-byte hook sits 4 bytes before the instruction the game rewrote at run time (3 opcode bytes,
                // then a 32-bit displacement: its writers store at hook + 4 and hook + 7); the cave resumes after it
                uint fh = FillerFix.At[g][7];
                Eq(fh + 4, BitConverter.ToUInt32(Hex(FillerFix.Old[g][1]), 2), "FillerFix[" + g + "]: the opcode writer's address");
                Eq(fh + 7, BitConverter.ToUInt32(Hex(FillerFix.Old[g][2]), 2), "FillerFix[" + g + "]: the displacement writer's address");
                Ok(FillerFix.At[g][0] >= DataKit.FreeStart, "FillerFix[" + g + "]: cave in the free area");
                Ok(DataKit.Reaches(FillerFix.At[g][0], Hex(FillerFix.New[g][0]), 0xE9, fh + 11), "FillerFix[" + g + "]: the cave resumes at " + DataKit.X(fh + 11));
            }
        }

        // The game's filler rewrote an instruction of its pixel loop (opcode dword, displacement); its writers now store the
        // same values in two dwords of the free area, which the cave reads as data
        [Test] static void Data_Filler_RedirectedWritersKeepTheirValues()
        {
            for (int g = 0; g < 2; g++)
            {
                uint op = BitConverter.ToUInt32(Hex(FillerFix.New[g][1]), 2);
                for (int i = 1; i <= 6; i++)
                {
                    byte[] o = Hex(FillerFix.Old[g][i]), n = Hex(FillerFix.New[g][i]);
                    string w = "FillerFix[" + g + "] writer " + i;
                    EqBytes(Slice(o, 0, 2), Slice(n, 0, 2), w + ": same instruction");
                    EqBytes(Slice(o, 6, o.Length - 6), Slice(n, 6, n.Length - 6), w + ": same value written");
                    bool displacement = o[0] == 0x89;                   // mov [disp], edx (the others: mov dword [opcode], imm32)
                    Eq(displacement ? op + 4 : op, BitConverter.ToUInt32(n, 2), w + ": stores into the free area");
                }
                byte[] cave = Hex(FillerFix.New[g][0]);
                // the first kind of opcode stored is 8A 84 03 (mov al, [ebx + eax + disp32]): its 2nd byte tells them apart
                byte second = Hex(FillerFix.Old[g][1])[7];
                Eq((byte)0x84, second, "FillerFix[" + g + "]: 2nd opcode byte of mov al, [ebx + eax + disp32]");
                Ok(DataKit.Contains(cave, DataKit.Cat(Bytes("803D"), DataKit.Le(op + 1), new byte[] { second })), "FillerFix[" + g + "]: the cave compares the stored opcode's 2nd byte");
                Ok(DataKit.Contains(cave, DataKit.Cat(Bytes("0305"), DataKit.Le(op + 4))), "FillerFix[" + g + "]: the cave adds the stored displacement");
            }
        }

        [Test] static void Data_Ashford_TextsAndTheirLanguage()
        {
            // AshfordFix goes English then French: "Deceased" and "Decede" with three e acute, 0x82 in the game's code page 437
            EqBytes(DataKit.Cat(Encoding.ASCII.GetBytes("Deceased"), new byte[] { 0 }), Hex(AshfordFix.New[0][1]), "English text");
            EqBytes(Bytes("44 82 63 82 64 82 00"), Hex(AshfordFix.New[1][1]), "French text (CP437)");
            for (int g = 0; g < 2; g++)
            {
                Eq(AshfordFix.New[g][1].Length, AshfordFix.Old[g][1].Length, "AshfordFix[" + g + "]: room for the text");
                Ok(DataKit.Contains(Hex(AshfordFix.New[g][0]), DataKit.Cat(Bytes("B8"), DataKit.Le(AshfordFix.At[g][1]))), "AshfordFix[" + g + "]: the cave hands the text's address (mov eax, text)");
                Ok(AshfordFix.At[g][0] + (uint)AshfordFix.New[g][0].Length / 2 <= AshfordFix.At[g][1], "AshfordFix[" + g + "]: the text after the code");
            }
        }

        // the language of each generated table, from TN+'s own French / English pairs in the same part of the executable
        [Test] static void Data_Tables_GeneratedOnesAreEnglishFirst()
        {
            // the game clock TryAttach uses: 0x446AE8 English, 0x446B98 French (the descrambler's timer reads it)
            Ok(DataKit.Contains(Hex(DescramblerFix.New[0][1]), DataKit.Le(0x446AE8)), "DescramblerFix[0] reads the English clock");
            Ok(DataKit.Contains(Hex(DescramblerFix.New[1][1]), DataKit.Le(0x446B98)), "DescramblerFix[1] reads the French clock");
            No(DataKit.Contains(Hex(DescramblerFix.New[0][1]), DataKit.Le(0x446B98)), "DescramblerFix[0]: not the French clock");
            // French - English, French first in TNPlus.cs: 3D engine 0x29xxxx-0x2Bxxxx -0x10 (it is 0x10 further in the
            // English exe), 0x2Fxxxx +0x1F0, 0x31xxxx-0x34xxxx +0x280, game data 0x35xxxx +0x20
            int engine = (int)FOV_HOOK[0] - (int)FOV_HOOK[1], ui = (int)UIFIX_AT[0] - (int)UIFIX_AT[1], late = (int)HITFIX_AT[0] - (int)HITFIX_AT[1];
            int data = (int)HdPayloadFr.PoolMax - (int)HdPayloadEn.PoolMax;
            Eq(-0x10, engine, "FOV_HOOK"); Eq(late, (int)PHYSFIX_AT[0] - (int)PHYSFIX_AT[1], "PHYSFIX_AT like HITFIX_AT");
            Func<uint[], int> d = a => (int)a[1] - (int)a[0];            // generated: [1] - [0] = French - English
            Eq(engine, d(new[] { NativeFixes.SkyAt[0][0], NativeFixes.SkyAt[1][0] }), "NativeFixes.SkyAt");
            Eq(engine, d(new[] { ViewFix.Site[0][2], ViewFix.Site[1][2] }), "ViewFix.Site routine 2");
            Eq(ui, d(new[] { TurretFix.At[0][4], TurretFix.At[1][4] }), "TurretFix hook 4");
            // (AshfordFix: its texts say it, Data_Ashford_TextsAndTheirLanguage)
            Eq(late, d(IrFix.SiteSpan), "IrFix.SiteSpan"); Eq(late, d(IrFix.SiteAffine), "IrFix.SiteAffine");
            Eq(late, d(new[] { ViewFix.Site[0][0], ViewFix.Site[1][0] }), "ViewFix.Site routine 0");
            Eq(late, d(new[] { FillerFix.At[0][7], FillerFix.At[1][7] }), "FillerFix hook");
            Eq(late, d(AffineFix.Old), "AffineFix.Old");
            Eq(late, d(new[] { IrInPlace.At[0][0], IrInPlace.At[1][0] }), "IrInPlace.At");
            Eq(late, d(new[] { SpanInPlace.At[0][0], SpanInPlace.At[1][0] }), "SpanInPlace.At");
            Eq(data, d(new[] { SMOKE_AT[0][0], SMOKE_AT[1][0] }), "SMOKE_AT");
        }

        // record 11 of the particle type table (EN 0x35C390, FR 0x35C3B0, 0x44 bytes a type), changed as its comment says
        [Test] static void Data_Smoke_RecordAsDescribed()
        {
            Eq(0x35C390u, SMOKE_AT[0][0] - 11 * 0x44, "English table");
            Eq(0x35C3B0u, SMOKE_AT[1][0] - 11 * 0x44, "French table");
            byte[] o = Hex(SMOKE_REC_OLD), n = Hex(SMOKE_REC_NEW);
            Eq(0x44, o.Length, "a record"); Eq(0x44, n.Length, "a record");
            int[] changed = { 0, 10, 11, 16 };
            int[] was = { 192, 50, 120, 60 }, now = { 256, 100, 200, 70 };   // particles, life from, life up to, % back
            for (int i = 0; i < 17; i++)
            {
                int k = Array.IndexOf(changed, i), a = BitConverter.ToInt32(o, 4 * i), b = BitConverter.ToInt32(n, 4 * i);
                if (k >= 0) { Eq(was[k], a, "old dword " + i); Eq(now[k], b, "new dword " + i); }
                else Eq(a, b, "dword " + i + " unchanged");
            }
            for (int l = 0; l < 2; l++) { Eq(SMOKE_REC_OLD, SMOKE_OLD[l][0], "SMOKE_OLD[" + l + "]"); Eq(SMOKE_REC_NEW, SMOKE_NEW[l][0], "SMOKE_NEW[" + l + "]"); }
        }

        // ================================================================== TNPlus.cs's in-place fixes
        [Test] static void Data_Fixes_LengthsAndPlaces()
        {
            string[] names = { "HITFIX", "PHYSFIX", "PHYSFIX2", "IDLEFIX", "IDLEFIX2", "UIFIX" };
            uint[][] ats = { HITFIX_AT, PHYSFIX_AT, PHYSFIX2_AT, IDLEFIX_AT, IDLEFIX2_AT, UIFIX_AT };
            byte[][] olds = { HITFIX_OLD, PHYSFIX_OLD, PHYSFIX2_OLD, IDLEFIX_OLD, IDLEFIX2_OLD, UIFIX_OLD };
            byte[][] news = { HITFIX_NEW, PHYSFIX_NEW, PHYSFIX2_NEW, IDLEFIX_NEW, IDLEFIX2_NEW, UIFIX_NEW };
            int[] places = { 2, 2, 2, 1, 1, 2 };                       // the idle fixes: same place in both executables
            for (int t = 0; t < names.Length; t++)
            {
                Eq(places[t], ats[t].Length, names[t] + ": places");
                Eq(olds[t].Length, news[t].Length, names[t] + ": TryFix reads Old's length and writes New there");
                No(Same(olds[t], news[t]), names[t] + ": New changes something");
                if (ats[t].Length == 2) No(ats[t][0] == ats[t][1], names[t] + ": two places");
            }
            // SMOOTH_*: 3 immediates per executable, 4 bytes each; OBJ_PATCHES: Old / OldEn / New the same length
            Eq(2, SMOOTH_AT.Length, "SMOOTH_AT"); Eq(3, SMOOTH_OLD.Length, "SMOOTH_OLD"); Eq(3, SMOOTH_NEW.Length, "SMOOTH_NEW");
            for (int i = 0; i < 3; i++) { Eq(4, SMOOTH_OLD[i].Length, "SMOOTH_OLD " + i); Eq(4, SMOOTH_NEW[i].Length, "SMOOTH_NEW " + i); for (int l = 0; l < 2; l++) Eq(3, SMOOTH_AT[l].Length, "SMOOTH_AT[" + l + "]"); }
            Eq(2, OBJ_TABLE.Length, "OBJ_TABLE");
            int generated = 0;
            foreach (CodePatch p in OBJ_PATCHES)
            {
                string w = "OBJ_PATCHES " + DataKit.X(p.Fr);
                Eq(p.Old.Length, p.OldEn.Length, w + ": French and English old bytes the same length");
                if (p.New != "") Eq(p.Old.Length, p.New.Length, w + ": New the same length");
                else
                {
                    // the scenery radius: TryObjDist builds mov eax, radius / nop, compared with Old's length
                    generated++;
                    Eq(6, p.Old.Length / 2, w + ": the generated mov eax, imm32 / nop is 6 bytes");
                }
            }
            Eq(1, generated, "one patch built by TryObjDist");
        }

        // the idle chance test is rewritten in the same 17 bytes; its exit (jge) still lands where the game's did
        [Test] static void Data_IdleFix_ExitUnchanged()
        {
            Eq((byte)0x7D, IDLEFIX_OLD[15], "old jge"); Eq((byte)0x7D, IDLEFIX_NEW[12], "new jge");
            Eq(17 + (sbyte)IDLEFIX_OLD[16], 14 + (sbyte)IDLEFIX_NEW[13], "jge target, from the start of the 17 bytes");
            Ok(Same(Slice(IDLEFIX_NEW, 14, 3), Bytes("909090")), "NOPs to the end");
            // the turn step: imul ebx, [esp + 0x3C], 0x2AA8 (frame time x 0x2AA8 >> 16, was 0x222)
            Ok(DataKit.Contains(IDLEFIX2_NEW, Bytes("695C243CA82A0000")), "turn step from the frame time");
            Ok(DataKit.Contains(IDLEFIX2_OLD, Bytes("3D22020000")) && DataKit.Contains(IDLEFIX2_OLD, Bytes("3DDEFDFFFF")), "the game's fixed +-0x222");
        }

        // object draw distance: buffer of 12-byte entries and its two limit checks agree, before and after
        [Test] static void Data_ObjPatches_ListSizeAndLimitsAgree()
        {
            CodePatch list = OBJ_PATCHES[0], c1 = OBJ_PATCHES[1], c2 = OBJ_PATCHES[2];
            Func<string, int> imm32 = h => BitConverter.ToInt32(Hex(h), 1);   // B8 imm32
            Func<string, int, int> imm16 = (h, at) => BitConverter.ToInt16(Hex(h), at);
            Eq(0, imm32(list.Old) % 12, "old list: whole entries"); Eq(0, imm32(list.New) % 12, "new list: whole entries");
            Eq(400, imm32(list.Old) / 12, "400 entries"); Eq(1200, imm32(list.New) / 12, "1200 entries");
            Eq(imm32(list.Old) / 12, imm16(c1.Old, 2), "cmp ax, limit (old)"); Eq(imm32(list.New) / 12, imm16(c1.New, 2), "cmp ax, limit (new)");
            Eq(imm32(list.Old) / 12, imm16(c2.Old, 3), "cmp cx, limit (old)"); Eq(imm32(list.New) / 12, imm16(c2.New, 3), "cmp cx, limit (new)");
            Eq(list.Old, list.OldEn, "the same bytes in both executables");
            // side buckets emptied up to 128: cmp eax, 0x80 in place of the game's variable (ObjDistTable caps ranges at 120)
            Eq(0x80, BitConverter.ToInt32(Hex(OBJ_PATCHES[3].New), 1), "side buckets");
            Eq((byte)0x90, Hex(OBJ_PATCHES[3].New)[5], "and a NOP for the 6th byte");
        }

        // ================================================================== __FF.EXE (TERRAIN_EXE)
        [Test] static void Data_TerrainExe_ShapesAndOffsets()
        {
            for (int l = 0; l < 2; l++)
            {
                List<KeyValuePair<uint, uint>> spans = new List<KeyValuePair<uint, uint>>();
                foreach (ExePatch p in TERRAIN_EXE)
                {
                    uint at = l == 0 ? p.Fr : p.En;
                    string w = DataKit.LANG[l] + " " + DataKit.X(at);
                    Eq(p.Old.Length, p.New.Length, w + ": Old / New");
                    if (p.Prev != null) { Eq(p.Old.Length, p.Prev.Length, w + ": Prev"); No(p.Prev == p.Old || p.Prev == p.New, w + ": Prev differs"); }
                    No(p.Old == p.New, w + ": New changes something");
                    Ok(at > EXE_CODE_DELTA, w + ": file offset (address - EXE_CODE_DELTA) positive");
                    spans.Add(new KeyValuePair<uint, uint>(at - EXE_CODE_DELTA, at - EXE_CODE_DELTA + (uint)p.Old.Length / 2));
                }
                spans.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (int i = 1; i < spans.Count; i++) Ok(spans[i].Key >= spans[i - 1].Value, DataKit.LANG[l] + ": patches at file offsets " + DataKit.X(spans[i - 1].Key) + " and " + DataKit.X(spans[i].Key) + " overlap");
                Ok(spans[0].Key > 0x1000, DataKit.LANG[l] + ": past the exe's headers");
            }
        }

        // vertex map 64x64 -> 128x128: every mask 0x3F -> 0x7F and shift 6 -> 7, nothing else in those 3-byte patches
        [Test] static void Data_TerrainExe_MapGoesFrom6To7Bits()
        {
            int masks = 0, shifts = 0;
            foreach (ExePatch p in TERRAIN_EXE)
            {
                if (p.Old.Length != 6) continue;
                byte[] o = Hex(p.Old), n = Hex(p.New);
                EqBytes(Slice(o, 0, 2), Slice(n, 0, 2), DataKit.X(p.Fr) + ": same instruction");
                if (o[2] == 0x3F) { Eq((byte)0x7F, n[2], DataKit.X(p.Fr) + ": mask"); masks++; }
                else { Eq((byte)6, o[2], DataKit.X(p.Fr) + ": shift by 6"); Eq((byte)7, n[2], DataKit.X(p.Fr) + ": by 7"); Eq((byte)0xC1, o[0], DataKit.X(p.Fr) + ": shl"); shifts++; }
            }
            Eq(24, masks, "masks"); Eq(12, shifts, "shifts");
            for (int l = 0; l < 2; l++)
            {
                ExePatch s = Array.Find(TERRAIN_EXE, p => (l == 0 ? p.Fr : p.En) == TERRAIN_SHIFT[l]);
                Eq("C1E306", s.Old, DataKit.LANG[l] + ": TERRAIN_SHIFT is a shl ebx, 6 of the patches");
                Eq("C1E307", s.New, DataKit.LANG[l] + ": TrySmooth looks for its shl ebx, 7");
            }
            // the map: 4 bytes a vertex, 2^6 then 2^7 per axis
            ExePatch map = Array.Find(TERRAIN_EXE, p => p.Fr == 0x2ADEF0);
            Eq(4 << 12, BitConverter.ToInt32(Hex(map.Old), 1), "16 KB"); Eq(4 << 14, BitConverter.ToInt32(Hex(map.New), 1), "64 KB");
        }

        // buffers sized in whole records (quads: 64 + 32 a quad, triangles 32, vertices and clipping points 39), and each
        // version of the clipping buffer with its limit checks; the render pool's malloc with its recorded size
        [Test] static void Data_TerrainExe_BufferSizesAgreeWithTheirLimits()
        {
            Func<uint, ExePatch> at = a => Array.Find(TERRAIN_EXE, p => p.Fr == a);
            Func<string, int> imm = h => BitConverter.ToInt32(Hex(h), h.StartsWith("81") ? 2 : 1);
            ExePatch quads = at(0x2AB0C4), verts = at(0x2ADEFF), tris = at(0x2ADF0E), clip = at(0x2B0511), pool = at(0x2C4F71), size = at(0x2C4F76);
            Eq(500, (imm(quads.Old) - 64) / 32, "quads, old"); Eq(0, (imm(quads.Old) - 64) % 32, "quads, old: whole");
            Eq(4096, (imm(quads.New) - 64) / 32, "quads, new"); Eq(0, (imm(quads.New) - 64) % 32, "quads, new: whole");
            Eq(1000, imm(tris.Old) / 32, "triangles, old"); Eq(8192, imm(tris.New) / 32, "triangles, new");
            Eq(750 * 39, imm(verts.Old), "vertices, old"); Eq(4608 * 39, imm(verts.New), "vertices, new");
            string[] cv = { clip.Old, clip.Prev, clip.New };
            int limits = 0;
            foreach (ExePatch p in TERRAIN_EXE)
            {
                if (!p.Old.StartsWith("81FB")) continue;                // cmp ebx, limit
                limits++;
                string[] lv = { p.Old, p.Prev, p.New };
                for (int v = 0; v < 3; v++)
                {
                    Eq(0, imm(cv[v]) % 39, "clipping buffer " + v + ": whole points");
                    Eq(imm(cv[v]) / 39, imm(lv[v]), "limit at " + DataKit.X(p.Fr) + " = clipping points (" + (v == 0 ? "game" : v == 1 ? "earlier TN+" : "now") + ")");
                }
            }
            Eq(5, limits, "the five limit checks");
            Eq(6400, imm(clip.New) / 39, "6400 clipping points");
            string[] pv = { pool.Old, pool.Prev, pool.New }, sv = { size.Old, size.Prev, size.New };
            for (int v = 0; v < 3; v++) Eq(imm(pv[v]), imm(sv[v]), "render pool: malloc and recorded size agree (" + v + ")");
            Eq(0x180000, imm(pool.New), "1.5 MB");
            // the in-memory fallback (unprepared exe): 768 quads, 1536 triangles, 1000 vertices, on the same immediates
            Eq(768, (BitConverter.ToInt32(SMOOTH_NEW[0], 0) - 64) / 32, "fallback quads");
            Eq(2 * 768, BitConverter.ToInt32(SMOOTH_NEW[1], 0) / 32, "fallback triangles: two per quad");
            Eq(1000 * 39, BitConverter.ToInt32(SMOOTH_NEW[2], 0), "fallback vertices");
            ExePatch[] same = { quads, tris, verts };
            for (int l = 0; l < 2; l++)
                for (int i = 0; i < 3; i++)
                {
                    Eq((l == 0 ? same[i].Fr : same[i].En) + 1, SMOOTH_AT[l][i], DataKit.LANG[l] + ": fallback " + i + " on the immediate of the exe's mov eax");
                    EqBytes(Slice(Hex(same[i].Old), 1, 4), SMOOTH_OLD[i], "fallback " + i + ": the game's immediate");
                }
            // the edge objects take the prepared exe's vertex buffer (4608) to 8192
            for (int l = 0; l < 2; l++)
            {
                string[] h = EDGE_HOOKS[l][3];
                Eq(l == 0 ? verts.Fr : verts.En, Convert.ToUInt32(h[0], 16), DataKit.LANG[l] + ": edge hook on the vertex buffer");
                Eq(verts.New, h[1], DataKit.LANG[l] + ": expects the prepared exe's bytes");
                Eq(8192 * 39, imm(h[2]), DataKit.LANG[l] + ": 8192 vertices");
            }
        }

        // N, the end of the polygon ring, clamped to 48 where the builder reads it: the far smooth terrain's largest ring
        [Test] static void Data_TerrainExe_RingClampIsTheFarTerrainMaximum()
        {
            ExePatch n = Array.Find(TERRAIN_EXE, p => p.Fr == 0x2A8980);
            byte[] b = Hex(n.New);
            int max = SMOOTH_FAR[SMOOTH_FAR.Length - 1];
            Ok(DataKit.Contains(b, new byte[] { 0x83, 0xF8, (byte)max }), "cmp eax, " + max);
            Ok(DataKit.Contains(b, DataKit.Cat(new byte[] { 0xB8 }, DataKit.Le((uint)max))), "mov eax, " + max);
            Eq(48, max, "SMOOTH_FAR");
            Eq(n.Old.Length, n.New.Length, "same length");
        }

        // ================================================================== field of view, true 16:9, wedge, edge objects
        [Test] static void Data_Fov_HookLengthsAsTryFovReadsThem()
        {
            Eq(7, FOV_HOOK_OLD.Length, "FOV_HOOK_OLD (TryFov reads 7 bytes)");
            Eq(5, WIDE_B_OLD.Length, "WIDE_B_OLD (5)"); Eq(5, WEDGE_OLD.Length, "WEDGE_OLD (5)");
            foreach (Array a in new Array[] { FOV_HOOK, FOV_HOOK_NEW, TRUE_A_NEW, WIDE_HOOK_B, WIDE_B_NEW, TRUE_B_NEW, WIDE_HOOK_C, WIDE_C_OLD, WIDE_C_NEW, TRUE_C_NEW, WEDGE_HOOK, WEDGE_NEW, FOV_CODE, TRUE_A_CODE, TRUE_B_CODE, TRUE_C_CODE, WEDGE_CODE, EDGE_CAVES, EDGE_HOOKS, KB_HOOK, TERRAIN_SHIFT })
                Eq(2, a.Length, "French and English");
            for (int l = 0; l < 2; l++)
            {
                Eq(7, FOV_HOOK_NEW[l].Length, "FOV_HOOK_NEW"); Eq(7, TRUE_A_NEW[l].Length, "TRUE_A_NEW");
                foreach (byte[] b in new[] { WIDE_B_NEW[l], TRUE_B_NEW[l], WIDE_C_OLD[l], WIDE_C_NEW[l], TRUE_C_NEW[l], WEDGE_NEW[l] }) Eq(5, b.Length, "5-byte call");
                Eq(5, EDGE_HOOKS[l].Length, "edge hooks"); Eq(5, EDGE_CAVES[l].Length, "edge caves");
                foreach (string[] h in EDGE_HOOKS[l]) { Eq(3, h.Length, "address, old, new"); Eq(h[1].Length, h[2].Length, "edge hook " + h[0] + ": old / new"); }
                for (int c = 0; c < 5; c++) Eq(EDGE_CAVES[0][c][0], EDGE_CAVES[l][c][0], "edge cave " + c + ": same place in both executables");
            }
        }

        [Test] static void Data_Fov_HooksCallTheirCaves()
        {
            for (int l = 0; l < 2; l++)
            {
                string L = " [" + DataKit.LANG[l] + "]";
                uint code = FOV_CAVE + 0x10, codeEnd = code + (uint)FOV_CODE[l].Length / 2;
                Func<byte[], uint, uint> call = (b, at) => { Eq((byte)0xE8, b[0], "a call at " + DataKit.X(at) + L); return DataKit.Rel(at, b, 0); };
                Eq(code, call(FOV_HOOK_NEW[l], FOV_HOOK[l]), "field of view -> its code" + L);
                Eq(TRUE_A, call(TRUE_A_NEW[l], FOV_HOOK[l]), "true 16:9 set-up" + L);
                EqBytes(Slice(FOV_HOOK_NEW[l], 5, 2), Slice(TRUE_A_NEW[l], 5, 2), "both then jump over the same code" + L);
                Eq((byte)0xEB, FOV_HOOK_NEW[l][5], "jmp short" + L);
                Eq(TRUE_B, call(TRUE_B_NEW[l], WIDE_HOOK_B[l]), "true 16:9 scale" + L);
                Eq(TRUE_C, call(TRUE_C_NEW[l], WIDE_HOOK_C[l]), "true 16:9 ratio (accepted by TryFov, never written)" + L);
                Eq(WEDGE_CAVE, call(WEDGE_NEW[l], WEDGE_HOOK[l]), "wedge" + L);
                uint scale = call(WIDE_B_NEW[l], WIDE_HOOK_B[l]), ratio = call(WIDE_C_NEW[l], WIDE_HOOK_C[l]);
                Ok(scale > code && scale < codeEnd, "stretched 16:9 scale entry inside the field of view code" + L);
                Ok(ratio > code && ratio < codeEnd, "stretched 16:9 ratio entry inside the field of view code" + L);
                Eq((byte)0xE8, WIDE_C_OLD[l][0], "the ratio hook replaces a call" + L);
            }
        }

        // what the hooks replace is done again in the caves: the same instructions, or the call they took the place of
        [Test] static void Data_Fov_CavesRedoWhatTheHooksReplaced()
        {
            for (int l = 0; l < 2; l++)
            {
                string L = " [" + DataKit.LANG[l] + "]";
                byte[] fov = Hex(FOV_CODE[l]);
                uint code = FOV_CAVE + 0x10;
                Ok(DataKit.Contains(fov, FOV_HOOK_OLD), "field of view code redoes mov eax, 0x10000 / sub eax, ecx" + L);
                Ok(DataKit.Contains(Hex(TRUE_A_CODE[l]), FOV_HOOK_OLD), "true 16:9 set-up too" + L);
                // scale entry: ... then the two movs the hook replaced, ret
                int eb = (int)(DataKit.Rel(WIDE_HOOK_B[l], WIDE_B_NEW[l], 0) - code);
                byte[] tail = DataKit.Cat(WIDE_B_OLD, new byte[] { 0xC3 });
                Ok(DataKit.Contains(Slice(fov, eb, Math.Min(24, fov.Length - eb)), tail), "stretched scale entry ends with the replaced movs and ret" + L);
                byte[] tb = Hex(TRUE_B_CODE[l]);
                EqBytes(tail, Slice(tb, tb.Length - tail.Length, tail.Length), "true 16:9 scale ends with the replaced movs and ret" + L);
                // ratio entry: mov eax, the callee of the replaced call / call eax
                uint callee = DataKit.Rel(WIDE_HOOK_C[l], WIDE_C_OLD[l], 0);
                byte[] callIt = DataKit.Cat(new byte[] { 0xB8 }, DataKit.Le(callee), Bytes("FFD0"));
                int ec = (int)(DataKit.Rel(WIDE_HOOK_C[l], WIDE_C_NEW[l], 0) - code);
                EqBytes(callIt, Slice(fov, ec, callIt.Length), "stretched ratio entry calls " + DataKit.X(callee) + L);
                EqBytes(callIt, Slice(Hex(TRUE_C_CODE[l]), 0, callIt.Length), "TRUE_C_CODE too" + L);
                // the stretched entries scale by the dwords TryFov puts at FOV_CAVE + 4 (vertical x4/3) and + 8 (ratio x3/4)
                EqBytes(DataKit.Cat(Bytes("F72D"), DataKit.Le(FOV_CAVE + 4)), Slice(fov, eb, 6), "stretched scale entry: imul dword [FOV_CAVE + 4]" + L);
                Ok(DataKit.Contains(Slice(fov, ec, Math.Min(24, fov.Length - ec)), DataKit.Cat(Bytes("F72D"), DataKit.Le(FOV_CAVE + 8))), "stretched ratio entry: imul dword [FOV_CAVE + 8]" + L);
                // wedge: mov ecx, the callee of the replaced call / call ecx
                uint wcallee = DataKit.Rel(WEDGE_HOOK[l], WEDGE_OLD, 0);
                Ok(DataKit.Contains(Hex(WEDGE_CODE[l]), DataKit.Cat(new byte[] { 0xB9 }, DataKit.Le(wcallee), Bytes("FFD1"))), "wedge calls " + DataKit.X(wcallee) + L);
                No(DataKit.Contains(Hex(WEDGE_CODE[1 - l]), DataKit.Cat(new byte[] { 0xB9 }, DataKit.Le(wcallee), Bytes("FFD1"))), "the other executable's wedge does not" + L);
            }
        }

        // the main camera the field of view applies to: 0x38FBD4 French, 0x38FB24 English (the two TryZoomFix accepts)
        [Test] static void Data_Fov_MainCameraPerLanguage()
        {
            uint[] cam = { 0x38FBD4, 0x38FB24 };
            for (int l = 0; l < 2; l++)
            {
                string L = " [" + DataKit.LANG[l] + "]";
                byte[] esi = DataKit.Cat(Bytes("81FE"), DataKit.Le(cam[l])), ebp = DataKit.Cat(Bytes("81FD"), DataKit.Le(cam[l]));
                Ok(DataKit.Contains(Hex(FOV_CODE[l]), esi), "FOV_CODE: cmp esi, camera" + L);
                Ok(DataKit.Contains(Hex(TRUE_A_CODE[l]), esi), "TRUE_A_CODE" + L);
                Ok(DataKit.Contains(Hex(TRUE_B_CODE[l]), esi), "TRUE_B_CODE" + L);
                Ok(DataKit.Contains(Hex(TRUE_C_CODE[l]), ebp), "TRUE_C_CODE: cmp ebp, camera" + L);
                foreach (string c in new[] { FOV_CODE[l], TRUE_A_CODE[l], TRUE_B_CODE[l], TRUE_C_CODE[l] })
                    No(DataKit.Contains(Hex(c), DataKit.Le(cam[1 - l])), "not the other executable's camera" + L);
            }
        }

        // the free-area addresses their code uses: the 16 bytes TryFov writes at FOV_CAVE (K, vertical scale, ratio), and
        // for true 16:9 0x469400 (zoom factor) / 0x469404 (2 x canvas width), with 848 columns
        [Test] static void Data_Fov_CodeReadsTheDataTryFovWrites()
        {
            for (int l = 0; l < 2; l++)
            {
                string L = " [" + DataKit.LANG[l] + "]";
                List<uint> fov = DataKit.Refs(Hex(FOV_CODE[l])), ta = DataKit.Refs(Hex(TRUE_A_CODE[l])), tb = DataKit.Refs(Hex(TRUE_B_CODE[l]));
                foreach (uint a in fov) Ok(a == FOV_CAVE || a == FOV_CAVE + 4 || a == FOV_CAVE + 8, "FOV_CODE reads " + DataKit.X(a) + L);
                foreach (uint d in new[] { FOV_CAVE, FOV_CAVE + 4, FOV_CAVE + 8 }) Ok(fov.Contains(d), "FOV_CODE reads " + DataKit.X(d) + L);
                foreach (uint a in ta) Ok(a == FOV_CAVE || a == 0x469400 || a == 0x469404, "TRUE_A_CODE uses " + DataKit.X(a) + L);
                Ok(ta.Contains(FOV_CAVE) && ta.Contains(0x469400) && ta.Contains(0x469404), "TRUE_A_CODE: K, zoom factor, canvas width" + L);
                EqList(new List<uint> { 0x469404 }, tb, "TRUE_B_CODE: the canvas width" + L);
                Ok(DataKit.Contains(Hex(TRUE_A_CODE[l]), Bytes("BB50030000")) && DataKit.Contains(Hex(TRUE_B_CODE[l]), Bytes("BA50030000")), "848 columns" + L);
                Ok(TRUE_A + (uint)TRUE_A_CODE[l].Length / 2 <= TRUE_B, "the set-up code before the scale" + L);
                // TryFov checks [TRUE_A, end of TRUE_B) free: the descrambler's second cave starts after it
                Ok(TRUE_B + (uint)TRUE_B_CODE[l].Length / 2 <= DescramblerFix.At[DataKit.GenOf(l)][1], "TryFov's free check stops before the descrambler" + L);
            }
        }

        [Test] static void Data_Edge_HooksCavesAndTheirCalls()
        {
            for (int l = 0; l < 2; l++)
            {
                string L = " [" + DataKit.LANG[l] + "]";
                List<uint> starts = new List<uint>();
                foreach (string[] c in EDGE_CAVES[l]) starts.Add(Convert.ToUInt32(c[0], 16));
                int[] cave = { 2, 3, 4, -1, 0 };                       // the cave each hook goes to (hook 3: buffer size only)
                byte[] op = { 0xE8, 0xE8, 0xE9, 0xB8, 0xE8 };
                for (int h = 0; h < 5; h++)
                {
                    string[] e = EDGE_HOOKS[l][h];
                    uint at = Convert.ToUInt32(e[0], 16);
                    byte[] o = Hex(e[1]), n = Hex(e[2]);
                    Eq(op[h], n[0], "hook " + e[0] + ": opcode" + L);
                    if (cave[h] < 0) continue;
                    Eq(starts[cave[h]], DataKit.Rel(at, n, 0), "hook " + e[0] + " -> cave" + L);
                    for (int i = 5; i < n.Length; i++) Eq((byte)0x90, n[i], "hook " + e[0] + ": NOP padding" + L);
                    byte[] body = Hex(EDGE_CAVES[l][cave[h]][1]);
                    if (h == 0) Ok(DataKit.Contains(body, DataKit.Cat(Bytes("031D"), Slice(o, 2, 4))), "cave redoes the read of " + DataKit.X(BitConverter.ToUInt32(o, 2)) + L);
                    if (h == 1) Ok(DataKit.Contains(body, Slice(o, 2, o.Length - 2)), "cave redoes cmp ah, 3 / sete al" + L);
                    if (h == 2) EqBytes(o, Slice(body, body.Length - o.Length, o.Length), "cave ends with the replaced epilogue" + L);
                    if (h == 4)
                    {
                        uint callee = DataKit.Rel(at, o, 0);
                        Ok(DataKit.Contains(body, DataKit.Cat(new byte[] { 0xB9 }, DataKit.Le(callee), Bytes("FFD1"))), "cave calls the builder " + DataKit.X(callee) + " first" + L);
                    }
                }
                // cave 0 calls cave 1 (merge) and ends with the bit masks of the cell bitmap at 0x46A7A0
                byte[] c0 = Hex(EDGE_CAVES[l][0][1]);
                Ok(DataKit.Contains(c0, DataKit.Cat(new byte[] { 0xB9 }, DataKit.Le(starts[1]), Bytes("FFD1"))), "cave 0 calls cave 1" + L);
                Eq(0x46A7A0u, starts[0] + (uint)c0.Length - 8, "bit masks at the end of cave 0" + L);
                EqBytes(Bytes("0102040810204080"), Slice(c0, c0.Length - 8, 8), "bit masks" + L);
                Ok(DataKit.Refs(c0).Contains(0x46A7A0), "cave 0 reads them" + L);
            }
        }

        // EDGE_SIN: 256 sines in 16.16 (a full turn), at 0x469500 in the edge data, before the cell bitmap 0x469900
        [Test] static void Data_Edge_SineTable()
        {
            byte[] t = Hex(EDGE_SINTAB);
            Eq(1024, t.Length, "256 dwords");
            for (int i = 0; i < 256; i++)
            {
                int v = BitConverter.ToInt32(t, 4 * i), e = (int)Math.Round(65536 * Math.Sin(2 * Math.PI * i / 256));
                Ok(Math.Abs(v - e) <= 1, "sin(" + i + "/256 turn): " + v + ", expected " + e);
            }
            Eq(0, BitConverter.ToInt32(t, 0), "sin 0"); Eq(0x10000, BitConverter.ToInt32(t, 64 * 4), "sin 90");
            Eq(0, BitConverter.ToInt32(t, 128 * 4), "sin 180"); Eq(-0x10000, BitConverter.ToInt32(t, 192 * 4), "sin 270");
            Ok(EDGE_SIN >= 0x469410 && EDGE_SIN + 1024 <= 0x469900, "inside the edge data, before the cell bitmap");
            Ok(EDGE_MODE >= 0x469410 && EDGE_MODE + 4 <= EDGE_SIN, "EDGE_MODE inside the edge data");
            for (int l = 0; l < 2; l++)
            {
                List<uint> refs = new List<uint>();
                foreach (string[] c in EDGE_CAVES[l]) refs.AddRange(DataKit.Refs(Hex(c[1])));
                Ok(refs.Contains(EDGE_SIN) && refs.Contains(EDGE_MODE) && refs.Contains(0x469900), DataKit.LANG[l] + ": the caves use EDGE_SIN, EDGE_MODE and the bitmap");
            }
        }

        // ================================================================== game keys
        [Test] static void Data_Keys_DataLayout()
        {
            Eq(KB_DATA, KB_ACTIVE, "ACTIVE first");
            uint[] dw = { KB_ACTIVE, KB_STATE, KB_SITE, KB_RESUME, KB_PM, KB_PATCH };
            for (int i = 1; i < dw.Length; i++) Eq(dw[i - 1] + 4, dw[i], "dword " + i + " after the previous one");
            Ok(KB_PATCH + (uint)Rel32Call(0x300000, KB_MAP_FN, 11).Length <= KB_ACT, "the 11-byte call TryKeys keeps in KB_PATCH ends before KB_ACT");
            Eq(KB_ACT + 256, KB_MAP, "KB_ACT: a word per key, 128 keys");
            Eq(KB_MAP + (uint)BuildKeyMap().Length, KB_DATA_END, "KB_MAP: BuildKeyMap's 1024 bytes up to KB_DATA_END");
            Eq(KB_CODE, KB_INSTALL, "the code starts with the install routine");
            Ok(KB_MAP_FN > KB_CODE && KB_MAP_FN < KB_CODE + (uint)KB_CODE_HEX.Length / 2, "KB_MAP_FN inside the code");
            // the handler's site: the 11 bytes written over it stay within the bytes TryKeys found (KB_SITE_SIG), and the
            // resume point it checks (KB_RESUME_SIG at +0x6A) lies after them
            Ok(KB_SITE_SIG.Length / 2 >= 11, "KB_SITE_SIG covers the 11 bytes written over the site");
            Eq(8, KB_RESUME_SIG.Length / 2, "TryKeys reads 8 bytes at the resume point (site + 0x6A, past the 11)");
        }

        [Test] static void Data_Keys_CodeUsesItsOwnData()
        {
            byte[] code = Hex(KB_CODE_HEX), sweep = Hex(KB_SWEEP_HEX);
            uint codeEnd = KB_CODE + (uint)code.Length;
            foreach (uint a in DataKit.Refs(code)) Ok(a >= KB_DATA && a < KB_DATA_END, "KB code uses " + DataKit.X(a));
            foreach (uint a in DataKit.Refs(sweep)) Ok(a >= KB_DATA && a < KB_DATA_END, "KB sweep uses " + DataKit.X(a));
            // install: copies the 11 bytes of KB_PATCH to [KB_SITE] (from inside the game: DOSBox sees that write)
            Ok(DataKit.Contains(code, DataKit.Cat(new byte[] { 0xBE }, DataKit.Le(KB_PATCH), Bytes("8B3D"), DataKit.Le(KB_SITE), new byte[] { 0xB9 }, DataKit.Le(11), Bytes("F3A4"))),
                "mov esi, KB_PATCH / mov edi, [KB_SITE] / mov ecx, 11 / rep movsb");
            Ok(DataKit.Contains(code, DataKit.Cat(Bytes("8B0D"), DataKit.Le(KB_RESUME), Bytes("890C24C3"))), "back to the handler through KB_RESUME");
            // BuildKeyMap's layout: map[scancode * 4 + modifier], a word each = 8 bytes a key
            Ok(DataKit.Contains(code, DataKit.Cat(Bytes("0FB734C5"), DataKit.Le(KB_MAP))), "movzx esi, word [eax * 8 + KB_MAP]");
            Ok(DataKit.Reaches(KB_CODE, code, 0xE8, KB_SWEEP), "the code calls the sweep");
            bool back = false;
            for (int i = 0; i + 5 <= sweep.Length; i++)
                if (sweep[i] == 0xE8) { uint to = DataKit.Rel(KB_SWEEP, sweep, i); if (to > KB_CODE && to < codeEnd) back = true; }
            Ok(back, "the sweep calls back into the code (release of a key)");
            Eq((byte)0xC3, sweep[sweep.Length - 1], "the sweep returns");
        }

        // ================================================================== HD payloads
        [Test] static void Data_HdPayload_UsePicksTheVariant()
        {
            string[] names = { "Zone", "End", "PoolBase", "Code", "Data", "PoolVar", "PoolMax", "XCode", "Frames", "Smoothing", "HudFilter", "HudStretch", "FpsValue", "SpeedDone", "SvPtr", "CodeBytes", "DataInit", "PatchAt", "PatchOld", "PatchNew", "SvCode", "English" };
            for (int l = 0; l < 2; l++)
                for (int v = 0; v < 3; v++)
                {
                    object[] want;
                    if (l == 0 && v == 0) want = new object[] { HdPayloadFr.Zone, HdPayloadFr.End, HdPayloadFr.PoolBase, HdPayloadFr.Code, HdPayloadFr.Data, HdPayloadFr.PoolVar, HdPayloadFr.PoolMax, HdPayloadFr.XCode, HdPayloadFr.Frames, HdPayloadFr.Smoothing, HdPayloadFr.HudFilter, 0u, 0u, 0u, 0u, HdPayloadFr.CodeBytes, HdPayloadFr.DataInit, HdPayloadFr.PatchAt, HdPayloadFr.PatchOld, HdPayloadFr.PatchNew, null, false };
                    else if (l == 1 && v == 0) want = new object[] { HdPayloadEn.Zone, HdPayloadEn.End, HdPayloadEn.PoolBase, HdPayloadEn.Code, HdPayloadEn.Data, HdPayloadEn.PoolVar, HdPayloadEn.PoolMax, HdPayloadEn.XCode, HdPayloadEn.Frames, HdPayloadEn.Smoothing, HdPayloadEn.HudFilter, 0u, 0u, 0u, 0u, HdPayloadEn.CodeBytes, HdPayloadEn.DataInit, HdPayloadEn.PatchAt, HdPayloadEn.PatchOld, HdPayloadEn.PatchNew, null, true };
                    else if (l == 0 && v == 1) want = new object[] { HdPayloadFrWide.Zone, HdPayloadFrWide.End, HdPayloadFrWide.PoolBase, HdPayloadFrWide.Code, HdPayloadFrWide.Data, HdPayloadFrWide.PoolVar, HdPayloadFrWide.PoolMax, HdPayloadFrWide.XCode, HdPayloadFrWide.Frames, HdPayloadFrWide.Smoothing, HdPayloadFrWide.HudFilter, HdPayloadFrWide.HudStretch, 0u, 0u, 0u, HdPayloadFrWide.CodeBytes, HdPayloadFrWide.DataInit, HdPayloadFrWide.PatchAt, HdPayloadFrWide.PatchOld, HdPayloadFrWide.PatchNew, null, false };
                    else if (l == 1 && v == 1) want = new object[] { HdPayloadEnWide.Zone, HdPayloadEnWide.End, HdPayloadEnWide.PoolBase, HdPayloadEnWide.Code, HdPayloadEnWide.Data, HdPayloadEnWide.PoolVar, HdPayloadEnWide.PoolMax, HdPayloadEnWide.XCode, HdPayloadEnWide.Frames, HdPayloadEnWide.Smoothing, HdPayloadEnWide.HudFilter, HdPayloadEnWide.HudStretch, 0u, 0u, 0u, HdPayloadEnWide.CodeBytes, HdPayloadEnWide.DataInit, HdPayloadEnWide.PatchAt, HdPayloadEnWide.PatchOld, HdPayloadEnWide.PatchNew, null, true };
                    else if (l == 0) want = new object[] { HdPayloadFr720.Zone, HdPayloadFr720.End, HdPayloadFr720.PoolBase, HdPayloadFr720.Code, HdPayloadFr720.Data, HdPayloadFr720.PoolVar, HdPayloadFr720.PoolMax, HdPayloadFr720.XCode, HdPayloadFr720.Frames, HdPayloadFr720.Smoothing, HdPayloadFr720.HudFilter, HdPayloadFr720.HudStretch, HdPayloadFr720.FpsValue, HdPayloadFr720.SpeedDone, HdPayloadFr720.SvPtr, HdPayloadFr720.CodeBytes, HdPayloadFr720.DataInit, HdPayloadFr720.PatchAt, HdPayloadFr720.PatchOld, HdPayloadFr720.PatchNew, HdPayloadFr720.SvCode, false };
                    else want = new object[] { HdPayloadEn720.Zone, HdPayloadEn720.End, HdPayloadEn720.PoolBase, HdPayloadEn720.Code, HdPayloadEn720.Data, HdPayloadEn720.PoolVar, HdPayloadEn720.PoolMax, HdPayloadEn720.XCode, HdPayloadEn720.Frames, HdPayloadEn720.Smoothing, HdPayloadEn720.HudFilter, HdPayloadEn720.HudStretch, HdPayloadEn720.FpsValue, HdPayloadEn720.SpeedDone, HdPayloadEn720.SvPtr, HdPayloadEn720.CodeBytes, HdPayloadEn720.DataInit, HdPayloadEn720.PatchAt, HdPayloadEn720.PatchOld, HdPayloadEn720.PatchNew, HdPayloadEn720.SvCode, true };
                    // the 1280x720 module only with true 16:9 (Use(english, false, true) is the 640x400 one)
                    for (int h = 0; h < (v == 0 ? 2 : 1); h++)
                    {
                        if (h == 0) DataKit.Use(l, v); else HdPayload.Use(l == 1, false, true);
                        object[] have = { HdPayload.Zone, HdPayload.End, HdPayload.PoolBase, HdPayload.Code, HdPayload.Data, HdPayload.PoolVar, HdPayload.PoolMax, HdPayload.XCode, HdPayload.Frames, HdPayload.Smoothing, HdPayload.HudFilter, HdPayload.HudStretch, HdPayload.FpsValue, HdPayload.SpeedDone, HdPayload.SvPtr, HdPayload.CodeBytes, HdPayload.DataInit, HdPayload.PatchAt, HdPayload.PatchOld, HdPayload.PatchNew, HdPayload.SvCode, HdPayload.English };
                        for (int i = 0; i < names.Length; i++)
                            Ok(Equals(want[i], have[i]), DataKit.Of(l, v) + (h == 1 ? " (asked with hd720 but no 16:9)" : "") + ": " + names[i] + " expected " + Show(want[i]) + ", got " + Show(have[i]));
                        Eq(v == 2 ? 16 : 0, HdPayload.SpeedNames.Length, DataKit.Of(l, v) + ": speed fix names");
                    }
                }
        }

        [Test] static void Data_HdPayload_PatchShapes()
        {
            for (int l = 0; l < 2; l++)
                for (int v = 0; v < 3; v++)
                {
                    DataKit.Use(l, v);
                    string w = DataKit.Of(l, v);
                    int n = HdPayload.PatchAt.Length, differ = 0;
                    Ok(n > 0, w + ": patches");
                    Eq(n, HdPayload.PatchOld.Length, w + ": PatchAt / PatchOld");
                    Eq(n, HdPayload.PatchNew.Length, w + ": PatchAt / PatchNew");
                    for (int i = 0; i < n; i++)
                    {
                        string p = w + " patch " + i + " at " + DataKit.X(HdPayload.PatchAt[i]);
                        Ok(HdPayload.PatchOld[i].Length > 0, p + ": not empty");
                        Eq(HdPayload.PatchOld[i].Length, HdPayload.PatchNew[i].Length, p + ": TryHdInject reads Old's length and compares New with it");
                        Ok(HdPayload.PatchAt[i] >= 0x100000 && HdPayload.PatchAt[i] + (uint)HdPayload.PatchOld[i].Length <= DataKit.FreeStart, p + ": in the game's code");
                        if (!Same(HdPayload.PatchOld[i], HdPayload.PatchNew[i])) differ++;
                        // 1280x720: some patches only check the opcode of an instruction whose operand the HD code handles
                        else Ok(HdPayload.PatchOld[i].Length <= 3, p + ": Old = New only for an opcode check (" + HdPayload.PatchOld[i].Length + " bytes)");
                        for (int j = i + 1; j < n; j++)
                            No(DataKit.Overlap(HdPayload.PatchAt[i], HdPayload.PatchAt[i] + (uint)HdPayload.PatchOld[i].Length, HdPayload.PatchAt[j], HdPayload.PatchAt[j] + (uint)HdPayload.PatchOld[j].Length), w + ": patches " + i + " and " + j + " overlap");
                    }
                    // all old and all new must not be the same state (TryHdInject tells "to do" from "already active")
                    Ok(differ > 0, w + ": patches that change something: " + differ);
                    if (v < 2) Eq(n, differ, w + ": every patch changes something");
                }
        }

        [Test] static void Data_HdPayload_Layout()
        {
            // the 24 KB of the prepared exe (the co-op build gives its copy of __FF.EXE 8 KB more, its pool starts higher)
            Ok(DataKit.FreeEnd - DataKit.FreeStart >= OBJ3_HD - OBJ3_SIZE, "the free area: at least 24 KB, " + DataKit.X(DataKit.FreeEnd - DataKit.FreeStart));
            Eq(0x6000u, OBJ3_HD - OBJ3_SIZE, "the prepared exe's object 3: 24 KB more (French)");
            Eq(0x6000u, OBJ3_HD_EN - OBJ3_SIZE_EN, "(English)");
            for (int l = 0; l < 2; l++)
                for (int v = 0; v < 3; v++)
                {
                    DataKit.Use(l, v);
                    string w = DataKit.Of(l, v);
                    Eq(DataKit.FreeStart, HdPayload.Zone, w + ": the zone starts the free area");
                    Eq(DataKit.FreeEnd, HdPayload.PoolBase, w + ": the pool right after it");
                    Ok(HdPayload.Zone <= HdPayload.Code, w + ": Zone <= Code");
                    Ok(HdPayload.Code + (uint)HdPayload.CodeBytes.Length <= HdPayload.Data, w + ": code (" + HdPayload.CodeBytes.Length + " bytes) before Data");
                    Ok(HdPayload.Data + (uint)HdPayload.DataInit.Length <= HdPayload.End, w + ": data (" + HdPayload.DataInit.Length + " bytes) before End");
                    Ok(HdPayload.End <= HdPayload.PoolBase, w + ": End inside the free area");
                    // FreeAreaReady, TryFov and TryKeys read HdPayloadFr / HdPayloadEn directly, whatever variant is in use
                    Eq(l == 1 ? HdPayloadEn.PoolVar : HdPayloadFr.PoolVar, HdPayload.PoolVar, w + ": PoolVar");
                    Eq(l == 1 ? HdPayloadEn.PoolBase : HdPayloadFr.PoolBase, HdPayload.PoolBase, w + ": PoolBase");
                    Eq(l == 1 ? HdPayloadEn.PoolMax : HdPayloadFr.PoolMax, HdPayload.PoolMax, w + ": PoolMax");
                    Eq(l == 1 ? HdPayloadEn.DriverMode : HdPayloadFr.DriverMode, HdPayload.DriverMode, w + ": DriverMode");
                    // the variables TN+ reads or writes are in the data TryHdInject puts there
                    string[] vn = { "Smoothing", "HudFilter", "Frames", "Valid", "HdWidth", "HdHeight", "HudStretch", "XCode", "FpsValue", "SpeedDone", "SpeedOn", "SpeedBad", "SvPtr", "SvOn" };
                    uint[] vv = { HdPayload.Smoothing, HdPayload.HudFilter, HdPayload.Frames, HdPayload.Valid, HdPayload.HdWidth, HdPayload.HdHeight, HdPayload.HudStretch, HdPayload.XCode, HdPayload.FpsValue, HdPayload.SpeedDone, HdPayload.SpeedOn, HdPayload.SpeedBad, HdPayload.SvPtr, HdPayload.SvOn };
                    for (int i = 0; i < vv.Length; i++)
                    {
                        if (vv[i] == 0) continue;
                        Ok(vv[i] >= HdPayload.Data && vv[i] + 4 <= HdPayload.Data + (uint)HdPayload.DataInit.Length, w + ": " + vn[i] + " " + DataKit.X(vv[i]) + " in the HD data");
                        for (int j = i + 1; j < vv.Length; j++) if (vv[j] != 0) No(DataKit.Overlap(vv[i], vv[i] + 4, vv[j], vv[j] + 4), w + ": " + vn[i] + " and " + vn[j] + " overlap");
                    }
                    if (HdPayload.FpsColour != 0) Ok(HdPayload.FpsColour >= HdPayload.Data && HdPayload.FpsColour < HdPayload.Data + (uint)HdPayload.DataInit.Length, w + ": FpsColour in the HD data");
                    // FixSmallRings reads the three camera pointers at SvPtr + 4, + 8, + 12
                    if (HdPayload.SvPtr != 0) Ok(HdPayload.SvPtr + 16 <= HdPayload.SvOn || HdPayload.SvOn + 4 <= HdPayload.SvPtr, w + ": SvPtr and its three cameras clear of SvOn");
                    // speed fixes: a bit per group in a 32-bit word
                    Ok(HdPayload.SpeedNames.Length <= 32, w + ": speed groups in a dword");
                    Eq(0u, HdPayload.SpeedNames.Length >= 32 ? 0u : HdPayload.SpeedOptional >> HdPayload.SpeedNames.Length, w + ": optional groups among the named ones");
                    Eq(v == 2, HdPayload.SvCode != null && HdPayload.SvPtr != 0, w + ": the cockpit camera module only in 1280x720");
                }
        }

        [Test] static void Data_HdPayload_PatchCallsLandInItsCode()
        {
            for (int l = 0; l < 2; l++)
                for (int v = 0; v < 3; v++)
                {
                    DataKit.Use(l, v);
                    int calls = 0;
                    for (int i = 0; i < HdPayload.PatchAt.Length; i++)
                    {
                        byte[] b = HdPayload.PatchNew[i];
                        if (b.Length < 5 || (b[0] != 0xE8 && b[0] != 0xE9)) continue;
                        uint to = DataKit.Rel(HdPayload.PatchAt[i], b, 0);
                        Ok(to >= HdPayload.Code && to < HdPayload.Code + (uint)HdPayload.CodeBytes.Length, DataKit.Of(l, v) + " patch " + i + " -> " + DataKit.X(to) + ": into the HD code");
                        calls++;
                    }
                    Ok(calls >= 5, DataKit.Of(l, v) + ": calls into the HD code " + calls);
                }
        }

        // what the HD code and data refer to in the free area stays in [Code, End): nothing in [Zone, Code), where the turret
        // fix and the infrared stub go, nothing after End (the other fixes' code and data)
        [Test] static void Data_HdPayload_ReferencesStayInItsZone()
        {
            for (int l = 0; l < 2; l++)
                for (int v = 0; v < 3; v++)
                {
                    DataKit.Use(l, v);
                    List<uint> refs = DataKit.Refs(HdPayload.CodeBytes);
                    refs.AddRange(DataKit.Refs(HdPayload.DataInit));
                    if (HdPayload.SvCode != null) refs.AddRange(DataKit.Refs(HdPayload.SvCode));
                    Ok(refs.Count > 20, DataKit.Of(l, v) + ": addresses found " + refs.Count);
                    foreach (uint a in refs)
                        Ok(a >= HdPayload.Code && a < HdPayload.End, DataKit.Of(l, v) + ": refers to " + DataKit.X(a) + ", outside [Code, End)");
                    for (int i = 0; i < HdPayload.PatchNew.Length; i++)
                        foreach (uint a in DataKit.Refs(HdPayload.PatchNew[i]))
                            Ok(a >= HdPayload.Code && a < HdPayload.End, DataKit.Of(l, v) + ": patch " + i + " refers to " + DataKit.X(a) + ", outside [Code, End)");
                }
        }

        // The 12 KB HD buffer (XCode): IrFix at its first page, ViewFix at XOff, AffineFix at the next page (Off), the
        // 1280x720 module at SvOff; the page base is the buffer rounded up to 4 KB, so up to 0xFFF of it is lost
        [Test] static void Data_XCode_BlocksShareTheBufferWithoutOverlap()
        {
            int sv = 0, svLen = 0;
            for (int l = 0; l < 2; l++) { DataKit.Use(l, 2); sv = (int)HdPayload.SvOff; svLen = Math.Max(svLen, HdPayload.SvCode.Length); }
            string[] names = { "IrFix", "ViewFix", "AffineFix", "1280x720 module" };
            int[] from = { 0, ViewFix.XOff, AffineFix.Off, sv };
            int[] len = { Math.Max(IrFix.Size[0], IrFix.Size[1]), Math.Max(ViewFix.Size[0], ViewFix.Size[1]), Math.Max(AffineFix.Size[0], AffineFix.Size[1]), svLen };
            for (int i = 0; i < 4; i++)
            {
                Ok(len[i] > 0, names[i] + ": size");
                Ok(from[i] + len[i] <= 0x2000, names[i] + ": inside the two pages after the base (TrySmallViews' own limit)");
                Ok(0xFFF + from[i] + len[i] <= 12288, names[i] + ": inside the 12 KB buffer however it is aligned");
                for (int j = i + 1; j < 4; j++) No(from[i] < from[j] + len[j] && from[j] < from[i] + len[i], names[i] + " and " + names[j] + " overlap");
            }
            Ok(IrFix.Size[0] <= ViewFix.XOff && ViewFix.XOff + ViewFix.Size[0] <= 0x1000, "IrFix and ViewFix share the first page");
            Ok(sv >= AffineFix.Off + AffineFix.Size[0], "the module after the affine block");
            for (int l = 0; l < 2; l++) { DataKit.Use(l, 2); Eq((uint)sv, HdPayload.SvOff, DataKit.LANG[l] + ": SvOff"); }
        }

        [Test] static void Data_Sha_GeneratedChecksAreLowerCaseHex()
        {
            Dictionary<string, string> all = new Dictionary<string, string>();
            Action<string, string[]> add = (n, s) => { Eq(2, s.Length, n + ": one per executable"); for (int g = 0; g < s.Length; g++) all.Add(n + "[" + g + "]", s[g]); };
            add("IrFix.Sha", IrFix.Sha); add("IrInPlace.Sha", IrInPlace.Sha); add("ViewFix.Sha", ViewFix.Sha); add("AffineFix.Sha", AffineFix.Sha);
            add("SpanInPlace.Sha", SpanInPlace.Sha); add("SpanInPlace.OnSha", SpanInPlace.OnSha); add("SpanInPlace.RanSha", SpanInPlace.RanSha);
            // compared with BitConverter.ToString(..).Replace("-", "").ToLowerInvariant(): an upper-case digit never matches
            foreach (KeyValuePair<string, string> e in all) Ok(DataKit.IsSha(e.Value), e.Key + ": 64 lower-case hex digits");
            List<string> seen = new List<string>();
            foreach (KeyValuePair<string, string> e in all)
            {
                if (e.Key == "AffineFix.Sha[1]") { Eq(AffineFix.Sha[0], e.Value, "AffineFix: the bytes it copies are the same in both executables"); continue; }
                No(seen.Contains(e.Value), e.Key + ": the same as another check");
                seen.Add(e.Value);
            }
            string[] exe = { HD_SHA_ORIGINAL, HD_SHA_READY, HD_SHA_READY2, HD_SHA_READY3, HD_SHA_READY4, HD_SHA_READY5, EN_SHA, EN_SHA_READY, EN_SHA_READY2, EN_SHA_READY3, EN_SHA_READY4, EN_SHA_READY5 };
            foreach (string s in exe) { Ok(DataKit.IsSha(s), "exe " + s); No(seen.Contains(s), "an exe SHA among the code checks: " + s); }
        }

        // ================================================================== the free area 0x465024-0x46B024
        [Test] static void Data_FreeArea_MapInsideTheFreeArea()
        {
            for (int l = 0; l < 2; l++)
                foreach (DataKit.Area a in DataKit.FreeMap(l))
                {
                    Ok(a.B > a.A, DataKit.LANG[l] + ": " + a + ": not empty");
                    Ok(a.A >= DataKit.FreeStart && a.B <= DataKit.FreeEnd, DataKit.LANG[l] + ": " + a + " inside [" + DataKit.X(DataKit.FreeStart) + ", " + DataKit.X(DataKit.FreeEnd) + ")");
                }
        }

        // every fix can run in the same game: no two areas may share a byte
        [Test] static void Data_FreeArea_NoTwoAreasOverlap()
        {
            for (int l = 0; l < 2; l++)
            {
                List<DataKit.Area> m = DataKit.FreeMap(l);
                Ok(m.Count >= 35, "areas: " + m.Count);
                for (int i = 0; i < m.Count; i++)
                    for (int j = i + 1; j < m.Count; j++)
                        No(m[i].Hits(m[j]), DataKit.LANG[l] + ": " + m[i] + " and " + m[j] + " overlap");
            }
        }

        // TryHdInject wants all of [Zone, End) empty, but its code and data start at Code: the turret fix's three first caves
        // and the infrared affine stub sit in [Zone, Code). The main loop runs those fixes only once the HD is in (or
        // given up), see Data_Run_CavesWrittenBeforeHdMakeItRefuse.
        [Test] static void Data_FreeArea_OnlyTurretCavesAndIrStubInTheHdZone()
        {
            for (int l = 0; l < 2; l++)
            {
                List<string> inZone = new List<string>();
                foreach (DataKit.Area a in DataKit.FreeMap(l))
                {
                    if (a.Feature == "HD") continue;
                    for (int v = 0; v < 3; v++)
                    {
                        DataKit.Use(l, v);
                        bool zone = a.A < HdPayload.End && HdPayload.Zone < a.B;
                        if (!zone) continue;
                        Ok(a.B <= HdPayload.Code, DataKit.Of(l, v) + ": " + a + " before the HD code (" + DataKit.X(HdPayload.Code) + ")");
                        if (!inZone.Contains(a.ToString())) inZone.Add(a.ToString());
                    }
                }
                int g = DataKit.GenOf(l);
                List<string> want = new List<string>();
                for (int i = 0; i < 3; i++) want.Add(new DataKit.Area("Turret", "cave " + i, TurretFix.At[g][i], TurretFix.New[g][i].Length / 2).ToString());
                want.Add(new DataKit.Area("IR", "affine stub", IrFix.StubAffine, IrFix.CodeStubAffine[g].Length / 2).ToString());
                inZone.Sort(StringComparer.Ordinal); want.Sort(StringComparer.Ordinal);
                EqList(want, inZone, DataKit.LANG[l] + ": the areas inside the HD zone");
            }
        }

        // every free-area address a fix's code refers to is one of its own areas, and every data area is used by its code
        [Test] static void Data_FreeArea_CodeRefersOnlyToItsOwnAreas()
        {
            for (int l = 0; l < 2; l++)
            {
                List<DataKit.Area> m = DataKit.FreeMap(l);
                List<DataKit.Area> used = new List<DataKit.Area>();
                foreach (DataKit.Blob b in DataKit.FreeCode(l))
                    foreach (uint a in DataKit.Refs(b.Code))
                    {
                        DataKit.Area owner = m.Find(x => x.Holds(a, 1));
                        Ok(owner != null, DataKit.LANG[l] + ": " + b.Feature + " " + b.Name + " refers to " + DataKit.X(a) + ", in no area");
                        Eq(b.Feature, owner.Feature, DataKit.LANG[l] + ": " + b.Feature + " " + b.Name + " refers to " + DataKit.X(a) + " of " + owner);
                        if (!used.Contains(owner)) used.Add(owner);
                    }
                string[] data = { "Turret flag", "Descrambler timers", "Ashford text", "Filler opcode and displacement", "IR span pointer", "IR affine pointer", "View pointer 0", "View pointer 1", "View pointer 2", "FOV data", "FOV true 16:9 data", "Edge data", "Keys data" };
                foreach (string d in data)
                {
                    DataKit.Area a = m.Find(x => x.Feature + " " + x.Name == d);
                    Ok(a != null, "area " + d);
                    Ok(used.Contains(a), DataKit.LANG[l] + ": " + a + " used by its code");
                }
            }
        }

        // ================================================================== the game code: one fix per byte
        // Exceptions, by design: the edge objects' vertex buffer hook on the prepared exe's bytes (Data_TerrainExe_Buffer
        // SizesAgreeWithTheirLimits), and the in-memory smooth terrain, used only when __FF.EXE is not prepared (TrySmooth:
        // no TERRAIN_EXE then, and no edge objects: TryFov needs the prepared exe)
        [Test] static void Data_GameCode_FixesNeverWriteTheSameBytes()
        {
            for (int l = 0; l < 2; l++)
            {
                List<DataKit.Area> w = DataKit.GameWrites(l);
                foreach (DataKit.Area a in w) Ok(a.A >= 0x100000 && a.B <= DataKit.FreeStart, DataKit.LANG[l] + ": " + a + " in the game, below the free area");
                for (int i = 0; i < w.Count; i++)
                    for (int j = i + 1; j < w.Count; j++)
                    {
                        if (!w[i].Hits(w[j])) continue;
                        string f = w[i].Feature, g = w[j].Feature;
                        if (f == "HD" && g == "HD") continue;               // three variants of the same patches (one per game)
                        bool edgeOnExe = (f == "Edge" && g == "TerrainExe") || (f == "TerrainExe" && g == "Edge");
                        bool fallback = (f == "Smooth" && (g == "TerrainExe" || g == "Edge")) || (g == "Smooth" && (f == "TerrainExe" || f == "Edge"));
                        Ok(edgeOnExe || fallback, DataKit.LANG[l] + ": " + w[i] + " and " + w[j] + " write the same bytes");
                    }
            }
        }

        // what a fix copies or checks (SHA-256) must still be the game's own bytes when it reads them: no other fix writes
        // there. Exception: the in-place infrared fix and the rebuilt one (IrFix) are alternatives (TryIrFix takes one), and
        // the in-place one checks IrFix's two sites on purpose (Data_IrInPlace_ChecksItsEntriesAndTheIrFixSites).
        [Test] static void Data_GameCode_CopiedAndCheckedBytesUntouchedByOtherFixes()
        {
            for (int l = 0; l < 2; l++)
            {
                List<DataKit.Area> w = DataKit.GameWrites(l), r = DataKit.GameReads(l);
                Ok(r.Count > 100, "reads: " + r.Count);
                foreach (DataKit.Area a in r)
                    foreach (DataKit.Area b in w)
                    {
                        if (!a.Hits(b) || a.Feature == b.Feature) continue;
                        bool ir = (a.Feature == "IR" && b.Feature == "IrInPlace") || (a.Feature == "IrInPlace" && b.Feature == "IR");
                        Ok(ir, DataKit.LANG[l] + ": " + a + " (read) is written by " + b);
                    }
            }
        }

        // ================================================================== on made-up game memory: TN+'s own writers
        // HD injection, each variant: its code and data, every patch, and nothing else of the free area; [Zone, Code) empty
        [Test] static void Data_Run_HdInjectEachVariantFrench() { DataHdInject(0); }
        [Test] static void Data_Run_HdInjectEachVariantEnglish() { DataHdInject(1); }
        static void DataHdInject(int lang)
        {
            for (int v = 0; v < 3; v++)
                using (FakeGame m = new FakeGame())
                {
                    string what = DataKit.Of(lang, v);
                    DataKit.HdOptions(lang, v); hdState = 0; hdEarly = 0;
                    DataKit.Use(lang, v);
                    byte[] codeBytes = HdPayload.CodeBytes;
                    DataKit.PoolReady(m, lang);
                    DataKit.PlaceHdOld(m, what);
                    TryHdInject();
                    Eq(1, hdState, what + ": injected");
                    Ok(ReferenceEquals(codeBytes, HdPayload.CodeBytes), what + ": TryHdInject took this variant");
                    List<KeyValuePair<uint, byte[]>> written = new List<KeyValuePair<uint, byte[]>>();
                    DataKit.Wrote(written, HdPayload.Code, HdPayload.CodeBytes);
                    DataKit.Wrote(written, HdPayload.Data, DataKit.HdData());
                    DataKit.CheckFree(m, written, what);
                    Ok(m.AllZero(HdPayload.Zone, (int)(HdPayload.Code - HdPayload.Zone)), what + ": [Zone, Code) left empty");
                    for (int i = 0; i < HdPayload.PatchAt.Length; i++) EqBytes(HdPayload.PatchNew[i], m.Get(HdPayload.PatchAt[i], HdPayload.PatchNew[i].Length), what + ": patch " + i);
                    // TN+ attached again to that game: recognized as active, nothing written again
                    byte[] before = m.Get(DataKit.FreeStart, (int)(DataKit.FreeEnd - DataKit.FreeStart));
                    hdState = 0;
                    TryHdInject();
                    Eq(1, hdState, what + ": already active");
                    EqBytes(before, m.Get(DataKit.FreeStart, (int)(DataKit.FreeEnd - DataKit.FreeStart)), what + ": nothing written again");
                }
        }

        // why the main loop runs the cave fixes after the HD injection: one written first in [Zone, Code) and the HD is refused
        [Test] static void Data_Run_CavesWrittenBeforeHdMakeItRefuse()
        {
            for (int c = 0; c < 2; c++)
                using (FakeGame m = new FakeGame())
                {
                    int lang = 0, g = DataKit.GenOf(lang);
                    string what = c == 0 ? "turret fix" : "infrared stubs";
                    DataKit.PoolReady(m, lang);
                    DataKit.HdOptions(lang, 2); DataKit.Use(lang, 2);
                    DataKit.PlaceHdOld(m, "HD");
                    if (c == 0)
                    {
                        for (int i = TurretFix.Zones; i < TurretFix.At[g].Length; i++) DataKit.Place(m, TurretFix.At[g][i], Hex(TurretFix.Old[g][i]), "turret hook");
                        TryTurretFix();
                        Eq(1, turretFixState, what);
                    }
                    else
                    {
                        DataKit.Place(m, IrFix.SiteSpan[g], Hex(IrFix.OldSpan[g]), "IR span"); DataKit.Place(m, IrFix.SiteAffine[g], Hex(IrFix.OldAffine[g]), "IR affine");
                        TryIrFix();
                        Eq(1, irFixState, what);
                    }
                    StringWriter said = new StringWriter();
                    TextWriter console = Console.Out;
                    Console.SetOut(said);
                    try { TryHdInject(); }
                    finally { Console.SetOut(console); }
                    Eq(-1, hdState, what + " first: the HD is refused");
                    Has(said.ToString(), "its memory area is not free", what + " first: the reason said");
                    Ok(m.AllZero(HdPayload.Code, HdPayload.CodeBytes.Length), what + " first: no HD code written");
                }
        }

        // every TryPieces table, both executables: applied from the game's bytes, recognized once applied, and refused on
        // memory that is not the game's (an empty one)
        [Test] static void Data_Run_TryPiecesAppliesEachTable()
        {
            string[] names = { "TurretFix", "DescramblerFix", "AshfordFix", "FillerFix", "Sky", "Drone", "Smoke" };
            uint[][][] ats = { TurretFix.At, DescramblerFix.At, AshfordFix.At, FillerFix.At, NativeFixes.SkyAt, NativeFixes.DroneAt, SMOKE_AT };
            string[][][] olds = { TurretFix.Old, DescramblerFix.Old, AshfordFix.Old, FillerFix.Old, NativeFixes.SkyOld, NativeFixes.DroneOld, SMOKE_OLD };
            string[][][] news = { TurretFix.New, DescramblerFix.New, AshfordFix.New, FillerFix.New, NativeFixes.SkyNew, NativeFixes.DroneNew, SMOKE_NEW };
            using (FakeGame m = new FakeGame())
                for (int t = 0; t < names.Length; t++)
                {
                    Array.Clear(m.Mem, 0, m.Mem.Length);
                    DataKit.PoolReady(m, 0);
                    Eq(-1, TryPieces(ats[t], olds[t], news[t], names[t], "test"), names[t] + ": refused on empty memory");
                    for (int g = 0; g < 2; g++)
                    {
                        string w = names[t] + "[" + g + "]";
                        Array.Clear(m.Mem, 0, m.Mem.Length);
                        DataKit.PoolReady(m, 1 - g);
                        for (int i = 0; i < ats[t][g].Length; i++) DataKit.Place(m, ats[t][g][i], Hex(olds[t][g][i]), w + " piece " + i);
                        Eq(1, TryPieces(ats[t], olds[t], news[t], names[t], "test"), w + ": applied");
                        for (int i = 0; i < ats[t][g].Length; i++) EqBytes(Hex(news[t][g][i]), m.Get(ats[t][g][i], news[t][g][i].Length / 2), w + " piece " + i);
                        // the other executable's places that are not this one's: untouched
                        for (int i = 0; i < ats[t][1 - g].Length; i++)
                        {
                            uint a = ats[t][1 - g][i]; int n = news[t][1 - g][i].Length / 2;
                            bool shared = false;
                            for (int k = 0; k < ats[t][g].Length; k++) if (DataKit.Overlap(a, a + (uint)n, ats[t][g][k], ats[t][g][k] + (uint)news[t][g][k].Length / 2)) shared = true;
                            if (!shared) Ok(m.AllZero(a, n), w + ": the other executable's place " + DataKit.X(a) + " left alone");
                        }
                        byte[] snap = m.Get(0x200000, (int)(DataKit.FreeEnd - 0x200000));
                        Eq(1, TryPieces(ats[t], olds[t], news[t], names[t], "test"), w + ": already on");
                        Ok(Same(snap, m.Get(0x200000, snap.Length)), w + ": nothing written again");
                    }
                }
        }

        // Stretched 16:9 without HD: the camera hooks go to the field of view code's entries, its data holds the zoom factor
        // (x0.75: 100.9 degrees), the vertical scale x4/3 and the pixel ratio x3/4 in 16.16; nothing of true 16:9 written
        [Test] static void Data_Run_FovStretched16x9()
        {
            using (FakeGame m = new FakeGame())
            {
                int lang = 0;
                DataKit.PoolReady(m, lang);
                OptHD = false; OptWide = true; OptWideTrue = false; OptWideAttach = true; haveWide = true; OptFov = 0;
                Ok(WideActive() && !TrueWideActive(), "stretched 16:9 running");
                DataKit.Place(m, FOV_HOOK[lang], FOV_HOOK_OLD, "FOV hook");
                DataKit.Place(m, WIDE_HOOK_B[lang], WIDE_B_OLD, "scale hook");
                DataKit.Place(m, WIDE_HOOK_C[lang], WIDE_C_OLD[lang], "ratio hook");
                DataKit.Place(m, WEDGE_HOOK[lang], WEDGE_OLD, "wedge hook");
                foreach (string[] h in EDGE_HOOKS[lang]) DataKit.Place(m, Convert.ToUInt32(h[0], 16), Hex(h[1]), "edge hook " + h[0]);
                TryFov();
                Eq(1, fovState, "field of view on"); Ok(wideHooks, "16:9 by the camera hooks"); Eq(1, edgeState, "edge objects");
                List<KeyValuePair<uint, byte[]>> w = new List<KeyValuePair<uint, byte[]>>();
                DataKit.Wrote(w, FOV_CAVE, DataKit.Cat(DataKit.Le(49152), DataKit.Le(0x15555), DataKit.Le(0xC000), new byte[4]));
                DataKit.Wrote(w, FOV_CAVE + 0x10, Hex(FOV_CODE[lang]));
                DataKit.Wrote(w, WEDGE_CAVE, Hex(WEDGE_CODE[lang]));
                DataKit.Wrote(w, EDGE_SIN, Hex(EDGE_SINTAB));
                DataKit.Wrote(w, EDGE_MODE, DataKit.Le(2));
                foreach (string[] c in EDGE_CAVES[lang]) DataKit.Wrote(w, Convert.ToUInt32(c[0], 16), Hex(c[1]));
                DataKit.CheckFree(m, w, "stretched 16:9");
                EqBytes(FOV_HOOK_NEW[lang], m.Get(FOV_HOOK[lang], 7), "camera set-up -> field of view code");
                EqBytes(WIDE_B_NEW[lang], m.Get(WIDE_HOOK_B[lang], 5), "vertical scale -> its entry");
                EqBytes(WIDE_C_NEW[lang], m.Get(WIDE_HOOK_C[lang], 5), "pixel ratio -> its entry");
                Ok(m.AllZero(TRUE_A, (int)(TRUE_B - TRUE_A) + TRUE_B_CODE[lang].Length / 2), "no true 16:9 code");
            }
        }

        // Everything at once, in the main loop's order (HD 1280x720 first, the field of view in true 16:9 with its wedge
        // and edge objects, the game keys, then the cave fixes): each area holds what its writer put there, no byte of the
        // free area is set outside them, and the game code's hooks now go there
        [Test] static void Data_Run_AllWritersTogetherFrench() { DataAllWriters(0); }
        [Test] static void Data_Run_AllWritersTogetherEnglish() { DataAllWriters(1); }
        static void DataAllWriters(int lang)
        {
            int g = DataKit.GenOf(lang);
            string L = DataKit.LANG[lang];
            using (FakeGame m = new FakeGame())
            {
                // ---- the game's own bytes where each fix looks (from TN+'s tables), the pool where the prepared exe has it
                DataKit.PoolReady(m, lang);
                DataKit.HdOptions(lang, 2);
                OptFov = 3;                                                // 110 degrees
                DataKit.Use(lang, 2);
                DataKit.PlaceHdOld(m, L + " HD");
                DataKit.Place(m, FOV_HOOK[lang], FOV_HOOK_OLD, "FOV hook");
                DataKit.Place(m, WIDE_HOOK_B[lang], WIDE_B_OLD, "scale hook");
                DataKit.Place(m, WIDE_HOOK_C[lang], WIDE_C_OLD[lang], "ratio hook");
                DataKit.Place(m, WEDGE_HOOK[lang], WEDGE_OLD, "wedge hook");
                foreach (string[] h in EDGE_HOOKS[lang]) DataKit.Place(m, Convert.ToUInt32(h[0], 16), Hex(h[1]), "edge hook " + h[0]);
                DataKit.Place(m, KB_HOOK[lang], Hex(KB_HOOK_OLD), "keys hook");
                uint site = 0x33F800;                                      // the keyboard handler, somewhere TryKeys looks
                DataKit.Place(m, site, Hex(KB_SITE_SIG), "keyboard handler");
                DataKit.Place(m, site + 0x6A, Hex(KB_RESUME_SIG), "keyboard handler, resume point");
                // the view reset: mov edx, 4 / mov ebx, [mode] / mov eax, camera / sub edx, ebx / xor ebx, ebx / xor ecx, ecx / call init
                uint zoomAt = 0x27F000, cam = lang == 1 ? 0x38FB24u : 0x38FBD4u, init = 0x2AC7A0, hook = zoomAt + 22;
                DataKit.Place(m, zoomAt, DataKit.Cat(Bytes("BA04000000 8B1D"), DataKit.Le(cam - 0xB4), Bytes("B8"), DataKit.Le(cam), Bytes("29DA 31DB 31C9 E8"),
                    BitConverter.GetBytes((int)init - (int)(hook + 5))), "view reset");
                uint[][] ats = { TurretFix.At[g], DescramblerFix.At[g], AshfordFix.At[g], FillerFix.At[g] };
                string[][] olds = { TurretFix.Old[g], DescramblerFix.Old[g], AshfordFix.Old[g], FillerFix.Old[g] }, news = { TurretFix.New[g], DescramblerFix.New[g], AshfordFix.New[g], FillerFix.New[g] };
                for (int t = 0; t < ats.Length; t++)
                    for (int i = 0; i < ats[t].Length; i++) if (ats[t][i] < DataKit.FreeStart) DataKit.Place(m, ats[t][i], Hex(olds[t][i]), "cave fix " + t + " piece " + i);
                DataKit.Place(m, IrFix.SiteSpan[g], Hex(IrFix.OldSpan[g]), "IR span entry");
                DataKit.Place(m, IrFix.SiteAffine[g], Hex(IrFix.OldAffine[g]), "IR affine entry");
                for (int r = 0; r < ViewFix.Site[g].Length; r++) DataKit.Place(m, ViewFix.Site[g][r], Hex(ViewFix.Old[g][r]), "view entry " + r);
                FindGameKey("fwd").Sc = 0x48;                              // one game key moved: the key translation is needed

                // ---- the writers
                TryHdInject(); Eq(1, hdState, L + ": HD");
                TryFov(); Eq(1, fovState, L + ": field of view"); Eq(1, edgeState, L + ": edge objects");
                TryKeys(); Eq(1, keysState, L + ": game keys");
                TryZoomFix(); Eq(1, zoomFixState, L + ": zoom fix");
                TryTurretFix(); Eq(1, turretFixState, L + ": turret fix");
                TryDescramblerFix(); Eq(1, descrFixState, L + ": descrambler fix");
                TryFillerFix(); Eq(1, fillerFixState, L + ": polygon filler fix");
                TryIrFix(); Eq(1, irFixState, L + ": infrared fix, stubs and entries");
                TryViewFix(); Eq(1, viewFixState, L + ": normal view fix, stubs and entries");
                TryAshfordFix(); Eq(1, ashfordState, L + ": Ashford fix");

                // ---- what each one wrote in the free area
                List<KeyValuePair<uint, byte[]>> w = new List<KeyValuePair<uint, byte[]>>();
                DataKit.Wrote(w, HdPayload.Code, HdPayload.CodeBytes);
                DataKit.Wrote(w, HdPayload.Data, DataKit.HdData());
                byte[] fovData = m.Get(FOV_CAVE, 16);
                double k = 1 / (1.1 * Math.Tan(110 * Math.PI / 360));
                k *= 848.0 / 640;                                          // true 16:9 with the HUD stretched (wide_hud)
                int kWant = (int)Math.Round(65536 * k), kHave = BitConverter.ToInt32(fovData, 0);
                Ok(Math.Abs(kHave - kWant) <= 1, L + ": K = " + kHave + ", expected " + kWant);
                DataKit.Wrote(w, FOV_CAVE, DataKit.Cat(DataKit.Le((uint)kHave), DataKit.Le(0x10000), DataKit.Le(0x10000), new byte[4]));
                DataKit.Wrote(w, FOV_CAVE + 0x10, Hex(FOV_CODE[lang]));
                DataKit.Wrote(w, TRUE_A, Hex(TRUE_A_CODE[lang]));
                DataKit.Wrote(w, TRUE_B, Hex(TRUE_B_CODE[lang]));
                DataKit.Wrote(w, WEDGE_CAVE, Hex(WEDGE_CODE[lang]));
                DataKit.Wrote(w, EDGE_SIN, Hex(EDGE_SINTAB));
                DataKit.Wrote(w, EDGE_MODE, DataKit.Le(2));
                foreach (string[] c in EDGE_CAVES[lang]) DataKit.Wrote(w, Convert.ToUInt32(c[0], 16), Hex(c[1]));
                byte[] kb = new byte[KB_DATA_END - KB_DATA];
                DataKit.SetInt(kb, KB_STATE - KB_DATA, 1);
                DataKit.SetInt(kb, KB_SITE - KB_DATA, (int)site);
                DataKit.SetInt(kb, KB_RESUME - KB_DATA, (int)(site + 0x6A));
                Rel32Call(site, KB_MAP_FN, 11).CopyTo(kb, (int)(KB_PATCH - KB_DATA));
                BuildKeyMap().CopyTo(kb, (int)(KB_MAP - KB_DATA));
                DataKit.Wrote(w, KB_DATA, kb);
                DataKit.Wrote(w, KB_CODE, Hex(KB_CODE_HEX));
                DataKit.Wrote(w, KB_SWEEP, Hex(KB_SWEEP_HEX));
                // movsx ebx, word [camera + 0xBE] / jmp init
                DataKit.Wrote(w, ZOOMFIX_CAVE, DataKit.Cat(Bytes("0FBF1D"), DataKit.Le(cam + 0xBE), Bytes("E9"), BitConverter.GetBytes((int)init - (int)(ZOOMFIX_CAVE + 12))));
                for (int t = 0; t < ats.Length; t++)
                    for (int i = 0; i < ats[t].Length; i++) if (ats[t][i] >= DataKit.FreeStart) DataKit.Wrote(w, ats[t][i], Hex(news[t][i]));
                DataKit.Wrote(w, IrFix.StubSpan, Hex(IrFix.CodeStubSpan[g]));
                DataKit.Wrote(w, IrFix.StubAffine, Hex(IrFix.CodeStubAffine[g]));
                DataKit.Wrote(w, IrFix.PtrSpan, DataKit.Le(IrFix.StubSpan));
                DataKit.Wrote(w, IrFix.PtrAffine, DataKit.Le(IrFix.StubAffine));
                for (int r = 0; r < ViewFix.Stub.Length; r++)
                {
                    DataKit.Wrote(w, ViewFix.Stub[r], Hex(ViewFix.CodeStub[g][r]));
                    DataKit.Wrote(w, ViewFix.Ptr[r], DataKit.Le(ViewFix.Stub[r]));
                }
                DataKit.CheckFree(m, w, L);

                // ---- the game code now goes there
                EqBytes(TRUE_A_NEW[lang], m.Get(FOV_HOOK[lang], 7), L + ": camera set-up -> true 16:9 set-up");
                EqBytes(TRUE_B_NEW[lang], m.Get(WIDE_HOOK_B[lang], 5), L + ": vertical scale -> true 16:9 scale");
                EqBytes(WIDE_C_OLD[lang], m.Get(WIDE_HOOK_C[lang], 5), L + ": the pixel ratio call left to the game in true 16:9");
                EqBytes(WEDGE_NEW[lang], m.Get(WEDGE_HOOK[lang], 5), L + ": wedge");
                foreach (string[] h in EDGE_HOOKS[lang]) EqBytes(Hex(h[2]), m.Get(Convert.ToUInt32(h[0], 16), h[2].Length / 2), L + ": edge hook " + h[0]);
                EqBytes(Rel32Call(KB_HOOK[lang], KB_INSTALL, 8), m.Get(KB_HOOK[lang], 8), L + ": keys hook");
                EqBytes(Hex(KB_SITE_SIG), m.Get(site, KB_SITE_SIG.Length / 2), L + ": the handler's site left to the install code (run by the game)");
                EqBytes(DataKit.Cat(Bytes("E8"), BitConverter.GetBytes((int)ZOOMFIX_CAVE - (int)(hook + 5))), m.Get(hook, 5), L + ": view reset -> zoom cave");
                for (int t = 0; t < ats.Length; t++)
                    for (int i = 0; i < ats[t].Length; i++) if (ats[t][i] < DataKit.FreeStart) EqBytes(Hex(news[t][i]), m.Get(ats[t][i], news[t][i].Length / 2), L + ": cave fix " + t + " piece " + i);
                EqBytes(Hex(IrFix.NewSpan[g]), m.Get(IrFix.SiteSpan[g], 6), L + ": IR span entry");
                EqBytes(Hex(IrFix.NewAffine[g]), m.Get(IrFix.SiteAffine[g], 6), L + ": IR affine entry");
                for (int r = 0; r < ViewFix.Site[g].Length; r++) EqBytes(Hex(ViewFix.New[r]), m.Get(ViewFix.Site[g][r], 6), L + ": view entry " + r);
                for (int i = 0; i < HdPayload.PatchAt.Length; i++) EqBytes(HdPayload.PatchNew[i], m.Get(HdPayload.PatchAt[i], HdPayload.PatchNew[i].Length), L + ": HD patch " + i);
            }
        }
    }
}
