# Remappage des touches du jeu (onglet KEYBOARD de Terra Nova Plus) : routine de traduction appelee par le
# gestionnaire clavier du jeu (IRQ1), et installateur execute PAR LE JEU au premier reglage de camera d'une mission
# (le gestionnaire tourne depuis le lancement : son code est dans le cache du coeur dynamique de DOSBox, un patch
# ecrit de l'exterieur n'y prendrait pas ; une ecriture du jeu lui-meme invalide le cache).
#
# Gestionnaire (bibliotheque clavier du jeu, EN 0x31AAE0 / FR 0x31AD60) : file d'evenements de mots
# (code | appui << 8) en [kb + 0x10 .. 0x410], index d'ecriture [kb], etat des touches [kb + 0x410 + code] (bit 0
# enfoncee). Le jeu suit Alt / Ctrl / Shift d'apres les evenements de la file (mot de drapeaux 0xF8290, 0x301CE0) :
# des evenements de modificateurs inseres dans la file font donc n'importe quelle combinaison.
# Site : les 11 octets "movsx eax, al / and eax, 0x17f / xor ah, 1" du chemin normal (hors prefixe E0), reperes par
# signature ; reprise (touche avalee) a site + 0x6A.
#
#   python kb_cave.py          assemble, teste dans unicorn, ecrit kb_cave.json (pour TNPlus)
import json, os, struct, sys
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

KD = 0x469A10                        # donnees (page sans code)
ACTIVE, STATE, SITE, RESUME, PM, PATCH = KD, KD + 4, KD + 8, KD + 0xC, KD + 0x10, KD + 0x14
ACT, MAP = KD + 0x20, KD + 0x120     # ACT : 128 mots, MAP : 128 x 4 mots (sans, Shift, Ctrl, Alt)
KD_END = MAP + 128 * 4 * 2
assert KD_END <= 0x46A000
KC = 0x46AA20                        # code (apres les objets des bords, 0x46A300-0x46AA16)
SWEEP = 0x46A1A0                     # balayage anti-touche bloquee (trou libre 0x46A19E-0x46A200 de la page de code)
HOOK = {'EN': 0x29E838, 'FR': 0x29E828}   # reglage de camera : mov [esp+4], edx / mov [esp+8], ebx
HOOK_OLD = bytes.fromhex('8954240489' + '5c2408')
SITE_SIG = bytes.fromhex('0fbec0257f01000080f4010ae47423')
RESUME_SIG = bytes.fromhex('c1e2088ad0895304')

# ACT : bit 15 actif, bits 0-7 destination (0xFF avalee), 8/9/10 Shift/Ctrl/Alt ajoutes, 11/12/13 Shift/Ctrl/Alt caches ;
#       bit 14 seul = appuyee telle quelle (hors mission, sans traduction, Alt tenu...) : elle reste telle quelle
#       jusqu'au relachement (sinon une repetition traduite en cours d'appui, apres l'entree en mission ou un Alt
#       relache, laissait la touche d'origine enfoncee pour le jeu : joueur qui tourne seul) ;
#       0xC0FF = accord du jeu frappe, la touche attend qu'Alt soit lache pour passer a sa traduction simple
# MAP : 0 rien ; bit 15 defini, bits 0-7 destination (0xFF = touche retiree), bits 8-9 modificateur voulu (0 aucun,
#       1 Shift, 2 Ctrl, 3 Alt) ; 0x4000 (Alt seulement) = accord du jeu reste sur une touche deplacee (Alt+D prudent
#       quand D est un pas de cote).
#       Shift ou Ctrl seul : entree exacte, puis l'entree sans modificateur (si elle n'en veut aucun, les modificateurs
#       tenus passent : Shift + deplacement = bond, Ctrl = drone). Ctrl + Shift : pareil sans entree exacte.
#       Alt tenu (seul ou avec d'autres) sur une touche deplacee sans accord a elle (ou dont l'accord est retire) :
#       sa traduction simple, Alt (et les autres) caches pendant l'appui et remis au relachement. Avant, la touche
#       passait telle quelle et le restait jusqu'au relachement : Q / D en AZERTY = les touches tourner du jeu, le
#       joueur tournait en pas de cote apres un ordre Alt+A. Accord du jeu (0x4000) : l'ordre frappe (appui et
#       relachement de suite : il part, la touche du jeu ne reste pas enfoncee), puis la traduction simple quand Alt
#       est lache. Touche non deplacee : telle quelle (Alt+T, Alt+N...), Alt remis pour le jeu s'il etait cache.
#       Les modificateurs a cacher ou ajouter se lisent dans l'etat des touches du jeu (pas les touches physiques) :
#       un Alt+M frappe pendant un pas de cote a Alt cache remet Alt et donne bien l'ordre.
def make_asm(TABS):
    return f"""
kb_install:
    cmp dword ptr [{STATE:#x}], 1
    jne ki_done
    pushfd
    cli
    push esi
    push edi
    push ecx
    mov esi, {PATCH:#x}
    mov edi, dword ptr [{SITE:#x}]
    mov ecx, 11
    rep movsb byte ptr es:[edi], byte ptr [esi]
    pop ecx
    pop edi
    pop esi
    popfd
    mov dword ptr [{STATE:#x}], 2
ki_done:
    mov dword ptr [esp+8], edx
    mov dword ptr [esp+12], ebx
    ret

kb_emit:
    push ecx
    movzx ecx, al
    test ah, 1
    jz em_up
    or byte ptr [ebx+ecx+1040], 1
    jmp em_q
em_up:
    and byte ptr [ebx+ecx+1040], 254
em_q:
    mov ecx, dword ptr [ebx]
    and ecx, 65535
    mov word ptr [ecx+ebx], ax
    add ecx, 2
    cmp ecx, 1040
    jb em_ok
    mov ecx, 16
em_ok:
    mov dword ptr [ebx], ecx
    pop ecx
    ret

kb_keys:
    pushad
    mov edx, eax
kk_l:
    bsf eax, ecx
    jz kk_d
    btr ecx, eax
    movzx eax, byte ptr [eax+{TABS:#x}]
    or eax, edx
    call kb_emit
    jmp kk_l
kk_d:
    popad
    ret

kb_map:
    push edi
    push ebp
    push edx
    movzx eax, al
    push eax
    mov ecx, eax
    and eax, 127
    xor ebp, ebp
    mov edx, 3
km_ml:
    cmp al, byte ptr [edx+{TABS:#x}]
    jne km_mn
    bts ebp, edx
km_mn:
    dec edx
    jns km_ml
    test ebp, ebp
    jz km_nomod
    test cl, 128
    jnz km_modup
    test dword ptr [{PM:#x}], ebp
    jnz km_swallow
    or dword ptr [{PM:#x}], ebp
km_ident:
    pop ecx
    mov eax, ecx
    and eax, 127
    xor ecx, 128
    shl ecx, 1
    and ecx, 256
    or eax, ecx
    pop edx
    pop ebp
    pop edi
    ret
km_swallow:
    pop eax
    pop edx
    pop ebp
    pop edi
    mov ecx, dword ptr [{RESUME:#x}]
    mov dword ptr [esp], ecx
    ret
km_modup:
    not ebp
    and dword ptr [{PM:#x}], ebp
    cmp ebp, -9
    jne km_ident
    xor ecx, ecx
    mov edi, 127
ku_l:
    cmp word ptr [edi*2+{ACT:#x}], 49407
    jne ku_n
    test ecx, ecx
    jnz ku_1
    inc ecx
    call kb_emit
ku_1:
    movzx esi, byte ptr [edi*8+{MAP:#x}]
    mov ebp, esi
    or ebp, 32768
    mov word ptr [edi*2+{ACT:#x}], bp
    push eax
    lea eax, [esi+256]
    call kb_emit
    pop eax
ku_n:
    dec edi
    jns ku_l
    test ecx, ecx
    jz km_ident
    jmp km_swallow
km_nomod:
    test cl, 128
    jnz km_rel
    movzx esi, word ptr [eax*2+{ACT:#x}]
    test esi, 32768
    jnz km_repeat
    test esi, 16384
    jnz km_ident
    cmp dword ptr [{ACTIVE:#x}], 0
    je km_raw
    mov edx, dword ptr [{PM:#x}]
    mov ecx, edx
    and ecx, 12
    shr ecx, 1
    test edx, 3
    jz km_k1
    or ecx, 1
km_k1:
    movzx esi, word ptr [eax*8+{MAP:#x}]
    mov edi, ecx
    cmp edi, 4
    jne km_k2
    mov edi, 3
    jmp km_kind
km_k2:
    cmp edi, 3
    jae km_combo
km_kind:
    lea edx, [eax*4+edi]
    movzx edx, word ptr [edx*2+{MAP:#x}]
    test edx, 32768
    jz km_noex
    cmp edi, 3
    jne km_exact
    cmp edx, 33023
    jne km_exact
    call km_real
    jnz km_fake
    jmp km_block
km_exact:
    mov esi, edx
    jmp km_fake
km_noex:
    test edi, edi
    jz km_raw
    cmp edi, 3
    jne km_plain
    test edx, 16384
    jnz km_tap
km_combo:
    call km_real
    jz km_raw
km_plain:
    test esi, 32768
    jz km_raw
    test ecx, 4
    jnz km_fake
    test esi, 768
    jnz km_fake
    mov edi, esi
    and edi, 255
    cmp edi, 255
    je km_block
    jmp km_press
km_real:
    test esi, 32768
    jz kr_no
    mov edx, esi
    not edx
    test edx, 255
    ret
kr_no:
    cmp esi, esi
    ret
km_tap:
    call km_altsync
    push eax
    or eax, 256
    call kb_emit
    mov eax, dword ptr [esp]
    call kb_emit
    pop eax
    mov word ptr [eax*2+{ACT:#x}], 49407
    jmp km_swallow
km_fake:
    mov edi, esi
    and edi, 255
    cmp edi, 255
    je km_block
    shr esi, 8
    and esi, 3
    xor ebp, ebp
    push edi
    push eax
    xor edx, edx
    mov ecx, 4
kf_g:
    movzx eax, byte ptr [ecx+{TABS:#x}-1]
    test byte ptr [ebx+eax+1040], 1
    jz kf_g2
    bts edx, ecx
kf_g2:
    loop kf_g
    shr edx, 1
    mov edi, 1
kf_l:
    movzx ecx, byte ptr [edi+{TABS+4:#x}-1]
    and ecx, edx
    cmp edi, esi
    jne kf_h
    test ecx, ecx
    jnz kf_n
    lea eax, [edi+7]
    bts ebp, eax
    movzx ecx, byte ptr [edi+{TABS+7:#x}-1]
    mov eax, 256
    call kb_keys
    jmp kf_n
kf_h:
    jecxz kf_n
    lea eax, [edi+10]
    bts ebp, eax
    xor eax, eax
    call kb_keys
kf_n:
    inc edi
    cmp edi, 4
    jb kf_l
    pop eax
    pop edi
    or ebp, edi
    jmp km_set
km_press:
    mov ebp, edi
km_set:
    or ebp, 32768
    mov word ptr [eax*2+{ACT:#x}], bp
    push eax
    mov eax, edi
    or eax, 256
    call kb_emit
    pop eax
    jmp km_swallow
km_block:
    mov word ptr [eax*2+{ACT:#x}], 33023
    jmp km_swallow
km_raw:
    mov word ptr [eax*2+{ACT:#x}], 16384
    call km_altsync
    jmp km_ident
km_altsync:
    test byte ptr [{PM:#x}], 8
    jz as_d
    test byte ptr [ebx+1096], 1
    jnz as_d
    push eax
    mov eax, 312
    call kb_emit
    pop eax
as_d:
    ret
km_repeat:
    test byte ptr [ebx+1096], 1
    jnz km_swallow
    mov edi, esi
    and edi, 255
    cmp edi, 255
    je km_swallow
    push eax
    mov eax, edi
    or eax, 256
    call kb_emit
    pop eax
    jmp km_swallow
km_rel:
    movzx esi, word ptr [eax*2+{ACT:#x}]
    mov word ptr [eax*2+{ACT:#x}], 0
    test esi, 32768
    jz km_ident
    mov edi, esi
    and edi, 255
    cmp edi, 255
    je km_swallow
    push eax
    mov eax, edi
    call kb_emit
    mov edx, dword ptr [{PM:#x}]
    mov edi, 1
kr_l:
    movzx ecx, byte ptr [edi+{TABS+4:#x}-1]
    and ecx, edx
    lea eax, [edi+7]
    bt esi, eax
    jnc kr_h
    test ecx, ecx
    jnz kr_h
    movzx ecx, byte ptr [edi+{TABS+7:#x}-1]
    xor eax, eax
    call kb_keys
kr_h:
    lea eax, [edi+10]
    bt esi, eax
    jnc kr_n
    mov eax, 256
    call kb_keys
kr_n:
    inc edi
    cmp edi, 4
    jb kr_l
    call {SWEEP:#x}
    pop eax
    jmp km_swallow
"""


TABLES = bytes([0x2A, 0x36, 0x1D, 0x38, 3, 4, 8, 1, 4, 8])   # Shift L / R, Ctrl, Alt keys; class masks; class keys


def assemble():
    global ASM
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    ASM = make_asm(0x46AFF0)
    n = len(bytes(ks.asm(ASM, KC)[0]))
    ASM = make_asm(KC + n)                                 # (same length: the table address is 32 bits either way)
    code = bytes(ks.asm(ASM, KC)[0]) + TABLES
    assert len(code) == n + len(TABLES)
    lab = {}
    for name in ('kb_install', 'kb_emit', 'kb_map'):
        lab[name] = KC + len(bytes(ks.asm(ASM.split('\n%s:' % name)[0], KC)[0])) if ASM.split('\n%s:' % name)[0].strip() else KC
    return code, lab


CODE, LAB = assemble()
# Balayage : une touche du jeu enfoncee (etat [kb + 0x410 + g]) que plus aucune touche physique n'explique (ni une
# traduction en cours vers g, ni g appuyee telle quelle) est relachee pour le jeu. Appele a chaque relachement d'une
# touche traduite : si un evenement parasite a laisse une touche enfoncee (joueur qui tourne seul), elle se relache
# des qu'on lache une touche de deplacement. Modificateurs (Ctrl, Shift, Alt) et touches etendues ignores.
SWEEP_ASM = f"""
    pushad
    xor ecx, ecx
ks_g:
    test byte ptr [ebx+ecx+1040], 1
    jz ks_next
    cmp ecx, 29
    je ks_next
    cmp ecx, 42
    je ks_next
    cmp ecx, 54
    je ks_next
    cmp ecx, 56
    je ks_next
    test word ptr [ecx*2+{ACT:#x}], 16384
    jnz ks_next
    xor edx, edx
ks_i:
    test byte ptr [edx*2+{ACT + 1:#x}], 128
    jz ks_inext
    cmp byte ptr [edx*2+{ACT:#x}], cl
    je ks_next
ks_inext:
    inc edx
    cmp dl, 128
    jb ks_i
    mov eax, ecx
    call {LAB['kb_emit']:#x}
ks_next:
    inc ecx
    cmp cl, 128
    jb ks_g
    popad
    ret
"""
SWEEP_CODE = bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm(SWEEP_ASM, SWEEP)[0])
assert SWEEP + len(SWEEP_CODE) <= 0x46A200, hex(SWEEP + len(SWEEP_CODE))
assert KC + len(CODE) <= 0x46AE54, hex(KC + len(CODE))   # (the turret fix's code follows: turret_fix.py)
# edx est conserve : le gestionnaire s'en sert apres (historique des octets, prefixe E0)
assert 'edx' in ASM


# ------------------------------------------------------------------ tests dans unicorn
def test():
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
    from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_EDX, UC_X86_REG_ESP, UC_X86_REG_EIP
    KB = 0x500000; STK = 0x600000; RET = 0x700000; RES = 0x700100
    MIN, MOD, SHF, CTL, ALT = 0, 0, 1, 2, 3
    fails = []

    def run(seq, maps, active=1, pm=0, pre=(), tapk=()):
        mu = Uc(UC_ARCH_X86, UC_MODE_32)
        mu.mem_map(0x460000, 0x10000); mu.mem_map(KB, 0x1000); mu.mem_map(STK - 0x1000, 0x2000); mu.mem_map(RET, 0x1000)
        mu.mem_write(KC, CODE); mu.mem_write(SWEEP, SWEEP_CODE)
        mu.mem_write(KB, struct.pack('<I', 0x10))
        for k in pre: mu.mem_write(KB + 0x410 + k, b'\x01')
        m = bytearray(128 * 8)
        for (sc, kind), (dst, dk) in maps.items():
            struct.pack_into('<H', m, (sc * 4 + kind) * 2, 0x8000 | dst | (dk << 8))
        for k in tapk: struct.pack_into('<H', m, (k * 4 + 3) * 2, 0x4000)
        mu.mem_write(MAP, bytes(m)); mu.mem_write(RESUME, struct.pack('<I', RES))
        mu.mem_write(PM, struct.pack('<I', pm))
        mu.mem_write(RET, b'\x90' * 0x200)
        from unicorn import UC_HOOK_CODE
        passe = [False]
        def vu(uc, addr, size, ud): passe[0] = True
        mu.hook_add(UC_HOOK_CODE, vu, begin=RET, end=RET)
        out = []
        for n, raw in enumerate(seq):
            mu.mem_write(ACTIVE, struct.pack('<I', active[n] if isinstance(active, list) else active))
            mu.reg_write(UC_X86_REG_EAX, raw); mu.reg_write(UC_X86_REG_EBX, KB); mu.reg_write(UC_X86_REG_EDX, 0x1234)
            mu.reg_write(UC_X86_REG_ESP, STK); mu.mem_write(STK, struct.pack('<I', RET))
            q0 = struct.unpack('<I', mu.mem_read(KB, 4))[0]
            passe[0] = False
            mu.emu_start(LAB['kb_map'], RES, count=4000)
            assert mu.reg_read(UC_X86_REG_EIP) == RES, 'sortie inattendue'
            eip = RET if passe[0] else RES
            esp_ret = mu.reg_read(UC_X86_REG_ESP)
            assert mu.reg_read(UC_X86_REG_EDX) == 0x1234, 'edx modifie'
            q1 = struct.unpack('<I', mu.mem_read(KB, 4))[0]
            evs = []
            i = q0
            while i != q1:
                evs.append(struct.unpack('<H', mu.mem_read(KB + i, 2))[0]); i = i + 2 if i + 2 < 0x410 else 0x10
            if eip == RET:
                v = mu.reg_read(UC_X86_REG_EAX) & 0x1ff
                evs.append(('isr', v))
                st = mu.mem_read(KB + 0x410 + (v & 0x7F), 1)[0]
                mu.mem_write(KB + 0x410 + (v & 0x7F), bytes([(st | 1) if v & 0x100 else (st & 0xFE)]))
            out.append(evs)
        return out

    def check(nom, got, want):
        if got != want:
            fails.append(nom); print('ECHEC', nom, got, '!=', want)
        else:
            print('ok', nom)

    W, K, A, H, X, T, LSH, CT, AL = 0x11, 0x25, 0x1E, 0x23, 0x2D, 0x14, 0x2A, 0x1D, 0x38
    # 1. sans remappage : tout passe tel quel au gestionnaire
    check('identite', run([W, W | 0x80], {}), [[('isr', 0x100 | W)], [('isr', W)]])
    # 2. Forward W -> K : K devient W (appui, repetition, relachement), W est retire
    mp = {(K, MIN): (W, 0), (W, MIN): (0xFF, 0)}
    check('K -> W', run([K, K, K | 0x80], mp), [[0x100 | W], [0x100 | W], [W]])
    check('W retire', run([W, W | 0x80], mp), [[], []])
    # 3. inactif (hors mission) : identite, mais un relachement en cours reste traduit
    check('inactif', run([K, K | 0x80], mp, active=0), [[('isr', 0x100 | K)], [('isr', K)]])
    # 4. Shift + K -> Shift + W (bond), Shift passe tel quel
    check('bond', run([LSH, K, K | 0x80, LSH | 0x80], mp), [[('isr', 0x100 | LSH)], [0x100 | W], [W], [('isr', LSH)]])
    # 5. Hold fire (Alt+H) sur K seul : Alt simule autour de H
    mp2 = {(K, MIN): (H, 3), (H, ALT): (0xFF, 0)}
    check('K -> Alt+H', run([K, K | 0x80], mp2), [[0x100 | AL, 0x100 | H], [H, AL]])
    # 6. Alt+H retire, mais H seul et Alt+autre passent
    check('Alt+H retire', run([AL, H, H | 0x80, AL | 0x80], mp2), [[('isr', 0x100 | AL)], [], [], [('isr', AL)]])
    # 7. Forward (W) sur Ctrl+K : Ctrl cache pendant W puis remis
    mp3 = {(K, CTL): (W, 0), (W, MIN): (0xFF, 0)}
    check('Ctrl+K -> W', run([CT, K, K | 0x80, CT | 0x80], mp3),
          [[('isr', 0x100 | CT)], [CT, 0x100 | W], [W, 0x100 | CT], [('isr', CT)]])
    # 8. Alt tenu + touche deplacee sans accord a elle : sa traduction simple, Alt cache pendant l'appui (pas d'ordre
    #    par accident, et plus jamais la touche d'origine telle quelle : joueur qui tournait), remis au relachement
    check('Alt+K -> W', run([AL, K, K | 0x80, AL | 0x80], mp), [[('isr', 0x100 | AL)], [AL, 0x100 | W], [W, 0x100 | AL], [('isr', AL)]])
    # 9. Exit game Alt+X sur Alt+T : exact Alt -> Alt
    mp4 = {(T, ALT): (X, 3), (X, ALT): (0xFF, 0)}
    check('Alt+T -> Alt+X', run([AL, T, T | 0x80, AL | 0x80], mp4), [[('isr', 0x100 | AL)], [0x100 | X], [X], [('isr', AL)]])
    # 10. appuyee hors mission, tenue pendant l'entree en mission : reste telle quelle jusqu'au relachement
    D, C = 0x20, 0x2E
    mp5 = {(D, MIN): (C, 0), (C, MIN): (D, 0)}
    check('tenue a l\'activation', run([D, D, D | 0x80], mp5, active=[0, 1, 1]), [[('isr', 0x100 | D)], [('isr', 0x100 | D)], [('isr', D)]])
    check('puis traduite', run([D, D | 0x80, D, D | 0x80], mp5, active=[0, 1, 1, 1]), [[('isr', 0x100 | D)], [('isr', D)], [0x100 | C], [C]])
    # 11. traduite, relachee hors mission : relachement traduit
    check('relachee a la sortie', run([D, D, D | 0x80], mp5, active=[1, 0, 0]), [[0x100 | C], [0x100 | C], [C]])
    # 12. Alt tenu a l'appui puis relache, repetition : reste telle quelle
    check('Alt relache pendant l\'appui', run([AL, C, AL | 0x80, C, C | 0x80], mp5),
          [[('isr', 0x100 | AL)], [AL, 0x100 | D], [('isr', AL)], [0x100 | D], [D]])
    # 13. touche parasite enfoncee pour le jeu sans touche physique : relachee au relachement d'une touche traduite
    check('touche parasite relachee', run([K, K | 0x80], mp, pre=[0x1E]), [[0x100 | W], [W, 0x1E]])
    check('parasite expliquee gardee', run([A, K, K | 0x80], mp, pre=[]), [[('isr', 0x100 | A)], [0x100 | W], [W]])
    # 14. disposition du proprietaire (AZERTY) : Q (1E) -> pas a gauche (2C), D (20) -> pas a droite (2E), Alt+A (10) ->
    #     ordre Alt+1E (ancien Alt+1E retire), Alt+M (27) -> ordre Alt+32, Alt+D (20) = l'ordre du jeu (prudent) laisse
    #     sur sa touche (marque 0x4000 : l'ordre donne d'une frappe, la touche attend qu'Alt soit lache)
    Q, Dk, Am, M, Tt, F = 0x1E, 0x20, 0x10, 0x27, 0x14, 0x21
    az = {(Q, MIN): (0x2C, 0), (Dk, MIN): (0x2E, 0), (Am, ALT): (0x1E, 3), (Q, ALT): (0xFF, 0), (M, ALT): (0x32, 3),
          (M, MIN): (0x32, 0)}
    check('Alt puis D : ordre du jeu, puis pas de cote', run([AL, Dk, AL | 0x80, Dk | 0x80], az, tapk=[Dk]),
          [[('isr', 0x100 | AL)], [0x100 | Dk, Dk], [AL, 0x100 | 0x2E], [0x2E]])
    check('Alt+D relache avant Alt', run([AL, Dk, Dk | 0x80, AL | 0x80], az, tapk=[Dk]),
          [[('isr', 0x100 | AL)], [0x100 | Dk, Dk], [], [('isr', AL)]])
    check('Alt+A puis Q avant Alt', run([AL, Am, Am | 0x80, Q, AL | 0x80, Q | 0x80], az),
          [[('isr', 0x100 | AL)], [0x100 | Q], [Q], [AL, 0x100 | 0x2C], [('isr', AL)], [0x2C]])
    check('Ctrl+Shift puis Q', run([CT, LSH, Q, Q | 0x80, LSH | 0x80, CT | 0x80], az),
          [[('isr', 0x100 | CT)], [('isr', 0x100 | LSH)], [0x100 | 0x2C], [0x2C], [('isr', LSH)], [('isr', CT)]])
    check('Ctrl puis Q', run([CT, Q, Q | 0x80, CT | 0x80], az), [[('isr', 0x100 | CT)], [0x100 | 0x2C], [0x2C], [('isr', CT)]])
    check('Alt tenu, Q puis Alt+M', run([AL, Q, M, M | 0x80, Q | 0x80, AL | 0x80], az),
          [[('isr', 0x100 | AL)], [AL, 0x100 | 0x2C], [0x100 | AL, 0x100 | 0x32], [0x32], [0x2C, 0x100 | AL], [('isr', AL)]])
    check('Alt tenu, Q puis Alt+T (touche du jeu)', run([AL, Q, Tt, Tt | 0x80, Q | 0x80, AL | 0x80], az),
          [[('isr', 0x100 | AL)], [AL, 0x100 | 0x2C], [0x100 | AL, ('isr', 0x100 | Tt)], [('isr', Tt)], [0x2C, 0x100 | AL], [('isr', AL)]])
    azf = dict(az); azf[(F, MIN)] = (0xFF, 0)
    check('Alt+F (touche retiree, ordre du jeu) tel quel', run([AL, F, F | 0x80, AL | 0x80], azf),
          [[('isr', 0x100 | AL)], [('isr', 0x100 | F)], [('isr', F)], [('isr', AL)]])
    check('Shift+Alt puis Q', run([LSH, AL, Q, Q | 0x80, AL | 0x80, LSH | 0x80], az),
          [[('isr', 0x100 | LSH)], [('isr', 0x100 | AL)], [LSH, AL, 0x100 | 0x2C], [0x2C, 0x100 | 0x2A, 0x100 | AL], [('isr', AL)], [('isr', LSH)]])
    check('QWERTY sans changement : Alt+A tel quel', run([AL, Q, Q | 0x80, AL | 0x80], {}),
          [[('isr', 0x100 | AL)], [('isr', 0x100 | Q)], [('isr', Q)], [('isr', AL)]])
    check('Q tenu puis Alt+A', run([Q, AL, Am, Am | 0x80, AL | 0x80, Q | 0x80], az),
          [[0x100 | 0x2C], [('isr', 0x100 | AL)], [0x100 | Q], [Q], [('isr', AL)], [0x2C]])
    # 15. repetitions (Uplink repete toutes les touches tenues, DOSBox la derniere) : un modificateur deja tenu n'est
    #     pas renvoye (il remettrait un Alt cache), une touche traduite ne se repete pas tant qu'Alt est enfonce pour
    #     le jeu (D tenu = C pour le jeu : Alt + C = l'ordre a couvert, a chaque repetition)
    check('repetitions sous Alt', run([AL, Q, AL, Q, AL | 0x80, Q, Q | 0x80], az),
          [[('isr', 0x100 | AL)], [AL, 0x100 | 0x2C], [], [0x100 | 0x2C], [('isr', AL)], [0x100 | 0x2C], [0x2C]])
    check('D tenu, Alt+A, repetition de D', run([Dk, AL, Am, Am | 0x80, Dk, AL | 0x80, Dk, Dk | 0x80], az),
          [[0x100 | 0x2E], [('isr', 0x100 | AL)], [0x100 | Q], [Q], [], [('isr', AL)], [0x100 | 0x2E], [0x2E]])
    print('ECHECS :', fails if fails else 'aucun')
    return not fails


if __name__ == '__main__':
    print('code %d octets en %#x-%#x, donnees %#x-%#x' % (len(CODE), KC, KC + len(CODE), KD, KD_END), {k: hex(v) for k, v in LAB.items()})
    ok = test()
    out = {'sweep_at': SWEEP, 'sweep': SWEEP_CODE.hex().upper(), 'code_at': KC, 'code': CODE.hex().upper(), 'install': LAB['kb_install'], 'map_fn': LAB['kb_map'],
           'data': KD, 'data_end': KD_END, 'active': ACTIVE, 'state': STATE, 'site': SITE, 'resume': RESUME, 'pm': PM,
           'patch': PATCH, 'act': ACT, 'map': MAP, 'hook': HOOK, 'hook_old': HOOK_OLD.hex().upper(),
           'site_sig': SITE_SIG.hex().upper(), 'resume_sig': RESUME_SIG.hex().upper()}
    json.dump(out, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'kb_cave.json'), 'w'), indent=1)
    sys.exit(0 if ok else 1)
