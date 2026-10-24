// Terra Nova Plus tests - the memory patches on made-up game memory (FakeGame): TryFix / TryPieces and the fixes built
// on them, object distance, terrain rings, periodic fixes, smooth terrain, in-place rewrites, zoom fix, HD injection, span fix.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

static partial class TNPlus
{
    internal static partial class Tests
    {
        // ================================================================ helpers (Patch*)
        // what the tool printed (Say...) while a runs; also passed on to the runner's console (shown on a failure)
        static string PatchSaid(Action a)
        {
            TextWriter old = Console.Out;
            StringWriter sw = new StringWriter();
            Console.SetOut(sw);
            try { a(); }
            finally { Console.SetOut(old); old.Write(sw.ToString()); }
            return sw.ToString();
        }

        // the prepared exe's free area is there: the game's pool variable holds the pool base (French or English game)
        static void PatchPool(FakeGame g, bool english)
        {
            if (english) g.PutUInt(HdPayloadEn.PoolVar, HdPayloadEn.PoolBase);
            else g.PutUInt(HdPayloadFr.PoolVar, HdPayloadFr.PoolBase);
        }

        static void PatchPutAll(FakeGame g, uint[] at, string[] hex) { for (int i = 0; i < at.Length; i++) g.Put(at[i], hex[i]); }
        static bool PatchIs(FakeGame g, uint at, string hex) { byte[] b = Hex(hex); return Same(g.Get(at, b.Length), b); }

        static string PatchSha(byte[] d)
        {
            using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(d)).Replace("-", "").ToLowerInvariant();
        }

        static void PatchShort(byte[] b, int at, int v) { BitConverter.GetBytes((short)v).CopyTo(b, at); }

        // a ring block as the game keeps it for a camera (0xA8 bytes): ring count at +0x5A, index of the first type-0 ring at
        // +0x58, the rings at +0x5C, 12 bytes each: type, (from, to) x the camera's zoom scale, step, (from, to), a zero byte.
        // Everything else is filler, to see what gets written.
        static byte[] PatchRingBlock(int count, int firstFlat, int[] types, int[] steps, int[] ends, double zoom)
        {
            byte[] b = new byte[0xA8];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)(0x40 + i % 0x31);
            PatchShort(b, 0x58, firstFlat);
            PatchShort(b, 0x5A, count);
            int from = 0;
            for (int k = 0; k < types.Length; k++)
            {
                int o = 0x5C + 12 * k;
                b[o] = (byte)types[k];
                PatchShort(b, o + 1, (int)Math.Round(from * zoom)); PatchShort(b, o + 3, (int)Math.Round(ends[k] * zoom));
                PatchShort(b, o + 5, steps[k]);
                PatchShort(b, o + 7, from); PatchShort(b, o + 9, ends[k]);
                b[o + 11] = 0;
                from = ends[k];
            }
            return b;
        }

        // the main view's terrain camera as the game loads it: 6 rings, types 1 2 0 0 0 0, steps 1 1 1 2 4 8
        static readonly int[] PatchTypes6 = { 1, 2, 0, 0, 0, 0 }, PatchSteps6 = { 1, 1, 1, 2, 4, 8 };
        static readonly int[] PatchGameEnds6 = { 4, 12, 30, 64, 100, 240 };
        static byte[] PatchMainRings(int[] ends) { return PatchRingBlock(6, 2, PatchTypes6, PatchSteps6, ends, 1); }

        // a cockpit camera (MFD view) as the game loads it: 5 rings, types 2 0 0 0 0, steps 1 1 2 2 4
        static readonly int[] PatchSmallTypes = { 2, 0, 0, 0, 0 }, PatchSmallGameSteps = { 1, 1, 2, 2, 4 };
        static readonly int[] PatchSmallGameEnds = { 9, 20, 40, 80, 160 };
        static byte[] PatchSmallRings(double zoom) { return PatchRingBlock(5, 1, PatchSmallTypes, PatchSmallGameSteps, PatchSmallGameEnds, zoom); }

        // cockpit camera s of the HD 1280x720 module: pointer at SvPtr + 4 + 4 s to a block whose first dword is the camera's
        // ring block (HdPayload.Use(..., true, true) first)
        static uint PatchSmallCam(FakeGame g, int s, byte[] rings)
        {
            uint blk = 0x300000 + (uint)s * 0x100, v = 0x310000 + (uint)s * 0x1000;
            g.PutUInt(HdPayload.SvPtr + 4 + 4 * (uint)s, blk);
            g.PutUInt(blk, v);
            g.Put(v, rings);
            return v;
        }

        // the call site of the view reset that TryZoomFix looks for: mov edx,4 / mov ebx,[mode] / mov eax,camera /
        // sub edx,ebx / xor ebx,ebx / xor ecx,ecx / call target; returns the address of the call
        static uint PatchZoomSite(FakeGame g, uint site, uint cam, uint mode, uint target)
        {
            uint hook = site + 22;
            g.Put(site, "BA04000000 8B1D"); g.PutUInt(site + 7, mode);
            g.Put(site + 11, "B8"); g.PutUInt(site + 12, cam);
            g.Put(site + 16, "29DA 31DB 31C9 E8"); g.PutInt(site + 23, (int)target - (int)(hook + 5));
            return hook;
        }

        // the made-up game that TryHdInject expects (HdPayload.Use first): the game's bytes at every patch, the pool there
        static void PatchHdGame(FakeGame g)
        {
            for (int i = 0; i < HdPayload.PatchAt.Length; i++) g.Put(HdPayload.PatchAt[i], HdPayload.PatchOld[i]);
            g.PutUInt(HdPayload.PoolVar, HdPayload.PoolBase);
        }

        // made-up fix for TryFix: the French exe's place, then the English one's
        static readonly uint[] PatchFixAt = { 0x100000, 0x100100 };
        static readonly byte[] PatchFixOld = { 0x89, 0xC2, 0xC1, 0xFA }, PatchFixNew = { 0x31, 0xED, 0x90, 0x90 };
        static int PatchFix() { return TryFix(PatchFixAt, PatchFixOld, PatchFixNew, "Made-up fix", "it works"); }

        // made-up fix for TryPieces, two executables: a code area (free: zeros) then a hook each
        static readonly uint[][] PatchPieceAt = { new uint[] { 0x100000, 0x100100 }, new uint[] { 0x100010, 0x100110 } };
        static readonly string[][] PatchPieceOld = { new[] { "00000000", "E812345678" }, new[] { "00000000", "E8ABCDEF01" } };
        static readonly string[][] PatchPieceNew = { new[] { "C3909090", "E8FBFEFFFF" }, new[] { "C3CCCCCC", "E8EBFEFFFF" } };
        static int PatchPieces() { return TryPieces(PatchPieceAt, PatchPieceOld, PatchPieceNew, "Made-up fix", "it works"); }

        // ================================================================ FreeAreaReady
        [Test] static void Patch_FreeArea_PoolAtItsBaseInEitherExe()
        {
            using (FakeGame g = new FakeGame())
            {
                No(FreeAreaReady(), "no pool yet (zeros)");
                PatchPool(g, false);
                Ok(FreeAreaReady(), "French pool variable = pool base");
                g.PutUInt(HdPayloadFr.PoolVar, 0);
                PatchPool(g, true);
                Ok(FreeAreaReady(), "English pool variable = pool base");
            }
        }

        [Test] static void Patch_FreeArea_OtherPoolValuesAreNotReady()
        {
            using (FakeGame g = new FakeGame())
            {
                // exe not prepared: the game puts its pool where the free area would be, or anywhere else
                foreach (uint v in new uint[] { HdPayloadFr.PoolBase - 0x6000, HdPayloadFr.PoolBase + 4, HdPayloadFr.PoolBase - 1, 0x01000000 })
                {
                    g.PutUInt(HdPayloadFr.PoolVar, v); g.PutUInt(HdPayloadEn.PoolVar, v);
                    No(FreeAreaReady(), "pool at 0x" + v.ToString("X"));
                }
            }
        }

        // ================================================================ TryFix
        [Test] static void Patch_TryFix_OldBytesBecomeNew()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(PatchFixAt[0], PatchFixOld);
                int r = 0;
                string said = PatchSaid(delegate { r = PatchFix(); });
                Eq(1, r, "result");
                EqBytes(PatchFixNew, g.Get(PatchFixAt[0], 4), "new bytes at the first address");
                Ok(g.AllZero(PatchFixAt[1], 4), "second address untouched");
                Has(said, "Made-up fix ON: it works", "said");
            }
        }

        [Test] static void Patch_TryFix_SecondAddressForTheOtherExe()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(PatchFixAt[1], PatchFixOld);
                Eq(1, PatchFix(), "result");
                EqBytes(PatchFixNew, g.Get(PatchFixAt[1], 4), "new bytes at the second address");
                Ok(g.AllZero(PatchFixAt[0], 4), "first address untouched");
            }
        }

        [Test] static void Patch_TryFix_AlreadyNewIsOnQuietly()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(PatchFixAt[0], PatchFixNew);
                g.Put(PatchFixAt[1], PatchFixOld);      // never looked at: the first exe already has the fix
                int r = 0;
                string said = PatchSaid(delegate { r = PatchFix(); });
                Eq(1, r, "result");
                Eq("", said, "nothing said");
                EqBytes(PatchFixOld, g.Get(PatchFixAt[1], 4), "second address left as it is");
            }
        }

        [Test] static void Patch_TryFix_UnknownBytesNothingWritten()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(PatchFixAt[0], "89C2C1FB");       // the old bytes but the last one
                g.Put(PatchFixAt[1], "31ED9091");       // the new bytes but the last one
                int r = 0;
                string said = PatchSaid(delegate { r = PatchFix(); });
                Eq(-1, r, "result");
                Has(said, "Made-up fix unavailable: unsupported game version", "said");
                EqBytes(Bytes("89C2C1FB"), g.Get(PatchFixAt[0], 4), "first address untouched");
                EqBytes(Bytes("31ED9091"), g.Get(PatchFixAt[1], 4), "second address untouched");
            }
        }

        // ================================================================ hit fix, physics fix chain
        [Test] static void Patch_HitFix_FrenchThenEnglishExe()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(0x3117D3, HITFIX_OLD);
                TryHitFix();
                Eq(1, hitFixState, "French: state");
                EqBytes(Bytes("8D4418FF 99 F7FB 909090909090"), g.Get(0x3117D3, 13), "French: lea eax,[eax+ebx-1] / cdq / idiv ebx / nop x6");
                Ok(g.AllZero(0x311553, 13), "French: the English place untouched");
            }
            hitFixState = 0;
            using (FakeGame g = new FakeGame())
            {
                g.Put(0x311553, HITFIX_OLD);
                TryHitFix();
                Eq(1, hitFixState, "English: state");
                EqBytes(HITFIX_NEW, g.Get(0x311553, 13), "English: new bytes");
                Ok(g.AllZero(0x3117D3, 13), "English: the French place untouched");
            }
            hitFixState = 0;
            using (FakeGame g = new FakeGame())
            {
                PatchSaid(TryHitFix);
                Eq(-1, hitFixState, "neither exe: unavailable");
            }
        }

        static void PatchPhysGame(FakeGame g, bool english)
        {
            g.Put(PHYSFIX_AT[english ? 1 : 0], PHYSFIX_OLD);
            g.Put(PHYSFIX2_AT[english ? 1 : 0], PHYSFIX2_OLD);
            g.Put(IDLEFIX2_AT[0], IDLEFIX2_OLD);        // same place in both exe
            g.Put(IDLEFIX_AT[0], IDLEFIX_OLD);
        }

        [Test] static void Patch_PhysFix_AllFourPartsFrench()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPhysGame(g, false);
                string said = PatchSaid(TryPhysFix);
                Eq(1, physFixState, "state");
                EqBytes(Bytes("31ED892D"), g.Get(0x3121F7, 4), "remainder cleared: jmp +6 -> xor ebp,ebp");
                EqBytes(Bytes("81FD00000000"), g.Get(0x3121DA, 6), "cmp ebp,0x28F -> cmp ebp,0");
                EqBytes(IDLEFIX2_NEW, g.Get(0x280CB4, IDLEFIX2_NEW.Length), "idle turn step");
                EqBytes(IDLEFIX_NEW, g.Get(0x280C58, IDLEFIX_NEW.Length), "idle gesture chance");
                foreach (string n in new[] { "Physics speed fix ON", "Physics smoothing ON", "Idle turn speed ON", "Idle animation fix ON" }) Has(said, n, "said");
            }
        }

        [Test] static void Patch_PhysFix_EnglishAddresses()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPhysGame(g, true);
                TryPhysFix();
                Eq(1, physFixState, "state");
                EqBytes(PHYSFIX_NEW, g.Get(0x311F77, 4), "physics fix at 0x311F77");
                EqBytes(PHYSFIX2_NEW, g.Get(0x311F5A, 6), "smoothing at 0x311F5A");
                EqBytes(IDLEFIX2_NEW, g.Get(0x280CB4, IDLEFIX2_NEW.Length), "idle turn step");
                EqBytes(IDLEFIX_NEW, g.Get(0x280C58, IDLEFIX_NEW.Length), "idle gesture chance");
                Ok(g.AllZero(0x3121F7, 4) && g.AllZero(0x3121DA, 6), "French places untouched");
            }
        }

        [Test] static void Patch_PhysFix_UnknownMainPartStopsTheChain()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPhysGame(g, false);
                g.Put(0x3121F7, "EB07892D");            // not the game's jmp +6
                PatchSaid(TryPhysFix);
                Eq(-1, physFixState, "state");
                EqBytes(PHYSFIX2_OLD, g.Get(0x3121DA, 6), "smoothing not applied");
                EqBytes(IDLEFIX2_OLD, g.Get(0x280CB4, IDLEFIX2_OLD.Length), "idle turn not applied");
                EqBytes(IDLEFIX_OLD, g.Get(0x280C58, IDLEFIX_OLD.Length), "idle gestures not applied");
            }
        }

        [Test] static void Patch_PhysFix_SmoothingIsOptional()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPhysGame(g, false);
                g.Put(0x3121DA, "81FD90020000");        // another value than 0x28F
                string said = PatchSaid(TryPhysFix);
                Eq(1, physFixState, "the physics fix stays on");
                EqBytes(Bytes("81FD90020000"), g.Get(0x3121DA, 6), "unknown smoothing bytes left");
                Has(said, "Physics smoothing unavailable", "said");
                EqBytes(IDLEFIX2_NEW, g.Get(0x280CB4, IDLEFIX2_NEW.Length), "idle turn applied anyway");
                EqBytes(IDLEFIX_NEW, g.Get(0x280C58, IDLEFIX_NEW.Length), "idle gestures applied anyway");
            }
        }

        [Test] static void Patch_PhysFix_IdleGesturesNeedTheIdleTurn()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPhysGame(g, false);
                g.Put(0x280CB4, "3D23020000");          // idle turn code not the game's
                PatchSaid(TryPhysFix);
                Eq(1, physFixState, "state");
                EqBytes(PHYSFIX2_NEW, g.Get(0x3121DA, 6), "smoothing applied");
                EqBytes(IDLEFIX_OLD, g.Get(0x280C58, IDLEFIX_OLD.Length), "idle gestures left as the game has them");
            }
            physFixState = 0;
            using (FakeGame g = new FakeGame())
            {
                PatchPhysGame(g, false);
                g.Put(0x280CB4, IDLEFIX2_NEW);          // idle turn already fixed (TN+ attached again): gestures follow
                TryPhysFix();
                EqBytes(IDLEFIX_NEW, g.Get(0x280C58, IDLEFIX_NEW.Length), "idle gestures applied");
            }
        }

        // ================================================================ TryPieces
        [Test] static void Patch_TryPieces_WaitsForTheFreeArea()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPutAll(g, PatchPieceAt[0], PatchPieceOld[0]);
                int r = 9;
                string said = PatchSaid(delegate { r = PatchPieces(); });
                Eq(0, r, "0: look again later");
                Eq("", said, "nothing said");
                Ok(g.AllZero(0x100000, 4), "code area not written");
                Ok(PatchIs(g, 0x100100, "E812345678"), "hook not written");
            }
        }

        [Test] static void Patch_TryPieces_AllPiecesOfTheFirstExe()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                PatchPutAll(g, PatchPieceAt[0], PatchPieceOld[0]);
                int r = 0;
                string said = PatchSaid(delegate { r = PatchPieces(); });
                Eq(1, r, "result");
                Ok(PatchIs(g, 0x100000, "C3909090"), "code written");
                Ok(PatchIs(g, 0x100100, "E8FBFEFFFF"), "hook written");
                Ok(g.AllZero(0x100010, 4) && g.AllZero(0x100110, 5), "the other exe's places untouched");
                Has(said, "Made-up fix ON: it works", "said");
            }
        }

        [Test] static void Patch_TryPieces_OtherExeAllOrNothing()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, true);                     // either pool variable will do
                PatchPutAll(g, PatchPieceAt[1], PatchPieceOld[1]);
                Eq(1, PatchPieces(), "result");
                Ok(PatchIs(g, 0x100010, "C3CCCCCC") && PatchIs(g, 0x100110, "E8EBFEFFFF"), "second exe's pieces written");
                // the first exe's code area matched (free) but its hook did not: none of its pieces written
                Ok(g.AllZero(0x100000, 4), "first exe's code area left free");
                Ok(g.AllZero(0x100100, 5), "first exe's hook place untouched");
            }
        }

        [Test] static void Patch_TryPieces_AlreadyNewIsOnQuietly()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                PatchPutAll(g, PatchPieceAt[0], PatchPieceNew[0]);
                int r = 0;
                string said = PatchSaid(delegate { r = PatchPieces(); });
                Eq(1, r, "result");
                Eq("", said, "nothing said");
            }
        }

        [Test] static void Patch_TryPieces_MixOfOldAndNewCompleted()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                g.Put(0x100000, "C3909090");            // code already there (an earlier run), hook still the game's
                g.Put(0x100100, "E812345678");
                int r = 0;
                string said = PatchSaid(delegate { r = PatchPieces(); });
                Eq(1, r, "result");
                Ok(PatchIs(g, 0x100100, "E8FBFEFFFF"), "hook written");
                Has(said, "Made-up fix ON", "said");
            }
        }

        [Test] static void Patch_TryPieces_UnknownOrTakenNothingWritten()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                g.Put(0x100100, "E812345679");          // first exe: hook not the game's
                g.Put(0x100010, "00000001");            // second exe: hook fine but its code area is not free
                g.Put(0x100110, "E8ABCDEF01");
                int r = 0;
                string said = PatchSaid(delegate { r = PatchPieces(); });
                Eq(-1, r, "result");
                Has(said, "Made-up fix unavailable: unexpected game code or memory", "said");
                Ok(g.AllZero(0x100000, 4), "first exe's code area untouched");
                Ok(PatchIs(g, 0x100010, "00000001") && PatchIs(g, 0x100110, "E8ABCDEF01"), "second exe untouched");
            }
        }

        // ================================================================ fixes on TryPieces, with their own tables
        [Test] static void Patch_TurretFix_FrenchOrEnglishCode()
        {
            No(TurretFix.New[0][0] == TurretFix.New[1][0], "the two exe's code differs (else this test proves nothing)");
            for (int l = 0; l < 2; l++)
            {
                turretFixState = 0;
                using (FakeGame g = new FakeGame())
                {
                    PatchPool(g, l == 1);
                    // the code areas are free (zeros, their Old), the hooks hold the game's bytes
                    for (int i = TurretFix.Zones; i < TurretFix.At[l].Length; i++) g.Put(TurretFix.At[l][i], TurretFix.Old[l][i]);
                    TryTurretFix();
                    Eq(1, turretFixState, "exe " + l + ": state");
                    for (int i = 0; i < TurretFix.At[l].Length; i++)
                        Ok(PatchIs(g, TurretFix.At[l][i], TurretFix.New[l][i]), "exe " + l + ": piece " + i + " at 0x" + TurretFix.At[l][i].ToString("X"));
                }
            }
        }

        [Test] static void Patch_DescramblerAndFillerFix()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                for (int i = DescramblerFix.Zones; i < DescramblerFix.At[0].Length; i++) g.Put(DescramblerFix.At[0][i], DescramblerFix.Old[0][i]);
                PatchPutAll(g, FillerFix.At[1], FillerFix.Old[1]);     // the filler's English bytes in the same made-up game
                TryDescramblerFix();
                TryFillerFix();
                Eq(1, descrFixState, "descrambler state");
                Eq(1, fillerFixState, "filler state");
                for (int i = 0; i < DescramblerFix.At[0].Length; i++) Ok(PatchIs(g, DescramblerFix.At[0][i], DescramblerFix.New[0][i]), "descrambler piece " + i);
                for (int i = 0; i < FillerFix.At[1].Length; i++) Ok(PatchIs(g, FillerFix.At[1][i], FillerFix.New[1][i]), "filler piece " + i);
            }
        }

        [Test] static void Patch_AshfordFix_UnknownExeQuietlyOff()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                string said = PatchSaid(TryAshfordFix);
                Eq(-1, ashfordState, "state");
                Eq("", said, "nothing said: another exe simply has no such call");
                Ok(g.AllZero(AshfordFix.At[0][0], AshfordFix.New[0][0].Length / 2), "nothing written");
            }
        }

        [Test] static void Patch_AshfordFix_FrenchDeceased()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                g.Put(AshfordFix.At[1][2], AshfordFix.Old[1][2]);      // English then French: the French hook
                string said = PatchSaid(TryAshfordFix);
                Eq(1, ashfordState, "state");
                Ok(PatchIs(g, AshfordFix.At[1][0], AshfordFix.New[1][0]), "code");
                // "Decede" with its e acutes in the game's code page 437 (0x82), then its NUL
                EqBytes(Bytes("44 82 63 82 64 82 00"), g.Get(AshfordFix.At[1][1], 7), "the French text");
                Ok(PatchIs(g, AshfordFix.At[1][2], AshfordFix.New[1][2]), "hook");
                Has(said, "Ashford fix ON", "said");
            }
        }

        [Test] static void Patch_NativeFixes_WaitForTheFreeAreaThenNeedAFreshGame()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(NativeFixes.SkyAt[1][0], NativeFixes.SkyOld[1][0]);
                TryNativeFixes();
                Eq(0, skyFixState, "no free area yet: sky fix waits");
                Eq(0, droneFixState, "no free area yet: drone fix waits");
                PatchPool(g, false);
                TryNativeFixes();                       // no game start counted for this DOSBox: not in place
                Eq(-1, skyFixState, "sky fix off");
                Eq(-1, droneFixState, "drone fix off");
                Ok(PatchIs(g, NativeFixes.SkyAt[1][0], NativeFixes.SkyOld[1][0]), "sky code left as the game has it");
            }
        }

        [Test] static void Patch_NativeFixes_FirstGameStartFrench()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                OptInPlaceAttach = true; pidGames[gamePid] = 1;
                g.Put(NativeFixes.SkyAt[1][0], NativeFixes.SkyOld[1][0]);     // English then French
                g.Put(NativeFixes.DroneAt[1][0], NativeFixes.DroneOld[1][0]);
                TryNativeFixes();
                Eq(1, skyFixState, "sky fix");
                Eq(1, droneFixState, "drone fix");
                Ok(PatchIs(g, NativeFixes.SkyAt[1][0], NativeFixes.SkyNew[1][0]), "sky code rewritten");
                Ok(PatchIs(g, NativeFixes.DroneAt[1][0], NativeFixes.DroneNew[1][0]), "drone test rewritten");
            }
        }

        [Test] static void Patch_Smoke_ThickerCloudRecord()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                g.Put(SMOKE_AT[1][0], SMOKE_REC_OLD);
                Eq(1, TryPieces(SMOKE_AT, SMOKE_OLD, SMOKE_NEW, "Thicker smoke", "x"), "result");
                uint r = SMOKE_AT[1][0];
                Eq(256, g.GetInt(r), "particles 192 -> 256");
                Eq(100, g.GetInt(r + 0x28), "shortest life 50 -> 100");
                Eq(200, g.GetInt(r + 0x2C), "longest life 120 -> 200");
                Eq(70, g.GetInt(r + 0x40), "back to life 60 -> 70 %");
                Ok(PatchIs(g, r, SMOKE_REC_NEW), "the whole record");
            }
        }

        // ================================================================ object distance
        static void PatchObjGame(FakeGame g, bool english)
        {
            foreach (CodePatch p in OBJ_PATCHES) g.Put(english ? p.En : p.Fr, english ? p.OldEn : p.Old);
        }

        [Test] static void Patch_ObjDist_FrenchMax()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchObjGame(g, false);
                objWritten = new byte[1];
                string said = PatchSaid(TryObjDist);
                Eq(1, objState, "state");
                Eq(0, objLang, "French");
                Ok(objWritten == null, "range table to be scaled again");
                Ok(PatchIs(g, 0x2A0F1C, "B840380000"), "object list 400 -> 1200 entries");
                Ok(PatchIs(g, 0x2A14E2, "663DB004"), "its check 1200");
                Ok(PatchIs(g, 0x2A16CC, "6681F9B004"), "its other check 1200");
                Ok(PatchIs(g, 0x2B3B16, "3D8000000090"), "side buckets 128");
                Ok(PatchIs(g, 0x2B584C, "83F97F"), "row cap 127");
                Ok(PatchIs(g, 0x2B55F7, "B82800000090"), "scenery radius: mov eax,40 / nop");
                foreach (CodePatch p in OBJ_PATCHES) Ok(g.AllZero(p.En, p.OldEn.Length / 2), "English place 0x" + p.En.ToString("X") + " untouched");
                Has(said, "Object distance MAX: scenery up to 40 cells, unit and building ranges x3", "said");
            }
        }

        [Test] static void Patch_ObjDist_EnglishFar()
        {
            using (FakeGame g = new FakeGame())
            {
                OptObjDist = 1;
                PatchObjGame(g, true);
                string said = PatchSaid(TryObjDist);
                Eq(1, objState, "state");
                Eq(1, objLang, "English");
                foreach (CodePatch p in OBJ_PATCHES)
                    if (p.New != "") Ok(PatchIs(g, p.En, p.New), "0x" + p.En.ToString("X"));
                Ok(PatchIs(g, 0x2B5607, "B81E00000090"), "scenery radius: mov eax,30 / nop");
                Has(said, "Object distance FAR: scenery up to 30 cells, unit and building ranges x2", "said");
            }
        }

        [Test] static void Patch_ObjDist_OneUnknownByteNothingWritten()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchObjGame(g, false);
                g.Put(0x2B584C, "83F977");               // cmp ecx,119 instead of the game's 120
                string said = PatchSaid(TryObjDist);
                Eq(-1, objState, "state");
                Has(said, "Object distance unavailable: unsupported game version", "said");
                foreach (CodePatch p in OBJ_PATCHES)
                    if (p.Fr != 0x2B584C) Ok(PatchIs(g, p.Fr, p.Old), "0x" + p.Fr.ToString("X") + " left as the game has it");
            }
        }

        [Test] static void Patch_ObjDist_AlreadyOn()
        {
            using (FakeGame g = new FakeGame())
            {
                foreach (CodePatch p in OBJ_PATCHES) g.Put(p.Fr, p.New != "" ? p.New : "B82800000090");
                TryObjDist();
                Eq(1, objState, "state");
                Eq(0, objLang, "French");
            }
        }

        [Test] static void Patch_ObjTable_NothingWhileNoMissionLoaded()
        {
            using (FakeGame g = new FakeGame())
            {
                objLang = 0;
                ObjDistTable();
                Ok(objWritten == null, "nothing recorded");
                Ok(g.AllZero(OBJ_TABLE[0], 9 * 128), "table still zeros");
            }
        }

        [Test] static void Patch_ObjTable_ScaledAndCappedAt120()
        {
            using (FakeGame g = new FakeGame())
            {
                objLang = 0;                            // OptObjDist 2 (MAX): x3
                uint t = OBJ_TABLE[0];
                g.Mem[t] = 20; g.Mem[t + 1] = 50; g.Mem[t + 2] = 1; g.Mem[t + 3] = 255; g.Mem[t + 9 * 128 - 1] = 90;
                g.Mem[t + 9 * 128] = 50;                // past the 9 x 128 table
                ObjDistTable();
                Eq(60, (int)g.Mem[t], "20 x3");
                Eq(120, (int)g.Mem[t + 1], "50 x3 = 150, capped");
                Eq(3, (int)g.Mem[t + 2], "1 x3");
                Eq(120, (int)g.Mem[t + 3], "255 x3, capped (no byte overflow)");
                Eq(120, (int)g.Mem[t + 9 * 128 - 1], "last entry 90 x3, capped");
                Eq(0, (int)g.Mem[t + 4], "zeros stay zeros");
                Eq(50, (int)g.Mem[t + 9 * 128], "the byte after the table untouched");
                EqBytes(g.Get(t, 9 * 128), objWritten, "what was written is remembered");
            }
        }

        [Test] static void Patch_ObjTable_ScaledOncePerLoadedTable()
        {
            using (FakeGame g = new FakeGame())
            {
                objLang = 0;
                uint t = OBJ_TABLE[0];
                g.Mem[t] = 20; g.Mem[t + 7] = 30;
                ObjDistTable();
                ObjDistTable();
                Eq(60, (int)g.Mem[t], "not scaled again while unchanged");
                Eq(90, (int)g.Mem[t + 7], "not scaled again while unchanged (2)");
                byte[] next = new byte[9 * 128];        // the next mission loads its own table
                next[0] = 10; next[5] = 40;
                g.Put(t, next);
                ObjDistTable();
                Eq(30, (int)g.Mem[t], "new table scaled");
                Eq(120, (int)g.Mem[t + 5], "new table scaled (2)");
                Eq(0, (int)g.Mem[t + 7], "nothing left of the old table");
            }
        }

        [Test] static void Patch_ObjTable_FarDoublesTheEnglishTable()
        {
            using (FakeGame g = new FakeGame())
            {
                objLang = 1; OptObjDist = 1;
                uint t = 0x41ECC0;
                g.Mem[t] = 20; g.Mem[t + 1] = 70; g.Mem[t + 2] = 60;
                ObjDistTable();
                Eq(40, (int)g.Mem[t], "20 x2");
                Eq(120, (int)g.Mem[t + 1], "70 x2 = 140, capped");
                Eq(120, (int)g.Mem[t + 2], "60 x2");
            }
        }

        // ================================================================ terrain rings (FixDetail)
        [Test] static void Patch_Detail_GameRingsBecomeSharper()
        {
            using (FakeGame g = new FakeGame())
            {
                aTerrain = 0x200000;
                byte[] game = PatchMainRings(PatchGameEnds6);
                g.Put(aTerrain, game);
                FixDetail(DETAIL_RINGS[2]);
                // ring 0 keeps its end (4), ring 1 the game's 12 (no smooth terrain), then SHARPER's 64 / 128 / 180 / 280;
                // each ring starts where the one before ends; both copies of (from, to) written
                EqBytes(Bytes(
                    "01 0000 0400 0100 0000 0400 00" +
                    "02 0400 0C00 0100 0400 0C00 00" +
                    "00 0C00 4000 0100 0C00 4000 00" +
                    "00 4000 8000 0200 4000 8000 00" +
                    "00 8000 B400 0400 8000 B400 00" +
                    "00 B400 1801 0800 B400 1801 00"), g.Get(aTerrain + 0x5C, 72), "the six rings");
                EqBytes(Bytes("4000 8000 B400"), g.Get(aTerrain + 0x30, 6), "ring distances at +0x30: 64 / 128 / 180");
                byte[] now = g.Get(aTerrain, 0xA8);
                for (int i = 0; i < 0xA8; i++)
                    if (i < 0x30 || (i >= 0x36 && i < 0x5C) || i >= 0x5C + 72) Eq(game[i], now[i], "byte +0x" + i.ToString("X") + " untouched");
            }
        }

        [Test] static void Patch_Detail_SmoothRingEndsAtSmoothCells()
        {
            using (FakeGame g = new FakeGame())
            {
                aTerrain = 0x200000;
                smoothState = 1;                        // polygon buffers enlarged: SHARPER's 24 cells (not prepared exe)
                g.Put(aTerrain, PatchMainRings(new[] { 7, 12, 30, 64, 100, 240 }));
                FixDetail(DETAIL_RINGS[1]);
                uint r = aTerrain + 0x5C;
                Eq((short)7, g.GetShort(r + 9), "ring 0 keeps its own end");
                Eq((short)7, g.GetShort(r + 12 + 7), "ring 1 starts there");
                Eq((short)24, g.GetShort(r + 12 + 9), "ring 1 (polygons) up to SmoothCells()");
                Eq((short)24, g.GetShort(r + 24 + 7), "ring 2 starts there");
                int[] d = DETAIL_RINGS[1];
                for (int k = 0; k < 4; k++) Eq((short)d[k], g.GetShort(r + 12 * (uint)(k + 2) + 9), "ring " + (k + 2) + ": SHARP's end");
                farTerrain = true;                      // prepared exe: 48 cells
                FixDetail(DETAIL_RINGS[1]);
                Eq((short)48, g.GetShort(r + 12 + 9), "ring 1 up to 48 cells with the prepared exe");
            }
        }

        [Test] static void Patch_Detail_OtherLayoutsLeftAlone()
        {
            using (FakeGame g = new FakeGame())
            {
                aTerrain = 0x200000;
                List<byte[]> others = new List<byte[]>();
                byte[] b = PatchMainRings(PatchGameEnds6); PatchShort(b, 0x5A, 5); others.Add(b);          // 5 rings
                b = PatchMainRings(PatchGameEnds6); PatchShort(b, 0x58, 3); others.Add(b);                 // first flat ring 3
                others.Add(PatchRingBlock(6, 2, new[] { 1, 0, 0, 0, 0, 0 }, PatchSteps6, PatchGameEnds6, 1));   // another type
                others.Add(PatchRingBlock(6, 2, PatchTypes6, new[] { 1, 1, 1, 2, 4, 4 }, PatchGameEnds6, 1));   // another step
                for (int i = 0; i < others.Count; i++)
                {
                    g.Put(aTerrain, others[i]);
                    FixDetail(DETAIL_RINGS[2]);
                    EqBytes(others[i], g.Get(aTerrain, 0xA8), "layout " + i + " untouched");
                }
            }
        }

        [Test] static void Patch_Detail_NothingWrittenWhenTheEndsAreOurs()
        {
            using (FakeGame g = new FakeGame())
            {
                aTerrain = 0x200000;
                // our ring ends already, the zoomed copies and +0x30 as the game has them now: left alone (only the ends count)
                byte[] ours = PatchRingBlock(6, 2, PatchTypes6, PatchSteps6, new[] { 4, 12, 64, 128, 180, 280 }, 1.29);
                g.Put(aTerrain, ours);
                FixDetail(DETAIL_RINGS[2]);
                EqBytes(ours, g.Get(aTerrain, 0xA8), "block untouched");
            }
        }

        // ================================================================ cockpit camera rings (FixSmallRings)
        [Test] static void Patch_SmallRings_GameCameraSharper()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true);
                uint v = PatchSmallCam(g, 0, PatchSmallRings(1));
                FixSmallRings();
                EqBytes(Bytes(
                    "02 0000 1800 0100 0000 1800 00" +
                    "00 1800 3000 0100 1800 3000 00" +
                    "00 3000 6000 0200 3000 6000 00" +
                    "00 6000 A000 0400 6000 A000 00" +
                    "00 A000 0001 0800 A000 0001 00"), g.Get(v + 0x5C, 60), "SHARPER: 24 / 48 / 96 / 160 / 256, steps 1 1 2 4 8");
                EqBytes(Bytes("3000 6000 A000"), g.Get(v + 0x30, 6), "ring distances 48 / 96 / 160");
            }
        }

        [Test] static void Patch_SmallRings_ZoomScaleKeptSharp()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true);
                OptDetail = 1;
                uint v = PatchSmallCam(g, 0, PatchSmallRings(1.5));     // zoomed: ring 4 at 240 for 160
                FixSmallRings();
                EqBytes(Bytes(
                    "02 0000 1800 0100 0000 1000 00" +
                    "00 1800 3C00 0100 1000 2800 00" +
                    "00 3C00 7800 0200 2800 5000 00" +
                    "00 7800 E100 0400 5000 9600 00" +
                    "00 E100 8001 0800 9600 0001 00"), g.Get(v + 0x5C, 60), "SHARP 16 / 40 / 80 / 150 / 256, zoomed copy x1.5");
                EqBytes(Bytes("3C00 7800 E100"), g.Get(v + 0x30, 6), "ring distances 40 / 80 / 150 x1.5");
            }
        }

        [Test] static void Patch_SmallRings_ThreeCameraPointers()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true);
                regSize = 0x400000;                     // the game's RAM ends at 4 MB: above is not the game's (readable here)
                byte[] game = PatchSmallRings(1);
                g.PutUInt(HdPayload.SvPtr + 4, 0x410000);               // camera 0: its block above the game's RAM
                g.PutUInt(0x410000, 0x320000); g.Put(0x320000, game);
                g.PutUInt(HdPayload.SvPtr + 8, 0x300100);               // camera 1: its ring block above the game's RAM
                g.PutUInt(0x300100, 0x410100); g.Put(0x410100, game);
                uint v = PatchSmallCam(g, 2, game);                     // camera 2
                FixSmallRings();
                Eq((short)24, g.GetShort(v + 0x5C + 9), "camera 2 rewritten");
                EqBytes(game, g.Get(0x320000, 0xA8), "camera 0 (block outside the game's RAM) not followed");
                EqBytes(game, g.Get(0x410100, 0xA8), "camera 1 (rings outside the game's RAM) not written");
                g.PutUInt(HdPayload.SvPtr + 4, 0); g.PutUInt(HdPayload.SvPtr + 8, 0x300100); g.PutUInt(0x300100, 0);
                g.Put(v, game);
                FixSmallRings();                        // no block / block without rings yet: skipped, the others still done
                Eq((short)24, g.GetShort(v + 0x5C + 9), "camera 2 rewritten again");
            }
        }

        [Test] static void Patch_SmallRings_OursOtherOrOffLeftAlone()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true);
                byte[] ours = PatchRingBlock(5, 1, PatchSmallTypes, SMALL_STEPS, SMALL_ENDS[2], 1.5);
                byte[] other = PatchRingBlock(5, 1, PatchSmallTypes, new[] { 1, 1, 2, 2, 8 }, PatchSmallGameEnds, 1);
                byte[] four = PatchRingBlock(4, 1, new[] { 2, 0, 0, 0 }, new[] { 1, 1, 2, 2 }, new[] { 9, 20, 40, 80 }, 1);
                uint v0 = PatchSmallCam(g, 0, ours), v1 = PatchSmallCam(g, 1, other), v2 = PatchSmallCam(g, 2, four);
                FixSmallRings();
                EqBytes(ours, g.Get(v0, 0xA8), "ours already: nothing written");
                EqBytes(other, g.Get(v1, 0xA8), "steps not the game's: untouched");
                EqBytes(four, g.Get(v2, 0xA8), "4 rings: untouched");
                byte[] game = PatchSmallRings(1);
                g.Put(v1, game);
                OptSmallViews = false;
                FixSmallRings();
                EqBytes(game, g.Get(v1, 0xA8), "small_views off: untouched");
            }
        }

        // ================================================================ PeriodicFixes
        static void PatchFogGame(FakeGame g, int weather)
        {
            haveFog = true; aFogTable = 0x200000; aWeather = 0x201000;
            g.PutInt(aWeather, weather);
        }

        [Test] static void Patch_Periodic_FogTableAndItsCopy()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchFogGame(g, 100);
                PeriodicFixes(1);                       // FAR: radius 180
                EqBytes(FogTable(180), g.Get(aFogTable, 1024), "fog table, radius 180");
                EqBytes(FogTable(180), g.Get(aFogTable + 1024, 1024), "its check copy, the same");
                No(Same(FogTable(100), FogTable(180)), "the two radii give other tables (else this test proves nothing)");
                PeriodicFixes(0);                       // NORMAL: the weather's own radius
                EqBytes(FogTable(100), g.Get(aFogTable, 1024), "fog table, weather radius 100");
                EqBytes(FogTable(100), g.Get(aFogTable + 1024, 1024), "copy, weather radius 100");
            }
        }

        [Test] static void Patch_Periodic_CopyRepairedWithTheTable()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchFogGame(g, 300);
                g.Put(aFogTable, FogTable(180));        // the table is right, its copy is not: both written at once
                g.Mem[aFogTable + 2047] = 0x0E;
                PeriodicFixes(1);
                EqBytes(FogTable(180), g.Get(aFogTable + 1024, 1024), "copy repaired");
                EqBytes(FogTable(180), g.Get(aFogTable, 1024), "table unchanged");
            }
        }

        [Test] static void Patch_Periodic_WeatherRadiusFrom20To2000()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchFogGame(g, 0);
                byte[] junk = new byte[2048];
                for (int i = 0; i < junk.Length; i++) junk[i] = 0x77;
                foreach (int w in new[] { 0, 19, 2001, -180, int.MaxValue })
                {
                    g.Put(aFogTable, junk); g.PutInt(aWeather, w);
                    PeriodicFixes(1);
                    EqBytes(junk, g.Get(aFogTable, 2048), "weather " + w + ": nothing written");
                }
                foreach (int w in new[] { 20, 2000 })
                {
                    g.Put(aFogTable, junk); g.PutInt(aWeather, w);
                    PeriodicFixes(0);
                    EqBytes(FogTable(w), g.Get(aFogTable + 1024, 1024), "weather " + w + ": written");
                }
                haveFog = false;
                g.Put(aFogTable, junk); g.PutInt(aWeather, 100);
                PeriodicFixes(1);
                EqBytes(junk, g.Get(aFogTable, 2048), "no fog found: nothing written");
            }
        }

        static void PatchResGame(FakeGame g, byte flags, byte button)
        {
            have400 = true; aResFlags = 0x200000; aResMode = 0x200010; aResCallback = 0x2A0000; aResButton = 0x200100;
            g.Mem[aResFlags] = flags; g.PutInt(aResMode, 5);
            g.PutUInt(aResButton, aResCallback); g.Mem[aResButton + 8] = button;
        }

        [Test] static void Patch_Periodic_Force400()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchResGame(g, 0x10, 1);
                PeriodicFixes(0);
                Eq(1, g.GetInt(aResMode), "mode 320x400");
                Eq(0x13, (int)g.Mem[aResFlags], "flags: pending | 400 lines, other bits kept");
                Eq(2, (int)g.Mem[aResButton + 8], "radio button on 320x400");
                g.Mem[aResFlags] = 0x12; g.PutInt(aResMode, 5); g.Mem[aResButton + 8] = 1;
                PeriodicFixes(0);
                Eq(5, g.GetInt(aResMode), "already 400 lines: mode not written");
                Eq(0x12, (int)g.Mem[aResFlags], "already 400 lines: flags not written");
                Eq(1, (int)g.Mem[aResButton + 8], "already 400 lines: button not written");
            }
        }

        [Test] static void Patch_Periodic_Force400RadioButtonChecks()
        {
            using (FakeGame g = new FakeGame())
            {
                foreach (byte st in new byte[] { 0, 3, 0x81 })
                {
                    PatchResGame(g, 0, st);
                    PeriodicFixes(0);
                    Eq(3, (int)g.Mem[aResFlags], "state " + st + ": flags");
                    Eq((int)st, (int)g.Mem[aResButton + 8], "state " + st + ": button left as it is");
                }
                PatchResGame(g, 0, 1);
                g.PutUInt(aResButton, aResCallback + 1);   // not the 320x400 button's data
                PeriodicFixes(0);
                Eq(1, g.GetInt(aResMode), "mode written");
                Eq(1, (int)g.Mem[aResButton + 8], "not the button: left as it is");
                PatchResGame(g, 0, 1);
                aResButton = 0;                         // no button found by the scan
                PeriodicFixes(0);
                Eq(1, g.GetInt(aResMode), "no button: mode still written");
            }
        }

        [Test] static void Patch_Periodic_Force400OffUnlessHd()
        {
            using (FakeGame g = new FakeGame())
            {
                OptForce400 = false; OptHD = false;
                PatchResGame(g, 0, 1);
                PeriodicFixes(0);
                Eq(5, g.GetInt(aResMode), "neither 320x400 nor HD: nothing written");
                Eq(0, (int)g.Mem[aResFlags], "flags untouched");
                OptHD = true;                           // HD needs 320x400 missions
                PeriodicFixes(0);
                Eq(1, g.GetInt(aResMode), "HD: 320x400 imposed");
            }
        }

        [Test] static void Patch_Periodic_DetailAndCockpitRings()
        {
            using (FakeGame g = new FakeGame())
            {
                haveDetail = true; aTerrain = 0x200000; OptDetail = 1;
                g.Put(aTerrain, PatchMainRings(PatchGameEnds6));
                HdPayload.Use(false, true, true); smallState = 1;
                uint v = PatchSmallCam(g, 0, PatchSmallRings(1));
                PeriodicFixes(0);
                int[] d = DETAIL_RINGS[1];
                for (int k = 0; k < 4; k++) Eq((short)d[k], g.GetShort(aTerrain + 0x5C + 12 * (uint)(k + 2) + 9), "main view ring " + (k + 2));
                Eq((short)16, g.GetShort(v + 0x5C + 9), "cockpit camera: SHARP's 16 cells");
                OptDetail = 0;                          // GAME: the game's rings
                byte[] main = PatchMainRings(PatchGameEnds6), cam = PatchSmallRings(1);
                g.Put(aTerrain, main); g.Put(v, cam);
                PeriodicFixes(0);
                EqBytes(main, g.Get(aTerrain, 0xA8), "GAME: main view untouched");
                EqBytes(cam, g.Get(v, 0xA8), "GAME: cockpit camera untouched");
            }
        }

        static void PatchWideGame(FakeGame g)
        {
            OptWideAttach = true; haveWide = true;     // stretched 16:9 on this game (HD off: not the true 16:9)
            aRatio = 0x300000; aRatioRef = 0x300010; aG3Width = 0x300030; aScaleX = 0x300040; aTerrain = 0x300100;
        }

        [Test] static void Patch_Periodic_StretchedPixelRatioOnce()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchWideGame(g);
                Ok(WideActive(), "stretched 16:9 active");
                g.PutInt(aRatio, 39321);                // 0.6 (320x400)
                g.PutUInt(aRatioRef, 0x300020); g.PutInt(0x300020, 78643);   // its reference: 1.2 (320x200)
                PeriodicFixes(0);
                Eq(29490, g.GetInt(aRatio), "0.6 x 0.75");
                Eq(58982, g.GetInt(0x300020), "1.2 x 0.75 at the reference");
                PeriodicFixes(0);
                Eq(29490, g.GetInt(aRatio), "not narrowed again");
                Eq(58982, g.GetInt(0x300020), "reference not narrowed again");
                g.PutInt(aRatio, 39321); g.PutUInt(aRatioRef, 0x800); g.PutInt(0x800, 39321);
                PeriodicFixes(0);
                Eq(29490, g.GetInt(aRatio), "ratio narrowed");
                Eq(39321, g.GetInt(0x800), "reference below 0x1000 ignored");
                g.PutInt(aRatio, 39321);
                wideHooks = true;                       // 16:9 done by the camera hooks: the ratio stays the game's
                PeriodicFixes(0);
                Eq(39321, g.GetInt(aRatio), "hooks: untouched");
            }
        }

        [Test] static void Patch_Periodic_StretchedTerrainCameraRealigned()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchWideGame(g);
                g.PutInt(aG3Width, 298); g.PutInt(aScaleX, 128000);
                g.PutInt(aTerrain + 4, 32768); g.PutInt(aTerrain + 0x0C, 16384);
                g.PutUInt(aTerrain + 0x1C, 0x300200); g.PutShort(0x300208, 298);   // main view canvas, width 298
                PeriodicFixes(0);
                // k = 2^32 / 128000 / 32768 = 1.024: both cached values x1.024 (truncated)
                Eq(33554, g.GetInt(aTerrain + 4), "1/scale copy");
                Eq(16777, g.GetInt(aTerrain + 0x0C), "99/scale copy");
                PeriodicFixes(0);
                Eq(33554, g.GetInt(aTerrain + 4), "aligned now (k within 2 %): not again");
                g.PutInt(aTerrain + 4, 32768); g.PutShort(0x300208, 320);         // the library draws another view
                PeriodicFixes(0);
                Eq(32768, g.GetInt(aTerrain + 4), "another canvas width: untouched");
                g.PutShort(0x300208, 298); g.PutInt(aScaleX, 65536);              // k = 2: not trusted
                PeriodicFixes(0);
                Eq(32768, g.GetInt(aTerrain + 4), "k out of 0.5..2: untouched");
            }
        }

        // ================================================================ smooth terrain (TrySmooth)
        [Test] static void Patch_Smooth_PreparedExe()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(TERRAIN_SHIFT[0], "C1E307");      // French prepared exe: shl ebx,7
                string said = PatchSaid(TrySmooth);
                Ok(farTerrain, "far terrain");
                Eq(1, smoothState, "state");
                Has(said, "Smooth terrain up to 48 cells (the game: 12)", "said");
                for (int l = 0; l < 2; l++) for (int i = 0; i < 3; i++) Ok(g.AllZero(SMOOTH_AT[l][i], 4), "buffer size " + l + "/" + i + " not written");
            }
            smoothState = 0; farTerrain = false;
            using (FakeGame g = new FakeGame())
            {
                OptDetail = 1;
                g.Put(TERRAIN_SHIFT[1], "C1E307");      // English
                string said = PatchSaid(TrySmooth);
                Ok(farTerrain, "English: far terrain");
                Has(said, "Smooth terrain up to 32 cells", "SHARP: 32 cells");
            }
        }

        [Test] static void Patch_Smooth_OldBuffersEnlarged()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(TERRAIN_SHIFT[0], "C1E306");      // the game's shl ebx,6: not prepared
                for (int i = 0; i < 3; i++) g.Put(SMOOTH_AT[0][i], SMOOTH_OLD[i]);
                string said = PatchSaid(TrySmooth);
                No(farTerrain, "not far");
                Eq(1, smoothState, "state");
                EqBytes(Bytes("40600000"), g.Get(0x2AB0C5, 4), "quad buffer 0x3EC0 -> 0x6040 bytes (500 -> 768 quads)");
                EqBytes(Bytes("00C00000"), g.Get(0x2ADF0F, 4), "triangle buffer 0x7D00 -> 0xC000 bytes");
                EqBytes(Bytes("58980000"), g.Get(0x2ADF00, 4), "vertex buffer 0x7242 -> 0x9858 bytes (750 -> 1000 vertices)");
                Has(said, "Smooth terrain up to 24 cells", "said");
            }
        }

        [Test] static void Patch_Smooth_EnglishPartlyDone()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(SMOOTH_AT[1][0], SMOOTH_NEW[0]); g.Put(SMOOTH_AT[1][1], SMOOTH_OLD[1]); g.Put(SMOOTH_AT[1][2], SMOOTH_NEW[2]);
                TrySmooth();
                Eq(1, smoothState, "state");
                for (int i = 0; i < 3; i++) EqBytes(SMOOTH_NEW[i], g.Get(SMOOTH_AT[1][i], 4), "English size " + i);
            }
        }

        [Test] static void Patch_Smooth_UnknownNothingWritten()
        {
            using (FakeGame g = new FakeGame())
            {
                for (int i = 0; i < 3; i++) g.Put(SMOOTH_AT[0][i], SMOOTH_OLD[i]);
                g.Put(SMOOTH_AT[0][2], "42730000");     // vertices: not the game's 750
                string said = PatchSaid(TrySmooth);
                Eq(-1, smoothState, "state");
                Has(said, "Smooth terrain unavailable: unsupported game version", "said");
                EqBytes(SMOOTH_OLD[0], g.Get(SMOOTH_AT[0][0], 4), "quads untouched");
                EqBytes(SMOOTH_OLD[1], g.Get(SMOOTH_AT[0][1], 4), "triangles untouched");
            }
        }

        // ================================================================ in-place rewrite (TryInPlace), made-up tables
        // checked code: 8 bytes at 0x100100 and 4 at 0x100200 (+ 16 at 0x100110); the second exe's tables match
        static void PatchInPlaceGame(FakeGame g)
        {
            g.Put(0x100100, "1011121314151617");
            g.Put(0x100200, "A0A1A2A3");
            for (int i = 0; i < 16; i++) g.Mem[0x100110 + i] = (byte)(0x20 + i);
        }

        static string PatchInPlaceSha(FakeGame g)
        {
            MemoryStream m = new MemoryStream();
            m.Write(g.Get(0x100100, 8), 0, 8); m.Write(g.Get(0x100200, 4), 0, 4); m.Write(g.Get(0x100110, 16), 0, 16);
            return PatchSha(m.ToArray());
        }

        static int PatchInPlace(string sha, int size1)
        {
            return TryInPlace(
                new[] { new uint[] { 0x100000 }, new uint[] { 0x100100, 0x100300, 0x100400 } },
                new[] { new[] { 1 }, new[] { 6, size1, 16 } },
                new[] {
                    new[] { new[] { "B:00" } },
                    new[] { new[] { "B:9090", "C:100104:4" }, new[] { "C:100100:2", "B:CC" }, new[] { "C:100110:10" } } },
                new[] { new uint[] { 0x100000 }, new uint[] { 0x100100, 0x100200, 0x100110 } },
                new[] { new[] { 4 }, new[] { 8, 4, 16 } },
                new[] { new string('0', 64), sha });
        }

        [Test] static void Patch_InPlace_PiecesCopiedBeforeAnythingIsWritten()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchInPlaceGame(g);
                Eq(1, PatchInPlace(PatchInPlaceSha(g), 3), "result");
                EqBytes(Bytes("9090 14151617"), g.Get(0x100100, 6), "entry 0: ours, then 4 bytes copied from 0x100104");
                // entry 1 copies 0x100100, which entry 0 overwrites: it got the game's bytes, read before any write
                EqBytes(Bytes("1011 CC"), g.Get(0x100300, 3), "entry 1: the game's bytes, then ours");
                EqBytes(g.Get(0x100110, 16), g.Get(0x100400, 16), "entry 2: \"C:100110:10\" = 0x10 bytes (lengths in hex)");
                Ok(g.AllZero(0x100000, 4), "first exe (check mismatch) untouched");
            }
        }

        [Test] static void Patch_InPlace_ShaMismatchNothingWritten()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchInPlaceGame(g);
                string sha = PatchInPlaceSha(g);
                Eq(-1, PatchInPlace(sha.ToUpperInvariant(), 3), "the tables' SHA-256 is lowercase hex");
                g.Mem[0x10011F] ^= 1;                   // one checked byte differs (the code ran, or another version)
                Eq(-1, PatchInPlace(sha, 3), "one byte differs");
                EqBytes(Bytes("1011121314151617"), g.Get(0x100100, 8), "entry 0 untouched");
                Ok(g.AllZero(0x100300, 3) && g.AllZero(0x100400, 16), "other entries untouched");
            }
        }

        [Test] static void Patch_InPlace_SizeMismatchNothingWritten()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchInPlaceGame(g);
                Eq(-1, PatchInPlace(PatchInPlaceSha(g), 4), "entry 1 makes 3 bytes, its table says 4");
                EqBytes(Bytes("1011121314151617"), g.Get(0x100100, 8), "entry 0 (before it) not written either");
                Ok(g.AllZero(0x100300, 4) && g.AllZero(0x100400, 16), "nothing written");
            }
        }

        [Test] static void Patch_InPlace_WrittenBytesChecked()
        {
            using (FakeGame g = new FakeGame())
            {
                g.Put(0x100100, "55AA");
                // two entries over the same bytes: the first one reads back what the second wrote, not its own: refused
                int r = TryInPlace(new[] { new uint[] { 0x100300, 0x100301 } }, new[] { new[] { 3, 2 } },
                    new[] { new[] { new[] { "B:111111" }, new[] { "B:2222" } } },
                    new[] { new uint[] { 0x100100 } }, new[] { new[] { 2 } }, new[] { PatchSha(Bytes("55AA")) });
                Eq(-1, r, "result");
            }
        }

        // ================================================================ zoom fix
        [Test] static void Patch_Zoom_WaitsForTheFreeArea()
        {
            using (FakeGame g = new FakeGame())
            {
                uint hook = PatchZoomSite(g, 0x26BB50, 0x38FB24, 0x38FA70, 0x2AC7A0);
                TryZoomFix();
                Eq(0, zoomFixState, "looked at again later");
                Ok(g.AllZero(ZOOMFIX_CAVE, 12), "cave not written");
                EqBytes(Bytes("E8350C0400"), g.Get(hook, 5), "call not written");
            }
        }

        [Test] static void Patch_Zoom_CaveAndCallEnglishCamera()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, true);
                uint hook = PatchZoomSite(g, 0x26BB50, 0x38FB24, 0x38FA70, 0x2AC7A0);
                EqBytes(Bytes("BA04000000 8B1D70FA3800 B824FB3800 29DA 31DB 31C9 E8350C0400"), g.Get(0x26BB50, 27), "the made-up call site");
                string said = PatchSaid(TryZoomFix);
                Eq(1, zoomFixState, "state");
                // movsx ebx, word [0x38FB24 + 0xBE] / jmp 0x2AC7A0
                EqBytes(Bytes("0FBF1D E2FB3800 E9 E426E4FF"), g.Get(ZOOMFIX_CAVE, 12), "cave");
                EqBytes(Bytes("E8 45E51F00"), g.Get(hook, 5), "call 0x46A0B0");
                Has(said, "Zoom fix ON: the zoom stays after the Options screen", "said");
            }
        }

        [Test] static void Patch_Zoom_FrenchCamera()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                uint hook = PatchZoomSite(g, 0x26BB50, 0x38FBD4, 0x38FB20, 0x2AC7A0);
                TryZoomFix();
                Eq(1, zoomFixState, "state");
                EqBytes(Bytes("0FBF1D 92FC3800 E9 E426E4FF"), g.Get(ZOOMFIX_CAVE, 12), "cave: zoom level of 0x38FBD4");
                EqBytes(Bytes("E845E51F00"), g.Get(hook, 5), "call");
            }
        }

        [Test] static void Patch_Zoom_AlreadyInPlace()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, true);
                PatchZoomSite(g, 0x26BB50, 0x38FB24, 0x38FA70, ZOOMFIX_CAVE);   // the call already goes to the cave
                string said = PatchSaid(TryZoomFix);
                Eq(1, zoomFixState, "state");
                Eq("", said, "nothing said");
                Ok(g.AllZero(ZOOMFIX_CAVE, 12), "cave not written");
            }
        }

        [Test] static void Patch_Zoom_CaveTakenOrAlreadyOurs()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, true);
                uint hook = PatchZoomSite(g, 0x26BB50, 0x38FB24, 0x38FA70, 0x2AC7A0);
                g.Mem[ZOOMFIX_CAVE + 11] = 1;
                string said = PatchSaid(TryZoomFix);
                Eq(-1, zoomFixState, "cave not free: state");
                Has(said, "Zoom fix unavailable: its memory area is not free", "said");
                EqBytes(Bytes("E8350C0400"), g.Get(hook, 5), "call left as the game has it");
                g.Put(ZOOMFIX_CAVE, "0FBF1DE2FB3800E9E426E4FF");   // our own cave (TN+ attached again)
                zoomFixState = 0;
                TryZoomFix();
                Eq(1, zoomFixState, "our cave: state");
                EqBytes(Bytes("E845E51F00"), g.Get(hook, 5), "call written");
            }
        }

        [Test] static void Patch_Zoom_LookalikeCallSitesSkipped()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, true);
                PatchZoomSite(g, 0x250000, 0x38FB00, 0x38FA4C, 0x2AC7A0);     // another camera
                PatchZoomSite(g, 0x250100, 0x38FB24, 0x38FA74, 0x2AC7A0);     // mode not camera - 0xB4
                PatchZoomSite(g, 0x250200, 0x38FB24, 0x38FA70, 0x2AC7A0); g.Mem[0x250200 + 16] = 0x2B;   // not sub edx,ebx
                byte[] before = g.Get(0x250000, 0x300);
                string said = PatchSaid(TryZoomFix);
                Eq(-1, zoomFixState, "no call site: off");
                Eq("", said, "silently");
                Ok(g.AllZero(ZOOMFIX_CAVE, 12), "cave not written");
                uint hook = PatchZoomSite(g, 0x26BB50, 0x38FB24, 0x38FA70, 0x2AC7A0);
                zoomFixState = 0;
                TryZoomFix();
                Eq(1, zoomFixState, "the real one after the lookalikes");
                EqBytes(Bytes("E845E51F00"), g.Get(hook, 5), "its call written");
                EqBytes(before, g.Get(0x250000, 0x300), "lookalikes untouched");
            }
        }

        // ================================================================ cockpit cameras in HD (TrySmallViews)
        [Test] static void Patch_SmallViews_OnlyWithTheHd720Module()
        {
            HdPayload.Use(false, false);
            hdState = 1;
            TrySmallViews();
            Eq(-1, smallState, "HD 640x400: no module");
            smallState = 0;
            HdPayload.Use(true, true, false);
            TrySmallViews();
            Eq(-1, smallState, "HD 848x480: no module");
        }

        [Test] static void Patch_SmallViews_WaitsForHdAndItsBuffer()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true);
                g.PutInt(HdPayload.SvOn, 7);
                TrySmallViews();
                Eq(0, smallState, "HD not injected yet");
                hdState = 1;
                TrySmallViews();
                Eq(0, smallState, "no 12 KB buffer yet");
                Eq(7, g.GetInt(HdPayload.SvOn), "nothing written");
            }
        }

        [Test] static void Patch_SmallViews_Installed()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true);
                hdState = 1;
                g.PutUInt(HdPayload.XCode, 0x400000);   // page-aligned buffer: the module at +SvOff
                string said = PatchSaid(TrySmallViews);
                Eq(1, smallState, "state");
                uint p = 0x400000 + HdPayload.SvOff;
                EqBytes(HdPayload.SvCode, g.Get(p, HdPayload.SvCode.Length), "module code");
                Eq(p, g.GetUInt(HdPayload.SvPtr), "its address for the HD code");
                Eq(1, g.GetInt(HdPayload.SvOn), "cockpit cameras in HD on");
                Has(said, "Cockpit cameras in HD ON", "said");
            }
        }

        [Test] static void Patch_SmallViews_UnalignedBufferAndOptionOff()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(true, true, true);
                hdState = 1; OptSmallViews = false;
                g.PutUInt(HdPayload.XCode, 0x400010);   // the next page: 0x401000
                g.PutInt(HdPayload.SvOn, 7);
                string said = PatchSaid(TrySmallViews);
                Eq(1, smallState, "state");
                uint p = 0x401000 + HdPayload.SvOff;
                EqBytes(HdPayload.SvCode, g.Get(p, HdPayload.SvCode.Length), "module code in a page of its own");
                Eq(p, g.GetUInt(HdPayload.SvPtr), "pointer");
                Eq(0, g.GetInt(HdPayload.SvOn), "small_views off: the module runs, cameras not in HD");
                HasNot(said, "Cockpit cameras", "nothing said");
            }
        }

        [Test] static void Patch_SmallViews_PointerAlreadySet()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, true, true);
                hdState = 1;
                g.PutUInt(HdPayload.XCode, 0x400000);
                uint p = 0x400000 + HdPayload.SvOff;
                g.PutUInt(HdPayload.SvPtr, p);          // ours (TN+ attached again)
                TrySmallViews();
                Eq(1, smallState, "ours: on");
                Ok(g.AllZero(p, HdPayload.SvCode.Length), "code not written again");
                smallState = 0;
                g.PutUInt(HdPayload.SvPtr, 0x123456);   // something else
                TrySmallViews();
                Eq(-1, smallState, "another pointer: off");
                Ok(g.AllZero(p, HdPayload.SvCode.Length), "code not written");
                Eq(0x123456u, g.GetUInt(HdPayload.SvPtr), "pointer kept");
            }
        }

        // ================================================================ HD injection (TryHdInject)
        [Test] static void Patch_Hd_InjectedFrench()
        {
            using (FakeGame g = new FakeGame())
            {
                GameDir = TestDir("game");              // no __FF.EXE there: language unknown, the French payload
                HdPayload.Use(false, false);
                PatchHdGame(g);
                string said = PatchSaid(TryHdInject);
                Eq(1, hdState, "state");
                Eq(HdPayloadFr.PoolMax, HdPayload.PoolMax, "French 640x400 payload");
                for (int i = 0; i < HdPayload.PatchAt.Length; i++)
                    EqBytes(HdPayload.PatchNew[i], g.Get(HdPayload.PatchAt[i], HdPayload.PatchNew[i].Length), "patch " + i + " at 0x" + HdPayload.PatchAt[i].ToString("X"));
                EqBytes(HdPayload.CodeBytes, g.Get(HdPayload.Code, HdPayload.CodeBytes.Length), "code");
                byte[] data = (byte[])HdPayload.DataInit.Clone();
                Eq(1, BitConverter.ToInt32(data, (int)(HdPayload.Smoothing - HdPayload.Data)), "the payload's smoothing is on");
                Array.Clear(data, (int)(HdPayload.Smoothing - HdPayload.Data), 4);   // ... and switched off
                Array.Clear(data, (int)(HdPayload.HudFilter - HdPayload.Data), 4);
                EqBytes(data, g.Get(HdPayload.Data, data.Length), "data, smoothing and HUD filter off");
                Has(said, "HD 640x400 ready: missions in 320x400 will be in HD", "said");
            }
        }

        [Test] static void Patch_Hd_InjectedEnglish720()
        {
            using (FakeGame g = new FakeGame())
            {
                GameDir = TestDir("game"); exeLangDir = GameDir; exeLang = "English";   // as if its __FF.EXE was English
                OptHD = true; OptWideAttach = true;     // true 16:9 (TRUE 720 by default) on this attached game
                OptSpeedFix = false; OptWideHud = false;
                HdPayload.Use(true, true, true);
                PatchHdGame(g);
                string said = PatchSaid(TryHdInject);
                Eq(1, hdState, "state");
                Eq(HdPayloadEn720.SvPtr, HdPayload.SvPtr, "English 1280x720 payload");
                for (int i = 0; i < HdPayload.PatchAt.Length; i++)
                    EqBytes(HdPayload.PatchNew[i], g.Get(HdPayload.PatchAt[i], HdPayload.PatchNew[i].Length), "patch " + i);
                EqBytes(HdPayload.CodeBytes, g.Get(HdPayload.Code, HdPayload.CodeBytes.Length), "code");
                Eq(1, BitConverter.ToInt32(HdPayload.DataInit, (int)(HdPayload.HudStretch - HdPayload.Data)), "the payload stretches the HUD");
                Eq(0, g.GetInt(HdPayload.HudStretch), "wide_hud = 0: centred");
                Eq(1, g.GetInt(HdPayload.SpeedDone), "speed fixes off: marked done before the first 3D frame");
                Has(said, "HD 1280x720 ready", "said");
            }
        }

        [Test] static void Patch_Hd_WaitsForThePool()
        {
            using (FakeGame g = new FakeGame())
            {
                GameDir = TestDir("game");
                HdPayload.Use(false, false);
                PatchHdGame(g);
                g.PutUInt(HdPayload.PoolVar, 0);        // the game has not allocated its pool yet
                for (int i = 0; i < 30; i++) TryHdInject();
                Eq(0, hdState, "waiting (no give-up count while the game bytes are there)");
                Ok(g.AllZero(HdPayload.Code, HdPayload.CodeBytes.Length), "code not written");
                g.PutUInt(HdPayload.PoolVar, HdPayload.PoolBase);
                TryHdInject();
                Eq(1, hdState, "injected once the pool is there");
            }
        }

        static string PatchHdRefused(Action<FakeGame> spoil)
        {
            using (FakeGame g = new FakeGame())
            {
                GameDir = TestDir("game");
                HdPayload.Use(false, false);
                PatchHdGame(g);
                spoil(g);
                string said = PatchSaid(TryHdInject);
                Eq(-1, hdState, "state");
                Ok(g.AllZero(HdPayload.Code, HdPayload.CodeBytes.Length), "code not written");
                for (int i = 0; i < HdPayload.PatchAt.Length; i++)
                    EqBytes(HdPayload.PatchOld[i], g.Get(HdPayload.PatchAt[i], HdPayload.PatchOld[i].Length), "patch " + i + " not written");
                return said;
            }
        }

        [Test] static void Patch_Hd_RefusedExeNotPrepared()
        {
            string said = PatchHdRefused(delegate(FakeGame g) { g.PutUInt(HdPayload.PoolVar, 0x46B000); });
            Has(said, "HD 640x400 unavailable: __FF.EXE not prepared", "said");
        }

        [Test] static void Patch_Hd_RefusedMissionAlreadyRan()
        {
            string said = PatchHdRefused(delegate(FakeGame g) { g.PutUInt(HdPayload.PoolMax, 0x2000); });
            Has(said, "HD 640x400 unavailable: a mission already ran in this game", "said");
        }

        [Test] static void Patch_Hd_RefusedZoneNotFree()
        {
            string said = PatchHdRefused(delegate(FakeGame g) { g.Mem[HdPayload.End - 1] = 0x90; });
            Has(said, "HD 640x400 unavailable: its memory area is not free", "said");
        }

        [Test] static void Patch_Hd_AlreadyActive()
        {
            using (FakeGame g = new FakeGame())
            {
                GameDir = TestDir("game");
                HdPayload.Use(false, false);
                for (int i = 0; i < HdPayload.PatchAt.Length; i++) g.Put(HdPayload.PatchAt[i], HdPayload.PatchNew[i]);
                g.Put(HdPayload.Code, HdPayload.CodeBytes);
                g.PutUInt(HdPayload.PoolMax, 0x2000);   // a mission ran since: no matter, it is ours
                string said = PatchSaid(TryHdInject);
                Eq(1, hdState, "state");
                Has(said, "HD 640x400 already active in this game", "said");
                Ok(g.AllZero(HdPayload.Data, HdPayload.DataInit.Length), "data not written over the running HD code's");
            }
        }

        [Test] static void Patch_Hd_UnknownCodeAfter20Looks()
        {
            using (FakeGame g = new FakeGame())
            {
                GameDir = TestDir("game");              // no game bytes at the patches (DOS/4GW still relocating, or another exe)
                for (int i = 1; i < 20; i++) TryHdInject();
                Eq(0, hdState, "19 looks: still waiting");
                Eq(19, hdEarly, "looks counted");
                string said = PatchSaid(TryHdInject);
                Eq(-1, hdState, "20th look: unavailable");
                Has(said, "HD 640x400 unavailable: unsupported game version", "said");
            }
        }

        // ================================================================ span fix state machine (TNPlusSpanFix.cs)
        // HD on and injected, French pool there, this DOSBox watched from its start (OptInPlaceAttach: as the tests do)
        static void PatchSpanReady(FakeGame g)
        {
            HdPayload.Use(false, false);
            OptHD = true; hdState = 1; OptInPlaceAttach = true;
            PatchPool(g, false);
            spanKey = gamePid;
        }

        [Test] static void Patch_Span_OffWithoutTheHdCode()
        {
            OptHD = false; hdState = 1;
            TrySpanFix();
            Eq(-1, spanState, "HD off");
            spanState = 0; OptHD = true; hdState = -1;
            TrySpanFix();
            Eq(-1, spanState, "HD unavailable");
        }

        [Test] static void Patch_Span_WaitsForHdAndTheFreeArea()
        {
            using (FakeGame g = new FakeGame())
            {
                OptHD = true; OptInPlaceAttach = true; spanKey = gamePid;
                TrySpanFix();
                Eq(0, spanState, "HD not injected yet");
                hdState = 1;
                TrySpanFix();
                Eq(0, spanState, "game code not relocated yet (no pool)");
            }
        }

        [Test] static void Patch_Span_OnlyInTheDosboxItWatches()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchSpanReady(g);
                OptInPlaceAttach = false;               // a DOSBox found running (attach mode)
                TrySpanFix();
                Eq(-1, spanState, "attached: left as it is");
                No(spanTainted, "not tainted for that");
                spanState = 0; OptInPlaceAttach = true; spanKey = null;   // not the DOSBox the counts are about
                TrySpanFix();
                Eq(-1, spanState, "other DOSBox key");
                spanState = 0; spanKey = gamePid;
                TrySpanFix();
                Eq(0, spanState, "its own DOSBox: module not pristine here, looked at again");
                No(spanTainted, "not tainted");
            }
        }

        [Test] static void Patch_Span_TaintedOrThreeInstalls()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchSpanReady(g);
                spanTainted = true;
                TrySpanFix();
                Eq(-1, spanState, "the game's own module ran in this DOSBox");
                spanState = 0; spanTainted = false; spanInstalls = 2;
                TrySpanFix();
                Eq(0, spanState, "2 installs: may install again");
                spanInstalls = 3;
                TrySpanFix();
                Eq(-1, spanState, "3 installs: never again in this DOSBox");
            }
        }

        [Test] static void Patch_Span_MissionRanFirst()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchSpanReady(g);
                g.PutUInt(HdPayloadFr.PoolMax, 0x1000);
                TrySpanFix();
                Eq(-1, spanState, "state");
                Ok(spanTainted, "the original module may have run: tainted");
            }
        }

        [Test] static void Patch_Span_KeyIsTheProcessItStarted()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchSpanReady(g);
                OptInPlaceAttach = false;
                launched = Process.GetCurrentProcess(); // stands for the DOSBox this TN+ started (no process started)
                Eq(launched.Id, gamePid, "the game runs in it");
                Ok(SpanOwn(), "its own DOSBox");
                Ok(ReferenceEquals(launched, SpanKey()), "its key is the Process object");
                spanKey = Process.GetCurrentProcess();  // another Process object, same pid: another DOSBox start
                TrySpanFix();
                Eq(-1, spanState, "a reused pid is not the same DOSBox");
                spanState = 0; spanKey = launched;
                TrySpanFix();
                Eq(0, spanState, "same DOSBox: looked at");
                launchedDemo = true;
                No(SpanOwn(), "a demo: never");
                launchedDemo = false; gamePid = launched.Id + 1;
                No(SpanOwn(), "a game in another DOSBox: never");
                Eq((object)gamePid, SpanKey(), "its key is then the pid");
            }
        }

        [Test] static void Patch_Span_TickTakesItsDosbox()
        {
            using (FakeGame g = new FakeGame())
            {
                OptInPlaceAttach = true; OptHD = true;  // hdState 0: TrySpanFix waits
                spanKey = "another DOSBox"; spanInstalls = 3; spanTainted = true;
                SpanTick(1.0);
                Eq((object)gamePid, spanKey, "key: this DOSBox");
                Eq(0, spanInstalls, "a new DOSBox: no install yet");
                No(spanTainted, "a new DOSBox: not tainted");
                Ok(spanSeen, "its game start seen");
                Eq(1.0, spanLast, "TrySpanFix ran");
                spanInstalls = 2; spanTainted = true;
                SpanTick(2.0);
                Eq(2, spanInstalls, "same DOSBox: installs kept");
                Ok(spanTainted, "same DOSBox: taint kept");
            }
        }

        [Test] static void Patch_Span_TickNotItsDosbox()
        {
            using (FakeGame g = new FakeGame())
            {
                spanKey = 12345; spanInstalls = 1;      // OptInPlaceAttach off, launched null: not ours
                SpanTick(1.0);
                Eq((object)12345, spanKey, "key kept");
                Eq(1, spanInstalls, "installs kept");
                No(spanSeen, "not seen");
                Eq(-1, spanState, "OptHD off: left as it is");
            }
        }

        [Test] static void Patch_Span_TickEveryHalfSecond()
        {
            spanLast = 5.0;                             // OptHD off: TrySpanFix gives -1 whenever it runs
            SpanTick(5.4);
            Eq(0, spanState, "0.4 s after the last try: not tried");
            SpanTick(5.6);
            Eq(-1, spanState, "0.6 s after: tried");
            spanState = 0; spanLast = 5.6;
            SpanTick(1.0);
            Eq(-1, spanState, "clock earlier than the last try: tried");
            spanState = 0; spanLast = -10; OptSpeedFix = false;
            SpanTick(20.0);
            Eq(0, spanState, "speed_fixes off: never tried");
        }

        [Test] static void Patch_Span_TickChecksOursStaysInPlace()
        {
            using (FakeGame g = new FakeGame())
            {
                OptInPlaceAttach = true; spanKey = gamePid;
                spanState = 1; spanCheck = 2.6;
                SpanTick(3.0);
                Eq(1, spanState, "checked at most once a second");
                SpanTick(3.7);                          // the module is not ours (zeros): the game's code may run
                Eq(-1, spanState, "not in place any more");
                Ok(spanTainted, "tainted");
                Eq(3.7, spanCheck, "checked now");
            }
        }

        [Test] static void Patch_Span_GameLeft()
        {
            spanSeen = true; spanState = 1;
            SpanGameLeft();
            No(spanTainted, "it ran with our module: not tainted");
            No(spanSeen, "seen reset");
            Eq(0, spanState, "state reset");
            spanSeen = true; spanState = 0;
            SpanGameLeft();
            Ok(spanTainted, "it ran without our module: tainted");
            spanTainted = false; spanSeen = true; spanState = -1;
            SpanGameLeft();
            Ok(spanTainted, "left as the game had it: tainted");
            spanTainted = false; spanSeen = false; spanState = -1;
            SpanGameLeft();
            No(spanTainted, "a start TN+ did not see: not counted");
            Eq(0, spanState, "state reset");
        }

        // ================================================================ InPlaceSafe, KeysFollowMission
        [Test] static void Patch_InPlaceSafe_FirstGameStartOnly()
        {
            gamePid = 4242; OptInPlaceAttach = true;
            No(InPlaceSafe(), "no game start counted");
            pidGames[4242] = 1;
            Ok(InPlaceSafe(), "first game start (attach option)");
            pidGames[4242] = 2;
            No(InPlaceSafe(), "second game start in that DOSBox");
            pidGames[4242] = 1; gamePid = 4243;
            No(InPlaceSafe(), "another DOSBox");
        }

        [Test] static void Patch_InPlaceSafe_LaunchedOrAttachOption()
        {
            launched = Process.GetCurrentProcess();     // stands for the DOSBox this TN+ started (no process started)
            gamePid = launched.Id; pidGames[gamePid] = 1;
            Ok(InPlaceSafe(), "the DOSBox it started");
            launchedDemo = true;
            No(InPlaceSafe(), "a demo it started");
            OptInPlaceAttach = true;
            Ok(InPlaceSafe(), "the attach option overrides (tests only)");
            OptInPlaceAttach = false; launchedDemo = false;
            gamePid = launched.Id + 1; pidGames[gamePid] = 1;
            No(InPlaceSafe(), "a game in another DOSBox");
        }

        [Test] static void Patch_KeysFollowMission()
        {
            using (FakeGame g = new FakeGame())
            {
                g.PutInt(KB_ACTIVE, 7);
                KeysFollowMission(true);
                Eq(7, g.GetInt(KB_ACTIVE), "keys not in place: nothing written");
                keysState = 1;
                KeysFollowMission(true);
                Eq(1, g.GetInt(KB_ACTIVE), "mission: our key map on");
                Ok(keysActive, "active");
                g.PutInt(KB_ACTIVE, 7);
                KeysFollowMission(true);
                Eq(7, g.GetInt(KB_ACTIVE), "no change: not written again");
                KeysFollowMission(false);
                Eq(0, g.GetInt(KB_ACTIVE), "menus: the game's keys");
                No(keysActive, "not active");
            }
        }

        // ================================================================ infrared / normal view / affine entries
        [Test] static void Patch_Ir_EntriesSentToStubs()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                g.Put(IrFix.SiteSpan[0], IrFix.OldSpan[0]); g.Put(IrFix.SiteAffine[0], IrFix.OldAffine[0]);
                string said = PatchSaid(TryIrFix);
                Eq(1, irFixState, "entries in place, waiting for the buffer");
                Eq(0, irLang, "French");
                Ok(PatchIs(g, IrFix.StubSpan, IrFix.CodeStubSpan[0]), "span stub");
                Ok(PatchIs(g, IrFix.StubAffine, IrFix.CodeStubAffine[0]), "affine stub");
                Eq(IrFix.StubSpan, g.GetUInt(IrFix.PtrSpan), "span pointer to its stub");
                Eq(IrFix.StubAffine, g.GetUInt(IrFix.PtrAffine), "affine pointer to its stub");
                Ok(PatchIs(g, IrFix.SiteSpan[0], "FF25A8934600"), "span entry: jmp [0x4693A8]");
                Ok(PatchIs(g, IrFix.SiteAffine[0], "FF25AC934600"), "affine entry: jmp [0x4693AC]");
                Eq("", said, "nothing said yet");
            }
        }

        [Test] static void Patch_Ir_InPlaceNotPossibleFallsBackKeepingPointers()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, true);
                OptInPlaceAttach = true; pidGames[gamePid] = 1;   // in place tried first: not pristine (made up)
                g.Put(IrFix.SiteSpan[1], IrFix.OldSpan[1]); g.Put(IrFix.SiteAffine[1], IrFix.OldAffine[1]);
                g.PutUInt(IrFix.PtrSpan, 0x401000);     // already set (the rebuilt routine): kept
                TryIrFix();
                Eq(1, irFixState, "entries");
                Eq(1, irLang, "English");
                Eq(0x401000u, g.GetUInt(IrFix.PtrSpan), "span pointer kept");
                Eq(IrFix.StubAffine, g.GetUInt(IrFix.PtrAffine), "affine pointer set");
            }
        }

        [Test] static void Patch_Ir_UnknownCodeOrTakenStubArea()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                string said = PatchSaid(TryIrFix);
                Eq(-1, irFixState, "unknown entries");
                Has(said, "Infrared speed fix unavailable: unexpected game code", "said");
                irFixState = 0;
                g.Put(IrFix.SiteSpan[0], IrFix.OldSpan[0]); g.Put(IrFix.SiteAffine[0], IrFix.OldAffine[0]);
                g.Mem[IrFix.StubAffine + 3] = 0xCC;
                said = PatchSaid(TryIrFix);
                Eq(-1, irFixState, "stub area taken");
                Has(said, "Infrared speed fix unavailable: its memory area is not free", "said");
                Ok(PatchIs(g, IrFix.SiteSpan[0], IrFix.OldSpan[0]), "span entry left as the game has it");
                Ok(g.AllZero(IrFix.StubSpan, 13), "span stub not written");
            }
        }

        [Test] static void Patch_Ir_RebuildWaitsThenChecksTheGameCode()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, false);
                HdPayload.Use(false, false);
                irFixState = 1; irLang = 0;
                g.PutUInt(IrFix.PtrSpan, IrFix.StubSpan);
                TryIrFix();
                Eq(1, irFixState, "no 12 KB buffer yet");
                g.PutUInt(HdPayload.XCode, 0x400000);
                g.PutUInt(IrFix.PtrSpan, 0x400000 + (uint)IrFix.EntrySpan[0]);
                TryIrFix();
                Eq(2, irFixState, "the rebuilt routine is already the target: on");
                irFixState = 1;
                g.PutUInt(IrFix.PtrSpan, IrFix.StubSpan);
                string said = PatchSaid(TryIrFix);      // the game's routines are made up (zeros): SHA-256 mismatch
                Eq(-1, irFixState, "unexpected game code");
                Has(said, "Infrared speed fix unavailable: unexpected game code", "said");
                Ok(g.AllZero(0x400000, IrFix.Size[0]), "nothing copied into the buffer");
                Eq(IrFix.StubSpan, g.GetUInt(IrFix.PtrSpan), "pointer still to the stub");
            }
        }

        [Test] static void Patch_View_EntriesSentToStubs()
        {
            using (FakeGame g = new FakeGame())
            {
                PatchPool(g, true);
                for (int r = 0; r < 3; r++) g.Put(ViewFix.Site[1][r], ViewFix.Old[1][r]);
                TryViewFix();
                Eq(1, viewFixState, "entries in place");
                Eq(1, viewLang, "English");
                for (int r = 0; r < 3; r++)
                {
                    Ok(PatchIs(g, ViewFix.Stub[r], ViewFix.CodeStub[1][r]), "stub " + r);
                    Eq(ViewFix.Stub[r], g.GetUInt(ViewFix.Ptr[r]), "pointer " + r);
                    Ok(PatchIs(g, ViewFix.Site[1][r], ViewFix.New[r]), "entry " + r);
                }
                viewFixState = 1;
                HdPayload.Use(true, false);
                g.PutUInt(HdPayload.XCode, 0x400000);
                g.PutUInt(ViewFix.Ptr[0], 0x400000 + (uint)ViewFix.XOff + (uint)ViewFix.Entry[1][0]);
                TryViewFix();
                Eq(2, viewFixState, "rebuilt routines already the target: on");
            }
        }

        [Test] static void Patch_Affine_NeedsHdThenChecksTheTable()
        {
            using (FakeGame g = new FakeGame())
            {
                HdPayload.Use(false, false);
                TryAffineFix();
                Eq(0, affineState, "HD not injected");
                hdState = 1;
                TryAffineFix();
                Eq(0, affineState, "no buffer yet");
                g.PutUInt(HdPayload.XCode, 0x400000);
                string said = PatchSaid(TryAffineFix);
                Eq(-1, affineState, "renderer table not the game's");
                Has(said, "Polygon speed fix (affine) unavailable: unexpected game code", "said");
                affineState = 0;
                g.PutUInt(AffineFix.Table[1], 0x400000 + (uint)AffineFix.Off + (uint)AffineFix.Entry[1]);
                TryAffineFix();
                Eq(1, affineState, "already ours: on");
                affineState = 0;
                g.PutUInt(AffineFix.Table[1], 0); g.PutUInt(AffineFix.Table[0], AffineFix.Old[0]);
                said = PatchSaid(TryAffineFix);         // the game's module is made up (zeros): SHA-256 mismatch
                Eq(-1, affineState, "unexpected game code");
                Eq(AffineFix.Old[0], g.GetUInt(AffineFix.Table[0]), "table left as the game has it");
                Ok(g.AllZero(0x401000, AffineFix.Size[0]), "nothing copied");
            }
        }

        // ================================================================ the tables themselves
        [Test] static void Patch_Tables_OldAndNewOfEqualLength()
        {
            // TryFix / TryPieces / TryHdInject read as many bytes as the new ones and compare them with the old ones: a
            // length mismatch would make a fix never apply (or never be seen as done)
            byte[][][] pairs = { new[] { HITFIX_OLD, HITFIX_NEW }, new[] { PHYSFIX_OLD, PHYSFIX_NEW }, new[] { PHYSFIX2_OLD, PHYSFIX2_NEW },
                new[] { IDLEFIX_OLD, IDLEFIX_NEW }, new[] { IDLEFIX2_OLD, IDLEFIX2_NEW }, new[] { UIFIX_OLD, UIFIX_NEW } };
            for (int i = 0; i < pairs.Length; i++) Eq(pairs[i][0].Length, pairs[i][1].Length, "TryFix pair " + i);
            for (int i = 0; i < 3; i++) Eq(SMOOTH_OLD[i].Length, SMOOTH_NEW[i].Length, "smooth " + i);
            foreach (CodePatch p in OBJ_PATCHES)
            {
                Eq(p.Old.Length, p.OldEn.Length, "object distance 0x" + p.Fr.ToString("X") + ": French / English");
                Eq(p.Old.Length / 2, p.New != "" ? p.New.Length / 2 : 6, "object distance 0x" + p.Fr.ToString("X") + ": old / new");
            }
            string[] names = { "turret", "descrambler", "filler", "ashford", "sky", "drone", "smoke" };
            uint[][][] ats = { TurretFix.At, DescramblerFix.At, FillerFix.At, AshfordFix.At, NativeFixes.SkyAt, NativeFixes.DroneAt, SMOKE_AT };
            string[][][] olds = { TurretFix.Old, DescramblerFix.Old, FillerFix.Old, AshfordFix.Old, NativeFixes.SkyOld, NativeFixes.DroneOld, SMOKE_OLD };
            string[][][] news = { TurretFix.New, DescramblerFix.New, FillerFix.New, AshfordFix.New, NativeFixes.SkyNew, NativeFixes.DroneNew, SMOKE_NEW };
            for (int t = 0; t < names.Length; t++)
            {
                Eq(2, ats[t].Length, names[t] + ": two exe");
                for (int l = 0; l < ats[t].Length; l++)
                {
                    Eq(ats[t][l].Length, olds[t][l].Length, names[t] + " exe " + l + ": old pieces");
                    Eq(ats[t][l].Length, news[t][l].Length, names[t] + " exe " + l + ": new pieces");
                    for (int i = 0; i < ats[t][l].Length; i++) Eq(olds[t][l][i].Length, news[t][l][i].Length, names[t] + " exe " + l + " piece " + i);
                }
            }
            for (int l = 0; l < 2; l++)
            {
                Eq(IrFix.OldSpan[l].Length, IrFix.NewSpan[l].Length, "infrared span entry " + l);
                Eq(IrFix.OldAffine[l].Length, IrFix.NewAffine[l].Length, "infrared affine entry " + l);
                for (int r = 0; r < 3; r++) Eq(ViewFix.Old[l][r].Length, ViewFix.New[r].Length, "view entry " + l + "/" + r);
            }
            for (int f = 0; f < 6; f++)
            {
                HdPayload.Use(f % 2 == 1, f >= 2, f >= 4);
                Eq(HdPayload.PatchAt.Length, HdPayload.PatchOld.Length, "HD flavour " + f + ": old patches");
                Eq(HdPayload.PatchAt.Length, HdPayload.PatchNew.Length, "HD flavour " + f + ": new patches");
                for (int i = 0; i < HdPayload.PatchAt.Length; i++)
                {
                    Eq(HdPayload.PatchOld[i].Length, HdPayload.PatchNew[i].Length, "HD flavour " + f + " patch " + i);
                    for (int j = i + 1; j < HdPayload.PatchAt.Length; j++)   // no two patches over the same bytes
                        No(HdPayload.PatchAt[i] < HdPayload.PatchAt[j] + HdPayload.PatchOld[j].Length && HdPayload.PatchAt[j] < HdPayload.PatchAt[i] + HdPayload.PatchOld[i].Length,
                            "HD flavour " + f + ": patches " + i + " and " + j + " overlap");
                }
            }
        }

        [Test] static void Patch_Tables_FreeAreaCavesApart()
        {
            // the 24 KB free area of the prepared exe (HD zone start to pool base): every cave and variable of the fixes in
            // it, per exe, never on top of another one nor of the HD code or data
            for (int l = 0; l < 2; l++)
            {
                List<string> name = new List<string>();
                List<uint> from = new List<uint>(), to = new List<uint>();
                Action<string, uint, int> add = delegate(string n, uint at, int len) { name.Add(n); from.Add(at); to.Add(at + (uint)len); };
                for (int i = 0; i < TurretFix.Zones; i++) add("turret code " + i, TurretFix.At[l][i], TurretFix.New[l][i].Length / 2);
                add("turret variable", 0x469390, 4);
                for (int i = 0; i < DescramblerFix.Zones; i++) add("descrambler code " + i, DescramblerFix.At[l][i], DescramblerFix.New[l][i].Length / 2);
                add("descrambler variables", 0x4693A0, 8);
                add("filler code", FillerFix.At[l][0], FillerFix.New[l][0].Length / 2);
                add("filler variables", 0x4693F8, 8);
                int a = 1 - l;                          // AshfordFix: English then French
                for (int i = 0; i < 2; i++) add("ashford " + i, AshfordFix.At[a][i], AshfordFix.New[a][i].Length / 2);
                add("infrared span stub", IrFix.StubSpan, IrFix.CodeStubSpan[l].Length / 2);
                add("infrared affine stub", IrFix.StubAffine, IrFix.CodeStubAffine[l].Length / 2);
                add("infrared pointers", IrFix.PtrSpan, 4); add("infrared pointers 2", IrFix.PtrAffine, 4);
                for (int r = 0; r < 3; r++) { add("view stub " + r, ViewFix.Stub[r], ViewFix.CodeStub[l][r].Length / 2); add("view pointer " + r, ViewFix.Ptr[r], 4); }
                add("zoom cave", ZOOMFIX_CAVE, 12);
                add("fov data", FOV_CAVE, 16); add("fov code", FOV_CAVE + 0x10, FOV_CODE[l].Length / 2);
                add("wedge", WEDGE_CAVE, WEDGE_CODE[l].Length / 2);
                add("true 16:9 set-up", TRUE_A, TRUE_A_CODE[l].Length / 2); add("true 16:9 scale", TRUE_B, TRUE_B_CODE[l].Length / 2);
                add("true 16:9 variables", 0x469400, 8);
                add("edge objects data", 0x469410, 0x469A00 - 0x469410);
                foreach (string[] c in EDGE_CAVES[l]) add("edge objects " + c[0], Convert.ToUInt32(c[0], 16), c[1].Length / 2);
                add("game keys data", KB_DATA, (int)(KB_DATA_END - KB_DATA));
                add("game keys code", KB_CODE, KB_CODE_HEX.Length / 2); add("game keys sweep", KB_SWEEP, KB_SWEEP_HEX.Length / 2);
                Ok(EDGE_MODE >= 0x469410 && EDGE_SIN >= 0x469410 && EDGE_SIN + EDGE_SINTAB.Length / 2 <= 0x469A00, "edge objects variables in their data");
                Ok(KB_ACTIVE >= KB_DATA && KB_MAP + 1024 <= KB_DATA_END, "game keys variables in their data");
                int caves = name.Count;
                for (int f = 0; f < 3; f++)
                {
                    HdPayload.Use(l == 1, f > 0, f == 2);
                    add("HD code " + f, HdPayload.Code, HdPayload.CodeBytes.Length);
                    add("HD data " + f, HdPayload.Data, HdPayload.DataInit.Length);
                    Ok(HdPayload.Data + HdPayload.DataInit.Length <= HdPayload.End, "HD " + f + ": data within its zone");
                }
                for (int i = 0; i < name.Count; i++)
                {
                    Ok(from[i] >= HdPayloadFr.Zone && to[i] <= HdPayloadFr.PoolBase, "exe " + l + ": " + name[i] + " (0x" + from[i].ToString("X") + "-0x" + to[i].ToString("X") + ") in the free area");
                    for (int j = i + 1; j < name.Count; j++)
                        if (i < caves || j < caves)     // the HD flavours share the zone with each other, never with a cave
                            No(from[i] < to[j] && from[j] < to[i], "exe " + l + ": " + name[i] + " (0x" + from[i].ToString("X") + "-0x" + to[i].ToString("X") + ") and " + name[j] +
                                " (0x" + from[j].ToString("X") + "-0x" + to[j].ToString("X") + ") overlap");
                }
            }
        }

        [Test] static void Patch_Tables_InPlaceCopiesOnlyCheckedBytes()
        {
            // TryInPlace writes only what the SHA-256 covers and copies only checked bytes; its pieces add up to each size
            string[] names = { "infrared in place", "spans in place" };
            uint[][][] at = { IrInPlace.At, SpanInPlace.At };
            int[][][] size = { IrInPlace.Size, SpanInPlace.Size };
            string[][][][] pieces = { IrInPlace.Pieces, SpanInPlace.Pieces };
            uint[][][] checkAt = { IrInPlace.CheckAt, SpanInPlace.CheckAt };
            int[][][] checkSize = { IrInPlace.CheckSize, SpanInPlace.CheckSize };
            for (int t = 0; t < names.Length; t++)
                for (int l = 0; l < at[t].Length; l++)
                {
                    Func<uint, int, bool> covered = delegate(uint a, int n)
                    {
                        for (uint x = a; x < a + n; x++)
                        {
                            bool c = false;
                            for (int i = 0; i < checkAt[t][l].Length && !c; i++) c = x >= checkAt[t][l][i] && x < checkAt[t][l][i] + checkSize[t][l][i];
                            if (!c) return false;
                        }
                        return true;
                    };
                    Eq(at[t][l].Length, pieces[t][l].Length, names[t] + " " + l + ": entries");
                    for (int e = 0; e < at[t][l].Length; e++)
                    {
                        int n = 0;
                        foreach (string p in pieces[t][l][e])
                        {
                            Ok(p.StartsWith("C:") || p.StartsWith("B:"), names[t] + ": piece " + p);
                            if (p[0] == 'C')
                            {
                                string[] f = p.Split(':');
                                int len = Convert.ToInt32(f[2], 16);
                                Ok(covered(Convert.ToUInt32(f[1], 16), len), names[t] + " " + l + " entry " + e + ": " + p + " copies checked bytes");
                                n += len;
                            }
                            else n += (p.Length - 2) / 2;
                        }
                        Eq(size[t][l][e], n, names[t] + " " + l + " entry " + e + ": pieces add up to its size");
                        Ok(covered(at[t][l][e], size[t][l][e]), names[t] + " " + l + " entry " + e + ": rewrites checked bytes only");
                    }
                }
            // the rebuilt copies (in the HD buffer): pieces add up to their size
            string[][][] rebuilt = { IrFix.Pieces, ViewFix.Pieces, AffineFix.Pieces };
            int[][] rsize = { IrFix.Size, ViewFix.Size, AffineFix.Size };
            for (int t = 0; t < rebuilt.Length; t++)
                for (int l = 0; l < rebuilt[t].Length; l++)
                {
                    int n = 0;
                    foreach (string p in rebuilt[t][l]) n += p[0] == 'C' ? Convert.ToInt32(p.Split(':')[2], 16) : (p.Length - 2) / 2;
                    Eq(rsize[t][l], n, "rebuilt table " + t + " exe " + l + ": size");
                }
        }

        [Test] static void Patch_Tables_ShaLowercaseHex()
        {
            // the code compares its lowercase hex SHA-256 with these strings as they are
            List<string> all = new List<string>();
            all.AddRange(IrInPlace.Sha); all.AddRange(SpanInPlace.Sha); all.AddRange(SpanInPlace.OnSha); all.AddRange(SpanInPlace.RanSha);
            all.AddRange(IrFix.Sha); all.AddRange(ViewFix.Sha); all.AddRange(AffineFix.Sha);
            foreach (string s in all)
            {
                Eq(64, s.Length, "SHA-256 " + s + ": 64 characters");
                foreach (char c in s) Ok((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), "SHA-256 " + s + ": lowercase hex");
            }
            Eq(2, SpanInPlace.OnMask.Length, "span masks: two exe");
            for (int l = 0; l < 2; l++)
                foreach (int[] m in new[] { SpanInPlace.OnMask[l], SpanInPlace.RanMask[l] })
                {
                    Eq(0, m.Length % 2, "span mask " + l + ": offset, length pairs");
                    for (int i = 0; i < m.Length; i += 2) Ok(m[i] >= 0 && m[i + 1] > 0 && m[i] + m[i + 1] <= SpanInPlace.Size[l][0], "span mask " + l + " field " + i / 2 + " inside the module");
                }
        }
    }
}
