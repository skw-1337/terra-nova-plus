# Normal view without self-modifying code (same method as ir_fix.py, after Callemann's IR fix).
# The two per-column routines of the 3D library, called for each polygon edge (from 0x2FB220), write operands INTO
# their own loops at every call: 0x317DF0 tests the edge against a per-column buffer, 0x317EA0 updates it. Both write
# the buffer's end into their loop's displacements, 0x317EA0 also its return value and the target of a jump (whether
# a column has been updated yet). DOSBox's dynamic core then translates those loops again and again. The new routines
# read the operand slots as data, at the same addresses: the set-up code is unchanged and writes into the old loops,
# which no longer run. In the new 0x317EA0 that jump always goes to the "first update" path: once a column is
# updated, that path only rewrites the same return value and jump target. Panama, SHARPER, true 16:9: 41 -> 46 fps
# standing at the locks, 37 -> 45 turning there, same fps where the view is light.
# Not done: the textured span loop (EN 0x34D390) is rewritten per span the same way, but reading its eleven operands
# from memory costs more per pixel than the retranslation saves (5 % slower in light views, no gain in heavy ones).
# An indexed load with a rewritten displacement goes through eax, which the loop reloads anyway (mov leaves the
# flags alone). French exe: both routines +0x280, checked instruction by instruction.
#   python view_fix.py EN_IMAGE FR_IMAGE          layout and checks (images: RAM 0x1AB000-0x470000 at the main menu)
#   python view_fix.py EN_IMAGE FR_IMAGE cs       C# table (src/TNPlusView.cs)
import hashlib, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from keystone import Ks, KS_ARCH_X86, KS_MODE_32
import ir_fix as F

FR_DELTA = 0x280
XOFF = 0x600                    # offset in the HD code's buffer (XCODE), after the infrared routines (1282 bytes)
ORDER = ('coltest', 'colupdate')
ENTRY = {'coltest': 0x317DF0, 'colupdate': 0x317EA0}
STUB = {'coltest': 0x469F40, 'colupdate': 0x469F50}       # free gap of the tool's code area
PTR = {'coltest': 0x4693F0, 'colupdate': 0x4693F4}        # data page: where each entry jumps
FREE = ((0x469F30, 0x46A000),)

lde = lambda a: 'mov eax, dword ptr [%#x];mov eax, dword ptr [edi+eax];' % a        # mov eax,[edi+slot]
ste = lambda a: 'mov eax, dword ptr [%#x];mov dword ptr [edi+eax], esi;' % a        # mov [edi+slot],esi (eax dead)
RECIPES = {   # EN addresses: low, high, no digest (the slots differ from call to call), edits {start: (stop, text)}
    'coltest': (0x317DF0, 0x317E9D, None, {
        0x317e82: (0x317e88, lde(0x317e84)),                    # the buffer's end, written at 0x317E07
    }),
    'colupdate': (0x317EA0, 0x317FAB, None, {
        0x317f5a: (0x317f60, ste(0x317f5c)),                    # the buffer's end, written at 0x317EBE-0x317ED0
        0x317f74: (0x317f7a, lde(0x317f76)),
        0x317f80: (0x317f86, ste(0x317f82)),
        0x317f94: (0x317f9a, lde(0x317f96)),
        0x317f9e: (0x317fa0, '@jg 0x317f50;'),                  # its target byte, written at 0x317EF5 / 0x317F62
        0x317fa5: (0x317faa, 'mov eax, dword ptr [0x317fa6];'), # the return value, written at 0x317EA5 / 0x317F55
    }),
}


def entry_stub(read, s, at):
    """the instructions under the 6-byte entry jmp, then a jump to the rest of the original routine"""
    ins, n = [], 0
    for i in Cs(CS_ARCH_X86, CS_MODE_32).disasm(read(s, 16), s):
        assert i.mnemonic[0] != 'j' and i.mnemonic not in ('call', 'ret', 'loop'), i.mnemonic
        ins.append('%s %s' % (i.mnemonic, i.op_str)); n += i.size
        if n >= 6: break
    return bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm('; '.join(ins + ['jmp %#x' % (s + n)]), at)[0])


def build(en_read, fr_read=None):
    out = {}
    for lang, read, d in (('EN', en_read, 0), ('FR', fr_read, FR_DELTA)):
        if read is None: continue
        pieces_all, copied_all, total, entry, site, stub = [], b'', 0, {}, {}, {}
        for name in ORDER:
            rec = RECIPES[name] if d == 0 else F.shifted(RECIPES[name], d)
            if d: F.same_shape(en_read, fr_read, RECIPES[name][0], RECIPES[name][1], d, RECIPES[name][3])
            s = ENTRY[name] + d
            pieces, copied, lab, size = F.layout(read, rec, s)
            pad = (-total) % 16
            if pad: pieces_all.append(('B', b'\x90' * pad)); total += pad
            entry[name] = total + lab[s]
            pieces_all += pieces; copied_all += copied; total += size
            site[name] = (s, read(s, 6), b'\xff\x25' + struct.pack('<I', PTR[name]))
            stub[name] = entry_stub(read, s, STUB[name])
            assert any(a <= STUB[name] and STUB[name] + len(stub[name]) <= b for a, b in FREE)
            _, ins = F.disasm(read, rec[0], rec[1])
            for i in ins:                   # nothing in the routine jumps into the 6 bytes the entry jmp takes
                if i.mnemonic.startswith('j'): assert not (s < int(i.op_str, 16) < s + 6)
        spans = sorted((STUB[n], STUB[n] + len(stub[n])) for n in ORDER)
        assert all(a[1] <= b[0] for a, b in zip(spans, spans[1:])), 'stubs overlap'
        assert XOFF + total <= 0x2000       # 12 KB buffer, its first page boundary at most 0xFFF bytes in
        out[lang] = dict(pieces=pieces_all, sha=hashlib.sha256(copied_all).hexdigest(), entry=entry, size=total,
                         site=site, stub=stub, copied=len(copied_all))
    return out


def export_cs(out):
    print('// Generated by tools/view_fix.py (python view_fix.py EN_IMAGE FR_IMAGE cs > ../src/TNPlusView.cs): do not edit.')
    print('// Normal view without self-modifying code: pieces C:address:length are copied from the game\'s own memory,')
    print('// B: pieces are ours (edits, re-encoded branches). Routines: %s. See tools/view_fix.py.' % ', '.join(ORDER))
    print('static class ViewFix\n{')
    L = ('EN', 'FR')
    q = lambda xs: ', '.join(xs)
    print('    public const int XOff = 0x%X;' % XOFF)
    print('    public static readonly uint[] Stub = { %s }, Ptr = { %s };' % (
        q('0x%X' % STUB[n] for n in ORDER), q('0x%X' % PTR[n] for n in ORDER)))
    print('    public static readonly string[] New = { %s };' % q('"%s"' % out['EN']['site'][n][2].hex().upper() for n in ORDER))
    rows = lambda f: ', '.join('new %s { %s }' % f(l) for l in L)
    print('    public static readonly uint[][] Site = { %s };' % rows(lambda l: ('uint[]', q('0x%X' % out[l]['site'][n][0] for n in ORDER))))
    print('    public static readonly string[][] Old = { %s };' % rows(lambda l: ('string[]', q('"%s"' % out[l]['site'][n][1].hex().upper() for n in ORDER))))
    print('    public static readonly string[][] CodeStub = {')
    for l in L: print('        new string[] { %s },' % q('"%s"' % out[l]['stub'][n].hex().upper() for n in ORDER))
    print('    };')
    print('    public static readonly int[][] Entry = { %s };' % rows(lambda l: ('int[]', q(str(out[l]['entry'][n]) for n in ORDER))))
    print('    public static readonly int[] Size = { %s };' % q(str(out[l]['size']) for l in L))
    print('    public static readonly string[] Sha = { %s };' % q('"%s"' % out[l]['sha'] for l in L))
    print('    public static readonly string[][] Pieces = {')
    for l in L:
        ps = ['"C:%X:%X"' % (p[1], p[2]) if p[0] == 'C' else '"B:%s"' % p[1].hex().upper() for p in out[l]['pieces']]
        print('        new string[] {')
        for k in range(0, len(ps), 6): print('            ' + ', '.join(ps[k:k + 6]) + ',')
        print('        },')
    print('    };\n}')


if __name__ == '__main__':
    en, fr = F.load(sys.argv[1]), F.load(sys.argv[2])
    out = build(en, fr)
    if 'cs' in sys.argv: export_cs(out)
    else:
        for l, o in out.items():
            print(l, 'block %d bytes, %d bytes copied from the game, %d pieces, sha %s' % (
                o['size'], o['copied'], len(o['pieces']), o['sha'][:16]))
            for n in ORDER:
                print('  %-9s entry +%-4d site %#x old %s new %s | stub %s' % (n, o['entry'][n], o['site'][n][0],
                      o['site'][n][1].hex(), o['site'][n][2].hex(), o['stub'][n].hex()))
