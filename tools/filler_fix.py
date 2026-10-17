# Textured polygon filler of the 3D library (EN 0x32D8E0-0x32E2D6) without the self-modifying code DOSBox can't keep.
# DOSBox Staging's dynamic core (dyn_x86) copes with most of this filler's self-modifying code: immediates of
# mov/add/and/81/C1 and [reg+disp32] displacements are read from memory once they have been rewritten. Two habits
# it can't: rewritten opcode bytes, which after four rewrites run through the interpreter every time.
#  - per pixel: the line set-up writes a dword over the instruction at 0x32E150, either "mov al,[ebx+eax+table]"
#    (8A 84 03 + table at 0x32E153) or "mov al,dl" + a dummy test (8A C2 A9 00). Profiled on Panama: 3.9 % of the
#    game's time on that one instruction. The six writes now go to two data words (STATE, TABLE) and the pixel loop
#    jumps (at 0x32E14C, "cmp al,10h / je") to a cave that does the same from those words.
#  - per edge: both edge set-ups (0x32D8E0, 0x32DB40) round three steps by patching two inc/dec eax each (40h/48h
#    after "cmp ax,8000h"); about 2 % of the time. Each block is rewritten in place: the rounding tests the same word
#    again ([edi+k], still in memory) and does dec eax, then inc eax twice if it was below 8000h.
# Flags differ only in CF after the rounding, which the next instructions overwrite (imul / sub; popad / ret at the
# end of an edge set-up). French exe: everything +0x280, checked instruction by instruction.
# These routines first run in a mission, so TN+ patches them at the main menu, while they are still as in the exe
# (the old bytes include their 0xDEADBEEF slots): DOSBox has translated nothing of them yet. Not done: the
# transparent texture filler (0x3316A0, adc eax,imm32 rewritten per line) already runs in the menus; patched from
# outside, its old translated writers would go on writing into the new code.
#   python filler_fix.py EN_IMAGE FR_IMAGE          patches and checks (images: RAM 0x1AB000-0x470000 at the main menu)
#   python filler_fix.py EN_IMAGE FR_IMAGE cs       C# table (src/TNPlusFiller.cs)
import re, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from keystone import Ks, KS_ARCH_X86, KS_MODE_32
import ir_fix as F

FR_DELTA = 0x280
CAVE = 0x469F60                 # free gap of the tool's code area (after the normal view fix stubs)
STATE, TABLE = 0x4693F8, 0x4693FC
HOOK = 0x32E14C                 # cmp al,10h / je 0x32E16A / first byte of the rewritten instruction (8A either way)
WRITERS = (0x32DE76, 0x32DE80, 0x32DE88, 0x32E1E3, 0x32E1ED, 0x32E1F5)
EDGES = ((0x32D9CC, 0x32DA11), (0x32DA61, 0x32DAA6), (0x32DAF3, 0x32DB38),
         (0x32DC2A, 0x32DC6F), (0x32DCC1, 0x32DD06), (0x32DD53, 0x32DD98))
ks = lambda src, at: bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm(src, at)[0])
md = Cs(CS_ARCH_X86, CS_MODE_32)


def dis(read, a, b):
    return list(md.disasm(read(a, b - a), a))


def edge_block(read, s, e):
    """cmp ax,8000h / jb / 2x mov byte [B|A],48h / jmp X / 2x mov byte [B|A],40h / jmp X /
       X: part1 B: inc eax part2 (starts with a store, then mov eax,[edi+k]) A: inc eax part3 -> fixed code"""
    ins = dis(read, s, e)
    t = ['%s %s' % (i.mnemonic, i.op_str) for i in ins]
    assert t[0] == 'cmp ax, 0x8000' and t[1].startswith('jb ') and t[4].startswith('jmp '), t[:5]
    bs = sorted(int(re.search(r'\[(0x[0-9a-f]+)\]', t[k]).group(1), 16) for k in (2, 3))
    assert t[2].endswith(', 0x48') and t[3].endswith(', 0x48') and t[5].endswith(', 0x40') and t[6].endswith(', 0x40')
    x = int(t[4].split()[1], 16)
    B, A = bs
    body = [i for i in ins if i.address >= x]
    p1 = [i for i in body if i.address < B]
    p2 = [i for i in body if B < i.address < A]
    p3 = [i for i in body if i.address > A]
    assert read(B, 1) in (b'\x40', b'\x48') and read(A, 1) in (b'\x40', b'\x48')
    m = re.fullmatch(r'mov eax, dword ptr \[edi \+ (0x[0-9a-f]+|\d+)\]', '%s %s' % (p2[1].mnemonic, p2[1].op_str))
    assert m, (p2[1].mnemonic, p2[1].op_str)
    k = int(m.group(1), 0)
    adj = lambda n: 'cmp word ptr [edi + %#x], 0x8000; dec eax; jae r%d; inc eax; inc eax; r%d:' % (k, n, n)
    src = '; '.join(['%s %s' % (i.mnemonic, i.op_str) for i in p1] + [adj(1)] +
                    ['%s %s' % (i.mnemonic, i.op_str) for i in p2] + [adj(2)] +
                    ['%s %s' % (i.mnemonic, i.op_str) for i in p3])
    code = ks(src, s)
    if s + len(code) < e: code += ks('jmp %#x' % e, s + len(code))
    assert len(code) <= e - s, (hex(s), len(code), e - s)
    return code + b'\xcc' * (e - s - len(code))


def build(read, d):
    """[(address, old bytes, new bytes)] in the order to apply: cave, the writers, the pixel hook, the edges"""
    out = []
    assert read(0x32E153 + d, 4) == bytes.fromhex('efbeadde'), 'image taken after the filler ran'
    cave = ks('''
        cmp al, 0x10
        je %#x
        cmp byte ptr [%#x], 0x84
        jne nolookup
        add eax, ebx
        add eax, dword ptr [%#x]
        mov al, byte ptr [eax]
        jmp %#x
    nolookup:
        mov al, dl
        jmp %#x''' % (0x32E16A + d, STATE + 1, TABLE, 0x32E157 + d, 0x32E157 + d), CAVE)
    out.append((CAVE, b'\0' * len(cave), cave))
    for w in WRITERS:
        a = w + d; i = dis(read, a, a + 10)[0]
        old = bytes(i.bytes)
        tgt = struct.unpack_from('<I', old, 2)[0]
        assert old[:2] in (b'\xc7\x05', b'\x89\x15') and tgt in (0x32E150 + d, 0x32E153 + d), (hex(a), old.hex())
        new = old[:2] + struct.pack('<I', STATE if tgt == 0x32E150 + d else TABLE) + old[6:]
        out.append((a, old, new))
    h = HOOK + d
    old = read(h, 5); assert old == b'\x3c\x10\x74\x1a\x8a', old.hex()
    out.append((h, old, ks('jmp %#x' % CAVE, h)))
    for s, e in EDGES:
        old = read(s + d, e - s)
        out.append((s + d, old, edge_block(read, s + d, e + d)))
    # nothing jumps into a rewritten range from outside it (the routines, decoded whole)
    spans = [(h, h + 11)] + [(s + d, e + d) for s, e in EDGES]
    for lo, hi in ((0x32D8E0, 0x32DB3A), (0x32DB40, 0x32DD9A), (0x32DE50, 0x32E2D6)):
        for i in F.disasm(read, lo + d, hi + d)[1]:
            if i.mnemonic.startswith('j') and i.op_str.startswith('0x'):
                t = int(i.op_str, 16)
                for a, b in spans:
                    assert not (a < t < b) or a <= i.address < b, (hex(i.address), hex(t))
    return out


def export_cs(en, fr):
    print('// Generated by tools/filler_fix.py (python filler_fix.py EN_IMAGE FR_IMAGE cs > ../src/TNPlusFiller.cs): do not edit.')
    print('// Polygon filler without rewritten opcodes: a cave for the pixel loop, its writers redirected, the edge')
    print('// roundings rewritten in place. See tools/filler_fix.py.')
    print('static class FillerFix\n{')
    q = lambda xs: ', '.join(xs)
    print('    public static readonly uint[][] At = {')
    for o in (en, fr): print('        new uint[] { %s },' % q('0x%X' % a for a, _, _ in o))
    print('    };')
    for name, k in (('Old', 1), ('New', 2)):
        print('    public static readonly string[][] %s = {' % name)
        for o in (en, fr):
            print('        new string[] {')
            for p in o: print('            "%s",' % p[k].hex().upper())
            print('        },')
        print('    };')
    print('}')


if __name__ == '__main__':
    en, fr = F.load(sys.argv[1]), F.load(sys.argv[2])
    for lo, hi in ((0x32D8E0, 0x32DB3A), (0x32DB40, 0x32DD9A), (0x32DE50, 0x32E2D6)):
        F.same_shape(en, fr, lo, hi, FR_DELTA, ())
    E, R = build(en, 0), build(fr, FR_DELTA)
    if 'cs' in sys.argv: export_cs(E, R)
    else:
        for (a, o, n), (b, p, m) in zip(E, R):
            print('%#x / %#x  %2d bytes  %s -> %s' % (a, b, len(n), o.hex()[:24], n.hex()[:48]))
