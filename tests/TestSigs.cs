// Terra Nova Plus tests - the code signatures and the guest RAM scan (TNPlus.cs: SIGS, HAZE_TEXT, Sig, Latin1, Cap, Best,
// IndexOf, Inside, ResolveAll, ScanRegion, PlausibleIvt), on made-up memory holding hand-built instances of the signatures.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ================================================================== helpers (all named Sigs*)
        // every signature of SIGS, in the order of the made-up code (SigsWorld)
        static readonly string[] SigsNames = { "camera", "haze", "mouse", "draw", "frame", "freeze", "res", "weather", "aspect", "scale", "ratioref", "terrain", "stereo" };

        // the bytes of an instance of a signature in SIGS's format: hex bytes as they are, "...." -> 4 x fill,
        // "(....)" -> the next of caps (little-endian)
        static byte[] SigsBytes(string sig, uint[] caps, byte fill)
        {
            List<byte> b = new List<byte>();
            int c = 0;
            foreach (string tok in sig.Split(' '))
            {
                if (tok == "(....)")
                {
                    if (c >= caps.Length) Fail("signature \"" + sig + "\": more captures than the " + caps.Length + " given");
                    b.AddRange(BitConverter.GetBytes(caps[c++]));
                }
                else if (tok == "....") { b.Add(fill); b.Add(fill); b.Add(fill); b.Add(fill); }
                else for (int i = 0; i < tok.Length; i += 2) b.Add(Convert.ToByte(tok.Substring(i, 2), 16));
            }
            if (c != caps.Length) Fail("signature \"" + sig + "\": " + c + " captures, " + caps.Length + " given");
            return b.ToArray();
        }

        static byte[] SigsInst(string name, params uint[] caps) { return SigsBytes(SIGS[name], caps, 0xCC); }

        // per byte of an instance: L literal, W wildcard, C capture
        static string SigsKinds(string sig)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string tok in sig.Split(' '))
            {
                if (tok == "(....)") sb.Append("CCCC");
                else if (tok == "....") sb.Append("WWWW");
                else sb.Append('L', tok.Length / 2);
            }
            return sb.ToString();
        }

        static int SigsCapCount(string sig)
        {
            int n = 0;
            foreach (string tok in sig.Split(' ')) if (tok == "(....)") n++;
            return n;
        }

        static byte[] SigsCat(params byte[][] parts)
        {
            List<byte> b = new List<byte>();
            foreach (byte[] p in parts) b.AddRange(p);
            return b.ToArray();
        }

        // does the signature match somewhere in these bytes (hex)
        static bool SigsIs(string sig, string hex) { return Sig(sig).IsMatch(Latin1(Bytes(hex))); }

        static string SigsHave()
        {
            return "fog=" + haveFog + " 400=" + have400 + " wide=" + haveWide + " stereo=" + haveStereo + " detail=" + haveDetail;
        }

        const string SigsAllHave = "fog=True 400=True wide=True stereo=True detail=True";

        // the program's start values of what ResolveAll sets
        static void SigsReset()
        {
            aHazeText = aPlayerId = aMaster = aCamHeading = aPitch = aWarp = aCursor = aDraw = aFrame = aFreeze = 0;
            aFogTable = aWeather = aResFlags = aResMode = aResCallback = aResButton = 0;
            aRatio = aRatioRef = aScaleX = aTerrain = aG3Width = aStereo = 0;
            haveFog = have400 = haveWide = haveDetail = haveStereo = false;
        }

        // Made-up guest RAM: one relocated instance of every signature (at CODE + 0x80 x its rank in SigsNames) whose
        // captures are consistent addresses of a data area (0x108000-0x10C1FF), the haze text, the 320x400 radio button
        // (a pointer to the res code, its state byte 8 bytes further). A test changes fields before Image() to break one
        // thing. ResolveAll's region is guest 0x100000-0x10FFFF (ORIGIN-END), ScanRegion's the whole FakeGame memory.
        sealed class SigsWorld
        {
            public const uint ORIGIN = 0x100000, END = 0x110000, CODE = 0x101000;
            public const int SIZE = 0x10000;
            public const long HOST = 0x50000000;        // made-up host address of guest 0 (ResolveAll itself reads no memory)
            public uint PlayerId = 0x108000, Master = 0x108100, HeadYaw = 0x108200, CamHeading = 0x108204, HeadPitch = 0x108208, CamPitch = 0x10820C;
            public uint FogCopy = 0x109400, FogTable = 0x109000, Text = 0x10A000;
            public uint Warp = 0x10B001, Cursor = 0x10B010, CursorY = 0x10B014, Freeze = 0x10B000;
            public uint Draw = 0x10B020, FrameW = 0x10B030, Frame = 0x10B034;
            public uint ResFlags = 0x10B040, ResMode = 0x10B044, Weather = 0x10B050;
            public uint AspH = 0x10B060, Ratio = 0x10B064, G3Width = 0x10B068;
            public uint Scale1 = 0x10B070, Scale2 = 0x10B074, Scale3 = 0x10B078;
            public uint RatioRef = 0x10B080, RrRatio = 0x10B064, Rr3 = 0x10B084;
            public uint Terrain = 0x10B100, TerrainCanvas = 0x10B11C;
            public uint Stereo = 0x10B200;
            public uint ButtonAt = 0x10C000;            // 0: no radio button pointing to the res code
            public byte ButtonState = 1;
            public bool RawCopies = false;              // + unrelocated copies (addresses - 0x100000) of all but haze, before and after
            public readonly HashSet<string> Omit = new HashSet<string>();
            public readonly List<KeyValuePair<uint, byte[]>> Extra = new List<KeyValuePair<uint, byte[]>>();

            public static uint At(string name) { return CODE + 0x80u * (uint)Array.IndexOf(SigsNames, name); }

            public void Add(uint guest, byte[] b) { Extra.Add(new KeyValuePair<uint, byte[]>(guest, b)); }

            public uint[] Caps(string name)
            {
                switch (name)
                {
                    case "camera": return new[] { PlayerId, Master, HeadYaw, CamHeading, HeadPitch, CamPitch };
                    case "haze": return new[] { FogCopy, FogTable, Text };
                    case "mouse": return new[] { Warp, Cursor, CursorY };
                    case "draw": return new[] { Draw };
                    case "frame": return new[] { FrameW, Frame };
                    case "freeze": return new[] { Freeze };
                    case "res": return new[] { ResFlags, ResMode };
                    case "weather": return new[] { Weather };
                    case "aspect": return new[] { AspH, Ratio, G3Width };
                    case "scale": return new[] { Scale1, Scale2, Scale3 };
                    case "ratioref": return new[] { RatioRef, RrRatio, Rr3 };
                    case "terrain": return new[] { Terrain, TerrainCanvas };
                    case "stereo": return new[] { Stereo };
                }
                Fail("no signature " + name);
                return null;
            }

            // the guest bytes origin .. origin + size - 1
            public byte[] Image(uint origin, int size)
            {
                byte[] img = new byte[size];
                for (int k = 0; k < SigsNames.Length; k++)
                {
                    string n = SigsNames[k];
                    if (Omit.Contains(n)) continue;
                    uint[] caps = Caps(n);
                    Put(img, origin, At(n), SigsInst(n, caps));
                    if (RawCopies && n != "haze")
                    {
                        uint[] raw = new uint[caps.Length];
                        for (int i = 0; i < caps.Length; i++) raw[i] = caps[i] >= 0x100000 ? caps[i] - 0x100000 : caps[i];
                        Put(img, origin, 0x100400 + 0x80u * (uint)k, SigsInst(n, raw));
                        Put(img, origin, 0x102000 + 0x80u * (uint)k, SigsInst(n, raw));
                    }
                }
                Put(img, origin, Text, HAZE_TEXT);
                if (ButtonAt != 0)
                {
                    Put(img, origin, ButtonAt, BitConverter.GetBytes(At("res")));
                    Put(img, origin, ButtonAt + 8, new byte[] { ButtonState });
                }
                foreach (KeyValuePair<uint, byte[]> e in Extra) Put(img, origin, e.Key, e.Value);
                return img;
            }

            static void Put(byte[] img, uint origin, uint guest, byte[] b)
            {
                if (guest < origin || guest - origin + b.Length > img.Length)
                    Fail("made-up memory: 0x" + guest.ToString("X") + " outside 0x" + origin.ToString("X") + "-0x" + (origin + img.Length).ToString("X"));
                Array.Copy(b, 0, img, (int)(guest - origin), b.Length);
            }
        }

        // ResolveAll on the world's region (guest 0x100000-0x10FFFF at a made-up host address), from the program's start
        // values; the haze match is the first relocated one (text address >= 1 MB), as ScanRegion picks it
        static bool SigsResolve(SigsWorld w)
        {
            SigsReset();
            guestBase = SigsWorld.HOST; regStart = SigsWorld.HOST + SigsWorld.ORIGIN; regSize = SigsWorld.SIZE;
            byte[] buf = w.Image(SigsWorld.ORIGIN, SigsWorld.SIZE);
            string mem = Latin1(buf);
            for (Match m = Sig(SIGS["haze"]).Match(mem); m.Success; m = m.NextMatch())
                if (Cap(m, 3) >= 0x100000) return ResolveAll(buf, mem, m);
            Fail("made-up memory without a relocated haze signature");
            return false;
        }

        // the addresses every attach needs (ResolveAll fails without them)
        static void SigsCheckCore(SigsWorld w)
        {
            Eq(w.Text, aHazeText, "aHazeText (haze capture 3)");
            Eq(w.PlayerId, aPlayerId, "aPlayerId (camera capture 1)");
            Eq(w.Master, aMaster, "aMaster (camera capture 2)");
            Eq(w.CamHeading, aCamHeading, "aCamHeading (camera capture 4)");
            Eq(w.HeadPitch, aPitch, "aPitch (camera capture 5)");
            Eq(w.Warp, aWarp, "aWarp (mouse capture 1)");
            Eq(w.Cursor, aCursor, "aCursor (mouse capture 2)");
            Eq(w.Draw, aDraw, "aDraw (draw capture 1)");
            Eq(w.Frame, aFrame, "aFrame (frame capture 2)");
            Eq(w.Freeze, aFreeze, "aFreeze (freeze capture 1)");
        }

        static void SigsCheckAll(SigsWorld w)
        {
            SigsCheckCore(w);
            Eq(SigsAllHave, SigsHave(), "optional features");
            Eq(w.FogTable, aFogTable, "aFogTable (haze capture 2)");
            Eq(w.Weather, aWeather, "aWeather");
            Eq(w.ResFlags, aResFlags, "aResFlags (res capture 1)");
            Eq(w.ResMode, aResMode, "aResMode (res capture 2)");
            Eq(SigsWorld.At("res"), aResCallback, "aResCallback (guest address of the res code)");
            Eq(w.ButtonAt, aResButton, "aResButton (where the radio button points to the res code)");
            Eq(w.Ratio, aRatio, "aRatio (aspect capture 2)");
            Eq(w.RatioRef, aRatioRef, "aRatioRef (ratioref capture 1)");
            Eq(w.Scale1, aScaleX, "aScaleX (scale capture 1)");
            Eq(w.Terrain, aTerrain, "aTerrain (terrain capture 1)");
            Eq(w.G3Width, aG3Width, "aG3Width (aspect capture 3)");
            Eq(w.Stereo, aStereo, "aStereo");
        }

        // a DOSBox-like interrupt table at Mem[at]: 256 vectors xxxx:offset, segment F000 (BIOS) where bios(i), 0070 elsewhere
        static void SigsIvt(FakeGame g, uint at, Func<int, bool> bios)
        {
            for (int i = 0; i < 256; i++)
            {
                g.PutShort(at + (uint)(4 * i), (short)(0x1000 + 8 * i));
                g.PutShort(at + (uint)(4 * i + 2), unchecked((short)(bios(i) ? 0xF000 : 0x0070)));
            }
        }

        // the world in a FakeGame, guest 0 = Mem[at], with an interrupt table of 256 BIOS vectors
        static void SigsLoad(FakeGame g, SigsWorld w, uint at)
        {
            g.Put(at, w.Image(0, g.Mem.Length - (int)at));
            SigsIvt(g, at, delegate(int i) { return true; });
        }

        // ================================================================== Latin1, Sig, Cap
        [Test] static void Sigs_Latin1MapsEveryByte()
        {
            byte[] all = new byte[256];
            for (int i = 0; i < 256; i++) all[i] = (byte)i;
            string s = Latin1(all);
            Eq(256, s.Length, "one char per byte");
            for (int i = 0; i < 256; i++)
                if (s[i] != (char)i) Fail("byte 0x" + i.ToString("X2") + " -> U+" + ((int)s[i]).ToString("X4") + " (expected U+00" + i.ToString("X2") + ")");
            Eq("", Latin1(new byte[0]), "no bytes");
        }

        // each byte value as a literal of a signature matches that byte and no other (regex-special, control, high bytes)
        [Test] static void Sigs_SigMatchesEachByteValueOnly()
        {
            // hay: "A c B \0" for every c; the match of "A b B" must be the 4 x b one, alone
            StringBuilder hay = new StringBuilder();
            for (int c = 0; c < 256; c++) hay.Append('A').Append((char)c).Append('B').Append('\0');
            string h = hay.ToString();
            for (int b = 0; b < 256; b++)
            {
                MatchCollection ms = Sig("41 " + b.ToString("x2") + " 42").Matches(h);
                string what = "signature 41 " + b.ToString("X2") + " 42";
                if (ms.Count != 1) Fail(what + ": " + ms.Count + " matches (expected 1)" + (ms.Count > 0 ? ", first at char " + ms[0].Index : ""));
                Eq(4 * b, ms[0].Index, what + ": match position");
                Eq(3, ms[0].Length, what + ": match length");
            }
        }

        [Test] static void Sigs_SigWholeByteRange()
        {
            byte[] all = new byte[256];
            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < 256; i++) { all[i] = (byte)i; hex.Append(i.ToString("x2")); }
            Regex r = Sig(hex.ToString());         // one token of 256 bytes
            Eq(1, r.GetGroupNumbers().Length, "no group (only group 0)");
            Match m = r.Match(Latin1(SigsCat(Bytes("0A0D"), all, Bytes("2E2E"))));
            Ok(m.Success, "the 256 bytes in a row are found");
            Eq(2, m.Index, "position");
            Eq(256, m.Length, "length");
            byte[] other = (byte[])all.Clone();
            other[255] = 0xFE;
            No(r.IsMatch(Latin1(other)), "last byte different: no match");
        }

        // the bytes that mean something in a regex are matched as themselves (Regex.Escape)
        [Test] static void Sigs_SigMetacharactersAreLiteral()
        {
            Ok(SigsIs("2e", "2e"), "2E '.' matches '.'");
            No(SigsIs("2e", "41"), "2E '.' is not 'any character'");
            Ok(SigsIs("41 2b", "41 2b"), "41 2B matches 'A+'");
            No(SigsIs("41 2b", "41 41"), "41 2B is not 'A one or more times'");
            No(SigsIs("41 2a 42", "42"), "41 2A 42 is not 'A*B'");
            No(SigsIs("41 3f 42", "42"), "41 3F 42 is not 'A?B'");
            Ok(SigsIs("41 3f 42", "41 3f 42"), "41 3F 42 matches 'A?B'");
            No(SigsIs("41 7c 42", "42"), "41 7C 42 is not 'A or B'");
            Ok(SigsIs("41 7c 42", "41 7c 42"), "41 7C 42 matches 'A|B'");
            Ok(SigsIs("5b", "5b"), "5B '[' alone is valid and matches '['");
            No(SigsIs("5b 41 42 5d", "41"), "5B 41 42 5D is not the class [AB]");
            Ok(SigsIs("5b 41 42 5d", "5b 41 42 5d"), "5B 41 42 5D matches '[AB]'");
            Eq(1, Sig("28 41 29").GetGroupNumbers().Length, "28 41 29: '(' ')' make no group");
            No(SigsIs("28 41 29", "41"), "28 41 29 is not the group (A)");
            Ok(SigsIs("28 41 29", "28 41 29"), "28 41 29 matches '(A)'");
            // the text "(....)" written as bytes is neither a capture nor a wildcard
            Eq(1, Sig("28 2e 2e 2e 2e 29").GetGroupNumbers().Length, "28 2E 2E 2E 2E 29: no group");
            No(SigsIs("28 2e 2e 2e 2e 29", "28 41 42 43 44 29"), "28 2E 2E 2E 2E 29 is not '(' + 4 any + ')'");
            Ok(SigsIs("28 2e 2e 2e 2e 29", "28 2e 2e 2e 2e 29"), "28 2E 2E 2E 2E 29 matches '(....)'");
            No(SigsIs("24", ""), "24 '$' is not 'end of text'");
            Ok(SigsIs("41 24 42", "41 24 42"), "41 24 42 matches 'A$B'");
            No(SigsIs("5e", "41"), "5E '^' is not 'start of text'");
            Ok(SigsIs("41 5e 42", "41 5e 42"), "41 5E 42 matches 'A^B'");
            No(SigsIs("5c 64", "35"), "5C 64 is not \\d");
            Ok(SigsIs("5c 64", "5c 64"), "5C 64 matches backslash + 'd'");
            No(SigsIs("41 7b 32 7d", "41 41"), "41 7B 32 7D is not 'A twice'");
            Ok(SigsIs("41 7b 32 7d", "41 7b 32 7d"), "41 7B 32 7D matches 'A{2}'");
            Ok(SigsIs("23 20 41 0a 0d 09", "23 20 41 0a 0d 09"), "'#', space, newline, CR, tab are literal");
            No(SigsIs("41 20 42", "41 42"), "a 20 byte is not ignored (space in the pattern)");
        }

        [Test] static void Sigs_SigWildcardIsAnyFourBytes()
        {
            Regex r = Sig("41 .... 42");
            Eq(1, r.GetGroupNumbers().Length, "\"....\" makes no group");
            foreach (string mid in new[] { "00000000", "0A0A0A0A", "0D0A0D0A", "FFEEDD80", "42424242", "41414141", "2E28295B" })
            {
                Match m = r.Match(Latin1(Bytes("00 41" + mid + "42 00")));
                Ok(m.Success, "41 " + mid + " 42 matches 41 .... 42 (any byte, newline included)");
                Eq(1, m.Index, "41 " + mid + " 42: position");
                Eq(6, m.Length, "41 " + mid + " 42: length");
            }
            No(r.IsMatch(Latin1(Bytes("41 0A0A0A 42"))), "3 bytes between: no match");
            No(r.IsMatch(Latin1(Bytes("41 0A0A0A0A0A 42"))), "5 bytes between: no match");
        }

        [Test] static void Sigs_SigCaptureIsFourBytes()
        {
            Regex r = Sig("41 (....) 42 (....)");
            Eq(3, r.GetGroupNumbers().Length, "two \"(....)\": groups 1 and 2");
            Match m = r.Match(Latin1(Bytes("00 41 0A0D00FF 42 78563412 00")));
            Ok(m.Success, "match");
            Eq(1, m.Index, "position");
            Eq(10, m.Length, "length");
            Eq(2, m.Groups[1].Index, "capture 1 position");
            Eq(4, m.Groups[1].Length, "capture 1 length");
            Eq(0xFF000D0Au, Cap(m, 1), "capture 1: newline, CR, 0 and FF bytes, little-endian");
            Eq(0x12345678u, Cap(m, 2), "capture 2");
            No(r.IsMatch(Latin1(Bytes("41 0A0D00 42 78563412"))), "3 bytes for a capture: no match");
        }

        [Test] static void Sigs_SigTokens()
        {
            string hay = Latin1(Bytes("00 8B15C1FA10 00"));
            foreach (string s in new[] { "8b15c1fa10", "8B15C1FA10", "8b 15 c1 fa 10", "8b15 c1fa10", "8b15  c1fa10", " 8b15 c1fa10 " })
            {
                Match m = Sig(s).Match(hay);
                Ok(m.Success, "\"" + s + "\" matches 8B 15 C1 FA 10");
                Eq(1, m.Index, "\"" + s + "\": position");
                Eq(5, m.Length, "\"" + s + "\": length");
            }
        }

        // a typo in a signature fails loudly (at the first attach), it never matches something else
        [Test] static void Sigs_SigMalformedThrows()
        {
            Throws<FormatException>(delegate { Sig("8b zz"); }, "non-hex token");
            Throws<FormatException>(delegate { Sig("8b15...."); }, "wildcard glued to hex bytes");
            Throws<ArgumentOutOfRangeException>(delegate { Sig("8b1"); }, "odd number of hex digits");
            Throws<ArgumentOutOfRangeException>(delegate { Sig("8b ( .... )"); }, "capture split by spaces");
        }

        [Test] static void Sigs_CapLittleEndian()
        {
            Regex r = Sig("(....)");
            string[] hex = { "00000000", "01000000", "78563412", "FFFFFFFF", "00000080", "0A0D2E28", "FF000000" };
            uint[] val = { 0, 1, 0x12345678, 0xFFFFFFFF, 0x80000000, 0x282E0D0A, 0xFF };
            for (int i = 0; i < hex.Length; i++)
            {
                Match m = r.Match(Latin1(Bytes(hex[i])));
                Ok(m.Success, hex[i] + ": match");
                Eq(val[i], Cap(m, 1), "Cap of bytes " + hex[i]);
            }
        }

        // ================================================================== SIGS, HAZE_TEXT
        [Test] static void Sigs_SignatureNames()
        {
            List<string> have = new List<string>(SIGS.Keys), want = new List<string>(SigsNames);
            have.Sort(StringComparer.Ordinal); want.Sort(StringComparer.Ordinal);
            EqList(want, have, "SIGS: the names ResolveAll and ScanRegion look up (no more, no less)");
        }

        // The other tests build their instances from SIGS itself: this copy catches an accidental edit of a signature (only
        // the real game can tell a good one). A deliberate change, checked in the game, is copied here too.
        [Test] static void Sigs_SignaturesPinned()
        {
            string[,] pin = {
                { "camera",   "8b15 (....) c1fa10 8d04d500000000 01d0 c1e002 01d0 05 (....) 668b15 (....) 668b401b 01d0 66a3 (....) 66a1 (....) 66a3 (....)" },
                { "haze",     "5231d2 8a82 (....) 3a82 (....) 740f 68 (....) 6a01 e8" },
                { "mouse",    "84d2 0f85 .... 803d (....) 00 7423 8b15 (....) a1 (....) c1fa10" },
                { "draw",     "8b15 (....) ff15 .... 30c9 880d ...." },
                { "frame",    "8b0424 8b15 (....) a3 (....) a1 ...." },
                { "freeze",   "f6400401 0f84 .... 803d (....) 00 0f85 .... c605 .... 01" },
                { "res",      "5352 8a15 (....) 8b1d (....) f6c201 7419 80e2fe" },
                { "weather",  "5352 31db 8b15 (....) 89d8 e8" },
                { "aspect",   "a1 (....) f72d (....) f73d (....) 3d00000100" },
                { "scale",    "c705 (....) 00000100 c705 (....) 00000100 c705 (....) 00000100 8b1d" },
                { "ratioref", "8b1d (....) 8b03 a3 (....) 8b1d (....) 0fb74308" },
                { "terrain",  "b8 (....) 83c222 31db 8915 (....)" },
                { "stereo",   "803d (....) 00 740d b87f000000 8b55fc 29d0" } };
            Eq(pin.GetLength(0), SIGS.Count, "number of signatures");
            for (int i = 0; i < pin.GetLength(0); i++)
            {
                string s;
                Ok(SIGS.TryGetValue(pin[i, 0], out s), "signature " + pin[i, 0]);
                Eq(pin[i, 1], s, "signature " + pin[i, 0]);
            }
        }

        // ResolveAll reads captures up to these numbers (Cap of a missing group would throw)
        [Test] static void Sigs_SignatureCaptureCounts()
        {
            string[] names = { "camera", "haze", "mouse", "draw", "frame", "freeze", "res", "weather", "aspect", "scale", "ratioref", "terrain", "stereo" };
            int[] caps = { 6, 3, 3, 1, 2, 1, 2, 1, 3, 3, 3, 2, 1 };
            for (int i = 0; i < names.Length; i++)
            {
                Eq(caps[i], SigsCapCount(SIGS[names[i]]), names[i] + ": \"(....)\" in the signature");
                Eq(caps[i] + 1, Sig(SIGS[names[i]]).GetGroupNumbers().Length, names[i] + ": groups of its regex (+ group 0)");
            }
        }

        [Test] static void Sigs_EverySignatureMatchesAnInstance()
        {
            foreach (KeyValuePair<string, string> e in SIGS)
            {
                Regex r = Sig(e.Value);
                int n = SigsCapCount(e.Value);
                uint[] caps = new uint[n];
                for (int i = 0; i < n; i++) caps[i] = 0x0A0DFF00u | (uint)(0x11 * (i + 1));   // newline, CR, FF bytes, all different
                foreach (byte fill in new byte[] { 0x00, 0x0A, 0x2E, 0xCC, 0xFF })
                {
                    string what = e.Key + " (wildcards " + fill.ToString("X2") + ")";
                    byte[] inst = SigsBytes(e.Value, caps, fill);
                    Match m = r.Match(Latin1(SigsCat(Bytes("0A0D2E28"), inst, Bytes("5B5C0A"))));
                    Ok(m.Success, what + ": no match");
                    Eq(4, m.Index, what + ": position");
                    Eq(inst.Length, m.Length, what + ": length");
                    for (int i = 0; i < n; i++) Eq(caps[i], Cap(m, i + 1), what + ": capture " + (i + 1));
                    No(m.NextMatch().Success, what + ": a second match");
                }
            }
        }

        // every literal byte of every signature is required, captures and wildcards take anything
        [Test] static void Sigs_EveryLiteralByteCounts()
        {
            foreach (KeyValuePair<string, string> e in SIGS)
            {
                Regex r = Sig(e.Value);
                int n = SigsCapCount(e.Value);
                byte[] inst = SigsBytes(e.Value, new uint[n], 0xCC);
                string kinds = SigsKinds(e.Value);
                Eq(inst.Length, kinds.Length, e.Key + ": kinds");
                Ok(r.IsMatch(Latin1(inst)), e.Key + ": the instance itself");
                for (int i = 0; i < inst.Length; i++)
                    foreach (byte flip in new byte[] { 0x01, 0x80 })
                    {
                        byte[] x = (byte[])inst.Clone();
                        x[i] ^= flip;
                        Eq(kinds[i] != 'L', r.IsMatch(Latin1(x)), e.Key + ": byte " + i + " (" + kinds[i] + ") ^ " + flip.ToString("X2") + " -> match");
                    }
            }
        }

        [Test] static void Sigs_HazeSignatureAndText()
        {
            Eq("Memory trash: hazeRadius", Encoding.ASCII.GetString(HAZE_TEXT), "HAZE_TEXT");
            Eq(24, HAZE_TEXT.Length, "HAZE_TEXT length (what GameStillLoaded compares)");
            // checkHazingOffset, written by hand: push edx / xor edx,edx / mov al,[edx+copy] / cmp al,[edx+table] / je +15 /
            // push text / push 1 / call
            byte[] code = Bytes("52 31D2 8A82 00941000 3A82 00901000 740F 68 00A01000 6A01 E8");
            Match m = Best("haze", Latin1(SigsCat(new byte[3], code, Bytes("FFFFFFFF"))));
            Ok(m != null, "haze found");
            Eq(3, m.Index, "position");
            Eq(0x109400u, Cap(m, 1), "capture 1: the fog table's check copy");
            Eq(0x109000u, Cap(m, 2), "capture 2: the fog table");
            Eq(0x10A000u, Cap(m, 3), "capture 3: the pushed text");
        }

        // ================================================================== Best
        [Test] static void Sigs_BestPrefersRelocatedCode()
        {
            byte[] raw = SigsInst("draw", 0x2A000), rel = SigsInst("draw", 0x1A2000), pad = new byte[16];
            Match m = Best("draw", Latin1(SigsCat(pad, raw, pad, rel, pad)));
            Ok(m != null, "raw copy first: found");
            Eq(0x1A2000u, Cap(m, 1), "raw copy first: the relocated code is chosen");
            Eq(32 + raw.Length, m.Index, "raw copy first: position");
            m = Best("draw", Latin1(SigsCat(pad, rel, pad, raw, pad)));
            Eq(0x1A2000u, Cap(m, 1), "relocated first: the relocated code is chosen");
            Eq(16, m.Index, "relocated first: position");
        }

        // the smallest capture of each match decides (a raw copy can have one big capture), the highest one wins
        [Test] static void Sigs_BestSmallestCaptureDecides()
        {
            byte[] a = SigsInst("frame", 0x900000, 0x10), b = SigsInst("frame", 0x180000, 0x180004), c = SigsInst("frame", 0xFFFFF, 0xFFFFFFF0);
            byte[] pad = new byte[8];
            Match m = Best("frame", Latin1(SigsCat(a, pad, b, pad, c)));
            Ok(m != null, "found");
            Eq(0x180000u, Cap(m, 1), "chosen: smallest capture 0x180000 (others: 0x10 and 0xFFFFF)");
            Eq(a.Length + 8, m.Index, "chosen: the middle one");
            m = Best("frame", Latin1(SigsCat(c, pad, a)));
            Eq(0xFFFFFu, Cap(m, 1), "0xFFFFF beats 0x10");
        }

        [Test] static void Sigs_BestTieKeepsTheFirst()
        {
            byte[] a = SigsInst("stereo", 0x150000), b = SigsInst("stereo", 0x150000);
            Match m = Best("stereo", Latin1(SigsCat(new byte[5], a, new byte[5], b)));
            Eq(5, m.Index, "same capture: the first one in memory");
            // same smallest capture, the others different: still the first
            byte[] c = SigsInst("aspect", 0x200000, 0x200004, 0x200008), d = SigsInst("aspect", 0x200000, 0x900000, 0x900000);
            m = Best("aspect", Latin1(SigsCat(c, new byte[5], d)));
            Eq(0, m.Index, "same smallest capture: the first one");
            Eq(0x200004u, Cap(m, 2), "the first one's captures");
        }

        [Test] static void Sigs_BestAbsent()
        {
            Ok(Best("draw", "") == null, "empty memory: null");
            Ok(Best("draw", Latin1(new byte[4096])) == null, "zeros: null");
            byte[] inst = SigsInst("draw", 0x150000);
            byte[] cut = new byte[inst.Length - 1];
            Array.Copy(inst, cut, cut.Length);
            Ok(Best("draw", Latin1(cut)) == null, "an instance cut by the end of the memory: null");
            Ok(Best("camera", Latin1(SigsInst("draw", 0x150000))) == null, "another signature's code: null");
            Match m = Best("draw", Latin1(SigsInst("draw", 0)));
            Ok(m != null, "a single match whose captures are all 0 is still returned");
            Eq(0u, Cap(m, 1), "its capture");
            Throws<KeyNotFoundException>(delegate { Best("nope", ""); }, "unknown signature name");
        }

        // ================================================================== IndexOf
        [Test] static void Sigs_IndexOfFindsTheFirstOccurrence()
        {
            byte[] hay = Bytes("4D656D 00FF80 4D656D 7F");
            Eq(0, IndexOf(hay, Bytes("4D656D"), 0), "at 0");
            Eq(3, IndexOf(hay, Bytes("00FF80"), 0), "in the middle, bytes >= 0x80");
            Eq(8, IndexOf(hay, Bytes("6D7F"), 0), "at the last possible position");
            Eq(9, IndexOf(hay, Bytes("7F"), 0), "one byte, the last one");
            Eq(-1, IndexOf(hay, Bytes("FF81"), 0), "absent");
            Eq(-1, IndexOf(hay, Bytes("7F00"), 0), "its first byte only, at the very end");
            Eq(0, IndexOf(hay, (byte[])hay.Clone(), 0), "the whole hay");
        }

        [Test] static void Sigs_IndexOfFrom()
        {
            byte[] hay = Bytes("4D656D 00FF80 4D656D 7F");
            Eq(6, IndexOf(hay, Bytes("4D656D"), 1), "from 1: the second occurrence");
            Eq(6, IndexOf(hay, Bytes("4D656D"), 6), "from = an occurrence: that one");
            Eq(-1, IndexOf(hay, Bytes("4D656D"), 7), "past the last occurrence");
            Eq(-1, IndexOf(hay, Bytes("7F"), hay.Length), "from = length");
            Eq(-1, IndexOf(hay, Bytes("7F"), hay.Length + 5), "from past the end: -1, no exception");
        }

        [Test] static void Sigs_IndexOfNeedleLongerThanHay()
        {
            Eq(-1, IndexOf(Bytes("4142"), Bytes("414243"), 0), "needle longer than the hay");
            Eq(-1, IndexOf(new byte[0], Bytes("41"), 0), "empty hay");
            Eq(-1, IndexOf(Bytes("4D"), HAZE_TEXT, 0), "HAZE_TEXT in 1 byte");
            Eq(-1, IndexOf(SigsCat(new byte[5], Slice(HAZE_TEXT, 0, 23)), HAZE_TEXT, 0), "HAZE_TEXT cut by the end of the memory");
            Eq(5, IndexOf(SigsCat(new byte[5], HAZE_TEXT), HAZE_TEXT, 0), "HAZE_TEXT ending the memory");
        }

        [Test] static void Sigs_IndexOfOverlappingCandidates()
        {
            Eq(1, IndexOf(Bytes("414141 42"), Bytes("414142"), 0), "a partial match at 0 does not hide the one at 1");
            Eq(2, IndexOf(Bytes("41424142 4143"), Bytes("41424143"), 0), "restarts right after a failed candidate");
            // the loops of ScanRegion / ResolveAll (i = IndexOf(.., i + 1)) visit every occurrence, overlapping ones too
            byte[] hay = Bytes("41414141");
            List<int> at = new List<int>();
            for (int i = IndexOf(hay, Bytes("4141"), 0); i >= 0; i = IndexOf(hay, Bytes("4141"), i + 1)) at.Add(i);
            EqList(new[] { 0, 1, 2 }, at, "41 41 in 41 41 41 41");
            byte[] two = SigsCat(HAZE_TEXT, new byte[3], HAZE_TEXT);
            at.Clear();
            for (int i = IndexOf(two, HAZE_TEXT, 0); i >= 0; i = IndexOf(two, HAZE_TEXT, i + 1)) at.Add(i);
            EqList(new[] { 0, 27 }, at, "HAZE_TEXT twice");
        }

        // ================================================================== Inside
        [Test] static void Sigs_InsideBounds()
        {
            guestBase = SigsWorld.HOST; regStart = SigsWorld.HOST + 0x100000; regSize = 0x10000;   // guest 0x100000-0x10FFFF
            Ok(Inside(0x100000, 1), "first byte");
            Ok(Inside(0x100000, 0x10000), "the whole region");
            No(Inside(0x100000, 0x10001), "the whole region + 1");
            No(Inside(0xFFFFF, 1), "the byte before");
            No(Inside(0xFFFFF, 2), "starting before, ending inside");
            Ok(Inside(0x10FFFF, 1), "last byte");
            No(Inside(0x10FFFF, 2), "crossing the end");
            No(Inside(0x110000, 1), "the byte after");
            Ok(Inside(0x10FFC0, 64), "64 bytes ending at the end");
            No(Inside(0x10FFC1, 64), "64 bytes, one past the end");
            No(Inside(0, 4), "guest 0, below the region");
            No(Inside(0xFFFFFFFF, 1), "top of the guest space (no 32-bit wrap)");
            No(Inside(0xFFFFFFC0, 64), "64 bytes at the top of the guest space");
            // the usual case: the region starts at guest 0
            regStart = guestBase; regSize = 0x480000;
            Ok(Inside(0, 1024), "region at guest 0: the interrupt table");
            Ok(Inside(0x47FFFC, 4), "region at guest 0: last 4 bytes");
            No(Inside(0x47FFFD, 4), "region at guest 0: past the end");
            // guest 0 after the start of the region
            guestBase = regStart + 0x1000;
            Ok(Inside(0, 4), "guest 0 inside the region");
            Ok(Inside(0x47EFFC, 4), "guest 0 at +0x1000: last 4 bytes");
            No(Inside(0x47EFFD, 4), "guest 0 at +0x1000: past the end");
            No(Inside(0xFFFFFFFF, 1), "guest 0 at +0x1000: 0xFFFFFFFF is 4 GB up, not the byte before guest 0");
            No(Inside(0xFFFFF000, 4), "guest 0 at +0x1000: 0xFFFFF000 is not guest 0 - 0x1000");
        }

        // ================================================================== ResolveAll
        [Test] static void Sigs_ResolveAllReadsEveryAddress()
        {
            SigsWorld w = new SigsWorld();
            Ok(SigsResolve(w), "ResolveAll on consistent memory");
            SigsCheckAll(w);
        }

        // unrelocated copies of the code (an exe image left in memory) before and after the real code are passed over
        [Test] static void Sigs_ResolveAllPrefersRelocatedCopies()
        {
            SigsWorld w = new SigsWorld();
            w.RawCopies = true;
            Ok(SigsResolve(w), "ResolveAll with raw copies around");
            SigsCheckAll(w);
        }

        [Test] static void Sigs_ResolveAllRequiredSignatures()
        {
            foreach (string n in new[] { "camera", "mouse", "draw", "frame", "freeze" })
            {
                SigsWorld w = new SigsWorld();
                w.Omit.Add(n);
                No(SigsResolve(w), "ResolveAll without the " + n + " signature");
            }
            Ok(SigsResolve(new SigsWorld()), "ResolveAll with all of them");
        }

        // a missing optional signature turns its feature off, nothing else
        [Test] static void Sigs_ResolveAllOptionalSignatures()
        {
            string[] names = { "weather", "res", "aspect", "scale", "ratioref", "terrain", "stereo" };
            string[] have = {
                "fog=False 400=True wide=True stereo=True detail=True",
                "fog=True 400=False wide=True stereo=True detail=True",
                "fog=True 400=True wide=False stereo=True detail=True",
                "fog=True 400=True wide=False stereo=True detail=True",
                "fog=True 400=True wide=False stereo=True detail=True",
                "fog=True 400=True wide=False stereo=True detail=False",
                "fog=True 400=True wide=True stereo=False detail=True" };
            for (int i = 0; i < names.Length; i++)
            {
                SigsWorld w = new SigsWorld();
                w.Omit.Add(names[i]);
                Ok(SigsResolve(w), "ResolveAll without " + names[i]);
                Eq(have[i], SigsHave(), "features without " + names[i]);
                SigsCheckCore(w);
                switch (names[i])
                {
                    case "weather": Eq(0u, aFogTable, "no weather: aFogTable"); Eq(0u, aWeather, "no weather: aWeather"); break;
                    case "res":
                        Eq(0u, aResFlags, "no res: aResFlags"); Eq(0u, aResMode, "no res: aResMode");
                        Eq(0u, aResCallback, "no res: aResCallback"); Eq(0u, aResButton, "no res: aResButton"); break;
                    case "terrain": Eq(0u, aTerrain, "no terrain: aTerrain"); Eq(0u, aRatio, "no terrain: aRatio"); break;
                    case "stereo": Eq(0u, aStereo, "no stereo: aStereo"); break;
                    default: Eq(0u, aRatio, "no " + names[i] + ": aRatio"); Eq(w.Terrain, aTerrain, "no " + names[i] + ": aTerrain (terrain detail)"); break;
                }
            }
        }

        // the "cursor frozen" flag is the byte before the "push cursor" flag in every known version
        [Test] static void Sigs_ResolveAllFreezeNextToWarp()
        {
            uint[] bad = { 0x10B002, 0x10B001, 0x10AFFF, 0x10B100 };
            foreach (uint f in bad)
            {
                SigsWorld w = new SigsWorld();
                w.Freeze = f;
                No(SigsResolve(w), "aFreeze 0x" + f.ToString("X") + " with aWarp 0x10B001");
            }
            SigsWorld moved = new SigsWorld();
            moved.Warp = 0x10B501; moved.Freeze = 0x10B500;
            Ok(SigsResolve(moved), "both moved together");
            Eq(0x10B501u, aWarp, "aWarp");
            Eq(0x10B500u, aFreeze, "aFreeze");
        }

        // the addresses every attach needs must have 64 bytes inside the region
        [Test] static void Sigs_ResolveAllCoreAddressesInside()
        {
            string[] names = { "aPlayerId", "aMaster", "aCamHeading", "aPitch", "aWarp (+ aFreeze before it)", "aCursor", "aDraw", "aFrame" };
            Action<SigsWorld, uint>[] set = {
                (w, a) => w.PlayerId = a, (w, a) => w.Master = a, (w, a) => w.CamHeading = a, (w, a) => w.HeadPitch = a,
                (w, a) => { w.Warp = a; w.Freeze = a - 1; },
                (w, a) => w.Cursor = a, (w, a) => w.Draw = a, (w, a) => w.Frame = a };
            for (int i = 0; i < names.Length; i++)
            {
                SigsWorld w = new SigsWorld();
                set[i](w, SigsWorld.END - 64);
                Ok(SigsResolve(w), names[i] + " at the end - 64");
                w = new SigsWorld();
                set[i](w, SigsWorld.END - 63);
                No(SigsResolve(w), names[i] + " at the end - 63");
                w = new SigsWorld();
                set[i](w, SigsWorld.ORIGIN - 4);
                No(SigsResolve(w), names[i] + " below the region");
            }
            // aFreeze checked on its own: the first byte of the region is fine, the byte before is not
            SigsWorld f = new SigsWorld();
            f.Freeze = SigsWorld.ORIGIN; f.Warp = SigsWorld.ORIGIN + 1;
            Ok(SigsResolve(f), "aFreeze at the first byte of the region");
            f.Freeze = SigsWorld.ORIGIN - 1; f.Warp = SigsWorld.ORIGIN;
            No(SigsResolve(f), "aFreeze the byte before the region (aWarp inside)");
        }

        // the captures ResolveAll does not keep are not checked
        [Test] static void Sigs_ResolveAllUncheckedCaptures()
        {
            SigsWorld w = new SigsWorld();
            w.HeadYaw = 0x7FFFFFF0; w.CamPitch = 5; w.CursorY = 0xFFFFFFFF; w.FrameW = 0x10; w.AspH = 0xFFFFFFF0; w.Rr3 = 0;
            Ok(SigsResolve(w), "camera 3 and 6, mouse 3, frame 1, aspect 1, ratioref 3 anywhere");
            SigsCheckAll(w);
        }

        // the fog table and its check copy right after it (the game compares them): 2 x 1024 bytes
        [Test] static void Sigs_ResolveAllFogCopyFollowsTable()
        {
            foreach (uint copy in new uint[] { 0x109000 + 1023, 0x109000 + 1025, 0x109000 - 1024, 0x109000 })
            {
                SigsWorld w = new SigsWorld();
                w.FogCopy = copy;
                Ok(SigsResolve(w), "copy at 0x" + copy.ToString("X") + ": still attaches");
                Eq("fog=False 400=True wide=True stereo=True detail=True", SigsHave(), "copy at 0x" + copy.ToString("X") + " (table 0x109000)");
                Eq(0u, aFogTable, "copy at 0x" + copy.ToString("X") + ": aFogTable not set");
                Eq(0u, aWeather, "copy at 0x" + copy.ToString("X") + ": aWeather not set");
            }
        }

        [Test] static void Sigs_ResolveAllFogBounds()
        {
            SigsWorld w = new SigsWorld();
            w.FogTable = SigsWorld.END - 2048; w.FogCopy = SigsWorld.END - 1024;
            Ok(SigsResolve(w), "table at the end - 2048");
            Ok(haveFog, "table + copy ending at the end of the region");
            Eq(SigsWorld.END - 2048, aFogTable, "aFogTable");
            w.FogTable = SigsWorld.END - 2047; w.FogCopy = SigsWorld.END - 1023;
            SigsResolve(w);
            No(haveFog, "table at the end - 2047 (its copy crosses the end)");
            w = new SigsWorld();
            w.FogTable = SigsWorld.ORIGIN - 0x100; w.FogCopy = w.FogTable + 1024;
            SigsResolve(w);
            No(haveFog, "table starting below the region");
            w = new SigsWorld();
            w.Weather = SigsWorld.END - 4;
            SigsResolve(w);
            Ok(haveFog, "weather at the end - 4");
            Eq(SigsWorld.END - 4, aWeather, "aWeather");
            w.Weather = SigsWorld.END - 3;
            SigsResolve(w);
            No(haveFog, "weather at the end - 3");
        }

        // the 320x400 radio button: its data points to the res code, state byte (1 or 2) 8 bytes further; one exactly
        [Test] static void Sigs_ResolveAllResButtonCount()
        {
            uint ptrTo = SigsWorld.At("res");
            SigsWorld w = new SigsWorld();
            w.ButtonAt = 0;
            Ok(SigsResolve(w), "no button: still attaches");
            Ok(have400, "no button: 320x400 still available");
            Eq(ptrTo, aResCallback, "no button: aResCallback");
            Eq(0u, aResButton, "no button: aResButton");

            w = new SigsWorld();
            w.Add(0x10C100, BitConverter.GetBytes(ptrTo)); w.Add(0x10C108, new byte[] { 2 });
            SigsResolve(w);
            Ok(have400, "two buttons: 320x400 available");
            Eq(0u, aResButton, "two buttons: none kept");

            w = new SigsWorld();
            w.ButtonState = 2;
            SigsResolve(w);
            Eq(w.ButtonAt, aResButton, "state 2 (400 lines chosen)");
        }

        [Test] static void Sigs_ResolveAllResButtonFilters()
        {
            uint ptrTo = SigsWorld.At("res");
            // decoys: a pointer before the code, pointers with a state byte other than 1 / 2: the real button is kept
            SigsWorld w = new SigsWorld();
            w.Add(0x100800, BitConverter.GetBytes(ptrTo)); w.Add(0x100808, new byte[] { 1 });
            w.Add(0x10C200, BitConverter.GetBytes(ptrTo)); w.Add(0x10C208, new byte[] { 0 });
            w.Add(0x10C300, BitConverter.GetBytes(ptrTo)); w.Add(0x10C308, new byte[] { 3 });
            Ok(SigsResolve(w), "with decoys");
            Eq(w.ButtonAt, aResButton, "decoys ignored");

            w = new SigsWorld();
            w.ButtonState = 0;
            SigsResolve(w);
            Eq(0u, aResButton, "the only pointer has state 0");

            // the end of the region: the pointer's state byte must not be the last byte (i + 9 < length)
            w = new SigsWorld();
            w.ButtonAt = 0;
            w.Add(SigsWorld.END - 10, BitConverter.GetBytes(ptrTo)); w.Add(SigsWorld.END - 2, new byte[] { 1 });
            SigsResolve(w);
            Eq(SigsWorld.END - 10, aResButton, "pointer 10 bytes before the end");
            w = new SigsWorld();
            w.ButtonAt = 0;
            w.Add(SigsWorld.END - 9, BitConverter.GetBytes(ptrTo)); w.Add(SigsWorld.END - 1, new byte[] { 1 });
            SigsResolve(w);
            Eq(0u, aResButton, "pointer 9 bytes before the end (state = last byte): not looked at");
        }

        [Test] static void Sigs_ResolveAllResBounds()
        {
            SigsWorld w = new SigsWorld();
            w.ResFlags = SigsWorld.END - 1;
            SigsResolve(w);
            Ok(have400, "state byte = last byte of the region");
            Eq(SigsWorld.END - 1, aResFlags, "aResFlags");
            w.ResFlags = SigsWorld.END;
            SigsResolve(w);
            No(have400, "state byte past the end");
            Eq(0u, aResButton, "no 320x400: aResButton not set");
            w = new SigsWorld();
            w.ResMode = SigsWorld.END - 4;
            SigsResolve(w);
            Ok(have400, "mode at the end - 4");
            w.ResMode = SigsWorld.END - 3;
            SigsResolve(w);
            No(have400, "mode at the end - 3");
            w = new SigsWorld();
            w.ResFlags = SigsWorld.ORIGIN - 1;
            SigsResolve(w);
            No(have400, "state byte below the region");
        }

        // widescreen: the pixel ratio variable seen by both aspect and ratioref, 3 scale words in a row, terrain canvas at +0x1C
        [Test] static void Sigs_ResolveAllWideConsistency()
        {
            SigsWorld w = new SigsWorld();
            w.RrRatio = w.Ratio + 4;
            Ok(SigsResolve(w), "other pixel ratio variable: still attaches");
            No(haveWide, "ratioref's ratio != aspect's");
            Ok(haveDetail, "ratio mismatch: terrain detail still there");
            Eq(0u, aRatio, "no widescreen: aRatio not set");
            Eq(0u, aScaleX, "no widescreen: aScaleX not set");

            w = new SigsWorld();
            w.Scale2 = w.Scale1 + 8;
            SigsResolve(w);
            No(haveWide, "scale word 2 not at +4");
            w = new SigsWorld();
            w.Scale3 = w.Scale1 + 12;
            SigsResolve(w);
            No(haveWide, "scale word 3 not at +8");

            w = new SigsWorld();
            w.TerrainCanvas = w.Terrain + 0x18;
            SigsResolve(w);
            Eq("fog=True 400=True wide=False stereo=True detail=False", SigsHave(), "terrain canvas not at +0x1C");
            Eq(0u, aTerrain, "terrain layout unknown: aTerrain not set");
        }

        [Test] static void Sigs_ResolveAllWideBounds()
        {
            string[] names = { "aRatio", "aRatioRef", "aScaleX", "aG3Width" };
            Action<SigsWorld, uint>[] set = {
                (w, a) => { w.Ratio = a; w.RrRatio = a; }, (w, a) => w.RatioRef = a,
                (w, a) => { w.Scale1 = a; w.Scale2 = a + 4; w.Scale3 = a + 8; }, (w, a) => w.G3Width = a };
            for (int i = 0; i < names.Length; i++)
            {
                SigsWorld w = new SigsWorld();
                set[i](w, SigsWorld.END - 4);
                Ok(SigsResolve(w), names[i] + " at the end - 4: attaches");
                Ok(haveWide, names[i] + " at the end - 4");
                w = new SigsWorld();
                set[i](w, SigsWorld.END - 3);
                Ok(SigsResolve(w), names[i] + " at the end - 3: attaches");
                No(haveWide, names[i] + " at the end - 3");
                Ok(haveDetail, names[i] + " at the end - 3: terrain detail still there");
            }
            // only the first scale word is checked against the region
            SigsWorld s = new SigsWorld();
            s.Scale1 = SigsWorld.END - 4; s.Scale2 = SigsWorld.END; s.Scale3 = SigsWorld.END + 4;
            SigsResolve(s);
            Ok(haveWide, "scale words 2 and 3 past the end");
            Eq(SigsWorld.END - 4, aScaleX, "aScaleX");
        }

        // the terrain camera: 0x20 bytes for widescreen, 0xA8 (the rings) for terrain detail
        [Test] static void Sigs_ResolveAllTerrainBounds()
        {
            uint[] at = { SigsWorld.END - 0xA8, SigsWorld.END - 0xA7, SigsWorld.END - 0x20, SigsWorld.END - 0x1F, SigsWorld.ORIGIN - 0x20 };
            string[] have = {
                "fog=True 400=True wide=True stereo=True detail=True",
                "fog=True 400=True wide=True stereo=True detail=False",
                "fog=True 400=True wide=True stereo=True detail=False",
                "fog=True 400=True wide=False stereo=True detail=False",
                "fog=True 400=True wide=False stereo=True detail=False" };
            for (int i = 0; i < at.Length; i++)
            {
                SigsWorld w = new SigsWorld();
                w.Terrain = at[i]; w.TerrainCanvas = at[i] + 0x1C;
                Ok(SigsResolve(w), "terrain at 0x" + at[i].ToString("X") + ": attaches");
                Eq(have[i], SigsHave(), "terrain camera at 0x" + at[i].ToString("X") + " (region end 0x110000)");
                if (haveDetail || haveWide) Eq(at[i], aTerrain, "aTerrain at 0x" + at[i].ToString("X"));
            }
        }

        [Test] static void Sigs_ResolveAllStereo()
        {
            SigsWorld w = new SigsWorld();
            w.Stereo = SigsWorld.END - 1;
            SigsResolve(w);
            Ok(haveStereo, "stereo byte = last byte of the region");
            Eq(SigsWorld.END - 1, aStereo, "aStereo");
            w.Stereo = SigsWorld.END;
            SigsResolve(w);
            No(haveStereo, "stereo byte past the end");
            Eq(0u, aStereo, "aStereo not set");
            w.Stereo = SigsWorld.ORIGIN - 1;
            SigsResolve(w);
            No(haveStereo, "stereo byte below the region");
            Eq(SigsAllHave.Replace("stereo=True", "stereo=False"), SigsHave(), "the rest is there");
        }

        // ================================================================== PlausibleIvt, ScanRegion (made-up DOSBox memory)
        [Test] static void Sigs_PlausibleIvt()
        {
            using (FakeGame g = new FakeGame(0x2000))
            {
                long host = guestBase;
                No(PlausibleIvt(host), "zeros: not an interrupt table");
                SigsIvt(g, 0, i => i < 32);
                Ok(PlausibleIvt(host), "32 BIOS vectors F000:xxxx");
                SigsIvt(g, 0, i => i < 31);
                No(PlausibleIvt(host), "31 BIOS vectors");
                SigsIvt(g, 0, i => i >= 224);
                Ok(PlausibleIvt(host), "32 BIOS vectors at the end of the table");
                SigsIvt(g, 0, i => i % 2 == 0);
                Ok(PlausibleIvt(host), "128 BIOS vectors, one in two");
                for (int i = 0; i < 256; i++) { g.PutShort((uint)(4 * i), unchecked((short)0xF000)); g.PutShort((uint)(4 * i + 2), 0x0070); }
                No(PlausibleIvt(host), "F000 as offsets, not segments");
                SigsIvt(g, 0x1000, i => i < 32);
                Ok(PlausibleIvt(host + 0x1000), "a table at guest 0x1000");
                No(PlausibleIvt(host + 0x1002), "2 bytes off: offsets read as segments");
            }
        }

        [Test] static void Sigs_ScanRegionFindsTheGame()
        {
            using (FakeGame g = new FakeGame(0x110000))
            {
                SigsWorld w = new SigsWorld();
                SigsLoad(g, w, 0);
                long host = guestBase;
                byte[] before = (byte[])g.Mem.Clone();
                SigsReset();
                Ok(ScanRegion(host, g.Mem.Length), "ScanRegion finds the made-up game");
                Eq(host, guestBase, "guestBase: guest 0 = the first byte");
                Eq(host, regStart, "regStart");
                Eq((long)g.Mem.Length, regSize, "regSize");
                SigsCheckAll(w);
                EqBytes(before, g.Mem, "the scan writes nothing");
            }
        }

        // the haze code of a raw exe copy (text address below 1 MB) is passed over, even when the base it gives looks good
        [Test] static void Sigs_ScanRegionSkipsRawExeCopy()
        {
            using (FakeGame g = new FakeGame(0x120000))
            {
                SigsWorld w = new SigsWorld();
                byte[] raw = SigsInst("haze", 0xF9400, 0xF9000, 0xFA000);
                w.Add(0x100400, raw);                              // before the real haze code
                SigsLoad(g, w, 0);
                SigsIvt(g, 0x10000, i => true);                    // where the raw copy would put guest 0 (0x10A000 - 0xFA000)
                long host = guestBase;
                Ok(ScanRegion(host, g.Mem.Length), "found");
                Eq(host, guestBase, "guest 0 from the relocated haze code");
                Eq(w.Text, aHazeText, "aHazeText");
                Eq(w.FogTable, aFogTable, "aFogTable from the relocated haze code");
                // what the skip avoids: from the raw copy, that wrong base would have been taken
                Match m = Sig(SIGS["haze"]).Match(Latin1(g.Mem));
                Eq(0xFA000u, Cap(m, 3), "the first haze match is the raw copy");
                guestBase = host + 0x10000;
                Ok(ResolveAll(g.Mem, Latin1(g.Mem), m), "the wrong base would resolve");
            }
        }

        [Test] static void Sigs_ScanRegionTextAddressThreshold()
        {
            using (FakeGame g = new FakeGame(0x110000))
            {
                long host = guestBase;
                SigsWorld w = new SigsWorld();
                w.Text = 0x100000;
                SigsLoad(g, w, 0);
                Ok(ScanRegion(host, g.Mem.Length), "text at guest 0x100000 (1 MB): relocated");
                Eq(0x100000u, aHazeText, "aHazeText");
                Array.Clear(g.Mem, 0, g.Mem.Length);
                w.Text = 0xFFFE0;
                SigsLoad(g, w, 0);
                No(ScanRegion(host, g.Mem.Length), "text at guest 0xFFFE0 (below 1 MB): taken for a raw copy");
            }
        }

        [Test] static void Sigs_ScanRegionImplausibleIvt()
        {
            using (FakeGame g = new FakeGame(0x110000))
            {
                long host = guestBase;
                SigsLoad(g, new SigsWorld(), 0);
                SigsIvt(g, 0, i => i < 31);
                No(ScanRegion(host, g.Mem.Length), "31 BIOS vectors at guest 0");
                Array.Clear(g.Mem, 0, 1024);
                No(ScanRegion(host, g.Mem.Length), "no interrupt table at guest 0");
                SigsIvt(g, 0, i => i < 32);
                Ok(ScanRegion(host, g.Mem.Length), "32 BIOS vectors at guest 0");
            }
        }

        // DOSBox's memory region may start after guest 0 (the interrupt table read before it)
        [Test] static void Sigs_ScanRegionStartsAfterGuestZero()
        {
            using (FakeGame g = new FakeGame(0x110000))
            {
                SigsWorld w = new SigsWorld();
                SigsLoad(g, w, 0);
                long host = guestBase;
                SigsReset();
                Ok(ScanRegion(host + 0x1000, g.Mem.Length - 0x1000), "region from guest 0x1000");
                Eq(host, guestBase, "guestBase");
                Eq(host + 0x1000, regStart, "regStart");
                Eq((long)(g.Mem.Length - 0x1000), regSize, "regSize");
                SigsCheckAll(w);
            }
        }

        // guest 0 is looked for up to 1 MB before the region
        [Test] static void Sigs_ScanRegionBaseWithin1MB()
        {
            using (FakeGame g = new FakeGame(0x110000))
            {
                SigsWorld w = new SigsWorld();
                SigsLoad(g, w, 0);
                long host = guestBase;
                Ok(ScanRegion(host + 0x100000, g.Mem.Length - 0x100000), "region from guest 0x100000 (guest 0 1 MB before)");
                Eq(host, guestBase, "guestBase");
                SigsCheckAll(w);
                guestBase = host;
                No(ScanRegion(host + 0x100001, g.Mem.Length - 0x100001), "region from guest 0x100001 (guest 0 more than 1 MB before)");
            }
        }

        // another copy of the text, earlier in memory, gives a wrong guest 0 without an interrupt table: passed over
        [Test] static void Sigs_ScanRegionPassesOverImplausibleBase()
        {
            using (FakeGame g = new FakeGame(0x112000))
            {
                SigsWorld w = new SigsWorld();
                w.Add(0x108800, HAZE_TEXT);                       // -> guest 0 at Mem[0x2000 + 0x108800 - 0x10A000] = Mem[0x800]
                SigsLoad(g, w, 0x2000);                            // guest 0 = Mem[0x2000]
                long mem0 = guestBase;
                Ok(ScanRegion(mem0, g.Mem.Length), "found");
                Eq(mem0 + 0x2000, guestBase, "guest 0 = Mem[0x2000]");
                Eq(mem0, regStart, "regStart");
                SigsCheckAll(w);
            }
        }

        [Test] static void Sigs_ScanRegionResolveFails()
        {
            using (FakeGame g = new FakeGame(0x110000))
            {
                long host = guestBase;
                SigsWorld w = new SigsWorld();
                w.Omit.Add("camera");
                SigsLoad(g, w, 0);
                No(ScanRegion(host, g.Mem.Length), "no camera code");
                Array.Clear(g.Mem, 0, g.Mem.Length);
                w = new SigsWorld();
                w.Freeze = w.Warp + 1;
                SigsLoad(g, w, 0);
                No(ScanRegion(host, g.Mem.Length), "freeze flag after the warp flag");
                Array.Clear(g.Mem, 0, g.Mem.Length);
                w = new SigsWorld();
                w.Omit.Add("haze");
                SigsLoad(g, w, 0);
                No(ScanRegion(host, g.Mem.Length), "no haze code");
            }
        }

        // the check of every loop: the haze text still where the scan found it
        [Test] static void Sigs_GameStillLoadedWhileTheTextIsThere()
        {
            using (FakeGame g = new FakeGame(0x110000))
            {
                g.Put(0x10A000, HAZE_TEXT);
                aHazeText = 0x10A000;
                IntPtr h = hProc;
                int pid = gamePid;
                Ok(GameStillLoaded(), "text in place: still loaded");
                Eq(h, hProc, "still attached (hProc)");
                Eq(pid, gamePid, "still attached (gamePid)");
            }
        }
    }
}
