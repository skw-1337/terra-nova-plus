# Normal-view affine textured polygons (the renderer behind the table dword EN 0x3663CC = 0x32F610, FR 0x366478 =
# 0x32F890) rebuilt as one routine in the HD code's buffer (XCODE), after the performance study of 2026-10-16.
# The game's module writes 16 operands into its own code per polygon, 5 per edge and 4 per line, and its pixel loop
# keeps a flag live across memory accesses (DOSBox saves and reloads the flags, 2.5 times per pixel) and switches
# between two paths on every v carry (1.5 translated blocks per pixel). The new pixel loop is branch-free (the carry
# picks step or step+width with sbb/and), one block per pixel; edge and line state live in a larger stack frame
# (one code write per line), the per-polygon constants stay operands DOSBox reads from memory. The gradient routine
# (EN 0x34C270) and the scan driver (0x34C490) are copied unchanged from the player's memory (pieces C, SHA-256
# checked) so that their calls and returns become direct jumps; only their 3 indirect call sites and 2 rets differ.
# Same arithmetic, divide-overflow fix-ups included: Unicorn, thousands of polygons EN and FR (mixed, degenerate,
# full screen), framebuffer, registers and every other memory write identical. Installed by swapping the table
# dword (data) once the block is written, so it can go in at any time.
#   python affine_fix.py EN_IMAGE FR_IMAGE          sizes and checks
#   python affine_fix.py EN_IMAGE FR_IMAGE cs       C# table (src/TNPlusAffine.cs)
import sys, struct, re, hashlib
from keystone import Ks, KS_ARCH_X86, KS_MODE_32
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

BASE = 0x1AB000
DELTA = 0x123457
ks = Ks(KS_ARCH_X86, KS_MODE_32)
md = Cs(CS_ARCH_X86, CS_MODE_32)
GRAD_LEN, DRV_LEN = 0x21E, 0x1A1
IMAGES = {}


def mem(lang):
    return IMAGES[lang]


def asm(text, addr):
    enc, _ = ks.asm(text, addr)
    return bytes(enc)


CONST = {
    'en': dict(RECIP=0x45f71c, RT=0x44b1a0, CM=0x35f8bc, GRAD=0x34c270, DRV=0x34c490, TABLE=0x3663cc, ORIG=0x32f610),
    'fr': dict(RECIP=0x45f7cc, RT=0x44b250, CM=0x35f964, GRAD=0x34c4f0, DRV=0x34c710, TABLE=0x366478, ORIG=0x32f890),
}
MARK = {  # placeholder immediates used to locate the slots after assembly
    'DVF': 0x5EAD0005, 'W': 0x5EAD0006, 'MASK': 0x5EAD0001, 'TEX': 0x5EAD0002, 'DUF': 0x5EAD0003,
    'DSTEP': 0x5EAD0004, 'DSHADE': 0x5EAD0007, 'CMAP': 0x5EAD0008,
}

def src(c, S, guard=False):
    """S = slot addresses (dict) ; c = constants ; guard: skip slot writes whose value is unchanged (P4)"""
    if not guard:
        TEXBLOCK = f"""    mov  byte ptr [{S['LW']:#x}], cl
    dec  ebp
    mov  dword ptr [{S['MASK']:#x}], ebp
    mov  ebp, dword ptr [eax]
    mov  dword ptr [{S['TEX']:#x}], ebp
"""
        CMBLOCK = f"""    mov  edx, 1
    shl  edx, cl
    mov  dword ptr [{S['W']:#x}], edx
    mov  edx, dword ptr [{c['CM']:#x}]
    mov  eax, esi
    mov  edx, dword ptr [edx+0x20]
    mov  dword ptr [{S['CMAP']:#x}], edx
"""
    else:
        TEXBLOCK = f"""    dec  ebp
    mov  edx, dword ptr [eax]
    cmp  edx, dword ptr [{S['TEX']:#x}]
    jne  tex_upd
    cmp  ebp, dword ptr [{S['MASK']:#x}]
    jne  tex_upd
    cmp  cl, byte ptr [{S['LW']:#x}]
    je   tex_ok
tex_upd:
    mov  byte ptr [{S['LW']:#x}], cl
    mov  dword ptr [{S['MASK']:#x}], ebp
    mov  dword ptr [{S['TEX']:#x}], edx
    mov  ebp, 1
    shl  ebp, cl
    mov  dword ptr [{S['W']:#x}], ebp
tex_ok:
"""
        CMBLOCK = f"""    mov  edx, dword ptr [{c['CM']:#x}]
    mov  eax, esi
    mov  edx, dword ptr [edx+0x20]
    cmp  edx, dword ptr [{S['CMAP']:#x}]
    je   cm_ok
    mov  dword ptr [{S['CMAP']:#x}], edx
cm_ok:
"""
    return f"""
nsetup:
    push ebp
    push esi
    push edi
    push ecx
    sub  esp, 0xb4
    mov  esi, esp
    mov  dword ptr [esi+0x80], ncb_r
    mov  dword ptr [esi+0x84], ncb_l
    mov  dword ptr [esi+0x7c], nrow
    mov  ebp, 1
    mov  cl, byte ptr [eax+0xf]
    shl  ebp, cl
    mov  cl, byte ptr [eax+0xe]
    mov  byte ptr [esi+0x16], cl
    shl  ebp, cl
{TEXBLOCK}    mov  edx, dword ptr [{c['RT']:#x}]
    movzx eax, word ptr [edx+0xc]
    mov  dword ptr [esi+0xac], eax
    mov  eax, dword ptr [edx]
    mov  dword ptr [esi+0xb0], eax
    mov  eax, esi
    mov  edx, ebx
    call {c['GRAD']:#x}
    mov  eax, dword ptr [esi+0x34]
    mov  edx, dword ptr [esi+0x38]
    mov  ebp, eax
    shl  ebp, 16
    mov  dword ptr [{S['DUF']:#x}], ebp
    mov  ebp, edx
    shl  ebp, 16
    mov  dword ptr [{S['DVF']:#x}], ebp
    sar  eax, 16
    sar  edx, 16
    shl  edx, cl
    add  eax, edx
    mov  dword ptr [{S['DSTEP']:#x}], eax
{CMBLOCK}    mov  edx, 3
    call {c['DRV']:#x}
    add  esp, 0xb4
    pop  ecx
    pop  edi
    pop  esi
    pop  ebp
    ret

ncb_l:
    pushal
    mov  edi, dword ptr [ebx]
    mov  ebx, eax
    mov  esi, dword ptr [edx]
    push esi
    mov  ecx, dword ptr [esi+4]
    mov  eax, ecx
    mov  ebp, dword ptr [edi+4]
    add  eax, 0xffff
    sar  eax, 16
    mov  dword ptr [ebx+0x44], eax
    sub  ecx, ebp
    mov  eax, dword ptr [esi]
    mov  esi, dword ptr [edi]
    mov  word ptr [ebx+0xa], si
    sub  eax, esi
    cdq
    mov  dword ptr [ebx], edi
    mov  edi, edx
    mov  edx, eax
    sar  edx, 16
    shl  eax, 16
    idiv ecx
    lea  edx, [eax+0x80000001]
    cmp  edx, 2
    sbb  edx, edx
    not  edx
    and  edi, edx
    add  eax, edi
    mov  edi, eax
    shl  eax, 16
    mov  dword ptr [ebx+0x88], eax
    mov  eax, edi
    sar  eax, 16
    mov  edx, dword ptr [ebx+0xac]
    add  eax, edx
    mov  dword ptr [ebx+0x8c], eax
    mov  eax, ebp
    add  eax, 0xffff
    sar  eax, 16
    imul edx
    add  eax, dword ptr [ebx+0xb0]
    xchg edi, eax
    neg  ebp
    and  ebp, 0xffff
    imul ebp
    shrd eax, edx, 16
    add  eax, 0xffff
    add  esi, eax
    mov  word ptr [ebx+0xa], si
    sar  esi, 16
    add  esi, edi
    dec  esi
    mov  dword ptr [ebx+0xa8], esi
    pop  esi
    mov  edi, dword ptr [ebx]
    mov  eax, dword ptr [esi+0x14]
    mov  edi, dword ptr [edi+0x14]
    sub  eax, edi
    mov  edx, eax
    sar  edx, 16
    shl  eax, 16
    idiv ecx
    mov  dword ptr [ebx+0x90], eax
    imul ebp
    shrd eax, edx, 16
    add  eax, edi
    mov  dword ptr [ebx+0x50], eax
    popal
    ret

ncb_r:
    pushal
    mov  edi, dword ptr [ebx]
    mov  ebx, eax
    mov  esi, dword ptr [edx]
    push esi
    mov  ecx, dword ptr [esi+4]
    mov  eax, ecx
    mov  ebp, dword ptr [edi+4]
    add  eax, 0xffff
    sar  eax, 16
    mov  dword ptr [ebx+0x20], eax
    sub  ecx, ebp
    mov  eax, dword ptr [esi]
    mov  esi, dword ptr [edi]
    mov  word ptr [ebx+8], si
    sub  eax, esi
    cdq
    mov  dword ptr [ebx], edi
    mov  edi, edx
    mov  edx, eax
    sar  edx, 16
    shl  eax, 16
    idiv ecx
    lea  edx, [eax+0x80000001]
    cmp  edx, 2
    sbb  edx, edx
    not  edx
    and  edi, edx
    add  eax, edi
    mov  edi, eax
    mov  word ptr [ebx+0x94], ax
    sar  eax, 16
    mov  edx, dword ptr [ebx+0xac]
    mov  dword ptr [ebx+0x3c], eax
    add  eax, edx
    mov  dword ptr [ebx+0x98], eax
    mov  eax, ebp
    add  eax, 0xffff
    sar  eax, 16
    imul edx
    add  eax, dword ptr [ebx+0xb0]
    xchg edi, eax
    neg  ebp
    and  ebp, 0xffff
    imul ebp
    shrd eax, edx, 16
    add  eax, 0xffff
    add  esi, eax
    mov  word ptr [ebx+8], si
    mov  edx, esi
    sar  esi, 16
    add  esi, edi
    dec  esi
    mov  dword ptr [ebx+0x1c], esi
    pop  esi
    push edx
    mov  edi, dword ptr [ebx]
    mov  eax, dword ptr [esi+0x14]
    mov  edi, dword ptr [edi+0x14]
    sub  eax, edi
    mov  edx, eax
    sar  edx, 16
    shl  eax, 16
    idiv ecx
    mov  dword ptr [ebx+0x9c], eax
    imul ebp
    shrd eax, edx, 16
    add  eax, edi
    mov  dword ptr [ebx+0x2c], eax
    pop  edx
    mov  edi, dword ptr [ebx]
    mov  esi, dword ptr [edi]
    and  edx, 0xffff0000
    sub  edx, esi
    mov  esi, edx
    mov  eax, dword ptr [ebx+0x34]
    imul esi
    shrd eax, edx, 16
    mov  ecx, eax
    mov  eax, dword ptr [ebx+0x58]
    imul ebp
    shrd eax, edx, 16
    add  eax, ecx
    add  eax, dword ptr [edi+8]
    mov  dword ptr [ebx+0x24], eax
    mov  eax, dword ptr [ebx+0x38]
    imul esi
    shrd eax, edx, 16
    mov  ecx, eax
    mov  eax, dword ptr [ebx+0x5c]
    imul ebp
    shrd eax, edx, 16
    add  eax, ecx
    add  eax, dword ptr [edi+0xc]
    mov  dword ptr [ebx+0x28], eax
    mov  ecx, dword ptr [ebx+0x3c]
    mov  eax, dword ptr [ebx+0x34]
    imul ecx
    add  eax, dword ptr [ebx+0x58]
    mov  dword ptr [ebx+0xa0], eax
    mov  eax, dword ptr [ebx+0x38]
    imul ecx
    add  eax, dword ptr [ebx+0x5c]
    mov  dword ptr [ebx+0xa4], eax
    popal
    ret

nrow:
    pushal
    mov  edx, eax
    mov  ebx, dword ptr [edx+0xa8]
line:
    mov  eax, dword ptr [edx+0x1c]
    sub  ebx, eax
    lea  ecx, [ebx-1]
    cmp  ecx, 0x3ff
    jae  special
    inc  eax
    push eax
    mov  ebp, dword ptr [ebx*4+{c['RECIP']:#x}]
    mov  esi, dword ptr [edx+0x28]
    mov  edi, dword ptr [edx+0x24]
    mov  eax, dword ptr [edx+0x50]
    mov  ecx, dword ptr [edx+0x2c]
    sub  eax, ecx
    ror  ecx, 16
    imul ebp
    mov  dword ptr [{S['DSHADE']:#x}], eax
    mov  ch, dl
    mov  eax, ecx
    mov  edx, ecx
    and  edx, 0xffff
    mov  ecx, esi
    shl  ecx, 16
    mov  cl, dh
    sar  esi, 16
    mov  ebp, edi
    shl  esi, 0x1f
    sar  ebp, 16
    add  esi, ebp
    mov  dh, dl
    shl  edi, 16
    sub  di, bx
    pop  ebx
    jmp  pix
pix:
    add  ecx, {MARK['DVF']:#x}
    sbb  ebp, ebp
    and  ebp, {MARK['W']:#x}
    and  esi, {MARK['MASK']:#x}
    mov  dl, byte ptr [esi+{MARK['TEX']:#x}]
    add  esi, ebp
    add  edi, {MARK['DUF']:#x}
    adc  esi, {MARK['DSTEP']:#x}
    add  eax, {MARK['DSHADE']:#x}
    adc  dh, cl
    mov  ch, byte ptr [edx+{MARK['CMAP']:#x}]
    mov  byte ptr [ebx], ch
    lea  ebx, [ebx+1]
    add  di, 1
    jnz  pix
epi:
    mov  edx, dword ptr [esp+0x1c]
    mov  ebx, dword ptr [edx+0xa8]
    mov  ecx, dword ptr [edx+0x8c]
    mov  eax, dword ptr [edx+8]
    mov  esi, dword ptr [edx+0x88]
    mov  edi, dword ptr [edx+0x94]
    add  eax, esi
    adc  ebx, ecx
    add  ax, di
    sbb  ebp, ebp
    mov  ecx, dword ptr [edx+0x1c]
    add  ecx, dword ptr [edx+0x98]
    sub  ecx, ebp
    mov  dword ptr [edx+8], eax
    mov  dword ptr [edx+0xa8], ebx
    mov  dword ptr [edx+0x1c], ecx
    mov  eax, dword ptr [edx+0x50]
    mov  ecx, dword ptr [edx+0x2c]
    add  ecx, dword ptr [edx+0x9c]
    add  eax, dword ptr [edx+0x90]
    mov  dword ptr [edx+0x2c], ecx
    mov  dword ptr [edx+0x50], eax
    mov  eax, dword ptr [edx+0x34]
    mov  edi, dword ptr [edx+0x24]
    and  eax, ebp
    add  edi, eax
    add  edi, dword ptr [edx+0xa0]
    mov  dword ptr [edx+0x24], edi
    mov  esi, dword ptr [edx+0x28]
    and  ebp, dword ptr [edx+0x38]
    add  esi, dword ptr [edx+0xa4]
    add  esi, ebp
    mov  dword ptr [edx+0x28], esi
    mov  eax, dword ptr [edx]
    dec  eax
    mov  dword ptr [edx], eax
    test eax, eax
    jnz  line
    popal
    xor  eax, eax
    ret
special:
    test ebx, ebx
    jz   epi
    popal
    mov  eax, 1
    ret
"""



def routine(text, name, nxt):
    """extract the body of label `name` from affine_new.src text up to label `nxt`"""
    a = text.index('\n' + name + ':')
    b = text.index('\n' + nxt + ':') if nxt else len(text)
    return text[a:b]



def find_slots(code, base):
    slots = {}
    for ins in md.disasm(code, base):
        b = bytes(ins.bytes)
        for k, v in MARK.items():
            p = b.find(struct.pack('<I', v))
            if p > 0: slots[k] = ins.address + p
        if ins.mnemonic == 'shl' and ins.op_str == 'esi, 0x1f': slots['LW'] = ins.address + 2
    return slots


def build_f15(lang, base):
    c = CONST[lang]
    m = mem(lang)
    rd = lambda a, n: m[a - BASE:a - BASE + n]
    g0, d0 = c['GRAD'], c['DRV']
    # check the driver's call sites (same in EN/FR)
    assert rd(d0 + 0xF9, 6) == bytes.fromhex('ff9580000000') and rd(d0 + 0x163, 6) == bytes.fromhex('ff9584000000') \
        and rd(d0 + 0x182, 3) == bytes.fromhex('ff557c') and rd(d0 + 0x1A0, 1) == b'\xc3' and rd(g0 + 0x21D, 1) == b'\xc3'
    S = {k: 0x7F000000 + i * 0x10 for i, k in enumerate(list(MARK) + ['LW'])}
    for _ in range(6):
        t = src(c, S)
        setup = routine(t, 'nsetup', 'ncb_l')
        setup = re.sub(r'\n    mov  dword ptr \[esi\+0x(80|84|7c)\], n\w+', '', setup)
        sa, rest = setup.split('    call %#x' % c['GRAD'])
        sb, sc = rest.split('    call %#x' % c['DRV'])
        cbl = routine(t, 'ncb_l', 'ncb_r'); cbr = routine(t, 'ncb_r', 'nrow'); row = routine(t, 'nrow', None)
        # layout (fixed point on addresses)
        L = {'setup_a': base}
        for _k in range(8):
            parts = []
            a = base

            def put(name, data):
                nonlocal a
                L[name] = a; parts.append((name, data)); a += len(data)
            put('setup_a', asm(sa + '\n    jmp %#x' % L.get('g_entry', base + 0x1000), a))
            put('setup_b', asm(sb + '\n    jmp %#x' % L.get('d_entry', base + 0x1000), a))
            put('setup_c', asm(sc, a))
            # gradient copy
            L['g_entry'] = a
            parts.append(('g_copy', ('C', g0, GRAD_LEN - 1))); a += GRAD_LEN - 1
            put('g_ret', b'\xe9' + struct.pack('<i', L['setup_b'] - (a + 5)))
            # driver copy
            L['d_entry'] = a
            parts.append(('d_copy0', ('C', d0, 0xF9))); a += 0xF9
            put('d_site_r', b'\xe9' + struct.pack('<i', L.get('ncb_r', a) - (a + 5)) + b'\x90')
            parts.append(('d_copy1', ('C', d0 + 0xFF, 0x64))); a += 0x64
            put('d_site_l', b'\xe9' + struct.pack('<i', L.get('ncb_l', a) - (a + 5)) + b'\x90')
            parts.append(('d_copy2', ('C', d0 + 0x169, 0x19))); a += 0x19
            site_row = a
            tramp = a + 3 + 0x1B + 5
            put('d_site_row', b'\xeb' + struct.pack('<b', tramp - (a + 2)) + b'\x90')
            parts.append(('d_copy3', ('C', d0 + 0x185, 0x1B))); a += 0x1B
            put('d_ret', b'\xe9' + struct.pack('<i', L['setup_c'] - (a + 5)))
            assert a == tramp
            put('d_tramp', b'\xe9' + struct.pack('<i', L.get('nrow', a) - (a + 5)))
            cont_r, cont_l, cont_row = L['d_entry'] + 0xFF, L['d_entry'] + 0x169, L['d_entry'] + 0x185
            put('ncb_l', asm(cbl.replace('    ret', '    jmp %#x' % cont_l), a))
            put('ncb_r', asm(cbr.replace('    ret', '    jmp %#x' % cont_r), a))
            put('nrow', asm(row.replace('    ret', '    jmp %#x' % cont_row), a))
            sizes = [(n, (d if isinstance(d, bytes) else d)) for n, d in parts]
            key = [(n, len(d) if isinstance(d, bytes) else d[2]) for n, d in parts]
            if _k and key == prev_key and all(L[n] == prev_L.get(n) for n in L):
                break
            prev_key, prev_L = key, dict(L)
        code = b''.join(d if isinstance(d, bytes) else rd(d[1], d[2]) for n, d in parts)
        s2 = find_slots(code[L['nrow'] - base:], L['nrow'])
        if s2 == S:
            break
        S = s2
    assert set(MARK) | {'LW'} <= set(S), S
    pieces = []
    for n, d in parts:
        if isinstance(d, bytes):
            if pieces and pieces[-1][0] == 'B': pieces[-1] = ('B', pieces[-1][1] + d)
            else: pieces.append(('B', d))
        else:
            pieces.append(('C', d[1], d[2]))
    labels = dict(L); labels['pix'] = S['DVF'] - 2; labels['epi'] = S['DVF'] - 2 + 0x40
    return dict(code=code, labels=labels, slots=S, pieces=pieces)


def relocs(kind, lang):
    f = build_f15
    r0, r1 = f(lang, 0x400000), f(lang, 0x400000 + DELTA)
    a, b = r0['code'], r1['code']
    assert len(a) == len(b)
    absr, relr = [], []
    i = 0
    while i < len(a):
        if a[i] != b[i]:
            va, vb = struct.unpack_from('<I', a, i)[0], struct.unpack_from('<I', b, i)[0]
            dlt = (vb - va) & 0xffffffff
            if dlt == DELTA: absr.append(i)
            elif dlt == (-DELTA) & 0xffffffff: relr.append(i)
            else: raise ValueError('reloc at %#x: %x %x' % (i, va, vb))
            i += 4
        else:
            i += 1
    return absr, relr


def blob_base0(kind, lang):
    """code assembled at base 0x400000, rebased to 0 (abs relocs hold offsets, rel32-to-game hold target-offset)"""
    f = build_f15
    r = f(lang, 0x400000)
    absr, relr = relocs(kind, lang)
    code = bytearray(r['code'])
    for o in absr: struct.pack_into('<I', code, o, (struct.unpack_from('<I', code, o)[0] - 0x400000) & 0xffffffff)
    for o in relr: struct.pack_into('<I', code, o, (struct.unpack_from('<I', code, o)[0] + 0x400000) & 0xffffffff)
    # pieces re-cut on the rebased code
    pieces, off = [], 0
    for p in r['pieces']:
        if p[0] == 'C':
            pieces.append(('C', p[1], p[2])); off += p[2]
        else:
            pieces.append(('B', bytes(code[off:off + len(p[1])]))); off += len(p[1])
    assert off == len(code)
    rel = lambda d: {k: (v - 0x400000) for k, v in d.items() if v is not None}
    return dict(code=bytes(code), pieces=pieces, abs=absr, rel=relr, labels=rel(r['labels']), slots=rel(r['slots']))


def install(b, P):
    code = bytearray(b['code'])
    for o in b['abs']: struct.pack_into('<I', code, o, (struct.unpack_from('<I', code, o)[0] + P) & 0xffffffff)
    for o in b['rel']: struct.pack_into('<I', code, o, (struct.unpack_from('<I', code, o)[0] - P) & 0xffffffff)
    return bytes(code)



def export_cs(out):
    print('// Generated by tools/affine_fix.py (python affine_fix.py EN_IMAGE FR_IMAGE cs > ../src/TNPlusAffine.cs): do not edit.')
    print('// Normal-view affine polygons rebuilt in XCODE: pieces C:address:length are copied from the game\'s own memory,')
    print('// B: pieces are ours; Abs = offsets of dwords to which the block address is added. See tools/affine_fix.py.')
    print('static class AffineFix\n{')
    q = lambda xs: ', '.join(xs)
    L = ('en', 'fr')
    print('    public const int Off = 0x1000;                 // from the page-aligned XCODE base: a page of its own')
    print('    public static readonly uint[] Table = { %s }, Old = { %s };' % (q('0x%X' % CONST[l]['TABLE'] for l in L), q('0x%X' % CONST[l]['ORIG'] for l in L)))
    print('    public static readonly int[] Size = { %s }, Entry = { 0, 0 };' % q(str(len(out[l]['code'])) for l in L))
    print('    public static readonly string[] Sha = { %s };' % q('"%s"' % out[l]['sha'] for l in L))
    print('    public static readonly int[][] Abs = { %s };' % q('new int[] { %s }' % q(str(o) for o in out[l]['abs']) for l in L))
    print('    public static readonly string[][] Pieces = {')
    for l in L:
        ps = ['"C:%X:%X"' % (p[1], p[2]) if p[0] == 'C' else '"B:%s"' % p[1].hex().upper() for p in out[l]['pieces']]
        print('        new string[] {')
        for k in range(0, len(ps), 4): print('            ' + ', '.join(ps[k:k + 4]) + ',')
        print('        },')
    print('    };\n}')


if __name__ == '__main__':
    IMAGES['en'] = open(sys.argv[1], 'rb').read()
    IMAGES['fr'] = open(sys.argv[2], 'rb').read()
    out = {}
    for lang in ('en', 'fr'):
        b = blob_base0('f15', lang)
        assert not b['rel'], 'rel32 into the game'
        m = mem(lang)
        copied = b''.join(m[p[1] - BASE:p[1] - BASE + p[2]] for p in b['pieces'] if p[0] == 'C')
        code = b''.join(m[p[1] - BASE:p[1] - BASE + p[2]] if p[0] == 'C' else p[1] for p in b['pieces'])
        assert code == b['code']
        assert m[CONST[lang]['TABLE'] - BASE:CONST[lang]['TABLE'] - BASE + 4] == struct.pack('<I', CONST[lang]['ORIG'])
        out[lang] = dict(b, sha=hashlib.sha256(copied).hexdigest())
    if 'cs' in sys.argv: export_cs(out)
    else:
        for lang in ('en', 'fr'):
            o = out[lang]
            print(lang, '%d bytes, %d copied, %d relocations, sha %s' % (len(o['code']), sum(p[2] for p in o['pieces'] if p[0] == 'C'),
                  len(o['abs']), o['sha'][:16]))
