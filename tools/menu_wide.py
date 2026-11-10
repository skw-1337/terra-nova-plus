# Vrai 16:9 : menus, briefings et videos 640x480 a leurs proportions (centres, noir sur les cotes) au lieu d'etre
# etires sur toute la largeur.
#
# Le jeu affiche ses ecrans 640x480 dans le mode VESA 0x101, que DOSBox etire en 16:9 (aspect = stretch, necessaire
# aux missions). Ici ces ecrans passent dans le mode 0x222 de DOSBox (848x480, vesa_modes = all, celui des missions
# en vrai 16:9) : l'image 640x480 du jeu y est dessinee a partir de la colonne 104, avec une ligne de 848 octets.
# Rien n'est change dans le code du jeu, tout passe par ses donnees :
#  - la bibliotheque 2D appelle, juste apres chaque changement de mode reussi du pilote video, une routine
#    facultative de la table du pilote (premiere entree, vide dans le jeu) : gr_set_mode, "mov eax, [ebp] / or eax,
#    eax / jz / call eax", ebx = mode du pilote. Notre routine s'y met. Pour le mode 20 (640x480, 256 couleurs) :
#    le numero VBE de l'entree 20 de la table des modes du pilote (remplie au demarrage par sa detection : 0x101)
#    devient 0x222 et le mode est refait par le pilote lui-meme ; si DOSBox le refuse (pas de vesa_modes = all),
#    retour a 0x101 et la routine se retire. Puis la ligne VESA est remise a 848 pixels (le pilote la fixe a la
#    largeur de sa table, 640 : meme appel 4F06 que le HD large), et la bibliotheque recoit l'adresse de l'ecran
#    decalee de 104 et la longueur de ligne 848 (variables qu'elle relit apres le pilote pour creer son ecran).
#  - les videos (Smacker) ont leur propre petit module SVGA, avec sa table de modes : son mode 640x480 devient
#    aussi 0x222 (table statique lue par sa detection, et table detectee si elle a deja eu lieu : numero et octets
#    par ligne), et son adresse d'ecran en cache est celle de l'ecran decalee de 104. Sans cela une video 640x480
#    remettrait 0x101 et le menu suivant serait dessine de travers (le jeu ne refait pas son mode apres une video
#    640x480, c'est le meme).
#  - les bandes laterales sont les pixels 0 laisses par le changement de mode, et la couleur 0 n'est pas noire dans
#    toutes les palettes des menus (0, 0, 17 dans celle des quartiers et de l'ecran de chargement, qui n'a aucun noir).
#    Une seconde routine prend la place de la routine de palette du pilote dans sa table (entree +0x20, mise par
#    notre routine des modes : le pilote l'ecrit a son initialisation) : elle l'appelle puis remet la couleur 0 du
#    DAC a noir, seulement pendant le mode 20 en 848x480.
# Les videos 320x200 (mode 13h) restent etirees.
#
# Emplacement : fin de la derniere page de l'objet de code (0x354C00, apres sa taille virtuelle : 0x354BC0 dans
# l'exe francais, 0x354940 dans l'anglais ; zeros dans le fichier, jamais utilisee). La zone libre de TN+ est pleine
# en 1280x720.
#   python menu_wide.py            affiche le code des deux exe
#   python menu_wide.py cs         table C# (src/TNPlusMenu.cs)
#   python menu_wide.py test       execute la routine dans unicorn (pilote simule)
#   python menu_wide.py inject     l'installe dans le jeu en cours (tnre.py, tests)
import struct, sys
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

CODE = 0x354C00
PAL_OFF = 0x100          # la routine de palette, apres celle des modes
OW, OX, VBE_GAME, VBE_WIDE, MODE = 848, 104, 0x101, 0x222, 20
# par exe : emplacement libre de la table du pilote (son entree +8 = changement de mode), table des modes du pilote,
# adresse lineaire de l'ecran du pilote, puis ce que la bibliotheque relit (adresse de l'ecran, longueur de ligne),
# structure d'appel en mode reel du pilote, et le module SVGA des videos (modes voulus, modes detectes, octets par
# ligne, cache de l'adresse d'ecran : physique, lineaire) ; adresse de l'appel dans gr_set_mode (verification)
EXES = [
    ('en', dict(slot=0x36D10C, setmode=0x308490, table=0x364DF0, lfb=0x364DE4, vbase=0x44B1B4, row=0x44B240,
                rmregs=0x45E318, smk_want=0x3645FC, smk_mode=0x44D200, smk_bpl=0x44D1D8, smk_phys=0x364648,
                smk_lin=0x36464C, site=0x308E23, mode=0x35F8D0, setpal=0x307EA8)),
    ('fr', dict(slot=0x36D1B8, setmode=0x308710, table=0x364E98, lfb=0x364E8C, vbase=0x44B264, row=0x44B2F0,
                rmregs=0x45E3C8, smk_want=0x3646A4, smk_mode=0x44D2B0, smk_bpl=0x44D288, smk_phys=0x3646F0,
                smk_lin=0x3646F4, site=0x3090A3, mode=0x35F978, setpal=0x308128)),
]
# gr_set_mode : call [ebp+8] (pilote) / or eax, eax / jnz echec / mov eax, [ebp] / or eax, eax / jz / call eax
SITE_SIG = 'ff55080bc0756d8b45000bc07402ffd0'


def source(a):
    vbe = a['table'] + 12 * MODE + 2
    return f"""
    cmp bx, {MODE}
    jne sortie
    pushad
    mov esi, {vbe:#x}
    cmp word ptr [esi], {VBE_WIDE:#x}
    je centre
    cmp word ptr [esi], {VBE_GAME:#x}
    jne fin
    mov word ptr [esi], {VBE_WIDE:#x}
    call refaire
    jz centre
    mov word ptr [esi], {VBE_GAME:#x}
    and dword ptr [{a['slot']:#x}], 0
    call refaire
    jmp fin
centre:
    mov edi, {a['rmregs']:#x}
    push 13
    pop ecx
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
    mov dword ptr [{a['rmregs'] + 0x1c:#x}], 0x4f06
    mov dword ptr [{a['rmregs'] + 0x18:#x}], {OW}
    mov edi, {a['rmregs']:#x}
    mov eax, 0x300
    mov ebx, 0x10
    xor ecx, ecx
    int 0x31
    mov word ptr [{a['row']:#x}], {OW}
    add dword ptr [{a['vbase']:#x}], {OX}
    cmp dword ptr [{a['slot'] + 0x20:#x}], {a['setpal']:#x}
    jne videos
    mov dword ptr [{a['slot'] + 0x20:#x}], {CODE + PAL_OFF:#x}
videos:
    mov dword ptr [{a['smk_want'] + 4:#x}], {VBE_WIDE:#x}
    cmp dword ptr [{a['smk_mode'] + 4:#x}], {VBE_GAME:#x}
    jne cache
    mov dword ptr [{a['smk_mode'] + 4:#x}], {VBE_WIDE:#x}
    mov dword ptr [{a['smk_bpl'] + 4:#x}], {OW}
cache:
    mov eax, dword ptr [{a['lfb']:#x}]
    mov dword ptr [{a['smk_phys']:#x}], eax
    add eax, {OX}
    mov dword ptr [{a['smk_lin']:#x}], eax
fin:
    popad
sortie:
    ret
refaire:
    push {MODE}
    pop eax
    mov edx, dword ptr [esp+48]
    call dword ptr [{a['slot'] + 8:#x}]
    test eax, eax
    ret
"""


def source_pal(a):
    vbe = a['table'] + 12 * MODE + 2
    return f"""
    call {a['setpal']:#x}
    cmp dword ptr [{a['mode']:#x}], {MODE}
    jne sortie
    cmp word ptr [{vbe:#x}], {VBE_WIDE:#x}
    jne sortie
    push eax
    push edx
    mov edx, 0x3c8
    xor eax, eax
    out dx, al
    inc edx
    out dx, al
    out dx, al
    out dx, al
    pop edx
    pop eax
sortie:
    ret
"""


def build(exe):
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    a = EXES[exe][1]
    main = bytes(ks.asm(source(a), CODE)[0])
    assert len(main) <= PAL_OFF
    return main.ljust(PAL_OFF, b'\x90') + bytes(ks.asm(source_pal(a), CODE + PAL_OFF)[0])


def cs():
    out = ['// Generated by tools/menu_wide.py (python menu_wide.py cs > ../src/TNPlusMenu.cs): do not edit.',
           '// True 16:9: the 640x480 screens (menus, briefings, their videos) centred in DOSBox\'s 848x480 mode instead of',
           '// stretched. English then French exe (see menu_wide.py).', 'static class MenuWide', '{',
           '    public const uint Code = 0x%X;' % CODE,
           '    public const string SiteSig = "%s";      // gr_set_mode: the call of the driver, then of the free slot' % SITE_SIG.upper()]
    row = lambda k: ', '.join('0x%X' % a[k] for _, a in EXES)
    out += ['    public static readonly uint[] Site = { %s }, Slot = { %s }, SetMode = { %s };' % (row('site'), row('slot'), row('setmode')),
            '    public static readonly uint[] Vbe = { %s };      // VBE number of the driver\'s 640x480 mode: 0x%X once the hook ran'
            % (', '.join('0x%X' % (a['table'] + 12 * MODE + 2) for _, a in EXES), VBE_WIDE),
            '    public static readonly uint[] Mode = { %s };     // the driver\'s current mode (%d = 640x480, -1 before its first one)' % (row('mode'), MODE),
            '    public const int Menus = %d, VbeGame = 0x%X, VbeWide = 0x%X;' % (MODE, VBE_GAME, VBE_WIDE),
            '    public static readonly string[] CodeHex = {']
    out += ['        "%s",' % build(e).hex().upper() for e in range(len(EXES))]
    out += ['    };', '}', '']
    return '\n'.join(out)


def test():
    """La routine dans unicorn : pilote simule (note ses appels, repond selon le mode VBE de la table), int 31h note."""
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_INTR, UC_HOOK_CODE
    from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_EDX, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP, UC_X86_REG_ECX
    ok = True
    for exe, (name, a) in enumerate(EXES):
        code = build(exe)
        for cas in ('premier', 'deja large', 'refuse', 'autre mode', 'video detectee', 'numero inattendu', 'autre palette'):
            mu = Uc(UC_ARCH_X86, UC_MODE_32)
            mu.mem_map(0x244000, 0x230000); mu.mem_map(0x100000, 0x10000)
            mu.mem_write(CODE, code)
            DRV, RET = 0x300000, 0x301000
            mu.mem_write(DRV, b'\xc3'); mu.mem_write(RET, b'\xf4')
            w32 = lambda ad, v: mu.mem_write(ad, struct.pack('<I', v & 0xffffffff))
            r32 = lambda ad: struct.unpack('<I', mu.mem_read(ad, 4))[0]
            r16 = lambda ad: struct.unpack('<H', mu.mem_read(ad, 2))[0]
            vbe = a['table'] + 12 * MODE + 2
            mu.mem_write(a['table'] + 12 * MODE, struct.pack('<4H', MODE, {'deja large': VBE_WIDE, 'numero inattendu': 0x110}.get(cas, VBE_GAME), 640, 480))
            w32(a['slot'], CODE); w32(a['slot'] + 8, DRV)
            pal0 = 0x31D470 if cas == 'autre palette' else a['setpal']     # (la routine VGA du pilote : laissee)
            w32(a['slot'] + 0x20, pal0)
            w32(a['lfb'], 0xC0000000); w32(a['vbase'], 0xC0000000)
            mu.mem_write(a['smk_want'], struct.pack('<5I', 0x13, 0x101, 0x103, 0x105, 0x107))
            if cas == 'video detectee':
                mu.mem_write(a['smk_mode'], struct.pack('<5I', 0, 0x101, 0x103, 0x105, 0x107))
                mu.mem_write(a['smk_bpl'], struct.pack('<5I', 0, 640, 800, 1024, 1280))
            calls, ints = [], []

            def on_code(uc, addr, size, _):
                if addr == DRV:                       # pilote : eax = mode, edx = drapeaux ; 0 = reussi
                    v = r16(vbe)
                    calls.append((uc.reg_read(UC_X86_REG_EAX), uc.reg_read(UC_X86_REG_EDX), v))
                    uc.reg_write(UC_X86_REG_EAX, 0xffffffff if (cas == 'refuse' and v == VBE_WIDE) else 0)
                    uc.reg_write(UC_X86_REG_EDX, 0x4f02)          # le pilote ne garde pas edx
                    w32(a['vbase'], 0xC0000000); mu.mem_write(a['row'], b'\0\0')

            def on_int(uc, n, _):
                ints.append((n, uc.reg_read(UC_X86_REG_EAX), uc.reg_read(UC_X86_REG_EBX) & 0xffff, uc.reg_read(UC_X86_REG_EDI),
                             r32(a['rmregs'] + 0x1c), r32(a['rmregs'] + 0x18)))
            mu.hook_add(UC_HOOK_CODE, on_code, begin=DRV, end=DRV)
            mu.hook_add(UC_HOOK_INTR, on_int)
            # pile comme dans gr_set_mode : retour, edi, ebp, edx (drapeaux), ecx, ebx
            sp = 0x108000
            mu.mem_write(sp, struct.pack('<6I', RET, 0x11, 0x22, 3, 0x44, 0x55))
            regs = {UC_X86_REG_ESP: sp, UC_X86_REG_EBX: 12 if cas == 'autre mode' else MODE, UC_X86_REG_EDX: 0x4f02,
                    UC_X86_REG_ESI: 0x1234, UC_X86_REG_EDI: 0x5678, UC_X86_REG_EBP: a['slot'], UC_X86_REG_ECX: 0x9abc, UC_X86_REG_EAX: 0}
            for k, v in regs.items(): mu.reg_write(k, v)
            mu.emu_start(CODE, RET, count=4000)
            kept = all(mu.reg_read(k) == v for k, v in regs.items() if k not in (UC_X86_REG_ESP, UC_X86_REG_EAX)) and mu.reg_read(UC_X86_REG_ESP) == sp + 4
            wide = dict(vbe=r16(vbe), slot=r32(a['slot']), row=r16(a['row']), vbase=r32(a['vbase']), want=r32(a['smk_want'] + 4),
                        smode=r32(a['smk_mode'] + 4), sbpl=r32(a['smk_bpl'] + 4), phys=r32(a['smk_phys']), lin=r32(a['smk_lin']))
            centred = dict(row=OW, vbase=0xC0000000 + OX, want=VBE_WIDE, phys=0xC0000000, lin=0xC0000000 + OX)
            pal_ok = r32(a['slot'] + 0x20) == (CODE + PAL_OFF if cas in ('premier', 'video detectee', 'deja large') else pal0)
            if cas in ('premier', 'video detectee', 'autre palette'):
                good = calls == [(MODE, 3, VBE_WIDE)] and wide['vbe'] == VBE_WIDE and wide['slot'] == CODE and all(wide[k] == v for k, v in centred.items()) \
                    and ints == [(0x31, 0x300, 0x10, a['rmregs'], 0x4f06, OW)] \
                    and (wide['smode'], wide['sbpl']) == ((VBE_WIDE, OW) if cas == 'video detectee' else (0, 0))
            elif cas == 'deja large':
                good = calls == [] and all(wide[k] == v for k, v in centred.items()) and len(ints) == 1 and wide['slot'] == CODE
            elif cas == 'refuse':
                good = calls == [(MODE, 3, VBE_WIDE), (MODE, 3, VBE_GAME)] and wide['vbe'] == VBE_GAME and wide['slot'] == 0 and wide['row'] == 0 \
                    and wide['vbase'] == 0xC0000000 and wide['want'] == 0x101 and not ints
            else:
                good = calls == [] and not ints and wide['row'] == 0 and wide['vbase'] == 0xC0000000 and wide['want'] == 0x101 and wide['slot'] == CODE
            ok &= good and kept and pal_ok
            print('%s %-17s %s%s%s' % (name, cas, 'ok' if good else 'ECHEC %s %s %s' % (calls, ints, wide), '' if kept else ' REGISTRES',
                                        '' if pal_ok else ' PALETTE %#x' % r32(a['slot'] + 0x20)))
        # la routine de palette : celle du pilote d'abord, puis la couleur 0 a noir pendant l'ecran centre seulement
        from unicorn import UC_HOOK_INSN
        from unicorn.x86_const import UC_X86_INS_OUT
        for cas, mode, v, want in (('ecran centre', MODE, VBE_WIDE, [(0x3c8, 0), (0x3c9, 0), (0x3c9, 0), (0x3c9, 0)]),
                                   ('mission', 19, VBE_WIDE, []), ('640x480 du jeu', MODE, VBE_GAME, [])):
            mu = Uc(UC_ARCH_X86, UC_MODE_32)
            mu.mem_map(0x244000, 0x230000); mu.mem_map(0x100000, 0x10000)
            mu.mem_write(CODE, code)
            RET = 0x301000
            mu.mem_write(RET, b'\xf4')
            mu.mem_write(a['setpal'], b'\xb8\x77\x00\x00\x00\xba\xda\x03\x00\x00\xc3')     # rend eax = 0x77, edx change
            mu.mem_write(a['mode'], struct.pack('<i', mode))
            mu.mem_write(a['table'] + 12 * MODE + 2, struct.pack('<H', v))
            outs, seen = [], []
            mu.hook_add(UC_HOOK_INSN, lambda uc, port, size, value, _: outs.append((port, value & 0xff)), None, 1, 0, UC_X86_INS_OUT)
            mu.hook_add(UC_HOOK_CODE, lambda uc, addr, size, _: seen.append((uc.reg_read(UC_X86_REG_EAX), uc.reg_read(UC_X86_REG_EDX), uc.reg_read(UC_X86_REG_EBX))),
                        begin=a['setpal'], end=a['setpal'])
            sp = 0x108000
            mu.mem_write(sp, struct.pack('<I', RET))
            for k, val in ((UC_X86_REG_ESP, sp), (UC_X86_REG_EAX, 5), (UC_X86_REG_EDX, 251), (UC_X86_REG_EBX, 0x44AF4F), (UC_X86_REG_ECX, 0x36D1B8)):
                mu.reg_write(k, val)
            mu.emu_start(CODE + PAL_OFF, RET, count=400)
            good = outs == want and seen == [(5, 251, 0x44AF4F)] and mu.reg_read(UC_X86_REG_EAX) == 0x77 and mu.reg_read(UC_X86_REG_EDX) == 0x3da \
                and mu.reg_read(UC_X86_REG_EBX) == 0x44AF4F and mu.reg_read(UC_X86_REG_ESP) == sp + 4
            ok &= good
            print('%s palette, %-14s %s' % (name, cas, 'ok' if good else 'ECHEC %s %s' % (outs, seen)))
    print('tout est bon' if ok else 'ECHEC')
    return ok


def inject():
    import os, time
    sys.path.insert(0, os.environ.get('TNRE_DIR', r'C:\Program Files (x86)\GOG Galaxy\Games\Terra Nova Strike Force Centauri\_MODS\re'))
    import tnre
    g = tnre.Guest()
    for exe, (name, a) in enumerate(EXES):
        if g.read(a['site'], len(SITE_SIG) // 2) != bytes.fromhex(SITE_SIG): continue
        code = build(exe)
        assert g.dword(a['slot'] + 8) == a['setmode'], 'table du pilote pas encore en place'
        cur = g.dword(a['slot'])
        if cur == CODE and g.read(CODE, len(code)) == code:
            print('deja en place (%s)' % name); return
        assert cur == 0 and not any(g.read(CODE, len(code))), 'emplacement occupe'
        g.write(CODE, code)
        g.write(a['slot'], struct.pack('<I', CODE))
        print('routine des menus en place (%s, %d octets), numero VBE du mode 20 : %#x' % (name, len(code), struct.unpack('<H', g.read(a['table'] + 12 * MODE + 2, 2))[0]))
        return
    print('jeu pas reconnu (code pas encore charge ?)')


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    if cmd == 'cs':
        sys.stdout.write(cs())
    elif cmd == 'test':
        sys.exit(0 if test() else 1)
    elif cmd == 'inject':
        inject()
    else:
        for e, (name, a) in enumerate(EXES):
            c = build(e)
            print('%s  %#x  %d octets\n%s' % (name, CODE, len(c), c.hex()))
