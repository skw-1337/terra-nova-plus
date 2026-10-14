# Shots through turrets (original bugs), ported to the DOSBox game from TNova Native (Callemann,
# engine/features/projectiles.py, MIT): three separate causes, three small changes.
#  1. Stale exclusion. The ray routine (EN 0x2F074C) skips the object in a shared slot (word EN 0x4464B8); impact
#     tracing leaves its last object there, so a later projectile sweep with no source body could ignore a turret
#     it had hit before, for good. For projectile sweeps (return address EN 0x2D96DA) with no source body (third
#     stack argument 0) the slot is emptied for that call and put back afterwards. The call is also flagged as a
#     projectile ray (PROJ) for part 2.
#  2. Vertical travel. The cell walk (EN 0x2DA91D) skips a terrain cell when the XY travel is tiny, so a shot falling
#     on a turret almost vertically was never tested. During a projectile ray, a segment that changes height
#     (EN 0x446778 != 0x446748) now looks at its cell.
#  3. Short cylinder segments. The cylinder test (EN 0x2DBAA0) rejects any segment whose squared XY length is below 8
#     (16.16) in its fixed-point quadratic: short segments always missed. Those are now intersected in floating point
#     (x87 here; the native build uses SSE2, which DOSBox does not emulate): inside the XY circle and under the top
#     plane (solid top, open bottom), earliest fraction in [0, 1]. The FPU state is saved and restored.
#   python turret_fix.py            prints the pieces for EN and FR (address, old, new)
#   python turret_fix.py test       checks part 3 against a reference in an emulator (Unicorn)
import struct, sys
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

CAVE_A, CAVE_A_END = 0x465024, 0x465100   # free start of object 3, before the HD code (same code page)
CAVE_B, CAVE_B_END = 0x46AE54, 0x46B000   # free end of the tool's code area
PROJ = 0x469390                           # flag "projectile ray" (a data page: never write in a code page)
LANG = {   # code moves by +0x1F0 / +0x150 in the French exe, data by +0xB0
    'EN': dict(RAY=0x2F074C, RET=0x2D96DA, EXCL=0x4464B8, VSITE=0x2DA91D, VSKIP=0x2DB061, Z1=0x446778, Z0=0x446748,
               CYL=0x2DBAA0),
    'FR': dict(RAY=0x2F093C, RET=0x2D982A, EXCL=0x446568, VSITE=0x2DAA6D, VSKIP=0x2DB1B1, Z1=0x446828, Z0=0x4467F8,
               CYL=0x2DBBF0),
}
RAY_OLD = bytes.fromhex('56575589e5')          # push esi / push edi / push ebp / mov ebp,esp
CYL_OLD = bytes.fromhex('56575583ec28')        # push esi / push edi / push ebp / sub esp,0x28
VSITE_OLD = bytes.fromhex('0f8c3e070000')      # jl VSKIP


def asm(src, at):
    return bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm(src, at)[0])


RAY_SRC = '''
    cmp dword ptr [esp], {RET}
    jne orig
    push ebp
    mov ebp, esp
    push dword ptr [{PROJ}]
    push -1
    cmp dword ptr [ebp+16], 0
    jne go
    push ecx
    movzx ecx, word ptr [{EXCL}]
    mov [ebp-8], ecx
    pop ecx
    mov word ptr [{EXCL}], 0
go:
    {COUNT_RAY}
    mov dword ptr [{PROJ}], 1
    push dword ptr [ebp+32]
    push dword ptr [ebp+28]
    push dword ptr [ebp+24]
    push dword ptr [ebp+20]
    push dword ptr [ebp+16]
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call orig
    pushfd
    push ecx
    mov ecx, [ebp-4]
    mov [{PROJ}], ecx
    mov ecx, [ebp-8]
    cmp ecx, -1
    je kept
    mov word ptr [{EXCL}], cx
kept:
    pop ecx
    popfd
    leave
    ret 28
orig:
    push esi
    push edi
    push ebp
    mov ebp, esp
    jmp {RAY5}
'''

VERT_SRC = '''
    jge go
    pushfd
    push eax
    cmp dword ptr [{PROJ}], 1
    jne skip
    mov eax, [{Z1}]
    cmp eax, [{Z0}]
    je skip
    pop eax
    popfd
    {COUNT_VERT}
go:
    jmp {VSITE6}
skip:
    pop eax
    popfd
    jmp {VSKIP}
'''

# st(0) -> lo = max(lo, st(0)) / hi = min(hi, st(0)) in the cylinder's frame; pops st(0)
MINMAX_SRC = '''
lo_max:
    fcom qword ptr [ebp-96]
    fnstsw ax
    sahf
    jbe lo_keep
    fst qword ptr [ebp-96]
lo_keep:
    fstp st(0)
    ret
hi_min:
    fcom qword ptr [ebp-104]
    fnstsw ax
    sahf
    jae hi_keep
    fst qword ptr [ebp-104]
hi_keep:
    fstp st(0)
    ret
'''

# ebp frame: pushad at ebp-4 (eax = object position) .. ebp-32; locals ebp-36 .. ebp-120; FPU state saved under them
# locals: -36 x0 rel, -40 y0 rel, -44 dx, -48 dy, -52 radius, -56 top, -60 z0 rel, -64 z1 rel (ints)
#         -72 a, -80 b, -88 c, -96 lo, -104 hi, -112 scratch (doubles), -120 scratch int
CYL_SRC = '''
    push ebp
    mov ebp, esp
    pushad
    mov edi, [ebp+12]
    sub edi, ebx
    mov eax, edi
    imul edi
    shrd eax, edx, 16
    mov esi, eax
    mov eax, [ebp+16]
    sub eax, ecx
    imul eax
    shrd eax, edx, 16
    add eax, esi
    cmp eax, 8
    jge orig
    sub esp, 196
    {COUNT_CYL}
    fnsave [esp]
    mov esi, [ebp-4]
    mov edx, [ebp-12]
    movzx eax, word ptr [edx+2]
    lea edx, [edx+eax*8+4]
    mov eax, [edx]
    mov [ebp-52], eax
    mov eax, [edx+4]
    mov [ebp-56], eax
    sub ebx, [esi]
    mov [ebp-36], ebx
    sub ecx, [esi+4]
    mov [ebp-40], ecx
    mov [ebp-44], edi
    mov eax, [ebp+16]
    sub eax, [ebp-8]
    mov [ebp-48], eax
    mov eax, [ebp+8]
    mov ecx, 0xaaa
    imul ecx
    shrd eax, edx, 16
    sub eax, [esi+8]
    mov [ebp-60], eax
    mov eax, [ebp+20]
    imul ecx
    shrd eax, edx, 16
    sub eax, [esi+8]
    mov [ebp-64], eax
    fild dword ptr [ebp-44]
    fimul dword ptr [ebp-44]
    fild dword ptr [ebp-48]
    fimul dword ptr [ebp-48]
    faddp st(1), st(0)
    fst qword ptr [ebp-72]
    ftst
    fnstsw ax
    fstp st(0)
    mov [ebp-120], eax
    fild dword ptr [ebp-36]
    fimul dword ptr [ebp-44]
    fild dword ptr [ebp-40]
    fimul dword ptr [ebp-48]
    faddp st(1), st(0)
    fstp qword ptr [ebp-80]
    fild dword ptr [ebp-52]
    fimul dword ptr [ebp-52]
    fstp qword ptr [ebp-112]
    fild dword ptr [ebp-36]
    fimul dword ptr [ebp-36]
    fild dword ptr [ebp-40]
    fimul dword ptr [ebp-40]
    faddp st(1), st(0)
    fsub qword ptr [ebp-112]
    fstp qword ptr [ebp-88]
    fldz
    fstp qword ptr [ebp-96]
    fld1
    fstp qword ptr [ebp-104]
    mov eax, [ebp-120]
    sahf
    je vertical
    fld qword ptr [ebp-72]
    fmul qword ptr [ebp-88]
    fstp qword ptr [ebp-112]
    fld qword ptr [ebp-80]
    fmul qword ptr [ebp-80]
    fsub qword ptr [ebp-112]
    ftst
    fnstsw ax
    sahf
    jb miss
    fsqrt
    fstp qword ptr [ebp-112]
    fld qword ptr [ebp-80]
    fchs
    fsub qword ptr [ebp-112]
    fdiv qword ptr [ebp-72]
    call {LOMAX}
    fld qword ptr [ebp-80]
    fchs
    fadd qword ptr [ebp-112]
    fdiv qword ptr [ebp-72]
    call {HIMIN}
    jmp height
vertical:
    fld qword ptr [ebp-88]
    ftst
    fnstsw ax
    sahf
    ja miss
height:
    mov eax, [ebp-64]
    sub eax, [ebp-60]
    je level
    mov [ebp-120], eax
    fild dword ptr [ebp-56]
    fisub dword ptr [ebp-60]
    fidiv dword ptr [ebp-120]
    test eax, eax
    js down
    call {HIMIN}
    jmp interval
down:
    call {LOMAX}
    jmp interval
level:
    mov eax, [ebp-56]
    cmp eax, [ebp-60]
    jl miss
interval:
    fld qword ptr [ebp-96]
    fcomp qword ptr [ebp-104]
    fnstsw ax
    sahf
    ja miss
    mov word ptr [ebp-120], 0x0f7f
    fldcw word ptr [ebp-120]
    mov dword ptr [ebp-120], 65536
    fld qword ptr [ebp-96]
    fimul dword ptr [ebp-120]
    mov edx, [ebp+28]
    fistp dword ptr [edx]
    mov byte ptr [ebp-4], 1
    jmp done
miss:
    mov byte ptr [ebp-4], 0
done:
    frstor [esp]
    add esp, 196
    popad
    pop ebp
    ret 24
orig:
    popad
    pop ebp
    push esi
    push edi
    push ebp
    sub esp, 0x28
    jmp {CYL6}
'''


def build(lang, count=False):
    """count: test build, counters of the three paths at PROJ+4 / +8 / +12"""
    L = LANG[lang]
    k = dict(PROJ=PROJ, RAY5=L['RAY'] + 5, VSITE6=L['VSITE'] + 6, CYL6=L['CYL'] + 6, **L)
    for i, n in enumerate(('COUNT_RAY', 'COUNT_VERT', 'COUNT_CYL')):
        k[n] = 'inc dword ptr [%d]' % (PROJ + 4 + 4 * i) if count else ''
    at, codes, nxt = {}, [], {CAVE_A: CAVE_A, CAVE_B: CAVE_B}
    for name, area, src in (('ray', CAVE_A, RAY_SRC), ('vert', CAVE_A, VERT_SRC), ('minmax', CAVE_A, MINMAX_SRC),
                            ('cyl', CAVE_B, CYL_SRC)):
        a = nxt[area]
        if name == 'cyl':                            # its helpers: hi_min is right after lo_max's 13 bytes
            k['LOMAX'] = at['minmax']; k['HIMIN'] = at['minmax'] + len(asm(MINMAX_SRC.split('hi_min:')[0], at['minmax']))
        c = asm(src.format(**k), a); at[name] = a; codes.append((a, c)); nxt[area] = a + len(c)
    assert nxt[CAVE_A] <= CAVE_A_END and nxt[CAVE_B] <= CAVE_B_END, (hex(nxt[CAVE_A]), hex(nxt[CAVE_B]))
    pieces = [(a, bytes(len(c)), c) for a, c in codes] + [
        (L['RAY'], RAY_OLD, asm('jmp %d' % at['ray'], L['RAY'])),
        (L['VSITE'], VSITE_OLD, asm('jmp %d' % at['vert'], L['VSITE']) + b'\x90'),
        (L['CYL'], CYL_OLD, asm('jmp %d' % at['cyl'], L['CYL']) + b'\x90'),
    ]
    for a, o, n in pieces: assert len(o) == len(n), hex(a)
    return pieces, at


# ---------------------------------------------------------------- test of part 3 (short segments, x87)
def reference(c, r, top, x0, y0, z0, x1, y1, z1):
    """TNova Native's double-precision short path (SSE2), in Python: (hit, fraction 16.16), or None if long"""
    dxe, dye = x1 - x0, y1 - y0
    q = ((dxe * dxe) >> 16) + ((dye * dye) >> 16)
    if q >= 8: return None
    xr, yr = x0 - c[0], y0 - c[1]
    a = dxe * dxe + dye * dye; b = dxe * xr + dye * yr; cc = xr * xr + yr * yr - r * r
    lo, hi = 0.0, 1.0
    if a == 0:
        if cc > 0: return (0, None)
    else:
        disc = b * b - a * cc
        if disc < 0: return (0, None)
        s = disc ** 0.5
        lo = max(lo, (-b - s) / a); hi = min(hi, (-b + s) / a)
    sc = lambda z: (z * 0xaaa) >> 16
    zz0, zz1 = sc(z0) - c[2], sc(z1) - c[2]
    dz = zz1 - zz0
    if dz == 0:
        if top - zz0 < 0: return (0, None)
    else:
        t = (top - zz0) / dz
        if dz > 0: hi = min(hi, t)
        else: lo = max(lo, t)
    if lo > hi: return (0, None)
    return (1, int(lo * 65536))


def test(n=5000):
    import random
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
    from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_ESP, \
        UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP
    L = LANG['EN']; pieces, at = build('EN')
    mu = Uc(UC_ARCH_X86, UC_MODE_32)
    mu.mem_map(0x460000, 0x10000); mu.mem_map(0x2D0000, 0x10000); mu.mem_map(0x600000, 0x10000)
    for a, o, nw in pieces[:4]: mu.mem_write(a, nw)
    mu.mem_write(L['CYL'] + 6, b'\xf4')              # the original body is not here: hlt marks "long path taken"
    END = 0x2D0010; mu.mem_write(END, b'\xf4')
    rnd = random.Random(1); bad = ok = longs = longbad = 0; hits = 0
    for k in range(n):
        c = (rnd.randint(-50, 50) << 16, rnd.randint(-50, 50) << 16, rnd.randint(-5, 5) << 16)
        r = rnd.randint(1, 3) << 15; top = rnd.randint(1, 4) << 15
        x0 = c[0] + rnd.randint(-3 << 16, 3 << 16); y0 = c[1] + rnd.randint(-3 << 16, 3 << 16)
        dx = rnd.choice([0, rnd.randint(-200, 200), rnd.randint(-40000, 40000)])
        dy = rnd.choice([0, rnd.randint(-200, 200), rnd.randint(-40000, 40000)])
        zr = lambda: ((c[2] + rnd.randint(-2 << 16, 2 << 16)) << 16) // 0xaaa
        z0, z1 = zr(), zr()
        ref = reference(c, r, top, x0, y0, z0, x0 + dx, y0 + dy, z1)
        mu.mem_write(0x600000, struct.pack('<3i', *c))
        mu.mem_write(0x600100, struct.pack('<HH2i', 0, 0, r, top))
        mu.mem_write(0x600200, struct.pack('<i', -12345))
        sp = 0x608000
        args = struct.pack('<6I', z0 & 0xffffffff, (x0 + dx) & 0xffffffff, (y0 + dy) & 0xffffffff, z1 & 0xffffffff,
                           0, 0x600200)
        mu.mem_write(sp, struct.pack('<I', END) + args)
        regs = dict(EAX=0x600000, EDX=0x600100, EBX=x0 & 0xffffffff, ECX=y0 & 0xffffffff, ESI=0x11111111,
                    EDI=0x22222222, EBP=0x33333333)
        for nm, v in regs.items(): mu.reg_write(globals().get('UC_X86_REG_' + nm) or locals()['UC_X86_REG_' + nm], v)
        mu.reg_write(UC_X86_REG_ESP, sp)
        try:
            mu.emu_start(at['cyl'], END, count=20000)
        except Exception as e:
            pass
        eip = mu.reg_read(UC_X86_REG_EIP); esp = mu.reg_read(UC_X86_REG_ESP)
        if ref is None:
            longs += 1                                # must reach the original body with its own registers
            if not (eip in (L['CYL'] + 6, L['CYL'] + 7) and esp == sp - 12 - 0x28 and
                    mu.reg_read(UC_X86_REG_EBX) == regs['EBX'] and mu.reg_read(UC_X86_REG_EAX) == regs['EAX']):
                longbad += 1
            continue
        al = mu.reg_read(UC_X86_REG_EAX) & 0xff
        frac = struct.unpack('<i', mu.mem_read(0x600200, 4))[0]
        hit, f = ref
        good = esp == sp + 4 + 24 and al == hit and (not hit or abs(frac - f) <= 1) \
            and mu.reg_read(UC_X86_REG_ESI) == regs['ESI'] and mu.reg_read(UC_X86_REG_EBP) == regs['EBP']
        hits += hit
        if good: ok += 1
        else:
            bad += 1
            if bad <= 5: print('ECART', k, 'ref', ref, 'x87', al, frac, 'esp', hex(esp), hex(sp + 28), 'eip', hex(eip))
    print('segments courts : %d justes, %d faux (%d touches) ; longs : %d vers l\'original, %d mal transmis'
          % (ok, bad, hits, longs, longbad))
    return bad == 0 and longbad == 0


def export_cs():
    """src/TNPlusTurret.cs: the pieces for English (0) and French (1), zones first, hooks last"""
    out = ['// Generated by tools/turret_fix.py (python turret_fix.py cs > ../src/TNPlusTurret.cs): do not edit.',
           '// Shots through turrets: stale exclusion, vertical travel, short cylinder segments (see turret_fix.py).',
           'static class TurretFix', '{', '    public static readonly uint[][] At = {']
    built = [build(l)[0] for l in ('EN', 'FR')]
    for pieces in built: out.append('        new uint[] { ' + ', '.join('0x%X' % a for a, o, n in pieces) + ' },')
    out.append('    };')
    for field, idx in (('Old', 1), ('New', 2)):
        out.append('    public static readonly string[][] %s = {' % field)
        for pieces in built: out.append('        new string[] { ' + ', '.join('"%s"' % p[idx].hex().upper() for p in pieces) + ' },')
        out.append('    };')
    out.append('    public const int Zones = 4;                      // the first 4 pieces are code in free areas, then 3 hooks')
    out.append('}')
    return '\r\n'.join(out) + '\r\n'


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == 'test':
        sys.exit(0 if test() else 1)
    if len(sys.argv) > 1 and sys.argv[1] == 'cs':
        sys.stdout.write(export_cs()); sys.exit(0)
    for lang in ('EN', 'FR'):
        pieces, at = build(lang)
        print(lang, 'ray %#x vert %#x cyl %#x' % (at['ray'], at['vert'], at['cyl']),
              '| zone A jusqu\'a %#x, zone B jusqu\'a %#x' % (pieces[2][0] + len(pieces[2][2]), pieces[3][0] + len(pieces[3][2])))
        for a, o, nw in pieces:
            print('  %#x %s -> %s' % (a, o.hex() if any(o) else '(%d zeros)' % len(o), nw.hex()))
