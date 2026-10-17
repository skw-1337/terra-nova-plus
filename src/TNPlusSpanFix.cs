// Normal-view textured spans (tools/spans_t14.py, table src/TNPlusSpanInPlace.cs): the game's span module
// (EN 0x34D2B0..0x34D50F, FR +0x280) rewritten IN PLACE at the menu while it never ran (SHA-256 of the pristine module
// with its slot placeholders + its three call sites). One translated DOSBox block per pixel instead of 3-4, no more
// retranslation of its 'adc eax,imm32'. With the HD code (like the affine fix: speed_fixes is an HD option), but it
// needs neither the HD buffer nor GP-block bytes. TN+ ships no game code: moved instructions are copied from the
// player's memory (C: pieces) before anything is written.
//
// Written from outside, which DOSBox does not see: it keeps running what it translated before. So the module is only
// rewritten if DOSBox never translated the original in THIS DOSBox process, not only in this game start. When the game
// starts again in the same DOSBox (TN+'s conf loops on the GOG launcher), its loader writes the original back, and the
// parts it leaves unchanged keep their old translations (set-up entries, old common part): they would run against our
// bytes and write into them (crash at the first span). Hence:
//  - only in a DOSBox started by this TN+ (Run() watches it from its start to its exit, attached to it alone, so every
//    game start in it is seen at its menu), never in one found running (attach mode, TN+ restarted: unknown past);
//  - only in a game start where no mission ran yet (PoolMax 0) and the module is pristine (never ran);
//  - never again in that DOSBox once one of its game starts ran without our module (the original ran, or may have):
//    decided when the game leaves (SpanGameLeft), and as soon as the original is seen run (RanSha);
//  - at most 3 times per DOSBox: each restart after our code ran adds 1 to DOSBox's change count of every byte that
//    differs from the original (from 4 changes on, an opcode byte runs in the interpreter at every pass).
using System;
using System.Diagnostics;

static partial class TNPlus
{
    static int spanState = 0;           // this game start: 0 to do, 1 ours in place, -1 left as the game has it
    static double spanLast = -10;       // last TrySpanFix, main loop clock
    static object spanKey = null;       // the DOSBox (started by this TN+) that the next three are about
    static int spanInstalls = 0;        // its game starts with our module written in
    static bool spanTainted = false;    // one of its game starts ran (or may have run) the game's own module
    static bool spanSeen = false;       // its current game start was seen (TN+ attached to it)
    static double spanCheck = -10;      // last check that our module is still in place

    // Main loop, on every pass with the game attached, right after 'bool fg = ForegroundIsGame();': SpanTick(now);
    static void SpanTick(double now)
    {
        try
        {
            if (SpanOwn())
            {
                if (!Equals(spanKey, SpanKey())) { spanKey = SpanKey(); spanInstalls = 0; spanTainted = false; }
                spanSeen = true;
            }
            if (OptSpeedFix && spanState == 0 && (now - spanLast > 0.5 || now < spanLast)) { spanLast = now; TrySpanFix(); }
            // ours must stay in place for the whole start: a restart the loop did not see, or anything rewriting the
            // module, means the game's own code may run in this DOSBox
            if (spanState == 1 && SpanOwn() && (now - spanCheck > 1 || now < spanCheck))
            { spanCheck = now; if (!SpanInPlaceOn()) { spanState = -1; spanTainted = true; } }
        }
        catch { }                       // memory not readable just now: looked at again on a later pass
    }

    // Main loop, where the other fix states are reset when the game left (that block runs on every pass until the
    // next game is attached): SpanGameLeft();
    static void SpanGameLeft()
    {
        if (spanSeen && spanState != 1) spanTainted = true;     // that game start ran without our module
        spanSeen = false; spanState = 0;
    }

    // the attached game runs in the DOSBox this TN+ started (not one of the 1996 demos: another executable).
    // inplace_attach = 1 (ini, TESTS ONLY, not safe): also the DOSBox it was attached to, which it may not have
    // watched from its start (the test harness starts DOSBox, then TN+ with --pid, before the game).
    // which DOSBox: the Process TN+ started (a new one is a new DOSBox even with a reused pid); tests: the pid
    static object SpanKey()
    {
        return launched != null && gamePid == launched.Id ? (object)launched : (object)gamePid;
    }

    static bool SpanOwn()
    {
        return !launchedDemo && gamePid != 0 && ((launched != null && gamePid == launched.Id) || OptInPlaceAttach);
    }

    static void TrySpanFix()
    {
        if (!OptHD || hdState == -1) { spanState = -1; return; }   // without the HD code: left as it is
        if (hdState != 1 || !FreeAreaReady()) return;              // HD not injected yet / game code not relocated yet
        if (SpanInPlaceOn()) { spanState = 1; Say("Polygon speed fix (spans) already ON", 0); return; }
        // a DOSBox this TN+ has not watched since it started, one where the original module ran (or may have), or
        // already 3 installs: the game's code is left as it is, silently
        if (!SpanOwn() || !Equals(spanKey, SpanKey()) || spanTainted || spanInstalls >= 3) { spanState = -1; return; }
        if (ReadUInt(HdPayload.PoolMax) != 0) { spanState = -1; spanTainted = true; return; }   // a mission ran first
        int r = TryInPlace(SpanInPlace.At, SpanInPlace.Size, SpanInPlace.Pieces, SpanInPlace.CheckAt, SpanInPlace.CheckSize, SpanInPlace.Sha);
        if (r == 0) return;                                         // memory not readable yet
        if (r == 1)
        {
            spanState = 1; spanInstalls++;
            Say("Polygon speed fix (spans) ON", 0);
            return;
        }
        // not pristine: the game's own module with its fields rewritten = it ran in this game start
        if (SpanMatches(SpanInPlace.RanMask, SpanInPlace.RanSha)) { spanState = -1; spanTainted = true; }
        // anything else (not relocated yet, other bytes): looked at again on a later pass, as long as no mission ran
    }

    // Already on (TN+ attached again after installing it): the module with the fields it rewrites at run time
    // (SpanInPlace.OnMask: offset, length pairs) zeroed, then the three call sites, hashed.
    static bool SpanInPlaceOn()
    {
        return SpanMatches(SpanInPlace.OnMask, SpanInPlace.OnSha);
    }

    // the module (At[l][0]) with the given fields zeroed, then the three call sites (OnCallAt[l]): SHA-256 = sha[l]
    // for one of the languages
    static bool SpanMatches(int[][] mask, string[] sha)
    {
        for (int l = 0; l < sha.Length; l++)
        {
            byte[] m = Read(SpanInPlace.At[l][0], SpanInPlace.Size[l][0]);
            if (m == null) return false;
            int[] k = mask[l];
            for (int i = 0; i < k.Length; i += 2) Array.Clear(m, k[i], k[i + 1]);
            var all = new System.IO.MemoryStream();
            all.Write(m, 0, m.Length);
            foreach (uint a in SpanInPlace.OnCallAt[l])
            {
                byte[] c = Read(a, 5);
                if (c == null) return false;
                all.Write(c, 0, c.Length);
            }
            byte[] sum;
            using (var h = System.Security.Cryptography.SHA256.Create()) sum = h.ComputeHash(all.ToArray());
            if (BitConverter.ToString(sum).Replace("-", "").ToLowerInvariant() == sha[l]) return true;
        }
        return false;
    }
}
