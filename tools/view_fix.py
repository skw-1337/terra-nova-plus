# Normal view without self-modifying code (same method as ir_fix.py, after Callemann's IR fix).
# The two per-column routines of the 3D library, called for each polygon edge (from 0x2FB220), write operands INTO
# their own loops at every call: 0x317DF0 tests the edge against a per-column buffer, 0x317EA0 updates it. Both write
# the buffer's end into their loop's displacements, 0x317EA0 also its return value and the target of a jump (whether
# a column has been updated yet). DOSBox's dynamic core then translates those loops again and again. The new routines
# read the operand slots as data, at the same addresses: the set-up code is unchanged and writes into the old loops,
# which no longer run. In the new 0x317EA0 that jump always goes to the "first update" path: once a column is
# updated, that path only rewrites the same return value and jump target. Panama, SHARPER, true 16:9: 41 -> 46 fps
# standing at the locks, 37 -> 45 turning there, same fps where the view is light.
# What costs is the jump target: DOSBox's dyn_x86 core reads rewritten immediates and [reg+disp32] displacements
# from memory once they have changed (see filler_fix.py), not branch displacements. Not done: the textured span loop
# (EN 0x34D390) rewrites its operands per span too, but nearly all of them are of the kind DOSBox reads from memory;
# a rewrite was 5 % slower.
# An indexed load with a rewritten displacement goes through eax, which the loop reloads anyway (mov leaves the
# flags alone). French exe: both routines +0x280, checked instruction by instruction.
#   python view_fix.py EN_IMAGE FR_IMAGE          layout and checks (images: RAM 0x1AB000-0x470000 at the main menu)
#   python view_fix.py EN_IMAGE FR_IMAGE cs       C# table (src/TNPlusView.cs)
import hashlib, re, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from keystone import Ks, KS_ARCH_X86, KS_MODE_32
import ir_fix as F

FR_DELTA = {'coltest': 0x280, 'colupdate': 0x280, 'colpass': -0x10}   # French exe: where each routine sits
XOFF = 0x600                    # offset in the HD code's buffer (XCODE), after the infrared routines (1282 bytes)
ORDER = ('coltest', 'colupdate', 'colpass')
ENTRY = {'coltest': 0x317DF0, 'colupdate': 0x317EA0, 'colpass': 0x2B2785}
STUB = {'coltest': 0x469F40, 'colupdate': 0x469F50, 'colpass': 0x469F90}     # free gaps of the tool's code area
PTR = {'coltest': 0x4693F0, 'colupdate': 0x4693F4, 'colpass': 0x4693EC}      # data page: where each entry jumps
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
    # water / haze pass of the terrain, column by column (called from 0x2A87E7): same pixels, same results, but the
    # usual pixel (colour not in the table) costs 5 instructions instead of 9 -- the source position and the row
    # table pointer it would step along are only worked out for the pixels it changes (esi + edi stays constant down
    # a column, the row pointer follows the counter); no longer writes its table address into its own loop
}


# Water / haze pass, version 4 (perf study 2026-10-17, lot 3): a guard first (the caller's table pointers [ebx+4] /
# [ebx+8] must be the constants below, else the previous body runs: cp_old), then usual pixels 4 at a time (4 lookups
# OR-ed, one jnz), a group with water done pixel by pixel in the game's order, a tail of 1-3 pixels one by one. Exit
# registers and flags as the game's routine. Checked in Unicorn against 0x2B2785 (EN and FR, 1200+ calls).
CP_LANG = {False: 'EN', True: 'FR'}
CP_ENTRY = {False: 0x2B2785, True: 0x2B2775}          # the routine (entry hook site)
CP_HIGH = {False: 0x2B27DA, True: 0x2B27CA}           # its end (ret + 1)
CP_CALLER = {False: 0x2A87B6, True: 0x2A87A6}         # 'mov edx,ROWS' .. 'call colpass' (the only caller)
CP_CONST = {False: (0x423D40, 0x424980), True: (0x423DF0, 0x424A30)}   # T1 (dword per colour), FT (byte per colour)
CP_ROWS = {False: 0x424140, True: 0x4241F0}           # row-remap pointer table ([ebx] = CP_ROWS + 4*row)
CP_SLOT = {False: 0x2B27BB, True: 0x2B27AB}           # the original's SMC slot (T1, written at entry): dead once hooked
CP_VIEW_LIMIT = 0x1000                                 # affine_fix block at page base + 0x1000

SHIPPED_BODY = '''
    mov eax, dword ptr [ebx+4]
    mov ebp, dword ptr [ebx+8]
    mov ebx, dword ptr [ebx]
    add esi, edx
    sub esp, 12
    mov dword ptr [esp], eax
    lea eax, [esi+edi]
    sub eax, edx
    mov dword ptr [esp+4], eax
    lea eax, [ebx+ecx*4]
    mov dword ptr [esp+8], eax
cp_loop:
    movzx eax, byte ptr [edi]
    cmp byte ptr [eax+ebp], 0
    jne cp_spec
cp_next:
    add edi, edx
    dec ecx
    jnz cp_loop
    pushfd
    mov ebx, dword ptr [esp+12]
    mov esi, dword ptr [esp+8]
    sub esi, edi
    add esi, edx
    popfd
    lea esp, [esp+12]
    pop ebp
    ret
cp_spec:
    shl eax, 2
    add eax, dword ptr [esp]
    mov eax, dword ptr [eax]
    add eax, dword ptr [esp+4]
    sub eax, edi
    movzx eax, byte ptr [eax]
    mov ebx, ecx
    shl ebx, 2
    neg ebx
    add ebx, dword ptr [esp+8]
    add eax, dword ptr [ebx]
    mov al, byte ptr [eax]
    mov byte ptr [edi], al
    xor eax, eax
    jmp cp_next
'''


def _spec(T1, rowoff):
    """water pixel: source = T1[c] + (esi0 + edi0) - edi, value = remap_row[source byte]; eax = c on entry"""
    return '''
    mov eax, dword ptr [eax*4+%#x]
    add eax, esi
    sub eax, edi
    movzx eax, byte ptr [eax]
    add eax, dword ptr [ebp+ecx*4+%d]
    mov al, byte ptr [eax]
    mov byte ptr [edi], al''' % (T1, rowoff)


def colpass_text(T1, FT):
    """edit text that replaces the routine after its 6 entry bytes (push ebp / mov ebp,imm, copied by layout)"""
    s = '''
    mov eax, dword ptr [ebx+4]
    mov ebp, dword ptr [ebx+8]
    sub eax, %(T1)#x
    sub ebp, %(FT)#x
    or eax, ebp
    jnz cp_old
    mov ebp, dword ptr [ebx]
    lea ebp, [ebp+ecx*4]
    add esi, edi
    neg ecx
    cmp ecx, -4
    jg t_top
g_top:
    movzx eax, byte ptr [edi]
    movzx ebx, byte ptr [edi+edx]
    mov al, byte ptr [eax+%(FT)#x]
    or al, byte ptr [ebx+%(FT)#x]
    movzx ebx, byte ptr [edi+edx*2]
    or al, byte ptr [ebx+%(FT)#x]
    lea ebx, [edi+edx*2]
    movzx ebx, byte ptr [ebx+edx]
    or al, byte ptr [ebx+%(FT)#x]
    jnz c_top
    lea edi, [edi+edx*4]
    add ecx, 4
    cmp ecx, -4
    jle g_top
    test ecx, ecx
    jnz t_top
    mov eax, ebx
    jmp done
c_top:''' % dict(T1=T1, FT=FT)
    for k in range(4):
        if k: s += '\nc_%d:\n    add edi, edx' % k
        s += '''
    movzx eax, byte ptr [edi]
    cmp byte ptr [eax+%#x], 0
    je %s''' % (FT, 'c_%d' % (k + 1) if k < 3 else 'c_u') + _spec(T1, 4 * k)
    s += '''
    add edi, edx
    add ecx, 4
    cmp ecx, -4
    jle c_top
    test ecx, ecx
    jnz t_top
    xor eax, eax
    jmp done
c_u:
    add edi, edx
    add ecx, 4
    cmp ecx, -4
    jle g_top
    test ecx, ecx
    jz done
t_top:
    movzx eax, byte ptr [edi]
    cmp byte ptr [eax+%(FT)#x], 0
    jne t_spec
    add edi, edx
    inc ecx
    jnz t_top
    jmp done
t_spec:''' % dict(FT=FT) + _spec(T1, 0) + '''
    add edi, edx
    inc ecx
    jnz t_top
    xor eax, eax
done:
    sub esi, edi
    add esi, edx
    lea ebx, [ebp-4]
    add ebx, 4
    inc ecx
    dec ecx
    pop ebp
    ret
cp_old:''' + SHIPPED_BODY
    assert not re.search(r'L[0-9a-f]+', s) and '@' not in s     # one keystone part for ir_fix.layout / shifted
    return s


# the caller window, EN and FR alike (operands checked; T1 / FT / CP_ROWS / call target extracted)
_CALLER_SHAPE = [
    ('mov', r'edx, (0x[0-9a-f]+)'),                 # CP_ROWS
    ('mov', r'edi, (0x[0-9a-f]+)'),                 # T1
    ('mov', r'ecx, dword ptr \[esp \+ 0x18\]'),
    ('lea', r'eax, \[ebp\*4\]'),
    ('mov', r'dword ptr \[esp \+ 4\], edi'),        # [esp+4] = T1
    ('add', r'edx, eax'),
    ('sub', r'ecx, ebp'),
    ('mov', r'eax, (0x[0-9a-f]+)'),                 # FT
    ('mov', r'dword ptr \[esp\], edx'),             # [esp] = CP_ROWS + 4*row
    ('mov', r'edi, ebx'),
    ('mov', r'edx, dword ptr \[esp \+ 0x10\]'),
    ('mov', r'ebx, esp'),                           # ebx = the 3-dword block
    ('mov', r'dword ptr \[esp \+ 8\], eax'),        # [esp+8] = FT
    ('call', r'(0x[0-9a-f]+)'),                     # the colpass
]


def caller_constants(read, fr):
    """T1, FT, CP_ROWS from the only caller's immediates; asserts the data flow into [ebx+4] / [ebx+8] and the call"""
    a = CP_CALLER[fr]
    ins = list(Cs(CS_ARCH_X86, CS_MODE_32).disasm(read(a, 0x40), a))[:len(_CALLER_SHAPE)]
    got = []
    for i, (mn, op) in zip(ins, _CALLER_SHAPE):
        m = re.fullmatch(op, i.op_str)
        assert i.mnemonic == mn and m, ('caller shape', CP_LANG[fr], hex(i.address), i.mnemonic, i.op_str)
        got += [int(g, 16) for g in m.groups()]
    rows, t1, ft, target = got
    assert target == CP_ENTRY[fr], ('call target', hex(target))
    assert ins[-1].address == {False: 0x2A87E7, True: 0x2A87D7}[fr]
    return t1, ft, rows


def recipe(fr, read=None):
    """RECIPES['colpass'] tuple for that exe (FR: FR addresses and FR constants). With read: constants from the caller
    (and they must be the known ones: the guard and the tests are written for them)."""
    T1, FT = CP_CONST[fr]
    if read is not None:
        t1, ft, rows = caller_constants(read, fr)
        assert (t1, ft, rows) == (T1, FT, CP_ROWS[fr]), ('unexpected caller constants', CP_LANG[fr], hex(t1), hex(ft), hex(rows))
    low, high = CP_ENTRY[fr], CP_HIGH[fr]
    return (low, high, None, {low + 6: (high, colpass_text(T1, FT))})



RECIPES['colpass'] = recipe(False)


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
    for lang, read in (('EN', en_read), ('FR', fr_read)):
        if read is None: continue
        pieces_all, copied_all, total, entry, site, stub = [], b'', 0, {}, {}, {}
        for name in ORDER:
            d = FR_DELTA[name] if lang == 'FR' else 0
            rec = recipe(lang == 'FR', read) if name == 'colpass' else \
                (RECIPES[name] if d == 0 else F.shifted(RECIPES[name], d))
            if d and name != 'colpass': F.same_shape(en_read, fr_read, RECIPES[name][0], RECIPES[name][1], d, RECIPES[name][3])
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
        assert XOFF + total <= 0x1000       # the affine block (affine_fix.py) starts at +0x1000
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
