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
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

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


def unchecked(r, lo, hi):
    """the same record, its bytes in [lo, hi) still written but no longer checked to see the group is in place
    (a later group rewrites them)"""
    at, bs = r
    return (at, [(o, n, m & ~4 if lo <= at + k < hi else m) for k, (o, n, m) in enumerate(bs)])


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
    ('horizon test', [(0x2B4780, 0x2B4770,     # ends 'pop ebx / ret' at 0x2B47B8: see the terrain returns below
        '53c1fa1085c07d0ca1087e42003b100f9ec05bc38b1dccca430039d87c1189d88b1d087e42003b5483fc0f9ec05bc38b1d087e42003b14830f9ec05bc38d4000',
        '53c1fa1085c07d09a1087e42003b10eb218b1dccca430039d87c0e89d88b1d087e42003b5483fceb098b1d087e42003b1483b0017e02b0005bc3909090909090',
        '53c1fa1085c07d0ca1b87e42003b100f9ec05bc38b1d7ccb430039d87c1189d88b1db87e42003b5483fc0f9ec05bc38b1db87e42003b14830f9ec05bc38d4000',
        '53c1fa1085c07d09a1b87e42003b10eb218b1d7ccb430039d87c0e89d88b1db87e42003b5483fceb098b1db87e42003b1483b0017e02b0005bc3909090909090')],
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

# ---------------------------------------------------------------- terrain raycaster and clouds
# Step 0x2B4428 and its only callee, the projection 0x2B2930: a memory access between the last flag writer and imul /
# adc makes DOSBox save and reload the flags (POPFQ) 5 times per call; loads moved, the register imuls placed after a
# test, the call made a push + jmp and the projection's ret a jmp back (single caller, same stack bytes). The step's
# epilogue and the horizon test's ret then jump straight to their two hot return sites (a ret always goes back to
# DOSBox's dispatcher, a jmp is linked; flags are dead at all 12 return sites; other callers still get a real ret).
# Cloud span 0x2B2768: the texel load moved above 'add ecx,esi' so adc follows add (no POPFQ per pixel); its texture
# displacement moves 2 bytes, the per-frame writer's address imm32 (0x2B27F3) follows. New opcodes land on that old
# slot: only while it still holds the placeholder (never written), otherwise left alone.
# FR: code -0x10, data +0xB0. Checked in Unicorn (terrain/inplace_fix.py, ret_cave.py): 4000 horizon calls, 600
# step+projection calls, 300 cloud spans, 3000 calls through the returns (hot, cold and unknown sites).
TERRAIN = dict(
    en=dict(step=(0x2B4428,
        '5351525657558b1d6cca43008d049d000000008b1570ca430029d801c2668b420166c1f8048b1d048842000fbfc80fafd9031dc8ca4300891570ca430081fb000001007d0430c9eb22e8bae4ffffa1f07d4200668b1d84ca430066895810668b1d88ca4300b10166895812',
        '535152565755a16cca43008d04408b1570ca430001c2668b42018b1d0488420066c1f8040fbfc80fafd9031dc8ca4300891570ca430081fb000001007d0430c9eb296876442b00e9bce4ffff8d642404a1f07d42008b1d84ca4300668958108b1d88ca4300b10166895812'),
        project=(0x2B2930,
        '55a1ec874200f7e98be8a1f8874200f7e98bf0a1c4ca430003e8a144cb430003f08bc381fbffffff007f0ec1e8108a8860c9430080c108eb0cc1e8188a8860c9430080c110d3eb80c10881e3ff0000008b049d60c54300f7ed0fadd003054ccb4300a3d0ca43008b049d60c14300f7ee0fadd0030548cb4300a3d4ca43005dc3',
        '558b2dec8742008b35f887420085c90fafe90faff1a1c4ca430001c5a144cb430001c689d881fbffffff007f07c1e810b108eb05c1e818b110028860c94300d3eb81e3ff0000008b049d60c5430080c108f7ed0fadd003054ccb4300a3d0ca43008b049d60c1430085c0f7ee0fadd0030548cb4300a3d4ca43005de9c41a0000'),
        step_epi=0x2B44F7, hz_epi=0x2B47B8, step_sites=(0x2B4BA0, 0x2B4DC0), hz_sites=(0x2B4BDC, 0x2B4DF9),
        swap=0x2B2768, writer=0x2B27F3),
    fr=dict(step=(0x2B4418,
        '5351525657558b1d1ccb43008d049d000000008b1520cb430029d801c2668b420166c1f8048b1db48842000fbfc80fafd9031d78cb4300891520cb430081fb000001007d0430c9eb22e8bae4ffffa1a07e4200668b1d34cb430066895810668b1d38cb4300b10166895812',
        '535152565755a11ccb43008d04408b1520cb430001c2668b42018b1db488420066c1f8040fbfc80fafd9031d78cb4300891520cb430081fb000001007d0430c9eb296866442b00e9bce4ffff8d642404a1a07e42008b1d34cb4300668958108b1d38cb4300b10166895812'),
        project=(0x2B2920,
        '55a19c884200f7e98be8a1a8884200f7e98bf0a174cb430003e8a1f4cb430003f08bc381fbffffff007f0ec1e8108a8810ca430080c108eb0cc1e8188a8810ca430080c110d3eb80c10881e3ff0000008b049d10c64300f7ed0fadd00305fccb4300a380cb43008b049d10c24300f7ee0fadd00305f8cb4300a384cb43005dc3',
        '558b2d9c8842008b35a888420085c90fafe90faff1a174cb430001c5a1f4cb430001c689d881fbffffff007f07c1e810b108eb05c1e818b110028810ca4300d3eb81e3ff0000008b049d10c6430080c108f7ed0fadd00305fccb4300a380cb43008b049d10c2430085c0f7ee0fadd00305f8cb4300a384cb43005de9c41a0000'),
        step_epi=0x2B44E7, hz_epi=0x2B47A8, step_sites=(0x2B4B90, 0x2B4DB0), hz_sites=(0x2B4BCC, 0x2B4DE9),
        swap=0x2B2758, writer=0x2B27E3))


def ret_caves(L, at):
    """(bytes, step cave address, horizon cave address): compare the return address with the two hot sites"""
    def src(sites, pops):
        s = ''.join('    pop %s\n' % r for r in pops)
        for k, a in enumerate(sites):
            s += '    cmp dword ptr [esp], %#x\n    jne r_%d\n    lea esp, [esp+4]\n    jmp %#x\nr_%d:\n' % (a, k, a, k)
        return s + '    ret\n'
    cs = asm(src(L['step_sites'], ['edx', 'ecx', 'ebx']), at)
    ch = asm(src(L['hz_sites'], ['ebx']), at + len(cs))
    return cs + ch, at, at + len(cs)


def terrain_groups(L, ret_at):
    _, cs, ch = ret_caves(L, ret_at)
    sw = L['swap']
    return [
        ('raycaster step', [rec(L['step'][0], L['step'][1], L['step'][2]), rec(L['project'][0], L['project'][1], L['project'][2])]),
        ('terrain returns', [rec(L['step_epi'], '5a595bc390', asm('jmp %#x' % cs, L['step_epi']).hex()),
                             rec(L['hz_epi'], '5bc3909090', asm('jmp %#x' % ch, L['hz_epi']).hex())]),
        ('cloud span', [rec(sw, '03ce8a83efbeadde', '8a83efbeadde03ce'),
                        rec(L['writer'], struct.pack('<I', sw + 8).hex(), struct.pack('<I', sw + 6).hex())]),
    ]


OPTIONAL = {'cloud span'}       # skipped quietly when its slot was already written (3D drawn before the first HD frame)

# ---------------------------------------------------------------- terrain fill columns without shade (EN 0x2B2AF7)
# Pixel = P[S[T[HI|dl]]] with T, S, P fixed for a call and dl + 1 per pixel across all its columns: the first 256
# pixels of a call are drawn as before and kept in a 256-byte table, every later one is read from it (4 per block).
# The routine's per-call operands live in its stack frame (300 bytes, table included), the per-frame row table and
# stride are read from the slots the game writes (EN 0x2B2B8A / 0x2B2BB0). Same columns, order, y-buffer, exit
# registers and flags (terrain/column_v3.py: 300 calls; independent check on EN and FR, whole address space: 0 diff).
COLUMN = dict(en=dict(entry=0x2B2AF7, old='ba4f2b2b008b4308', rowt=0x2B2B8A, stride=0x2B2BB0),
              fr=dict(entry=0x2B2AE7, old='ba3f2b2b008b4308', rowt=0x2B2B7A, stride=0x2B2BA0))
COLUMN_SRC = '''
    sub esp, 300
    mov eax, dword ptr [ebx+8]
    lea eax, [eax+ecx*4]
    mov dword ptr [esp+0], eax
    mov eax, dword ptr [ebx+0xc]
    add eax, ecx
    sub eax, dword ptr [ebx+0x24]
    mov dword ptr [esp+4], eax
    mov eax, dword ptr [{rowt}]
    mov dword ptr [esp+8], eax
    mov eax, dword ptr [{stride}]
    mov dword ptr [esp+36], eax
    mov dword ptr [esp+12], 256
    mov eax, dword ptr [ebx+0x14]
    mov dword ptr [esp+32], eax
    mov edx, dword ptr [ebx+4]
    mov eax, edx
    and eax, 0xffffff00
    mov dword ptr [esp+28], eax
    add eax, dword ptr [ebx]
    mov dword ptr [esp+16], eax
    mov eax, dword ptr [ebx+0x18]
    mov dword ptr [esp+20], eax
    mov eax, dword ptr [ebx+0x1c]
    mov dword ptr [esp+24], eax
    mov ebp, dword ptr [esp+0]
    mov esi, dword ptr [ebx+0x10]
    neg ecx
    shl ecx, 2
b_top:
    mov eax, esi
    sar eax, 0x10
    mov ebx, dword ptr [ecx+ebp]
    cmp eax, ebx
    jl b_draw
b_skip:
    add esi, dword ptr [esp+32]
    add ecx, 4
    jne b_top
    lea esp, [esp+300]
    pop ebp
    ret
b_draw:
    sar ecx, 2
b_draw2:
    mov dword ptr [ebp+ecx*4], eax
    mov ebp, dword ptr [esp+8]
    mov edi, dword ptr [ebp+eax*4]
    sub ebx, eax
    add edi, dword ptr [esp+4]
    add edi, ecx
    xor eax, eax
    movzx edx, dl
    mov ebp, dword ptr [esp+36]
    cmp dword ptr [esp+12], 0
    jle b_lut
    push ecx
    push esi
    push ebx
    mov ecx, dword ptr [esp+12+12]
    sub ecx, ebx
    jge b_pall
    neg ecx
    mov dword ptr [esp], ecx
    mov ebx, dword ptr [esp+12+12]
    mov dword ptr [esp+12+12], 0
    jmp b_pgo
b_pall:
    mov dword ptr [esp+12+12], ecx
    mov dword ptr [esp], 0
b_pgo:
    mov ecx, dword ptr [esp+12+16]
    mov esi, dword ptr [esp+12+20]
    mov ebp, dword ptr [esp+12+24]
    sub ebx, 4
    jl b_prem
b_p4:
    mov al, byte ptr [edx+ecx]
    mov al, byte ptr [eax+esi]
    mov al, byte ptr [eax+ebp]
    mov byte ptr [esp+edx+52], al
    add edi, dword ptr [esp+12+36]
    mov byte ptr [edi], al
    inc dl
    mov al, byte ptr [edx+ecx]
    mov al, byte ptr [eax+esi]
    mov al, byte ptr [eax+ebp]
    mov byte ptr [esp+edx+52], al
    add edi, dword ptr [esp+12+36]
    mov byte ptr [edi], al
    inc dl
    mov al, byte ptr [edx+ecx]
    mov al, byte ptr [eax+esi]
    mov al, byte ptr [eax+ebp]
    mov byte ptr [esp+edx+52], al
    add edi, dword ptr [esp+12+36]
    mov byte ptr [edi], al
    inc dl
    mov al, byte ptr [edx+ecx]
    mov al, byte ptr [eax+esi]
    mov al, byte ptr [eax+ebp]
    mov byte ptr [esp+edx+52], al
    add edi, dword ptr [esp+12+36]
    mov byte ptr [edi], al
    inc dl
    sub ebx, 4
    jge b_p4
b_prem:
    add ebx, 4
    jz b_pdone
b_plain:
    mov al, byte ptr [edx+ecx]
    mov al, byte ptr [eax+esi]
    mov al, byte ptr [eax+ebp]
    mov byte ptr [esp+edx+52], al
    add edi, dword ptr [esp+12+36]
    mov byte ptr [edi], al
    inc dl
    dec ebx
    jnz b_plain
b_pdone:
    mov ebp, dword ptr [esp+12+36]
    cmp dword ptr [esp+12+12], 0
    jg b_pend
    mov ecx, dword ptr [esp+52]
    mov dword ptr [esp+308], ecx
b_pend:
    pop ebx
    pop esi
    pop ecx
    test ebx, ebx
    jz b_done
b_lut:
    sub ebx, 4
    jl b_rem
b_l4:
    mov al, byte ptr [esp+edx+40]
    mov byte ptr [edi+ebp], al
    mov al, byte ptr [esp+edx+41]
    mov byte ptr [edi+ebp*2], al
    lea edi, [edi+ebp*2]
    mov al, byte ptr [esp+edx+42]
    mov byte ptr [edi+ebp], al
    mov al, byte ptr [esp+edx+43]
    mov byte ptr [edi+ebp*2], al
    lea edi, [edi+ebp*2]
    add dl, 4
    sub ebx, 4
    jge b_l4
b_rem:
    add ebx, 4
    jz b_done
b_l1:
    mov al, byte ptr [esp+edx+40]
    add edi, ebp
    mov byte ptr [edi], al
    inc dl
    dec ebx
    jnz b_l1
b_done:
    or edx, dword ptr [esp+28]
    mov ebp, dword ptr [esp+0]
    add esi, dword ptr [esp+32]
    inc ecx
    je b_exit
    mov eax, esi
    sar eax, 0x10
    mov ebx, dword ptr [ebp+ecx*4]
    cmp eax, ebx
    jl b_draw2
    shl ecx, 2
    jmp b_skip
b_exit:
    lea esp, [esp+300]
    pop ebp
    ret
'''


def column_cave(L, at):
    return asm(COLUMN_SRC.format(rowt=hex(L['rowt']), stride=hex(L['stride'])), at)


def column_group(L, at):
    hook = asm('jmp %#x' % at, L['entry'])
    return ('terrain columns', [rec(L['entry'], L['old'], hook.hex() + L['old'][2 * len(hook):])])


# ---------------------------------------------------------------- terrain shading down the columns (EN 0x2B29B0)
# Beyond the polygon ring the ground is drawn as column slabs, one call per projected cell edge: the shade is
# interpolated across the columns but constant down each column, so it jumps at every edge along the jagged ring lines
# (stair steps on shaded slopes, at medium and long range). The renderer is replaced as a whole (jmp at its entry):
# each column keeps E[x] = (top row, ring tag, shade 8.8) of the last slab call that reached it, drawn or hidden, in the
# y-buffer's own block (allocated 0x1400 bytes larger, E cleared); when E[x] belongs to the previous ring, the shade
# runs down the column along the line through (top, s) and (top_e, s_e), per pixel ('add / adc' on the S row), so the
# slab below meets it without a step and a silhouette gets the value between the two ring crossings. Otherwise
# (first slab above the polygon ring, older ring, same level at both ends) the slab is flat as before. The ring tag is
# counted by the per-ring call (EN 0x2B5826), reset by the y-buffer allocation (once per view: main view and cockpit
# cameras). Unshaded calls (shade step 0) take the same routine; a canvas whose S is not a shade row (remap / infrared)
# is drawn flat and never matches. P (fog row, per ring) and the stride (per view) live in disp32 fields written only
# when they change. Same y-buffer as the game's; checked in Unicorn on a whole terrain frame (EN): with the tags off
# the same pixels as the game, with them on only the shading inside slabs changes. FR: code -0x10, data +0xB0, the
# allocator FR 0x2C4FD0.
VS = dict(en=dict(entry=0x2B29B0, alloc=0x2AB65E, ring=0x2B5826, SB=0x41C228, ROWT=0x2B2A6A, STR=0x2B2AA0,
                  AC33C=0x2AC33C, RECIP=0x427F54, MALLOC=0x2C4F40, CANVAS=0x44B1A0, ABACK=0x2AB666, BLK=0x427E10),
          fr=dict(entry=0x2B29A0, alloc=0x2AB64E, ring=0x2B5816, SB=0x41C2D8, ROWT=0x2B2A5A, STR=0x2B2A90,
                  AC33C=0x2AC32C, RECIP=0x428004, MALLOC=0x2C4FD0, CANVAS=0x44B250, ABACK=0x2AB656, BLK=0x427EC0))
VS_VARS = dict(RING=0x467AF0, RTAG=0x467AF4, PTAG=0x467AF8)    # a page without code (HD data, after its NV block)
EOFF = 0x1400                   # E[x] after the y-buffer in its block: 4 x 1280 columns
VS_MARK = 0x7E570000
VS_NAMES = ['P%d' % k for k in range(10)] + ['S%d' % k for k in range(6)]


def px_flat(k, store):
    return '''
    mov al, byte ptr [esi+edx]
    inc dl
    mov al, byte ptr [eax+ecx]
    .byte 0x8a, 0x80
    .long {P%d}
    %s''' % (k, store)


def px_grad(k, store):
    return '''
    mov al, byte ptr [esi+edx]
    inc dl
    add ebx, ebp
    adc ch, dh
    mov al, byte ptr [eax+ecx]
    .byte 0x8a, 0x80
    .long {P%d}
    %s''' % (k, store)


def st_field(j):
    return '.byte 0x88, 0x87\n    .long {S%d}' % j


def vs_src():
    s = '''
vs_entry:
    push ebp
    mov ebp, dword ptr [ebx+0x18]
    mov eax, ebp
    sub eax, {SB}
    cmp eax, 0x1000
    jae vs_special
    push dword ptr [{PTAG}]
    push {SB}
    add eax, dword ptr [ebx+0x28]
    or eax, 0x80
    mov ebp, eax
    jmp vs_frame
vs_special:
    push 0xfffff000
    push ebp
    mov ebp, 0x80
vs_frame:
    push dword ptr [{STR}]
    push dword ptr [{ROWT}]
    push dword ptr [ebx]
    push dword ptr [ebx+0x2c]
    push dword ptr [ebx+0x14]
    mov eax, dword ptr [ebx+0xc]
    add eax, ecx
    sub eax, dword ptr [ebx+0x24]
    push eax
    mov eax, dword ptr [ebx+8]
    lea eax, [eax+ecx*4]
    push eax
    mov eax, dword ptr [ebx+0x1c]
    cmp eax, dword ptr [{FP0}]
    je vs_pok
''' + ''.join('    mov dword ptr [{FP%d}], eax\n' % k for k in range(10)) + '''
vs_pok:
    mov eax, dword ptr [esp+24]
    cmp eax, dword ptr [{FS0}]
    je vs_sok
    mov dword ptr [{FS0}], eax
    mov dword ptr [{FS5}], eax
    add eax, eax
    mov dword ptr [{FS1}], eax
    add eax, dword ptr [esp+24]
    mov dword ptr [{FS2}], eax
    add eax, dword ptr [esp+24]
    mov dword ptr [{FS3}], eax
    mov dword ptr [{FS4}], eax
vs_sok:
    mov edx, dword ptr [ebx+4]
    mov esi, dword ptr [ebx+0x10]
    mov edi, dword ptr [esp]
    neg ecx
vs_col:
    mov eax, esi
    sar eax, 16
    mov ebx, dword ptr [edi+ecx*4]
    cmp eax, ebx
    jl vs_draw
vs_hide:
    shl eax, 20
    mov ebx, ebp
    and ebx, 0xfff
    or eax, ebx
    or eax, dword ptr [{RTAG}]
    mov dword ptr [edi+ecx*4+{EOFF}], eax
    add esi, dword ptr [esp+8]
    add ebp, dword ptr [esp+12]
    inc ecx
    jne vs_col
vs_exit:
    cmp dword ptr [esp+28], {SB}
    jne vs_x2
    mov eax, ebp
    add eax, {SB}
    sub eax, dword ptr [{BLK18}]
    mov dword ptr [{BLK28}], eax
vs_x2:
    add esp, 36
    pop ebp
    ret

vs_draw:
    mov dword ptr [edi+ecx*4], eax
    sub ebx, eax
    push ecx
    push esi
    mov esi, ebp
    and esi, 0xfff
    or esi, dword ptr [{RTAG}]
    push ebp
    mov ebp, eax
    shl ebp, 20
    or ebp, esi
    mov esi, dword ptr [edi+ecx*4+{EOFF}]
    mov dword ptr [edi+ecx*4+{EOFF}], ebp
    mov ebp, dword ptr [esp]
    mov edi, dword ptr [esp+32]
    mov edi, dword ptr [edi+eax*4]
    cmp ebp, 0xfff
    ja vs_fl0
    add edi, dword ptr [esp+16]
    add edi, ecx
    mov ecx, esi
    xor ecx, dword ptr [esp+44]
    test ecx, 0xff000
    jnz vs_flat
    mov ecx, esi
    xor ecx, ebp
    test ecx, 0xf00
    jz vs_flat
    mov ecx, esi
    shr ecx, 20
    sub ecx, eax
    cmp ecx, ebx
    jl vs_flat
    dec ecx
    cmp ecx, 255
    ja vs_slow
    and esi, 0xfff
    sub esi, ebp
    imul esi, dword ptr [ecx*4+{RECIP4}]
    sar esi, 8
vs_step:
    mov ecx, ebp
    shl ecx, 8
    sub ecx, esi
    mov eax, ecx
    shl eax, 16
    or ebx, eax
    sar ecx, 8
    and ecx, 0xffffff00
    add ecx, dword ptr [esp+40]
    mov ebp, esi
    shl ebp, 16
    sar esi, 16
    mov eax, esi
    mov dh, al
    movzx eax, al
    shl eax, 8
    mov esi, dword ptr [esp+28]
    sub esi, eax
    xor eax, eax
    sub bx, 4
    jl g_rem
g_4:''' + px_grad(5, st_field(0)) + px_grad(6, st_field(1)) + px_grad(7, st_field(2)) + px_grad(8, st_field(3)) + '''
    .byte 0x8d, 0xbf
    .long {S4}
    sub bx, 4
    jge g_4
g_rem:
    add bx, 4
    jz g_done
g_1:''' + px_grad(9, '.byte 0x8d, 0xbf\n    .long {S5}\n    mov byte ptr [edi], al') + '''
    dec bx
    jnz g_1
g_done:
    and edx, 0xff
    jmp vs_after

vs_slow:
    inc ecx
    jle vs_flat
    and esi, 0xfff
    sub esi, ebp
    shl esi, 8
    mov eax, esi
    push edx
    cdq
    idiv ecx
    pop edx
    mov esi, eax
    jmp vs_step

vs_fl0:
    add edi, dword ptr [esp+16]
    add edi, ecx
vs_flat:
    mov ecx, ebp
    and ecx, 0xff00
    add ecx, dword ptr [esp+40]
    mov esi, dword ptr [esp+28]
    mov ebp, dword ptr [esp+36]
    xor eax, eax
    sub ebx, 4
    jl f_rem
f_4:''' + px_flat(0, 'mov byte ptr [edi+ebp], al') + px_flat(1, 'mov byte ptr [edi+ebp*2], al\n    lea edi, [edi+ebp*2]') + \
        px_flat(2, 'mov byte ptr [edi+ebp], al') + px_flat(3, 'mov byte ptr [edi+ebp*2], al\n    lea edi, [edi+ebp*2]') + '''
    sub ebx, 4
    jge f_4
f_rem:
    add ebx, 4
    jz vs_after
f_1:''' + px_flat(4, 'add edi, ebp\n    mov byte ptr [edi], al') + '''
    dec ebx
    jnz f_1
vs_after:
    pop ebp
    pop esi
    pop ecx
    mov edi, dword ptr [esp]
    add esi, dword ptr [esp+8]
    add ebp, dword ptr [esp+12]
    inc ecx
    je vs_exit
    mov eax, esi
    sar eax, 16
    mov ebx, dword ptr [edi+ecx*4]
    cmp eax, ebx
    jl vs_draw
    jmp vs_hide

'''
    return s


# retarget of the per-ring call at EN 0x2B5826 (call 0x2AC33C): next ring tag. RING is set to 1 by the y-buffer
# allocation (once per view); 1..254, then 0 = off for the rest of the view (no entry matches 0xFF).
RING_SRC = '''
    push eax
    mov eax, dword ptr [{RING}]
    test eax, eax
    jz r_off
    inc eax
    cmp eax, 0xff
    jb r_ok
    xor eax, eax
r_ok:
    mov dword ptr [{RING}], eax
r_off:
    shl eax, 12
    mov dword ptr [{RTAG}], eax
    sub eax, 0x1000
    mov dword ptr [{PTAG}], eax
    pop eax
    jmp {AC33C}
'''

# EN 0x2AB65E 'shl eax,2 / call 0x2C4F40' (8 bytes) -> jmp here: one block for the y-buffer and E, E cleared
ALLOC_SRC = '''
    shl eax, 2
    add eax, {EOFF}
    call {MALLOC}
    push edi
    push ecx
    push eax
    lea edi, [eax+{EOFF}]
    mov ecx, dword ptr [{CANVAS}]
    mov ecx, dword ptr [ecx+6]
    sar ecx, 16
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
    mov dword ptr [{RING}], 1
    pop eax
    pop ecx
    pop edi
    jmp {ABACK}
'''



def _vs_fmt(src, keys):
    return src.format(**{k: (hex(v) if isinstance(v, int) else v) for k, v in keys.items()})


def _vs_column(L, at):
    """the column renderer at 'at': (bytes, {field: address}); no self-written field crosses a page"""
    keys = dict(L); keys.update(VS_VARS); keys['EOFF'] = EOFF; keys['RECIP4'] = L['RECIP'] + 4
    keys['BLK18'] = L['BLK'] + 0x18; keys['BLK28'] = L['BLK'] + 0x28
    k1 = dict(keys); k1.update({n: VS_MARK + i for i, n in enumerate(VS_NAMES)})
    k1.update({'F' + n: 0x7E560000 + 4 * i for i, n in enumerate(VS_NAMES)})
    c1 = asm(_vs_fmt(vs_src(), k1), at)
    F = {}
    for i, n in enumerate(VS_NAMES):
        m = struct.pack('<I', VS_MARK + i)
        j = c1.find(m)
        assert j > 0 and c1.find(m, j + 1) < 0, n
        F[n] = at + j
    k2 = dict(keys); k2.update({n: 0 for n in VS_NAMES}); k2.update({'F' + n: F[n] for n in VS_NAMES})
    c2 = asm(_vs_fmt(vs_src(), k2), at)
    assert len(c2) == len(c1)
    return c2, F


def vs_cave(L, at):
    """column renderer, then the ring counter and the y-buffer allocation: (bytes, column, ring, alloc addresses)"""
    pad = 0
    while True:
        col = at + pad
        code, F = _vs_column(L, col)
        if all((a & 0xFFF) <= 0xFFC for a in F.values()):
            break
        pad += 4
    keys = dict(L); keys.update(VS_VARS); keys['EOFF'] = EOFF
    ring = (col + len(code) + 15) & ~15
    rc = asm(_vs_fmt(RING_SRC, keys), ring)
    alloc = (ring + len(rc) + 15) & ~15
    ac = asm(_vs_fmt(ALLOC_SRC, keys), alloc)
    blob = bytearray(alloc + len(ac) - at)
    blob[:pad] = b'\xcc' * pad
    blob[col - at:col - at + len(code)] = code
    blob[ring - at:ring - at + len(rc)] = rc
    blob[alloc - at:alloc - at + len(ac)] = ac
    return bytes(blob), col, ring, alloc


def vs_group(L, at):
    _, col, ring, alloc = vs_cave(L, at)
    e, a, r = L['entry'], L['alloc'], L['ring']
    call = lambda src, to: 'e8' + struct.pack('<i', to - (src + 5)).hex()
    return ('terrain shading', [
        rec(e, '558b432c0bc0', asm('jmp %#x' % col, e).hex() + 'c0'),
        rec(a, 'c1e002' + call(a + 3, L['MALLOC']), asm('jmp %#x' % alloc, a).hex() + '909090'),
        rec(r, call(r, L['AC33C']), call(r, ring))])


# ---------------------------------------------------------------- hash probe (EN 0x2A1433, once per bucket lookup)
# The bucket lookup 0x2A13B8 compares the 4-byte key of the bucket head with 'repe cmpsb' (run by DOSBox's
# interpreter). Same 20 bytes, a translated byte loop that leaves what repe cmpsb leaves: ECX = 3-k, ESI/EDI += k+1
# (k = first differing byte, 4 on a match), the same bytes read in the same order; the first byte's mismatch (most
# probes) leaves from the block before. EAX and the flags differ only where they are dead: on a mismatch EAX is
# reloaded at 0x2A1451 and the flags rewritten by 'xor edx,edx'; on a match EAX bits 16-31 are killed by 'cwde' at
# 0x2A1465, the flags by 'test ax,ax'. The code before (0x2A141E) and the kept exits up to 'test ax,ax' are checked.
# Assumes DF = 0 (Watcom ABI) and flat segments: DS:[EDI] read where the original read ES:[EDI] (EDI = ESP+28h), as
# 'hash chain' already does. FR: code -0x10, data +0xB0. Checked in Unicorn (perf/lot3/hash_2a1433): every first
# differing byte x every XOR value, 1200 lookups on planted tables, 1014 whole calls (real EN tables, planted FR ones).
HASH = dict(en=dict(at=0x2A1433, word=0x3C5DC2, tab=0x3BA990, keys=0x3BDF10),
            fr=dict(at=0x2A1423, word=0x3C5E72, tab=0x3BAA40, keys=0x3BDFC0))


def hash_group(L):
    h = lambda v: struct.pack('<I', v).hex()
    at = L['at']
    new = asm('add esi, eax\n xor ecx, ecx\n mov cl, 3\n lodsb\n inc edi\n cmp al, byte ptr [edi - 1]\n jne %#x\n'
              'lp: lodsb\n inc edi\n cmp al, byte ptr [edi - 1]\n loope lp' % (at + 0x1E), at)
    assert len(new) == 20
    return ('hash probe', [rec(at, 'b90400000001c631c0f3a6740519c083d8ff85c0', new.hex()),
                           rec(at - 0x15, '8b80%sbe%sc1f8108d7c2428c1e004' % (h(L['tab'] - 2), h(L['keys']))),
                           rec(at + 0x14, '750866a1%seb148b4424288b1d%s31d2c1fb10e89748ffff98668b0485%s66894424386685c0' % (
                               h(L['word']), h(L['word'] - 2), h(L['tab'])))])


# ---------------------------------------------------------------- terrain shade columns and ground texture rows
# The shaded column renderer (EN 0x2B29B0) and the texture-only span (EN 0x2B26E0) draw 1 pixel per translated block.
# Shade: its per-column pixel loop runs in our routine (jmp over 'push ebp / and ebp,0xFF00' at EN 0x2B2A78, back
# to 0x2B2AAF), 4 pixels per block, reading S / T / stride / P from the game's own slots; P sits in 5 [eax+disp32]
# fields rewritten only when the slot changes. Span: the routine is replaced as a whole (jmp at EN 0x2B26E0), 4 pixels
# per block, its texture in 5 [ebx+disp32] fields refreshed from the slot the per-frame writer fills, the end of
# the span on the stack. Same pixels, accesses in the same order, same exit registers and flags (lot3/terrain_p4:
# Unicorn, EN and FR, raw / shipped / new machines, whole memory compared). FR: code -0x10.
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

TP4_CAVE_MAX = 420          # bytes, padding for page placement included
TP4_MARK = 0x7E570000       # marker displacements of the self-written fields (pass 1)
TP4_PAD = b'\xcc'           # dead bytes in front of a routine when a field would end too close to a page end
_tp4_md = Cs(CS_ARCH_X86, CS_MODE_32)


def tp4_lang(fr):
    """the per-language addresses: FR code is EN - 0x10 for both routines"""
    d = -0x10 if fr else 0
    L = dict(s_hook=0x2B2A78, s_back=0x2B2AAF, S=0x2B2A81, T=0x2B2A92, STR=0x2B2AA0, P=0x2B2AA6,
             s_scol=0x2B2A9A, s_base=0x2B2A1F, s_pro=0x2B29BC, s_wr=0x2B2AD8,
             t_hook=0x2B26E0, TEX=0x2B2722, t_end=0x2B2739, t_wr=0x2B27DA, t_texf=0x2B2726)
    L = {k: v + d for k, v in L.items()}
    L['d'] = d
    return L


def _tp4_le(v):
    return struct.pack('<I', v & 0xffffffff).hex()


def _tp4_spx(k, store):
    return '''
    mov al, byte ptr [esi+edx]
    inc dl
    mov al, byte ptr [eax+ecx]
    .byte 0x8a, 0x80
    .long %#x
    %s''' % (TP4_MARK + k, store)


def tp4_shade_src(L, f):
    """shade column pixel loop; f: {k: address of P field k} (pass 2) or {} (pass 1)"""
    s = '''
    push ecx
    push esi
    push ebp
    mov ecx, ebp
    and ecx, 0xff00
    add ecx, dword ptr [%(S)#x]
    mov esi, dword ptr [%(T)#x]
    mov ebp, dword ptr [%(STR)#x]
    mov eax, dword ptr [%(P)#x]
    cmp eax, dword ptr [%(F0)#x]
    je m_go
    mov dword ptr [%(F0)#x], eax
    mov dword ptr [%(F1)#x], eax
    mov dword ptr [%(F2)#x], eax
    mov dword ptr [%(F3)#x], eax
    mov dword ptr [%(F4)#x], eax
m_go:
    xor eax, eax
    sub ebx, 4
    jl m_rem
m_4:''' + _tp4_spx(0, 'mov byte ptr [edi+ebp], al') + \
        _tp4_spx(1, 'mov byte ptr [edi+ebp*2], al\n    lea edi, [edi+ebp*2]') + \
        _tp4_spx(2, 'mov byte ptr [edi+ebp], al') + \
        _tp4_spx(3, 'mov byte ptr [edi+ebp*2], al\n    lea edi, [edi+ebp*2]') + '''
    sub ebx, 4
    jge m_4
m_rem:
    add ebx, 4
    jz m_done
m_1:''' + _tp4_spx(4, 'add edi, ebp\n    mov byte ptr [edi], al') + '''
    dec ebx
    jnz m_1
m_done:
    pop ebp
    pop esi
    pop ecx
    jmp %(BACK)#x
'''
    d = dict(S=L['S'], T=L['T'], STR=L['STR'], P=L['P'], BACK=L['s_back'])
    for k in range(5): d['F%d' % k] = f.get(k, 0x7E560000)
    return s % d


def _tp4_tpx(k):
    return '''
    .byte 0x8a, 0x83
    .long %#x
    add ecx, esi
    adc bh, dl
    add edx, ebp
    adc bl, cl
    mov byte ptr [edi+%d], al''' % (TP4_MARK + k, k)


def tp4_span_src(L, f):
    """texture span (whole routine); f as above (texture fields)"""
    s = '''
    push ebp
    mov eax, dword ptr [%(TEX)#x]
    cmp eax, dword ptr [%(F0)#x]
    je s_go
    mov dword ptr [%(F0)#x], eax
    mov dword ptr [%(F1)#x], eax
    mov dword ptr [%(F2)#x], eax
    mov dword ptr [%(F3)#x], eax
    mov dword ptr [%(F4)#x], eax
s_go:
    xor eax, eax
    mov ax, word ptr [esi+0x16]
    add eax, edi
    push eax
    sub eax, 4
    sbb edx, edx
    not edx
    and eax, edx
    push eax
    mov edx, ecx
    shl edx, 0x10
    mov ebp, dword ptr [esi]
    shl ebp, 0x10
    mov eax, dword ptr [esi+0x10]
    mov esi, dword ptr [esi+4]
    and ebx, 0xffff
    and ecx, 0xffff0000
    and edx, 0xffff0000
    mov dl, ah
    mov cl, al
    cmp edi, dword ptr [esp]
    jae s_tail
s_four:''' + _tp4_tpx(0) + _tp4_tpx(1) + _tp4_tpx(2) + _tp4_tpx(3) + '''
    add edi, 4
    cmp edi, dword ptr [esp]
    jb s_four
s_tail:
    .byte 0x8a, 0x83
    .long %(M4)#x
    add ecx, esi
    adc bh, dl
    add edx, ebp
    adc bl, cl
    mov byte ptr [edi], al
    add edi, 1
    cmp edi, dword ptr [esp+4]
    jne s_tail
    lea esp, [esp+8]
    pop ebp
    ret
'''
    d = dict(TEX=L['TEX'], M4=TP4_MARK + 4)
    for k in range(5): d['F%d' % k] = f.get(k, 0x7E560000)
    return s % d


TP4_SRC = {'shade': tp4_shade_src, 'span': tp4_span_src}
TP4_LOOP = {'shade': 'jge', 'span': 'jb'}         # the 4-pixel loop's backward branch


def tp4_page_ok(fields):
    """dyn_x86 reads a dword field from memory only if it starts below page index 4093 (decode_fetchd_imm); kept with
    the stricter rule of the IR cave: index + 4 <= 0xFFD"""
    return all((a & 0xFFF) + 4 <= 0xFFD for a in fields)


def _tp4_assemble(kind, L, at):
    """two passes; returns (code with the 5 fields set to 0, [field addresses])"""
    fn = TP4_SRC[kind]
    c1 = asm(fn(L, {}), at)
    f = {}
    for k in range(5):
        m = struct.pack('<I', TP4_MARK + k)
        i = c1.find(m)
        assert i > 0 and c1.find(m, i + 1) < 0, (kind, k)
        f[k] = at + i
    c2 = asm(fn(L, f), at)
    assert len(c2) == len(c1), kind
    for k in range(5):
        o = f[k] - at
        assert c2[o:o + 4] == struct.pack('<I', TP4_MARK + k)
        c2 = c2[:o] + b'\0\0\0\0' + c2[o + 4:]          # every field starts at 0: the first refresh writes them
    return c2, [f[k] for k in range(5)]


def tp4_checks(kind, L, at, code, fields):
    """DOSBox placement rules (asserts): the writer stores are exactly the 5 fields and start a block (right after the
    'je' that skips them) which ends in a branch before the first field (a field write never lands in the running
    block); the fields are disp32 of 8A 80 / 8A 83 (mod 2: read from memory once rewritten); the 4-pixel loop block
    is at most 32 instructions (CreateCacheBlock limit) and branches back to its own start; the routine ends in jmp /
    ret; the fields are page_ok. Returns the loop block's instruction count."""
    ins = list(_tp4_md.disasm(code, at))
    assert sum(i.size for i in ins) == len(code), kind
    stores = [i for i in ins if i.mnemonic == 'mov' and i.bytes[0] == 0xA3]
    assert [struct.unpack_from('<I', i.bytes, 1)[0] for i in stores] == fields, kind
    k0 = ins.index(stores[0])
    assert ins[k0 - 1].mnemonic == 'je', kind
    assert [i.address for i in ins[k0:k0 + 5]] == [s.address for s in stores]
    kb = k0 + 5
    while ins[kb].mnemonic[0] != 'j': kb += 1
    assert ins[kb].address + ins[kb].size <= min(fields) - 2, kind
    assert ins[-1].mnemonic in ('jmp', 'ret'), kind
    for a in fields:
        i = [x for x in ins if x.address < a < x.address + x.size][0]
        assert i.bytes[:2] in (b'\x8a\x80', b'\x8a\x83') and i.address + 2 == a and i.size == 6, (kind, hex(a))
    j = ins.index([x for x in ins if x.address + 2 == fields[0]][0])
    while ins[j - 1].mnemonic[0] != 'j': j -= 1
    start, n = ins[j].address, 1
    while ins[j].mnemonic[0] != 'j': j += 1; n += 1
    assert ins[j].mnemonic == TP4_LOOP[kind] and int(ins[j].op_str, 16) == start, (kind, ins[j].mnemonic)
    assert n <= 32, (kind, n)
    assert tp4_page_ok(fields), (kind, [hex(a) for a in fields])
    return n


def tp4_caves(fr, at):
    """(both routines assembled at 'at', {'shade': entry, 'span': entry, 'fields': [10 addresses], 'pads': [p0, p1],
    'loop_ins': [n0, n1]}). A routine is moved forward by dead int3 bytes when a field would end past page index
    0xFFC (at most 6 bytes each)."""
    L = tp4_lang(fr)
    blob = b''
    info = dict(fields=[], pads=[], loop_ins=[])
    for kind in ('shade', 'span'):
        pad = 0
        while True:
            entry = at + len(blob) + pad
            code, fields = _tp4_assemble(kind, L, entry)
            if tp4_page_ok(fields): break
            pad += 1
            assert pad < 16
        info['loop_ins'].append(tp4_checks(kind, L, entry, code, fields))
        blob += TP4_PAD * pad + code
        info[kind] = entry
        info['fields'] += fields
        info['pads'].append(pad)
    assert len(blob) <= TP4_CAVE_MAX, len(blob)
    return blob, info


def tp4_records(fr, info):
    """the two groups: each hook, then every fixed byte of the code our routines reproduce as check-only context
    (slots the game rewrites are left out; the relocated base operands bind each group to its exe)"""
    L = tp4_lang(fr)
    le = _tp4_le
    hs = asm('jmp %#x' % info['shade'], L['s_hook']).hex()
    ht = asm('jmp %#x' % info['span'], L['t_hook']).hex()
    assert len(hs) == len(ht) == 10
    shade = [
        rec(L['s_hook'], '5581e500ff000081c5', hs + '000081c5'),       # push ebp / and ebp,0xFF00 / add ebp,S..
        rec(L['s_hook'] + 0x0D, '892d' + le(L['s_scol']) + '5d33c0eb008a82'),   # mov [Scol],ebp / pop ebp / xor
        rec(L['s_hook'] + 0x1E, 'fec28a80'),           # (eax,eax / jmp +0 / mov al,[edx+T]..) inc dl / mov al,[eax+
        rec(L['s_hook'] + 0x26, '81c7'),               # Scol].. / add edi,stride..
        rec(L['s_hook'] + 0x2C, '8a80'),               # mov al,[eax+P]..
        rec(L['s_hook'] + 0x32, '88074b75e181c6'),     # mov [edi],al / dec ebx / jne loop / add esi,..
        rec(L['s_pro'], 'ba' + le(L['s_base'])),       # mov edx,0x2B2A1F: per-call slot writer base
        rec(L['s_wr'], 'ba' + le(L['s_base'])),        # the same in the per-frame stride writer
    ]
    span = [
        rec(L['t_hook'], '5533c0668b4616', ht + '4616'),   # push ebp / xor eax,eax / mov ax,[esi+16h]
        # add eax,edi / mov edx,END+4 / mov [edx-4],eax / steppers set-up / mov cl,al / jmp loop / padding /
        # mov al,[ebx+TEX]..
        rec(L['t_hook'] + 7, '03c7ba' + le(L['t_end']) + '8942fc8bd1c1e2108b2ec1e5108b46108b760481e3ffff000081e1'
                                                        '0000ffff81e20000ffff8ad48ac8eb072e8bc02e8bc0908a83'),
        rec(L['t_hook'] + 0x46, '03ce12fa03d512d9880783c70181ff'),   # add/adc steppers, store, add edi,1, cmp edi,..
        rec(L['t_hook'] + 0x59, '75e55dc3'),           # jne loop / pop ebp / ret
        rec(L['t_wr'], 'b8' + le(L['t_texf']) + '8970fc'),   # per-frame writer: mov eax,TEX+4 / mov [eax-4],esi
    ]
    return [('terrain shade columns', shade), ('ground texture rows', span)]


def tp4_groups(fr, cave_at):
    """([(group name, [records])], both routines assembled at cave_at)"""
    blob, info = tp4_caves(fr, cave_at)
    return tp4_records(fr, info), blob


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
# 2 blocks, 26 instructions and 13 memory accesses per pixel (locals reloaded and stored at each one): now two pixels
# per block, 11 instructions each, u and v kept in registers as 16.16 (integer bytes in BL/BH with add/adc, the same modulo-256 values), the
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
    mov dword ptr [{lt1} + 3], edx
    mov dword ptr [{ls1} + 2], ecx
    mov dword ptr [{lr1} + 2], esi
    mov dword ptr [{lt2} + 3], edx
    mov dword ptr [{ls2} + 2], ecx
    mov dword ptr [{lr2} + 2], esi
    mov edx, dword ptr [esp]
    mov ebx, edx
    shl edx, 16
    shr ebx, 16
    mov dword ptr [{lu1} + 2], edx
    mov byte ptr [{lui1} + 2], bl
    mov dword ptr [{lu2} + 2], edx
    mov byte ptr [{lui2} + 2], bl
    mov edx, dword ptr [esp + 4]
    mov ebx, edx
    shl edx, 16
    shr ebx, 16
    mov dword ptr [{lv1} + 2], edx
    mov byte ptr [{lvi1} + 2], bl
    mov dword ptr [{lv2} + 2], edx
    mov byte ptr [{lvi2} + 2], bl
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
    test cl, 1
    jz ir_lp
    inc ecx
    jmp ir_mid{pad}
ir_lp:
    movzx eax, byte ptr [ebx + 0x7fff1f01]
    mov al, byte ptr [eax + 0x7fff1f02]
    mov al, byte ptr [eax + 0x7fff1f03]
    mov byte ptr [edi], al
    inc edi
    add edx, 0x7fff1f04
    adc bl, 0x71
    add ebp, 0x7fff1f05
    adc bh, 0x61
ir_mid:
    movzx eax, byte ptr [ebx + 0x7fff2f01]
    mov al, byte ptr [eax + 0x7fff2f02]
    mov al, byte ptr [eax + 0x7fff2f03]
    mov byte ptr [edi], al
    inc edi
    add edx, 0x7fff2f04
    adc bl, 0x72
    add ebp, 0x7fff2f05
    adc bh, 0x62
    sub ecx, 2
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

# slot label -> (instruction as capstone prints it with the marker operands, field offset, field size)
FIELD = {}
for _k in (1, 2):
    FIELD.update({
        'lt%d' % _k: ('movzx eax, byte ptr [ebx + 0x7fff%df01]' % _k, 3, 4),
        'ls%d' % _k: ('mov al, byte ptr [eax + 0x7fff%df02]' % _k, 2, 4),
        'lr%d' % _k: ('mov al, byte ptr [eax + 0x7fff%df03]' % _k, 2, 4),
        'lu%d' % _k: ('add edx, 0x7fff%df04' % _k, 2, 4),
        'lui%d' % _k: ('adc bl, 0x7%d' % _k, 2, 1),
        'lv%d' % _k: ('add ebp, 0x7fff%df05' % _k, 2, 4),
        'lvi%d' % _k: ('adc bh, 0x6%d' % _k, 2, 1)})


def _labels(L, at, pad=0):
    """pass 1 (slot labels = at): the marker operands give each slot instruction's address. pad: int3 bytes after the
    prologue's last 'jmp ir_mid' (dead bytes, never decoded: the block ends at that jmp) to move the loop"""
    fmt = lambda lab: IR_SRC.format(tex=hex(L['tex']), epi=hex(L['epi']), pad='\n    int3' * pad,
                                    **{k: hex(v) for k, v in lab.items()})
    code = asm(fmt({k: at for k in FIELD}), at)
    lab, marks = {}, {v[0]: k for k, v in FIELD.items()}
    ins = list(Cs(CS_ARCH_X86, CS_MODE_32).disasm(code, at))
    assert sum(i.size for i in ins) == len(code)
    for i in ins:
        s = '%s %s' % (i.mnemonic, i.op_str)
        if s in marks:
            assert marks[s] not in lab, s
            lab[marks[s]] = i.address
    assert len(lab) == 14 and len(set(lab.values())) == 14, lab
    back = [i for i in ins if i.mnemonic == 'jne' and i.op_str == hex(lab['lt1'])]     # the loop's 'jnz ir_lp'
    assert len(back) == 1
    loop = (lab['lt1'], back[0].address + back[0].size)
    for k, (s, off, n) in FIELD.items():      # the field holds the marker: offsets are those of the encoding
        mk = int(s.split('0x')[-1].rstrip(']'), 16)
        assert code[lab[k] - at + off:lab[k] - at + off + n] == mk.to_bytes(n, 'little'), (k, code.hex())
    return fmt, code, lab, loop


def page_ok(fields):
    """dyn_x86 reads a field from memory only if it starts below page index 4093 (decode_fetchd_imm); kept with the
    stricter rule of irrow_final.page_ok: index + size <= 0xFFD"""
    return all((a & 0xFFF) + n <= 0xFFD for a, n in fields)


def _placed(L, at, pad):
    fmt, code, lab, (lp, end) = _labels(L, at, pad)
    fields = sorted((lab[k] + off, n) for k, (s, off, n) in FIELD.items())
    return page_ok(fields) and lp >> 12 == (end - 1) >> 12


def ir_pad(L, at):
    """0 when the cave can sit at 'at' as reviewed (every field page_ok, the loop in one page: the case of the current
    speed_fix layout); otherwise the int3 bytes after 'jmp ir_mid' that start the loop at the next page (at most 0x55)"""
    if _placed(L, at, 0): return 0
    lp = _labels(L, at, 0)[3][0]
    pad = 0x1000 - (lp & 0xFFF)
    assert _placed(L, at, pad), 'IR cave at %#x: no placement' % at
    return pad


def ir_fields(L, at):
    """[(address, size)] of the 14 operand fields the prologue rewrites at each row"""
    lab = _labels(L, at, ir_pad(L, at))[2]
    return sorted((lab[k] + off, n) for k, (s, off, n) in FIELD.items())


def ir_cave(L, at):
    """assembled twice: the marker immediates give the slot addresses, then the prologue writes into them. Never
    returns a cave with a field DOSBox would keep static: if 'at' would put the loop across a page, the loop is moved
    to the next page by dead int3 padding after the prologue (ir_pad), else the asserts below stop the build"""
    pad = ir_pad(L, at)
    fmt, code, lab, (lp, end) = _labels(L, at, pad)
    code2 = asm(fmt(lab), at)
    assert len(code2) == len(code)
    assert code2[lp - at:end - at] == code[lp - at:end - at]           # only the prologue's addresses changed
    fields = sorted((lab[k] + off, n) for k, (s, off, n) in FIELD.items())
    assert lp == lab['lt1'] and all(lp <= a and a + n <= end for a, n in fields)    # every field in the loop blocks
    ins = list(Cs(CS_ARCH_X86, CS_MODE_32).disasm(code2, at))
    st = [i for i in ins if i.mnemonic == 'mov' and i.op_str.startswith(('dword ptr [0x', 'byte ptr [0x'))]
    assert sorted((int(i.op_str.split('[')[1].rstrip(']').split(']')[0], 16), 4 if i.op_str[0] == 'd' else 1)
                  for i in st) == fields                                # the prologue stores = exactly the 14 fields
    assert all(i.address < lp for i in st)                              # all written before the loop...
    pre = [i for i in ins if i.address < lp and i.mnemonic != 'int3'][-1]   # ...whose blocks end before it:
    assert pre.mnemonic == 'jmp' and int(pre.op_str, 16) == lab['lt2']  # 'jz ir_lp' (even) / 'jmp ir_mid' (odd)
    assert code2[pre.address + pre.size - at:lp - at] == b'\xcc' * pad  # then only the dead padding, if any
    assert any(i.mnemonic == 'je' and i.op_str == hex(lp) for i in ins)
    assert page_ok(fields), 'IR cave at %#x: an operand field ends past page index 0xFFC, move the cave' % at
    assert lp >> 12 == (end - 1) >> 12, 'IR cave at %#x: loop crosses a page' % at
    return code2


# ---------------------------------------------------------------- the patcher (guest code)
# Table: per group u16 size, u8 records; per record u32 address, u8 length, u8 flags (1 old bytes follow, 2 new bytes
# follow, 4 a mask byte per byte follows: 1 check old, 2 write new, 4 check new). Without a mask: check the old
# bytes, write the new ones that differ, check them; old bytes only = check only; new bytes only = write and check.
# Scratch at install time: the first 16 bytes of T (the sky routine rebuilds T at each call).
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
    push dword ptr [esp]
gp_ra:
    call gp_hdr
    xor edx, edx
gp_ba:
    call gp_byte
    push ecx
    mov cl, byte ptr [edi + edx]
    test eax, 0x10000
    jz gp_a1
    cmp cl, al
    je gp_a1
    or bl, 2
gp_a1:
    test eax, 0x40000
    jz gp_a2
    cmp cl, ah
    je gp_a2
    or bl, 1
gp_a2:
    pop ecx
    inc edx
    cmp edx, ecx
    jb gp_ba
    dec dword ptr [esp]
    jnz gp_ra
    add esp, 4
    test bl, 1
    jz gp_ok
    test bl, 2
    jnz gp_bad
    mov esi, dword ptr [esp + 4]
    push dword ptr [esp]
gp_rb:
    call gp_hdr
    xor edx, edx
gp_bb:
    call gp_byte
    test eax, 0x20000
    jz gp_b1
    mov byte ptr [edi + edx], ah
gp_b1:
    inc edx
    cmp edx, ecx
    jb gp_bb
    dec dword ptr [esp]
    jnz gp_rb
    add esp, 4
gp_ok:
    or dword ptr [{ok}], ebp
    jmp gp_nx
gp_bad:
    or dword ptr [{bad}], ebp
gp_nx:
    add esp, 8
    pop esi
    add ebp, ebp
    jmp gp_group
gp_hdr:
    mov edi, dword ptr [esi]
    movzx ecx, byte ptr [esi + 4]
    movzx eax, byte ptr [esi + 5]
    mov dword ptr [{fl}], eax
    add esi, 6
    mov dword ptr [{po}], esi
    test al, 1
    jz gh_1
    add esi, ecx
gh_1:
    mov dword ptr [{pn}], esi
    test al, 2
    jz gh_2
    add esi, ecx
gh_2:
    mov dword ptr [{pm}], esi
    test al, 4
    jz gp_ret
    add esi, ecx
    ret
gp_byte:
    push ebx
    push esi
    mov ebx, dword ptr [{fl}]
    xor eax, eax
    test bl, 1
    jz gb_1
    mov esi, dword ptr [{po}]
    mov al, byte ptr [esi + edx]
gb_1:
    mov ah, al
    test bl, 2
    jz gb_2
    mov esi, dword ptr [{pn}]
    mov ah, byte ptr [esi + edx]
gb_2:
    test bl, 4
    jz gb_derive
    mov esi, dword ptr [{pm}]
    movzx esi, byte ptr [esi + edx]
    jmp gb_done
gb_derive:
    xor esi, esi
    test bl, 1
    jz gd_1
    or esi, 1
gd_1:
    test bl, 2
    jnz gd_2
    or esi, 4
    jmp gb_done
gd_2:
    or esi, 6
    test bl, 1
    jz gb_done
    cmp al, ah
    jne gb_done
    and esi, 0xfffffffd
gb_done:
    shl esi, 16
    or eax, esi
    pop esi
    pop ebx
gp_ret:
    ret
'''


def caves(fr, T):
    """our routines in the GP block, in order: (name, assembler at a given address)"""
    L = 'fr' if fr else 'en'
    return [('sky', lambda at: asm(sky_cave(SKY[L], T), at)),
            ('ir', lambda at: ir_cave(IRROW[L], at)),
            ('ret', lambda at: ret_caves(TERRAIN[L], at)[0]),
            ('shading', lambda at: vs_cave(VS[L], at)[0]),
            ('terrain p4', lambda at: tp4_groups(fr, at)[1])]


def groups(fr, cv, T):
    """[(name, [records])] for one exe; cv: {routine name: its address in the GP block}"""
    L = 'fr' if fr else 'en'
    out = [('transparent filler', tf_groups(fr))]
    for name, rs, ctx in P1:
        recs = [rec(f if fr else e, ofr if fr else oen, nfr if fr else nen) for e, f, oen, nen, ofr, nfr in rs]
        recs += [rec(f if fr else e, bf if fr else be) for e, f, be, bf in ctx]
        if name == 'horizon test':      # its 'pop ebx / ret' becomes a jmp in 'terrain returns': not rechecked
            recs = [unchecked(r, TERRAIN[L]['hz_epi'], TERRAIN[L]['hz_epi'] + 5) for r in recs]
        out.append((name, recs))
    s = SKY[L]
    la, lb = sky_loops(s, T)
    old_a, old_b = SKY_OLD_A.replace(' ', ''), SKY_OLD_B.replace(' ', '')
    fl = struct.pack('<I', s['flags']).hex()
    old_a = old_a.replace('..........', fl + '00'); old_b = old_b.replace('..........', fl + '00')
    assert len(la) <= len(old_a) // 2 and len(lb) <= len(old_b) // 2
    pad = lambda b, n: (b + b'\x90' * (n - len(b))).hex()
    hook = asm('jmp %#x' % cv['sky'], s['hook'])
    out.append(('sky pass', [rec(s['hook'], '5351525657', hook.hex()), rec(s['hook'] + 5, '55'),
                             rec(s['la'], old_a, pad(la, len(old_a) // 2)), rec(s['lb'], old_b, pad(lb, len(old_b) // 2)),
                             rec(s['out'], 'a1')]))
    r = IRROW[L]
    old = IRROW_OLD % struct.pack('<I', r['tex']).hex()
    hook = asm('jmp %#x' % cv['ir'], r['hook'])
    out.append(('infrared ground', [rec(r['hook'], old[:10], hook.hex()), rec(r['hook'] + 5, old[10:]),
                                    rec(r['epi'], '83c4145d5f5e59c3')]))
    out += terrain_groups(TERRAIN[L], cv['ret'])      # after 'horizon test': its returns check the new bytes
    out.append(vs_group(VS[L], cv['shading']))      # in the place of 'terrain columns' (its routine is no longer reached)
    out.append(hash_group(HASH[L]))                    # after the 13 groups of lot 2: their bits stay
    out += tp4_groups(fr, cv['terrain p4'])[0]   # last: after every shipped group, their bits stay
    return out


def derived(flags, o, n):
    """the mask the patcher derives for a byte when the record carries none"""
    if not flags & 2: return 5
    if not flags & 1: return 6
    return 5 if o == n else 7


def encode(bs):
    """record bytes -> (flags, payload): old and/or new arrays, a mask array only when the derived one differs"""
    olds = any(m & 1 for o, n, m in bs)
    news = any(m & 2 or (m & 4 and not (m & 1 and o == n)) for o, n, m in bs)
    f = (1 if olds else 0) | (2 if news else 0)
    if not all(derived(f, o, n) == m for o, n, m in bs):
        f = 7
    pay = (bytes(o for o, n, m in bs) if f & 1 else b'') + (bytes(n for o, n, m in bs) if f & 2 else b'') + \
          (bytes(m for o, n, m in bs) if f & 4 else b'')
    return f, pay


def table(gs, where=None):
    """groups -> bytes (format: see PATCHER). where: {address: offset in the table of the new value of that byte}, filled
    if given (records that carry new bytes)"""
    t = b''
    for name, recs in gs:
        body = b''
        for at, bs in recs:
            assert 0 < len(bs) < 256
            f, pay = encode(bs)
            if where is not None and f & 2:
                for k in range(len(bs)):
                    where.setdefault(at + k, len(t) + 3 + len(body) + 6 + (len(bs) if f & 1 else 0) + k)
            body += struct.pack('<IBB', at, len(bs), f) + pay
        t += struct.pack('<HB', 3 + len(body), len(recs)) + body
    return t + b'\0\0'


def tf_mode(fr, tab_at, where):
    """the filler's transparency byte MM as the last set-up left it (path C's je, 0x74, after set-ups 1 and 3; 0xFF after
    the opaque set-up 2): a polygon can reuse the set-up of the one before without calling it again"""
    d = 0x280 if fr else 0
    return ''.join('\n    cmp byte ptr [%#x], 0x74\n    sete al\n    mov byte ptr [%#x], al' % (
        0x3317A0 + d, tab_at + where[a + d]) for a in (0x331760, 0x331780))


def build(fr, base, done, ok, bad):
    """our block at base: T (256 B, rebuilt by the sky routine each frame), the patcher, our routines, the table.
    Returns (bytes, patcher address, groups)."""
    T = base
    at = base + 0x100
    where = {}
    table(groups(fr, {n: at for n, f in caves(fr, T)}, T), where)
    scr = dict(fl=T, po=T + 4, pn=T + 8, pm=T + 12)
    pat_len = len(asm(PATCHER.format(done=done, ok=ok, bad=bad, table=at, pre=tf_mode(fr, at, where), **scr), at))
    cv, code, nxt = {}, {}, (at + pat_len + 15) & ~15
    for name, mk in caves(fr, T):
        cv[name] = nxt; code[name] = mk(nxt); nxt = (nxt + len(code[name]) + 15) & ~15
    tab_at = nxt
    gs = groups(fr, cv, T)
    where = {}
    tab = table(gs, where)
    pat = asm(PATCHER.format(done=done, ok=ok, bad=bad, table=tab_at, pre=tf_mode(fr, tab_at, where), **scr), at)
    assert len(pat) == pat_len
    blob = bytearray(tab_at - base) + tab
    blob[at - base:at - base + len(pat)] = pat
    for name in cv: blob[cv[name] - base:cv[name] - base + len(code[name])] = code[name]
    assert len(gs) <= 32
    return bytes(blob), at, gs


def optional_mask(gs):
    return sum(1 << k for k, (n, r) in enumerate(gs) if n in OPTIONAL)


def apply(mem, gs, base=0x1AB000):
    """the patcher's work on a bytearray image, group by group in order: [(name, 'on' | 'already' | 'bad')]"""
    res = []
    for name, recs in gs:
        not_new = not_old = False
        for a, bs in recs:
            for k, (o, n, m) in enumerate(bs):
                c = mem[a - base + k]
                if m & OLD and c != o: not_old = True
                if m & 4 and c != n: not_new = True
        if not not_new: res.append((name, 'already')); continue
        if not_old: res.append((name, 'bad')); continue
        for a, bs in recs:
            for k, (o, n, m) in enumerate(bs):
                if m & 2: mem[a - base + k] = n
        res.append((name, 'on'))
    return res


def check(image, fr, base=0x468100):
    """the groups applied to a memory image like the patcher does; returns True if every non-optional one goes on"""
    mem = bytearray(open(image, 'rb').read())
    blob, pat, gs = build(fr, base, 0x466000, 0x466004, 0x466008)
    good = True
    for name, st in apply(mem, gs):
        print('  %-20s %s%s' % (name, st, ' (optional)' if name in OPTIONAL else ''))
        good &= st == 'on' or name in OPTIONAL
    print('  %d bytes at %#x, patcher %#x' % (len(blob), base, pat))
    return good


if __name__ == '__main__':
    ok = True
    for img, fr in ((sys.argv[1], False), (sys.argv[2], True)):
        print('FR' if fr else 'EN', img)
        ok &= check(img, fr)
    print('all groups match the old bytes' if ok else 'MISMATCH')
