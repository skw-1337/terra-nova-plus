# Speed fixes written by the game itself, from the HD 720 payload (tools/hd720.py) at its first 3D frame.
# DOSBox's dynamic core (dyn_x86, DOSBox Staging 0.82) has no translation for a few instructions (SETcc, REPE CMPSB)
# and runs them through its interpreter at every execution; the transparent-texture filler has a test opcode that the
# game itself rewrites, which ends up interpreted for every pixel; some column and row loops cost 2-3 translated
# blocks per pixel. Same-length rewrites of those spots, and two loops moved to small routines in our area.
# The game writes them (code run by the guest): DOSBox drops whatever it had translated from those bytes, which a
# write from outside would not do. Each group checks every fixed byte first (bytes the game rewrites as it runs are
# skipped), then writes all of its bytes or none, once; a group already in place is left alone.
# Found and checked by the performance study of 2026-10-16 (Unicorn: old and new code run from the same states,
# registers, flags and memory compared). Every group draws the same pixels: nothing changes on screen.
#   python speed_fix.py EN_IMAGE FR_IMAGE      checks the groups against memory images (RAM 0x1AB000-0x470000)
import struct, sys
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

ks = Ks(KS_ARCH_X86, KS_MODE_32)
asm = lambda src, at: bytes(ks.asm(src, at)[0])

# '..' = a byte the game rewrites (operand slots): not checked; in a new string, not checked once written
OLD, NEW = 1, 6     # mask bits: 1 check the old byte, 2 write the new one, 4 check it to see the group is in place


def rec(at, old, new=None):
    """one record: same-length old/new hex ('..' = any byte); new None = only check (context the rewrite relies on)"""
    old = old.replace(' ', ''); new = (new or old).replace(' ', '')
    assert len(old) == len(new) and len(old) % 2 == 0, (hex(at), old, new)
    out = []
    for k in range(0, len(old), 2):
        o, n = old[k:k + 2], new[k:k + 2]
        m = (OLD if o != '..' else 0) | (0 if n == '..' else (6 if n != o or o == '..' else 4))
        out.append((0 if o == '..' else int(o, 16), 0 if n == '..' else int(n, 16), m))
    return (at, out)


# ---------------------------------------------------------------- the transparent-texture filler (EN 0x3316A0)
# The set-up writes a dword at the end of path C's adc immediate and so bumps DOSBox's rewrite count of the next
# opcode (test dl,0xff), which then runs in the interpreter for each pixel of paths C/D. Every pixel now goes through
# paths A/B (never written by the game): 'cmp dl,MM / jae draw' (MM = 1 transparent, 0 opaque, written by the set-up
# in place of its old je/and byte), adc in its 81 /2 form (immediate read from memory once rewritten), the two
# per-line writers one byte further. FR: +0x280, writer operands 0xd9/0xf9.
def tf_groups(fr):
    d = 0x280 if fr else 0
    w1, w2 = ('d919', 'da19') if fr else ('5917', '5a17')
    w3, w4 = ('f919', 'fa19') if fr else ('7917', '7a17')
    return [
        rec(0x331742 + d, '0f8278000000eb46', '0f8228000000eb06'),
        rec(0x331758 + d, '15 ........ f6c2ff754003f5ffc3', '81d0 ........ 80fa01733f03f543'),
        rec(0x331778 + d, '15 ........ f6c2ff752003f54377c975e7e962000000', '81d0 ........ 80fa01731f03f54377c875e6eb640000'),
        rec(0x3317B1 + d, '77dd743b25ff', '779d743bebb9'),
        rec(0x3316E8 + d, '8915' + w1, '8915' + w2),
        rec(0x3316FC + d, '8915' + w3, '8915' + w4),
        rec(0x3315DF + d, 'b1ff', 'b100'),
        rec(0x3315F8 + d, 'b174', 'b101'),
        rec(0x331614 + d, '884f10884f40', '884fd0884ff0'),
    ]


# ---------------------------------------------------------------- SETcc / REPE CMPSB in hot game paths
# Same length, same results in AL and memory; registers or flags that differ are dead (overwritten or popped first).
# FR: code -0x10, data +0xB0.
P1 = [   # (name, [(en, fr, old_en, new_en, old_fr, new_fr)], context [(en, fr, bytes_en, bytes_fr)])
    ('horizon test', [(0x2B4780, 0x2B4770,
        '53c1fa1085c07d0ca1087e42003b100f9ec05bc38b1dccca430039d87c1189d88b1d087e42003b5483fc0f9ec05bc38b1d087e42003b14830f9ec05bc38d4000',
        '53c1fa1085c07d09a1087e42003b10eb218b1dccca430039d87c0e89d88b1d087e42003b5483fceb098b1d087e42003b14835bb0017e02b000c3909090909090',
        '53c1fa1085c07d0ca1b87e42003b100f9ec05bc38b1d7ccb430039d87c1189d88b1db87e42003b5483fc0f9ec05bc38b1db87e42003b14830f9ec05bc38d4000',
        '53c1fa1085c07d09a1b87e42003b10eb218b1d7ccb430039d87c0e89d88b1db87e42003b5483fceb098b1db87e42003b14835bb0017e02b000c3909090909090')],
     []),
    ('view test', [(0x2B417F, 0x2B416F,
        '85c07d0bf7db39d80f9fc05e595bc339d80f9cc05e595bc3', '85c07d0cf7db39d8b0017ff0b000ebec39d8b0017ce6ebf4',
        '85c07d0bf7db39d80f9fc05e595bc339d80f9cc05e595bc3', '85c07d0cf7db39d8b0017ff0b000ebec39d8b0017ce6ebf4')],
     [(0x2B417B, 0x2B416B, '5e595bc3', '5e595bc3')]),
    ('map cell test', [(0x2B3DBC, 0x2B3DAC,
        '000f95c025ff0000000f95c025ff000000', '0119c040e9ce0000009090909090909090',
        '000f95c025ff0000000f95c025ff000000', '0119c040e9ce0000009090909090909090')],
     [(0x2B3DB5, 0x2B3DA5, '80bc031c5e3c', '80bc03cc5e3c')]),
    ('map cell key', [(0x2B3E27, 0x2B3E17,      # a mismatch still ends in the original repe cmpsb: exact registers
        'b90400000081c610df3b0031c0f3a6740519c083d8ff85c0750866a1c25d3c00eb15',
        '8b8610df3b003b0512cc430075090fb705c25d3c00eb206a045981c610df3b00f3a6',
        'b90400000081c6c0df3b0031c0f3a6740519c083d8ff85c0750866a1725e3c00eb15',
        '8b86c0df3b003b05c2cc430075090fb705725e3c00eb206a045981c6c0df3b00f3a6')],
     [(0x2B3E1F, 0x2B3E0F, 'bf12cc4300', 'bfc2cc4300')]),
    ('hash chain', [(0x295D36, 0x295D26,
        'b90400000081c610df3b0031c0f3a6740519c083d8ff85c075be', '8b8610df3b003b0775ceeb0e9090909090909090909090909090',
        'b90400000081c6c0df3b0031c0f3a6740519c083d8ff85c075be', '8b86c0df3b003b0775ceeb0e9090909090909090909090909090')],
     []),
    ('terrain cell', [
        (0x2B1925, 0x2B1915, '8b1424886e1c85d20f9dc0e966010000', '8b0424886e1cf7d0c1e81fe966010000',
                             '8b1424886e1c85d20f9dc0e966010000', '8b0424886e1cf7d0c1e81fe966010000'),
        (0x2B1A85, 0x2B1A75, 'a176eb410080cb048b157aeb4100885e1c39d00f9ec0', '8b0576eb4100804e1c043b057aeb4100b0017e02b000',
                             'a126ec410080cb048b152aec4100885e1c39d00f9ec0', '8b0526ec4100804e1c043b052aec4100b0017e02b000'),
        (0x2B199C, 0x2B198C, 'e9f7000000', 'e9f4000000', 'e9f7000000', 'e9f4000000')],
     [(0x2B1A82, 0x2B1A72, '8a5e1c', '8a5e1c'), (0x2B199A, 0x2B198A, '85d2', '85d2')]),
]

# ---------------------------------------------------------------- sky / haze pass (EN 0x2A8658, once per frame)
# Its two column loops replace "flagged" colours (byte table FLAGS) with the haze colour: 3 blocks / 10 instructions
# per pixel. A 256-byte table T[v] = flag[v] ? colour : v is built at each call (the game changes one flag just
# before), then each loop is 'movzx / mov dl,[edx+T] / mov [eax],dl / add / dec / jnz': every pixel gets the same
# byte (unflagged ones their own). The entry jumps to our code, which pushes like the original and builds T.
SKY = dict(en=dict(hook=0x2A8658, resume=0x2A865E, la=0x2A8701, lb=0x2A8749, out=0x2A876D, flags=0x424980, col=0x3CE99F),
           fr=dict(hook=0x2A8648, resume=0x2A864E, la=0x2A86F1, lb=0x2A8739, out=0x2A875D, flags=0x424A30, col=0x3CEA4F))
SKY_OLD_A = '8b74242c31d28a1080ba..........7408 8a93bf0000008810 8b4c2410 45 01c8 39f5 7d48 ebde'
SKY_OLD_B = '8b7c241031d28a1080ba..........7408 8a93bf0000008810 8b54240c 45 01f8 39d5 7ce0'


def sky_cave(L, T):
    return '''
    push ebx
    push ecx
    push edx
    push esi
    push edi
    push ebp
    xor ecx, ecx
sk_tb:
    mov edx, ecx
    mov al, byte ptr [ecx + %(F)#x]
    neg al
    sbb al, al
    mov ah, al
    and al, byte ptr [%(C)#x]
    not ah
    and ah, dl
    or al, ah
    mov byte ptr [ecx + %(T)#x], al
    inc ecx
    cmp ecx, 256
    jb sk_tb
    jmp %(R)#x
''' % dict(F=L['flags'], C=L['col'], T=T, R=L['resume'])


def sky_loops(L, T):
    a = asm('''
    mov esi, dword ptr [esp + 0x2c]
    mov ecx, dword ptr [esp + 0x10]
    sub esi, ebp
la: movzx edx, byte ptr [eax]
    mov dl, byte ptr [edx + %#x]
    mov byte ptr [eax], dl
    add eax, ecx
    dec esi
    jnz la
    mov ebp, dword ptr [esp + 0x2c]
    jmp %#x''' % (T, L['out']), L['la'])
    b = asm('''
    mov edi, dword ptr [esp + 0x10]
    mov esi, dword ptr [esp + 0xc]
    sub esi, ebp
lb: movzx edx, byte ptr [eax]
    mov dl, byte ptr [edx + %#x]
    mov byte ptr [eax], dl
    add eax, edi
    dec esi
    jnz lb
    mov ebp, dword ptr [esp + 0xc]
    jmp %#x''' % (T, L['out']), L['lb'])
    return a, b


# ---------------------------------------------------------------- infrared ground plane rows (EN 0x2A779E)
# 2 blocks, 26 instructions and 13 memory accesses per pixel (locals reloaded and stored at each one): now one block
# of 11, u and v kept in registers as 16.16 (integer bytes in BL/BH with add/adc, the same modulo-256 values), the
# texture, shade and remap tables in [reg+disp32] fields that DOSBox reads from memory once rewritten. Leaves the
# locals and registers as the original loop does, then its epilogue. Reached only with a shade and a remap table.
IRROW = dict(en=dict(hook=0x2A779E, epi=0x2A799F, tex=0x427D88), fr=dict(hook=0x2A778E, epi=0x2A798F, tex=0x427E38))
IRROW_OLD = ('8b3c248b4424088d50ff8954240885c00f84eb0100008b5c241089e8c1e810c1eb0888fc8b15%s8a040225ff000000'
             '8a040125ff0000008b54240c8a04068d5a0188028b4424048b54241001fd01c2895c240c89542410eba7')
IR_SRC = '''
    mov eax, dword ptr [esp + 8]
    test eax, eax
    jz ir_z0
    mov edx, dword ptr [{tex}]
    mov dword ptr [{lt} + 3], edx
    mov dword ptr [{ls} + 2], ecx
    mov dword ptr [{lr} + 2], esi
    mov edx, dword ptr [esp]
    mov ebx, edx
    shl edx, 16
    mov dword ptr [{lu} + 2], edx
    shr ebx, 16
    mov byte ptr [{lui} + 2], bl
    mov edx, dword ptr [esp + 4]
    mov ebx, edx
    shl edx, 16
    mov dword ptr [{lv} + 2], edx
    shr ebx, 16
    mov byte ptr [{lvi} + 2], bl
    mov ecx, eax
    mov edi, dword ptr [esp + 0xc]
    mov eax, dword ptr [esp + 0x10]
    mov ebx, ebp
    shr ebx, 16
    mov edx, eax
    shr edx, 16
    mov bh, dl
    and ebx, 0xffff
    mov edx, ebp
    shl edx, 16
    shl eax, 16
    mov ebp, eax
    jmp ir_lp
ir_lp:
    movzx eax, byte ptr [ebx + 0x7fffff01]
    mov al, byte ptr [eax + 0x7fffff02]
    mov al, byte ptr [eax + 0x7fffff03]
    mov byte ptr [edi], al
    inc edi
    add edx, 0x7fffff04
    adc bl, 0x75
    add ebp, 0x7fffff05
    adc bh, 0x76
    dec ecx
    jnz ir_lp
    mov ebx, edi
    mov dword ptr [esp + 0xc], edi
    mov eax, dword ptr [esp + 4]
    imul eax, dword ptr [esp + 8]
    add dword ptr [esp + 0x10], eax
    xor eax, eax
    or edx, -1
    mov dword ptr [esp + 8], edx
    jmp {epi}
ir_z0:
    or edx, -1
    mov dword ptr [esp + 8], edx
    jmp {epi}
'''


def ir_cave(L, at):
    """assembled twice: the marker immediates give the slot addresses, then the prologue writes into them"""
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    lab = dict(lt=at, ls=at, lr=at, lu=at, lui=at, lv=at, lvi=at)
    fmt = lambda: IR_SRC.format(tex=hex(L['tex']), epi=hex(L['epi']), **{k: hex(v) for k, v in lab.items()})
    code = asm(fmt(), at)
    mark = {'0x7fffff01': 'lt', '0x7fffff02': 'ls', '0x7fffff03': 'lr', '0x7fffff04': 'lu', '0x7fffff05': 'lv'}
    for i in Cs(CS_ARCH_X86, CS_MODE_32).disasm(code, at):
        for m, k in mark.items():
            if m in i.op_str: lab[k] = i.address
        if i.mnemonic == 'adc' and i.op_str == 'bl, 0x75': lab['lui'] = i.address
        if i.mnemonic == 'adc' and i.op_str == 'bh, 0x76': lab['lvi'] = i.address
    assert len(set(lab.values())) == 7, lab
    code2 = asm(fmt(), at)
    assert len(code2) == len(code)
    return code2


# ---------------------------------------------------------------- the patcher (guest code)
PATCHER = '''
gpatch:
    cmp dword ptr [{done}], 0
    jne gp_ret
    mov dword ptr [{done}], 1{pre}
    mov esi, {table}
    mov ebp, 1
gp_group:
    movzx eax, word ptr [esi]
    test eax, eax
    jz gp_ret
    add eax, esi
    push eax
    movzx ecx, byte ptr [esi + 2]
    add esi, 3
    push esi
    push ecx
    xor ebx, ebx
gp_ra:
    mov edi, dword ptr [esi]
    movzx edx, byte ptr [esi + 4]
    add esi, 5
gp_ba:
    mov al, byte ptr [edi]
    test byte ptr [esi + 2], 1
    jz gp_a1
    cmp al, byte ptr [esi]
    je gp_a1
    or bl, 2
gp_a1:
    test byte ptr [esi + 2], 4
    jz gp_a2
    cmp al, byte ptr [esi + 1]
    je gp_a2
    or bl, 1
gp_a2:
    inc edi
    add esi, 3
    dec edx
    jnz gp_ba
    dec ecx
    jnz gp_ra
    pop ecx
    pop esi
    test bl, 1
    jz gp_ok
    test bl, 2
    jnz gp_bad
gp_rb:
    mov edi, dword ptr [esi]
    movzx edx, byte ptr [esi + 4]
    add esi, 5
gp_bb:
    test byte ptr [esi + 2], 2
    jz gp_b1
    mov al, byte ptr [esi + 1]
    mov byte ptr [edi], al
gp_b1:
    inc edi
    add esi, 3
    dec edx
    jnz gp_bb
    dec ecx
    jnz gp_rb
gp_ok:
    or dword ptr [{ok}], ebp
    jmp gp_nx
gp_bad:
    or dword ptr [{bad}], ebp
gp_nx:
    pop esi
    add ebp, ebp
    jmp gp_group
gp_ret:
    ret
'''


def groups(fr, sky_at, ir_at, T):
    """[(name, [records])] for one exe; sky_at / ir_at: where our two routines are"""
    L = 'fr' if fr else 'en'
    out = [('transparent filler', tf_groups(fr))]
    for name, rs, ctx in P1:
        recs = [rec(f if fr else e, ofr if fr else oen, nfr if fr else nen) for e, f, oen, nen, ofr, nfr in rs]
        recs += [rec(f if fr else e, bf if fr else be) for e, f, be, bf in ctx]
        out.append((name, recs))
    s = SKY[L]
    la, lb = sky_loops(s, T)
    old_a, old_b = SKY_OLD_A.replace(' ', ''), SKY_OLD_B.replace(' ', '')
    fl = struct.pack('<I', s['flags']).hex()
    old_a = old_a.replace('..........', fl + '00'); old_b = old_b.replace('..........', fl + '00')
    assert len(la) <= len(old_a) // 2 and len(lb) <= len(old_b) // 2
    pad = lambda b, n: (b + b'\x90' * (n - len(b))).hex()
    hook = asm('jmp %#x' % sky_at, s['hook'])
    out.append(('sky pass', [rec(s['hook'], '5351525657', hook.hex()), rec(s['hook'] + 5, '55'),
                             rec(s['la'], old_a, pad(la, len(old_a) // 2)), rec(s['lb'], old_b, pad(lb, len(old_b) // 2)),
                             rec(s['out'], 'a1')]))
    r = IRROW[L]
    old = IRROW_OLD % struct.pack('<I', r['tex']).hex()
    hook = asm('jmp %#x' % ir_at, r['hook'])
    out.append(('infrared ground', [rec(r['hook'], old[:10], hook.hex()), rec(r['hook'] + 5, old[10:]),
                                    rec(r['epi'], '83c4145d5f5e59c3')]))
    return out


def table(gs, where=None):
    """groups -> bytes: per group u16 size, u8 records; per record u32 address, u8 length, (old, new, mask) per byte.
    where: {address: offset in the table of the new value of that byte}, filled if given"""
    t = b''
    for name, recs in gs:
        body = b''
        for at, bs in recs:
            assert len(bs) < 256
            if where is not None:
                for k in range(len(bs)): where.setdefault(at + k, len(t) + 3 + len(body) + 5 + 3 * k + 1)
            body += struct.pack('<IB', at, len(bs)) + bytes(x for o, n, m in bs for x in (o, n, m))
        t += struct.pack('<HB', 3 + len(body), len(recs)) + body
    return t + b'\0\0'


def tf_mode(fr, tab_at, where):
    """the filler's transparency byte MM as the last set-up left it (path C's je, 0x74, after set-ups 1 and 3; 0xFF after
    the opaque set-up 2): a polygon can reuse the set-up of the one before without calling it again"""
    d = 0x280 if fr else 0
    return ''.join('\n    cmp byte ptr [%#x], 0x74\n    sete al\n    mov byte ptr [%#x], al' % (
        0x3317A0 + d, tab_at + where[a + d]) for a in (0x331760, 0x331780))


def build(fr, base, done, ok, bad):
    """our block at base: T (256 B, rebuilt by the sky routine each frame), the patcher, the two routines, the table.
    Returns (bytes, patcher address, groups)."""
    L = 'fr' if fr else 'en'
    T = base
    at = base + 0x100
    where = {}
    table(groups(fr, at, at, T), where)
    pat_len = len(asm(PATCHER.format(done=done, ok=ok, bad=bad, table=at, pre=tf_mode(fr, at, where)), at))
    sky_at = (at + pat_len + 15) & ~15
    sky = asm(sky_cave(SKY[L], T), sky_at)
    ir_at = (sky_at + len(sky) + 15) & ~15
    ir = ir_cave(IRROW[L], ir_at)
    tab_at = (ir_at + len(ir) + 15) & ~15
    gs = groups(fr, sky_at, ir_at, T)
    where = {}
    tab = table(gs, where)
    pat = asm(PATCHER.format(done=done, ok=ok, bad=bad, table=tab_at, pre=tf_mode(fr, tab_at, where)), at)
    assert len(pat) == pat_len
    blob = bytearray(tab_at - base) + tab
    blob[at - base:at - base + len(pat)] = pat
    blob[sky_at - base:sky_at - base + len(sky)] = sky
    blob[ir_at - base:ir_at - base + len(ir)] = ir
    assert len(gs) <= 32
    return bytes(blob), at, gs


def check(image, fr, base=0x468100):
    """every group against a memory image: old (fixed bytes) found, new not yet there"""
    data = open(image, 'rb').read()
    rd = lambda a, n: data[a - 0x1AB000:a - 0x1AB000 + n]
    blob, pat, gs = build(fr, base, 0x466000, 0x466004, 0x466008)
    good = True
    for name, recs in gs:
        st = []
        for a, bs in recs:
            cur = rd(a, len(bs))
            old_ok = all(c == o for c, (o, n, m) in zip(cur, bs) if m & OLD)
            new_ok = all(c == n for c, (o, n, m) in zip(cur, bs) if m & 4)
            st.append('old' if old_ok else ('new' if new_ok else 'BAD'))
        print('  %-20s %s' % (name, ' '.join(st)))
        good &= all(x == 'old' for x in st)
    print('  %d bytes at %#x, patcher %#x' % (len(blob), base, pat))
    return good


if __name__ == '__main__':
    ok = True
    for img, fr in ((sys.argv[1], False), (sys.argv[2], True)):
        print('FR' if fr else 'EN', img)
        ok &= check(img, fr)
    print('all groups match the old bytes' if ok else 'MISMATCH')
