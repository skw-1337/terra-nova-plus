# Infrared view without self-modifying loops, after TNova Native (Callemann, engine/features/terrain_raster.py, MIT).
# In infrared the 3D library draws the terrain with two routines (EN 0x34E223 spans, 0x3311F0 affine rows) whose
# set-up writes texture steps, masks and table addresses INTO their instructions, many times per frame: DOSBox's
# dynamic core then translates them again and again (IR: 62 -> 35 fps here, uneven when turning fast). Callemann's
# edits read those operand slots as data instead; the rest of each routine is the game's own code.
# TN+ ships no game code: at run time it copies the game's instructions from the player's own memory (pieces 'C'),
# inserts the edits (pieces 'B', our bytes, branches re-encoded) and checks the copied bytes against a SHA-256.
# The new block lives in a buffer the HD code allocates (XCODE); until then a 6-byte jmp [PTR] at each entry goes to
# a stub that runs the original routine. French exe: both routines +0x280, checked instruction by instruction.
#   python ir_fix.py EN_IMAGE FR_IMAGE          layout and checks (images: RAM 0x1AB000-0x470000 at the main menu)
#   python ir_fix.py EN_IMAGE FR_IMAGE cs       C# table (src/TNPlusIr.cs)
import hashlib, re, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

BASE = 0x1AB000                 # memory images start here
FR_DELTA = 0x280
ENTRY = {'span': 0x34E223, 'affine': 0x3311F0}
STUB = {'span': 0x46A0E0, 'affine': 0x4650E8}      # free gaps of the tool's code area
PTR = {'span': 0x4693A8, 'affine': 0x4693AC}        # data page: where each entry jumps
XCODE = 0x466228                                    # HD variable: buffer for the new routines (hd_mod.py)
CC = dict(jo=0, jno=1, jb=2, jae=3, je=4, jne=5, jbe=6, ja=7, js=8, jns=9, jp=10, jnp=11, jl=12, jge=13, jle=14, jg=15)

RECIPES = {   # EN addresses: low, high, SHA-256 of the original bytes, edits {start: (stop, replacement)}
    'span': (0x34e223, 0x34e40a, 'cbd8b18738d6ae7dfb0d759e873b3edbdac83178cc8f6428973ff408fea9d9f0', {
        0x34e22b: (0x34e235, 'mov ebp, dword ptr [0x34e22c];mov ebx, dword ptr [0x34e231];'),
        0x34e246: (0x34e249, 'push ecx;mov cl,[0x34e248];sar eax,cl;pop ecx;'),
        0x34e277: (0x34e27a, 'push ecx;mov cl,[0x34e279];sar eax,cl;pop ecx;'),
        0x34e2bc: (0x34e2c4, 'push ecx;mov cl,[0x34e2be];sar eax,cl;pop ecx;and eax, dword ptr [0x34e2c0];'),
        0x34e2d5: (0x34e2da, 'mov ecx, dword ptr [0x34e2d6];'),
        0x34e2e4: (0x34e2ea, 'and ebx, dword ptr [0x34e2e6];'),
        0x34e2f1: (0x34e2f9, 'push ecx;mov cl,[0x34e2f3];sar eax,cl;pop ecx;and eax, dword ptr [0x34e2f5];'),
        0x34e310: (0x34e322, 'and ebx, dword ptr [0x34e312];add ebp, dword ptr [0x34e318];push esi;mov esi,dword ptr [0x34e31e];mov cl, byte ptr [ebx+esi];pop esi;'),
        0x34e324: (0x34e33e, 'add edx, dword ptr [0x34e326];adc ebx, dword ptr [0x34e32c];push esi;movsx esi,byte ptr [0x34e332];mov cl, byte ptr [ecx+esi];pop esi;push esi;mov esi,dword ptr [0x34e335];mov byte ptr [eax+esi], cl;pop esi;add eax, dword ptr [0x34e33a];'),
        0x34e340: (0x34e346, 'add edi, dword ptr [0x34e342];'),
        0x34e348: (0x34e34e, 'test edx, dword ptr [0x34e34a];'),
        0x34e350: (0x34e362, 'and ebx, dword ptr [0x34e352];add ebp, dword ptr [0x34e358];push esi;mov esi,dword ptr [0x34e35e];mov cl, byte ptr [ebx+esi];pop esi;'),
        0x34e364: (0x34e37e, 'add edx, dword ptr [0x34e366];adc ebx, dword ptr [0x34e36c];push esi;movsx esi,byte ptr [0x34e372];mov cl, byte ptr [ecx+esi];pop esi;push esi;mov esi,dword ptr [0x34e375];mov byte ptr [eax+esi], cl;pop esi;add eax, dword ptr [0x34e37a];'),
        0x34e380: (0x34e386, 'add edi, dword ptr [0x34e382];'),
        0x34e390: (0x34e3a2, 'and ebx, dword ptr [0x34e392];add ebp, dword ptr [0x34e398];push esi;mov esi,dword ptr [0x34e39e];mov cl, byte ptr [ebx+esi];pop esi;'),
        0x34e3a4: (0x34e3be, 'add edx, dword ptr [0x34e3a6];adc ebx, dword ptr [0x34e3ac];push esi;movsx esi,byte ptr [0x34e3b2];mov cl, byte ptr [ecx+esi];pop esi;push esi;mov esi,dword ptr [0x34e3b5];mov byte ptr [eax+esi], cl;pop esi;add eax, dword ptr [0x34e3ba];'),
        0x34e3c0: (0x34e3c6, 'add edi, dword ptr [0x34e3c2];'),
        0x34e3c8: (0x34e3ce, 'test edx, dword ptr [0x34e3ca];'),
        0x34e3d0: (0x34e3e2, 'and ebx, dword ptr [0x34e3d2];add ebp, dword ptr [0x34e3d8];push esi;mov esi,dword ptr [0x34e3de];mov cl, byte ptr [ebx+esi];pop esi;'),
        0x34e3e4: (0x34e3fe, 'add edx, dword ptr [0x34e3e6];adc ebx, dword ptr [0x34e3ec];push esi;movsx esi,byte ptr [0x34e3f2];mov cl, byte ptr [ecx+esi];pop esi;push esi;mov esi,dword ptr [0x34e3f5];mov byte ptr [eax+esi], cl;pop esi;add eax, dword ptr [0x34e3fa];'),
        0x34e400: (0x34e406, 'add edi, dword ptr [0x34e402];'),
    }),
    'affine': (0x3311de, 0x3313c3, '93260db7d7ad7a691f83fb0a05822c8d917df27e6ec2791c24fa95bd9d940e0f', {
        0x3311df: (0x3311e4, 'mov eax, dword ptr [0x3311e0];'),
        0x331200: (0x331206, 'cmp ebx, dword ptr [0x331202];'),
        0x331227: (0x33122a, 'push ecx;mov cl,[0x331229];shl edx,cl;pop ecx;'),
        0x331234: (0x33123a, 'add edx, dword ptr [0x331236];'),
        0x331245: (0x331250, 'push ecx;mov cl,[0x331247];shl esi,cl;pop ecx;push ecx;mov cl,[0x33124a];sar eax,cl;pop ecx;and eax, dword ptr [0x33124c];'),
        0x331255: (0x33125b, 'push ecx;mov cl,[0x331257];shl edi,cl;pop ecx;push ecx;mov cl,[0x33125a];sar edx,cl;pop ecx;'),
        0x331266: (0x331275, 'test eax, dword ptr [0x331267];test eax, dword ptr [0x33126c];L331270:and eax, dword ptr [0x331271];'),
        0x331277: (0x331282, 'push edi;mov edi,[0x331279];mov dl, byte ptr [eax+edi];pop edi;adc eax, dword ptr [0x33127e];'),
        0x331284: (0x331290, 'push edi;mov edi,[0x331286];mov dl, byte ptr [edx+edi];pop edi;push edi;mov edi,[0x33128c];mov byte ptr [ebx+edi], dl;pop edi;'),
        0x331295: (0x3312a5, 'and eax, dword ptr [0x331296];and edx, dword ptr [0x33129c];L3312a0:and eax, dword ptr [0x3312a1];'),
        0x3312a7: (0x3312b2, 'push edi;mov edi,[0x3312a9];mov dl, byte ptr [eax+edi];pop edi;adc eax, dword ptr [0x3312ae];'),
        0x3312b4: (0x3312c0, 'push edi;mov edi,[0x3312b6];mov dl, byte ptr [edx+edi];pop edi;push edi;mov edi,[0x3312bc];mov byte ptr [ebx+edi], dl;pop edi;'),
        0x3312c8: (0x3312cd, 'mov eax, dword ptr [0x3312c9];'),
        0x3312ed: (0x3312f9, 'add eax, dword ptr [0x3312ef];adc ebx, dword ptr [0x3312f5];'),
        0x3312fc: (0x331306, 'add ax, word ptr [0x3312fe];adc ecx, dword ptr [0x331302];'),
        0x331320: (0x331326, 'cmp ebx, dword ptr [0x331322];'),
        0x331335: (0x331340, 'add edx, dword ptr [0x331337];add eax, dword ptr [0x33133c];'),
        0x33134c: (0x33134f, 'push ecx;mov cl,[0x33134e];shl edx,cl;pop ecx;'),
        0x331357: (0x331362, 'add edx, dword ptr [0x331359];add eax, dword ptr [0x33135e];'),
        0x331376: (0x33137c, 'add edx, dword ptr [0x331378];'),
        0x33138e: (0x331393, 'mov esi, dword ptr [0x33138f];'),
    }),
}


def ks(src):
    return bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm(src, 0)[0])


def load(path):
    img = open(path, 'rb').read()
    return lambda a, n: img[a - BASE:a - BASE + n]


def shifted(rec, d):
    """the recipe for an exe whose routine sits d bytes further (addresses inside the routine and labels move)"""
    low, high, digest, edits = rec
    f = lambda m: '%#x' % (int(m.group(0), 16) + d) if low <= int(m.group(0), 16) < high else m.group(0)
    g = lambda m: 'L%x' % (int(m.group(1), 16) + d)
    sub = lambda t: re.sub(r'L([0-9a-f]+)', g, re.sub(r'0x[0-9a-fA-F]+', f, t))
    return (low + d, high + d, None, {a + d: (s + d, sub(t)) for a, (s, t) in edits.items()})


def disasm(read, low, high):
    data = read(low, high - low)
    ins = list(Cs(CS_ARCH_X86, CS_MODE_32).disasm(data, low))
    assert sum(i.size for i in ins) == high - low, 'routine did not decode completely'
    return data, ins


def layout(read, rec, hooked):
    """pieces ('C', address, length) / ('B', bytes), the copied bytes, label offsets. The instructions under the
    6-byte entry jmp (hooked = its address) are assembled here: at run time those bytes are the tool's jmp."""
    low, high, digest, edits = rec
    data, ins = disasm(read, low, high)
    if digest: assert hashlib.sha256(data).hexdigest() == digest, 'not the verified original routine'
    bounds = {i.address for i in ins}
    items, end = [], low
    for i in ins:
        a = i.address
        if a < end: continue
        if a in edits:
            stop, text = edits[a]
            assert stop in bounds | {high}
            items.append(('L', a))
            for part in re.split(r'(L[0-9a-f]+):', text):
                if re.fullmatch(r'L[0-9a-f]+', part): items.append(('L', int(part[1:], 16)))
                elif part.strip(): items.append(('B', ks(part)))
            end = stop
            continue
        items.append(('L', a))
        if i.mnemonic.startswith('j'):
            t = int(i.op_str, 16); assert low <= t < high and t in bounds, 'branch leaves the routine'
            items.append(('J', i.mnemonic, t))
        elif a < hooked + 6 and a + i.size > hooked:
            items.append(('B', ks('%s %s' % (i.mnemonic, i.op_str))))
        else:
            assert i.mnemonic not in ('call', 'loop', 'jecxz') and 'rip' not in i.op_str
            items.append(('C', a, i.size))
    size = lambda it: len(it[1]) if it[0] == 'B' else it[2] if it[0] == 'C' else (5 if it[1] == 'jmp' else 6) if it[0] == 'J' else 0
    off, lab = 0, {}
    for it in items:
        if it[0] == 'L': lab[it[1]] = off
        off += size(it)
    pieces, copied, off = [], b'', 0
    for it in items:
        if it[0] == 'C':
            if pieces and pieces[-1][0] == 'C' and pieces[-1][1] + pieces[-1][2] == it[1]:
                pieces[-1] = ('C', pieces[-1][1], pieces[-1][2] + it[2])
            else: pieces.append(('C', it[1], it[2]))
            copied += read(it[1], it[2])
        elif it[0] in ('B', 'J'):
            if it[0] == 'B': b = it[1]
            elif it[1] == 'jmp': b = b'\xe9' + struct.pack('<i', lab[it[2]] - (off + 5))
            else: b = bytes([0x0f, 0x80 + CC[it[1]]]) + struct.pack('<i', lab[it[2]] - (off + 6))
            if pieces and pieces[-1][0] == 'B': pieces[-1] = ('B', pieces[-1][1] + b)
            else: pieces.append(('B', b))
        off += size(it)
    return pieces, copied, lab, off


def same_shape(en_read, fr_read, low, high, d):
    """the French routine = the English one, addresses inside the routine moved by d (others may differ)"""
    _, a = disasm(en_read, low, high); _, b = disasm(fr_read, low + d, high + d)
    assert len(a) == len(b)
    for x, y in zip(a, b):
        assert x.mnemonic == y.mnemonic and x.size == y.size, (hex(x.address), x.mnemonic, y.mnemonic)
        nx = re.findall(r'0x[0-9a-f]+', x.op_str); ny = re.findall(r'0x[0-9a-f]+', y.op_str)
        assert len(nx) == len(ny)
        for p, q in zip(nx, ny):
            p, q = int(p, 16), int(q, 16)
            if low <= p < high: assert q == p + d, (hex(x.address), hex(p), hex(q))


def build(en_read, fr_read):
    out = {}
    for lang, read, d in (('EN', en_read, 0), ('FR', fr_read, FR_DELTA)):
        blk, offs = [], {}
        total = 0
        pieces_all, copied_all = [], b''
        for name in ('span', 'affine'):
            rec = RECIPES[name] if d == 0 else shifted(RECIPES[name], d)
            if d: same_shape(en_read, fr_read, RECIPES[name][0], RECIPES[name][1], d)
            pieces, copied, lab, size = layout(read, rec, ENTRY[name] + d)
            pad = (-total) % 16
            if pad: pieces_all.append(('B', b'\x90' * pad)); total += pad
            offs[name] = total + lab[ENTRY[name] + d]
            pieces_all += pieces; copied_all += copied; total += size
        # entries: 6-byte jmp [PTR], the stub runs the overwritten instructions then the rest of the original
        site, stub = {}, {}
        s = ENTRY['span'] + d; o = read(s, 8)
        assert o[:4] == b'\x60\x8b\xf0\xb9', o.hex()                       # pushad / mov esi,eax / mov ecx,imm32
        site['span'] = (s, o[:6], b'\xff\x25' + struct.pack('<I', PTR['span']))
        stub['span'] = bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm('pushad; mov esi, eax; mov ecx, %#x; jmp %#x' % (
            struct.unpack('<I', o[4:8])[0], s + 8), STUB['span'])[0])
        s = ENTRY['affine'] + d; o = read(s, 9)
        assert o[:5] == b'\x60\x8b\xf8\x8b\x1d', o.hex()                   # pushad / mov edi,eax / mov ebx,[abs]
        site['affine'] = (s, o[:6], b'\xff\x25' + struct.pack('<I', PTR['affine']))
        stub['affine'] = bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm('pushad; mov edi, eax; mov ebx, dword ptr [%#x]; jmp %#x' % (
            struct.unpack('<I', o[5:9])[0], s + 9), STUB['affine'])[0])
        for name in ('span', 'affine'):     # nothing in the routines jumps into the 6 bytes the entry jmp takes
            low, high = (RECIPES[name][0] + d, RECIPES[name][1] + d)
            _, ins = disasm(read, low, high)
            for i in ins:
                if i.mnemonic.startswith('j'): assert not (site[name][0] < int(i.op_str, 16) < site[name][0] + 6)
        assert STUB['span'] + len(stub['span']) <= 0x46A100 and STUB['affine'] + len(stub['affine']) <= 0x465100
        out[lang] = dict(pieces=pieces_all, sha=hashlib.sha256(copied_all).hexdigest(), entry=offs, size=total,
                         site=site, stub=stub, copied=len(copied_all))
    return out


def export_cs(out):
    print('// Generated by tools/ir_fix.py (python ir_fix.py EN_IMAGE FR_IMAGE cs > ../src/TNPlusIr.cs): do not edit.')
    print('// Infrared terrain without self-modifying loops (after Callemann): pieces C:address:length are copied from the')
    print("// game's own memory, B: pieces are ours (edits, re-encoded branches). See tools/ir_fix.py.")
    print('static class IrFix\n{')
    print('    public const uint StubSpan = 0x%X, StubAffine = 0x%X, PtrSpan = 0x%X, PtrAffine = 0x%X, XCode = 0x%X;' % (
        STUB['span'], STUB['affine'], PTR['span'], PTR['affine'], XCODE))
    L = ('EN', 'FR')
    q = lambda xs: ', '.join(xs)
    print('    public static readonly uint[] SiteSpan = { %s }, SiteAffine = { %s };' % (
        q('0x%X' % out[l]['site']['span'][0] for l in L), q('0x%X' % out[l]['site']['affine'][0] for l in L)))
    for k, n in (('OldSpan', ('span', 1)), ('OldAffine', ('affine', 1)), ('NewSpan', ('span', 2)), ('NewAffine', ('affine', 2))):
        print('    public static readonly string[] %s = { %s };' % (k, q('"%s"' % out[l]['site'][n[0]][n[1]].hex().upper() for l in L)))
    print('    public static readonly string[] CodeStubSpan = { %s };' % q('"%s"' % out[l]['stub']['span'].hex().upper() for l in L))
    print('    public static readonly string[] CodeStubAffine = { %s };' % q('"%s"' % out[l]['stub']['affine'].hex().upper() for l in L))
    print('    public static readonly string[] Sha = { %s };' % q('"%s"' % out[l]['sha'] for l in L))
    print('    public static readonly int[] EntrySpan = { %s }, EntryAffine = { %s }, Size = { %s };' % (
        q(str(out[l]['entry']['span']) for l in L), q(str(out[l]['entry']['affine']) for l in L), q(str(out[l]['size']) for l in L)))
    print('    public static readonly string[][] Pieces = {')
    for l in L:
        ps = ['"C:%X:%X"' % (p[1], p[2]) if p[0] == 'C' else '"B:%s"' % p[1].hex().upper() for p in out[l]['pieces']]
        print('        new string[] {')
        for k in range(0, len(ps), 6): print('            ' + ', '.join(ps[k:k + 6]) + ',')
        print('        },')
    print('    };\n}')


if __name__ == '__main__':
    en, fr = load(sys.argv[1]), load(sys.argv[2])
    out = build(en, fr)
    if 'cs' in sys.argv: export_cs(out)
    else:
        for l, o in out.items():
            print(l, 'block %d bytes, entries span +%d affine +%d, %d bytes copied from the game, %d pieces, sha %s' % (
                o['size'], o['entry']['span'], o['entry']['affine'], o['copied'], len(o['pieces']), o['sha'][:16]))
            for n in ('span', 'affine'):
                print('  %s: site %#x old %s new %s | stub %s' % (n, o['site'][n][0], o['site'][n][1].hex(), o['site'][n][2].hex(), o['stub'][n].hex()))
