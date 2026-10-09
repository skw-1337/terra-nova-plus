# ============================================================
#  le.py - lecture du format LE (DOS/4GW) de __FF.EXE,
#  correspondance exe <-> RAM emulee, desassemblage (capstone)
#
#  python le.py objets
#  python le.py localise SNAP          (ou est charge chaque objet)
#  python le.py xref SNAP ADRESSE_HEX  (instructions qui touchent l'adresse)
#  python le.py dis SNAP ADRESSE_HEX [N]
#  python le.py fichier ADRESSE_HEX    (adresse RAM -> offset dans __FF.EXE)
# ============================================================
import json, os, struct, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
EXE = os.path.join(HERE, '..', '..', 'TNOVA', '__FF.EXE')
MAPF = os.path.join(HERE, 'objets.json')


def parse(path=EXE):
    d = open(path, 'rb').read()
    le = d.find(b'LE\x00\x00')
    u32 = lambda o: struct.unpack_from('<I', d, le + o)[0]
    page_size = u32(0x28)
    last_page = u32(0x2C)
    n_pages = u32(0x14)
    obj_tab, n_obj, pmap = u32(0x40), u32(0x44), u32(0x48)
    data_pages = u32(0x80)                       # relatif au debut du fichier
    objs = []
    for i in range(n_obj):
        vsize, base, flags, pidx, pcount, _ = struct.unpack_from('<6I', d, le + obj_tab + 24 * i)
        raw = bytearray()
        file_offs = []
        for p in range(pidx, pidx + pcount):
            e = d[le + pmap + 4 * (p - 1): le + pmap + 4 * p]
            num = (e[0] << 16) | (e[1] << 8) | e[2]
            off = data_pages + (num - 1) * page_size
            size = last_page if num == n_pages else page_size
            file_offs.append(off)
            raw += d[off:off + size]
        objs.append(dict(n=i + 1, vsize=vsize, base=base, flags=flags,
                         code=bool(flags & 4), pages=file_offs, page_size=page_size,
                         raw=bytes(raw)))
    return objs


def localise(snap):
    """Trouve l'adresse de chargement de chaque objet dans la RAM emulee."""
    mem = open(os.path.join(HERE, 'snaps', snap + '.bin'), 'rb').read()
    from collections import Counter
    res = {}
    for o in parse():
        raw = o['raw']
        votes = Counter()
        for off in range(0x40, len(raw) - 64, max(0x100, len(raw) // 60)):
            chunk = raw[off:off + 48]
            if len(set(chunk)) < 12:
                continue
            i = mem.find(chunk)
            if i != -1 and mem.find(chunk, i + 1) == -1:
                votes[i - off] += 1
        if votes:
            res[o['n']] = votes.most_common(1)[0][0]
            res[o['n'], 'cands'] = [b for b, _ in votes.most_common(3)]
    # une copie brute (non relogee) de l'exe peut trainer en RAM : on garde
    # pour chaque objet le candidat aligne comme les autres objets (DOS/4GW
    # charge tous les objets avec le meme decalage dans la page)
    from collections import Counter as C2
    low = C2(b & 0xfff for k, b in res.items() if not isinstance(k, tuple)).most_common(1)[0][0]
    for k in [k for k in res if isinstance(k, tuple)]:
        n = k[0]
        ok = [b for b in res[k] if b & 0xfff == low]
        if ok:
            res[n] = ok[0]
        del res[k]
    json.dump(res, open(MAPF, 'w'))
    return res


def charge_map():
    return {int(k): v for k, v in json.load(open(MAPF)).items()}


def objet_de(addr):
    objs = parse()
    for n, base in charge_map().items():
        o = objs[n - 1]
        if base <= addr < base + o['vsize']:
            return o, base
    return None, None


def vers_fichier(addr):
    o, base = objet_de(addr)
    if not o:
        return None
    rel = addr - base
    page = rel // o['page_size']
    return o['pages'][page] + rel % o['page_size']


def code_ram(snap):
    mem = open(os.path.join(HERE, 'snaps', snap + '.bin'), 'rb').read()
    objs = parse()
    m = charge_map()
    for n, base in m.items():
        o = objs[n - 1]
        if o['code']:
            return base, mem[base:base + o['vsize']], mem
    raise SystemExit('objet de code non localise')


def dis(snap, addr, n=40):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    mem = open(os.path.join(HERE, 'snaps', snap + '.bin'), 'rb').read()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    out = []
    for ins in md.disasm(mem[addr:addr + 16 * n], addr):
        out.append('%08x: %-24s %s %s' % (ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
        if len(out) >= n:
            break
    return out


def xref(snap, target, span=4):
    """Instructions dont un operande (deplacement ou immediat) vaut target..target+span-1."""
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    base, code, _ = code_ram(snap)
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.skipdata = True
    hits = []
    pats = set()
    for t in range(target - 3, target + span):
        pats.add(struct.pack('<I', t & 0xffffffff))
    # prefiltre rapide : positions ou apparait l'adresse sur 4 octets
    cand = set()
    for p in pats:
        i = code.find(p)
        while i != -1:
            cand.add(i)
            i = code.find(p, i + 1)
    for c in sorted(cand):
        # redesassemble a partir de quelques octets avant pour retrouver l'instruction
        for back in range(1, 8):
            s = c - back
            if s < 0:
                continue
            ins = next(md.disasm(code[s:s + 16], base + s), None)
            if ins and ins.size > back and ins.size >= back + 4:
                txt = '%08x: %-24s %s %s' % (ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str)
                if txt not in hits:
                    hits.append(txt)
                break
    return hits


if __name__ == '__main__':
    a = sys.argv[1:]
    if a[0] == 'objets':
        for o in parse():
            print('objet %d  base 0x%08x  taille 0x%06x  flags 0x%04x %s  pages %d' % (
                o['n'], o['base'], o['vsize'], o['flags'], 'CODE' if o['code'] else 'DATA', len(o['pages'])))
    elif a[0] == 'localise':
        for n, b in localise(a[1]).items():
            print('objet %d charge a 0x%08x' % (n, b))
    elif a[0] == 'xref':
        for h in xref(a[1], int(a[2], 16), int(a[3]) if len(a) > 3 else 1):
            print(h)
    elif a[0] == 'dis':
        print('\n'.join(dis(a[1], int(a[2], 16), int(a[3]) if len(a) > 3 else 40)))
    elif a[0] == 'fichier':
        f = vers_fichier(int(a[1], 16))
        print('offset fichier 0x%x' % f if f is not None else 'hors objets')
