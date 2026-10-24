// The SAVES button and the update check (TNPlusGui.cs): ResBlock on made-up "LG Res File v2" files, SaveFile, SaveSlots
// (the 9 lines of the list), BackupSave (a deleted save goes to "save backups"), NewerVersion, ReleaseTag and VERSION.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ---------------------------------------------------------------- helpers (prefix Saves)
        // one block of a made-up LG Res file: the bytes stored in the file and what its directory entry says
        sealed class SavesBlock
        {
            public int Id;
            public byte[] Data = new byte[0];       // stored bytes (for a packed block: the packed bytes)
            public int Size = -1;                   // low 24 bits of the entry's first word; -1: Data.Length
            public int Flags;                       // its high byte: bit 0 packed, bit 1 compound
            public int Packed = -1;                 // low 24 bits of the second word (packed size); -1: Data.Length
            public int Type;                        // its high byte
        }

        const string SavesMagic = "LG Res File v2\r\n\u001A";
        const int SavesHeadMagic = 0x06EF0018;      // int 0 of a save's head block (1776)

        static SavesBlock SavesBlk(int id, byte[] data)
        {
            SavesBlock b = new SavesBlock(); b.Id = id; b.Data = data; return b;
        }

        // a packed block: the stored (packed) bytes, its unpacked size in the size word, flag bit 0
        static SavesBlock SavesPackedBlk(int id, byte[] stored, int unpacked, int type)
        {
            SavesBlock b = SavesBlk(id, stored); b.Size = unpacked; b.Flags = 1; b.Type = type; return b;
        }

        // n bytes that differ from block to block (seed), never all zero
        static byte[] SavesFill(int n, int seed)
        {
            byte[] b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)(seed * 31 + i * 7 + 1);
            return b;
        }

        // an "LG Res File v2": the 0x80-byte header (its text, the directory offset at 0x7C), the blocks' data from
        // dataAt (0x80 or more, a gap of 0xCC before), each one padded to 4 bytes with pad, then the directory: u16
        // count, u32 dataAt, per block u16 id, u32 size | flags << 24, u32 packed size | type << 24. dirAt >= 0 puts the
        // directory there instead (in the header, between its text and 0x7C).
        static byte[] SavesResFile(int dataAt, int dirAt, byte pad, params SavesBlock[] blocks)
        {
            List<byte> f = new List<byte>(Encoding.ASCII.GetBytes(SavesMagic));
            while (f.Count < 0x80) f.Add(0);
            while (f.Count < dataAt) f.Add(0xCC);
            foreach (SavesBlock b in blocks)
            {
                f.AddRange(b.Data);
                while ((f.Count - dataAt) % 4 != 0) f.Add(pad);
            }
            List<byte> dir = new List<byte>();
            dir.AddRange(BitConverter.GetBytes((ushort)blocks.Length));
            dir.AddRange(BitConverter.GetBytes(dataAt));
            foreach (SavesBlock b in blocks)
            {
                int size = b.Size >= 0 ? b.Size : b.Data.Length, packed = b.Packed >= 0 ? b.Packed : b.Data.Length;
                dir.AddRange(BitConverter.GetBytes((ushort)b.Id));
                dir.AddRange(BitConverter.GetBytes((uint)(size & 0xFFFFFF) | (uint)b.Flags << 24));
                dir.AddRange(BitConverter.GetBytes((uint)(packed & 0xFFFFFF) | (uint)b.Type << 24));
            }
            byte[] r;
            if (dirAt < 0) { dirAt = f.Count; f.AddRange(dir); r = f.ToArray(); }
            else
            {
                if (dirAt < SavesMagic.Length || dirAt + dir.Count > 0x7C) Fail("test builder: the directory does not fit in the header");
                r = f.ToArray();
                dir.CopyTo(r, dirAt);
            }
            BitConverter.GetBytes(dirAt).CopyTo(r, 0x7C);
            return r;
        }

        static byte[] SavesRes(params SavesBlock[] blocks) { return SavesResFile(0x80, -1, 0, blocks); }

        // a file in the test's own folder
        static string SavesPut(string name, byte[] data) { return PutFile(Path.Combine(ExeDir, name), data); }

        // the save's name block (1775): the name, NUL-padded to 32 bytes
        static byte[] SavesName(string name)
        {
            byte[] b = new byte[32];
            Encoding.ASCII.GetBytes(name).CopyTo(b, 0);
            return b;
        }

        // a head block (1776) of length bytes: magic at 0, a word at 4, the mission field (mission + 2) at 8, a word at 12
        static byte[] SavesHead(int magic, int missionField, int length)
        {
            byte[] h = new byte[16];
            BitConverter.GetBytes(magic).CopyTo(h, 0);
            BitConverter.GetBytes(0x01020304).CopyTo(h, 4);
            BitConverter.GetBytes(missionField).CopyTo(h, 8);
            BitConverter.GetBytes(0x55AA55AA).CopyTo(h, 12);
            byte[] r = new byte[length];
            Array.Copy(h, r, Math.Min(16, length));
            return r;
        }

        // a save as the list reads it: other blocks around the name (1775) and head (1776) blocks
        static byte[] SavesSave(byte[] nameBlock, byte[] headBlock)
        {
            List<SavesBlock> l = new List<SavesBlock>();
            l.Add(SavesBlk(1000, SavesFill(37, 1)));
            if (nameBlock != null) l.Add(SavesBlk(1775, nameBlock));
            if (headBlock != null) l.Add(SavesBlk(1776, headBlock));
            l.Add(SavesBlk(2000, SavesFill(5, 9)));
            return SavesRes(l.ToArray());
        }

        static byte[] SavesSave(string name, int missionField)
        {
            return SavesSave(SavesName(name), SavesHead(SavesHeadMagic, missionField, 16));
        }

        // the game's save folder: <ExeDir>\game\TNOVA
        static string SavesTnova() { return TestDir("game", "TNOVA"); }

        // the 9 lines of a folder without saves
        static List<string> SavesEmpty()
        {
            List<string> l = new List<string>();
            for (int i = 1; i <= 9; i++) l.Add(i + "  (empty)");
            return l;
        }

        static void SavesNewer(bool expected, string latest, string current)
        {
            Eq(expected, NewerVersion(latest, current), "NewerVersion(" + Show(latest) + ", " + Show(current) + ")");
        }

        // ---------------------------------------------------------------- ResBlock
        [Test] static void Saves_ResBlockFindsBlocksById()
        {
            byte[] a = SavesFill(16, 1), b = SavesFill(40, 2), c = SavesFill(8, 3), e = SavesFill(100, 4);
            string f = SavesPut("A.RES", SavesRes(SavesBlk(300, a), SavesBlk(1775, b), SavesBlk(7, c), SavesBlk(65535, e)));
            EqBytes(a, ResBlock(f, 300), "block 300, the first");
            EqBytes(b, ResBlock(f, 1775), "block 1775");
            EqBytes(c, ResBlock(f, 7), "block 7 (the ids are in no order)");
            EqBytes(e, ResBlock(f, 65535), "block 65535 (the largest u16 id), the last");
        }

        [Test] static void Saves_ResBlockOddSizesAre4Aligned()
        {
            int[] sizes = { 1, 2, 3, 5, 6, 7, 9, 4, 13 };
            List<SavesBlock> bl = new List<SavesBlock>();
            for (int i = 0; i < sizes.Length; i++) bl.Add(SavesBlk(100 + i, SavesFill(sizes[i], i + 1)));
            byte[] d = SavesResFile(0x80, -1, 0xEE, bl.ToArray());
            // 4 + 4 + 4 + 8 + 8 + 8 + 12 + 4 + 16 = 68 bytes of padded data: the made-up file really is padded
            Eq(0x80 + 68, BitConverter.ToInt32(d, 0x7C), "test builder: directory right after the padded data");
            string f = SavesPut("ODD.RES", d);
            for (int i = 0; i < sizes.Length; i++)
                EqBytes(bl[i].Data, ResBlock(f, 100 + i), "block of " + sizes[i] + " bytes (its 0xEE padding left out)");
        }

        [Test] static void Saves_ResBlockEmptyBlock()
        {
            byte[] next = SavesFill(6, 5);
            string f = SavesPut("E.RES", SavesRes(SavesBlk(1, new byte[0]), SavesBlk(2, next)));
            byte[] e = ResBlock(f, 1);
            Ok(e != null, "an empty block is found (not null)");
            Eq(0, e.Length, "and empty");
            EqBytes(next, ResBlock(f, 2), "the next block starts where the empty one does");
        }

        [Test] static void Saves_ResBlockSizesUpTo24Bits()
        {
            byte[] big = SavesFill(0x10003, 1), small = SavesFill(10, 2);   // 65539 bytes: more than a u16 holds, odd
            string f = SavesPut("BIG.RES", SavesRes(SavesBlk(1, big), SavesBlk(2, small)));
            EqBytes(big, ResBlock(f, 1), "a block of 0x10003 bytes");
            EqBytes(small, ResBlock(f, 2), "the block after it (at 0x80 + 0x10004)");
        }

        [Test] static void Saves_ResBlockPackedIsNullNextStillFound()
        {
            byte[] before = SavesFill(10, 1), after = SavesFill(12, 2), last = SavesFill(3, 3);
            // 13 packed bytes for 1000 unpacked, type byte 0xFF; then 1 packed byte for 0x123456, type 1
            string f = SavesPut("P.RES", SavesRes(SavesBlk(10, before), SavesPackedBlk(11, SavesFill(13, 9), 1000, 0xFF),
                SavesBlk(12, after), SavesPackedBlk(13, SavesFill(1, 7), 0x123456, 1), SavesBlk(14, last)));
            EqBytes(before, ResBlock(f, 10), "the block before the packed one");
            Ok(ResBlock(f, 11) == null, "a packed block: null (the tool does not unpack)");
            EqBytes(after, ResBlock(f, 12), "the block after it: the offset moved by the packed size rounded to 4 (16), not by the " +
                "unpacked one (1000) nor with the type byte");
            Ok(ResBlock(f, 13) == null, "a second packed block: null");
            EqBytes(last, ResBlock(f, 14), "the last block, after a packed one of 1 byte (4 with its padding)");
        }

        [Test] static void Saves_ResBlockOtherFlagsReturnedRaw()
        {
            int[] flags = { 0x02, 0x04, 0x08, 0x10, 0x80, 0xFE };   // compound (bit 1) and the others: anything but bit 0
            List<SavesBlock> bl = new List<SavesBlock>();
            for (int i = 0; i < flags.Length; i++) { SavesBlock b = SavesBlk(20 + i, SavesFill(5 + i, i)); b.Flags = flags[i]; bl.Add(b); }
            SavesBlock both = SavesBlk(40, SavesFill(9, 40)); both.Flags = 0x03;   // packed and compound
            bl.Add(both);
            bl.Add(SavesBlk(41, SavesFill(4, 41)));
            string f = SavesPut("F.RES", SavesRes(bl.ToArray()));
            for (int i = 0; i < flags.Length; i++)
                EqBytes(bl[i].Data, ResBlock(f, 20 + i), "flags 0x" + flags[i].ToString("X2") + ": the stored bytes as they are");
            Ok(ResBlock(f, 40) == null, "flags 0x03 (packed + compound): null");
            EqBytes(bl[bl.Count - 1].Data, ResBlock(f, 41), "the block after it");
        }

        [Test] static void Saves_ResBlockUnpackedIgnoresSecondWord()
        {
            // a block that is not packed: its length is the size word's; the packed size and type change nothing
            SavesBlock a = SavesBlk(1, SavesFill(10, 1)); a.Packed = 0xABCDEF; a.Type = 0x7F;
            SavesBlock b = SavesBlk(2, SavesFill(7, 2)); b.Packed = 0; b.Type = 0xFF;
            SavesBlock c = SavesBlk(3, SavesFill(5, 3));
            string f = SavesPut("W.RES", SavesRes(a, b, c));
            EqBytes(a.Data, ResBlock(f, 1), "packed size 0xABCDEF, type 0x7F");
            EqBytes(b.Data, ResBlock(f, 2), "packed size 0, type 0xFF");
            EqBytes(c.Data, ResBlock(f, 3), "the block after them");
        }

        [Test] static void Saves_ResBlockFirstOfTwoSameIds()
        {
            byte[] first = SavesFill(8, 1), second = SavesFill(12, 2);
            string f = SavesPut("D.RES", SavesRes(SavesBlk(1775, first), SavesBlk(1775, second)));
            EqBytes(first, ResBlock(f, 1775), "two blocks 1775: the first one");
        }

        [Test] static void Saves_ResBlockMissingIdIsNull()
        {
            string f = SavesPut("M.RES", SavesRes(SavesBlk(1775, SavesFill(8, 1)), SavesBlk(1, SavesFill(4, 2))));
            Ok(ResBlock(f, 1776) == null, "an id that is not in the directory");
            Ok(ResBlock(f, 0) == null, "id 0");
            Ok(ResBlock(f, -1) == null, "id -1");
            Ok(ResBlock(f, 1775 + 0x10000) == null, "1775 + 65536: the ids are u16, no wrap-around");
            Ok(ResBlock(SavesPut("Z.RES", SavesRes()), 1775) == null, "an empty directory (count 0)");
        }

        [Test] static void Saves_ResBlockLayoutFromItsHeader()
        {
            byte[] a = SavesFill(9, 1), b = SavesFill(4, 2);
            // data from 0x100 (0x80 bytes of 0xCC after the header): the first data offset comes from the directory
            string f = SavesPut("G.RES", SavesResFile(0x100, -1, 0, SavesBlk(1, a), SavesBlk(2, b)));
            EqBytes(a, ResBlock(f, 1), "data from 0x100: block 1");
            EqBytes(b, ResBlock(f, 2), "data from 0x100: block 2");
            // the directory before the data (in the header): wherever 0x7C says
            string g = SavesPut("H.RES", SavesResFile(0x80, 0x20, 0, SavesBlk(1, a), SavesBlk(2, b)));
            EqBytes(a, ResBlock(g, 1), "directory at 0x20: block 1");
            EqBytes(b, ResBlock(g, 2), "directory at 0x20: block 2");
            // after its 14 characters the header text can be anything
            byte[] d = SavesRes(SavesBlk(1, a));
            Encoding.ASCII.GetBytes("LG Res File v2 by someone else").CopyTo(d, 0);
            EqBytes(a, ResBlock(SavesPut("I.RES", d), 1), "\"LG Res File v2\" followed by other text");
        }

        [Test] static void Saves_ResBlockNotLgResIsNull()
        {
            byte[] good = SavesRes(SavesBlk(1775, SavesFill(8, 1)));
            EqBytes(SavesFill(8, 1), ResBlock(SavesPut("OK.RES", good), 1775), "control: the made-up file is read");
            string[] heads = { "LG Res File v1", "lg res file v2", "LG Res File V2", "LG Res  ile v2", " LG Res File v2", "\0LG Res File v2" };
            for (int i = 0; i < heads.Length; i++)
            {
                byte[] d = (byte[])good.Clone();
                Encoding.ASCII.GetBytes(heads[i]).CopyTo(d, 0);
                Ok(ResBlock(SavesPut("N" + i + ".RES", d), 1775) == null, "header " + Show(heads[i]) + ": null");
            }
            Ok(ResBlock(SavesPut("ZERO.RES", new byte[0x200]), 1775) == null, "0x200 zero bytes");
            byte[] text = Encoding.ASCII.GetBytes(new string('x', 300));
            Ok(ResBlock(SavesPut("TEXT.RES", text), 1775) == null, "a text file");
        }

        [Test] static void Saves_ResBlockTooShortIsNull()
        {
            // exactly 0x80 bytes: the text, a directory at 0x20 (one block of 8 bytes at 0x70), its offset at 0x7C
            byte[] d = new byte[0x80], data = SavesFill(8, 3);
            Encoding.ASCII.GetBytes(SavesMagic).CopyTo(d, 0);
            BitConverter.GetBytes((ushort)1).CopyTo(d, 0x20);
            BitConverter.GetBytes(0x70).CopyTo(d, 0x22);
            BitConverter.GetBytes((ushort)9).CopyTo(d, 0x26);
            BitConverter.GetBytes(8).CopyTo(d, 0x28);
            BitConverter.GetBytes(8).CopyTo(d, 0x2C);
            data.CopyTo(d, 0x70);
            BitConverter.GetBytes(0x20).CopyTo(d, 0x7C);
            EqBytes(data, ResBlock(SavesPut("MIN.RES", d), 9), "a file of exactly 0x80 bytes is long enough");
            byte[] s = new byte[0x7F];
            Array.Copy(d, s, s.Length);
            Ok(ResBlock(SavesPut("S7F.RES", s), 9) == null, "0x7F bytes: null");
            Ok(ResBlock(SavesPut("S0.RES", new byte[0]), 9) == null, "an empty file");
            Ok(ResBlock(SavesPut("S14.RES", Encoding.ASCII.GetBytes("LG Res File v2")), 9) == null, "only the 14 characters");
            Ok(ResBlock(SavesPut("S17.RES", Encoding.ASCII.GetBytes(SavesMagic)), 9) == null, "only the header's text");
        }

        [Test] static void Saves_ResBlockTruncatedDataIsNull()
        {
            byte[] a = SavesFill(12, 1), b = SavesFill(40, 2);
            // directory in the header, data at the end: cutting the file cuts the last block
            byte[] full = SavesResFile(0x80, 0x20, 0, SavesBlk(1, a), SavesBlk(2, b));
            EqBytes(b, ResBlock(SavesPut("T.RES", full), 2), "control: the whole file");
            byte[] cut = new byte[full.Length - 1];
            Array.Copy(full, cut, cut.Length);
            string g = SavesPut("T1.RES", cut);
            Ok(ResBlock(g, 2) == null, "a block one byte short: null");
            EqBytes(a, ResBlock(g, 1), "the whole block before it is still read");
            // a size past the end of the file (the directory at the end, as usual)
            SavesBlock big = SavesBlk(3, SavesFill(8, 3)); big.Size = 0x100000;
            Ok(ResBlock(SavesPut("T2.RES", SavesRes(big)), 3) == null, "a size of 1 MB in a file of less than 200 bytes: null");
            big.Size = 0xFFFFFF;
            Ok(ResBlock(SavesPut("T3.RES", SavesRes(big)), 3) == null, "the largest size (0xFFFFFF): null");
        }

        [Test] static void Saves_ResBlockBadDirectoryOffsetIsNull()
        {
            byte[] good = SavesRes(SavesBlk(1775, SavesFill(8, 1)));
            int[] bad = { good.Length, good.Length - 1, good.Length - 5, 0x10000, -16, int.MaxValue, int.MinValue };
            for (int i = 0; i < bad.Length; i++)
            {
                byte[] d = (byte[])good.Clone();
                BitConverter.GetBytes(bad[i]).CopyTo(d, 0x7C);
                Ok(ResBlock(SavesPut("B" + i + ".RES", d), 1775) == null, "directory offset " + bad[i] + " (file of " + good.Length + " bytes): null");
            }
        }

        [Test] static void Saves_ResBlockShortDirectory()
        {
            // the directory claims more entries than the file holds: the entries that are there still count
            byte[] a = SavesFill(4, 1), b = SavesFill(4, 2);
            byte[] d = SavesRes(SavesBlk(1, a), SavesBlk(2, b));
            int diro = BitConverter.ToInt32(d, 0x7C);
            BitConverter.GetBytes((ushort)0xFFFF).CopyTo(d, diro);
            string f = SavesPut("SD.RES", d);
            EqBytes(a, ResBlock(f, 1), "block 1");
            EqBytes(b, ResBlock(f, 2), "block 2, the last entry in the file");
            Ok(ResBlock(f, 3) == null, "an id not there: the entries run past the end of the file, null and no exception");
            // the last entry cut in its size word: its block is not found, the one before still is
            byte[] cut = new byte[d.Length - 6];
            Array.Copy(d, cut, cut.Length);
            string g = SavesPut("SD1.RES", cut);
            EqBytes(a, ResBlock(g, 1), "block 1 with the last entry cut");
            Ok(ResBlock(g, 2) == null, "block 2 with its entry cut: null");
        }

        [Test] static void Saves_ResBlockBadDataOffsetIsNull()
        {
            byte[] good = SavesRes(SavesBlk(1, SavesFill(9, 1)));
            int diro = BitConverter.ToInt32(good, 0x7C);
            int[] bad = { -4, good.Length, good.Length - 8, int.MaxValue };
            for (int i = 0; i < bad.Length; i++)
            {
                byte[] d = (byte[])good.Clone();
                BitConverter.GetBytes(bad[i]).CopyTo(d, diro + 2);
                Ok(ResBlock(SavesPut("O" + i + ".RES", d), 1) == null, "first data offset " + bad[i] + ": null");
            }
        }

        [Test] static void Saves_ResBlockNoFileIsNullNeverThrows()
        {
            Ok(ResBlock(Path.Combine(ExeDir, "NOPE.RES"), 1775) == null, "a missing file");
            Ok(ResBlock(Path.Combine(Path.Combine(ExeDir, "nodir"), "SAVE01.RES"), 1775) == null, "a missing folder");
            Ok(ResBlock(TestDir("ADIR.RES"), 1775) == null, "a folder");
            Ok(ResBlock("", 1775) == null, "an empty path");
            Ok(ResBlock(null, 1775) == null, "a null path");
        }

        // ---------------------------------------------------------------- SaveFile, SaveSlots
        [Test] static void Saves_SaveFileNames()
        {
            string dir = Path.Combine(ExeDir, "TNOVA");
            Eq(Path.Combine(dir, "SAVE01.RES"), SaveFile(dir, 1), "slot 1");
            Eq(Path.Combine(dir, "SAVE09.RES"), SaveFile(dir, 9), "slot 9");
            Eq(Path.Combine(dir, "SAVE00.RES"), SaveFile(dir, 0), "slot 0 (nothing selected in the list: the dialog stops at slot < 1)");
            Eq(Path.Combine(dir, "SAVE10.RES"), SaveFile(dir, 10), "slot 10");
        }

        [Test] static void Saves_SlotsEmptyFolder()
        {
            EqList(SavesEmpty(), SaveSlots(SavesTnova()), "a TNOVA folder without saves");
            EqList(SavesEmpty(), SaveSlots(Path.Combine(ExeDir, "NOGAME")), "a folder that does not exist");
        }

        [Test] static void Saves_SlotsNameAndMission()
        {
            string dir = SavesTnova();
            PutFile(SaveFile(dir, 1), SavesSave("Base camp", 7));
            List<string> e = SavesEmpty();
            e[0] = "1  Base camp  (mission 05)";      // the mission field minus 2
            EqList(e, SaveSlots(dir), "slot 1 used");
        }

        [Test] static void Saves_SlotsMixed()
        {
            string dir = SavesTnova();
            PutFile(SaveFile(dir, 2), SavesSave("Sector 7", 14));
            PutFile(SaveFile(dir, 4), SavesSave(SavesName("No head"), null));
            PutFile(SaveFile(dir, 6), "not a save");
            PutFile(SaveFile(dir, 9), SavesSave("Last", 38));
            List<string> e = SavesEmpty();
            e[1] = "2  Sector 7  (mission 12)";
            e[3] = "4  No head";
            e[5] = "6  (unreadable)";
            e[8] = "9  Last  (mission 36)";
            EqList(e, SaveSlots(dir), "4 slots used");
        }

        [Test] static void Saves_SlotsMissionNumbers()
        {
            string dir = SavesTnova();
            int[] fields = { 2, 3, 4, 11, 12, 21, 27, 101, 102 };
            string[] shown = { "00", "01", "02", "09", "10", "19", "25", "99", "100" };
            for (int i = 0; i < 9; i++) PutFile(SaveFile(dir, i + 1), SavesSave("M", fields[i]));
            List<string> l = SaveSlots(dir);
            for (int i = 0; i < 9; i++)
                Eq((i + 1) + "  M  (mission " + shown[i] + ")", l[i], "mission field " + fields[i] + " (two digits at least)");
        }

        [Test] static void Saves_SlotsNoMissionWhenHeadBad()
        {
            string dir = SavesTnova();
            byte[] name = SavesName("Alpha");
            PutFile(SaveFile(dir, 1), SavesSave(name, null));                                           // no head block
            PutFile(SaveFile(dir, 2), SavesSave(name, SavesHead(SavesHeadMagic, 6, 11)));               // 11 bytes
            PutFile(SaveFile(dir, 3), SavesSave(name, SavesHead(SavesHeadMagic, 6, 12)));               // 12: enough
            PutFile(SaveFile(dir, 4), SavesSave(name, SavesHead(SavesHeadMagic + 1, 6, 16)));           // other magic
            PutFile(SaveFile(dir, 5), SavesSave(name, SavesHead(0x1800EF06, 6, 16)));                  // byte-swapped
            PutFile(SaveFile(dir, 6), SavesSave(name, new byte[0]));                                    // empty head
            PutFile(SaveFile(dir, 7), SavesRes(SavesBlk(1775, name),                                    // head packed
                SavesPackedBlk(1776, SavesHead(SavesHeadMagic, 6, 16), 64, 0)));
            PutFile(SaveFile(dir, 8), SavesRes(SavesBlk(1775, name), SavesBlk(1777, SavesHead(SavesHeadMagic, 6, 16))));   // not 1776
            List<string> e = SavesEmpty();
            for (int i = 0; i < 8; i++) e[i] = (i + 1) + "  Alpha";
            e[2] = "3  Alpha  (mission 04)";
            EqList(e, SaveSlots(dir), "the mission only with a head block of 12 bytes or more starting with 0x06EF0018");
        }

        [Test] static void Saves_SlotsUnreadable()
        {
            string dir = SavesTnova();
            byte[] save = SavesSave("Cut", 5);
            byte[] cut = new byte[BitConverter.ToInt32(save, 0x7C) + 6];   // the directory's entries (at the end) are lost
            Array.Copy(save, cut, cut.Length);
            byte[] v1 = (byte[])save.Clone();
            Encoding.ASCII.GetBytes("LG Res File v1").CopyTo(v1, 0);
            PutFile(SaveFile(dir, 1), new byte[0]);
            PutFile(SaveFile(dir, 2), "not a save, more than 128 bytes " + new string('.', 200));
            PutFile(SaveFile(dir, 3), cut);
            PutFile(SaveFile(dir, 4), v1);
            PutFile(SaveFile(dir, 5), SavesRes(SavesBlk(1000, SavesFill(8, 1))));   // neither name nor head block
            List<string> e = SavesEmpty();
            for (int i = 0; i < 5; i++) e[i] = (i + 1) + "  (unreadable)";
            EqList(e, SaveSlots(dir), "files that are there but cannot be read");
        }

        [Test] static void Saves_SlotsUnreadableNameStillShowsMission()
        {
            string dir = SavesTnova();
            byte[] head = SavesHead(SavesHeadMagic, 9, 16);
            PutFile(SaveFile(dir, 2), SavesSave(null, head));                                             // no name block
            PutFile(SaveFile(dir, 3), SavesRes(SavesPackedBlk(1775, SavesFill(5, 1), 32, 0), SavesBlk(1776, head)));   // packed
            List<string> e = SavesEmpty();
            e[1] = "2  (unreadable)  (mission 07)";
            e[2] = "3  (unreadable)  (mission 07)";
            EqList(e, SaveSlots(dir), "the name block missing or packed, the head block fine");
        }

        [Test] static void Saves_SlotsNameTrimsTrailingNuls()
        {
            string dir = SavesTnova();
            byte[] head = SavesHead(SavesHeadMagic, 3, 16);
            PutFile(SaveFile(dir, 1), SavesSave(SavesName("Alpha"), head));                                      // + 27 NULs
            PutFile(SaveFile(dir, 2), SavesSave(new byte[32], head));                                            // all NULs
            PutFile(SaveFile(dir, 3), SavesSave(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345"), head));   // no NUL
            PutFile(SaveFile(dir, 4), SavesSave(Encoding.ASCII.GetBytes("Z"), head));                            // 1 byte
            PutFile(SaveFile(dir, 5), SavesSave(new byte[0], head));                                             // 0 bytes
            List<string> e = SavesEmpty();
            e[0] = "1  Alpha  (mission 01)";
            e[1] = "2    (mission 01)";                 // an empty name: nothing between the slot and the mission
            e[2] = "3  ABCDEFGHIJKLMNOPQRSTUVWXYZ012345  (mission 01)";
            e[3] = "4  Z  (mission 01)";
            e[4] = "5    (mission 01)";
            EqList(e, SaveSlots(dir), "names");
        }

        [Test] static void Saves_SlotsNameWithBytesAfterItsNul()
        {
            // a fixed-size name buffer that held a longer name before: "Short", NUL, the end of the old one. A ListBox
            // shows a string up to its first NUL: the line must show the slot and the name there (see the report on the
            // mission put after the old bytes); what follows the NUL is not checked here.
            string dir = SavesTnova();
            byte[] name = SavesName("Short");
            Encoding.ASCII.GetBytes("er name of before").CopyTo(name, 6);
            PutFile(SaveFile(dir, 5), SavesSave(name, SavesHead(SavesHeadMagic, 6, 16)));
            string l = SaveSlots(dir)[4];
            int z = l.IndexOf('\0');
            string shown = z < 0 ? l : l.Substring(0, z);
            Ok(shown.StartsWith("5  Short", StringComparison.Ordinal), "shown part " + Show(shown));
            Ok(l.EndsWith("  (mission 04)", StringComparison.Ordinal), "the mission is still in the line " + Show(l));
            HasNot(shown, "name of before", "the old bytes after the NUL are not shown");
        }

        [Test] static void Saves_SlotsNonAsciiNameStillListed()
        {
            // a byte >= 0x80 in the name (an accented letter of the DOS code page): one character in its place
            string dir = SavesTnova();
            byte[] name = SavesName("Op ration");
            name[2] = 0x82;                         // CP437 e acute
            PutFile(SaveFile(dir, 2), SavesSave(name, SavesHead(SavesHeadMagic, 5, 16)));
            string l = SaveSlots(dir)[1];
            Ok(l.StartsWith("2  Op", StringComparison.Ordinal) && l.EndsWith("ration  (mission 03)", StringComparison.Ordinal), "line " + Show(l));
            Eq("2  Op?ration  (mission 03)".Length, l.Length, "length of " + Show(l) + ": one character for the byte");
        }

        [Test] static void Saves_SlotsOtherFilesIgnored()
        {
            string dir = SavesTnova();
            byte[] save = SavesSave("Other", 4);
            foreach (string n in new[] { "SAVE00.RES", "SAVE10.RES", "SAVE1.RES", "SAVE01.BAK", "SAVE01.RES.OLD", "SAVEGAME.RES", "SAVE05" })
                PutFile(Path.Combine(dir, n), save);
            TestDir("game", "TNOVA", "SAVE02.RES");          // a folder with a save's name
            PutFile(SaveFile(dir, 3), SavesSave("Real", 4));
            List<string> e = SavesEmpty();
            e[2] = "3  Real  (mission 02)";
            EqList(e, SaveSlots(dir), "only SAVE01.RES to SAVE09.RES count, and only files");
        }

        [Test] static void Saves_SlotsDeleteFreesTheSlot()
        {
            // the DELETE button without its dialogs: BackupSave on the slot's file, then the list again
            string dir = SavesTnova();
            PutFile(SaveFile(dir, 3), SavesSave("Third", 5));
            PutFile(SaveFile(dir, 4), SavesSave("Fourth", 6));
            List<string> e = SavesEmpty();
            e[2] = "3  Third  (mission 03)";
            e[3] = "4  Fourth  (mission 04)";
            EqList(e, SaveSlots(dir), "before");
            string to = BackupSave(SaveFile(dir, 3), 3);
            e[2] = "3  (empty)";
            EqList(e, SaveSlots(dir), "after deleting slot 3");
            EqBytes(SavesName("Third"), ResBlock(to, 1775), "the backup is the same save, still readable");
        }

        // ---------------------------------------------------------------- BackupSave
        [Test] static void Saves_BackupMovesTheSave()
        {
            string dir = SavesTnova();
            byte[] save = SavesSave("Base camp", 9);
            string f = PutFile(SaveFile(dir, 7), save);
            string bk = Path.Combine(ExeDir, "save backups");
            No(Directory.Exists(bk), "no backups folder at first");
            DateTime before = DateTime.Now;
            string to = BackupSave(f, 7);
            DateTime after = DateTime.Now;
            Ok(Directory.Exists(bk), "the save backups folder is made next to TNPlus.exe (ExeDir)");
            string name = Path.GetFileName(to);
            Eq(Path.Combine(bk, name), to, "the returned file is in it");
            No(File.Exists(f), "the save is gone from its slot");
            Ok(File.Exists(to), "the backup is there");
            EqBytes(save, File.ReadAllBytes(to), "same content");
            Ok(Regex.IsMatch(name, "^SAVE07 [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}-[0-9]{2}-[0-9]{2}\\.RES$"), "name " + Show(name));
            DateTime at = DateTime.ParseExact(name.Substring(7, 19), "yyyy-MM-dd HH-mm-ss", CultureInfo.CurrentCulture);
            DateTime from = new DateTime(before.Ticks - before.Ticks % TimeSpan.TicksPerSecond);
            Ok(at >= from && at <= after, "time in the name " + at.ToString("s") + " between " + from.ToString("s") + " and " + after.ToString("s"));
            string[] files = Directory.GetFiles(bk);
            Eq(1, files.Length, "one file in save backups");
        }

        [Test] static void Saves_BackupKeepsOtherBackups()
        {
            string dir = SavesTnova(), bk = TestDir("save backups");
            string old = PutFile(Path.Combine(bk, "SAVE03 2020-01-01 00-00-00.RES"), new byte[] { 1, 2, 3 });
            string note = PutFile(Path.Combine(bk, "notes.txt"), "mine");
            byte[] s3 = SavesSave("Three", 4), s4 = SavesSave("Four", 4);
            PutFile(SaveFile(dir, 3), s3);
            PutFile(SaveFile(dir, 4), s4);
            string t3 = BackupSave(SaveFile(dir, 3), 3), t4 = BackupSave(SaveFile(dir, 4), 4);
            Ok(t3 != t4, "two slots backed up in the same second: two names");
            Ok(Path.GetFileName(t3).StartsWith("SAVE03 ", StringComparison.Ordinal), "slot 3: " + t3);
            Ok(Path.GetFileName(t4).StartsWith("SAVE04 ", StringComparison.Ordinal), "slot 4: " + t4);
            EqBytes(s3, File.ReadAllBytes(t3), "backup of slot 3");
            EqBytes(s4, File.ReadAllBytes(t4), "backup of slot 4");
            EqBytes(new byte[] { 1, 2, 3 }, File.ReadAllBytes(old), "an older backup of slot 3 is kept");
            Eq("mine", File.ReadAllText(note), "another file of the folder is kept");
            Eq(4, Directory.GetFiles(bk).Length, "files in save backups");
        }

        [Test] static void Saves_BackupNeverOverwrites()
        {
            // a backup of the same slot already there for every second the call can take: the move fails, nothing is lost
            string dir = SavesTnova(), bk = TestDir("save backups");
            byte[] save = SavesSave("New", 5);
            string f = PutFile(SaveFile(dir, 5), save);
            DateTime now = DateTime.Now;
            List<string> made = new List<string>();
            for (int s = -2; s <= 30; s++)
                made.Add(PutFile(Path.Combine(bk, "SAVE05 " + now.AddSeconds(s).ToString("yyyy-MM-dd HH-mm-ss") + ".RES"), new byte[] { 0xBB }));
            Throws<IOException>(delegate { BackupSave(f, 5); }, "a backup of that name already there");
            Ok(File.Exists(f), "the save is still in its slot");
            EqBytes(save, File.ReadAllBytes(f), "unchanged");
            foreach (string m in made) EqBytes(new byte[] { 0xBB }, File.ReadAllBytes(m), "earlier backup " + Path.GetFileName(m) + " untouched");
        }

        [Test] static void Saves_BackupMissingSaveThrows()
        {
            string dir = SavesTnova();
            Throws<IOException>(delegate { BackupSave(SaveFile(dir, 2), 2); }, "no save in slot 2 (FileNotFoundException is an IOException)");
            string bk = Path.Combine(ExeDir, "save backups");
            Eq(0, Directory.Exists(bk) ? Directory.GetFiles(bk).Length : 0, "no backup made");
        }

        // ---------------------------------------------------------------- NewerVersion, ReleaseTag, VERSION
        [Test] static void Saves_VersionEqualIsNotNewer()
        {
            SavesNewer(false, "1.2.3", "1.2.3");
            SavesNewer(false, "0.1", "0.1");
            SavesNewer(false, "10.20.30.40", "10.20.30.40");
            SavesNewer(false, "1.02.3", "1.2.3");       // the same numbers
        }

        [Test] static void Saves_VersionNewer()
        {
            SavesNewer(true, "1.2.4", "1.2.3");         // patch
            SavesNewer(true, "1.3.0", "1.2.3");         // minor
            SavesNewer(true, "1.3.0", "1.2.9");
            SavesNewer(true, "2.0.0", "1.2.3");         // major
            SavesNewer(true, "2.0.0", "1.99.99");
            SavesNewer(true, "1.10.0", "1.9.0");        // numbers, not text
            SavesNewer(true, "1.2.10", "1.2.9");
        }

        [Test] static void Saves_VersionOlderIsNotNewer()
        {
            SavesNewer(false, "1.2.2", "1.2.3");
            SavesNewer(false, "1.1.9", "1.2.3");
            SavesNewer(false, "0.9.9", "1.2.3");
            SavesNewer(false, "1.9.0", "1.10.0");
            SavesNewer(false, "1.2.9", "1.2.10");
        }

        [Test] static void Saves_VersionPartCounts()
        {
            // 2, 3 and 4 numbers; only pairs whose answer does not hang on a missing number (1.2 vs 1.2.0: see the report)
            SavesNewer(true, "1.3", "1.2.3");
            SavesNewer(false, "1.2", "1.2.3");
            SavesNewer(true, "2.0", "1.2.3");
            SavesNewer(true, "1.2.4", "1.2");
            SavesNewer(false, "1.1.9", "1.2");
            SavesNewer(true, "1.2.3.1", "1.2.3");
            SavesNewer(false, "1.2.2.9", "1.2.3");
            SavesNewer(true, "1.2.4", "1.2.3.9");
            SavesNewer(false, "1.2.3", "1.2.3.1");
        }

        [Test] static void Saves_VersionCurrentWithSuffix()
        {
            // the coop builds: "1.2.3-coop3" is compared as 1.2.3
            SavesNewer(false, "1.2.3", "1.2.3-coop3");
            SavesNewer(true, "1.2.4", "1.2.3-coop3");
            SavesNewer(true, "1.3.0", "1.2.3-coop3");
            SavesNewer(false, "1.2.2", "1.2.3-coop3");
            SavesNewer(false, "1.2.3", "1.2.3-beta-2");
        }

        [Test] static void Saves_VersionGarbageIsFalse()
        {
            SavesNewer(false, null, "1.2.3");
            SavesNewer(false, "", "1.2.3");
            SavesNewer(false, "abc", "1.2.3");
            SavesNewer(false, "1.2.x", "1.2.3");
            SavesNewer(false, "9.9.9-beta1", "1.2.3");  // ReleaseTag never gives a suffix
            SavesNewer(false, "1.2.3.4.5", "1.2.3");
            SavesNewer(false, "1.2.99999999999", "1.2.3");
            SavesNewer(false, "1.2.4", null);
            SavesNewer(false, "1.2.4", "");
            SavesNewer(false, "1.2.4", "garbage");
            SavesNewer(false, "1.2.4", "-coop3");
        }

        [Test] static void Saves_TagFromGitHubAnswer()
        {
            // the shape of api.github.com/repos/.../releases/latest (shortened)
            string json = "{\n  \"url\": \"https://api.github.com/repos/skw-1337/terra-nova-plus/releases/1\",\n" +
                "  \"html_url\": \"https://github.com/skw-1337/terra-nova-plus/releases/tag/v1.2.4\",\n  \"id\": 123456,\n" +
                "  \"author\": {\n    \"login\": \"skw-1337\",\n    \"id\": 42\n  },\n  \"node_id\": \"RE_kwDOAbCdEf\",\n" +
                "  \"tag_name\": \"v1.2.4\",\n  \"target_commitish\": \"stable\",\n  \"name\": \"Terra Nova Plus 1.2.4\",\n" +
                "  \"draft\": false,\n  \"prerelease\": false,\n  \"assets\": [ { \"name\": \"TerraNovaPlus-9.9.9.zip\", \"size\": 345678 } ],\n" +
                "  \"body\": \"Fixes. Version 9.9.9 is not this one.\"\n}";
            Eq("1.2.4", ReleaseTag(json), "tag v1.2.4");
            Ok(NewerVersion(ReleaseTag(json), "1.2.3"), "1.2.4 is an update for 1.2.3");
            Eq("2.0.1", ReleaseTag(json.Replace("\n", "\r\n").Replace("v1.2.4", "v2.0.1")), "the same with CRLF");
        }

        [Test] static void Saves_TagWithoutVAndSpacing()
        {
            Eq("1.2.3", ReleaseTag("{\"tag_name\":\"1.2.3\"}"), "no v, no spaces");
            Eq("1.2.3", ReleaseTag("{\"tag_name\":\"v1.2.3\"}"), "v, no spaces");
            Eq("1.2.3", ReleaseTag("{ \"tag_name\"  :   \"v1.2.3\" }"), "spaces");
            Eq("1.2.3", ReleaseTag("{\"tag_name\"\r\n\t:\t\r\n \"v1.2.3\"}"), "tabs and line breaks");
            Eq("10.20.30", ReleaseTag("{\"tag_name\": \"v10.20.30\"}"), "several digits");
            Eq("1.3", ReleaseTag("{\"tag_name\": \"v1.3\"}"), "two numbers");
        }

        [Test] static void Saves_TagSuffixAndTrailingDot()
        {
            Eq("1.2.4", ReleaseTag("{\"tag_name\": \"v1.2.4-beta1\"}"), "-beta1 left out");
            Eq("2.0.0", ReleaseTag("{\"tag_name\": \"v2.0.0-rc.1\"}"), "-rc.1 left out");
            Eq("1.2.3", ReleaseTag("{\"tag_name\": \"v1.2.3-coop3\"}"), "-coop3 left out");
            Eq("1.2.3", ReleaseTag("{\"tag_name\": \"v1.2.3+build.5\"}"), "+build.5 left out");
            Eq("1.2.3", ReleaseTag("{\"tag_name\": \"v1.2.3 final\"}"), "text after a space left out");
            Eq("1.2", ReleaseTag("{\"tag_name\": \"v1.2.\"}"), "a trailing dot");
            Eq("1.2.3", ReleaseTag("{\"tag_name\": \"v1.2.3..\"}"), "two trailing dots");
            Eq("1.2.3", ReleaseTag("{\"tag_name\": \"v1.2.3.-x\"}"), "a dot before the suffix");
        }

        [Test] static void Saves_TagMissingIsNull()
        {
            string[] none = {
                "", "{}", "{\"name\": \"1.2.3\"}", "{\"tag_name\": null}", "{\"tag_name\": \"\"}", "{\"tag_name\": \"v\"}",
                "{\"tag_name\": \"latest\"}", "{\"tag_name\": \"release-1.2\"}", "{\"tag_name\": \"v.1\"}", "{\"tag_name\": \"vv1.2\"}",
                "{\"tag_name\": 123}", "{\"old_tag_name\": \"v9.9.9\"}", "{\"Tag_Name\": \"v9.9.9\"}", "{\"tag_name \": \"v9.9.9\"}",
                "{\"message\": \"API rate limit exceeded for 1.2.3.4.\", \"documentation_url\": \"https://docs.github.com/rest\"}",
                "<html><body>502 Bad Gateway</body></html>" };
            foreach (string j in none) Ok(ReleaseTag(j) == null, "no tag in " + Show(j) + ": null, got " + Show(ReleaseTag(j)));
        }

        [Test] static void Saves_TagFirstOfSeveral()
        {
            Eq("1.0.0", ReleaseTag("[{\"tag_name\": \"v1.0.0\"}, {\"tag_name\": \"v2.0.0\"}]"), "two: the first");
            // in a text, the quotes are escaped (\"tag_name\"): not a key
            Eq("1.2.4", ReleaseTag("{\"body\": \"see \\\"tag_name\\\": \\\"v9.9.9\\\"\", \"tag_name\": \"v1.2.4\"}"), "an escaped one in the body before the key");
        }

        [Test] static void Saves_TagNullJsonThrows()
        {
            // GitHub's answer is never null (StreamReader.ReadToEnd): LatestRelease's catch is what covers it
            Throws<ArgumentNullException>(delegate { ReleaseTag(null); }, "ReleaseTag(null)");
        }

        [Test] static void Saves_VersionConstant()
        {
            string cur = VERSION.Split('-')[0];
            Version v = null;
            try { v = new Version(cur); } catch (Exception e) { Fail("VERSION " + Show(VERSION) + " is not a version: " + e.Message); }
            Ok(v.Build >= 0 && v.Revision < 0, "VERSION has 3 numbers like the release tags (vX.Y.Z): " + Show(VERSION));
            No(NewerVersion(cur, VERSION), "VERSION is not newer than itself");
            No(NewerVersion(VERSION, VERSION), "VERSION (as the latest) is not newer than itself");
            Eq(cur, ReleaseTag("{\"tag_name\": \"v" + VERSION + "\"}"), "its release tag gives it back");
            Ok(NewerVersion(new Version(v.Major, v.Minor, v.Build + 1).ToString(), VERSION), "the next patch is an update");
            Ok(NewerVersion(new Version(v.Major + 1, 0, 0).ToString(), VERSION), "the next major is an update");
        }

        [Test] static void Saves_VersionAttributesFollowVersion()
        {
            // the exe's version (Explorer, file properties) and the tool's own VERSION: the same numbers
            Version cur = new Version(VERSION.Split('-')[0]);
            Assembly asm = Assembly.GetExecutingAssembly();
            Version av = asm.GetName().Version;
            Eq(cur.ToString(), new Version(av.Major, av.Minor, av.Build).ToString(), "AssemblyVersion " + av + " vs VERSION " + VERSION);
            object[] fv = asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
            Eq(1, fv.Length, "one AssemblyFileVersion");
            Version f = new Version(((AssemblyFileVersionAttribute)fv[0]).Version);
            Eq(cur.ToString(), new Version(f.Major, f.Minor, f.Build).ToString(), "AssemblyFileVersion " + f + " vs VERSION " + VERSION);
            object[] iv = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            Eq(1, iv.Length, "one AssemblyInformationalVersion");
            string info = ((AssemblyInformationalVersionAttribute)iv[0]).InformationalVersion;
            Ok(info == cur.ToString() || info.StartsWith(cur + "-", StringComparison.Ordinal), "AssemblyInformationalVersion " + Show(info) + " vs VERSION " + VERSION);
        }
    }
}
