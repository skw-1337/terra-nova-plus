# Normal view: the two per-column routines of the 3D library, called for each polygon edge (from 0x2FB220), replaced
# by our own in the HD code's buffer: 0x317DF0 tests an edge against a per-column buffer, 0x317EA0 updates it. The
# game's versions write operands INTO their own loops at every call (the buffer's end, the return value, a jump
# target), which DOSBox's dynamic core translates again and again; the first fix (1.2) kept their code and read those
# slots as data. Since 2026-11-16 both are written
# anew (see below): no slot, 4 columns per translated block.
# Not done: the textured span loop (EN 0x34D390) is fixed in place (spans_t14.py).
# French exe: both routines +0x280, checked instruction by instruction.
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

# ---------------------------------------------------------------- the two per-column routines, rewritten (2026-11-16)
# A host-side profile of DOSBox's emulation thread showed what these cost: a translated block costs about as much as
# 11 plain instructions, the loops ran 2 blocks per column, pushal / popal go through a function for each register,
# and the operand slots kept in the old routines sit in a code page (slow writes). New bodies, same set-up arithmetic
# (the DDA is the game's: fraction in the high half of ebx, integer in esi), same results:
#  - registers saved in a stack frame with plain moves, the buffer's end and the result in that frame (no slot);
#  - columns 4 per block, without a branch inside: 'buffer > y' is counted with the carry of an unsigned compare of
#    the two values with their sign bit flipped (exact for any 32-bit values), and 'no column counted yet' is folded
#    into the loop test (cmp ebp,1 / sbb / and LIMIT / cmp / jb): one conditional jump per 4 columns, still stopping
#    at the first group that has one;
#  - column test (0x317DF0): 1 if the edge is above the buffer in one of its columns. The game stops at the first
#    one; it writes nothing, so looking at the rest of a group changes nothing;
#  - column update (0x317EA0): the game's two loops (search: write where buffer > y, then run: write while
#    buffer >= y) leave buffer = min(buffer, y) in every column of the edge (where buffer == y the value written is
#    the one already there) and return 1 if a column was strictly above. Columns are only tested until one is; from
#    that group (or single column) to the end of the edge the minimum is written without a branch.
# Checked in Unicorn against the game's routines, EN and FR: registers, returned value and buffer identical over
# thousands of edges (buffers of screen values, extremes, values next to the edge; empty and one-column edges).
_B = '0x80000000'
_SAVE = """
    lea esp, [esp-40]
    mov dword ptr [esp+0], ebx
    mov dword ptr [esp+4], ecx
    mov dword ptr [esp+8], edx
    mov dword ptr [esp+12], esi
    mov dword ptr [esp+16], edi
    mov dword ptr [esp+20], ebp
    xor ecx, ecx
    mov dword ptr [esp+32], ecx
    mov edi, dword ptr [eax]
    mov esi, dword ptr [edx]"""
_SETUP = """
    mov ecx, dword ptr [edi]
    mov ebp, dword ptr [esi]
    mov edx, ecx
    add edx, 0xffff
    sar edx, 0x10
    lea ebx, [ebx+edx*4]
    mov dword ptr [esp+24], ebx
    sub ecx, ebp
    mov ebx, ebp
    neg ebp
    and ebp, 0xffff
    add ebx, 0xffff
    sar ebx, 0x10
    sub edx, ebx
    jle {p}_out
    mov esi, dword ptr [esi+4]
    mov eax, dword ptr [edi+4]
    mov edi, edx
    shl edi, 2
    neg edi
    sub eax, esi
    je {p}_nodiv
    cdq
    mov ebx, edx
    inc ebx
    mov edx, eax
    sar edx, 0x10
    shl eax, 0x10
    idiv ecx
    add eax, ebx
    add esi, ebx
{p}_nodiv:
    mov ecx, eax
    add esi, 0xffff
    imul ebp
    shrd eax, edx, 0x10
    add esi, eax
    mov ebx, esi
    shl ebx, 0x10
    sar esi, 0x10
    mov edx, ecx
    shl ecx, 0x10
    sar edx, 0x10
    sub ebx, ecx
    sbb esi, edx
    add edi, dword ptr [esp+24]
    lea esi, [esi-%s]
    xor ebp, ebp
    mov eax, dword ptr [esp+24]
    lea eax, [eax-12]
    mov dword ptr [esp+28], eax
""" % _B
_REST = """
{p}_out:
    mov eax, dword ptr [esp+32]
    mov ebx, dword ptr [esp+0]
    mov ecx, dword ptr [esp+4]
    mov edx, dword ptr [esp+8]
    mov esi, dword ptr [esp+12]
    mov edi, dword ptr [esp+16]
    mov ebp, dword ptr [esp+20]
    lea esp, [esp+40]
    ret"""


def _col_test(k):
    """one column, test only: ebp += (buffer > y)"""
    return """
    mov eax, dword ptr [edi+%d]
    lea eax, [eax-%s]
    add ebx, ecx
    adc esi, edx
    cmp esi, eax
    adc ebp, 0""" % (4 * k, _B)


def _col_min(k):
    """one column: buffer = min(buffer, y)"""
    return """
    mov eax, dword ptr [edi+%d]
    lea eax, [eax-%s]
    add ebx, ecx
    adc esi, edx
    sub eax, esi
    sbb ebp, ebp
    and eax, ebp
    lea eax, [eax+esi-%s]
    mov dword ptr [edi+%d], eax""" % (4 * k, _B, _B, 4 * k)


def _chk(lim, loop):
    """go on while edi < [esp+lim] and no column counted yet: one conditional jump"""
    return """
    cmp ebp, 1
    sbb eax, eax
    and eax, dword ptr [esp+%d]
    cmp edi, eax
    jb %s""" % (lim, loop)


COLTEST = (_SAVE + _SETUP + """
    jmp ct_gc
ct_g:""" + ''.join(_col_test(k) for k in range(4)) + """
    lea edi, [edi+16]
ct_gc:""" + _chk(28, 'ct_g') + """
    jmp ct_tc
ct_t:""" + _col_test(0) + """
    lea edi, [edi+4]
ct_tc:""" + _chk(24, 'ct_t') + """
    neg ebp
    sbb eax, eax
    neg eax
    mov dword ptr [esp+32], eax""" + _REST).format(p='ct')

COLUPDATE = (_SAVE + _SETUP + """
    jmp cu_gc
cu_g:""" + ''.join(_col_test(k) for k in range(4)) + """
    lea edi, [edi+16]
cu_gc:""" + _chk(28, 'cu_g') + """
    test ebp, ebp
    jnz cu_back4
    jmp cu_tc
cu_t:""" + _col_test(0) + """
    lea edi, [edi+4]
cu_tc:""" + _chk(24, 'cu_t') + """
    test ebp, ebp
    jz cu_out
    sub ebx, ecx
    sbb esi, edx
    lea edi, [edi-4]
    jmp cu_w
cu_back4:""" + """
    sub ebx, ecx
    sbb esi, edx""" * 4 + """
    lea edi, [edi-16]
cu_w:
    mov dword ptr [esp+32], 1
    jmp cu_wgc
cu_wg:""" + ''.join(_col_min(k) for k in range(4)) + """
    lea edi, [edi+16]
cu_wgc:
    cmp edi, dword ptr [esp+28]
    jb cu_wg
    jmp cu_wtc
cu_wt:""" + _col_min(0) + """
    lea edi, [edi+4]
cu_wtc:
    cmp edi, dword ptr [esp+24]
    jb cu_wt""" + _REST).format(p='cu')

# What the game's two routines must be for the new ones to stand in for them: every instruction outside the operand
# slots (the instructions the game rewrites: their bytes differ from call to call), read from the player's memory and
# hashed with the copied pieces (V: pieces, verified only; nothing of them goes into the new block).
_lde = lambda a: 'mov eax, dword ptr [%#x];mov eax, dword ptr [edi+eax];' % a
_ste = lambda a: 'mov eax, dword ptr [%#x];mov dword ptr [edi+eax], esi;' % a
GUARD = {
    'coltest': (0x317DF0, 0x317E9D, None, {0x317e82: (0x317e88, _lde(0x317e84))}),
    'colupdate': (0x317EA0, 0x317FAB, None, {
        0x317f5a: (0x317f60, _ste(0x317f5c)), 0x317f74: (0x317f7a, _lde(0x317f76)), 0x317f80: (0x317f86, _ste(0x317f82)),
        0x317f94: (0x317f9a, _lde(0x317f96)), 0x317f9e: (0x317fa0, '@jg 0x317f50;'),
        0x317fa5: (0x317faa, 'mov eax, dword ptr [0x317fa6];')}),
}

RECIPES = {   # EN addresses: low, high, no digest (the game rewrites operands inside), edits {start: (stop, text)}
    'coltest': (0x317DF0, 0x317E9D, None, {0x317DF0: (0x317E9D, COLTEST)}),
    'colupdate': (0x317EA0, 0x317FAB, None, {0x317EA0: (0x317FAB, COLUPDATE)}),
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
            if d and name != 'colpass': F.same_shape(en_read, fr_read, GUARD[name][0], GUARD[name][1], d, GUARD[name][3])
            s = ENTRY[name] + d
            pieces, copied, lab, size = F.layout(read, rec, s)
            if name in GUARD:               # the game's routine: verified, not copied
                grec = GUARD[name] if d == 0 else F.shifted(GUARD[name], d)
                for p in F.layout(read, grec, s)[0]:
                    if p[0] == 'C': pieces_all.append(('V', p[1], p[2])); copied_all += read(p[1], p[2])
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
    print('// Normal view per-column routines: B: pieces are ours; V:address:length pieces are parts of the game\'s own')
    print('// routines, read from memory and hashed (verified, not copied). Routines: %s. See tools/view_fix.py.' % ', '.join(ORDER))
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
        ps = ['"%s:%X:%X"' % p if p[0] in 'CV' else '"B:%s"' % p[1].hex().upper() for p in out[l]['pieces']]
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
