# Normal-view textured spans fixed IN PLACE at the main menu (EN 0x34D2B0..0x34D50F, FR +0x280): the module that
# draws one constant-z textured, shaded line per call (set-ups 0x34D2B0 y-major / 0x34D2E6 x-major once per polygon,
# span 0x34D390 per line; called from EN 0x331B8B / 0x331B10 / 0x331AFF only). Its pixel loop runs as 3-4 translated
# DOSBox blocks per pixel (three conditional branches: slope carry, end, v carry) and its two 'adc eax,imm32' in the
# short form 15 are rewritten per span, which DOSBox's dynamic core translates again each time.
# "T14r": the same 14 guest instructions per pixel in ONE self-linked block. The slope fraction moves to bp, the v
# fraction stays in the high half of ebp, and esi = 2*(-v carry) + slope carry (-2..1) picks the texture advance
# (adc eax,[TV+esi*4]: S+W,S+W,S,S) and the destination step (add ebx,[TS+esi*4]: s0,s1,s0,s1) from two 4-dword tables
# placed after the routine's ret (data, never decoded). Every field the code writes into itself is now of a kind DOSBox
# reads from memory once rewritten (imm32 of 25/05/81, 66 81 imm16, C1 imm8, [reg+disp32]) or table data: no
# retranslation, no opcode/modrm byte ever written. 'mov cl,[ecx+C]' sits right after the texel read and
# 'mov [ebx+D],cl' just before the TS read, so the store takes the flag save the TS read would pay (dyn_read_word
# protects the flags unconditionally): 3 PUSHFQ per pixel (VARIANT 'T14', the analyst's order: 4).
# Same arithmetic in the same order (perf study: Unicorn, EN and FR, sequences of set-ups and spans on one machine:
# registers, flags, every write outside the module identical; see test_eq.py).
# Only while the module never ran: the SHA-256 covers the whole pristine module (0xDEADBEEF / 0xFF slot placeholders,
# which the game overwrites at the first set-up) and the three call sites. That proves it for this game start only;
# src/TNPlusSpanFix.cs adds the DOSBox-process rules (a DOSBox started by TN+, every earlier game start in it run with
# our module, at most 3 installs); on_sha() / ran_sha() let it tell 'ours, already on' and 'the game's own module,
# it ran' from anything else. TN+ ships no game code: every instruction
# kept from the game is a piece 'C:address:length' copied from the player's memory before anything is written
# (re-pointed stores keep the game's opcode/modrm bytes, only their displacement is ours); 'B:' pieces are ours (new
# instructions, branches, displacements, tables, int3 fill).
#   python spans_t14.py EN_IMAGE FR_IMAGE [T14]        edits and checks
#   python spans_t14.py EN_IMAGE FR_IMAGE [T14] cs     C# table (src/TNPlusSpanInPlace.cs, class SpanInPlace)
import hashlib, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM

NAME = 'spans_t14'
BASE = 0x1AB000
FR_DELTA = 0x280
LO, HI = 0x34D2B0, 0x34D510                     # the module (EN); entries 0x34D2B0, 0x34D2E6, 0x34D390 unchanged
Y0, X0, S0 = 0x34D2B0, 0x34D2E6, 0x34D390
CALLS = ((0x331B8B, Y0), (0x331B10, X0), (0x331AFF, S0))   # its only callers: call rel32 (EN; FR +0x280)
CONTEXT = tuple((s, 5) for s, t in CALLS)       # checked too: still the game's calls to the entries
VARIANT = 'T14r'                                # 'T14r' (default) or 'T14' (the analyst's loop order)
FILL, TFILL = 0xCC, 0xEE                        # unused bytes (int3), table bytes before the first set-up/span
TBL = ('TVm2', 'TVm1', 'TV0', 'TV1', 'TSm2', 'TSm1', 'TS0', 'TS1')
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True
CC = dict(jo=0, jno=1, jb=2, jae=3, je=4, jne=5, jbe=6, ja=7, js=8, jns=9, jp=10, jnp=11, jl=12, jge=13, jle=14, jg=15)

# our own instructions
MOV_EBP_EBX = b'\x89\xdd'                       # mov ebp,ebx
MOV_EBP_ESI = b'\x89\xf5'                       # mov ebp,esi
MOV_W_EDI_CX = b'\x66\x89\x4f'                  # mov word ptr [edi+d8],cx       (+ disp8)
MOVZX_EBP_W8 = b'\x0f\xb7\x68\x08'              # movzx ebp,word ptr [eax+8]
MOV_BP_P38 = b'\x66\x8b\x6e\x38'                # mov bp,word ptr [esi+0x38]
SBB_ESI = b'\x1b\xf6'                           # sbb esi,esi
ADC_ESI = b'\x13\xf6'                           # adc esi,esi
ADD_BP_SF = b'\x66\x81\xc5\xef\xbe'             # add bp,imm16                   (field L.sf at +3)
ADC_EAX_TV = b'\x13\x04\xb5'                    # adc eax,[esi*4+abs32]          (+ abs32 = TV0)
ADD_EBX_TS = b'\x03\x1c\xb5'                    # add ebx,[esi*4+abs32]          (+ abs32 = TS0)
SUB_EBP_DV = b'\x2b\x2d'                        # sub ebp,[abs32]                (+ abs32 = L.dv)


def load(path):
    data = open(path, 'rb').read()
    return lambda a, n: data[a - BASE:a - BASE + n]


def ins1(read, a):
    return next(md.disasm(read(a, 16), a))


class Out:
    """new bytes as pieces: ('C', address, length) copied from the game, ('B', bytes) ours"""
    def __init__(self, read): self.read, self.p = read, []
    def c(self, a, n):
        if self.p and self.p[-1][0] == 'C' and self.p[-1][1] + self.p[-1][2] == a:
            self.p[-1] = ('C', self.p[-1][1], self.p[-1][2] + n)
        else: self.p.append(('C', a, n))
    def b(self, data):
        if not data: return
        if self.p and self.p[-1][0] == 'B': self.p[-1] = ('B', self.p[-1][1] + data)
        else: self.p.append(('B', data))
    def bytes(self):
        return b''.join(self.read(x[1], x[2]) if x[0] == 'C' else x[1] for x in self.p)
    def __iter__(self): return iter(self.p)           # the pieces list itself
    def __len__(self): return len(self.p)


def field_offset(i):
    """offset of the operand the code writes into: the immediate if there is one, else the displacement"""
    return i.imm_offset if any(o.type == X86_OP_IMM for o in i.operands) else i.disp_offset


def layout(read, start, end, items, d, lab=None, base=None, out=None):
    """items: ('C', orig[, field]) instruction copied from the game (field: name of the operand written into it) |
    ('R', orig, field, base) copied store/load whose displacement now points at field (disp = field - value of the
    base register, base None = abs32) | ('I', orig, base) copied 'mov r32,imm32' loading a base register value |
    ('B', bytes[, field, offset]) ours | ('BD', field, base) disp8 of field, ours (after the opcode/modrm of a B item) |
    ('BA', bytes, field) ours + abs32 of field | ('L', name) |
    ('J', mnemonic, label) short branch, ours | ('T', names) 4-byte table entries, ours.
    Without lab: the labels (branch targets, fields, table entries) at this exe's addresses. With lab and base: the
    region's bytes appended to out, unused bytes filled with int3."""
    pos, labels = start + d, {}
    for it in items:
        k = it[0]
        if k == 'L': labels[it[1]] = pos; continue
        if k in ('C', 'R', 'I'):
            i = ins1(read, it[1] + d)
            if k == 'C' and len(it) > 2: labels[it[2]] = pos + field_offset(i)
            n = i.size
        elif k == 'B':
            if len(it) > 2: labels[it[2]] = pos + it[3]
            n = len(it[1])
        elif k == 'BD': n = 1
        elif k == 'BA': n = len(it[1]) + 4
        elif k == 'J': n = 2
        elif k == 'T':
            for j, name in enumerate(it[1]): labels[name] = pos + 4 * j
            n = 4 * len(it[1])
        else: raise ValueError(it)
        if lab is not None:
            if k == 'C':
                assert not i.mnemonic.startswith('j') and i.mnemonic not in ('call', 'loop'), hex(it[1])
                out.c(it[1] + d, n)
            elif k == 'R':
                v = lab[it[2]] - (base[it[3]] if it[3] else 0)
                off, sz = i.disp_offset, i.disp_size
                assert sz in (1, 4) and off > 0, hex(it[1])
                out.c(it[1] + d, off)
                if sz == 1:
                    assert -128 <= v <= 127, (hex(it[1]), it[2], v); out.b(bytes([v & 0xff]))
                else: out.b(struct.pack('<I', v & 0xffffffff))
                if n > off + sz: out.c(it[1] + d + off + sz, n - off - sz)
            elif k == 'I':
                assert 0xb8 <= i.bytes[0] <= 0xbf and n == 5, hex(it[1])
                out.c(it[1] + d, 1); out.b(struct.pack('<I', base[it[2]]))
            elif k == 'B': out.b(it[1])
            elif k == 'BD':
                v = lab[it[1]] - base[it[2]]; assert -128 <= v <= 127, (it, v); out.b(bytes([v & 0xff]))
            elif k == 'BA': out.b(it[1] + struct.pack('<I', lab[it[2]]))
            elif k == 'J':
                r = lab[it[2]] - (pos + 2); assert -128 <= r <= 127, (hex(pos), it, r)
                out.b(bytes([0xeb if it[1] == 'jmp' else 0x70 + CC[it[1]], r & 0xff]))
            elif k == 'T': out.b(bytes([TFILL]) * n)
        pos += n
    assert pos <= end + d, (hex(pos), hex(end + d))
    if lab is not None: out.b(bytes([FILL]) * (end + d - pos))
    return labels


def setup_y_items():
    """0x34D2B0: y-major steps s0 = P / P-1, s1 = P+1 / P (slope >= 0 / < 0), poly+0x10 = P+1 / P-1, then COMMON"""
    return [('C', 0x34d2b0), ('I', 0x34d2b1, 'L'), ('C', 0x34d2b6), ('C', 0x34d2bc), ('C', 0x34d2bf), ('C', 0x34d2c2),
            ('C', 0x34d2c8), ('C', 0x34d2ca), ('J', 'js', 'NY'), ('C', 0x34d2ce), ('B', MOV_EBP_EBX),
            ('J', 'jmp', 'COMMON'),
            ('L', 'NY'), ('C', 0x34d2da), ('B', MOV_EBP_ESI), ('C', 0x34d2ca), ('J', 'jmp', 'COMMON')]


def setup_x_items():
    """0x34D2E6: x-major steps (1, P+1), (P+1, 1) for slope 1.0, (1-P, 1) for slope < 0; COMMON: TS table, slope,
    texture base T, width W, the u/v masks and shifts from lw and H, M = (H << lw) - 1, shade table C"""
    return [('C', 0x34d2e6), ('C', 0x34d2e7), ('I', 0x34d2e9, 'L'), ('C', 0x34d2ee), ('C', 0x34d2ef), ('C', 0x34d2f5),
            ('C', 0x34d2f8), ('C', 0x34d2fb), ('C', 0x34d301), ('J', 'js', 'NX'), ('C', 0x34d305), ('C', 0x34d306),
            ('J', 'je', 'EQ'), ('B', MOV_EBP_EBX), ('J', 'jmp', 'COMMON'),
            ('L', 'NX'), ('C', 0x34d319), ('C', 0x34d31b),
            ('L', 'EQ'), ('B', MOV_EBP_ESI), ('C', 0x34d2ca),
            ('L', 'COMMON'),
            ('R', 0x34d2cf, 'TSm2', 'L'), ('R', 0x34d2d2, 'TS0', 'L'),        # mov [edi+d8],esi    s0
            ('R', 0x34d37f, 'TSm1', 'L'), ('R', 0x34d382, 'TS1', 'L'),        # mov [edi+d8],ebp    s1
            ('B', MOV_W_EDI_CX), ('BD', 'L.sf', 'L'),                         # slope fraction
            ('C', 0x34d328), ('C', 0x34d331), ('R', 0x34d333, 'L.T', 'L'),
            ('I', 0x34d339, 'S'), ('B', MOVZX_EBP_W8), ('R', 0x34d347, 'S.W', 'S'),
            ('C', 0x34d34a), ('R', 0x34d34d, 'E.sar', 'S'),
            ('C', 0x34d350), ('C', 0x34d352), ('C', 0x34d353), ('C', 0x34d355), ('R', 0x34d356, 'E.u', 'S'),
            ('C', 0x34d359), ('C', 0x34d35d), ('C', 0x34d35f), ('R', 0x34d360, 'E.v', 'S'),
            ('C', 0x34d363), ('R', 0x34d365, 'S.and2', 'S'), ('R', 0x34d368, 'S.and1', 'S'),
            ('C', 0x34d36b), ('C', 0x34d36d), ('C', 0x34d36f), ('R', 0x34d372, 'S.sar2', 'S'),
            ('R', 0x34d375, 'S.sar1', 'S'),
            ('C', 0x34d378), ('C', 0x34d379), ('R', 0x34d37f, 'L.M', 'L'), ('C', 0x34d385), ('R', 0x34d388, 'L.C', 'L'),
            ('C', 0x34d38e), ('C', 0x34d38f)]


def loop_items(variant):
    """LA: one pixel; esi = 2*(-v carry) + slope carry from the previous pixel (entry: -v carry)"""
    T, C, D = ('C', 0x34d460, 'L.M'), ('C', 0x34d476, 'L.C'), ('C', 0x34d47c, 'L.D')
    head = [('L', 'LA'), T, ('C', 0x34d465, 'L.du'), ('C', 0x34d46b, 'L.T')]
    TV = ('BA', ADC_EAX_TV, 'TV0')
    mid = [('C', 0x34d482, 'L.dl'), ('C', 0x34d488), ('C', 0x34d49a, 'L.dv'), ('B', SBB_ESI),
           ('B', ADD_BP_SF, 'L.sf', 3), ('B', ADC_ESI)]
    tail = [('BA', ADD_EBX_TS, 'TS0'), ('J', 'jne', 'LA')]
    if variant == 'T14':                       # and, add, T, TV, C, D, ... (4 flag saves per pixel in dyn_x86)
        return head[:1] + [T] + head[2:] + [TV, C, D] + mid + tail
    assert variant == 'T14r'                    # and, add, T, C, TV, ..., D, TS (3)
    return head[:1] + [T] + head[2:] + [C, TV] + mid + [D] + tail


def span_items(variant):
    """0x34D390: per span fields (du, dv, TV = S / S+W, end pointer D, light step dl), u/v/light set-up, the loop,
    the game's epilogue (u, v, light written back to the poly struct), then the TV/TS tables (never executed)"""
    return ([('C', 0x34d390), ('C', 0x34d391), ('I', 0x34d393, 'L'), ('C', 0x34d398), ('C', 0x34d39b), ('C', 0x34d39d),
             ('C', 0x34d3a0), ('R', 0x34d3a2, 'L.du', 'L'), ('C', 0x34d3a8), ('C', 0x34d3ab), ('C', 0x34d3ae),
             ('C', 0x34d3b0), ('R', 0x34d3b3, 'L.dv', 'L'),
             ('C', 0x34d3bc, 'S.sar1'), ('C', 0x34d3bf, 'S.and1'), ('C', 0x34d3c5),
             ('R', 0x34d3c7, 'TV0', 'L'), ('R', 0x34d3c7, 'TV1', 'L'), ('C', 0x34d3ca, 'S.W'),
             ('R', 0x34d3cf, 'TVm2', 'L'), ('R', 0x34d3cf, 'TVm1', 'L'),
             ('C', 0x34d3d2), ('C', 0x34d3d5), ('R', 0x34d3d8, 'L.D', 'L'), ('C', 0x34d3db), ('C', 0x34d3e0),
             ('C', 0x34d3e3), ('C', 0x34d3e6), ('C', 0x34d3e9), ('C', 0x34d3eb), ('C', 0x34d3ee), ('C', 0x34d3f1),
             ('R', 0x34d3f3, 'L.dl', 'L'), ('C', 0x34d3f9), ('C', 0x34d3fc), ('C', 0x34d3fe), ('C', 0x34d401),
             ('C', 0x34d404), ('C', 0x34d406), ('C', 0x34d409), ('C', 0x34d40b), ('C', 0x34d40e, 'S.sar2'),
             ('C', 0x34d411, 'S.and2'), ('C', 0x34d417), ('B', MOV_BP_P38), ('C', 0x34d419), ('C', 0x34d41c),
             ('R', 0x34d423, 'L.dv', None), ('B', SBB_ESI)]
            + loop_items(variant)
            + [('BA', SUB_EBP_DV, 'L.dv'),
               ('C', 0x34d431), ('C', 0x34d432), ('C', 0x34d434, 'E.u'), ('C', 0x34d43a), ('C', 0x34d43e),
               ('C', 0x34d441, 'E.sar'), ('C', 0x34d444, 'E.v'), ('C', 0x34d449), ('C', 0x34d44d), ('C', 0x34d450),
               ('C', 0x34d452), ('C', 0x34d454), ('C', 0x34d458), ('C', 0x34d45b), ('C', 0x34d45c),
               ('T', TBL)])


def regions(variant=None):
    return [(Y0, X0, setup_y_items()), (X0, S0, setup_x_items()), (S0, HI, span_items(variant or VARIANT))]


def plan(read, d=0, variant=None):
    """labels (fields, branch targets, tables) and base register values: 'L' (edi/ecx: loop fields and tables),
    'S' (edx: span fields), each the middle of its fields so every store reaches its field with a disp8"""
    lab = {}
    for s, e, items in regions(variant):
        lab.update(layout(read, s, e, items, d))
    loopf = [lab[n] for n in lab if n[:2] in ('L.', 'TV', 'TS')]
    spanf = [lab[n] for n in lab if n[:2] in ('S.', 'E.')]
    return lab, {'L': (min(loopf) + max(loopf) + 3) // 2, 'S': (min(spanf) + max(spanf) + 3) // 2}


def build(read, d=0, variant=None):
    """[(address, length, Out)] for an exe whose module sits d bytes further (FR 0x280): one edit, the whole module.
    Out iterates over its pieces ('C', address, length) / ('B', bytes); Out.bytes() = the new bytes"""
    for site, tgt in CALLS:                                   # the three callers: call rel32 to the entries
        c = read(site + d, 5)
        assert c[0] == 0xE8 and site + d + 5 + struct.unpack('<i', c[1:])[0] == tgt + d, (hex(site + d), c.hex())
    assert read(Y0 + d + 6, 2) == b'\x8b\x1d' and read(0x34D379 + d, 2) == b'\x8b\x1d'   # the two globals
    lab, base = plan(read, d, variant)
    out = Out(read)
    for s, e, items in regions(variant):
        layout(read, s, e, items, d, lab, base, out)
    assert len(out.bytes()) == HI - LO
    return [(LO + d, HI - LO, out)]


def fields(read, d=0, variant=None):
    """every operand the new code writes into itself: [(name, address, length)] (the dynamic fields, then the
    tables); everything else in the module stays as installed"""
    lab, base = plan(read, d, variant)
    n = {'L.sf': 2, 'S.sar1': 1, 'S.sar2': 1, 'E.sar': 1}
    return sorted(((k, v, n.get(k, 4)) for k, v in lab.items() if k[:2] in ('L.', 'S.', 'E.', 'TV', 'TS')),
                  key=lambda x: x[1])


def checked(edits, d):
    return sorted([(a, n) for a, n, o in edits] + [(a + d, n) for a, n in CONTEXT])


def sha(read, ranges):
    return hashlib.sha256(b''.join(read(a, n) for a, n in ranges)).hexdigest()


def on_mask(read, edits, d, variant=None):
    """'already on' test (TN+ attached again after the install): offsets in the edit of the bytes the new code
    rewrites at run time, as [offset, length, ...]; on_sha() hashes the edit with them zeroed + the call sites"""
    a0 = edits[0][0]
    return [x for k, a, n in fields(read, d, variant) for x in (a - a0, n)]


def on_sha(read, edits, d, variant=None, cur=None):
    """SHA-256 of the installed module with the run-time fields zeroed, then the three call sites; cur = a read
    function of the memory to test (default: the module as build() makes it)"""
    a0, n0, o = edits[0]
    m = bytearray(cur(a0, n0) if cur else o.bytes())
    msk = on_mask(read, edits, d, variant)
    for i in range(0, len(msk), 2):
        m[msk[i]:msk[i] + msk[i + 1]] = bytes(msk[i + 1])
    r = cur or read
    return hashlib.sha256(bytes(m) + b''.join(r(a + d, n) for a, n in sorted(CONTEXT))).hexdigest()


ORIG_END = 0x34D507                             # the original's last instruction (jmp) ends here, zero padding after
PLACEHOLDER = 0xDEADBEEF


def ran_fields(read, d=0):
    """the ORIGINAL module's run-time fields, [(address, length)]: the operands its set-ups and span write into, i.e.
    its 27 imm32/disp32 holding 0xDEADBEEF and the imm8 0xFF of its three 'sar r32,0xFF', as shipped (found here from
    the placeholders of the pristine module; test_static.py checks them against the original's own stores and
    test_eq.py against what the original changes when it runs)"""
    lo, end, out, pos = LO + d, ORIG_END + d, [], LO + d
    for i in md.disasm(read(lo, end - lo), lo):
        b = bytes(i.bytes)
        if i.imm_size == 4 and struct.unpack_from('<I', b, i.imm_offset)[0] == PLACEHOLDER:
            out.append((i.address + i.imm_offset, 4))
        elif i.disp_size == 4 and struct.unpack_from('<I', b, i.disp_offset)[0] == PLACEHOLDER:
            out.append((i.address + i.disp_offset, 4))
        elif b[0] == 0xC1 and i.imm_size == 1 and b[i.imm_offset] == 0xFF:
            out.append((i.address + i.imm_offset, 1))
        pos = i.address + i.size
    assert pos == end and read(end, HI + d - end) == bytes(HI + d - end), 'original module layout'
    assert len(out) == 30 and sum(n for a, n in out) == 111, len(out)
    return out


def ran_mask(read, d=0):
    """[offset, length, ...] in the module (from LO + d) of the original's run-time fields"""
    return [x for a, n in ran_fields(read, d) for x in (a - LO - d, n)]


def ran_sha(read, d=0, cur=None):
    """SHA-256 of the module with the ORIGINAL's run-time fields zeroed, then the three call sites (the order of
    on_sha): the game's own module whatever its fields hold, i.e. pristine or after it ran; cur = a read function of
    the memory to test (default: read, the pristine image)"""
    r = cur or read
    m = bytearray(r(LO + d, HI - LO))
    msk = ran_mask(read, d)
    for i in range(0, len(msk), 2):
        m[msk[i]:msk[i] + msk[i + 1]] = bytes(msk[i + 1])
    return hashlib.sha256(bytes(m) + b''.join(r(a + d, n) for a, n in sorted(CONTEXT))).hexdigest()


def tables(read, d, variant=None):
    """one language's row of the C# table: (edits, checked ranges, sha, on mask, on sha, ran mask, ran sha)"""
    e = build(read, d, variant)
    ck = checked(e, d)
    return (e, ck, sha(read, ck), on_mask(read, e, d, variant), on_sha(read, e, d, variant), ran_mask(read, d),
            ran_sha(read, d))


def new_image(img_read, edits, lo=0x330000, hi=0x350000):
    """for tests: the code range [lo, hi) with the edits applied, built from the pieces"""
    m = bytearray(img_read(lo, hi - lo))
    news = [(a, o.bytes()) for a, n, o in edits]            # every C piece read before anything is written
    for a, b in news: m[a - lo:a - lo + len(b)] = b
    return lo, bytes(m)


def export_cs(per, cls='SpanInPlace', tool='spans_t14', what='Normal-view textured spans fixed in place'):
    """C# table for TNPlus.cs TryInPlace() and TNPlusSpanFix.cs; per = [tables(read, d)] for EN then FR:
    (edits, checked ranges, sha, on mask, on sha, ran mask, ran sha)"""
    print('// Generated by tools/%s.py (python %s.py EN_IMAGE FR_IMAGE cs > ../src/TNPlus%s.cs): do not edit.' % (tool, tool, cls))
    print('// %s: pieces C:address:length are copied from the game\'s own memory BEFORE' % what)
    print('// anything is written, B: pieces are ours. Check: SHA-256 of the bytes at Check (pristine code). See tools/%s.py.' % tool)
    print('// OnMask/OnSha: the module once installed (offsets/lengths in At[l][0] of the fields it rewrites, zeroed before')
    print('// hashing, then the three call sites at OnCallAt): already on when TN+ attaches again.')
    print('// RanMask/RanSha: the same with the fields the GAME\'s module rewrites: the original module whatever its fields')
    print('// hold (pristine, or after it ran: then Sha no longer matches).')
    print('static class %s\n{' % cls)
    q = lambda xs: ', '.join(xs)
    print('    public static readonly uint[][] At = { %s };' % q('new uint[] { %s }' % q('0x%X' % a for a, n, o in t[0]) for t in per))
    print('    public static readonly int[][] Size = { %s };' % q('new int[] { %s }' % q(str(n) for a, n, o in t[0]) for t in per))
    print('    public static readonly uint[][] CheckAt = { %s };' % q('new uint[] { %s }' % q('0x%X' % a for a, n in t[1]) for t in per))
    print('    public static readonly int[][] CheckSize = { %s };' % q('new int[] { %s }' % q(str(n) for a, n in t[1]) for t in per))
    print('    public static readonly string[] Sha = { %s };' % q('"%s"' % t[2] for t in per))
    print('    public static readonly uint[][] OnCallAt = { %s };' % q('new uint[] { %s }' % q('0x%X' % (a + d) for a, n in sorted(CONTEXT)) for d in (0, FR_DELTA)))
    print('    public static readonly int[][] OnMask = { %s };' % q('new int[] { %s }' % q(str(x) for x in t[3]) for t in per))
    print('    public static readonly string[] OnSha = { %s };' % q('"%s"' % t[4] for t in per))
    print('    public static readonly int[][] RanMask = { %s };' % q('new int[] { %s }' % q(str(x) for x in t[5]) for t in per))
    print('    public static readonly string[] RanSha = { %s };' % q('"%s"' % t[6] for t in per))
    print('    public static readonly string[][][] Pieces = {')
    for t in per:
        e = t[0]
        print('        new string[][] {')
        for a, n, o in e:
            ps = ['"C:%X:%X"' % (x[1], x[2]) if x[0] == 'C' else '"B:%s"' % x[1].hex().upper() for x in o.p]
            print('            new string[] { %s },' % ', '.join(ps))
        print('        },')
    print('    };\n}')


if __name__ == '__main__':
    if 'T14' in sys.argv[3:]: VARIANT = 'T14'
    en, fr = load(sys.argv[1]), load(sys.argv[2])
    per = [tables(read, d) for read, d in ((en, 0), (fr, FR_DELTA))]
    E, F = per[0][0], per[1][0]
    assert len(E) == len(F) and all(x[0] + FR_DELTA == y[0] and x[1] == y[1] for x, y in zip(E, F)), 'FR shape'
    if 'cs' in sys.argv: export_cs(per)
    else:
        for l, (e, ck, s, m, os_, rm, rs) in zip(('EN', 'FR'), per):
            print(l, '%s %s: %d edit, %d bytes, %d copied (C), %d ours (B), sha %s, on-sha %s, %d run-time bytes; '
                  'ran-sha %s, %d run-time bytes of the original' % (
                      NAME, VARIANT, len(e), sum(n for a, n, o in e), sum(x[2] for a, n, o in e for x in o.p if x[0] == 'C'),
                      sum(len(x[1]) for a, n, o in e for x in o.p if x[0] == 'B'), s[:16], os_[:16], sum(m[1::2]),
                      rs[:16], sum(rm[1::2])))
            for a, n, o in e: print('   %#x %3d  %s' % (a, n, ' '.join(('C%X+%d' % (x[1], x[2])) if x[0] == 'C' else x[1].hex() for x in o.p)[:300]))
