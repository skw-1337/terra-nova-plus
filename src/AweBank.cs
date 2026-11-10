// Terra Nova Plus - AWE32 music.
// The game ships its own Sound Blaster AWE32 bank, SOUND\FF.SBK ("TerraNova GM" by Eric Brosius, SoundFont 1).
// Nearly all of its instruments use samples from the AWE32's ROM, so it needs awe32.raw (a 1 MB dump of that
// ROM, as used by 86Box) next to TNPlus.exe. This builds a SoundFont 2 that DOSBox Staging's FluidSynth can play.
//
// What the card played was read from the game's own driver, SOUND\SBAWE32.MDI (Miles, with Creative's AWE32
// library in it), run in an emulator against a made-up EMU8000:
//  - the game loads FF.SBK as the card's only bank (bank 0; the driver's built-in General MIDI set is never
//    installed). A program FF.SBK doesn't have plays its first preset (the piano), a key without a zone is silent:
//    the music asks for program 123 (bird tweet), dulcimer keys 48-49 and drum keys 69, 70, 84, 87, which the
//    card never played as written;
//  - pitch: cents = (key - rootKey + coarseTune + 60) * 100 - samplePitch + fineTune, halved when scaleTuning is 1
//    (awesfx has the same); loop = [loop start, loop end + 1) in ROM addresses; a zone's generators replace the
//    instrument's and preset's, they are not added;
//  - a few of the bank's instruments are out of tune on the card too: its sine wave zones (whistle, low
//    recorder) are 21 cents sharp, and its own samples (piano bass, fretless and acoustic bass, square wave,
//    distortion guitar...) are 5 to 20 cents off their root key, on loops the driver makes 2 samples too long.
// By default the bank is completed and tuned: what FF.SBK lacks comes from the AWE32's own General MIDI set,
// which that same driver holds (Creative's presets for the ROM, read from the player's SBAWE32.MDI, nothing of it
// is in this file), and the zones above are brought back in tune. "exact" gives the card as it was.
// Without SBAWE32.MDI the gaps are filled from the Roland GS set of Windows (gm.dls), as before.
//
// The SoundFont 1 -> SoundFont 2 conversion follows awesfx (sbkconv.c, parsesf.c, sffile.c),
// Copyright (C) 1996-1999 Takashi Iwai, so unlike the rest of Terra Nova Plus (MIT) this file is
// under the GNU General Public License:
//
// This program is free software; you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation; either version 2 of the
// License, or (at your option) any later version. This program is distributed in the hope that it
// will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
// FITNESS FOR A PARTICULAR PURPOSE. See GPL-2.0.txt.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

static class AweBank
{
    public const int ROM_SIZE = 1048576;

    // ------------------------------------------------------------------ RIFF
    class Chunk { public string Id; public int Off, Size; }
    static List<Chunk> Chunks(byte[] b, int off, int end)
    {
        List<Chunk> res = new List<Chunk>();
        while (off + 8 <= end)
        {
            int sz = BitConverter.ToInt32(b, off + 4);
            res.Add(new Chunk { Id = Encoding.ASCII.GetString(b, off, 4), Off = off + 8, Size = sz });
            off += 8 + sz + (sz & 1);
        }
        return res;
    }
    static string ListType(byte[] b, Chunk c) { return c.Id == "LIST" ? Encoding.ASCII.GetString(b, c.Off, 4) : null; }
    static string Str(byte[] b, int off, int n)
    {
        int e = off; while (e < off + n && b[e] != 0) e++;
        return Encoding.GetEncoding(1252).GetString(b, off, e - off).Trim();
    }
    static int FloorDiv(int a, int b) { int q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }

    // ------------------------------------------------------------------ SoundFont 1 (FF.SBK, the driver's own set)
    class Gen { public int Id, Val; public Gen(int id, int val) { Id = id; Val = val; } }
    class Sbk
    {
        public byte[] Smpl = new byte[0];
        public List<string> Names = new List<string>();
        public List<int[]> Shdr = new List<int[]>();       // start, end, startloop, endloop
        public List<object[]> Phdr = new List<object[]>(); // name, prog, bank, bag
        public List<int> Pbag = new List<int>(), Ibag = new List<int>(), InstBag = new List<int>();
        public List<Gen> Pgen = new List<Gen>(), Igen = new List<Gen>();
        public Dictionary<int, int> Native = new Dictionary<int, int>();   // the driver's set: ROM sample start -> its pitch in cents
        public int Find(int bank, int prog)
        {
            for (int i = 0; i < Phdr.Count - 1; i++) if ((int)Phdr[i][2] == bank && (int)Phdr[i][1] == prog) return i;
            return -1;
        }
    }
    static Sbk LoadSbk(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        Dictionary<string, Chunk> C = new Dictionary<string, Chunk>();
        foreach (Chunk c in Chunks(d, 12, d.Length))
            foreach (Chunk c2 in Chunks(d, c.Off + 4, c.Off + c.Size)) C[c2.Id] = c2;
        Sbk s = new Sbk();
        Chunk k = C["smpl"]; s.Smpl = new byte[k.Size]; Buffer.BlockCopy(d, k.Off, s.Smpl, 0, k.Size);
        k = C["snam"]; for (int i = 0; i < k.Size / 20; i++) s.Names.Add(Str(d, k.Off + i * 20, 20));
        k = C["shdr"];
        for (int i = 0; i < k.Size / 16; i++)
        {
            int o = k.Off + i * 16;
            s.Shdr.Add(new[] { BitConverter.ToInt32(d, o), BitConverter.ToInt32(d, o + 4), BitConverter.ToInt32(d, o + 8), BitConverter.ToInt32(d, o + 12) });
        }
        k = C["phdr"];
        for (int i = 0; i < k.Size / 38; i++)
        {
            int o = k.Off + i * 38;
            s.Phdr.Add(new object[] { Str(d, o, 20), (int)BitConverter.ToUInt16(d, o + 20), (int)BitConverter.ToUInt16(d, o + 22), (int)BitConverter.ToUInt16(d, o + 24) });
        }
        k = C["pbag"]; for (int i = 0; i < k.Size / 4; i++) s.Pbag.Add(BitConverter.ToUInt16(d, k.Off + i * 4));
        k = C["ibag"]; for (int i = 0; i < k.Size / 4; i++) s.Ibag.Add(BitConverter.ToUInt16(d, k.Off + i * 4));
        k = C["pgen"]; for (int i = 0; i < k.Size / 4; i++) s.Pgen.Add(new Gen(BitConverter.ToUInt16(d, k.Off + i * 4), BitConverter.ToInt16(d, k.Off + i * 4 + 2)));
        k = C["igen"]; for (int i = 0; i < k.Size / 4; i++) s.Igen.Add(new Gen(BitConverter.ToUInt16(d, k.Off + i * 4), BitConverter.ToInt16(d, k.Off + i * 4 + 2)));
        k = C["inst"]; for (int i = 0; i < k.Size / 22; i++) s.InstBag.Add(BitConverter.ToUInt16(d, k.Off + i * 22 + 20));
        return s;
    }
    static List<List<Gen>> Zones(List<int> bags, List<Gen> gens, int a, int b)
    {
        List<List<Gen>> res = new List<List<Gen>>();
        for (int z = a; z < b; z++)
        {
            int g0 = bags[z], g1 = z + 1 < bags.Count ? bags[z + 1] : gens.Count;
            res.Add(gens.GetRange(g0, g1 - g0));
        }
        return res;
    }
    static Dictionary<int, int> ToDict(List<Gen> z)
    {
        Dictionary<int, int> d = new Dictionary<int, int>();
        foreach (Gen g in z) d[g.Id] = g.Val;
        return d;
    }
    static int Get(Dictionary<int, int> d, int k, int def) { int v; return d.TryGetValue(k, out v) ? v : def; }

    // The AWE32's General MIDI set inside SBAWE32.MDI (Creative's "embedded" presets for the ROM): between the words
    // 0x1234 and 0x5678, the tables of a SoundFont 1 bank in the driver's compact form: presets (program, bank with
    // 0x80 for the drum kit, first zone), zone -> first generator, generators of 3 bytes (id, 16-bit amount), instrument
    // -> first zone, zones, generators, samples (4 x 32 bits). The driver keeps generators 5 to 38 in EMU8000 units
    // (its loader converts a bank once): brought back to SoundFont 1 units here, with the driver's own time tables.
    static Sbk LoadEmbedded(string path, Sbk names)
    {
        try
        {
            byte[] d = File.ReadAllBytes(path);
            int a = Find(d, new byte[] { 0x34, 0x12, 0, 0, 0, 0 }, 0);
            int[] atk = WordTable(d, new[] { 11878, 5939, 3959, 2970 }), dcy = WordTable(d, new[] { 47513, 23756, 15838, 11878 });
            if (a < 0 || atk == null || dcy == null) return null;
            Sbk s = new Sbk();
            int o = a + 2;
            while (true)
            {
                int w = BitConverter.ToUInt16(d, o), bag = BitConverter.ToUInt16(d, o + 2);
                o += 4;
                bool end = w == 0xff80;
                int bank = w >> 8, prog = w & 0xff;
                s.Phdr.Add(new object[] { end ? "EOP" : bank == 0x80 ? "Standard" : "Program " + prog, prog, bank == 0x80 ? 128 : bank, bag });
                if (end) break;
                if (s.Phdr.Count > 4096) return null;
            }
            int nb = (int)s.Phdr[s.Phdr.Count - 1][3] + 1;
            for (int i = 0; i < nb; i++) s.Pbag.Add(BitConverter.ToUInt16(d, o + 2 * i));
            o += 2 * nb;
            int ng = s.Pbag[nb - 1], maxInst = 0;
            for (int i = 0; i < ng; i++)
            {
                Gen g = new Gen(d[o + 3 * i], FromReg(d[o + 3 * i], BitConverter.ToInt16(d, o + 3 * i + 1), atk, dcy));
                if (g.Id == 41) maxInst = Math.Max(maxInst, g.Val);
                s.Pgen.Add(g);
            }
            o += 3 * ng; o += o & 1;
            for (int i = 0; i < maxInst + 2; i++) s.InstBag.Add(BitConverter.ToUInt16(d, o + 2 * i));
            o += 2 * (maxInst + 2);
            int nib = s.InstBag[maxInst + 1] + 1;
            for (int i = 0; i < nib; i++) s.Ibag.Add(BitConverter.ToUInt16(d, o + 2 * i));
            o += 2 * nib;
            int nig = s.Ibag[nib - 1], maxSmp = 0;
            for (int i = 0; i < nig; i++)
            {
                Gen g = new Gen(d[o + 3 * i], FromReg(d[o + 3 * i], BitConverter.ToInt16(d, o + 3 * i + 1), atk, dcy));
                if (g.Id == 53) { g.Val &= 0xffff; maxSmp = Math.Max(maxSmp, g.Val); }
                s.Igen.Add(g);
            }
            o += 3 * nig; o += o & 1;
            Dictionary<int, string> known = new Dictionary<int, string>();
            if (names != null)
                for (int i = 0; i < names.Shdr.Count && i < names.Names.Count; i++)
                    if (names.Names[i].StartsWith("*")) known[names.Shdr[i][0]] = names.Names[i];
            for (int i = 0; i <= maxSmp; i++)
            {
                int[] h = { BitConverter.ToInt32(d, o + 16 * i), BitConverter.ToInt32(d, o + 16 * i + 4), BitConverter.ToInt32(d, o + 16 * i + 8), BitConverter.ToInt32(d, o + 16 * i + 12) };
                s.Shdr.Add(h);
                string n;
                s.Names.Add(known.TryGetValue(h[0], out n) ? n : "*ROM" + h[0].ToString("X5"));
            }
            o += 16 * (maxSmp + 1);
            if (BitConverter.ToUInt16(d, o) != 0x5678) return null;
            // each ROM sample's own pitch: the samplePitch of the zones that play it
            for (int z = 0; z + 1 < s.Ibag.Count; z++)
            {
                Dictionary<int, int> zd = ToDict(s.Igen.GetRange(s.Ibag[z], s.Ibag[z + 1] - s.Ibag[z]));
                if (zd.ContainsKey(53) && zd.ContainsKey(55) && !zd.ContainsKey(58) && !zd.ContainsKey(51) && !zd.ContainsKey(52)
                    && !s.Native.ContainsKey(s.Shdr[zd[53]][0]))
                    s.Native[s.Shdr[zd[53]][0]] = zd[55];
            }
            return s.Phdr.Count > 1 ? s : null;
        }
        catch { return null; }
    }
    static int Find(byte[] hay, byte[] needle, int from)
    {
        for (int i = from; i <= hay.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
    static int[] WordTable(byte[] d, int[] first)
    {
        byte[] n = new byte[first.Length * 2];
        for (int i = 0; i < first.Length; i++) { n[2 * i] = (byte)first[i]; n[2 * i + 1] = (byte)(first[i] >> 8); }
        int at = Find(d, n, 0);
        if (at < 0 || at + 256 > d.Length) return null;
        int[] t = new int[128];
        for (int i = 0; i < 128; i++) t[i] = BitConverter.ToUInt16(d, at + 2 * i);
        return t;
    }
    // EMU8000 units -> SoundFont 1 units, the reverse of the driver's loader (0x4502): the times through its tables
    // (attack: the first step whose time is under the one asked; decay and release the same from step 0)
    static int FromReg(int g, int v, int[] atk, int[] dcy)
    {
        switch (g)
        {
            case 8: return v >= 255 ? 127 : v / 2;
            case 10: case 11: case 13: case 22: case 24: return v / 2;
            case 9: case 14: return v * 8;
            case 21: case 23: case 25: case 33: return (int)Math.Round(((0x8000 - (v & 0xffff)) & 0xffff) * 0.725);
            case 26: case 34: return v >= 127 ? 6 : atk[Math.Max(1, v) - 1] + 1;
            case 27: return (127 - Math.Max(0, Math.Min(127, v))) * 92;
            case 28: case 30: case 38: return v >= 127 ? dcy[127] : dcy[Math.Max(0, v)] + 1;
            case 29: case 37: return (Math.Max(0, Math.Min(127, v)) * 75 + 99) / 100;
        }
        return v;
    }

    // One sounding zone of a preset as the driver builds it at note-on (0x1a76): the generators of the preset zone,
    // then the instrument's global zone, then the zone, each replacing the one before (nothing is added); the key must
    // be in the preset zone's range and in the zone's. Velocity ranges are not looked at.
    class Layer { public Dictionary<int, int> Z; public int Lo, Hi; }
    static List<Layer> Layers(Sbk S, int pi)
    {
        List<Layer> res = new List<Layer>();
        List<Gen> pglob = new List<Gen>();
        foreach (List<Gen> pz in Zones(S.Pbag, S.Pgen, (int)S.Phdr[pi][3], (int)S.Phdr[pi + 1][3]))
        {
            Dictionary<int, int> pg = ToDict(pz);
            if (!pg.ContainsKey(41)) { pglob.AddRange(pz); continue; }
            int inst = pg[41];
            if (inst < 0 || inst + 1 >= S.InstBag.Count) continue;
            int pr = Get(pg, 43, 0x7f00) & 0xffff;
            List<Gen> iglob = new List<Gen>();
            foreach (List<Gen> iz in Zones(S.Ibag, S.Igen, S.InstBag[inst], S.InstBag[inst + 1]))
            {
                Dictionary<int, int> z = ToDict(iz);
                if (!z.ContainsKey(53)) { iglob.AddRange(iz); continue; }
                int zr = Get(z, 43, 0x7f00) & 0xffff;
                Layer l = new Layer { Z = new Dictionary<int, int>(), Lo = Math.Max(pr & 0xff, zr & 0xff), Hi = Math.Min(Math.Min(pr >> 8, zr >> 8), 127) };
                foreach (List<Gen> part in new[] { pglob, pz, iglob, iz })
                    foreach (Gen x in part) if (x.Id != 41 && x.Id != 43) l.Z[x.Id] = x.Val;
                res.Add(l);
            }
        }
        return res;
    }

    // SF1 values -> SF2 (awesfx sbkconv.c)
    static int Tc(int ms) { return (int)(Math.Log(Math.Max(ms, 1) / 1000.0) / Math.Log(2.0) * 1200.0); }
    static int Conv(int g, int v)
    {
        switch (g)
        {
            case 5: case 6: case 7: return v >= 0 ? (1200 * v / 64 + 1) / 2 : -((1200 * -v / 64 + 1) / 2);
            case 8: return v == 127 ? 14400 : 59 * v + 4366;
            case 9: return FloorDiv(v * 3, 2);
            case 10: case 11: return (int)(1200.0 * (g == 10 ? 3 : 6) * v / 64);
            case 13: return (int)(120.0 * v / 64);
            case 15: case 16: return FloorDiv(v * 1000, 256);
            case 17: return FloorDiv(v * 1000, 127) - 500;
            case 21: case 23: case 25: case 26: case 27: case 28: case 30: case 33: case 34: case 35: case 36: case 38: return Tc(v);
            case 22: case 24:
                if (v == 0) return g == 22 ? -725 : -15600;
                return (int)(1200 * Math.Log10(v) / Math.Log10(2.0) - 7925.0);
            case 29: return v < 96 ? FloorDiv(1000 * (96 - v), 96) : 0;
            case 37: return v < 96 ? FloorDiv(2000 - 21 * v, 2) : 0;
            // the driver: 3/8 dB less per unit under 127, in FluidSynth's scale (it takes 0.4 of a SoundFont's attenuation)
            case 48: return (int)((127 - Math.Max(0, Math.Min(127, v))) * 9.375);
        }
        return v;
    }
    // not played by the driver (14, 31, 32, 44: velocity range), internal or unknown
    static readonly int[] DROP_SF1 = { 14, 18, 19, 20, 31, 32, 42, 44, 49, 59 };
    const int ATTEN_DEFAULT = 110;      // the driver's initial attenuation when a zone gives none
    static int Clamp(int g, int v)
    {
        int lo, hi;
        switch (g)
        {
            case 8: lo = 1500; hi = 13500; break;
            case 9: lo = 0; hi = 960; break;
            case 15: case 16: case 29: lo = 0; hi = 1000; break;
            case 17: lo = -500; hi = 500; break;
            case 48: case 37: lo = 0; hi = 1440; break;
            case 31: case 32: case 39: case 40: lo = -1200; hi = 1200; break;
            case 21: case 23: case 25: case 26: case 27: case 28: case 30: case 33: case 34: case 35: case 36: case 38: lo = -12000; hi = 8000; break;
            default: return v;
        }
        return Math.Max(lo, Math.Min(hi, v));
    }

    // ------------------------------------------------------------------ DLS level 1 (gm.dls)
    class Wsmp { public int Unity, Fine, Gain; public int LoopStart = -1, LoopLen; }
    class Region { public int Kl, Kh, Vl, Vh, KeyGroup, Wave; public Wsmp W; public List<int[]> Art = new List<int[]>(); }
    class DlsIns { public string Name = ""; public uint Bank; public int Prog; public List<Region> Regions = new List<Region>(); public List<int[]> Art = new List<int[]>(); }
    class Wave { public int Off, Rate; public byte[] Data; public Wsmp W; }
    class Dls { public List<DlsIns> Ins = new List<DlsIns>(); public List<Wave> Waves = new List<Wave>(); public List<int> Cue = new List<int>(); }

    static List<int[]> ParseArt(byte[] d, int o)
    {
        int cb = BitConverter.ToInt32(d, o), n = BitConverter.ToInt32(d, o + 4);
        List<int[]> res = new List<int[]>();
        for (int i = 0; i < n; i++)
        {
            int p = o + cb + i * 12;
            res.Add(new[] { BitConverter.ToUInt16(d, p), BitConverter.ToUInt16(d, p + 2), BitConverter.ToUInt16(d, p + 4), BitConverter.ToUInt16(d, p + 6), BitConverter.ToInt32(d, p + 8) });
        }
        return res;
    }
    static Wsmp ParseWsmp(byte[] d, int o)
    {
        int cb = BitConverter.ToInt32(d, o);
        Wsmp w = new Wsmp { Unity = BitConverter.ToUInt16(d, o + 4), Fine = BitConverter.ToInt16(d, o + 6), Gain = BitConverter.ToInt32(d, o + 8) };
        if (BitConverter.ToInt32(d, o + 16) > 0)
        {
            w.LoopStart = BitConverter.ToInt32(d, o + cb + 8);
            w.LoopLen = BitConverter.ToInt32(d, o + cb + 12);
        }
        return w;
    }
    static Dls LoadDls(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        Dls D = new Dls();
        List<int> ptbl = new List<int>();
        foreach (Chunk c in Chunks(d, 12, d.Length))
        {
            string t = ListType(d, c);
            if (c.Id == "ptbl")
            {
                int cb = BitConverter.ToInt32(d, c.Off), n = BitConverter.ToInt32(d, c.Off + 4);
                for (int i = 0; i < n; i++) ptbl.Add(BitConverter.ToInt32(d, c.Off + cb + i * 4));
            }
            else if (t == "lins")
            {
                foreach (Chunk c2 in Chunks(d, c.Off + 4, c.Off + c.Size))
                {
                    DlsIns ins = new DlsIns();
                    foreach (Chunk c3 in Chunks(d, c2.Off + 4, c2.Off + c2.Size))
                    {
                        string t3 = ListType(d, c3);
                        if (c3.Id == "insh") { ins.Bank = BitConverter.ToUInt32(d, c3.Off + 4); ins.Prog = BitConverter.ToInt32(d, c3.Off + 8); }
                        else if (t3 == "INFO")
                        {
                            foreach (Chunk c4 in Chunks(d, c3.Off + 4, c3.Off + c3.Size))
                                if (c4.Id == "INAM") ins.Name = Str(d, c4.Off, c4.Size);
                        }
                        else if (t3 == "lart")
                        {
                            foreach (Chunk c4 in Chunks(d, c3.Off + 4, c3.Off + c3.Size))
                                if (c4.Id == "art1") ins.Art.AddRange(ParseArt(d, c4.Off));
                        }
                        else if (t3 == "lrgn")
                        {
                            foreach (Chunk c4 in Chunks(d, c3.Off + 4, c3.Off + c3.Size))
                            {
                                Region r = new Region();
                                foreach (Chunk c5 in Chunks(d, c4.Off + 4, c4.Off + c4.Size))
                                {
                                    string t5 = ListType(d, c5);
                                    if (c5.Id == "rgnh")
                                    {
                                        r.Kl = BitConverter.ToUInt16(d, c5.Off); r.Kh = BitConverter.ToUInt16(d, c5.Off + 2);
                                        r.Vl = BitConverter.ToUInt16(d, c5.Off + 4); r.Vh = BitConverter.ToUInt16(d, c5.Off + 6);
                                        r.KeyGroup = BitConverter.ToUInt16(d, c5.Off + 10);
                                    }
                                    else if (c5.Id == "wsmp") r.W = ParseWsmp(d, c5.Off);
                                    else if (c5.Id == "wlnk") r.Wave = BitConverter.ToInt32(d, c5.Off + 8);
                                    else if (t5 == "lart")
                                        foreach (Chunk c6 in Chunks(d, c5.Off + 4, c5.Off + c5.Size))
                                            if (c6.Id == "art1") r.Art.AddRange(ParseArt(d, c6.Off));
                                }
                                ins.Regions.Add(r);
                            }
                        }
                    }
                    D.Ins.Add(ins);
                }
            }
            else if (t == "wvpl")
            {
                int baseOff = c.Off + 4;
                foreach (Chunk c2 in Chunks(d, c.Off + 4, c.Off + c.Size))
                {
                    Wave w = new Wave { Off = c2.Off - 8 - baseOff };
                    foreach (Chunk c3 in Chunks(d, c2.Off + 4, c2.Off + c2.Size))
                    {
                        if (c3.Id == "fmt ") w.Rate = BitConverter.ToInt32(d, c3.Off + 4);
                        else if (c3.Id == "data") { w.Data = new byte[c3.Size]; Buffer.BlockCopy(d, c3.Off, w.Data, 0, c3.Size); }
                        else if (c3.Id == "wsmp") w.W = ParseWsmp(d, c3.Off);
                    }
                    D.Waves.Add(w);
                }
            }
        }
        Dictionary<int, int> offs = new Dictionary<int, int>();
        for (int i = 0; i < D.Waves.Count; i++) offs[D.Waves[i].Off] = i;
        foreach (int p in ptbl) D.Cue.Add(offs[p]);
        return D;
    }

    // DLS connection blocks -> SF2 generators
    static void DlsArt(List<int[]> arts, Dictionary<int, int> g)
    {
        double kd = double.NaN, kd2 = double.NaN;
        foreach (int[] a in arts)
        {
            int src = a[0], ctl = a[1], dst = a[2], sc = a[4];
            double v = sc / 65536.0;
            bool inf = sc == int.MinValue;
            if (src == 0 && ctl == 0)
            {
                switch (dst)
                {
                    case 0x206: g[34] = inf ? -12000 : (int)v; break;
                    case 0x207: g[36] = (int)v; break;
                    case 0x209: g[38] = (int)v; break;
                    case 0x20a: { double pct = v / 10.0; g[37] = pct >= 100 ? 0 : (pct <= 0 ? 1440 : (int)(-200 * Math.Log10(pct / 100))); break; }
                    case 0x30a: g[26] = inf ? -12000 : (int)v; break;
                    case 0x30b: g[28] = (int)v; break;
                    case 0x30d: g[30] = (int)v; break;
                    case 0x30e: { double pct = v / 10.0; g[29] = Math.Max(0, Math.Min(1000, (int)(1000 - pct * 10))); break; }
                    case 0x104: g[22] = (int)v; break;
                    case 0x105: g[21] = inf ? -12000 : (int)v; break;
                    case 4: g[17] = Math.Max(-500, Math.Min(500, (int)v)); break;
                }
            }
            else if (src == 1 && ctl == 0) { if (dst == 3) g[5] = (int)v; else if (dst == 1) g[13] = (int)v; }
            else if (src == 5 && ctl == 0 && dst == 3) g[7] = (int)v;
            else if (src == 3 && ctl == 0 && dst == 0x207) kd = v;      // key number -> decay (DLS: scale * key / 128)
            else if (src == 3 && ctl == 0 && dst == 0x30b) kd2 = v;
        }
        if (!double.IsNaN(kd)) { g[40] = Math.Max(-1200, Math.Min(1200, (int)(-kd / 128))); g[36] = (int)(Get(g, 36, -12000) + kd * 60 / 128); }
        if (!double.IsNaN(kd2)) { g[32] = Math.Max(-1200, Math.Min(1200, (int)(-kd2 / 128))); g[28] = (int)(Get(g, 28, -12000) + kd2 * 60 / 128); }
    }

    // ------------------------------------------------------------------ SF2 writer
    class Sample { public string Name; public byte[] Pcm; public int Rate, LoopStart = -1, LoopEnd; }
    class Zone { public Dictionary<int, int> G; public int Smp; public Zone(Dictionary<int, int> g, int s) { G = g; Smp = s; } }
    class Sf2
    {
        public List<Sample> Samples = new List<Sample>();
        public List<KeyValuePair<string, List<Zone>>> Insts = new List<KeyValuePair<string, List<Zone>>>();
        public List<object[]> Presets = new List<object[]>();     // name, bank, prog, inst
        Dictionary<string, int> cache = new Dictionary<string, int>();
        public int AddSample(string key, string name, byte[] pcm, int rate, int ls, int le)
        {
            int i;
            if (cache.TryGetValue(key, out i)) return i;
            Samples.Add(new Sample { Name = name, Pcm = pcm, Rate = rate, LoopStart = ls, LoopEnd = le });
            cache[key] = Samples.Count - 1;
            return Samples.Count - 1;
        }
        public int AddInst(string name, List<Zone> z) { Insts.Add(new KeyValuePair<string, List<Zone>>(name, z)); return Insts.Count - 1; }

        static void Name(BinaryWriter w, string s)
        {
            byte[] b = new byte[20];
            byte[] t = Encoding.GetEncoding(1252).GetBytes(s);
            Array.Copy(t, b, Math.Min(t.Length, 20));
            w.Write(b);
        }
        static byte[] Ck(string id, byte[] data)
        {
            MemoryStream m = new MemoryStream(); BinaryWriter w = new BinaryWriter(m);
            w.Write(Encoding.ASCII.GetBytes(id)); w.Write(data.Length); w.Write(data);
            if ((data.Length & 1) != 0) w.Write((byte)0);
            return m.ToArray();
        }
        static byte[] List(string type, params byte[][] parts)
        {
            MemoryStream body = new MemoryStream();
            body.Write(Encoding.ASCII.GetBytes(type), 0, 4);
            foreach (byte[] p in parts) body.Write(p, 0, p.Length);
            return Ck("LIST", body.ToArray());
        }
        public byte[] Build(string title)
        {
            MemoryStream smpl = new MemoryStream(), shdr = new MemoryStream();
            BinaryWriter sh = new BinaryWriter(shdr);
            foreach (Sample s in Samples)
            {
                int st = (int)smpl.Length / 2;
                smpl.Write(s.Pcm, 0, s.Pcm.Length);
                int en = (int)smpl.Length / 2;
                smpl.Write(new byte[92], 0, 92);
                Name(sh, s.Name);
                sh.Write(st); sh.Write(en);
                sh.Write(s.LoopStart >= 0 ? st + s.LoopStart : st); sh.Write(s.LoopStart >= 0 ? st + s.LoopEnd : st);
                sh.Write(s.Rate); sh.Write((byte)60); sh.Write((sbyte)0); sh.Write((ushort)0); sh.Write((ushort)1);
            }
            Name(sh, "EOS"); sh.Write(new byte[26]);
            MemoryStream inst = new MemoryStream(), ibag = new MemoryStream(), igen = new MemoryStream();
            BinaryWriter iw = new BinaryWriter(inst), bw = new BinaryWriter(ibag), gw = new BinaryWriter(igen);
            int bi = 0, gi = 0;
            foreach (KeyValuePair<string, List<Zone>> ins in Insts)
            {
                Name(iw, ins.Key); iw.Write((ushort)bi);
                foreach (Zone z in ins.Value)
                {
                    bw.Write((ushort)gi); bw.Write((ushort)0); bi++;
                    List<int[]> items = new List<int[]>();
                    foreach (int k in new[] { 43, 44 }) if (z.G.ContainsKey(k)) items.Add(new[] { k, z.G[k] });
                    foreach (KeyValuePair<int, int> kv in z.G) if (kv.Key != 43 && kv.Key != 44 && kv.Key != 53) items.Add(new[] { kv.Key, kv.Value });
                    items.Add(new[] { 53, z.Smp });
                    foreach (int[] it in items)
                    {
                        gw.Write((ushort)it[0]);
                        if (it[0] == 43 || it[0] == 44 || it[0] == 53) gw.Write((ushort)it[1]); else gw.Write((short)it[1]);
                        gi++;
                    }
                }
            }
            Name(iw, "EOI"); iw.Write((ushort)bi); bw.Write((ushort)gi); bw.Write((ushort)0); gw.Write(new byte[4]);
            MemoryStream phdr = new MemoryStream(), pbag = new MemoryStream(), pgen = new MemoryStream();
            BinaryWriter pw = new BinaryWriter(phdr), pbw = new BinaryWriter(pbag), pgw = new BinaryWriter(pgen);
            bi = 0; gi = 0;
            foreach (object[] p in Presets.OrderBy(p => (int)p[1]).ThenBy(p => (int)p[2]))
            {
                Name(pw, (string)p[0]); pw.Write((ushort)(int)p[2]); pw.Write((ushort)(int)p[1]); pw.Write((ushort)bi); pw.Write(new byte[12]);
                pbw.Write((ushort)gi); pbw.Write((ushort)0); bi++;
                pgw.Write((ushort)41); pgw.Write((ushort)(int)p[3]); gi++;
            }
            Name(pw, "EOP"); pw.Write((ushort)0); pw.Write((ushort)0); pw.Write((ushort)bi); pw.Write(new byte[12]);
            pbw.Write((ushort)gi); pbw.Write((ushort)0); pgw.Write(new byte[4]);
            byte[] t = Encoding.ASCII.GetBytes(title);
            byte[] inam = new byte[t.Length + 2 - t.Length % 2]; Array.Copy(t, inam, t.Length);
            byte[] info = List("INFO", Ck("ifil", new byte[] { 2, 0, 1, 0 }), Ck("isng", Encoding.ASCII.GetBytes("EMU8000\0")),
                Ck("INAM", inam), Ck("ISFT", Encoding.ASCII.GetBytes("TNPlus\0\0")));
            byte[] sdta = List("sdta", Ck("smpl", smpl.ToArray()));
            byte[] pdta = List("pdta", Ck("phdr", phdr.ToArray()), Ck("pbag", pbag.ToArray()), Ck("pmod", new byte[10]), Ck("pgen", pgen.ToArray()),
                Ck("inst", inst.ToArray()), Ck("ibag", ibag.ToArray()), Ck("imod", new byte[10]), Ck("igen", igen.ToArray()), Ck("shdr", shdr.ToArray()));
            MemoryStream body = new MemoryStream();
            body.Write(Encoding.ASCII.GetBytes("sfbk"), 0, 4);
            foreach (byte[] p in new[] { info, sdta, pdta }) body.Write(p, 0, p.Length);
            return Ck("RIFF", body.ToArray());
        }
    }

    // ------------------------------------------------------------------ build
    public static string DlsPath() { return Path.Combine(Environment.SystemDirectory, "drivers", "gm.dls"); }

    static DlsIns DlsFind(Dls D, int prog, bool drum)
    {
        foreach (DlsIns i in D.Ins)
            if (((i.Bank >> 31) != 0) == drum && (i.Bank & 0x7fff) == 0 && i.Prog == prog) return i;
        return null;
    }
    static int DlsSample(Sf2 sf, Dls D, Region r, out Wsmp ws, out bool loop)
    {
        Wave w = D.Waves[D.Cue[r.Wave]];
        ws = r.W ?? w.W;
        loop = ws.LoopStart >= 0;
        return sf.AddSample("dls" + r.Wave + "_" + ws.LoopStart + "_" + ws.LoopLen, "GS" + r.Wave.ToString("000"), w.Data, w.Rate,
            loop ? ws.LoopStart : -1, loop ? ws.LoopStart + ws.LoopLen : 0);
    }
    static Zone DlsZone(Sf2 sf, Dls D, DlsIns ins, Region r)
    {
        Dictionary<int, int> raw = new Dictionary<int, int>();
        DlsArt(r.Art.Count > 0 ? r.Art : ins.Art, raw);
        Dictionary<int, int> g = new Dictionary<int, int>();
        foreach (KeyValuePair<int, int> kv in raw) g[kv.Key] = Clamp(kv.Key, kv.Value);
        Wsmp ws; bool loop;
        int smp = DlsSample(sf, D, r, out ws, out loop);
        g[43] = r.Kl | (r.Kh << 8); g[44] = r.Vl | (Math.Min(r.Vh, 127) << 8);
        g[58] = ws.Unity; g[52] = ws.Fine;
        int att = (int)(-(double)ws.Gain / 65536.0);
        if (att != 0) g[48] = Clamp(48, att);
        if (loop) g[54] = 1;
        if (r.KeyGroup != 0) g[57] = r.KeyGroup;
        return new Zone(g, smp);
    }

    // The AWE32 ROM as the card addresses it. Dumps made with AWE-DUMP start one word late (86Box moves them back
    // the same way): the ROM's name "1MGM" is then in words 3 and 4, not 2 and 3.
    static byte[] RomWords(byte[] rom)
    {
        if (rom[6] == 0x4d && rom[7] == 0x31 && rom[8] == 0x4d && rom[9] == 0x47)
        {
            byte[] r = new byte[rom.Length];
            Buffer.BlockCopy(rom, 2, r, 0, rom.Length - 2);
            return r;
        }
        return rom;
    }
    static int Word(byte[] b, int i) { return (short)(b[2 * i] | b[2 * i + 1] << 8); }

    // The loop of one of the bank's own samples as its waveform repeats. The driver's loop is [start - 2, end - 1) of
    // the sample chunk (its loop end + 1 as for the ROM, on samples it stores 2 words further), 2 samples longer
    // than the wave's period in FF.SBK: a small step back at each turn and a note a few cents flat. Returns the
    // period the samples after the loop start repeat with (within 3 of the driver's length), 0 when none stands out.
    static int NaturalLoop(byte[] smpl, int ls, int le)
    {
        int len = le - ls + 1, n = smpl.Length / 2, best = 0;
        long bestCost = long.MaxValue, second = long.MaxValue;
        for (int p = len - 3; p <= len + 1; p++)
        {
            const int W = 24;
            if (p < 8 || ls < 0 || ls + p + W > n) continue;
            long cost = 0;
            for (int k = 0; k < W; k++) cost += Math.Abs(Word(smpl, ls + p + k) - Word(smpl, ls + k));
            if (cost < bestCost) { second = bestCost; bestCost = cost; best = p; }
            else if (cost < second) second = cost;
        }
        return best != 0 && second != long.MaxValue && bestCost * 2 < second ? best : 0;
    }

    class Ctx
    {
        public Sf2 Sf = new Sf2();
        public byte[] Rom;
        public bool Retune;
        public Dictionary<int, int> Native;         // ROM sample start -> its own pitch (cents), from the driver's set
        public int Tuned;
    }

    // a layer -> an SF2 zone on keys lo..hi; null when its sample cannot be read. own: a zone of the game's bank (the
    // driver's set is in tune as it is, detuned layers included)
    static Zone Convert(Ctx c, Sbk S, Layer l, int lo, int hi, bool drum, bool own)
    {
        Dictionary<int, int> z = l.Z;
        int si = z[53] & 0xffff;
        if (si >= S.Shdr.Count || si >= S.Names.Count) return null;
        string sname = S.Names[si];
        int[] sh = S.Shdr[si];
        Dictionary<int, int> g = new Dictionary<int, int>();
        g[43] = lo | (hi << 8);
        foreach (KeyValuePair<int, int> x in z)
        {
            int id = x.Key;
            if (Array.IndexOf(DROP_SF1, id) >= 0 || id == 53 || id == 55 || id == 58 || id == 51 || id == 52 || id == 56 || id == 39 || id == 40) continue;
            g[id] = Clamp(id, Conv(id, x.Value));
        }
        // SBK defaults that differ from SF2 (awesfx init_layer_items, the driver's table 0x1697): envelopes sustain at
        // zero, LFO rates, attenuation
        if (!g.ContainsKey(29)) g[29] = 1000;
        if (!g.ContainsKey(37)) g[37] = 1000;
        if (!g.ContainsKey(22)) g[22] = -725;
        if (!g.ContainsKey(24)) g[24] = -15600;
        if (!g.ContainsKey(48)) g[48] = Clamp(48, Conv(48, ATTEN_DEFAULT));
        // hold and decay of the volume envelope by key: the driver takes milliseconds off per key above 60 (not on the
        // drum channel), SF2 scales the time; matched in the middle of the zone
        if (!drum)
            foreach (int[] ks in new[] { new[] { 35, 39 }, new[] { 36, 40 } })
            {
                int per = Get(z, ks[1], 0), t60 = Get(z, ks[0], 0), mid = (lo + hi) / 2;
                if (per == 0 || t60 <= 0) continue;
                if (mid == 60) mid = 72;
                int tm = Math.Max(1, Math.Min(65535, t60 + (60 - mid) * per));
                g[ks[1]] = Clamp(ks[1], (int)Math.Round(1200.0 * Math.Log((double)tm / t60, 2.0) / (60 - mid)));
            }
        // pitch (the driver's note-on 0x1f48, awesfx set_rootkey: SF1 branch)
        int scale = Get(z, 56, 0) == 1 ? 50 : 100;
        int root = 60, tune = 0;
        if (z.ContainsKey(55))
        {
            int sp = z[55];
            root = FloorDiv(sp, 100); tune = -(sp - root * 100);
            if (tune <= -50) { root++; tune += 100; }
            if (scale == 50) tune = FloorDiv(tune, 2);
        }
        if (z.ContainsKey(58)) root += z[58] - 60;
        tune += Get(z, 51, 0) * scale + FloorDiv(Get(z, 52, 0) * scale, 100);
        if (scale != 100) g[56] = scale;
        bool isRom = sname.StartsWith("*");     // sample in the AWE32 ROM (addresses in 16-bit words)
        byte[] src = isRom ? c.Rom : S.Smpl;
        bool loop = (Get(z, 54, 0) & 1) != 0 && sh[3] > sh[2];
        // the loop: [loop start, loop end + 1) for the ROM (what the driver programs, and where the waveforms close);
        // the bank's own samples: the driver's [start - 2, end - 1), or the wave's own period when tuning
        int ls = sh[2], le = sh[3] + 1, natural = 0;
        if (loop && !isRom)
        {
            natural = c.Retune ? NaturalLoop(S.Smpl, sh[2], sh[3]) : 0;
            if (natural > 0) le = ls + natural;
            else if (sh[2] - 2 >= sh[0]) { ls = sh[2] - 2; le = sh[3] - 1; }
        }
        int end = loop ? Math.Max(sh[1], le) : sh[1];
        if (sh[0] < 0 || end < sh[0] || end * 2 > src.Length || (loop && ls < sh[0])) return null;
        if (c.Retune && own && loop && !drum && scale == 100)
        {
            double err = 0;
            if (!isRom)
            {
                // its own pitch from the loop: a few whole periods, the count nearest the zone's root key
                int len = le - ls;
                double froot = 440.0 * Math.Pow(2.0, (root - 69) / 12.0);
                int periods = (int)Math.Round(len * froot / 44100.0);
                if (periods >= 1 && periods <= 16) err = 1200.0 * Math.Log(periods * 44100.0 / len / froot, 2.0);
            }
            else if (c.Native != null && c.Native.ContainsKey(sh[0]) && Get(z, 52, 0) == 0)
                err = c.Native[sh[0]] - (root * 100 - tune);      // a zone left at a whole key on a sample that is not
            if (Math.Abs(err) >= 3 && Math.Abs(err) <= 50) { tune -= (int)Math.Round(err); c.Tuned++; }
        }
        byte[] pcm = new byte[(end - sh[0]) * 2];
        Buffer.BlockCopy(src, sh[0] * 2, pcm, 0, pcm.Length);
        int smp = c.Sf.AddSample((isRom ? "rom" + sh[0] + "_" + end : "ram" + si) + "_" + (loop ? ls + "_" + le : "-"), sname.TrimStart('*'), pcm, 44100,
            loop ? ls - sh[0] : -1, loop ? le - sh[0] : 0);
        g[58] = Math.Max(0, Math.Min(127, root));
        int coarse = tune >= 0 ? tune / 100 : -(-tune / 100);
        int fine = tune - coarse * 100;
        if (coarse != 0) g[51] = coarse; else g.Remove(51);
        if (fine != 0) g[52] = fine; else g.Remove(52);
        return new Zone(g, smp);
    }

    // rom: the AWE32 ROM (1 MB). mdiPath: the game's SBAWE32.MDI (null or unreadable: gaps from dlsPath, when it
    // exists). fill: what FF.SBK lacks comes from the card's General MIDI set (false: the card as it was, where a
    // missing program plays the bank's first preset). retune: the zones the bank leaves out of tune are corrected.
    public static byte[] Build(string sbkPath, string mdiPath, string dlsPath, byte[] rom, bool fill, bool retune, out string note)
    {
        if (rom == null || rom.Length != ROM_SIZE) throw new ArgumentException("AWE32 ROM: 1 MB expected");
        Sbk S = LoadSbk(sbkPath);
        Sbk G = (fill || retune) && mdiPath != null && File.Exists(mdiPath) ? LoadEmbedded(mdiPath, S) : null;
        Dls D = G == null && fill && dlsPath != null && File.Exists(dlsPath) ? LoadDls(dlsPath) : null;
        Ctx c = new Ctx { Rom = RomWords(rom), Retune = retune, Native = G != null ? G.Native : null };
        Sf2 sf = c.Sf;
        HashSet<int> covered = new HashSet<int>();
        int firstInst = -1, filled = 0;
        for (int pi = 0; pi < S.Phdr.Count - 1; pi++)
        {
            string name = (string)S.Phdr[pi][0];
            int prog = (int)S.Phdr[pi][1], bank = (int)S.Phdr[pi][2];
            bool drum = bank == 128;
            List<Zone> zones = new List<Zone>();
            bool[] done = new bool[128];
            foreach (Layer l in Layers(S, pi))
            {
                if (l.Lo > l.Hi) continue;
                Zone zz = Convert(c, S, l, l.Lo, l.Hi, drum, true);
                if (zz == null) continue;
                for (int k = l.Lo; k <= l.Hi; k++) done[k] = true;
                zones.Add(zz);
            }
            if (fill && G != null)      // keys Brosius didn't define: the card's own General MIDI ones
            {
                int gi = G.Find(bank, prog);
                if (gi >= 0) filled += Fill(c, G, gi, done, zones, drum);
            }
            else if (D != null)         // ... or the Roland GS ones
            {
                DlsIns dins = DlsFind(D, prog, drum);
                if (dins != null)
                    foreach (Region r in dins.Regions)
                    {
                        int k = r.Kl;
                        while (k <= r.Kh && k < 128)
                        {
                            if (done[k]) { k++; continue; }
                            int a = k;
                            while (k + 1 <= r.Kh && k + 1 < 128 && !done[k + 1]) k++;
                            Zone zz = DlsZone(sf, D, dins, r);
                            zz.G[43] = a | (k << 8);
                            zones.Add(zz);
                            k++;
                        }
                    }
            }
            int inst = sf.AddInst(name, zones);
            if (pi == 0) firstInst = inst;
            sf.Presets.Add(new object[] { name, bank, prog, inst });
            covered.Add(bank * 256 + prog);
        }
        if (fill && G != null)
        {
            for (int gi = 0; gi < G.Phdr.Count - 1; gi++)
            {
                int prog = (int)G.Phdr[gi][1], bank = (int)G.Phdr[gi][2];
                if (covered.Contains(bank * 256 + prog)) continue;
                List<Zone> zones = new List<Zone>();
                Fill(c, G, gi, new bool[128], zones, bank == 128);
                string name = (string)G.Phdr[gi][0];
                sf.Presets.Add(new object[] { name, bank, prog, sf.AddInst(name, zones) });
                covered.Add(bank * 256 + prog);
                filled++;
            }
        }
        else if (D != null)
        {
            foreach (DlsIns ins in D.Ins)
            {
                bool drum = (ins.Bank >> 31) != 0;
                int bank = drum ? 128 : (int)((ins.Bank >> 8) & 0x7f);
                if ((ins.Bank & 0x7f) != 0 || covered.Contains(bank * 256 + ins.Prog)) continue;
                List<Zone> zones = new List<Zone>();
                foreach (Region r in ins.Regions) zones.Add(DlsZone(sf, D, ins, r));
                sf.Presets.Add(new object[] { ins.Name, bank, ins.Prog, sf.AddInst(ins.Name, zones) });
            }
        }
        else if (!fill && firstInst >= 0)       // the card: a program the bank doesn't have plays its first preset
        {
            for (int prog = 0; prog < 128; prog++)
                if (!covered.Contains(prog)) sf.Presets.Add(new object[] { (string)S.Phdr[0][0], 0, prog, firstInst });
        }
        string noMdi = "SBAWE32.MDI not " + (mdiPath != null && File.Exists(mdiPath) ? "recognized" : "found");
        note = !fill ? "the card as it was (the bank alone)"
            : G != null ? "completed from the card's General MIDI set (SBAWE32.MDI)"
            : D != null ? "completed from the Roland GS set of Windows (" + noMdi + ")" : "the bank alone (" + noMdi + ", no gm.dls)";
        if (c.Tuned > 0) note += ", " + c.Tuned + (c.Tuned == 1 ? " zone tuned" : " zones tuned");
        return sf.Build("Terra Nova AWE32");
    }

    // without the driver: FF.SBK completed from gm.dls, nothing tuned
    public static byte[] Build(string sbkPath, string dlsPath, byte[] rom)
    {
        string note;
        return Build(sbkPath, null, dlsPath, rom, true, false, out note);
    }

    // the zones of the driver's preset gi on the keys not done yet; returns 1 when it added some
    static int Fill(Ctx c, Sbk G, int gi, bool[] done, List<Zone> zones, bool drum)
    {
        int added = 0;
        foreach (Layer l in Layers(G, gi))
        {
            int k = l.Lo;
            while (k <= l.Hi)
            {
                if (done[k]) { k++; continue; }
                int a = k;
                while (k + 1 <= l.Hi && !done[k + 1]) k++;
                Zone zz = Convert(c, G, l, a, k, drum, false);
                if (zz != null) { zones.Add(zz); added = 1; }
                k++;
            }
        }
        return added;
    }
}
