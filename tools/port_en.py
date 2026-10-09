# Portage du mod HD vers l'exe ANGLAIS (GOG EN = Steam) : pour chaque adresse francaise utilisee par hd_mod.py,
# retrouve l'equivalent anglais par signature de code (instructions identiques, adresses absolues et sauts
# relatifs masques). Images RAM : snaps/m19.bin (FR, jeu charge) et snaps/en_menu.bin (EN, menu principal).
#   python port_en.py            -> affiche la table et l'ecrit dans port_en.json
import json, re, struct, sys
import capstone
import le

FR_SNAP, EN_SNAP = 'snaps/pal.bin', 'snaps/en_menu.bin'    # pal.bin : image FR SANS le mod HD injecte
FR_EXE, EN_EXE = r'..\hd\__FF.EXE.original', r'..\..\_BACKUP_EN\__FF.EXE'
fr = open(FR_SNAP, 'rb').read()
en = open(EN_SNAP, 'rb').read()
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
md.detail = True


def localise(mem, exe):
    """base de chargement de chaque objet LE dans l'image RAM"""
    from collections import Counter
    res = {}
    for o in le.parse(exe):
        raw = o['raw']; votes = Counter()
        for off in range(0x40, len(raw) - 64, max(0x100, len(raw) // 80)):
            chunk = raw[off:off + 48]
            if len(set(chunk)) < 12: continue
            i = mem.find(chunk)
            if i != -1 and mem.find(chunk, i + 1) == -1: votes[i - off] += 1
        res[o['n']] = (votes.most_common(1)[0][0] if votes else None, o['vsize'])
    return res


# le code est charge au meme endroit dans les deux versions (0x20ED5C, verifie) ; une copie brute non relogee de
# l'exe traine plus bas en RAM, d'ou des plages fixees ici plutot que devinees
OBJS_FR, OBJS_EN = le.parse(FR_EXE), le.parse(EN_EXE)
FR_CODE = (0x20ed5c, 0x20ed5c + OBJS_FR[0]['vsize'])
EN_CODE = (0x20ed5c, 0x20ed5c + OBJS_EN[0]['vsize'])
FR_LO, FR_HI = 0x20ed5c, 0x465024 + 0x4000
EN_LO, EN_HI = 0x20ed5c, 0x470000


def obj3_base(mem, objs, lo):
    raw = objs[2]['raw']
    for off in (0, 0x40, 0x80, 0x100, 0x200):
        chunk = raw[off:off + 32]
        i = mem.find(chunk, lo)
        if i >= 0 and mem.find(chunk, i + 1) < 0: return i - off
    return None


print('objet 3 : FR base %s (fin %s)  EN base %s (fin %s)' % tuple(
    hex(x) if x else None for x in (obj3_base(fr, OBJS_FR, 0x300000), (obj3_base(fr, OBJS_FR, 0x300000) or 0) + OBJS_FR[2]['vsize'],
                                    obj3_base(en, OBJS_EN, 0x300000), (obj3_base(en, OBJS_EN, 0x300000) or 0) + OBJS_EN[2]['vsize'])))


def is_addr(v, lo, hi): return lo <= v < hi


def signature(mem, a, lo, hi, n_insn=10, before=0):
    """regex des n instructions a partir de a : adresses absolues et deplacements relatifs masques"""
    pat = b''
    for ins in md.disasm(mem[a:a + 15 * n_insn], a, n_insn):
        b = bytes(ins.bytes)
        mask = [False] * len(b)
        # operandes immediats / deplacements ressemblant a une adresse du jeu, et sauts / appels relatifs
        if ins.mnemonic in ('call', 'jmp', 'loop', 'jecxz') or ins.mnemonic.startswith('j'):
            if len(b) >= 5 and b[-5] in (0xe8, 0xe9) or (len(b) == 6 and b[0] == 0x0f):
                for k in range(len(b) - 4, len(b)): mask[k] = True
            elif len(b) == 2 and (b[0] == 0xeb or 0x70 <= b[0] <= 0x7f or b[0] in (0xe0, 0xe1, 0xe2, 0xe3)):
                mask[1] = True                      # saut court : la distance peut changer d'une version a l'autre
        d = ins
        for off, size in ((d.disp_offset, d.disp_size), (d.imm_offset, d.imm_size)):
            if size == 4 and off:
                v = struct.unpack_from('<I', b, off)[0]
                if is_addr(v, lo, hi):
                    for k in range(off, off + 4): mask[k] = True
        for k, byte in enumerate(b):
            pat += b'.' if mask[k] else re.escape(bytes([byte]))
    return pat


def nearest(ms, a):
    """plusieurs correspondances : le code ne bouge que de quelques centaines d'octets entre les versions"""
    if len(ms) == 1: return ms[0]
    if not ms: return None
    ms = sorted(ms, key=lambda m: abs(m - a))
    return ms[0] if abs(ms[0] - a) < 0x1000 and abs(ms[1] - a) > 0x2000 else None


def find_code(a, n_insn=10):
    """adresse EN d'une adresse de code FR"""
    pat = signature(fr, a, FR_LO, FR_HI, n_insn)
    ms = [m.start() + EN_CODE[0] for m in re.finditer(pat, en[EN_CODE[0]:EN_CODE[1]], re.S)]
    if nearest(ms, a) is None and n_insn < 24: return find_code(a, n_insn + 6)
    return nearest(ms, a), len(ms)


STARTS = None
def insn_starts():
    """debuts d'instruction du code FR, d'apres le desassemblage code.asm"""
    global STARTS
    if STARTS is None:
        import bisect
        STARTS = sorted(int(l[:8], 16) for l in open('code.asm', encoding='utf-8', errors='replace') if len(l) > 9 and l[8] == ':')
    return STARTS


def xrefs(mem, a, lo, hi, limit=12):
    """positions des dwords == a dans le code"""
    key = struct.pack('<I', a); out = []; i = lo
    while len(out) < limit:
        i = mem.find(key, i, hi)
        if i < 0: break
        out.append(i); i += 1
    return out


def insn_at(mem, pos, base_guess):
    """l'instruction (du listing code.asm) qui contient l'offset pos"""
    import bisect
    st = insn_starts(); i = bisect.bisect_right(st, pos) - 1
    if i < 0: return None
    for ins in md.disasm(mem[st[i]:st[i] + 15], st[i], 1):
        return ins if ins.address <= pos < ins.address + ins.size else None
    return None


def find_data(a):
    """adresse EN d'une variable FR : via les instructions qui la referencent"""
    from collections import Counter
    votes = Counter()
    for h in xrefs(fr, a, *FR_CODE):
        ins = insn_at(fr, h, FR_CODE[0])
        if ins is None: continue
        start = ins.address
        # signature : l'instruction puis les 7 suivantes, le dword cible repere par sa position
        pat = signature(fr, start, FR_LO, FR_HI, 8)
        ms = [m.start() + EN_CODE[0] for m in re.finditer(pat, en[EN_CODE[0]:EN_CODE[1]], re.S)]
        if nearest(ms, start) is None:
            pat = signature(fr, start, FR_LO, FR_HI, 16)
            ms = [m.start() + EN_CODE[0] for m in re.finditer(pat, en[EN_CODE[0]:EN_CODE[1]], re.S)]
        m = nearest(ms, start)
        if m is None: continue
        v = struct.unpack_from('<I', en, m + (h - start))[0]
        if is_addr(v, EN_LO, EN_HI): votes[v] += 1
    if not votes: return None, 0
    (v, n), = votes.most_common(1)
    return v, n


if __name__ == '__main__':
    s = open('hd_mod.py', encoding='utf-8').read()
    a0 = s.index('ASM = '); asm = s[a0:s.index('"""', s.index('"""', a0) + 3) + 3]
    pl = s[s.index('PATCHES = ['):s.index('def etat')]
    lits = set(int(x, 16) for x in re.findall(r'0x[0-9a-fA-F]{5,6}', asm + pl))
    lits |= {0x38fbd4, 0x41ec74, 0x43e360, 0x35d03c, 0x35f978, 0x35f974, 0x44b250, 0x2a0f58, 0x29e820}
    table = {}
    for a in sorted(lits):
        if is_addr(a, *FR_CODE):
            b, n = find_code(a); kind = 'code'
        elif is_addr(a, FR_CODE[1], FR_HI):
            b, n = find_data(a); kind = 'data'
        else:
            continue
        table[a] = b
        print('%s %#x -> %s  (%s)' % (kind, a, hex(b) if b else 'INTROUVABLE', n))
    json.dump({hex(k): (hex(v) if v else None) for k, v in table.items()}, open('port_en.json', 'w'), indent=1)
