// AWE32 music bank (src/AweBank.cs): AweBank.Build turning a SoundFont 1 bank (FF.SBK), a DLS level 1 set (gm.dls) and the
// 1 MB AWE32 ROM into a SoundFont 2. The three inputs are made up here (small RIFF files, a patterned ROM); the SF2 is parsed back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ------------------------------------------------------------------ RIFF pieces (little-endian, odd chunks padded)
        static byte[] AweCat(params byte[][] parts)
        {
            MemoryStream m = new MemoryStream();
            foreach (byte[] p in parts) m.Write(p, 0, p.Length);
            return m.ToArray();
        }
        static byte[] AweCk(string id, params byte[][] data)
        {
            byte[] d = AweCat(data);
            return AweCat(Encoding.ASCII.GetBytes(id), BitConverter.GetBytes(d.Length), d, new byte[d.Length & 1]);
        }
        static byte[] AweList(string type, params byte[][] parts) { return AweCk("LIST", Encoding.ASCII.GetBytes(type), AweCat(parts)); }
        static byte[] AweU16(int v) { return new byte[] { (byte)v, (byte)(v >> 8) }; }
        static byte[] AweI32(int v) { return BitConverter.GetBytes(v); }
        static byte[] AweName(string s)             // a 20-byte name field (code page 1252, as the converter reads it)
        {
            byte[] b = new byte[20], t = Encoding.GetEncoding(1252).GetBytes(s);
            Array.Copy(t, b, Math.Min(20, t.Length));
            return b;
        }
        static byte[] AweZ(string s) { return AweCat(Encoding.GetEncoding(1252).GetBytes(s), new byte[1]); }
        static byte[] AweWords(int[] w)
        {
            byte[] b = new byte[w.Length * 2];
            for (int i = 0; i < w.Length; i++) { b[2 * i] = (byte)w[i]; b[2 * i + 1] = (byte)(w[i] >> 8); }
            return b;
        }
        static int[] AweSeq(int start, int n, Func<int, int> word)
        {
            int[] r = new int[n];
            for (int i = 0; i < n; i++) r[i] = word(start + i);
            return r;
        }
        static int AweKr(int lo, int hi) { return lo | (hi << 8); }       // keyRange / velRange amount

        // the made-up AWE32 ROM: 16-bit word i holds AweRomWord(i); the made-up SBK RAM samples are AweRamWord(i)
        static int AweRomWord(int i) { return (i * 3 + 0x1111) & 0xffff; }
        static int AweRamWord(int i) { return (0x8000 + i * 7) & 0xffff; }
        static byte[] AweRomImage()
        {
            byte[] r = new byte[AweBank.ROM_SIZE];
            for (int i = 0; i < r.Length / 2; i++) { int w = AweRomWord(i); r[2 * i] = (byte)w; r[2 * i + 1] = (byte)(w >> 8); }
            return r;
        }

        // ------------------------------------------------------------------ a SoundFont 1 bank, as LoadSbk reads it
        // RIFF sfbk: LIST INFO, LIST sdta {snam (20-byte names), smpl}, LIST pdta {phdr 38, pbag 4, pmod, pgen 4, inst 22,
        // ibag 4, imod, igen 4, shdr 16 (start, end, loop start, loop end, in 16-bit words)}, each list with its terminal record.
        sealed class AweSbk
        {
            public byte[] Smpl = new byte[0];
            readonly List<string> names = new List<string>();
            readonly List<int[]> hdrs = new List<int[]>();
            readonly List<KeyValuePair<string, int[][]>> insts = new List<KeyValuePair<string, int[][]>>();
            readonly List<object[]> presets = new List<object[]>();     // name, prog, bank, zones

            public int Sample(string name, int start, int end, int loopStart, int loopEnd)
            {
                names.Add(name); hdrs.Add(new[] { start, end, loopStart, loopEnd });
                return names.Count - 1;
            }
            // a zone: its generators as { id, amount, id, amount... }
            public int Inst(string name, params int[][] zones) { insts.Add(new KeyValuePair<string, int[][]>(name, zones)); return insts.Count - 1; }
            public void Preset(string name, int prog, int bank, params int[][] zones) { presets.Add(new object[] { name, prog, bank, zones }); }
            // a preset of one instrument of these zones
            public void Simple(string name, int prog, int bank, params int[][] zones) { Preset(name, prog, bank, new[] { 41, Inst(name, zones) }); }

            public byte[] Image()
            {
                MemoryStream snam = new MemoryStream(), shdr = new MemoryStream();
                for (int i = 0; i < names.Count; i++)
                {
                    Put(snam, AweName(names[i]));
                    foreach (int v in hdrs[i]) Put(shdr, AweI32(v));
                }
                Put(snam, AweName("EOS")); Put(shdr, new byte[16]);
                MemoryStream phdr = new MemoryStream(), pbag = new MemoryStream(), pgen = new MemoryStream();
                MemoryStream inst = new MemoryStream(), ibag = new MemoryStream(), igen = new MemoryStream();
                int bag = 0, gen = 0;
                foreach (object[] p in presets)
                {
                    Put(phdr, AweName((string)p[0]), AweU16((int)p[1]), AweU16((int)p[2]), AweU16(bag), new byte[12]);
                    PutZones(pbag, pgen, (int[][])p[3], ref bag, ref gen);
                }
                Put(phdr, AweName("EOP"), AweU16(0), AweU16(0), AweU16(bag), new byte[12]);
                Put(pbag, AweU16(gen), AweU16(0)); Put(pgen, new byte[4]);
                bag = 0; gen = 0;
                foreach (KeyValuePair<string, int[][]> i in insts)
                {
                    Put(inst, AweName(i.Key), AweU16(bag));
                    PutZones(ibag, igen, i.Value, ref bag, ref gen);
                }
                Put(inst, AweName("EOI"), AweU16(bag));
                Put(ibag, AweU16(gen), AweU16(0)); Put(igen, new byte[4]);
                return AweCk("RIFF", Encoding.ASCII.GetBytes("sfbk"),
                    AweList("INFO", AweCk("ifil", AweU16(1), AweU16(0)), AweCk("INAM", AweZ("Made-up SBK"))),
                    AweList("sdta", AweCk("snam", snam.ToArray()), AweCk("smpl", Smpl)),
                    AweList("pdta", AweCk("phdr", phdr.ToArray()), AweCk("pbag", pbag.ToArray()), AweCk("pmod", new byte[10]),
                        AweCk("pgen", pgen.ToArray()), AweCk("inst", inst.ToArray()), AweCk("ibag", ibag.ToArray()),
                        AweCk("imod", new byte[10]), AweCk("igen", igen.ToArray()), AweCk("shdr", shdr.ToArray())));
            }
            static void PutZones(MemoryStream bags, MemoryStream gens, int[][] zones, ref int bag, ref int gen)
            {
                foreach (int[] z in zones)
                {
                    Put(bags, AweU16(gen), AweU16(0)); bag++;
                    for (int k = 0; k + 1 < z.Length; k += 2) { Put(gens, AweU16(z[k]), AweU16(z[k + 1])); gen++; }
                }
            }
            static void Put(MemoryStream m, params byte[][] parts) { foreach (byte[] p in parts) m.Write(p, 0, p.Length); }
        }

        // ------------------------------------------------------------------ a DLS level 1 set, as LoadDls reads it
        // RIFF DLS: LIST lins of LIST ins {insh, LIST lrgn of LIST rgn {rgnh, wsmp, wlnk, LIST lart {art1}}, LIST lart, LIST INFO
        // {INAM}}, ptbl (offsets of the waves in wvpl), LIST wvpl of LIST wave {fmt, wsmp, data}
        sealed class AweDls
        {
            readonly List<byte[]> ins = new List<byte[]>(), waves = new List<byte[]>();
            public int[] Cues;          // ptbl entry -> wave number; null: one entry per wave, in order

            public void Ins(string name, uint bank, int prog, byte[] lart, params byte[][] regions)
            {
                ins.Add(AweList("ins ", AweCk("insh", AweI32(regions.Length), AweI32((int)bank), AweI32(prog)), AweList("lrgn", regions),
                    lart ?? new byte[0], AweList("INFO", AweCk("INAM", AweZ(name)))));
            }
            // a 16-bit mono PCM wave; returns its number (its ptbl entry when Cues is null)
            public int Wave(int rate, int[] pcm, byte[] wsmp)
            {
                waves.Add(AweList("wave", AweCk("fmt ", AweU16(1), AweU16(1), AweI32(rate), AweI32(rate * 2), AweU16(2), AweU16(16)),
                    wsmp ?? new byte[0], AweCk("data", AweWords(pcm))));
                return waves.Count - 1;
            }
            public static byte[] Rgn(int kl, int kh, int vl, int vh, int keyGroup, byte[] wsmp, int cue, byte[] lart)
            {
                return AweList("rgn ", AweCk("rgnh", AweU16(kl), AweU16(kh), AweU16(vl), AweU16(vh), AweU16(0), AweU16(keyGroup)),
                    wsmp ?? new byte[0], AweCk("wlnk", AweU16(0), AweU16(0), AweI32(1), AweI32(cue)), lart ?? new byte[0]);
            }
            // gain in DLS units (1/65536 cB); loopStart < 0: no loop
            public static byte[] Wsmp(int unity, int fine, int gain, int loopStart, int loopLen)
            {
                byte[] head = AweCat(AweI32(20), AweU16(unity), AweU16(fine), AweI32(gain), AweI32(0), AweI32(loopStart >= 0 ? 1 : 0));
                return loopStart < 0 ? AweCk("wsmp", head) : AweCk("wsmp", head, AweI32(16), AweI32(0), AweI32(loopStart), AweI32(loopLen));
            }
            // LIST lart {art1}: connection blocks given as { source, control, destination, scale (16.16) } ...
            public static byte[] Art(params int[] c)
            {
                int n = c.Length / 4;
                MemoryStream m = new MemoryStream();
                foreach (byte[] b in new[] { AweI32(8), AweI32(n) }) m.Write(b, 0, b.Length);
                for (int i = 0; i < n; i++)
                    foreach (byte[] b in new[] { AweU16(c[4 * i]), AweU16(c[4 * i + 1]), AweU16(c[4 * i + 2]), AweU16(0), AweI32(c[4 * i + 3]) })
                        m.Write(b, 0, b.Length);
                return AweList("lart", AweCk("art1", m.ToArray()));
            }
            public byte[] Image()
            {
                int[] offs = new int[waves.Count];
                for (int i = 1; i < waves.Count; i++) offs[i] = offs[i - 1] + waves[i - 1].Length;
                int[] cues = Cues ?? Enumerable.Range(0, waves.Count).ToArray();
                MemoryStream pt = new MemoryStream();
                foreach (byte[] b in new[] { AweI32(8), AweI32(cues.Length) }) pt.Write(b, 0, b.Length);
                foreach (int c in cues) pt.Write(AweI32(offs[c]), 0, 4);
                return AweCk("RIFF", Encoding.ASCII.GetBytes("DLS "), AweCk("colh", AweI32(ins.Count)), AweCk("vers", new byte[8]),
                    AweList("lins", ins.ToArray()), AweCk("ptbl", pt.ToArray()), AweList("wvpl", waves.ToArray()),
                    AweList("INFO", AweCk("INAM", AweZ("Made-up DLS"))));
            }
        }

        // ------------------------------------------------------------------ the SoundFont 2 read back
        // The constructor checks the RIFF structure: RIFF size = file size - 8, top-level LISTs and their chunks fitting exactly.
        sealed class AweSf2
        {
            public readonly byte[] B;
            public readonly List<string> Top = new List<string>();
            public readonly Dictionary<string, List<string>> Kids = new Dictionary<string, List<string>>();
            readonly Dictionary<string, int[]> at = new Dictionary<string, int[]>();     // chunk id -> data offset, size

            public AweSf2(byte[] b)
            {
                B = b;
                Ok(b != null && b.Length >= 12, "SF2: at least a RIFF header");
                Eq("RIFF", Id(0), "SF2: RIFF");
                Eq(b.Length - 8, I32(4), "SF2: RIFF size = file size - 8");
                Eq("sfbk", Id(8), "SF2: form type");
                int p = 12;
                while (p < b.Length)
                {
                    Ok(p + 12 <= b.Length, "SF2: LIST header at " + p + " inside the file");
                    int sz = I32(p + 4), end = p + 8 + sz;
                    Eq("LIST", Id(p), "SF2: top level chunk at " + p);
                    Ok(sz >= 4 && end <= b.Length, "SF2: LIST at " + p + " (" + sz + " bytes) inside the file");
                    string type = Id(p + 8);
                    List<string> kids = new List<string>();
                    Top.Add(type); Kids[type] = kids;
                    int q = p + 12;
                    while (q < end)
                    {
                        Ok(q + 8 <= end, "SF2: " + type + ": chunk header at " + q + " inside its LIST");
                        string id = Id(q);
                        int n = I32(q + 4);
                        Ok(n >= 0 && q + 8 + n <= end, "SF2: " + type + "/" + id + " (" + n + " bytes) inside its LIST");
                        No(at.ContainsKey(id), "SF2: a single " + id + " chunk");
                        kids.Add(id); at[id] = new[] { q + 8, n };
                        q += 8 + n + (n & 1);
                    }
                    Eq(end, q, "SF2: LIST " + type + " size = its chunks");
                    p = end + (sz & 1);
                }
                Eq(b.Length, p, "SF2: the file ends with its last LIST");
            }
            string Id(int o) { return Encoding.ASCII.GetString(B, o, 4); }
            public int U16(int o) { return BitConverter.ToUInt16(B, o); }
            public int S16(int o) { return BitConverter.ToInt16(B, o); }
            public int I32(int o) { return BitConverter.ToInt32(B, o); }
            public string Name(int o) { int e = o; while (e < o + 20 && B[e] != 0) e++; return Encoding.GetEncoding(1252).GetString(B, o, e - o); }
            public byte[] Raw(int o, int n) { byte[] r = new byte[n]; Array.Copy(B, o, r, 0, n); return r; }
            public bool AllZero(int o, int n) { for (int i = 0; i < n; i++) if (B[o + i] != 0) return false; return true; }
            public int Off(string id) { int[] a; if (!at.TryGetValue(id, out a)) Fail("SF2: no " + id + " chunk"); return a[0]; }
            public int Size(string id) { Off(id); return at[id][1]; }
            public byte[] Data(string id) { return Raw(Off(id), Size(id)); }
            public int Count(string id, int rec)
            {
                int n = Size(id);
                Eq(0, n % rec, "SF2: " + id + " (" + n + " bytes) made of " + rec + "-byte records");
                return n / rec;
            }
            public int Word(int i) { return U16(Off("smpl") + 2 * i); }

            public List<AwePre> Presets()           // file order, without the terminal EOP
            {
                List<AwePre> r = new List<AwePre>();
                int n = Count("phdr", 38);
                for (int i = 0; i < n - 1; i++)
                {
                    int o = Off("phdr") + 38 * i, bag = U16(o + 24), go = Off("pgen") + 4 * U16(Off("pbag") + 4 * bag);
                    r.Add(new AwePre { Name = Name(o), Prog = U16(o + 20), Bank = U16(o + 22), Bag = bag, Inst = U16(go) == 41 ? U16(go + 2) : -1 });
                }
                return r;
            }
            public int InstCount { get { return Count("inst", 22) - 1; } }
            public string InstName(int i) { return Name(Off("inst") + 22 * i); }
            // the zones of an instrument: their generators { id, amount } in file order (keyRange, velRange, sampleID unsigned)
            public List<List<int[]>> Zones(int inst)
            {
                Ok(inst >= 0 && inst < InstCount, "SF2: instrument " + inst + " exists");
                int o = Off("inst") + 22 * inst, b0 = U16(o + 20), b1 = U16(o + 22 + 20);
                List<List<int[]>> r = new List<List<int[]>>();
                for (int b = b0; b < b1; b++)
                {
                    int g0 = U16(Off("ibag") + 4 * b), g1 = U16(Off("ibag") + 4 * b + 4);
                    List<int[]> z = new List<int[]>();
                    for (int g = g0; g < g1; g++)
                    {
                        int go = Off("igen") + 4 * g, id = U16(go);
                        z.Add(new[] { id, id == 43 || id == 44 || id == 53 ? U16(go + 2) : S16(go + 2) });
                    }
                    r.Add(z);
                }
                return r;
            }
            public List<List<int[]>> ZonesOf(int bank, int prog)
            {
                foreach (AwePre p in Presets()) if (p.Bank == bank && p.Prog == prog) return Zones(p.Inst);
                Fail("SF2: no preset " + bank + ":" + prog);
                return null;
            }
            public int SampleCount { get { return Count("shdr", 46) - 1; } }
            public AweSmp Sample(int i)
            {
                Ok(i >= 0 && i < SampleCount, "SF2: sample " + i + " exists");
                int o = Off("shdr") + 46 * i;
                return new AweSmp { Name = Name(o), Start = I32(o + 20), End = I32(o + 24), LoopStart = I32(o + 28), LoopEnd = I32(o + 32),
                    Rate = I32(o + 36), Pitch = B[o + 40], Corr = (sbyte)B[o + 41], Link = U16(o + 42), Type = U16(o + 44) };
            }
            public int[] Pcm(int i)
            {
                AweSmp s = Sample(i);
                int[] r = new int[s.End - s.Start];
                for (int k = 0; k < r.Length; k++) r[k] = Word(s.Start + k);
                return r;
            }
        }
        sealed class AwePre { public string Name; public int Prog, Bank, Bag, Inst; }
        sealed class AweSmp { public string Name; public int Start, End, LoopStart, LoopEnd, Rate, Pitch, Corr, Link, Type; }

        // ------------------------------------------------------------------ helpers
        static AweSf2 AweBuild(AweSbk sbk, AweDls dls)
        {
            string s = PutFile(Path.Combine(ExeDir, "FF.SBK"), sbk.Image());
            string d = PutFile(Path.Combine(ExeDir, "gm.dls"), (dls ?? new AweDls()).Image());
            return new AweSf2(AweBank.Build(s, d, AweRomImage()));
        }

        // a bit of everything: ROM and RAM samples, a loop, a melodic preset, a drum kit filled from the DLS, a DLS-only preset
        static void AweStdInputs(out AweSbk s, out AweDls d)
        {
            s = new AweSbk();
            s.Smpl = AweWords(AweSeq(0, 64, AweRamWord));
            int rom = s.Sample("*Rom A", 1000, 1100, 1020, 1080), ram = s.Sample("Ram B", 4, 20, 6, 18);
            s.Simple("Piano", 0, 0, new[] { 43, AweKr(0, 59), 53, rom }, new[] { 43, AweKr(60, 127), 54, 1, 53, ram });
            s.Simple("Kit", 0, 128, new[] { 43, AweKr(36, 36), 53, rom });
            d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 40, i => i * 11), AweDls.Wsmp(60, 0, 0, -1, 0));
            d.Ins("GS Kit", 0x80000000, 0, null, AweDls.Rgn(35, 38, 0, 127, 0, null, w, null));
            d.Ins("GS Organ", 0, 16, null, AweDls.Rgn(0, 127, 0, 127, 0, null, w, null));
        }
        static AweSf2 AweStd() { AweSbk s; AweDls d; AweStdInputs(out s, out d); return AweBuild(s, d); }

        static List<string> AwePresets(AweSf2 f) { return f.Presets().Select(p => p.Bank + ":" + p.Prog + " " + p.Name).ToList(); }
        static bool AweHas(List<int[]> zone, int gen) { foreach (int[] g in zone) if (g[0] == gen) return true; return false; }
        static int AweGen(List<int[]> zone, int gen)
        {
            foreach (int[] g in zone) if (g[0] == gen) return g[1];
            Fail("no generator " + gen + " in the zone " + AweShow(zone));
            return 0;
        }
        static string AweShow(List<int[]> zone) { return "{ " + string.Join(" ", zone.Select(g => g[0] + "=" + g[1]).ToArray()) + " }"; }
        // each zone's key range "lo-hi" ("all": no keyRange generator)
        static List<string> AweKeys(List<List<int[]>> zones)
        {
            return zones.Select(z => AweHas(z, 43) ? (AweGen(z, 43) & 0xff) + "-" + (AweGen(z, 43) >> 8) : "all").ToList();
        }
        // the zone has exactly these generators { id, amount... } besides its sampleID, which comes last
        static void AweGens(List<int[]> zone, string what, params int[] expected)
        {
            List<string> e = new List<string>(), a = new List<string>();
            for (int i = 0; i + 1 < expected.Length; i += 2) e.Add(expected[i] + "=" + expected[i + 1]);
            foreach (int[] g in zone) if (g[0] != 53) a.Add(g[0] + "=" + g[1]);
            List<string> missing = e.Except(a).ToList(), extra = a.Except(e).ToList();
            if (missing.Count > 0 || extra.Count > 0 || a.Count != e.Count)
                Fail(what + ": missing " + string.Join(" ", missing.ToArray()) + "; unexpected " + string.Join(" ", extra.ToArray()) + "; zone " + AweShow(zone));
            Eq(53, zone[zone.Count - 1][0], what + ": sampleID last");
        }
        static string AweTune(List<int[]> z)
        {
            return "root " + AweGen(z, 58) + (AweHas(z, 51) ? " coarse " + AweGen(z, 51) : "") + (AweHas(z, 52) ? " fine " + AweGen(z, 52) : "") +
                (AweHas(z, 56) ? " scale " + AweGen(z, 56) : "");
        }

        // ------------------------------------------------------------------ tests
        [Test] static void Awe_RomSizeChecked()
        {
            Eq(1048576, AweBank.ROM_SIZE, "ROM_SIZE: the 1 MB AWE32 ROM dump (awe32.raw)");
            string none = Path.Combine(ExeDir, "missing.sbk");
            // the ROM is checked first: the (missing) bank and DLS are not even opened
            ArgumentException e = Throws<ArgumentException>(delegate { AweBank.Build(none, none, null); }, "no ROM");
            Has(e.Message, "1 MB", "the message PrepareAwe shows");
            Throws<ArgumentException>(delegate { AweBank.Build(none, none, new byte[0]); }, "empty ROM");
            Throws<ArgumentException>(delegate { AweBank.Build(none, none, new byte[AweBank.ROM_SIZE - 1]); }, "ROM one byte short");
            Throws<ArgumentException>(delegate { AweBank.Build(none, none, new byte[AweBank.ROM_SIZE + 2]); }, "ROM one word too long");
            // a right-sized ROM gets past the check: then the missing bank is what fails (PrepareAwe catches it)
            Throws<FileNotFoundException>(delegate { AweBank.Build(none, none, new byte[AweBank.ROM_SIZE]); }, "missing FF.SBK");
        }

        [Test] static void Awe_DlsPath()
        {
            string p = AweBank.DlsPath();
            Eq(Path.Combine(Path.Combine(Environment.SystemDirectory, "drivers"), "gm.dls"), p, "gm.dls in the drivers folder of the system directory");
            Eq("gm.dls", Path.GetFileName(p), "file name");
            Eq("drivers", Path.GetFileName(Path.GetDirectoryName(p)), "its folder");
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Ok(p.EndsWith("\\drivers\\gm.dls", StringComparison.OrdinalIgnoreCase), "Windows path " + p);
                Eq(Environment.SystemDirectory.TrimEnd('\\'), Path.GetDirectoryName(Path.GetDirectoryName(p)), "under the system directory");
            }
        }

        [Test] static void Awe_RiffLayout()
        {
            AweSf2 f = AweStd();        // the parser itself checks every RIFF / LIST / chunk size against what it holds
            EqList(new[] { "INFO", "sdta", "pdta" }, f.Top, "LISTs of the sfbk form, in the SF2 order");
            EqList(new[] { "ifil", "isng", "INAM", "ISFT" }, f.Kids["INFO"], "INFO chunks (ifil, isng, INAM mandatory)");
            EqList(new[] { "smpl" }, f.Kids["sdta"], "sdta: 16-bit samples only");
            EqList(new[] { "phdr", "pbag", "pmod", "pgen", "inst", "ibag", "imod", "igen", "shdr" }, f.Kids["pdta"], "pdta chunks, in the order the SF2 spec requires");
            Eq(0, f.B.Length & 1, "even file size");
        }

        [Test] static void Awe_InfoChunk()
        {
            AweSf2 f = AweStd();
            EqBytes(new byte[] { 2, 0, 1, 0 }, f.Data("ifil"), "ifil: SoundFont 2.01");
            EqBytes(Encoding.ASCII.GetBytes("EMU8000\0"), f.Data("isng"), "isng: the AWE32's sound engine");
            EqBytes(Encoding.ASCII.GetBytes("Terra Nova AWE32\0\0"), f.Data("INAM"), "INAM: bank name, zero-terminated, even size");
            EqBytes(Encoding.ASCII.GetBytes("TNPlus\0\0"), f.Data("ISFT"), "ISFT: the tool");
        }

        [Test] static void Awe_PdtaRecords()
        {
            AweSf2 f = AweStd();
            // presets: Piano and Kit from the bank, GS Organ from the DLS (GS Kit is the bank's Kit: not added)
            int ph = f.Off("phdr"), np = f.Count("phdr", 38);
            Eq(4, np, "phdr: 3 presets + EOP");
            EqList(new[] { "0:0 Piano", "0:16 GS Organ", "128:0 Kit" }, AwePresets(f), "presets");
            int eop = ph + 38 * (np - 1);
            Eq("EOP", f.Name(eop), "terminal preset name");
            Eq(0, f.U16(eop + 20), "EOP program");
            Eq(0, f.U16(eop + 22), "EOP bank");
            for (int i = 0; i < np; i++) Ok(f.AllZero(ph + 38 * i + 26, 12), "phdr " + i + ": library, genre, morphology 0");
            // one zone per preset, holding only its instrument
            int nb = f.Count("pbag", 4), ng = f.Count("pgen", 4);
            Eq(np, nb, "pbag: one zone per preset + terminal");
            Eq(nb - 1, f.U16(eop + 24), "EOP bag index = the terminal pbag");
            Eq(nb, ng, "pgen: one generator per zone + terminal");
            for (int i = 0; i < np; i++) Eq(i, f.U16(ph + 38 * i + 24), "phdr " + i + " bag index");
            for (int i = 0; i < nb; i++)
            {
                Eq(i, f.U16(f.Off("pbag") + 4 * i), "pbag " + i + " generator index");
                Eq(0, f.U16(f.Off("pbag") + 4 * i + 2), "pbag " + i + " modulator index (no modulators)");
            }
            foreach (AwePre p in f.Presets()) Eq(p.Name, f.InstName(p.Inst), p.Name + ": instrument generator, to the instrument of its name");
            Ok(f.AllZero(f.Off("pgen") + 4 * (ng - 1), 4), "pgen terminal record");
            EqBytes(new byte[10], f.Data("pmod"), "pmod: the terminal modulator only");
            EqBytes(new byte[10], f.Data("imod"), "imod: the terminal modulator only");
            // instruments: Piano 2 zones, Kit 3 (its key + 2 pieces of the GS kit), GS Organ 1
            int ni = f.Count("inst", 22), nib = f.Count("ibag", 4), nig = f.Count("igen", 4);
            Eq(4, ni, "inst: 3 + EOI");
            EqList(new[] { "Piano", "Kit", "GS Organ", "EOI" }, Enumerable.Range(0, ni).Select(i => f.InstName(i)).ToList(), "instrument names");
            EqList(new[] { 0, 2, 5, 6 }, Enumerable.Range(0, ni).Select(i => f.U16(f.Off("inst") + 22 * i + 20)).ToList(), "instrument bag indexes");
            Eq(7, nib, "ibag: 6 zones + terminal");
            int last = -1;
            for (int i = 0; i < nib; i++)
            {
                int g = f.U16(f.Off("ibag") + 4 * i);
                Ok(g > last, "ibag " + i + ": generator index growing");
                last = g;
                Eq(0, f.U16(f.Off("ibag") + 4 * i + 2), "ibag " + i + " modulator index");
            }
            Eq(nig - 1, last, "terminal ibag = the terminal igen");
            Ok(f.AllZero(f.Off("igen") + 4 * (nig - 1), 4), "igen terminal record");
            // samples
            int ns = f.Count("shdr", 46);
            Eq(4, ns, "shdr: ROM A, Ram B, the GS wave + EOS");
            int eos = f.Off("shdr") + 46 * (ns - 1);
            Eq("EOS", f.Name(eos), "terminal sample name");
            Ok(f.AllZero(eos + 20, 26), "EOS: the rest zero");
        }

        [Test] static void Awe_SmplLayout()
        {
            AweSf2 f = AweStd();
            int n = f.SampleCount, next = 0;
            Eq(3, n, "samples (the ROM sample used by two zones is stored once)");
            for (int i = 0; i < n; i++)
            {
                AweSmp s = f.Sample(i);
                Eq(next, s.Start, "sample " + i + " starts right after the previous one's 46 zero samples");
                Ok(s.End > s.Start, "sample " + i + " not empty");
                for (int w = s.End; w < s.End + 46; w++) Eq(0, f.Word(w), "zero sample " + (w - s.End) + " after sample " + i);
                next = s.End + 46;
            }
            Eq(next * 2, f.Size("smpl"), "smpl ends with the last sample's 46 zero samples");
            EqList(new[] { 100, 16, 40 }, Enumerable.Range(0, n).Select(i => f.Sample(i).End - f.Sample(i).Start).ToList(), "sample lengths");
        }

        [Test] static void Awe_RomAndRamSamples()
        {
            AweSbk s = new AweSbk();
            s.Smpl = AweWords(AweSeq(0, 64, AweRamWord));
            int a = s.Sample("*Rom A", 1000, 1100, 0, 0), b = s.Sample("Ram B", 4, 20, 0, 0);
            s.Simple("Two", 0, 0, new[] { 43, AweKr(0, 59), 53, a }, new[] { 43, AweKr(60, 127), 53, b });
            AweSf2 f = AweBuild(s, null);
            List<List<int[]>> z = f.ZonesOf(0, 0);
            Eq(2, z.Count, "zones");
            int ia = AweGen(z[0], 53), ib = AweGen(z[1], 53);
            // '*': in the ROM, addresses in 16-bit words; otherwise in the bank's smpl (although the ROM has words 4-19 too)
            EqList(AweSeq(1000, 100, AweRomWord), f.Pcm(ia), "'*Rom A': words 1000-1099 of the ROM");
            EqList(AweSeq(4, 16, AweRamWord), f.Pcm(ib), "'Ram B': words 4-19 of smpl");
            Eq("Rom A", f.Sample(ia).Name, "ROM sample name without its '*'");
            Eq("Ram B", f.Sample(ib).Name, "RAM sample name");
            foreach (int i in new[] { ia, ib })
            {
                AweSmp x = f.Sample(i);
                Eq(44100, x.Rate, x.Name + ": rate (SF1 has none: the AWE32's 44.1 kHz)");
                Eq(60, x.Pitch, x.Name + ": original pitch (the zones give the root key)");
                Eq(0, x.Corr, x.Name + ": pitch correction");
                Eq(0, x.Link, x.Name + ": sample link");
                Eq(1, x.Type, x.Name + ": mono sample (in smpl, not a ROM sample)");
                Eq(x.Start, x.LoopStart, x.Name + ": no loop: loop start = start");
                Eq(x.Start, x.LoopEnd, x.Name + ": no loop: loop end = start");
            }
        }

        [Test] static void Awe_LoopPoints()
        {
            AweSbk s = new AweSbk();
            int lp = s.Sample("*Loop", 1000, 1100, 1020, 1080);
            int tail = s.Sample("*Tail", 2000, 2050, 2010, 2050);
            int flat = s.Sample("*Flat", 3000, 3040, 3010, 3010);
            s.Simple("Loops", 0, 0,
                new[] { 43, AweKr(0, 9), 54, 1, 53, lp },
                new[] { 43, AweKr(10, 19), 53, lp },
                new[] { 43, AweKr(20, 29), 54, 3, 53, lp },
                new[] { 43, AweKr(30, 39), 54, 1, 53, tail },
                new[] { 43, AweKr(40, 49), 54, 1, 53, flat });
            AweSf2 f = AweBuild(s, null);
            List<List<int[]>> z = f.ZonesOf(0, 0);
            Eq(5, z.Count, "zones");
            int[] smp = z.Select(x => AweGen(x, 53)).ToArray();
            AweSmp looped = f.Sample(smp[0]);
            EqList(AweSeq(1000, 100, AweRomWord), f.Pcm(smp[0]), "looped: words 1000-1099 (the loop ends inside)");
            Eq(21, looped.LoopStart - looped.Start, "loop start: SBK loop start + 1 (1020 + 1 - 1000)");
            Eq(82, looped.LoopEnd - looped.Start, "loop end: SBK loop end + 2 (1080 + 2 - 1000)");
            Eq(1, AweGen(z[0], 54), "sampleModes kept");
            Ok(smp[1] != smp[0], "the same SBK sample without loop: a sample of its own");
            AweSmp plain = f.Sample(smp[1]);
            EqList(AweSeq(1000, 100, AweRomWord), f.Pcm(smp[1]), "unlooped: the same words");
            Eq(plain.Start, plain.LoopStart, "unlooped: loop start = start");
            Eq(plain.Start, plain.LoopEnd, "unlooped: loop end = start");
            No(AweHas(z[1], 54), "no sampleModes where the SBK zone has none");
            Eq(smp[0], smp[2], "sampleModes 3 (bit 0 set) loops too: the looped sample again");
            Eq(3, AweGen(z[2], 54), "sampleModes 3 kept");
            AweSmp t = f.Sample(smp[3]);
            EqList(AweSeq(2000, 52, AweRomWord), f.Pcm(smp[3]), "loop end + 2 past the sample end: the sample runs to it (2052)");
            Eq(11, t.LoopStart - t.Start, "lengthened sample: loop start 2010 + 1");
            Eq(52, t.LoopEnd - t.Start, "lengthened sample: loop end 2050 + 2");
            AweSmp fl = f.Sample(smp[4]);
            EqList(AweSeq(3000, 40, AweRomWord), f.Pcm(smp[4]), "empty loop: the sample as it is");
            Eq(fl.Start, fl.LoopStart, "empty loop (loop end <= loop start): no loop points");
            Eq(fl.Start, fl.LoopEnd, "empty loop: no loop points (end)");
            Eq(4, f.SampleCount, "samples: looped, unlooped, lengthened, empty loop");
        }

        [Test] static void Awe_BadSamplesLeaveKeysToDls()
        {
            AweSbk s = new AweSbk();
            s.Smpl = AweWords(AweSeq(0, 64, AweRamWord));
            int past = s.Sample("*Past", 524200, 524300, 0, 0);    // ends 12 words after the ROM (524288 words)
            int neg = s.Sample("*Neg", -5, 10, 0, 0);
            int back = s.Sample("*Back", 500, 400, 0, 0);
            int big = s.Sample("Ram Big", 0, 100, 0, 0);           // smpl holds 64 words only
            int edge = s.Sample("*Edge", 524188, 524288, 0, 0);    // the ROM's last 100 words: fine
            s.Simple("Holes", 0, 0, new[] { 43, AweKr(10, 19), 53, past }, new[] { 43, AweKr(20, 29), 53, neg },
                new[] { 43, AweKr(30, 39), 53, back }, new[] { 43, AweKr(40, 49), 53, big }, new[] { 43, AweKr(50, 59), 53, edge });
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            d.Ins("GS Piano", 0, 0, null, AweDls.Rgn(0, 127, 0, 127, 0, null, w, null));
            AweSf2 f = AweBuild(s, d);
            List<List<int[]>> z = f.ZonesOf(0, 0);
            EqList(new[] { "50-59", "0-49", "60-127" }, AweKeys(z), "zones whose sample is out of its data are left out, their keys go to the DLS");
            EqList(AweSeq(524188, 100, AweRomWord), f.Pcm(AweGen(z[0], 53)), "*Edge: the ROM's last 100 words");
            Eq("GS000", f.Sample(AweGen(z[1], 53)).Name, "the free keys: the GS wave");
            Eq(2, f.SampleCount, "samples: Edge and the GS wave");
        }

        [Test] static void Awe_GeneratorOrder()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0);
            // generators in a scrambled order, sampleID in the middle
            s.Simple("Mixed", 0, 0, new[] { 48, 127, 53, a, 44, AweKr(0, 63), 17, 64, 43, AweKr(0, 127) }, new[] { 53, a, 44, AweKr(64, 127), 9, 10 });
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(60, 0, -30 * 65536, 2, 10));
            d.Ins("GS Pad", 0, 88, AweDls.Art(0, 0, 0x206, 0, 0, 0, 4, 100 * 65536), AweDls.Rgn(10, 90, 0, 127, 1, null, w, null));
            AweSf2 f = AweBuild(s, d);
            int zones = 0;
            foreach (AwePre p in f.Presets())
                foreach (List<int[]> z in f.Zones(p.Inst))
                {
                    zones++;
                    string what = p.Name + " zone " + AweShow(z);
                    int i43 = z.FindIndex(g => g[0] == 43), i44 = z.FindIndex(g => g[0] == 44);
                    if (i43 >= 0) Eq(0, i43, what + ": keyRange first");
                    if (i44 >= 0) Eq(i43 >= 0 ? 1 : 0, i44, what + ": velRange first or right after keyRange");
                    Eq(53, z[z.Count - 1][0], what + ": sampleID last");
                    foreach (int id in new[] { 43, 44, 53 }) Ok(z.FindAll(g => g[0] == id).Count <= 1, what + ": generator " + id + " once");
                    Eq(z.Count, z.Select(g => g[0]).Distinct().Count(), what + ": no generator twice");
                }
            Eq(3, zones, "zones checked (2 from the bank, 1 from the DLS)");
            List<List<int[]>> m = f.ZonesOf(0, 0);
            Eq(AweKr(0, 127), AweGen(m[0], 43), "keyRange amount kept");
            Eq(AweKr(0, 63), AweGen(m[0], 44), "velRange amount kept");
            Eq(AweKr(64, 127), AweGen(m[1], 44), "velRange of a zone without keyRange");
            No(AweHas(m[1], 43), "no keyRange where the SBK zone has none (SF2 default: all keys)");
        }

        [Test] static void Awe_PresetsSorted()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0);
            int[] z = { 53, a };
            s.Simple("Kit", 0, 128, z);
            s.Simple("Brass", 61, 0, z);
            s.Simple("Piano", 0, 0, z);
            s.Simple("Var", 5, 8, z);
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            byte[] r = AweDls.Rgn(0, 127, 0, 127, 0, null, w, null);
            d.Ins("GS Power", 0x80000000, 16, null, r);
            d.Ins("GS Bright", 0, 1, null, r);
            d.Ins("GS Var", 8 << 8, 4, null, r);
            AweSf2 f = AweBuild(s, d);
            List<AwePre> p = f.Presets();
            EqList(new[] { "0:0 Piano", "0:1 GS Bright", "0:61 Brass", "8:4 GS Var", "8:5 Var", "128:0 Kit", "128:16 GS Power" }, AwePresets(f),
                "presets sorted by bank, then program");
            for (int i = 0; i < p.Count; i++)
            {
                Eq(i, p[i].Bag, p[i].Name + ": one zone each");
                Eq(p[i].Name, f.InstName(p[i].Inst), p[i].Name + ": its own instrument");
            }
            // the instruments keep the order they were made in: the bank's presets (file order), then the DLS ones
            EqList(new[] { "Kit", "Brass", "Piano", "Var", "GS Power", "GS Bright", "GS Var" }, Enumerable.Range(0, f.InstCount).Select(i => f.InstName(i)).ToList(), "instruments");
        }

        [Test] static void Awe_DlsOnlyPresets()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0);
            s.Simple("Piano", 0, 0, new[] { 43, AweKr(0, 127), 53, a });
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            byte[] r = AweDls.Rgn(0, 127, 0, 127, 0, null, w, null);
            d.Ins("GS Piano", 0, 0, null, r);                           // the bank has program 0: left out
            d.Ins("GS Celesta", 0, 10, null, AweDls.Rgn(0, 63, 0, 127, 0, null, w, null), AweDls.Rgn(64, 127, 0, 127, 0, null, w, null));
            d.Ins("GS Standard", 0x80000000, 0, null, r);               // drums -> bank 128
            d.Ins("GS Var 16", 16 << 8, 10, null, r);                   // bank select MSB 16 -> bank 16
            d.Ins("GS LSB", 1, 11, null, r);                            // bank select LSB set: left out
            d.Ins("GS Drum LSB", 0x80000001, 8, null, r);               // left out too
            d.Ins("GS Kit MSB", 0x80000100, 40, null, r);               // drums, whatever the MSB: bank 128
            AweSf2 f = AweBuild(s, d);
            EqList(new[] { "0:0 Piano", "0:10 GS Celesta", "16:10 GS Var 16", "128:0 GS Standard", "128:40 GS Kit MSB" }, AwePresets(f),
                "DLS instruments the bank does not have, at their SF2 bank");
            List<List<int[]>> c = f.ZonesOf(0, 10);
            EqList(new[] { "0-63", "64-127" }, AweKeys(c), "a DLS-only preset: one zone per region");
            Eq(AweGen(c[0], 53), AweGen(c[1], 53), "same wave, same loop: one sample");
            EqList(new[] { "0-127" }, AweKeys(f.ZonesOf(0, 0)), "the bank's piano covers all keys: nothing from GS Piano");
        }

        [Test] static void Awe_DlsFillsFreeKeys()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0), b = s.Sample("*B", 300, 400, 0, 0);
            s.Simple("Piano", 0, 0, new[] { 43, AweKr(40, 50), 53, a }, new[] { 43, AweKr(60, 70), 53, b });
            s.Simple("Full", 1, 0, new[] { 53, a });            // no keyRange: all keys, nothing left for the DLS
            AweDls d = new AweDls();
            int w0 = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(48, 0, 0, -1, 0));
            int w1 = d.Wave(22050, AweSeq(100, 30, AweRamWord), AweDls.Wsmp(72, 0, 0, -1, 0));
            d.Ins("GS Piano", 0, 0, null, AweDls.Rgn(0, 55, 0, 127, 0, null, w0, null), AweDls.Rgn(56, 127, 0, 127, 0, null, w1, null));
            d.Ins("GS Bright", 0, 1, null, AweDls.Rgn(0, 127, 0, 127, 0, null, w0, null));
            AweSf2 f = AweBuild(s, d);
            EqList(new[] { "0:0 Piano", "0:1 Full" }, AwePresets(f), "presets: the DLS ones the bank has are not added again");
            List<List<int[]>> z = f.ZonesOf(0, 0);
            EqList(new[] { "40-50", "60-70", "0-39", "51-55", "56-59", "71-127" }, AweKeys(z), "the bank's zones, then each DLS region cut to the free keys");
            int sa = AweGen(z[0], 53), sb = AweGen(z[1], 53), r0 = AweGen(z[2], 53), r1 = AweGen(z[4], 53);
            Eq(r0, AweGen(z[3], 53), "both pieces of the first region: one sample");
            Eq(r1, AweGen(z[5], 53), "both pieces of the second region: one sample");
            Eq(4, new[] { sa, sb, r0, r1 }.Distinct().Count(), "4 different samples");
            Eq("GS000", f.Sample(r0).Name, "first region's wave");
            Eq("GS001", f.Sample(r1).Name, "second region's wave");
            Eq(48, AweGen(z[2], 58), "a DLS piece: root key = its region's unity note");
            Eq(72, AweGen(z[5], 58), "a DLS piece of the second region: its unity note");
            EqList(new[] { "all" }, AweKeys(f.ZonesOf(0, 1)), "a zone without keyRange takes all keys: no DLS piece");
        }

        [Test] static void Awe_DlsFillsDrumKeys()
        {
            AweSbk s = new AweSbk();
            int k = s.Sample("*Kick", 100, 200, 0, 0), sn = s.Sample("*Snare", 300, 400, 0, 0);
            s.Simple("Drums", 0, 128, new[] { 43, AweKr(36, 36), 53, k }, new[] { 43, AweKr(38, 38), 53, sn });
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            int wh = d.Wave(22050, AweSeq(50, 20, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            d.Ins("GS Piano", 0, 0, null, AweDls.Rgn(0, 127, 0, 127, 0, null, w, null));    // melodic program 0: not for the kit
            d.Ins("GS Standard", 0x80000000, 0, null, AweDls.Rgn(35, 35, 0, 127, 0, null, w, null), AweDls.Rgn(36, 36, 0, 127, 0, null, w, null),
                AweDls.Rgn(37, 38, 0, 127, 0, null, w, null), AweDls.Rgn(42, 42, 0, 127, 1, null, wh, null));
            AweSf2 f = AweBuild(s, d);
            EqList(new[] { "0:0 GS Piano", "128:0 Drums" }, AwePresets(f), "presets: the GS kit is the bank's, the GS piano is added");
            List<List<int[]>> z = f.ZonesOf(128, 0);
            EqList(new[] { "36-36", "38-38", "35-35", "37-37", "42-42" }, AweKeys(z), "the bank's drum keys, then the GS kit's other keys");
            Eq(1, AweGen(z[4], 57), "hi-hat key group -> exclusiveClass");
            No(AweHas(z[3], 57), "no exclusiveClass without key group");
            Eq("GS001", f.Sample(AweGen(z[4], 53)).Name, "hi-hat: its own wave");
        }

        [Test] static void Awe_SbkConversions()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0);
            s.Simple("Conv", 0, 0,
                new[] { 43, AweKr(0, 9), 8, 127, 9, 100, 5, 64, 6, -64, 7, 1, 10, 64, 11, -32, 13, 32, 15, 255, 16, -10, 17, 0,
                    21, 1000, 23, 0, 34, 3000, 36, 10, 38, 30000, 22, 1, 24, 0, 29, 48, 37, 10, 31, 12, 40, -120, 48, 127, 57, 3, 54, 0, 53, a },
                new[] { 43, AweKr(10, 19), 8, 50, 9, 1000, 15, 300, 17, 127, 29, 96, 37, 0, 48, 0, 22, 0, 40, 32767, 31, -32768, 6, 0, 13, -64, 53, a },
                new[] { 43, AweKr(20, 29), 8, 0, 48, 64, 17, 64, 16, 256, 15, 0, 37, 95, 29, 95, 53, a });
            AweSf2 f = AweBuild(s, null);
            List<List<int[]>> z = f.ZonesOf(0, 0);
            Eq(3, z.Count, "zones");
            // filter cutoff 127 -> 14400 then clamped to 13500; Q * 3/2; LFO / envelope to pitch (1200 v / 64 + 1) / 2, to filter
            // 3600 / 7200 cents per 64, to volume 120 cB per 64; sends v * 1000 / 256; pan v * 1000 / 127 - 500; times (ms) in
            // timecents, 0 ms as 1 ms (-11958); LFO frequency 1 -> -7925 cents, 0 -> -725 (mod) / -15600 (vib); sustains below
            // 96: (96 - v) * 1000 / 96 (mod), (2000 - 21 v) / 2 (vol); key scaling v * 5.55 / 12; attenuation 127 -> 0 cB
            AweGens(z[0], "zone 0", 43, AweKr(0, 9), 8, 13500, 9, 150, 5, 600, 6, -600, 7, 9, 10, 3600, 11, -3600, 13, 60, 15, 996, 16, 0, 17, -500,
                21, 0, 23, -11958, 34, 1901, 36, -7972, 38, 5888, 22, -7925, 24, -15600, 29, 500, 37, 895, 31, 5, 40, -55, 48, 0, 57, 3, 54, 0, 58, 60);
            // cutoff 59 v + 4366; clamps: Q 960, send 1000, key scaling +-1200; attenuation 0 -> 1000 cB; sustain 96 -> 0, vol 0 -> 1000
            AweGens(z[1], "zone 1", 43, AweKr(10, 19), 8, 7316, 9, 960, 15, 1000, 17, 500, 29, 0, 37, 1000, 48, 1000, 22, -725, 40, 1200, 31, -1200,
                6, 0, 13, -120, 24, -15600, 58, 60);
            // attenuation 64 -> -2000 log10(64 / 127) = 595 cB (awesfx's formula); pan 64 -> 3; sustains 95 -> 10 / 2
            AweGens(z[2], "zone 2", 43, AweKr(20, 29), 8, 4366, 48, 595, 17, 3, 16, 1000, 15, 0, 37, 2, 29, 10, 22, -725, 24, -15600, 58, 60);
            Eq(1, f.SampleCount, "one sample for the three zones");
        }

        [Test] static void Awe_SbkDefaultsAndDroppedGenerators()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0);
            s.Simple("Bare", 0, 0, new[] { 43, AweKr(0, 63), 53, a },
                // unused / reserved SF1 generators and scaleTuning 0: dropped; address offsets: kept as they are
                new[] { 43, AweKr(64, 127), 14, 7, 18, 7, 19, 7, 20, 7, 42, 7, 49, 7, 59, 7, 56, 0, 0, 4, 12, -2, 53, a });
            AweSf2 f = AweBuild(s, null);
            List<List<int[]>> z = f.ZonesOf(0, 0);
            Eq(2, z.Count, "zones");
            // SBK defaults that differ from SF2's: both envelopes sustain at zero, LFO rates -725 / -15600 cents; root key 60
            AweGens(z[0], "bare zone", 43, AweKr(0, 63), 29, 1000, 37, 1000, 22, -725, 24, -15600, 58, 60);
            AweGens(z[1], "zone with dropped generators", 43, AweKr(64, 127), 0, 4, 12, -2, 29, 1000, 37, 1000, 22, -725, 24, -15600, 58, 60);
        }

        [Test] static void Awe_RootKeyAndTuning()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0);
            // SF1 samplePitch (55, cents), overridingRootKey (58), coarse (51) / fine (52) tune, scaleTuning flag (56: 50 cents a key)
            int[][] cases = {
                new int[0],
                new[] { 55, 6250 },                 // 62.50 -> root 62, tune -50 -> root 63, +50
                new[] { 55, 6020 },
                new[] { 55, 6000, 58, 72 },         // root + (72 - 60)
                new[] { 51, 2, 52, -30 },           // +170 cents -> coarse 1, fine 70
                new[] { 56, 1, 51, 3 },             // 3 keys of 50 cents
                new[] { 56, 1, 55, 6030 },          // sample tune halved too (awesfx)
                new[] { 55, 9000, 58, 127 },        // 90 + 67: clamped to 127
                new[] { 51, -1, 52, -50 },          // -150 -> coarse -1, fine -50
                new[] { 58, 30 },
                new[] { 55, 6075 },
                new[] { 55, 5990 },
            };
            string[] expected = {
                "root 60", "root 63 fine 50", "root 60 fine -20", "root 72", "root 60 coarse 1 fine 70", "root 60 coarse 1 fine 50 scale 50",
                "root 60 fine -15 scale 50", "root 127", "root 60 coarse -1 fine -50", "root 30", "root 61 fine 25", "root 60 fine 10",
            };
            int[][] zones = new int[cases.Length][];
            for (int i = 0; i < cases.Length; i++) zones[i] = new[] { 43, AweKr(10 * i, 10 * i + 9) }.Concat(cases[i]).Concat(new[] { 53, a }).ToArray();
            s.Simple("Tuning", 0, 0, zones);
            AweSf2 f = AweBuild(s, null);
            List<List<int[]>> z = f.ZonesOf(0, 0);
            Eq(cases.Length, z.Count, "zones");
            EqList(expected, z.Select(x => AweTune(x)).ToList(), "root key (58), coarse (51), fine (52), scale tuning (56)");
            Eq(1, f.SampleCount, "one sample");
            Eq(60, f.Sample(0).Pitch, "the sample's own pitch stays 60: each zone's root key says it");
            Eq(0, f.Sample(0).Corr, "no pitch correction in the sample");
        }

        [Test] static void Awe_DlsZone()
        {
            AweDls d = new AweDls();
            int[] pcm0 = AweSeq(0, 32, AweRamWord), pcm1 = AweSeq(500, 24, AweRomWord);
            int w0 = d.Wave(22050, pcm0, AweDls.Wsmp(60, 0, 0, -1, 0));
            int w1 = d.Wave(11025, pcm1, AweDls.Wsmp(72, 0, 0, -1, 0));
            const int M = 65536;
            byte[] own = AweDls.Art(
                0, 0, 0x206, int.MinValue,      // EG1 attack, -infinity (0 s) -> attackVolEnv -12000
                0, 0, 0x207, 1200 * M,          // EG1 decay -> decayVolEnv
                0, 0, 0x209, int.MinValue,      // EG1 release -infinity: -32768, clamped to -12000
                0, 0, 0x20a, 500 * M,           // EG1 sustain 50.0 % -> sustainVolEnv -200 log10(0.5) = 60 cB
                0, 0, 0x30a, -2400 * M,         // EG2 attack -> attackModEnv
                0, 0, 0x30b, 600 * M,           // EG2 decay -> decayModEnv
                0, 0, 0x30d, 0,                 // EG2 release -> releaseModEnv
                0, 0, 0x30e, 250 * M,           // EG2 sustain 25.0 % -> sustainModEnv 750 (0.1 % below full)
                0, 0, 0x104, -500 * M,          // LFO frequency -> freqModLFO
                0, 0, 0x105, int.MinValue,      // LFO delay -infinity -> delayModLFO -12000
                0, 0, 4, -250 * M,              // pan
                1, 0, 3, 50 * M,                // LFO -> pitch: modLfoToPitch
                1, 0, 1, 20 * M,                // LFO -> attenuation: modLfoToVolume
                5, 0, 3, 100 * M,               // EG2 -> pitch: modEnvToPitch
                2, 0, 1, -960 * M);             // velocity -> attenuation: left to the SF2 default modulator
            byte[] inst = AweDls.Art(
                3, 0, 0x207, -12800 * M,        // key number -> EG1 decay (scale * key / 128), before the decay itself
                0, 0, 0x207, 1200 * M,
                3, 0, 0x30b, 2560 * M);         // key number -> EG2 decay, without EG2 decay (taken as -12000)
            d.Ins("GS Test", 0, 20, inst,
                AweDls.Rgn(30, 90, 10, 200, 2, AweDls.Wsmp(64, -12, -10 * M, 4, 8), w0, own),
                AweDls.Rgn(91, 127, 0, 127, 0, null, w1, null));
            AweSf2 f = AweBuild(new AweSbk(), d);
            List<List<int[]>> z = f.ZonesOf(0, 20);
            Eq(2, z.Count, "one zone per region");
            // velocity range top clamped to 127; wsmp: unity note -> root key, fine tune, gain -10 cB -> attenuation 10, loop,
            // key group -> exclusiveClass; the region's own articulation replaces the instrument's
            AweGens(z[0], "region with its own wsmp and articulation",
                43, AweKr(30, 90), 44, AweKr(10, 127), 58, 64, 52, -12, 48, 10, 54, 1, 57, 2,
                34, -12000, 36, 1200, 38, -12000, 37, 60, 26, -2400, 28, 600, 30, 0, 29, 750,
                22, -500, 21, -12000, 17, -250, 5, 50, 13, 20, 7, 100);
            // key number -> decay: keynumToVolEnvDecay = -scale / 128 = 100, decay + scale * 60 / 128 = 1200 - 6000;
            // EG2: keynumToModEnvDecay -20, decayModEnv -12000 + 1200. The wave's own wsmp: unity 72, no loop, no gain.
            AweGens(z[1], "region with the instrument's articulation and its wave's wsmp",
                43, AweKr(91, 127), 44, AweKr(0, 127), 58, 72, 52, 0, 36, -4800, 40, 100, 28, -10800, 32, -20);
            AweSmp s0 = f.Sample(AweGen(z[0], 53)), s1 = f.Sample(AweGen(z[1], 53));
            Eq("GS000", s0.Name, "sample name: GS + the region's wave table index");
            Eq(22050, s0.Rate, "rate from fmt");
            EqList(pcm0, f.Pcm(AweGen(z[0], 53)), "first wave's data");
            Eq(4, s0.LoopStart - s0.Start, "loop start (wsmp)");
            Eq(12, s0.LoopEnd - s0.Start, "loop end = start + length");
            Eq(60, s0.Pitch, "original pitch");
            Eq(1, s0.Type, "mono sample");
            Eq("GS001", s1.Name, "second wave");
            Eq(11025, s1.Rate, "second wave's rate");
            EqList(pcm1, f.Pcm(AweGen(z[1], 53)), "second wave's data");
            Eq(s1.Start, s1.LoopStart, "no loop: loop start = start");
            Eq(s1.Start, s1.LoopEnd, "no loop: loop end = start");
        }

        [Test] static void Awe_DlsEdgeValues()
        {
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            const int M = 65536;
            d.Ins("GS Edges", 0, 30, null,
                // positive gain (louder): attenuation 0; sustain 100 % -> 0 cB; EG2 sustain 0 % -> 1000; pan clamped to 500
                AweDls.Rgn(0, 9, 0, 127, 0, AweDls.Wsmp(60, 0, 10 * M, -1, 0), w, AweDls.Art(0, 0, 0x20a, 1000 * M, 0, 0, 0x30e, 0, 0, 0, 4, 900 * M)),
                // gain -200 dB: clamped to 1440 cB; sustain 0 % -> 1440; EG2 sustain 100 % -> 0; pan -500
                AweDls.Rgn(10, 19, 0, 127, 0, AweDls.Wsmp(60, 0, -2000 * M, -1, 0), w, AweDls.Art(0, 0, 0x20a, 0, 0, 0, 0x30e, 1000 * M, 0, 0, 4, -900 * M)),
                // gain under 1 cB: no attenuation generator; sustain above 100 % -> 0; a finite attack time as it is
                AweDls.Rgn(20, 29, 0, 127, 0, AweDls.Wsmp(60, 0, -M / 2, -1, 0), w, AweDls.Art(0, 0, 0x20a, 1500 * M, 0, 0, 0x206, 100 * M)));
            AweSf2 f = AweBuild(new AweSbk(), d);
            List<List<int[]>> z = f.ZonesOf(0, 30);
            Eq(3, z.Count, "zones");
            AweGens(z[0], "louder", 43, AweKr(0, 9), 44, AweKr(0, 127), 58, 60, 52, 0, 48, 0, 37, 0, 29, 1000, 17, 500);
            AweGens(z[1], "much quieter", 43, AweKr(10, 19), 44, AweKr(0, 127), 58, 60, 52, 0, 48, 1440, 37, 1440, 29, 0, 17, -500);
            AweGens(z[2], "half a cB", 43, AweKr(20, 29), 44, AweKr(0, 127), 58, 60, 52, 0, 37, 0, 34, 100);
            Eq(1, f.SampleCount, "same wave, no loop in any wsmp: one sample");
        }

        [Test] static void Awe_DlsCueTable()
        {
            AweDls d = new AweDls();
            int[] a = AweSeq(0, 10, AweRamWord), b = AweSeq(1000, 14, AweRomWord);
            d.Wave(8000, a, AweDls.Wsmp(60, 0, 0, -1, 0));
            d.Wave(16000, b, AweDls.Wsmp(60, 0, 0, -1, 0));
            d.Cues = new[] { 1, 0 };            // ptbl: entry 0 is the second wave, entry 1 the first
            d.Ins("GS One", 0, 1, null, AweDls.Rgn(0, 127, 0, 127, 0, null, 0, null));
            d.Ins("GS Two", 0, 2, null, AweDls.Rgn(0, 127, 0, 127, 0, null, 1, null));
            AweSf2 f = AweBuild(new AweSbk(), d);
            int one = AweGen(f.ZonesOf(0, 1)[0], 53), two = AweGen(f.ZonesOf(0, 2)[0], 53);
            Eq(16000, f.Sample(one).Rate, "wlnk table index 0 -> ptbl entry 0 -> the second wave");
            EqList(b, f.Pcm(one), "its data");
            Eq("GS000", f.Sample(one).Name, "named after the table index");
            Eq(8000, f.Sample(two).Rate, "wlnk table index 1 -> the first wave");
            EqList(a, f.Pcm(two), "its data");
            Eq("GS001", f.Sample(two).Name, "named after the table index");
        }

        [Test] static void Awe_SampleSharing()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 120, 180);
            s.Simple("P1", 0, 0, new[] { 43, AweKr(0, 63), 53, a }, new[] { 43, AweKr(64, 127), 54, 1, 53, a });
            s.Simple("P2", 1, 0, new[] { 53, a });                  // as P1's first zone
            s.Simple("P3", 2, 0, new[] { 54, 1, 53, a });           // as P1's second
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 40, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            d.Ins("GS Loops", 0, 3, null,
                AweDls.Rgn(0, 63, 0, 127, 0, AweDls.Wsmp(60, 0, 0, 4, 8), w, null),
                AweDls.Rgn(64, 127, 0, 127, 0, AweDls.Wsmp(60, 0, 0, 2, 10), w, null),
                AweDls.Rgn(0, 127, 0, 127, 0, AweDls.Wsmp(66, 5, 0, 4, 8), w, null));    // the first loop again, other tuning
            AweSf2 f = AweBuild(s, d);
            List<List<int[]>> p1 = f.ZonesOf(0, 0), g = f.ZonesOf(0, 3);
            Eq(AweGen(p1[0], 53), AweGen(f.ZonesOf(0, 1)[0], 53), "same SBK sample, unlooped twice: one sample");
            Eq(AweGen(p1[1], 53), AweGen(f.ZonesOf(0, 2)[0], 53), "same SBK sample, looped twice: one sample");
            Ok(AweGen(p1[0], 53) != AweGen(p1[1], 53), "looped and unlooped: two samples");
            Eq(AweGen(g[0], 53), AweGen(g[2], 53), "same wave, same loop: one sample (tuning stays in the zone)");
            Ok(AweGen(g[0], 53) != AweGen(g[1], 53), "same wave, other loop: a sample of its own");
            Eq(66, AweGen(g[2], 58), "the shared sample's zone keeps its own unity note");
            AweSmp l1 = f.Sample(AweGen(g[0], 53)), l2 = f.Sample(AweGen(g[1], 53));
            Eq(4, l1.LoopStart - l1.Start, "first loop start"); Eq(12, l1.LoopEnd - l1.Start, "first loop end");
            Eq(2, l2.LoopStart - l2.Start, "second loop start"); Eq(12, l2.LoopEnd - l2.Start, "second loop end");
            EqList(f.Pcm(AweGen(g[0], 53)), f.Pcm(AweGen(g[1], 53)), "both: the wave's data");
            Eq(4, f.SampleCount, "samples: A unlooped, A looped, the wave with two loops");
        }

        [Test] static void Awe_Names()
        {
            AweSbk s = new AweSbk();
            s.Smpl = new byte[16];
            int a = s.Sample("*Rom Sample", 100, 200, 0, 0), b = s.Sample(" Ram  ", 0, 8, 0, 0);
            s.Simple("  Grand Piano  ", 0, 0, new[] { 43, AweKr(0, 63), 53, a }, new[] { 43, AweKr(64, 127), 53, b });
            s.Simple("Caf\u00e9 Organ", 16, 0, new[] { 53, a });
            s.Simple("ABCDEFGHIJKLMNOPQRST", 17, 0, new[] { 53, a });      // 20 characters: no room for a zero
            AweDls d = new AweDls();
            int w = d.Wave(22050, AweSeq(0, 20, AweRamWord), AweDls.Wsmp(60, 0, 0, -1, 0));
            d.Ins("GS Instrument With A Long Name", 0, 18, null, AweDls.Rgn(0, 127, 0, 127, 0, null, w, null));
            AweSf2 f = AweBuild(s, d);
            EqList(new[] { "0:0 Grand Piano", "0:16 Caf\u00e9 Organ", "0:17 ABCDEFGHIJKLMNOPQRST", "0:18 GS Instrument With A" }, AwePresets(f),
                "preset names: SBK names trimmed, DLS name cut to 20 characters");
            EqBytes(AweCat(new byte[] { 0x43, 0x61, 0x66, 0xE9 }, Encoding.ASCII.GetBytes(" Organ"), new byte[10]), f.Raw(f.Off("phdr") + 38, 20),
                "code page 1252 name, zero-filled");
            EqBytes(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRST"), f.Raw(f.Off("phdr") + 76, 20), "20-character name: the whole field");
            foreach (AwePre p in f.Presets()) Eq(p.Name, f.InstName(p.Inst), "instrument named as its preset");
            List<List<int[]>> z = f.ZonesOf(0, 0);
            Eq("Rom Sample", f.Sample(AweGen(z[0], 53)).Name, "ROM sample name");
            Eq("Ram", f.Sample(AweGen(z[1], 53)).Name, "RAM sample name, trimmed");
        }

        [Test] static void Awe_GlobalZonesMakeNoZone()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0);
            int i = s.Inst("Layered", new[] { 17, 30, 48, 20 }, new[] { 43, AweKr(0, 127), 53, a });   // first zone global (no sampleID)
            s.Preset("Layered", 0, 0, new[] { 48, 10 }, new[] { 41, i });                                // first zone global (no instrument)
            AweSf2 f = AweBuild(s, null);
            EqList(new[] { "0:0 Layered" }, AwePresets(f), "presets");
            List<List<int[]>> z = f.ZonesOf(0, 0);
            EqList(new[] { "0-127" }, AweKeys(z), "only the zone with a sample makes a zone");
            EqList(AweSeq(100, 100, AweRomWord), f.Pcm(AweGen(z[0], 53)), "its sample");
            Eq(1, f.Count("pbag", 4) - 1, "one preset zone");
        }

        [Test] static void Awe_EmptyInstrument()
        {
            AweSbk s = new AweSbk();
            int bad = s.Sample("*Bad", 600000, 600100, 0, 0);     // past the 1 MB ROM
            s.Simple("Ghost", 7, 0, new[] { 53, bad });
            AweSf2 f = AweBuild(s, null);
            EqList(new[] { "0:7 Ghost" }, AwePresets(f), "the preset is still there");
            Eq(0, f.ZonesOf(0, 7).Count, "without zone");
            Eq(0, f.SampleCount, "no sample");
            Eq(0, f.Size("smpl"), "empty smpl");
            Eq(1, f.Count("ibag", 4), "ibag: the terminal only");
            Eq(1, f.Count("igen", 4), "igen: the terminal only");
        }

        [Test] static void Awe_PresetOfTwoInstruments()
        {
            AweSbk s = new AweSbk();
            int a = s.Sample("*A", 100, 200, 0, 0), b = s.Sample("*B", 300, 400, 0, 0);
            int lo = s.Inst("Low", new[] { 43, AweKr(0, 59), 53, a });
            int hi = s.Inst("High", new[] { 43, AweKr(60, 127), 53, b }, new[] { 43, AweKr(60, 127), 44, AweKr(100, 127), 53, a });
            s.Preset("Split", 0, 0, new[] { 41, lo }, new[] { 41, hi });
            AweSf2 f = AweBuild(s, null);
            EqList(new[] { "0-59", "60-127", "60-127" }, AweKeys(f.ZonesOf(0, 0)), "the zones of both instruments, in order, in one instrument");
            Eq(1, f.InstCount, "one SF2 instrument per preset");
            Eq("Split", f.InstName(0), "named after the preset");
        }

        [Test] static void Awe_SameInputSameBank()
        {
            AweSbk s; AweDls d;
            AweStdInputs(out s, out d);
            byte[] sb = s.Image(), db = d.Image(), rom = AweRomImage(), romCopy = (byte[])rom.Clone();
            string sp = PutFile(Path.Combine(TestDir("game", "TNOVA", "SOUND"), "FF.SBK"), sb);
            string dp = PutFile(Path.Combine(TestDir("windows", "drivers"), "gm.dls"), db);
            byte[] one = AweBank.Build(sp, dp, rom), two = AweBank.Build(sp, dp, rom);
            EqBytes(one, two, "two builds of the same files");
            EqBytes(sb, File.ReadAllBytes(sp), "FF.SBK left as it was");
            EqBytes(db, File.ReadAllBytes(dp), "gm.dls left as it was");
            EqBytes(romCopy, rom, "ROM left as it was");
        }
    }
}
