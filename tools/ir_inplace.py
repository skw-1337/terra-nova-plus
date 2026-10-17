# Infrared view fixed IN PLACE (replaces ir_fix.py's rebuilt copies when the routines are still as loaded).
# Of all the operands the IR routines write into their own loops, DOSBox's dynamic core reads nearly all from memory
# once rewritten (imm32 of 81/05, [reg+disp32]); only six force a new translation each time they change: the span
# loop's four colour-table lookups 'mov cl,[ecx+disp8]' (written per polygon) and the affine rows' two
# 'adc eax,imm32' in their short form 15 (written per row). Here they become 'mov cl,[ecx+disp32]' and '81 D0 imm32',
# the span loop re-laid to make room (two flag-only fillers dropped, A1/B1 jump straight to C2/D2 instead of through a
# jae that is never taken there), the affine fillers re-encoded shorter, every writer of a moved field retargeted.
# The game's own fast loops stay: 12 guest instructions per span pixel instead of 22 in the rebuilt copy, no
# push/pop around each access. Same pixels (Unicorn, perf study: random spans and rows, sequences of set-ups and
# spans, EN and FR: 0 difference).
# Only while the code never ran: the old bytes (with the 0xDEADBEEF slot placeholders) and the two entries without
# ir_fix's jmp are checked through a SHA-256; otherwise TN+ falls back to ir_fix. TN+ ships no game code: moved
# instructions are pieces 'C:address:length' copied from the player's memory before anything is written.
#   python ir_inplace.py EN_IMAGE FR_IMAGE          edits and checks
#   python ir_inplace.py EN_IMAGE FR_IMAGE cs       C# table (src/TNPlusIrInPlace.cs)
import hashlib, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_MEM

BASE = 0x1AB000
FR_DELTA = 0x280
CONTEXT = ((0x34E223, 6), (0x3311F0, 6))        # the two entries: still the game's (ir_fix not installed)
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True
CC = dict(jo=0, jno=1, jb=2, jae=3, je=4, jne=5, jbe=6, ja=7, js=8, jns=9, jp=10, jnp=11, jl=12, jge=13, jle=14, jg=15)


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


def layout(read, start, end, items, d):
    """items: ('C', orig) copied instruction | ('LK', orig) mov cl,[ecx+d8] -> 8A 89 disp32 | ('ADC', orig) 15 imm32
    -> 81 D0 imm32 | ('B', bytes) | ('L', name) | ('J', mnemonic, label or original address) short branch |
    ('FAR', original target) jmp rel32. Returns (Out, original byte address -> new address, labels)."""
    pos, lab, plan = start + d, {}, []
    for it in items:
        if it[0] == 'L': lab[it[1]] = pos; continue
        n = {'C': lambda: ins1(read, it[1] + d).size, 'LK': lambda: 6, 'ADC': lambda: 6, 'B': lambda: len(it[1]),
             'J': lambda: 2, 'FAR': lambda: 5}[it[0]]()
        plan.append((pos, it)); pos += n
    assert pos <= end + d, (hex(pos), hex(end + d))
    out, moved = Out(read), {}
    for p, it in plan:
        if it[0] == 'C':
            i = ins1(read, it[1] + d)
            assert not i.mnemonic.startswith('j')
            out.c(it[1] + d, i.size)
            for k in range(i.size): moved[it[1] + d + k] = p + k
        elif it[0] == 'LK':
            b0 = bytes(ins1(read, it[1] + d).bytes)
            assert b0[:2] == b'\x8a\x49', b0.hex()
            out.b(b'\x8a\x89' + struct.pack('<i', struct.unpack('b', b0[2:3])[0]))
            moved[it[1] + d + 2] = p + 2
        elif it[0] == 'ADC':
            b0 = bytes(ins1(read, it[1] + d).bytes)
            assert b0[0] == 0x15, b0.hex()
            out.b(b'\x81\xd0'); out.c(it[1] + d + 1, 4)
            for k in range(4): moved[it[1] + d + 1 + k] = p + 2 + k
        elif it[0] == 'B': out.b(it[1])
        elif it[0] == 'FAR': out.b(b'\xe9' + struct.pack('<i', it[1] + d - (p + 5)))
        elif it[0] == 'J':
            t = lab[it[2]] if isinstance(it[2], str) else it[2] + d
            r = t - (p + 2); assert -128 <= r <= 127, (hex(p), it, r)
            out.b(bytes([0xeb if it[1] == 'jmp' else 0x70 + CC[it[1]], r & 0xff]))
    out.b(b'\x90' * ((end + d) - pos))
    return out, moved, lab


def span_items():
    A = [('L', 'A1'), ('C', 0x34e310), ('C', 0x34e316), ('C', 0x34e31c), ('J', 'jb', 'C2'),
         ('L', 'A2'), ('C', 0x34e324), ('C', 0x34e32a), ('LK', 0x34e330), ('C', 0x34e333), ('C', 0x34e339),
         ('J', 'je', 0x34e2e2), ('C', 0x34e340), ('J', 'jae', 'A1')]
    B = [('L', 'B1'), ('C', 0x34e350), ('C', 0x34e356), ('C', 0x34e35c), ('J', 'jb', 'D2'),
         ('L', 'B2'), ('C', 0x34e364), ('C', 0x34e36a), ('LK', 0x34e370), ('C', 0x34e373), ('C', 0x34e379),
         ('J', 'je', 'TRAMP'), ('C', 0x34e380), ('J', 'jae', 'A1'), ('J', 'jmp', 'B1'),
         ('L', 'TRAMP'), ('FAR', 0x34e2e2)]
    C = [('L', 'C1'), ('C', 0x34e390), ('C', 0x34e396), ('C', 0x34e39c), ('J', 'jae', 'A2'),
         ('L', 'C2'), ('C', 0x34e3a4), ('C', 0x34e3aa), ('LK', 0x34e3b0), ('C', 0x34e3b3), ('C', 0x34e3b9),
         ('J', 'je', 'TRAMP'), ('C', 0x34e3c0), ('J', 'jae', 'C1')]
    D = [('L', 'D1'), ('C', 0x34e3d0), ('C', 0x34e3d6), ('C', 0x34e3dc), ('J', 'jae', 'B2'),
         ('L', 'D2'), ('C', 0x34e3e4), ('C', 0x34e3ea), ('LK', 0x34e3f0), ('C', 0x34e3f3), ('C', 0x34e3f9),
         ('J', 'je', 'TRAMP'), ('C', 0x34e400), ('J', 'jae', 'C1'), ('J', 'jmp', 'D1')]
    return A + B + C + D


def affine_items():
    return [('J', 'jb', 'B'), ('C', 0x331266), ('B', b'\x8d\x74\x26\x00'),
            ('L', 'A'), ('C', 0x331270), ('C', 0x331275), ('C', 0x331277), ('ADC', 0x33127d), ('C', 0x331282),
            ('C', 0x331284), ('C', 0x33128a), ('C', 0x331290), ('J', 'ja', 'A'), ('J', 'je', 0x3312e0),
            ('B', b'\x3e\x83\xe0\xff' + b'\x81\xe2\x00\xff\xff\xff'),
            ('L', 'B'), ('C', 0x3312a0), ('C', 0x3312a5), ('C', 0x3312a7), ('ADC', 0x3312ad), ('C', 0x3312b2),
            ('C', 0x3312b4), ('C', 0x3312ba), ('C', 0x3312c0), ('J', 'ja', 'A'), ('J', 'jne', 'B'), ('J', 'jmp', 0x3312e0)]


def retarget(read, a, moved, base_val):
    """an instruction at a reading or writing a field that moved ([reg+disp], base_val = the register's value, or
    [abs]): the same instruction with the displacement following the field. None if it did not move."""
    i = ins1(read, a)
    m = [o for o in i.operands if o.type == X86_OP_MEM][0]
    tgt = (base_val + m.mem.disp) & 0xffffffff
    if tgt not in moved or moved[tgt] == tgt: return None
    nd = m.mem.disp + (moved[tgt] - tgt)
    off, sz = i.disp_offset, i.disp_size
    out = Out(read)
    out.c(a, off)
    if sz == 1:
        assert -128 <= nd <= 127; out.b(bytes([nd & 0xff]))
    else: out.b(struct.pack('<i', nd))
    if i.size > off + sz: out.c(a + off + sz, i.size - off - sz)
    return (a, i.size, out)


SPAN_WRITERS = [0x34e16f, 0x34e172, 0x34e178, 0x34e17b, 0x34e181, 0x34e184, 0x34e187, 0x34e18a, 0x34e195, 0x34e19b,
                0x34e19e, 0x34e1a1, 0x34e212, 0x34e218, 0x34e21b, 0x34e21e,                      # set-up, edi=0x34E38A
                0x34e23d, 0x34e240, 0x34e253, 0x34e256, 0x34e261, 0x34e266, 0x34e271, 0x34e274, 0x34e284, 0x34e287,
                0x34e28f, 0x34e294, 0x34e29d, 0x34e2a2, 0x34e2a5, 0x34e2ab]                      # entry, ecx=0x34E38A
AFF_WRITERS = (0x331179, 0x33117e, 0x3311a1, 0x3311a6, 0x3311b5, 0x3311b8, 0x331198, 0x3311ae, 0x3311b1, 0x33118c)
AFF_ABS = (0x33122e, 0x33123a, 0x33136e, 0x33137c, 0x331017, 0x331310, 0x331011, 0x331309, 0x3311f3, 0x3312e4)


def build(read, d=0):
    """[(address, length, Out)] for an exe whose routines sit d bytes further (FR 0x280)"""
    edits = []
    out, moved, lab = layout(read, 0x34e310, 0x34e40a, span_items(), d)
    assert lab['A1'] == 0x34e310 + d
    edits.append((0x34e310 + d, 0x34e40a - 0x34e310, out))
    base = 0x34e38a + d
    for a in SPAN_WRITERS:
        e = retarget(read, a + d, moved, base)
        if e: edits.append(e)
    e = retarget(read, 0x34e2cf + d, moved, 0)                  # add edi,[0x34E342] reads the A dz field
    assert e; edits.append(e)
    old = read(0x34e1b8 + d, 15)                                # set-up: the 4 lookups get sx(T & 0xFF) as a dword
    assert old[:3] == b'\x88\x4f\xa8' and old[9:11] == b'\x88\x8f', old.hex()
    o = Out(read); o.b(b'\x0f\xbe\xd9')                         # movsx ebx,cl
    for olda in (0x34e332, 0x34e3b2, 0x34e372, 0x34e3f2):
        nd = moved[olda + d] - base; assert -128 <= nd <= 127
        o.b(b'\x89\x5f' + bytes([nd & 0xff]))                   # mov [edi+d8],ebx
    edits.append((0x34e1b8 + d, 15, o))
    i = ins1(read, 0x34e2da + d); assert bytes(i.bytes[:2]) == b'\x0f\x82'
    o = Out(read); o.b(b'\x0f\x82' + struct.pack('<i', lab['B1'] - (0x34e2da + d + 6)))
    edits.append((0x34e2da + d, 6, o))                          # entry: jb -> the new B1
    out, moved, lab = layout(read, 0x331264, 0x3312c7, affine_items(), d)
    assert lab['B'] + 0xf == 0x3312ae + d and lab['A'] + 0xf == 0x33127e + d
    edits.append((0x331264 + d, 0x3312c7 - 0x331264, out))
    for a in AFF_WRITERS:
        e = retarget(read, a + d, moved, 0x331270 + d)
        if e: edits.append(e)
    for a in AFF_ABS:
        e = retarget(read, a + d, moved, 0)
        if e: edits.append(e)
    return edits


def checked(edits, d):
    return sorted([(a, n) for a, n, o in edits] + [(a + d, n) for a, n in CONTEXT])


def sha(read, ranges):
    return hashlib.sha256(b''.join(read(a, n) for a, n in ranges)).hexdigest()


def new_image(img_read, edits, lo=0x330000, hi=0x350000):
    """for tests: the code range [lo, hi) with the edits applied, built from the pieces"""
    m = bytearray(img_read(lo, hi - lo))
    news = [(a, o.bytes()) for a, n, o in edits]            # every C piece read before anything is written
    for a, b in news: m[a - lo:a - lo + len(b)] = b
    return lo, bytes(m)


def export_cs(per, cls='IrInPlace', tool='ir_inplace', what='Infrared routines fixed in place'):
    """C# table for TNPlus.cs TryInPlace(); per = [(edits, checked ranges, sha)] for EN then FR"""
    print('// Generated by tools/%s.py (python %s.py EN_IMAGE FR_IMAGE cs > ../src/TNPlus%s.cs): do not edit.' % (tool, tool, cls))
    print('// %s: pieces C:address:length are copied from the game\'s own memory BEFORE' % what)
    print('// anything is written, B: pieces are ours. Check: SHA-256 of the bytes at Check (pristine code). See tools/%s.py.' % tool)
    print('static class %s\n{' % cls)
    q = lambda xs: ', '.join(xs)
    print('    public static readonly uint[][] At = { %s };' % q('new uint[] { %s }' % q('0x%X' % a for a, n, o in e) for e, ck, s in per))
    print('    public static readonly int[][] Size = { %s };' % q('new int[] { %s }' % q(str(n) for a, n, o in e) for e, ck, s in per))
    print('    public static readonly uint[][] CheckAt = { %s };' % q('new uint[] { %s }' % q('0x%X' % a for a, n in ck) for e, ck, s in per))
    print('    public static readonly int[][] CheckSize = { %s };' % q('new int[] { %s }' % q(str(n) for a, n in ck) for e, ck, s in per))
    print('    public static readonly string[] Sha = { %s };' % q('"%s"' % s for e, ck, s in per))
    print('    public static readonly string[][][] Pieces = {')
    for e, ck, s in per:
        print('        new string[][] {')
        for a, n, o in e:
            ps = ['"C:%X:%X"' % (x[1], x[2]) if x[0] == 'C' else '"B:%s"' % x[1].hex().upper() for x in o.p]
            print('            new string[] { %s },' % ', '.join(ps))
        print('        },')
    print('    };\n}')


if __name__ == '__main__':
    en, fr = load(sys.argv[1]), load(sys.argv[2])
    per = []
    for read, d in ((en, 0), (fr, FR_DELTA)):
        e = build(read, d)
        ck = checked(e, d)
        per.append((e, ck, sha(read, ck)))
    E, F = per[0][0], per[1][0]
    assert len(E) == len(F) and all(x[0] + FR_DELTA == y[0] and x[1] == y[1] for x, y in zip(E, F)), 'FR shape'
    if 'cs' in sys.argv: export_cs(per)
    else:
        for l, (e, ck, s) in zip(('EN', 'FR'), per):
            print(l, '%d edits, %d bytes, %d copied, sha %s' % (len(e), sum(n for a, n, o in e),
                  sum(x[2] for a, n, o in e for x in o.p if x[0] == 'C'), s[:16]))
            for a, n, o in e: print('   %#x %3d  %s' % (a, n, ' '.join(('C%X+%d' % (x[1], x[2])) if x[0] == 'C' else x[1].hex() for x in o.p)[:150]))
