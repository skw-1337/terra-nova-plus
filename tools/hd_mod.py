# Terra Nova HD - injection en RAM.
#  phase 1 : la vue 3D principale (VIEW1, quel que soit son canvas : cockpit 298x199, vue entiere 320x390...) est
#            rendue en double largeur dans un tampon a part (malloc du jeu), puis reduite dans l'image de travail
#            (crochets avant/apres le rendu 3D 0x29E746 / 0x29E80C). Tables de la vue (lignes, colonnes) et somme de
#            controle 0x427EA8 refaites au vol pour la largeur du moment.
#  phase 2 : ecran VESA 640x400 (mode 19) ; l'ecran du jeu est detourne vers un tampon factice et, apres
#            chaque copie du jeu (0x261FE4), la moitie gauche de l'ecran factice (= l'ecran 320x400 du jeu, curseur
#            et reticule compris) est recopiee doublee en largeur dans la vraie memoire video lineaire, avec les
#            pixels HD a la place de la 3D (sauf la ou le HUD a dessine). Au detournement, l'ecran factice est
#            initialise avec l'image de travail (le decor fixe du cockpit a pu etre dessine sur le vrai ecran juste
#            apres le changement de mode) ; la taille d'ecran logique du jeu (0x44706C, aussi rectangle plein ecran
#            0x447068) et le cadre souris/curseur (0x2E476C) sont remis a 320x400 si le chemin de changement de mode
#            les a pris sur l'ecran 640x400 (relancer une mission, retour du menu O) ; la sous-zone d'ecran du curseur
#            0x4470C0, si elle a ete creee avant le detournement, est decalee vers l'ecran factice (sinon le curseur
#            est aussi dessine directement dans la memoire video : 2e viseur fin a mi-distance).
#  HUD : les dessins en 320x200 affiches en lignes doublees sont agrandis en Scale2x (seulement sur les contours
#            nets a 2 couleurs : texte, traits, ronds ; pas sur les textures) ; le reste est double tel quel.
#  apres le rendu HD : les rectangles ecran des objets (table 0x397D50, 10 o, nombre [0x35BFA0], refaite a chaque
#            image ; servent au clic droit de verrouillage et aux cadres de cible) sont ramenes a la largeur du jeu,
#            et la projection horizontale du moteur 3D (0x36237A/82/8A/92) aussi.
# + corrections des limites a 320/255 colonnes (cf. hd_fix.py) et tampon 0x41ED30 (copie de la vue 3D, 320x400)
#   agrandi a 640x400.
# A appliquer AU MENU PRINCIPAL (code de mission pas encore dans le cache dynamique de DOSBox).
# GOG francais, mode 400 lignes.
#   python hd_mod.py [--sans-ecran]   injecte (--sans-ecran = phase 1 seule)
#   python hd_mod.py etat             compteurs
#   python hd_mod.py shot NOM         sauve le tampon HD en PNG
#   python hd_mod.py lissage [on|off] lissage horizontal de la 3D (table de couleurs moyennes, actif par defaut)
#   python hd_mod.py hud [on|off]     Scale2x du HUD (actif par defaut)
#   python hd_mod.py export [FICHIER] ecrit le code assemble pour Terra Nova Plus (src/HdPayload.cs)
import json, os, re, struct, sys
import numpy as np
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

# --en : exe ANGLAIS (GOG EN = Steam). Memes routines, autres adresses : table FR -> EN construite par port_en.py
# (signatures de code), completee a la main pour les adresses que ses signatures ne couvrent pas (verifiees octet
# par octet). Le code est charge au meme endroit, l'objet 3 finit aussi en 0x465024 : ZONE / CODE / DATA ne bougent pas.
EN = '--en' in sys.argv
WIDE = '--wide' in sys.argv   # vrai 16:9 : VESA 0x222 848x480 de DOSBox, ecran du jeu centre (x2, 6 lignes pour 5)
OW, OH, OX = (848, 480, 104) if WIDE else (640, 400, 0)
# structure de registres de l'appel en mode reel (DPMI 0x300) du pilote video : 0x1093C8 dans l'exe francais
# (A VERIFIER pour l'exe anglais : decalage des donnees -0xB0 suppose)
RMREGS = 0x45e318 if EN else 0x45e3c8
MAP = {}
if EN:
    MAP = {int(k, 16): int(v, 16) for k, v in json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'port_en.json'))).items() if v}
    MAP.update({0x2b274a: 0x2b275a, 0x35cdae: 0x35cd8e, 0x44af40: 0x44ae90, 0x44af41: 0x44ae91, 0x44af42: 0x44ae92,
                0x43e358: 0x43e2a8, 0x2ece04: 0x2ecc24})
# (0x400020 n'est PAS une adresse : taille du curseur 32x64 passee a 0x2E476C, identique dans les deux exe ;
#  la decaler de -0xB0 donnait un sous-curseur de -144 x 63 -> plantage au 2e passage en mission)
M = lambda a: MAP.get(a, a)

WMAX, HMAX, HMARGE = (848 if WIDE else 640), 400, 8
NROWS = HMAX + HMARGE + 8
EXPORT = len(sys.argv) > 1 and sys.argv[1] in ('export', 'asm')
if not EXPORT:
    import tnre
    g = tnre.Guest()
    POOL_BASE, POOL_SIZE = g.dword(M(0x43e360)), g.dword(M(0x43e358))
# Emplacement du mod : 16 Ko ajoutes a la fin de l'objet 3 de __FF.EXE (_MODS/hd/exe_place.py : taille virtuelle
# 0x10F9B0 -> 0x1139B0). Le chargeur les remplit de zeros au-dessus de la pile (qui part de l'ancienne fin
# 0x465024 et descend) et le tas commence 16 Ko plus haut : rien dans le jeu n'y ecrit jamais.
# (Essais abandonnes : haut de la reserve temporaire = rempli par le menu des options ; plage nulle 0x114000 =
#  utilisee au menu principal.)
ZONE, PLACE = 0x465024, 0x6000   # objet 3 de __FF.EXE agrandi de 24 Ko (16 avant le vrai 16:9)
CODE = (ZONE + 0xff) & ~0xff                      # 0x465100 .. 0x4660ff
DATA = CODE + 0x1000                              # variables + tables de la vue HD (0x1800)
NEW_A, NEW_B = DATA + 0x1800, DATA + 0x1800 + WMAX * 4   # tableaux par colonne agrandis (640 col., 848 en 16:9)
FIN = NEW_B + WMAX * 4 + 0x10
assert FIN <= ZONE + PLACE - 0x200          # les derniers octets portent l'en-tete du 1er bloc du tas
V = {k: DATA + 4 * i for i, k in enumerate(
    'HBUF ACTIVE S_BITS S_RATIO S_SUM S_V1_48 S_V1_4C S_V2_48 S_V2_4C FRAMES ESSAIS '
    'SBUF LFB VALID RATIO_OK COPIES S_ROW S_CLIP CAM_R RHALF BTAB LISSE PALSUM PALVU PALSTAB TR TG TB CALCULS '
    'S_CANVAS S_W0 S_H0 S_CX W_CUR ROWK CXK CXSUM DLT S_V1_8 S_V2_8 S_V1_10 S_V2_10 '
    'FX0 FY0 FW FH FWW HUDF HUDON LSON ROWN HX0 HX1 HROW '
    'WM1 PREVB SHADB FORCE SIG SEGEND LROW SROW TRIED VUP R5 SIDE FSIDE S_V1_18 SIDEOK HDR HSTR CHG'.split())}
assert max(V.values()) < DATA + 0x500
CXTAB, ROWTAB = DATA + 0x500, DATA + 0x900
DIRTYP, OUTROW = DATA + 0x1000, DATA + 0x1200     # lignes modifiees (2 o de marge de chaque cote), ligne composee
MASK = DATA + 0x1480                              # vrai 16:9 : 1 la ou la ligne composee montre la 3D (320 o)
assert ROWTAB + 4 * NROWS <= DATA + 0x1800
VIEW1, VIEW2 = M(0x38fbd4), M(0x41ec74)
P = lambda v: struct.pack('<I', v & 0xffffffff)
v = lambda k: '%#x' % V[k]

ASM = f"""
check_mode:
    cmp dword ptr [0x35f978], 19
    je cm_ecran
    ret
cm_ecran:
    mov eax, dword ptr [{v('SBUF')}]
    test eax, eax
    jnz cm_have
    mov eax, {640 * 400:#x}
    call dword ptr [0x35f54c]
    test eax, eax
    jz cm_ret
    mov dword ptr [{v('SBUF')}], eax
cm_have:
    mov esi, dword ptr [0x44b258]
    cmp dword ptr [esi], eax
    je cm_sub
    mov ecx, dword ptr [esi]
    mov dword ptr [{v('LFB')}], ecx
    mov dword ptr [{v('FORCE')}], 1
    mov dword ptr [esi], eax
WIDE_RM
    mov edi, eax
    mov esi, dword ptr [0x447028]
    movzx ebx, word ptr [0x447034]
    mov edx, 400
cm_init:
    push esi
    push edi
    mov ecx, 80
    rep movsd dword ptr es:[edi], dword ptr [esi]
    pop edi
    pop esi
    add esi, ebx
    add edi, 640
    dec edx
    jnz cm_init
    cmp word ptr [0x44706c], 320
    je cm_sub
    mov word ptr [0x44706c], 320
    mov word ptr [0x44706e], 400
    mov eax, 0x1900140
    mov edx, 4194336
    call 0x2e476c
cm_sub:
    mov ecx, dword ptr [0x4470c0]
    sub ecx, dword ptr [{v('LFB')}]
    cmp ecx, 0x40000
    jae cm_ret
    add ecx, dword ptr [{v('SBUF')}]
    mov dword ptr [0x4470c0], ecx
cm_ret:
    ret

camratio:
    mov eax, dword ptr [0x35f974]
    mov eax, dword ptr [eax]
    cmp dword ptr [0x35f978], 19
    jne cr_1
    sar eax, 1
cr_1:
    mov dword ptr [{v('CAM_R')}], eax
    mov eax, {V['CAM_R']:#x}
    ret

blend_calc:
    push eax
    push ebx
    push ecx
    push esi
    push edi
    inc dword ptr [{v('CALCULS')}]
    movzx ecx, al
    movzx ebx, ah
    lea ecx, [ecx+ecx*2]
    lea ebx, [ebx+ebx*2]
    movzx esi, byte ptr [ecx+0x44af40]
    movzx edi, byte ptr [ebx+0x44af40]
    mov eax, esi
    sub eax, edi
    imul eax, eax
    mov edx, eax
    add esi, edi
    shr esi, 1
    mov dword ptr [{v('TR')}], esi
    movzx esi, byte ptr [ecx+0x44af41]
    movzx edi, byte ptr [ebx+0x44af41]
    mov eax, esi
    sub eax, edi
    imul eax, eax
    add edx, eax
    add esi, edi
    shr esi, 1
    mov dword ptr [{v('TG')}], esi
    movzx esi, byte ptr [ecx+0x44af42]
    movzx edi, byte ptr [ebx+0x44af42]
    mov eax, esi
    sub eax, edi
    imul eax, eax
    add edx, eax
    add esi, edi
    shr esi, 1
    mov dword ptr [{v('TB')}], esi
    cmp edx, 5000
    ja bc_bord
    mov eax, ecx
    xor edx, edx
    mov ecx, 3
    div ecx
    mov edx, eax
    jmp bc_fin
bc_bord:
    mov edi, 0x7fffffff
    xor edx, edx
    xor ecx, ecx
    mov ebx, 0x44af40
bc_loop:
    movzx eax, byte ptr [ebx]
    sub eax, dword ptr [{v('TR')}]
    imul eax, eax
    mov esi, eax
    movzx eax, byte ptr [ebx+1]
    sub eax, dword ptr [{v('TG')}]
    imul eax, eax
    add esi, eax
    movzx eax, byte ptr [ebx+2]
    sub eax, dword ptr [{v('TB')}]
    imul eax, eax
    add esi, eax
    cmp esi, edi
    jae bc_next
    mov edi, esi
    mov edx, ecx
bc_next:
    add ebx, 3
    inc ecx
    cmp ecx, 256
    jb bc_loop
bc_fin:
    or edx, 0x8000
    pop edi
    pop esi
    pop ecx
    pop ebx
    pop eax
    mov word ptr [ebp+eax*2], dx
    ret

WIDE_SROW
pre:
    cmp byte ptr [ebp+0x2c], 0
    je pre_go
    call 0x2ad18c
    ret
pre_go:
    pushad
    call check_mode
    inc dword ptr [{v('ESSAIS')}]
    cmp ebp, {VIEW1:#x}
    jne pre_lo
    mov esi, dword ptr [{VIEW1 + 0x1c:#x}]
    cmp dword ptr [0x44b250], esi
    jne pre_lo
    mov dword ptr [{v('VALID')}], 0
    test byte ptr [0x35a560], 2
    jz pre_lo
    movzx eax, word ptr [esi+8]
    cmp eax, 16
    jb pre_lo
    cmp eax, 320
    ja pre_lo
    movzx ebx, word ptr [esi+0xa]
    cmp ebx, {HMAX}
    ja pre_lo
    mov ecx, dword ptr [{VIEW1 + 0x10:#x}]
    lea edx, [ecx+ecx+1]
    sub edx, eax
    cmp edx, 2
    ja pre_lo
    cmp dword ptr [{VIEW1 + 0x4c:#x}], {CXTAB:#x}
    je pre_lo
    mov dword ptr [{v('S_CANVAS')}], esi
    mov dword ptr [{v('S_W0')}], eax
    mov dword ptr [{v('S_H0')}], ebx
    mov dword ptr [{v('S_CX')}], ecx
    mov eax, dword ptr [{v('HBUF')}]
    test eax, eax
    jnz pre_have
    mov eax, {WMAX * (HMAX + HMARGE):#x}
    call dword ptr [0x35f54c]
    test eax, eax
    jz pre_lo
    mov dword ptr [{v('HBUF')}], eax
pre_have:
    mov eax, dword ptr [{v('S_W0')}]
    add eax, eax
WIDE_W
    mov dword ptr [{v('W_CUR')}], eax
    cmp eax, dword ptr [{v('ROWK')}]
    je pre_rt_ok
    mov dword ptr [{v('ROWK')}], eax
    mov edi, {ROWTAB:#x}
    xor edx, edx
    mov ecx, {NROWS}
pre_rt:
    mov dword ptr [edi], edx
    add edx, eax
    add edi, 4
    dec ecx
    jnz pre_rt
pre_rt_ok:
    mov ebx, dword ptr [{v('S_CX')}]
    add ebx, ebx
WIDE_ADDSIDE_EBX
    cmp ebx, dword ptr [{v('CXK')}]
    je pre_cx_ok
    mov dword ptr [{v('CXK')}], ebx
    mov edi, {CXTAB:#x}
    mov ecx, 0x10000
    xor esi, esi
pre_cx:
    mov edx, ebx
    xor eax, eax
    div ecx
    mov dword ptr [edi], eax
    add esi, eax
    add edi, 4
    add ecx, 0x100
    cmp ecx, 0x20000
    jb pre_cx
    mov dword ptr [{v('CXSUM')}], esi
pre_cx_ok:
    mov esi, dword ptr [{VIEW1 + 0x4c:#x}]
    xor eax, eax
    mov ecx, 256
pre_os:
    add eax, dword ptr [esi]
    add esi, 4
    dec ecx
    jnz pre_os
    mov edx, dword ptr [{v('CXSUM')}]
    sub edx, eax
    mov dword ptr [{v('DLT')}], edx
    mov esi, dword ptr [{v('S_CANVAS')}]
    mov eax, dword ptr [{v('HBUF')}]
    mov ebx, dword ptr [esi]
    mov dword ptr [{v('S_BITS')}], ebx
    movzx ebx, word ptr [esi+0xc]
    mov dword ptr [{v('S_ROW')}], ebx
    mov ebx, dword ptr [esi+0x34]
    mov dword ptr [{v('S_CLIP')}], ebx
    mov dword ptr [esi], eax
    mov eax, dword ptr [{v('W_CUR')}]
    mov word ptr [esi+8], ax
    mov word ptr [esi+0xc], ax
    shl eax, 16
    mov dword ptr [esi+0x34], eax
    mov dword ptr [{v('ACTIVE')}], 1
    inc dword ptr [{v('FRAMES')}]
    mov ebx, dword ptr [0x35f974]
    mov eax, dword ptr [ebx]
    mov dword ptr [{v('S_RATIO')}], eax
    cmp dword ptr [0x35f978], 19
    je pre_r19
    add eax, eax
    mov dword ptr [ebx], eax
pre_r19:
    mov eax, dword ptr [{VIEW1 + 8:#x}]
    mov dword ptr [{v('S_V1_8')}], eax
    add eax, eax
    mov dword ptr [{VIEW1 + 8:#x}], eax
    mov eax, dword ptr [{VIEW2 + 8:#x}]
    mov dword ptr [{v('S_V2_8')}], eax
    add eax, eax
    mov dword ptr [{VIEW2 + 8:#x}], eax
    mov eax, dword ptr [{VIEW1 + 0x10:#x}]
    mov dword ptr [{v('S_V1_10')}], eax
    add eax, eax
WIDE_ADDSIDE_EAX
    mov dword ptr [{VIEW1 + 0x10:#x}], eax
    mov eax, dword ptr [{VIEW2 + 0x10:#x}]
    mov dword ptr [{v('S_V2_10')}], eax
    add eax, eax
WIDE_ADDSIDE_EAX
    mov dword ptr [{VIEW2 + 0x10:#x}], eax
    mov eax, dword ptr [{VIEW1 + 0x48:#x}]
    mov dword ptr [{v('S_V1_48')}], eax
    mov eax, dword ptr [{VIEW1 + 0x4c:#x}]
    mov dword ptr [{v('S_V1_4C')}], eax
    mov eax, dword ptr [{VIEW2 + 0x48:#x}]
    mov dword ptr [{v('S_V2_48')}], eax
    mov eax, dword ptr [{VIEW2 + 0x4c:#x}]
    mov dword ptr [{v('S_V2_4C')}], eax
    mov dword ptr [{VIEW1 + 0x48:#x}], {ROWTAB:#x}
    mov dword ptr [{VIEW1 + 0x4c:#x}], {CXTAB:#x}
    mov dword ptr [{VIEW2 + 0x48:#x}], {ROWTAB:#x}
    mov dword ptr [{VIEW2 + 0x4c:#x}], {CXTAB:#x}
    mov eax, dword ptr [0x427ea8]
    mov dword ptr [{v('S_SUM')}], eax
    add eax, dword ptr [{v('DLT')}]
    mov dword ptr [0x427ea8], eax
    jmp pre_out
pre_lo:
    cmp dword ptr [0x35f978], 19
    jne pre_out
    mov ebx, dword ptr [0x35f974]
    mov eax, dword ptr [ebx]
    mov dword ptr [{v('S_RATIO')}], eax
    sar eax, 1
    mov dword ptr [ebx], eax
    mov dword ptr [{v('RHALF')}], 1
pre_out:
    popad
    ret

post:
    cmp byte ptr [ebp+0x2c], 0
    je post_go
    call 0x2ad1cc
post_go:
    cmp dword ptr [{v('RHALF')}], 0
    je post_hd
    push eax
    push ebx
    mov ebx, dword ptr [0x35f974]
    mov eax, dword ptr [{v('S_RATIO')}]
    mov dword ptr [ebx], eax
    mov dword ptr [{v('RHALF')}], 0
    pop ebx
    pop eax
post_hd:
    cmp dword ptr [{v('ACTIVE')}], 0
    je post_ret
    pushad
    mov dword ptr [{v('ACTIVE')}], 0
    mov esi, dword ptr [{v('S_CANVAS')}]
    mov edi, dword ptr [{v('S_BITS')}]
    mov dword ptr [esi], edi
    mov eax, dword ptr [{v('S_W0')}]
    mov word ptr [esi+8], ax
    mov eax, dword ptr [{v('S_ROW')}]
    mov word ptr [esi+0xc], ax
    mov ebx, dword ptr [{v('S_CLIP')}]
    mov dword ptr [esi+0x34], ebx
    sub eax, dword ptr [{v('S_W0')}]
    mov ebp, eax
    mov esi, dword ptr [{v('HBUF')}]
WIDE_ADDSIDE_ESI
    mov edx, dword ptr [{v('S_H0')}]
    test edx, edx
    jz post_rest
post_row:
    mov ecx, dword ptr [{v('S_W0')}]
post_px:
    mov al, byte ptr [esi]
    mov byte ptr [edi], al
    add esi, 2
    inc edi
    dec ecx
    jnz post_px
    add edi, ebp
WIDE_ADDSIDE_ESI
WIDE_ADDSIDE_ESI
    dec edx
    jnz post_row
post_rest:
    mov ebx, dword ptr [0x35f974]
    mov eax, dword ptr [{v('S_RATIO')}]
    mov dword ptr [ebx], eax
    mov eax, dword ptr [{v('S_V1_8')}]
    mov dword ptr [{VIEW1 + 8:#x}], eax
    mov eax, dword ptr [{v('S_V2_8')}]
    mov dword ptr [{VIEW2 + 8:#x}], eax
    mov eax, dword ptr [{v('S_V1_10')}]
    mov dword ptr [{VIEW1 + 0x10:#x}], eax
    mov eax, dword ptr [{v('S_V2_10')}]
    mov dword ptr [{VIEW2 + 0x10:#x}], eax
    mov eax, dword ptr [{v('S_V1_48')}]
    mov dword ptr [{VIEW1 + 0x48:#x}], eax
    mov eax, dword ptr [{v('S_V1_4C')}]
    mov dword ptr [{VIEW1 + 0x4c:#x}], eax
    mov eax, dword ptr [{v('S_V2_48')}]
    mov dword ptr [{VIEW2 + 0x48:#x}], eax
    mov eax, dword ptr [{v('S_V2_4C')}]
    mov dword ptr [{VIEW2 + 0x4c:#x}], eax
    mov eax, dword ptr [{v('S_SUM')}]
    mov dword ptr [0x427ea8], eax
    mov ecx, dword ptr [0x35bfa0]
    cmp ecx, 0
    jle post_nrect
    cmp ecx, 64
    ja post_nrect
    mov esi, 0x397d50
post_rect:
WIDE_RECT
    add esi, 10
    dec ecx
    jnz post_rect
post_nrect:
    mov eax, dword ptr [{v('S_W0')}]
    mov dword ptr [0x36237a], eax
    shl eax, 15
    mov dword ptr [0x36238a], eax
    mov dword ptr [0x362392], eax
WIDE_HALFW
    sar eax, 16
    mov dword ptr [0x362382], eax
    mov eax, dword ptr [{v('S_BITS')}]
    sub eax, dword ptr [0x447028]
    jb post_nv
    mov ecx, dword ptr [{v('S_ROW')}]
    cmp ecx, 320
    jb post_nv
    xor edx, edx
    div ecx
    mov ebx, eax
    add ebx, dword ptr [{v('S_H0')}]
    cmp ebx, 400
    ja post_nv
    mov ebx, edx
    add ebx, dword ptr [{v('S_W0')}]
    cmp ebx, 320
    ja post_nv
    mov dword ptr [{v('FY0')}], eax
    mov dword ptr [{v('FX0')}], edx
    mov eax, dword ptr [{v('S_W0')}]
    mov dword ptr [{v('FW')}], eax
    mov eax, dword ptr [{v('S_H0')}]
    mov dword ptr [{v('FH')}], eax
    mov eax, dword ptr [{v('W_CUR')}]
    mov dword ptr [{v('FWW')}], eax
WIDE_FSIDE
    mov dword ptr [{v('VALID')}], 1
post_nv:
    popad
post_ret:
    ret

flush:
    call 0x26282c
    pushad
    call check_mode
    cmp dword ptr [0x35f978], 19
    je fl_m19
    mov dword ptr [{v('FORCE')}], 1
    jmp fl_out
fl_m19:
    mov edi, dword ptr [{v('LFB')}]
    test edi, edi
    jz fl_out
    inc dword ptr [{v('COPIES')}]
    cmp dword ptr [{v('LISSE')}], 0
    je fl_prep_fin
    mov eax, dword ptr [{v('BTAB')}]
    test eax, eax
    jnz fl_bt
    mov eax, 0x20000
    call dword ptr [0x35f54c]
    test eax, eax
    jnz fl_bt_ok
    mov dword ptr [{v('LISSE')}], 0
    jmp fl_prep_fin
fl_bt_ok:
    mov dword ptr [{v('BTAB')}], eax
    mov edi, eax
    mov ecx, 0x8000
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
    mov dword ptr [{v('PALSTAB')}], 0
fl_bt:
    xor eax, eax
    mov ecx, 192
    mov esi, 0x44af40
fl_ps:
    rol eax, 3
    add eax, dword ptr [esi]
    add esi, 4
    dec ecx
    jnz fl_ps
    cmp eax, dword ptr [{v('PALSUM')}]
    je fl_prep_fin
    cmp eax, dword ptr [{v('PALVU')}]
    je fl_ps_meme
    mov dword ptr [{v('PALVU')}], eax
    mov dword ptr [{v('PALSTAB')}], 0
    jmp fl_prep_fin
fl_ps_meme:
    inc dword ptr [{v('PALSTAB')}]
    cmp dword ptr [{v('PALSTAB')}], 20
    jb fl_prep_fin
    mov dword ptr [{v('PALSUM')}], eax
    mov edi, dword ptr [{v('BTAB')}]
    mov ecx, 0x8000
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
fl_prep_fin:
    xor eax, eax
    cmp dword ptr [{v('LISSE')}], 0
    je fl_l0
    cmp dword ptr [{v('BTAB')}], 0
    je fl_l0
    inc eax
fl_l0:
    mov dword ptr [{v('LSON')}], eax
    cmp dword ptr [{v('TRIED')}], 0
    jne fl_bufs
    mov dword ptr [{v('TRIED')}], 1
    mov eax, {320 * 400:#x}
    call dword ptr [0x35f54c]
    mov dword ptr [{v('PREVB')}], eax
    mov eax, {640 * 400:#x}
    call dword ptr [0x35f54c]
    mov dword ptr [{v('SHADB')}], eax
    mov dword ptr [{v('FORCE')}], 1
fl_bufs:
    mov eax, dword ptr [{v('VALID')}]
    shl eax, 1
    or eax, dword ptr [{v('HUDF')}]
    imul eax, eax, 1000
    add eax, dword ptr [{v('FX0')}]
    imul eax, eax, 1000
    add eax, dword ptr [{v('FY0')}]
    imul eax, eax, 1000
    add eax, dword ptr [{v('FW')}]
    xor eax, dword ptr [{v('FH')}]
    cmp eax, dword ptr [{v('SIG')}]
    je fl_sig_ok
    mov dword ptr [{v('SIG')}], eax
    mov dword ptr [{v('FORCE')}], 1
fl_sig_ok:
    mov edi, dword ptr [{v('PREVB')}]
    test edi, edi
    jz fl_nodirty
    cmp dword ptr [{v('SHADB')}], 0
    je fl_nodirty
    mov esi, dword ptr [{v('SBUF')}]
    xor edx, edx
fl_p1:
    push esi
    push edi
    mov ecx, 80
    repe cmpsd dword ptr [esi], dword ptr es:[edi]
    pop edi
    pop esi
    je fl_p1_same
    mov byte ptr [edx+{DIRTYP + 2:#x}], 1
    push esi
    push edi
    mov ecx, 80
    rep movsd dword ptr es:[edi], dword ptr [esi]
    pop edi
    pop esi
    jmp fl_p1_next
fl_p1_same:
    mov byte ptr [edx+{DIRTYP + 2:#x}], 0
fl_p1_next:
    add esi, 640
    add edi, 320
    inc edx
    cmp edx, 400
    jb fl_p1
    jmp fl_p2
fl_nodirty:
    mov dword ptr [{v('FORCE')}], 1
fl_p2:
    mov eax, dword ptr [{v('LFB')}]
WIDE_P2
    mov dword ptr [{v('LROW')}], eax
    mov eax, dword ptr [{v('SHADB')}]
    mov dword ptr [{v('SROW')}], eax
    mov esi, dword ptr [{v('SBUF')}]
    mov dword ptr [{v('ROWN')}], 0
fl_row:
    mov eax, dword ptr [{v('ROWN')}]
    mov edx, 320
    mov dword ptr [{v('HX0')}], edx
    mov dword ptr [{v('HX1')}], edx
    xor ebx, ebx
    cmp dword ptr [{v('VALID')}], 0
    je fl_nohd
    mov edx, eax
    sub edx, dword ptr [{v('FY0')}]
    jb fl_nohd
    cmp edx, dword ptr [{v('FH')}]
    jae fl_nohd
    imul edx, dword ptr [{v('FWW')}]
    add edx, dword ptr [{v('HBUF')}]
    mov ecx, dword ptr [{v('FX0')}]
    sub edx, ecx
    sub edx, ecx
WIDE_ADDFSIDE_EDX
    mov dword ptr [{v('HROW')}], edx
    mov edx, dword ptr [{v('ROWN')}]
    sub edx, dword ptr [{v('FY0')}]
    mov dword ptr [{v('VUP')}], edx
    mov dword ptr [{v('HX0')}], ecx
    add ecx, dword ptr [{v('FW')}]
    mov dword ptr [{v('HX1')}], ecx
    inc ebx
fl_nohd:
WIDE_HDR
    test ebx, ebx
    jnz fl_need
    cmp dword ptr [{v('FORCE')}], 0
    jne fl_need
    cmp dword ptr [eax+{DIRTYP:#x}], 0
    jne fl_need
    cmp byte ptr [eax+{DIRTYP + 4:#x}], 0
    jne fl_need
    jmp fl_skip
fl_need:
    xor edx, edx
    cmp dword ptr [{v('HUDF')}], 0
    je fl_h0
    cmp eax, 2
    jb fl_h0
    cmp eax, 398
    jae fl_h0
    inc edx
fl_h0:
    mov dword ptr [{v('HUDON')}], edx
    mov edi, {OUTROW:#x}
    xor ecx, ecx
    mov eax, dword ptr [{v('HX0')}]
    mov dword ptr [{v('SEGEND')}], eax
    call seg_f
    mov eax, dword ptr [{v('HX1')}]
    mov dword ptr [{v('SEGEND')}], eax
    call seg_h
    mov dword ptr [{v('SEGEND')}], 320
    call seg_f
    push esi
    mov edi, dword ptr [{v('SROW')}]
    test edi, edi
    jz fl_write
    cmp dword ptr [{v('FORCE')}], 0
    jne fl_write_sh
    mov esi, {OUTROW:#x}
    mov ecx, 160
    repe cmpsd dword ptr [esi], dword ptr es:[edi]
    je fl_wdone
fl_write_sh:
    mov edi, dword ptr [{v('SROW')}]
    mov esi, {OUTROW:#x}
    mov ecx, 160
    rep movsd dword ptr es:[edi], dword ptr [esi]
fl_write:
WIDE_STR
    mov edi, dword ptr [{v('LROW')}]
    mov esi, {OUTROW:#x}
    mov ecx, 160
    rep movsd dword ptr es:[edi], dword ptr [esi]
WIDE_WR
fl_wdone:
WIDE_SIDES
    pop esi
fl_skip:
    add esi, 640
WIDE_SK
    cmp dword ptr [{v('SROW')}], 0
    je fl_nosrow
    add dword ptr [{v('SROW')}], 640
fl_nosrow:
    inc dword ptr [{v('ROWN')}]
    cmp dword ptr [{v('ROWN')}], 400
    jb fl_row
    mov dword ptr [{v('FORCE')}], 0
fl_out:
    popad
    ret

seg_f:
    cmp ecx, dword ptr [{v('SEGEND')}]
    jae sf_ret
sf_loop:
    mov al, byte ptr [esi+ecx]
    mov ah, al
    mov dl, byte ptr [esi+ecx-1]
    cmp dl, byte ptr [esi+ecx+1]
    je sf_st
    call filt_slow
sf_st:
    mov word ptr [edi+ecx*2], ax
    inc ecx
    cmp ecx, dword ptr [{v('SEGEND')}]
    jb sf_loop
sf_ret:
    ret

seg_h:
    cmp ecx, dword ptr [{v('SEGEND')}]
    jae sh_ret
sh_loop:
    mov al, byte ptr [esi+ecx]
    mov ebx, dword ptr [{v('HROW')}]
    cmp al, byte ptr [ebx+ecx*2]
    jne sh_filter
WIDE_M1
    cmp dword ptr [{v('LSON')}], 0
    je sh_plain
    lea ebx, [ebx+ecx*2]
    mov ebp, dword ptr [{v('BTAB')}]
    movzx eax, word ptr [ebx]
    mov dx, word ptr [ebp+eax*2]
    test dh, 0x80
    jnz sh_b1
    call blend_calc
sh_b1:
    cmp dword ptr [{v('VUP')}], 0
    je sh_s1
    push ebx
    sub ebx, dword ptr [{v('FWW')}]
    movzx eax, dl
    mov ah, byte ptr [ebx]
    pop ebx
    mov dx, word ptr [ebp+eax*2]
    test dh, 0x80
    jnz sh_s1
    call blend_calc
sh_s1:
    mov byte ptr [edi+ecx*2], dl
    movzx eax, word ptr [ebx+1]
    mov dx, word ptr [ebp+eax*2]
    test dh, 0x80
    jnz sh_b2
    call blend_calc
sh_b2:
    cmp dword ptr [{v('VUP')}], 0
    je sh_s2
    push ebx
    sub ebx, dword ptr [{v('FWW')}]
    movzx eax, dl
    mov ah, byte ptr [ebx+1]
    pop ebx
    mov dx, word ptr [ebp+eax*2]
    test dh, 0x80
    jnz sh_s2
    call blend_calc
sh_s2:
    mov byte ptr [edi+ecx*2+1], dl
    jmp sh_next
sh_plain:
    mov ax, word ptr [ebx+ecx*2]
    mov word ptr [edi+ecx*2], ax
    jmp sh_next
sh_filter:
WIDE_M0
    mov ah, al
    mov dl, byte ptr [esi+ecx-1]
    cmp dl, byte ptr [esi+ecx+1]
    je sh_fst
    call filt_slow
sh_fst:
    mov word ptr [edi+ecx*2], ax
sh_next:
    inc ecx
    cmp ecx, dword ptr [{v('SEGEND')}]
    jb sh_loop
sh_ret:
    ret

filt_slow:
    cmp dword ptr [{v('HUDON')}], 0
    je fs_ret
    test ecx, ecx
    jz fs_ret
    cmp ecx, 319
    jae fs_ret
    mov dl, byte ptr [esi+ecx-1]
    mov dh, byte ptr [esi+ecx+1]
    cmp dl, dh
    je fs_ret
    cmp al, dl
    je fs_pok
    cmp al, dh
    jne fs_ret
fs_pok:
    mov ebx, dword ptr [esi+ecx+639]
    xor ebx, dword ptr [esi+ecx-1]
    and ebx, 0xffffff
    jz fs_top
    mov ebx, dword ptr [esi+ecx-641]
    xor ebx, dword ptr [esi+ecx-1]
    and ebx, 0xffffff
    jnz fs_ret
    mov bl, byte ptr [esi+ecx+640]
    mov bh, byte ptr [esi+ecx-1280]
    jmp fs_s2
fs_top:
    mov bl, byte ptr [esi+ecx-640]
    mov bh, byte ptr [esi+ecx+1280]
fs_s2:
    cmp bl, bh
    je fs_ret
    cmp bh, dl
    je fs_ook
    cmp bh, dh
    jne fs_ret
fs_ook:
    cmp bl, dl
    jne fs_nb
    mov al, dl
    ret
fs_nb:
    cmp bl, dh
    jne fs_ret
    mov ah, dh
fs_ret:
    ret

occl:
    push eax
    mov eax, dword ptr [0x44b250]
    movzx eax, word ptr [eax+8]
    dec eax
    mov dword ptr [{v('WM1')}], eax
    cmp dword ptr [esp+0x18], eax
    jle occl_ok
    mov dword ptr [esp+0x18], eax
occl_ok:
    pop eax
    ret

rainx:
    xor eax, eax
    mov al, byte ptr [0x35a560]
    sar ecx, 4
    sar eax, 1
    push edx
    mov edx, dword ptr [0x44b250]
    cmp edx, dword ptr [{v('S_CANVAS')}]
    jne rainx_ret
    movzx edx, word ptr [edx+8]
    cmp edx, dword ptr [{v('W_CUR')}]
    jne rainx_ret
    add ecx, ecx
WIDE_ADDSIDE_ECX
rainx_ret:
    pop edx
    ret
"""
# vrai 16:9 : effacement complet et debut decale de OX quand tout est recompose, 1 ligne sur 5 doublee (400 -> 480),
# longueur de ligne VESA 848 pour le mode 19 ; sinon le texte d'origine
WIDE_TXT = {
    'WIDE_P2': f"""    cmp dword ptr [{v('FORCE')}], 0
    je fl_p2_nc
    mov edi, eax
    mov ecx, {OW * OH // 4}
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
    mov eax, dword ptr [{v('LFB')}]
fl_p2_nc:
    add eax, {OX}
    mov dword ptr [{v('R5')}], 0""",
    'WIDE_WR': f"""    cmp dword ptr [{v('R5')}], 0
    jne fl_wdone
    mov edi, dword ptr [{v('LROW')}]
    add edi, {OW}
    mov esi, {OUTROW:#x}
    mov ecx, 160
    rep movsd dword ptr es:[edi], dword ptr [esi]""",
    'WIDE_SK': f"""    add dword ptr [{v('LROW')}], {OW}
    cmp dword ptr [{v('R5')}], 0
    jne fl_r5
    add dword ptr [{v('LROW')}], {OW}
fl_r5:
    inc dword ptr [{v('R5')}]
    cmp dword ptr [{v('R5')}], 5
    jb fl_r5b
    mov dword ptr [{v('R5')}], 0
fl_r5b:""",
    # le pilote a deja fixe la ligne VESA a la largeur de sa table (640) : 4F06 refait ici a 848 pixels, par la meme
    # structure d'appel en mode reel que lui (son code est deja dans le cache de DOSBox, un patch n'y prendrait pas)
    'WIDE_RM': f"""    push eax
    mov edi, {RMREGS:#x}
    mov ecx, 13
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
    mov dword ptr [{RMREGS + 0x1c:#x}], 0x4f06
    mov dword ptr [{RMREGS + 0x18:#x}], {OW}
    mov edi, {RMREGS:#x}
    mov eax, 0x300
    mov ebx, 0x10
    xor ecx, ecx
    int 0x31
    pop eax""",
    'WIDE_W': f"""    mov eax, {OW}
    sub eax, dword ptr [{v('S_W0')}]
    sub eax, dword ptr [{v('S_W0')}]
    sar eax, 1
    mov dword ptr [{v('SIDE')}], eax
    mov eax, {OW}""",
    'WIDE_ADDSIDE_EBX': f"    add ebx, dword ptr [{v('SIDE')}]",
    'WIDE_ADDSIDE_EAX': f"    add eax, dword ptr [{v('SIDE')}]",
    'WIDE_ADDSIDE_ESI': f"    add esi, dword ptr [{v('SIDE')}]",
    'WIDE_ADDSIDE_ECX': f"    add ecx, dword ptr [{v('SIDE')}]",
    # demi-angle de culling de la camera principale (+0x18) pour la largeur HD : atan((centre HD / 2 CX) / zoom),
    # meme routine d'angle que le reglage de la camera (0x2ECE04, eax = 1.0 x rapport, edx = zoom)
    'WIDE_ANGLE': f"""    movzx eax, word ptr [{VIEW1 + 0x18:#x}]
    mov dword ptr [{v('S_V1_18')}], eax
    mov eax, dword ptr [{v('CXK')}]
    shl eax, 15
    xor edx, edx
    mov ecx, dword ptr [{v('S_CX')}]
    test ecx, ecx
    jz wa_out
    div ecx
    mov edx, dword ptr [{VIEW1:#x}]
    xor ebx, ebx
    call 0x2ece04
    mov word ptr [{VIEW1 + 0x18:#x}], ax
wa_out:""",
    'WIDE_ANGLE_BACK': f"""    mov eax, dword ptr [{v('S_V1_18')}]
    mov word ptr [{VIEW1 + 0x18:#x}], ax""",
    # rectangles ecran des objets (x HD -> x du jeu) : (x - SIDE) / 2, borne a [0, W0 - 1]
    'WIDE_RECT': f"""    cmp dword ptr [{v('HSTR')}], 0
    jne wrc_s
    movsx eax, word ptr [esi]
    sub eax, dword ptr [{v('SIDE')}]
    sar eax, 1
    call rect_clamp
    mov word ptr [esi], ax
    movsx eax, word ptr [esi+4]
    sub eax, dword ptr [{v('SIDE')}]
    sar eax, 1
    call rect_clamp
    mov word ptr [esi+4], ax
    jmp wrc_e
wrc_s:
    movsx eax, word ptr [esi]
    call rect_str
    mov word ptr [esi], ax
    movsx eax, word ptr [esi+4]
    call rect_str
    mov word ptr [esi+4], ax
wrc_e:""",
    'WIDE_FSIDE': f"""    mov eax, dword ptr [{v('SIDE')}]
    mov dword ptr [{v('FSIDE')}], eax
    mov edx, dword ptr [{v('FX0')}]
    add edx, edx
    add edx, {OX}
    xor ecx, ecx
    cmp eax, edx
    jne wf_ok
    inc ecx
wf_ok:
    mov dword ptr [{v('SIDEOK')}], ecx""",
    'WIDE_ADDFSIDE_EDX': f"    add edx, dword ptr [{v('FSIDE')}]",
    # apres le rendu, la bibliotheque repasse en coordonnees du jeu pour la suite (cadre de la cible verrouillee...) :
    # son echelle a ete calculee pour 848 colonnes, la demi-largeur qui va avec est 848/4 (et non W0/2)
    'WIDE_HALFW': f"""    mov ecx, dword ptr [{v('W_CUR')}]
    cmp dword ptr [{v('HSTR')}], 0
    jne hw_s
    shl ecx, 14
    jmp hw_w
hw_s:
    imul ecx, ecx, 12365
hw_w:
    mov dword ptr [0x36238a], ecx""",
    # ligne de 848 colonnes : HUD etire (x 640/848, OUTROW) et, la ou le jeu montre la 3D, pixel HD de la meme colonne
    'WIDE_M1': f"    mov byte ptr [ecx+{MASK:#x}], 1",
    'WIDE_M0': f"    mov byte ptr [ecx+{MASK:#x}], 0",
    # plages de la ligne du jeu (HUD / 3D, d'apres MASK) ; colonne d'ecran de debut du pixel g = (1696 g + 319) / 640 ;
    # 3D : copie de la ligne HD (colonne d'ecran = colonne HD), HUD : OUTROW etire (u = (640 X + 320) / 848)
    'WIDE_SROW': f"""stretch_row:
    push ebx
    push ebp
    mov edi, ebx
    xor ecx, ecx
sr_run:
    cmp ecx, 320
    jae sr_done
    mov edx, 320
    xor ebp, ebp
    cmp dword ptr [{v('SIDEOK')}], 0
    je sr_emit
    mov eax, dword ptr [{v('HX0')}]
    cmp ecx, eax
    jae sr_in
    mov edx, eax
    jmp sr_emit
sr_in:
    mov eax, dword ptr [{v('HX1')}]
    cmp ecx, eax
    jae sr_emit
    push edi
    push ecx
    movzx ebp, byte ptr [ecx+{MASK:#x}]
    lea edi, [ecx+{MASK:#x}]
    mov edx, eax
    sub eax, ecx
    mov ecx, eax
    mov eax, ebp
    repe scasb byte ptr es:[edi]
    je sr_all
    lea edx, [edi-{MASK + 1:#x}]
sr_all:
    pop ecx
    pop edi
sr_emit:
    imul ebx, ecx, 1696
    add ebx, 319
    shr ebx, 7
    imul ebx, ebx, 52429
    shr ebx, 18
    imul eax, edx, 1696
    add eax, 319
    shr eax, 7
    imul eax, eax, 52429
    shr eax, 18
    mov ecx, edx
    push ecx
    push esi
    push edi
    mov ecx, eax
    sub ecx, ebx
    jbe sr_next
    add edi, ebx
    test ebp, ebp
    jz sr_hud
    cmp dword ptr [{v('LSON')}], 0
    jne sr_lisse
    mov esi, dword ptr [{v('HROW')}]
    sub esi, {OX}
    add esi, ebx
    rep movsb byte ptr es:[edi], byte ptr [esi]
    jmp sr_next
sr_hud:
    mov eax, ebx
    imul eax, eax, 640
    add eax, 320
    xor edx, edx
    mov esi, {OW}
    div esi
    lea esi, [eax+{OUTROW:#x}]
sr_h1:
    mov al, byte ptr [esi]
    stosb byte ptr es:[edi], al
    add edx, 640
    cmp edx, {OW}
    jb sr_h2
    sub edx, {OW}
    inc esi
sr_h2:
    dec ecx
    jnz sr_h1
    jmp sr_next
sr_lisse:
    mov ebp, dword ptr [{v('BTAB')}]
    mov esi, dword ptr [{v('HROW')}]
    sub esi, {OX}
    add esi, ebx
sr_l0:
    movzx eax, word ptr [esi]
    mov dx, word ptr [ebp+eax*2]
    test dh, 0x80
    jnz sr_l1
    call blend_calc
sr_l1:
    cmp dword ptr [{v('VUP')}], 0
    je sr_l2
    mov ebx, esi
    sub ebx, dword ptr [{v('FWW')}]
    movzx eax, dl
    mov ah, byte ptr [ebx]
    mov dx, word ptr [ebp+eax*2]
    test dh, 0x80
    jnz sr_l2
    call blend_calc
sr_l2:
    mov al, dl
    stosb byte ptr es:[edi], al
    inc esi
    dec ecx
    jnz sr_l0
sr_next:
    pop edi
    pop esi
    pop ecx
    jmp sr_run
sr_done:
    pop ebp
    pop ebx
    ret
""",
    'WIDE_HDR': f"    mov dword ptr [{v('HDR')}], ebx",
    'WIDE_STR': f"""    cmp dword ptr [{v('HSTR')}], 0
    je fl_wcen
    mov dword ptr [{v('CHG')}], 1
    jmp fl_wdone
fl_wcen:""",
    # cotes de l'ecran (x < OX et x >= OX + 640) : pixels HD de la meme ligne quand la vue est centree
    'WIDE_SIDES': f"""    cmp dword ptr [{v('HSTR')}], 0
    je ws_cen
    cmp dword ptr [{v('CHG')}], 0
    jne ws_str
    cmp dword ptr [{v('HDR')}], 0
    je ws_out
ws_str:
    mov dword ptr [{v('CHG')}], 0
    mov ebx, dword ptr [{v('LROW')}]
    sub ebx, {OX}
    call stretch_row
    cmp dword ptr [{v('R5')}], 0
    jne ws_out
    mov esi, ebx
    lea edi, [ebx+{OW}]
    mov ecx, {OW // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    jmp ws_out
ws_cen:
    cmp dword ptr [{v('HDR')}], 0
    je ws_out
    cmp dword ptr [{v('SIDEOK')}], 0
    je ws_out
    mov ebx, dword ptr [{v('LROW')}]
    call side_row
    cmp dword ptr [{v('R5')}], 0
    jne ws_out
    add ebx, {OW}
    call side_row
ws_out:""",
} if WIDE else {'WIDE_P2': None, 'WIDE_WR': None, 'WIDE_SK': f"    add dword ptr [{v('LROW')}], 640", 'WIDE_RM': None,
                'WIDE_W': None, 'WIDE_ADDSIDE_EBX': None, 'WIDE_ADDSIDE_EAX': None, 'WIDE_ADDSIDE_ESI': None,
                'WIDE_ADDSIDE_ECX': None, 'WIDE_ANGLE': None, 'WIDE_ANGLE_BACK': None, 'WIDE_HALFW': None,
                'WIDE_RECT': """    sar word ptr [esi], 1
    sar word ptr [esi+4], 1""", 'WIDE_FSIDE': None, 'WIDE_ADDFSIDE_EDX': None, 'WIDE_HDR': None, 'WIDE_SIDES': None, 'WIDE_STR': None, 'WIDE_SROW': None, 'WIDE_M1': None, 'WIDE_M0': None}
if WIDE:   # routines ajoutees en tete de code (les etiquettes sont calculees sur le code qui precede)
    ASM = f"""
rect_clamp:
    test eax, eax
    jge rc_pos
    xor eax, eax
rc_pos:
    cmp eax, dword ptr [{v('S_W0')}]
    jl rc_ret
    mov eax, dword ptr [{v('S_W0')}]
    dec eax
rc_ret:
    ret

rect_str:
    sub eax, {OW // 2}
    imul eax, eax, 24731
    sar eax, 16
    push edx
    mov edx, dword ptr [{v('S_W0')}]
    sar edx, 1
    add eax, edx
    pop edx
    jmp rect_clamp

side_row:
    mov esi, dword ptr [{v('HROW')}]
    sub esi, {OX}
    lea edi, [ebx-{OX}]
    mov ecx, {OX // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    mov esi, dword ptr [{v('HROW')}]
    add esi, 640
    lea edi, [ebx+640]
    mov ecx, {OX // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    ret
""" + ASM
for k, t in WIDE_TXT.items():
    ASM = ASM.replace(k + '\n', '' if t is None else t + '\n')
assert 'WIDE_' not in ASM
ASM = re.sub(r'0x[0-9a-fA-F]{5,6}', lambda m: '0x%x' % M(int(m.group(), 16)), ASM)   # adresses du jeu selon la version



def assemble():
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    code = bytes(ks.asm(ASM, CODE)[0])
    labels = {}
    for name in ('camratio', 'pre', 'post', 'flush', 'occl', 'rainx'):
        labels[name] = CODE + len(bytes(ks.asm(ASM.split('\n%s:' % name)[0], CODE)[0]))
    return code, labels


def call_rel(src, dst):
    return b'\xe8' + struct.pack('<i', dst - (src + 5))


code, LBL = assemble()
assert len(code) < 0x1000
DATA_INIT = bytearray(0x1800)
struct.pack_into('<I', DATA_INIT, V['LISSE'] - DATA, 1)
struct.pack_into('<I', DATA_INIT, V['HUDF'] - DATA, 0)   # Scale2x HUD retire du launcher (06/10/2026)
struct.pack_into('<I', DATA_INIT, V['WM1'] - DATA, 319)
if WIDE:
    struct.pack_into('<I', DATA_INIT, V['HSTR'] - DATA, 1)   # vrai 16:9 : HUD etire sur toute la largeur

PATCHES = [
    # tableaux par colonne limites a 320 (hd_fix.py)
    (M(0x2a849d), bytes.fromhex('bf') + P(M(0x4238f0)), bytes.fromhex('bf') + P(NEW_A)),
    (M(0x2a84a2), bytes.fromhex('b9') + P(0x500), bytes.fromhex('b9') + P(WMAX * 4)),
    (M(0x2a869b), bytes.fromhex('8b92') + P(M(0x4238f0)), bytes.fromhex('8b92') + P(NEW_A)),
    (M(0x29e1fe), bytes.fromhex('899a') + P(M(0x41e6ec)), bytes.fromhex('899a') + P(NEW_B)),
    (M(0x29e239), bytes.fromhex('891c85') + P(M(0x41e6ec)), bytes.fromhex('891c85') + P(NEW_B)),
    (M(0x29e362), bytes.fromhex('8998') + P(M(0x41e6ec)), bytes.fromhex('8998') + P(NEW_B)),
    (M(0x29e3de), bytes.fromhex('b9') + P(M(0x41e6f0)), bytes.fromhex('b9') + P(NEW_B + 4)),
    # segment de nuages : compteur 16 bits
    (M(0x2b274a), bytes.fromhex('8ac8c1e51081e60000ffff33c090'), bytes.fromhex('668bc8c1e51081e60000ffff33c0')),
    (M(0x2b276f), bytes.fromhex('fec9'), bytes.fromhex('6649')),
    # tampon 0x41ED30 (copie largeur x hauteur du canvas 3D, refaite a chaque image par 0x2A1CB8) : 320x400 -> 640x400
    (M(0x2614c3), bytes.fromhex('b8') + P(0x1f400), bytes.fromhex('b8') + P(WMAX * 400)),
    (M(0x26152d), bytes.fromhex('b8') + P(0x1f400), bytes.fromhex('b8') + P(WMAX * 400)),
    (M(0x262f80), bytes.fromhex('b8') + P(0x1f400), bytes.fromhex('b8') + P(WMAX * 400)),
    # test objet / relief (0x2A0F58) : colonnes bornees a 319 -> largeur reelle de la vue - 1
    (M(0x2a1011), bytes.fromhex('817c24103f0100007e08c74424103f010000'), call_rel(M(0x2a1011), LBL['occl']) + b'\x90' * 13),
    (M(0x2a1032), bytes.fromhex('81fb3f010000'), bytes.fromhex('3b1d') + P(V['WM1'])),
    # pluie (0x2AF9A8) : colonne d'une goutte = x >> 4 -> doublee pendant une image HD (zone d'apparition inchangee)
    (M(0x2afa24), bytes.fromhex('31c0a0') + P(M(0x35a560)) + bytes.fromhex('c1f904d1f8'), call_rel(M(0x2afa24), LBL['rainx']) + b'\x90' * 7),
    # crochets avant / apres le rendu 3D
    (M(0x29e746), bytes.fromhex('807d2c007405e83bea0000'), call_rel(M(0x29e746), LBL['pre']) + b'\x90' * 6),
    (M(0x29e80c), bytes.fromhex('807d2c007405e8b5e90000'), call_rel(M(0x29e80c), LBL['post']) + b'\x90' * 6),
]
ECRAN = [
    # mission en 400 lignes : mode 19 (VESA 640x400) au lieu de 2 (ModeX 320x400)
    (M(0x262f64), bytes.fromhex('b802000000'), bytes.fromhex('b813000000')),
    # meme chose pour l'autre chemin (table des modes d'ecran 0x2B6704 : relancer une mission...)
    (M(0x35cdae), struct.pack('<h', 2), struct.pack('<h', 19)),
    # echelles de la camera (0x29E820) : proportion a l'echelle 320 colonnes quel que soit le chemin du changement de mode
    (M(0x29ea41), bytes.fromhex('a1') + P(M(0x35f974)), call_rel(M(0x29ea41), LBL['camratio'])),
    # apres chaque copie du jeu vers l'ecran : notre copie doublee vers la memoire video
    (M(0x261fe4), call_rel(M(0x261fe4), M(0x26282c)), call_rel(M(0x261fe4), LBL['flush'])),
]
if WIDE:
    ECRAN += [
        # mode 19 du pilote : numero VBE 0x100 (640x400) -> 0x222 (848x480 de DOSBox, vesa_modes = all)
        # (entree complete : numero 19, VBE, largeur 640, hauteur 400 ; table 0x364E98 FR / 0x364DF0 EN, +0xE4)
        (0x364ed4 if EN else 0x364f7c, bytes.fromhex('1300000180029001'), bytes.fromhex('1300220280029001')),
    ]


def etat():
    print('code %#x (%d o)  donnees %#x  %s' % (CODE, len(code), DATA, {k: hex(a) for k, a in LBL.items()}))
    st = []
    for a, old, new in PATCHES + ECRAN:
        cur = g.read(a, len(old))
        st.append('o' if cur == old else 'P' if cur == new else '?')
    print('patchs', ''.join(st), '| code en place:', g.read(CODE, len(code)) == code)
    print({k: hex(g.dword(a) & 0xffffffff) for k, a in V.items()})
    print('mode pilote', g.dword(M(0x35f978)), '| rapport', hex(g.dword(g.dword(M(0x35f974))) & 0xffffffff),
          '| reserve max utilisee', hex(g.dword(M(0x35d03c))), '| reserve en', hex(POOL_BASE))


def export(path, cls):
    def arr(b):
        toks = ['0x%02x' % x for x in b]
        lines = [', '.join(toks[i:i + 16]) for i in range(0, len(toks), 16)]
        return 'new byte[] {\n            ' + ',\n            '.join(lines) + '\n        }'
    out = []
    out.append('// GENERE par _MODS/re/hd_mod.py export - ne pas modifier a la main.')
    out.append('// Terra Nova HD (3D en double largeur + ecran VESA 640x400 + HUD Scale2x) : code injecte, donnees '
               'initiales et patchs.')
    out.append('// Adresses de la version %s (__FF.EXE avec objet 3 agrandi de 24 Ko)%s.' % ('anglaise GOG / Steam' if EN else 'francaise GOG',
               ', vrai 16:9 (VESA 848x480 de DOSBox, ecran du jeu centre, 3D sur toute la largeur)' if WIDE else ''))
    out.append('static class ' + cls)
    out.append('{')
    out.append('    public const uint Zone = 0x%x, End = 0x%x, PoolBase = 0x%x;' % (ZONE, FIN, ZONE + PLACE))
    out.append('    public const uint Code = 0x%x, Data = 0x%x;' % (CODE, DATA))
    out.append('    public const uint Smoothing = 0x%x, HudFilter = 0x%x, Frames = 0x%x, PoolMax = 0x%x, '
               'DriverMode = 0x%x;' % (V['LISSE'], V['HUDF'], V['FRAMES'], M(0x35d03c), M(0x35f978)))
    out.append('    public const uint Valid = 0x%x, HdWidth = 0x%x, HdHeight = 0x%x;   // last 3D frame: drawn in HD, its width (2x) and height'
               % (V['VALID'], V['FWW'], V['FH']))
    out.append('    public const uint HudStretch = 0x%x;               // true 16:9: 1 = HUD stretched to the full width, 0 = centred' % V.get('HSTR', 0))
    out.append('    public const uint PoolVar = 0x%x;                  // the game\'s pool base pointer' % M(0x43e360))
    out.append('    public static readonly byte[] CodeBytes = %s;' % arr(code))
    out.append('    public static readonly byte[] DataInit = %s;' % arr(bytes(DATA_INIT)))
    out.append('    public static readonly uint[] PatchAt = { %s };' % ', '.join('0x%x' % a for a, o, n in PATCHES + ECRAN))
    out.append('    public static readonly byte[][] PatchOld = {')
    for a, o, n in PATCHES + ECRAN:
        out.append('        new byte[] { %s },' % ', '.join('0x%02x' % x for x in o))
    out.append('    };')
    out.append('    public static readonly byte[][] PatchNew = {')
    for a, o, n in PATCHES + ECRAN:
        out.append('        new byte[] { %s },' % ', '.join('0x%02x' % x for x in n))
    out.append('    };')
    out.append('}')
    with open(path, 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('\n'.join(out) + '\n')
    print('exporte', path, '(%d o de code, %d patchs)' % (len(code), len(PATCHES + ECRAN)))


def bascule(nom, var):
    val = {'on': 1, 'off': 0}.get(sys.argv[2] if len(sys.argv) > 2 else '', None)
    if val is None:
        val = 0 if g.dword(V[var]) else 1
    g.write(V[var], struct.pack('<I', val))
    print(nom, 'actif' if val else 'coupe')


cmd = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith('--') else 'inject'
if cmd == 'export':
    args = [x for x in sys.argv[2:] if not x.startswith('--')]
    cls = 'HdPayload' + ('En' if EN else 'Fr') + ('Wide' if WIDE else '')
    export(args[0] if args else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', '%s.cs' % cls), cls)
elif cmd == 'asm':
    print('code %d o, etiquettes %s' % (len(code), {k: hex(a) for k, a in LBL.items()}))
elif cmd == 'lissage':
    bascule('lissage', 'LISSE')
elif cmd == 'hud':
    bascule('Scale2x HUD', 'HUDF')
elif cmd == 'etat':
    etat()
elif cmd == 'shot':
    hb = g.dword(V['HBUF']) & 0xffffffff
    w, h = g.dword(V['FWW']), g.dword(V['FH'])
    from PIL import Image
    pal = np.frombuffer(g.read(M(0x44af40), 768), np.uint8).reshape(256, 3)
    # image complete : lue entre deux rendus (ACTIVE = 0) et sans nouveau rendu pendant la lecture
    import time as _t
    for _ in range(400):
        if g.dword(V['ACTIVE']) == 0:
            f1 = g.dword(V['FRAMES']); raw = g.read(hb, w * h)
            if g.dword(V['ACTIVE']) == 0 and g.dword(V['FRAMES']) == f1: break
        _t.sleep(0.0005)
    else:
        raise SystemExit('pas d image complete')
    a = np.frombuffer(raw, np.uint8).reshape(h, w)
    name = sys.argv[2] if len(sys.argv) > 2 else 'hdmod'
    Image.fromarray(pal[a]).resize((w * 2, h * 2 * 6 // 5), Image.NEAREST).save(r'shots\%s.png' % name)
    print('sauve shots\\%s.png (%dx%d)' % (name, w, h))
else:
    if POOL_BASE != ZONE + PLACE:
        raise SystemExit('__FF.EXE non prepare (lancer _MODS/hd/exe_place.py) : reserve en %#x' % POOL_BASE)
    if any(g.read(ZONE, FIN - ZONE)):
        raise SystemExit('zone du mod non nulle (deja injecte ?)')
    liste = PATCHES + ([] if '--sans-ecran' in sys.argv else ECRAN)
    st = [g.read(a, len(old)) == old for a, old, new in liste]
    if not all(st):
        raise SystemExit('octets inattendus : %s' % st)
    g.write(DATA, bytes(DATA_INIT))
    g.write(CODE, code)
    for a, old, new in liste:
        g.write(a, new)
    etat()
    print('injecte (RAM seulement)')
