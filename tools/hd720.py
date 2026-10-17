# Terra Nova HD 720 : vrai 16:9 en 1280x720, injection en RAM (meme principe que hd_mod.py --wide, en plus grand).
#  ecran : mode VESA 0x17D de DOSBox (1280x960, 8 bits) dont le CRTC est reprogramme a 600 lignes ; DOSBox affiche
#          alors 1280x600 etire en 1280x720 (pixels 1:1,2, comme le 320x400 du jeu en 4:3).
#  3D    : la vue principale est rendue en 3 x les colonnes du jeu (+ les cotes 16:9, 1280 colonnes en tout) et en
#          1,5 x ses lignes (cockpit 199 -> 298/299 lignes) : centre, focale, table 1/x des colonnes ET des lignes
#          (VIEW +0x50, comprise dans la somme de controle 0x427EA8) poses avant le rendu, remis apres.
#  HUD   : l'ecran 320x400 du jeu agrandi x4 en largeur (etire) ou x3 centre, 400 -> 600 lignes (ligne 2k -> 1 ligne,
#          2k+1 -> 2 lignes) ; la 3D HD remplace les pixels ou l'ecran du jeu montre la 3D (comparaison pixel du jeu /
#          pixel HD reduit, comme hd_mod.py).
#  Memoire : les gros tableaux (tampon HD 1280x608, table des lignes, table 1/y, tableaux par colonne du moteur pour
#          1280 colonnes) sont alloues dans le tas du jeu au premier rendu ; les 7 instructions du moteur qui visent
#          les tableaux par colonne sont alors repointees PAR LE JEU (ecritures emulees : DOSBox retraduit).
# A appliquer AU MENU PRINCIPAL. --en : exe anglais (GOG EN = Steam).
#   python hd720.py [--en]              injecte (RAM)
#   python hd720.py asm [--en]          taille et etiquettes
#   python hd720.py export [--en]       ecrit src/HdPayloadEn720.cs / HdPayloadFr720.cs
import json, os, re, struct, sys

from keystone import Ks, KS_ARCH_X86, KS_MODE_32

EN = '--en' in sys.argv
OW, OH, OX, KX = 1280, 600, 160, 3            # ecran, decalage du jeu centre (960 = 3 x 320), colonnes 3D par colonne du jeu
RMREGS = 0x45e318 if EN else 0x45e3c8
MAP = {}
if EN:
    MAP = {int(k, 16): int(v, 16) for k, v in json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'port_en.json'))).items() if v}
    MAP.update({0x2b274a: 0x2b275a, 0x35cdae: 0x35cd8e, 0x44af40: 0x44ae90, 0x44af41: 0x44ae91, 0x44af42: 0x44ae92,
                0x43e358: 0x43e2a8, 0x2ece04: 0x2ecc24,
                0x36237e: 0x3622d6, 0x362386: 0x3622de, 0x36238e: 0x3622e6, 0x362396: 0x3622ee})   # projection : hauteur
M = lambda a: MAP.get(a, a)

WMAX, HMAX, HMARGE = OW, OH, 8
NROWS = HMAX + HMARGE + 8
EXPORT = len(sys.argv) > 1 and sys.argv[1] in ('export', 'asm')
if not EXPORT:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import tnre
    g = tnre.Guest()
    POOL_BASE = g.dword(M(0x43e360))
ZONE, PLACE = 0x465024, 0x6000
CODE = (ZONE + 0xff) & ~0xff                      # 0x465100 : code (6 Ko)
DATA = CODE + 0x1800                              # 0x466900 : variables et petites tables (6 Ko)
GP = DATA + 0x1800                                # 0x468100 : correctifs de vitesse poses par le jeu (speed_fix.py)
FIN = 0x469390                                    # fin de la zone libre du HD dans l'exe prepare (TN+ ensuite)
V = {k: DATA + 4 * i for i, k in enumerate(
    'HBUF ACTIVE S_BITS S_RATIO S_SUM S_V1_48 S_V1_4C S_V2_48 S_V2_4C FRAMES ESSAIS '
    'SBUF LFB VALID COPIES S_ROW S_CLIP RHALF LISSE HUDF CAM_R '
    'S_CANVAS S_W0 S_H0 S_CX W_CUR ROWK CXK CXSUM DLT S_V1_8 S_V2_8 S_V1_10 S_V2_10 '
    'FX0 FY0 FW FH FWW ROWN HX0 HX1 HROW PREVB SHADB FORCE SIG TRIED SIDE FSIDE SIDEOK HSTR HUDW HCROW XCODE '
    'H3 S_CLIPY S_V1_C S_V2_C S_V1_14 S_V2_14 S_V1_50 S_V2_50 CYTB CYK CYSUM VY0 VB0 ROWT NEWA NEWB TMP '
    'RA RB ERS ERO HRW FPSV FPSCOL FD GPDONE GPOK GPBAD ROWB SVPTR SVB0 SVB1 SVCUR SVFAIL'.split())}
ROWTAB = DATA + 0x200                             # tableaux par ligne : sites a repointer, puis copies (ROW_SITES)
assert max(V.values()) < ROWTAB
CXTAB = DATA + 0x500                              # 256 x 4
OUTROW = DATA + 0x900                             # ligne de sortie du HUD (1280 + 4)
DIRTYP = DATA + 0xE10                             # lignes modifiees de l'ecran du jeu (2 o de marge)
MASK = DATA + 0x1000                              # 1 la ou la ligne du jeu montre la 3D (320 o)
FONT = DATA + 0x1200                              # chiffres 3x5 du compteur d'images (10 mots)
NV = DATA + 0x1140                                # variables du chemin rapide (page 0x467000 : pas de code, ecritures directes)
BL = DATA + 0x1240                                # bascules HUD/3D d'une ligne du jeu (x), 322 dwords max
assert BL + 4 * 322 <= DATA + 0x1800
NVN = {k: NV + 4 * i for i, k in enumerate('Y BLP G8 G4 HX1 RA RB HROW HCROW BEND BLAST DST HRW R SP LASTB LAST3D ANY3D S'.split())}
assert NV + 4 * len(NVN) <= FONT and FONT + 20 <= BL and BL % 8 == 0   # em_pend teste le bit 2 de BL
n = lambda k: '%#x' % NVN[k]
assert MASK + 0x140 <= DATA + 0x1800
VIEW1, VIEW2 = M(0x38fbd4), M(0x41ec74)
# correctifs de vitesse (instructions que DOSBox interprete, boucles a plusieurs blocs par pixel) : ecrits par le jeu
# lui-meme a la 1re image 3D (pre_go), groupe par groupe, apres verification des octets d'origine
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import speed_fix
GP_BLOB, GPAT, GP_GROUPS = speed_fix.build(not EN, GP, V['GPDONE'], V['GPOK'], V['GPBAD'])
assert GP + len(GP_BLOB) <= FIN
P = lambda v: struct.pack('<I', v & 0xffffffff)
v = lambda k: '%#x' % V[k]
# instructions du moteur qui visent ses tableaux par colonne (320 colonnes ; hd_fix.py) : repointees au 1er rendu
SITES_A = [(0x2a849d, 1, 'bf'), (0x2a869b, 2, '8b92')]                      # mov edi,A / mov edx,[edx+A]
SITES_N = [(0x2a84a2, 1, 'b9')]                                               # mov ecx, octets a copier
SITES_B = [(0x29e1fe, 2, '899a'), (0x29e239, 3, '891c85'), (0x29e362, 2, '8998')]
SITES_B4 = [(0x29e3de, 1, 'b9')]                                              # mov ecx, B + 4
# tableaux par LIGNE du moteur (adresses anglaises ; francais : code -0x10, donnees +0xB0) : 512 lignes pour le voile
# (pointeur de table de brume par ligne), 480 pour le sol infrarouge (6 tableaux) ; la vue complete fait 585 lignes HD
# (390 x 1,5) : le tableau du voile debordait sur la table des couleurs a voiler (0x424980, faite une fois par mission :
# infrarouge casse au loin jusqu'a la fin de la mission), ceux du sol IR sur leurs voisins. Repointes au 1er rendu vers
# NROWS lignes chacun (alloc_all), contenu d'origine recopie d'abord.
ROW_ARRAYS = [(0x424140, 512), (0x424a80, 480), (0x425400, 480), (0x425f80, 480), (0x426700, 480), (0x426e80, 480),
              (0x427600, 480)]
ROW_SITES = [  # (instruction, decalage du champ, opcode verifie, tableau, cible d'origine)
    (0x2a85c6, 2, '8982', 0, 0x42413c), (0x2a860a, 2, '8981', 0, 0x42413c), (0x2a8628, 2, '8981', 0, 0x42413c),
    (0x2a87b6, 1, 'ba', 0, 0x424140), (0x2b22f5, 1, 'bb', 0, 0x424140),
    (0x2a79f7, 2, '8b96', 1, 0x424a80), (0x2a7a4a, 2, '8b96', 1, 0x424a80), (0x2a7a1c, 2, '89b9', 1, 0x424a7c),
    (0x2a7622, 2, '8987', 2, 0x425400), (0x2a76d1, 2, '8b90', 2, 0x425400), (0x2a775f, 3, '8b0c8d', 2, 0x425400),
    (0x2a73ad, 2, '8987', 3, 0x425f80), (0x2a757d, 2, '8987', 3, 0x425f80), (0x2a7616, 2, '898f', 3, 0x425f80),
    (0x2a76b3, 2, '8bb8', 3, 0x425f80),
    (0x2a75c5, 2, '8997', 4, 0x426700), (0x2a76c0, 2, '8ba8', 4, 0x426700),
    (0x2a75bd, 2, '8987', 5, 0x426e80), (0x2a76a0, 2, '8bb8', 5, 0x426e80),
    (0x2a73b3, 2, '8987', 6, 0x427600), (0x2a7530, 2, '8987', 6, 0x427600), (0x2a761c, 2, '898f', 6, 0x427600),
    (0x2a76aa, 2, '8bb8', 6, 0x427600)]
RC = (lambda a: a) if EN else (lambda a: a - 0x10)            # code
RD = (lambda a: a) if EN else (lambda a: a + 0xB0)            # donnees
ROWSZ = NROWS * 4
ROWCPY = ROWTAB + 8 * len(ROW_SITES) + 4
assert ROWCPY + 12 * len(ROW_ARRAYS) + 4 <= CXTAB

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
    push eax
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
    call crtc600
    pop eax
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

crtc600:
    mov dx, 0x3d4
    mov al, 0x11
    out dx, al
    inc dx
    in al, dx
    and al, 0x7f
    out dx, al
    dec dx
    mov al, 0x12
    out dx, al
    inc dx
    mov al, {(OH - 1) & 0xff:#x}
    out dx, al
    dec dx
    mov al, 0x07
    out dx, al
    inc dx
    in al, dx
    and al, 0xbd
    or al, {(((OH - 1) >> 8) & 1) << 1 | (((OH - 1) >> 9) & 1) << 6:#x}
    out dx, al
    dec dx
    mov al, 0x38
    out dx, al
    inc dx
    mov al, 0x48
    out dx, al
    dec dx
    mov al, 0x39
    out dx, al
    inc dx
    mov al, 0xa5
    out dx, al
    dec dx
    mov al, 0x5e
    out dx, al
    inc dx
    in al, dx
    and al, 0xfd
    or al, {((OH - 1) >> 10) & 1 and 2:#x}
    out dx, al
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

rect_clamp:
    test eax, eax
    jge rc_pos
    xor eax, eax
rc_pos:
    cmp eax, edx
    jl rc_ret
    lea eax, [edx-1]
rc_ret:
    ret

alloc_all:
    mov eax, {WMAX * (HMAX + HMARGE):#x}
    call dword ptr [0x35f54c]
    test eax, eax
    jz aa_ret
    mov dword ptr [{v('TMP')}], eax
    mov eax, {NROWS * 4:#x}
    call dword ptr [0x35f54c]
    test eax, eax
    jz aa_ret
    mov dword ptr [{v('ROWT')}], eax
    mov eax, 1024
    call dword ptr [0x35f54c]
    test eax, eax
    jz aa_ret
    mov dword ptr [{v('CYTB')}], eax
    mov eax, {WMAX * 8 + 64:#x}
    call dword ptr [0x35f54c]
    test eax, eax
    jz aa_ret
    mov dword ptr [{v('NEWA')}], eax
    add eax, {WMAX * 4 + 32:#x}
    mov dword ptr [{v('NEWB')}], eax
    mov eax, {len(ROW_ARRAYS) * ROWSZ:#x}
    call dword ptr [0x35f54c]
    test eax, eax
    jz aa_ret
    mov dword ptr [{v('ROWB')}], eax
    mov ebx, {ROWCPY}
aa_rc:
    mov esi, dword ptr [ebx]
    test esi, esi
    jz aa_rp
    mov edi, dword ptr [{v('ROWB')}]
    add edi, dword ptr [ebx+4]
    mov ecx, dword ptr [ebx+8]
    rep movsd dword ptr es:[edi], dword ptr [esi]
    add ebx, 12
    jmp aa_rc
aa_rp:
    mov ebx, {ROWTAB}
aa_rs:
    mov edi, dword ptr [ebx]
    test edi, edi
    jz aa_rd
    mov eax, dword ptr [{v('ROWB')}]
    add eax, dword ptr [ebx+4]
    mov dword ptr [edi], eax
    add ebx, 8
    jmp aa_rs
aa_rd:
SELFPATCH
    mov eax, dword ptr [{v('TMP')}]
    mov dword ptr [{v('HBUF')}], eax
aa_ret:
    ret

pre:
    cmp byte ptr [ebp+0x2c], 0
    je pre_go
    call 0x2ad18c
    ret
pre_go:
    pushad
    call check_mode
    call {GPAT:#x}
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
    cmp ebx, 400
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
    mov eax, dword ptr [esi]
    sub eax, dword ptr [0x447028]
    jb pre_lo
    movzx ecx, word ptr [esi+0xc]
    test ecx, ecx
    jz pre_lo
    xor edx, edx
    div ecx
    cmp eax, 400
    jae pre_lo
    mov dword ptr [{v('VY0')}], eax
    mov ecx, eax
    add eax, dword ptr [{v('S_H0')}]
    lea eax, [eax+eax*2]
    shr eax, 1
    lea edx, [ecx+ecx*2]
    shr edx, 1
    mov dword ptr [{v('VB0')}], edx
    sub eax, edx
    mov dword ptr [{v('H3')}], eax
    cmp dword ptr [{v('HBUF')}], 0
    jne pre_have
    call alloc_all
    cmp dword ptr [{v('HBUF')}], 0
    je pre_lo
pre_have:
    mov eax, dword ptr [{v('S_W0')}]
    lea eax, [eax+eax*2]
    neg eax
    add eax, {OW}
    sar eax, 1
    mov dword ptr [{v('SIDE')}], eax
    mov eax, {OW}
    mov dword ptr [{v('W_CUR')}], eax
    cmp eax, dword ptr [{v('ROWK')}]
    je pre_rt_ok
    mov dword ptr [{v('ROWK')}], eax
    mov edi, dword ptr [{v('ROWT')}]
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
    lea ebx, [ebx+ebx*2]
    add ebx, dword ptr [{v('SIDE')}]
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
    call sum256
    mov edx, dword ptr [{v('CXSUM')}]
    sub edx, eax
    mov dword ptr [{v('DLT')}], edx
    mov ebx, dword ptr [{v('H3')}]
    sar ebx, 1
    cmp ebx, dword ptr [{v('CYK')}]
    je pre_cy_ok
    mov dword ptr [{v('CYK')}], ebx
    mov edi, dword ptr [{v('CYTB')}]
    mov ecx, 0x10000
    xor esi, esi
pre_cy:
    mov edx, ebx
    xor eax, eax
    div ecx
    mov dword ptr [edi], eax
    add esi, eax
    add edi, 4
    add ecx, 0x100
    cmp ecx, 0x20000
    jb pre_cy
    mov dword ptr [{v('CYSUM')}], esi
pre_cy_ok:
    mov esi, dword ptr [{VIEW1 + 0x50:#x}]
    call sum256
    mov edx, dword ptr [{v('CYSUM')}]
    sub edx, eax
    add dword ptr [{v('DLT')}], edx
    mov esi, dword ptr [{v('S_CANVAS')}]
    mov eax, dword ptr [{v('HBUF')}]
    mov ebx, dword ptr [esi]
    mov dword ptr [{v('S_BITS')}], ebx
    movzx ebx, word ptr [esi+0xc]
    mov dword ptr [{v('S_ROW')}], ebx
    mov ebx, dword ptr [esi+0x34]
    mov dword ptr [{v('S_CLIP')}], ebx
    mov ebx, dword ptr [esi+0x38]
    mov dword ptr [{v('S_CLIPY')}], ebx
    mov dword ptr [esi], eax
    mov eax, dword ptr [{v('W_CUR')}]
    mov word ptr [esi+8], ax
    mov word ptr [esi+0xc], ax
    shl eax, 16
    mov dword ptr [esi+0x34], eax
    mov eax, dword ptr [{v('H3')}]
    mov word ptr [esi+0xa], ax
    shl eax, 16
    mov dword ptr [esi+0x38], eax
    mov dword ptr [{v('ACTIVE')}], 1
    inc dword ptr [{v('FRAMES')}]
    mov ebx, dword ptr [0x35f974]
    mov eax, dword ptr [ebx]
    mov dword ptr [{v('S_RATIO')}], eax
    mov eax, dword ptr [{VIEW1 + 8:#x}]
    mov dword ptr [{v('S_V1_8')}], eax
    lea eax, [eax+eax*2]
    mov dword ptr [{VIEW1 + 8:#x}], eax
    mov eax, dword ptr [{VIEW2 + 8:#x}]
    mov dword ptr [{v('S_V2_8')}], eax
    lea eax, [eax+eax*2]
    mov dword ptr [{VIEW2 + 8:#x}], eax
    mov eax, dword ptr [{VIEW1 + 0x10:#x}]
    mov dword ptr [{v('S_V1_10')}], eax
    lea eax, [eax+eax*2]
    add eax, dword ptr [{v('SIDE')}]
    mov dword ptr [{VIEW1 + 0x10:#x}], eax
    mov eax, dword ptr [{VIEW2 + 0x10:#x}]
    mov dword ptr [{v('S_V2_10')}], eax
    lea eax, [eax+eax*2]
    add eax, dword ptr [{v('SIDE')}]
    mov dword ptr [{VIEW2 + 0x10:#x}], eax
    mov ebx, dword ptr [{v('H3')}]
    sar ebx, 1
    mov eax, dword ptr [{VIEW1 + 0x14:#x}]
    mov dword ptr [{v('S_V1_14')}], eax
    mov dword ptr [{VIEW1 + 0x14:#x}], ebx
    mov eax, dword ptr [{VIEW2 + 0x14:#x}]
    mov dword ptr [{v('S_V2_14')}], eax
    mov dword ptr [{VIEW2 + 0x14:#x}], ebx
    mov eax, dword ptr [{VIEW1 + 0xc:#x}]
    mov dword ptr [{v('S_V1_C')}], eax
    imul dword ptr [{v('H3')}]
    idiv dword ptr [{v('S_H0')}]
    mov dword ptr [{VIEW1 + 0xc:#x}], eax
    mov eax, dword ptr [{VIEW2 + 0xc:#x}]
    mov dword ptr [{v('S_V2_C')}], eax
    imul dword ptr [{v('H3')}]
    idiv dword ptr [{v('S_H0')}]
    mov dword ptr [{VIEW2 + 0xc:#x}], eax
    mov eax, dword ptr [{VIEW1 + 0x48:#x}]
    mov dword ptr [{v('S_V1_48')}], eax
    mov eax, dword ptr [{VIEW1 + 0x4c:#x}]
    mov dword ptr [{v('S_V1_4C')}], eax
    mov eax, dword ptr [{VIEW1 + 0x50:#x}]
    mov dword ptr [{v('S_V1_50')}], eax
    mov eax, dword ptr [{VIEW2 + 0x48:#x}]
    mov dword ptr [{v('S_V2_48')}], eax
    mov eax, dword ptr [{VIEW2 + 0x4c:#x}]
    mov dword ptr [{v('S_V2_4C')}], eax
    mov eax, dword ptr [{VIEW2 + 0x50:#x}]
    mov dword ptr [{v('S_V2_50')}], eax
    mov eax, dword ptr [{v('ROWT')}]
    mov dword ptr [{VIEW1 + 0x48:#x}], eax
    mov dword ptr [{VIEW2 + 0x48:#x}], eax
    mov dword ptr [{VIEW1 + 0x4c:#x}], {CXTAB:#x}
    mov dword ptr [{VIEW2 + 0x4c:#x}], {CXTAB:#x}
    mov eax, dword ptr [{v('CYTB')}]
    mov dword ptr [{VIEW1 + 0x50:#x}], eax
    mov dword ptr [{VIEW2 + 0x50:#x}], eax
    mov eax, dword ptr [0x427ea8]
    mov dword ptr [{v('S_SUM')}], eax
    add eax, dword ptr [{v('DLT')}]
    mov dword ptr [0x427ea8], eax
    jmp pre_out
pre_lo:
    mov eax, dword ptr [{v('SVPTR')}]
    test eax, eax
    jz pl_old
    call eax
    test eax, eax
    jnz pre_out
pl_old:
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

sum256:
    xor eax, eax
    mov ecx, 256
s256:
    add eax, dword ptr [esi]
    add esi, 4
    dec ecx
    jnz s256
    ret

post:
    cmp byte ptr [ebp+0x2c], 0
    je post_go
    call 0x2ad1cc
post_go:
    cmp dword ptr [{v('SVCUR')}], 0
    je pg_nosv
    pushad
    mov eax, dword ptr [{v('SVPTR')}]
    add eax, SVOFF_POST
    call eax
    popad
pg_nosv:
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
    mov eax, dword ptr [{v('S_H0')}]
    mov word ptr [esi+0xa], ax
    mov eax, dword ptr [{v('S_ROW')}]
    mov word ptr [esi+0xc], ax
    mov ebx, dword ptr [{v('S_CLIP')}]
    mov dword ptr [esi+0x34], ebx
    mov ebx, dword ptr [{v('S_CLIPY')}]
    mov dword ptr [esi+0x38], ebx
    sub eax, dword ptr [{v('S_W0')}]
    mov ebp, eax
    xor edx, edx
post_row:
    cmp edx, dword ptr [{v('S_H0')}]
    jae post_rest
    mov ecx, dword ptr [{v('W_CUR')}]
    mov eax, edx
    add eax, dword ptr [{v('VY0')}]
    lea eax, [eax+eax*2]
    shr eax, 1
    sub eax, dword ptr [{v('VB0')}]
    imul eax, ecx
    add eax, dword ptr [{v('HBUF')}]
    add eax, dword ptr [{v('SIDE')}]
    mov esi, eax
    mov ecx, dword ptr [{v('S_W0')}]
    cmp ecx, 8
    jb post_px8t
post_px8:
    mov al, byte ptr [esi+6]
    mov ah, byte ptr [esi+9]
    shl eax, 16
    mov al, byte ptr [esi]
    mov ah, byte ptr [esi+3]
    mov bl, byte ptr [esi+18]
    mov bh, byte ptr [esi+21]
    shl ebx, 16
    mov bl, byte ptr [esi+12]
    mov bh, byte ptr [esi+15]
    mov dword ptr [edi], eax
    mov dword ptr [edi+4], ebx
    add esi, 24
    add edi, 8
    sub ecx, 8
    cmp ecx, 8
    jae post_px8
    cmp ecx, 4
    jb post_px1
post_px4:
    mov al, byte ptr [esi+6]
    mov ah, byte ptr [esi+9]
    shl eax, 16
    mov al, byte ptr [esi]
    mov ah, byte ptr [esi+3]
    mov dword ptr [edi], eax
    add esi, 12
    add edi, 4
    sub ecx, 4
    cmp ecx, 4
    jae post_px4
post_px1:
    test ecx, ecx
    jz post_eol
post_px:
    mov al, byte ptr [esi]
    mov byte ptr [edi], al
    add esi, 3
    inc edi
    dec ecx
    jnz post_px
post_eol:
    add edi, ebp
    inc edx
    jmp post_row
post_px8t:
    cmp ecx, 4
    jb post_px1
    jmp post_px4
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
    mov eax, dword ptr [{v('S_V1_14')}]
    mov dword ptr [{VIEW1 + 0x14:#x}], eax
    mov eax, dword ptr [{v('S_V2_14')}]
    mov dword ptr [{VIEW2 + 0x14:#x}], eax
    mov eax, dword ptr [{v('S_V1_C')}]
    mov dword ptr [{VIEW1 + 0xc:#x}], eax
    mov eax, dword ptr [{v('S_V2_C')}]
    mov dword ptr [{VIEW2 + 0xc:#x}], eax
    mov eax, dword ptr [{v('S_V1_48')}]
    mov dword ptr [{VIEW1 + 0x48:#x}], eax
    mov eax, dword ptr [{v('S_V1_4C')}]
    mov dword ptr [{VIEW1 + 0x4c:#x}], eax
    mov eax, dword ptr [{v('S_V1_50')}]
    mov dword ptr [{VIEW1 + 0x50:#x}], eax
    mov eax, dword ptr [{v('S_V2_48')}]
    mov dword ptr [{VIEW2 + 0x48:#x}], eax
    mov eax, dword ptr [{v('S_V2_4C')}]
    mov dword ptr [{VIEW2 + 0x4c:#x}], eax
    mov eax, dword ptr [{v('S_V2_50')}]
    mov dword ptr [{VIEW2 + 0x50:#x}], eax
    mov eax, dword ptr [{v('S_SUM')}]
    mov dword ptr [0x427ea8], eax
    mov ecx, dword ptr [0x35bfa0]
    cmp ecx, 0
    jle post_nrect
    cmp ecx, 64
    ja post_nrect
    mov esi, 0x397d50
post_rect:
    mov edx, dword ptr [{v('S_W0')}]
    movsx eax, word ptr [esi]
    call rect_x
    mov word ptr [esi], ax
    movsx eax, word ptr [esi+4]
    call rect_x
    mov word ptr [esi+4], ax
    mov edx, dword ptr [{v('S_H0')}]
    movsx eax, word ptr [esi+2]
    call rect_y
    mov word ptr [esi+2], ax
    movsx eax, word ptr [esi+6]
    call rect_y
    mov word ptr [esi+6], ax
    add esi, 10
    dec ecx
    jnz post_rect
post_nrect:
    mov eax, dword ptr [{v('S_W0')}]
    mov dword ptr [0x36237a], eax
    shl eax, 15
    mov dword ptr [0x362392], eax
    sar eax, 16
    mov dword ptr [0x362382], eax
    mov eax, dword ptr [{v('W_CUR')}]
    cmp dword ptr [{v('HSTR')}], 0
    jne pn_s
    imul eax, eax, 10923
    jmp pn_w
pn_s:
    shl eax, 13
pn_w:
    mov dword ptr [0x36238a], eax
    mov eax, dword ptr [{v('S_H0')}]
    mov dword ptr [0x36237e], eax
    shl eax, 15
    mov dword ptr [0x36238e], eax
    mov dword ptr [0x362396], eax
    sar eax, 16
    mov dword ptr [0x362386], eax
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
    mov eax, dword ptr [{v('SIDE')}]
    mov dword ptr [{v('FSIDE')}], eax
    lea edx, [edx+edx*2]
    add edx, {OX}
    xor ecx, ecx
    cmp eax, edx
    jne pn_sk
    inc ecx
pn_sk:
    mov dword ptr [{v('SIDEOK')}], ecx
    mov dword ptr [{v('VALID')}], 1
post_nv:
    popad
post_ret:
    ret

rect_x:
    cmp dword ptr [{v('HSTR')}], 0
    jne rx_s
    sub eax, dword ptr [{v('SIDE')}]
    push edx
    cdq
    push ecx
    mov ecx, 3
    idiv ecx
    pop ecx
    pop edx
    jmp rect_clamp
rx_s:
    sub eax, {OW // 2}
    sar eax, 2
    push edx
    sar edx, 1
    add eax, edx
    pop edx
    jmp rect_clamp

rect_y:
    add eax, dword ptr [{v('VB0')}]
    add eax, eax
    push edx
    cdq
    push ecx
    mov ecx, 3
    idiv ecx
    pop ecx
    pop edx
    sub eax, dword ptr [{v('VY0')}]
    jmp rect_clamp

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
    cmp dword ptr [{v('TRIED')}], 0
    jne fl_bufs
    mov dword ptr [{v('TRIED')}], 1
    mov eax, {320 * 400:#x}
    call dword ptr [0x35f54c]
    mov dword ptr [{v('PREVB')}], eax
    mov eax, {OW * 400:#x}
    call dword ptr [0x35f54c]
    mov dword ptr [{v('SHADB')}], eax
    mov eax, 12288
    call dword ptr [0x35f54c]
    mov dword ptr [{v('XCODE')}], eax
    mov eax, {320 * 400 * 2:#x}
    call dword ptr [0x35f54c]
    mov dword ptr [{v('HUDW')}], eax
    call clr_hudw
    mov dword ptr [{v('FORCE')}], 1
fl_bufs:
    mov eax, dword ptr [{v('VALID')}]
    imul eax, eax, 1000
    add eax, dword ptr [{v('FX0')}]
    imul eax, eax, 1000
    add eax, dword ptr [{v('FY0')}]
    imul eax, eax, 1000
    add eax, dword ptr [{v('FW')}]
    xor eax, dword ptr [{v('FH')}]
    xor eax, dword ptr [{v('HSTR')}]
    cmp eax, dword ptr [{v('SIG')}]
    je fl_sig_ok
    mov dword ptr [{v('SIG')}], eax
    mov dword ptr [{v('FORCE')}], 1
    call clr_hudw
fl_sig_ok:
    mov edi, dword ptr [{v('PREVB')}]
    test edi, edi
    jz fl_nodirty
    cmp dword ptr [{v('SHADB')}], 0
    je fl_nodirty
    mov esi, dword ptr [{v('SBUF')}]
    xor edx, edx
fl_p1:
    xor ecx, ecx
fp_c:
    mov eax, dword ptr [esi+ecx]
    xor eax, dword ptr [edi+ecx]
    mov ebx, dword ptr [esi+ecx+4]
    xor ebx, dword ptr [edi+ecx+4]
    or eax, ebx
    mov ebx, dword ptr [esi+ecx+8]
    xor ebx, dword ptr [edi+ecx+8]
    or eax, ebx
    mov ebx, dword ptr [esi+ecx+12]
    xor ebx, dword ptr [edi+ecx+12]
    or eax, ebx
    mov ebx, dword ptr [esi+ecx+16]
    xor ebx, dword ptr [edi+ecx+16]
    or eax, ebx
    mov ebx, dword ptr [esi+ecx+20]
    xor ebx, dword ptr [edi+ecx+20]
    or eax, ebx
    mov ebx, dword ptr [esi+ecx+24]
    xor ebx, dword ptr [edi+ecx+24]
    or eax, ebx
    mov ebx, dword ptr [esi+ecx+28]
    xor ebx, dword ptr [edi+ecx+28]
    or eax, ebx
    jnz fp_dirty
    add ecx, 32
    cmp ecx, 320
    jb fp_c
    jmp fl_p1_same
fp_dirty:
    mov byte ptr [edx+{DIRTYP:#x}], 1
    push esi
    push edi
    mov ecx, 80
    rep movsd dword ptr es:[edi], dword ptr [esi]
    pop edi
    pop esi
    jmp fl_p1_next
fl_p1_same:
    mov byte ptr [edx+{DIRTYP:#x}], 0
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
    cmp dword ptr [{v('FORCE')}], 0
    je fl_p2_nc
    mov edi, dword ptr [{v('LFB')}]
    mov ecx, {OW * OH // 4}
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
fl_p2_nc:
    mov dword ptr [{v('ROWN')}], 0
    cmp dword ptr [{v('HUDW')}], 0
    jne nr_start
fl_row:
    mov eax, dword ptr [{v('ROWN')}]
    lea ecx, [eax+eax*2]
    mov edx, ecx
    shr ecx, 1
    mov dword ptr [{v('RA')}], ecx
    add edx, 3
    shr edx, 1
    mov dword ptr [{v('RB')}], edx
    xor ebx, ebx
    cmp dword ptr [{v('VALID')}], 0
    je fl_nohd
    mov edx, eax
    sub edx, dword ptr [{v('FY0')}]
    jb fl_nohd
    cmp edx, dword ptr [{v('FH')}]
    jae fl_nohd
    inc ebx
fl_nohd:
    test ebx, ebx
    jnz fl_need
    cmp dword ptr [{v('FORCE')}], 0
    jne fl_need
    cmp byte ptr [eax+{DIRTYP:#x}], 0
    je fl_skip
fl_need:
    push ebx
    imul esi, eax, 640
    add esi, dword ptr [{v('SBUF')}]
    call build_outrow
    pop ebx
    test ebx, ebx
    jz fl_hud
    mov eax, dword ptr [{v('RA')}]
    sub eax, dword ptr [{v('VB0')}]
    imul eax, dword ptr [{v('FWW')}]
    add eax, dword ptr [{v('HBUF')}]
    add eax, dword ptr [{v('FSIDE')}]
    mov ecx, dword ptr [{v('FX0')}]
    lea ecx, [ecx+ecx*2]
    sub eax, ecx
    mov dword ptr [{v('HROW')}], eax
    xor edx, edx
    cmp dword ptr [{v('HUDW')}], edx
    je fl_hc
    imul edx, dword ptr [{v('ROWN')}], 640
    add edx, dword ptr [{v('HUDW')}]
fl_hc:
    mov dword ptr [{v('HCROW')}], edx
    mov eax, dword ptr [{v('FX0')}]
    mov dword ptr [{v('HX0')}], eax
    add eax, dword ptr [{v('FW')}]
    mov dword ptr [{v('HX1')}], eax
    call seg_mask
    mov edx, dword ptr [{v('RA')}]
fl_r3:
    cmp edx, dword ptr [{v('RB')}]
    jae fl_skip
    push edx
    mov eax, edx
    sub eax, dword ptr [{v('VB0')}]
    imul eax, dword ptr [{v('FWW')}]
    add eax, dword ptr [{v('HBUF')}]
    mov dword ptr [{v('HRW')}], eax
    imul edi, edx, {OW}
    add edi, dword ptr [{v('LFB')}]
    call emit_row
    pop edx
    inc edx
    jmp fl_r3
fl_hud:
    imul edi, dword ptr [{v('ROWN')}], {OW}
    add edi, dword ptr [{v('SHADB')}]
    cmp dword ptr [{v('FORCE')}], 0
    jne fl_hw
    push edi
    mov esi, {OUTROW:#x}
    mov ecx, {OW // 4}
    repe cmpsd dword ptr [esi], dword ptr es:[edi]
    pop edi
    je fl_skip
fl_hw:
    mov esi, {OUTROW:#x}
    mov ecx, {OW // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    mov edx, dword ptr [{v('RA')}]
fl_hw2:
    cmp edx, dword ptr [{v('RB')}]
    jae fl_skip
    imul edi, edx, {OW}
    add edi, dword ptr [{v('LFB')}]
    mov esi, {OUTROW:#x}
    mov ecx, {OW // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    inc edx
    jmp fl_hw2
fl_skip:
    inc dword ptr [{v('ROWN')}]
    cmp dword ptr [{v('ROWN')}], 400
    jb fl_row
fl_end:
    mov eax, dword ptr [{v('SVPTR')}]
    test eax, eax
    jz fe_nosv
    add eax, SVOFF_FLUSH
    call eax
fe_nosv:
    call fps_draw
    mov dword ptr [{v('FORCE')}], 0
fl_out:
    popad
    ret


fps_draw:
    mov eax, dword ptr [{v('FPSV')}]
    test eax, eax
    jz fd_ret
    cmp eax, 999
    jbe fd_n
    mov eax, 999
fd_n:
    xor ecx, ecx
    mov ebx, 10
fd_div:
    xor edx, edx
    div ebx
    push edx
    inc ecx
    test eax, eax
    jnz fd_div
    mov dword ptr [{v('FD')}], ecx
    mov edx, 2
fd_bg:
    lea eax, [edx+edx]
    push edx
    xor edx, edx
    mov ebx, 3
    div ebx
    pop edx
    cmp dword ptr [{v('VALID')}], 0
    je fd_hud
    mov ebx, eax
    sub ebx, dword ptr [{v('FY0')}]
    jb fd_hud
    cmp ebx, dword ptr [{v('FH')}]
    jb fd_bgn
fd_hud:
    imul esi, eax, {OW}
    add esi, dword ptr [{v('SHADB')}]
    add esi, {OW - 32}
    imul edi, edx, {OW}
    add edi, dword ptr [{v('LFB')}]
    add edi, {OW - 32}
    mov ebx, 7
fd_cp:
    mov eax, dword ptr [esi]
    mov dword ptr [edi], eax
    add esi, 4
    add edi, 4
    dec ebx
    jnz fd_cp
fd_bgn:
    inc edx
    cmp edx, 17
    jb fd_bg
    mov ecx, dword ptr [{v('FD')}]
    mov edi, dword ptr [{v('LFB')}]
    add edi, {4 * OW + OW - 6}
    mov eax, ecx
    shl eax, 3
    sub edi, eax
    mov al, byte ptr [{v('FPSCOL')}]
    mov ah, al
fd_dig:
    pop ebx
    movzx ebx, word ptr [ebx*2+{FONT:#x}]
    push edi
    mov esi, 14
    mov edx, 5
fd_row:
    push edi
    bt ebx, esi
    jnc fd_c1
    mov word ptr [edi], ax
    mov word ptr [edi+{OW}], ax
fd_c1:
    dec esi
    bt ebx, esi
    jnc fd_c2
    mov word ptr [edi+2], ax
    mov word ptr [edi+{OW + 2}], ax
fd_c2:
    dec esi
    bt ebx, esi
    jnc fd_c3
    mov word ptr [edi+4], ax
    mov word ptr [edi+{OW + 4}], ax
fd_c3:
    dec esi
    pop edi
    add edi, {2 * OW}
    dec edx
    jnz fd_row
    pop edi
    add edi, 8
    dec ecx
    jnz fd_dig
fd_ret:
    ret

clr_hudw:
    mov edi, dword ptr [{v('HUDW')}]
    test edi, edi
    jz ch_ret
    mov ecx, {320 * 400 * 2 // 4}
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
ch_ret:
    ret

build_outrow:
    mov edi, {OUTROW:#x}
    xor ecx, ecx
    cmp dword ptr [{v('HSTR')}], 0
    je bo_cen
bo_s:
    movzx eax, byte ptr [esi+ecx]
    imul eax, eax, 0x01010101
    mov dword ptr [edi+ecx*4], eax
    inc ecx
    cmp ecx, 320
    jb bo_s
    ret
bo_cen:
    add edi, {OX}
bo_c:
    movzx eax, byte ptr [esi+ecx]
    imul eax, eax, 0x01010101
    mov dword ptr [edi], eax
    add edi, 3
    inc ecx
    cmp ecx, 320
    jb bo_c
    xor eax, eax
    mov edi, {OUTROW:#x}
    mov ecx, {OX // 4}
    rep stosd dword ptr es:[edi], eax
    mov edi, {OUTROW + OX + 960:#x}
    mov ecx, {(OW - OX - 960) // 4}
    rep stosd dword ptr es:[edi], eax
    ret

seg_mask:
    mov ecx, dword ptr [{v('HX0')}]
    mov ebp, dword ptr [{v('HX1')}]
    cmp ecx, ebp
    jae sm_ret
    mov ebx, dword ptr [{v('HROW')}]
    mov edx, dword ptr [{v('HCROW')}]
    test edx, edx
    jz sm_nohc
sm_loop:
    lea edi, [ecx+ecx*2]
    mov al, byte ptr [esi+ecx]
    cmp al, byte ptr [ebx+edi]
    jne sm_hud
    cmp byte ptr [edx+ecx*2+1], 0
    je sm_3d
    cmp byte ptr [edx+ecx*2], al
    je sm_known
    mov byte ptr [edx+ecx*2+1], 0
sm_3d:
    mov byte ptr [ecx+{MASK:#x}], 1
    inc ecx
    cmp ecx, ebp
    jb sm_loop
    ret
sm_hud:
    mov byte ptr [edx+ecx*2], al
    mov byte ptr [edx+ecx*2+1], 1
sm_known:
    mov byte ptr [ecx+{MASK:#x}], 0
    inc ecx
    cmp ecx, ebp
    jb sm_loop
    ret
sm_nohc:
    lea edi, [ecx+ecx*2]
    mov al, byte ptr [esi+ecx]
    cmp al, byte ptr [ebx+edi]
    sete byte ptr [ecx+{MASK:#x}]
    inc ecx
    cmp ecx, ebp
    jb sm_nohc
sm_ret:
    ret

emit_row:
    mov ebx, dword ptr [{v('HRW')}]
    cmp dword ptr [{v('HSTR')}], 0
    jne er_str
    push edi
    mov esi, {OUTROW:#x}
    cmp dword ptr [{v('SIDEOK')}], 0
    je er_l
    mov esi, ebx
er_l:
    mov ecx, {OX // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    pop edi
    push edi
    add edi, {OX + 960}
    mov esi, {OUTROW + OX + 960:#x}
    cmp dword ptr [{v('SIDEOK')}], 0
    je er_r
    lea esi, [ebx+{OX + 960}]
er_r:
    mov ecx, {(OW - OX - 960) // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    pop edi
    mov dword ptr [{v('ERS')}], 3
    mov dword ptr [{v('ERO')}], {OX}
    jmp er_go
er_str:
    mov dword ptr [{v('ERS')}], 4
    mov dword ptr [{v('ERO')}], 0
er_go:
    xor ecx, ecx
er_run:
    cmp ecx, 320
    jae er_done
    xor ebp, ebp
    mov edx, 320
    mov eax, dword ptr [{v('HX0')}]
    cmp ecx, eax
    jae er_in
    mov edx, eax
    jmp er_emit
er_in:
    mov eax, dword ptr [{v('HX1')}]
    cmp ecx, eax
    jae er_emit
    push edi
    push ecx
    movzx ebp, byte ptr [ecx+{MASK:#x}]
    lea edi, [ecx+{MASK:#x}]
    mov edx, eax
    sub eax, ecx
    mov ecx, eax
    mov eax, ebp
    repe scasb byte ptr es:[edi]
    je er_all
    lea edx, [edi-{MASK + 1:#x}]
er_all:
    pop ecx
    pop edi
er_emit:
    push edx
    push edi
    mov eax, dword ptr [{v('ERS')}]
    imul ecx, eax
    add ecx, dword ptr [{v('ERO')}]
    imul eax, edx
    add eax, dword ptr [{v('ERO')}]
    sub eax, ecx
    jbe er_next
    lea esi, [ecx+{OUTROW:#x}]
    test ebp, ebp
    jz er_cp
    lea esi, [ebx+ecx]
er_cp:
    add edi, ecx
    mov ecx, eax
    shr ecx, 2
    rep movsd dword ptr es:[edi], dword ptr [esi]
    mov ecx, eax
    and ecx, 3
    rep movsb byte ptr es:[edi], byte ptr [esi]
er_next:
    pop edi
    pop ecx
    jmp er_run
er_done:
    ret

nr_start:
    xor eax, eax
    mov dword ptr [{n('Y')}], eax
    mov dword ptr [{n('ANY3D')}], eax
    mov dword ptr [{n('LAST3D')}], eax
    mov dword ptr [{n('LASTB')}], -1
nr_row:
    mov eax, dword ptr [{n('Y')}]
    cmp dword ptr [{v('VALID')}], 0
    je nr_hudrow
    mov edx, eax
    sub edx, dword ptr [{v('FY0')}]
    cmp edx, dword ptr [{v('FH')}]
    jb nr_3drow
nr_hudrow:
    cmp dword ptr [{v('FORCE')}], 0
    jne nr_hneed
    cmp byte ptr [eax+{DIRTYP:#x}], 0
    je nr_next
nr_hneed:
    mov dword ptr [{n('LASTB')}], eax
    mov dword ptr [{n('LAST3D')}], 0
    lea esi, [eax+eax*4]
    shl esi, 7
    add esi, dword ptr [{v('SBUF')}]
    call nb_full
    mov eax, dword ptr [{n('Y')}]
    lea edi, [eax+eax*4]
    shl edi, 8
    add edi, dword ptr [{v('SHADB')}]
    cmp dword ptr [{v('FORCE')}], 0
    jne nr_hw
    push edi
    mov esi, {OUTROW:#x}
    mov ecx, {OW // 4}
    repe cmpsd dword ptr [esi], dword ptr es:[edi]
    pop edi
    je nr_next
nr_hw:
    mov esi, {OUTROW:#x}
    mov ecx, {OW // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    mov eax, dword ptr [{n('Y')}]
    lea ecx, [eax+eax*2]
    lea edx, [ecx+3]
    shr ecx, 1
    shr edx, 1
nr_hw2:
    lea edi, [ecx+ecx*4]
    shl edi, 8
    add edi, dword ptr [{v('LFB')}]
    mov esi, {OUTROW:#x}
    mov ebx, ecx
    mov ecx, {OW // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    lea ecx, [ebx+1]
    cmp ecx, edx
    jb nr_hw2
nr_next:
    mov eax, dword ptr [{n('Y')}]
    inc eax
    mov dword ptr [{n('Y')}], eax
    cmp eax, 400
    jb nr_row
    mov dword ptr [{v('ROWN')}], 400
    mov dword ptr [{v('RA')}], 598
    mov dword ptr [{v('RB')}], 600
    cmp dword ptr [{n('ANY3D')}], 0
    je nr_fx2
    mov eax, dword ptr [{n('HROW')}]
    mov dword ptr [{v('HROW')}], eax
    mov eax, dword ptr [{n('HCROW')}]
    mov dword ptr [{v('HCROW')}], eax
    mov eax, dword ptr [{v('FX0')}]
    mov dword ptr [{v('HX0')}], eax
    add eax, dword ptr [{v('FW')}]
    mov dword ptr [{v('HX1')}], eax
    mov eax, dword ptr [{n('HRW')}]
    mov dword ptr [{v('HRW')}], eax
    mov eax, 4
    xor edx, edx
    cmp dword ptr [{v('HSTR')}], 0
    jne nr_fxe
    mov eax, 3
    mov edx, {OX}
nr_fxe:
    mov dword ptr [{v('ERS')}], eax
    mov dword ptr [{v('ERO')}], edx
    mov edi, dword ptr [{v('FX0')}]
    add edi, {MASK:#x}
    mov ecx, dword ptr [{v('FW')}]
    xor eax, eax
    rep stosb byte ptr es:[edi], al
    mov ebx, {BL + 4:#x}
nr_fxm:
    cmp ebx, dword ptr [{n('BLAST')}]
    jae nr_fx2
    mov eax, dword ptr [ebx+4]
    sub eax, dword ptr [{v('ERO')}]
    xor edx, edx
    div dword ptr [{v('ERS')}]
    mov ecx, eax
    mov eax, dword ptr [ebx]
    sub eax, dword ptr [{v('ERO')}]
    xor edx, edx
    div dword ptr [{v('ERS')}]
    sub ecx, eax
    lea edi, [eax+{MASK:#x}]
    mov al, 1
    rep stosb byte ptr es:[edi], al
    add ebx, 8
    jmp nr_fxm
nr_fx2:
    cmp dword ptr [{n('LAST3D')}], 0
    je fl_end
    mov eax, dword ptr [{n('LASTB')}]
    lea esi, [eax+eax*4]
    shl esi, 7
    add esi, dword ptr [{v('SBUF')}]
    call nb_full
    jmp fl_end

nr_3drow:
    mov dword ptr [{n('LASTB')}], eax
    mov dword ptr [{n('LAST3D')}], 1
    mov dword ptr [{n('ANY3D')}], 1
    lea esi, [eax+eax*4]
    shl esi, 7
    add esi, dword ptr [{v('SBUF')}]
    mov dword ptr [{n('S')}], esi
    lea ecx, [eax+eax*2]
    lea edx, [ecx+3]
    shr ecx, 1
    shr edx, 1
    mov dword ptr [{n('RA')}], ecx
    mov dword ptr [{n('RB')}], edx
    mov edx, dword ptr [{v('FWW')}]
    sub ecx, dword ptr [{v('VB0')}]
    imul ecx, edx
    add ecx, dword ptr [{v('HBUF')}]
    add ecx, dword ptr [{v('FSIDE')}]
    mov edx, dword ptr [{v('FX0')}]
    lea edx, [edx+edx*2]
    sub ecx, edx
    mov dword ptr [{n('HROW')}], ecx
    lea ebp, [eax+eax*4]
    shl ebp, 7
    add ebp, dword ptr [{v('HUDW')}]
    mov dword ptr [{n('HCROW')}], ebp
    mov ecx, dword ptr [{v('FX0')}]
    mov ebx, ecx
    add ebx, dword ptr [{v('FW')}]
    mov dword ptr [{n('HX1')}], ebx
    lea edx, [ebx-8]
    mov dword ptr [{n('G8')}], edx
    lea edx, [ebx-4]
    mov dword ptr [{n('G4')}], edx
    mov dword ptr [{BL:#x}], 0
    mov dword ptr [{n('BLP')}], {BL + 4:#x}
    lea edi, [ecx+ecx*2]
    add edi, dword ptr [{n('HROW')}]
    jmp enterH

enter3:
    cmp ecx, dword ptr [{n('G8')}]
    jg tail3
L3:
    mov eax, dword ptr [esi+ecx]
    mov dl, byte ptr [edi+6]
    mov dh, byte ptr [edi+9]
    shl edx, 16
    mov dl, byte ptr [edi]
    mov dh, byte ptr [edi+3]
    xor eax, edx
    mov edx, dword ptr [esi+ecx+4]
    mov bl, byte ptr [edi+18]
    mov bh, byte ptr [edi+21]
    shl ebx, 16
    mov bl, byte ptr [edi+12]
    mov bh, byte ptr [edi+15]
    xor edx, ebx
    or eax, edx
    mov edx, dword ptr [ebp+ecx*2]
    or edx, dword ptr [ebp+ecx*2+4]
    or edx, dword ptr [ebp+ecx*2+8]
    or edx, dword ptr [ebp+ecx*2+12]
    and edx, 0xff00ff00
    or eax, edx
    jnz mix3
    add ecx, 8
    add edi, 24
    cmp ecx, dword ptr [{n('G8')}]
    jle L3
tail3:
    mov ebx, dword ptr [{n('HX1')}]
    cmp ecx, ebx
    jl M3
    mov edx, dword ptr [{n('BLP')}]
    mov dword ptr [edx], ecx
    add edx, 4
    jmp cls_fin
mix3:
    lea ebx, [ecx+8]
M3:
    mov al, byte ptr [esi+ecx]
    cmp al, byte ptr [edi]
    jne M3_hud
    cmp byte ptr [ebp+ecx*2+1], 0
    je M3_nx
    cmp byte ptr [ebp+ecx*2], al
    je M3_sw
    mov byte ptr [ebp+ecx*2+1], 0
M3_nx:
    inc ecx
    add edi, 3
    cmp ecx, ebx
    jl M3
    jmp enter3
M3_hud:
    mov byte ptr [ebp+ecx*2], al
    mov byte ptr [ebp+ecx*2+1], 1
M3_sw:
    mov edx, dword ptr [{n('BLP')}]
    mov dword ptr [edx], ecx
    add edx, 4
    mov dword ptr [{n('BLP')}], edx
    jmp MH_nx
MH:
    mov al, byte ptr [esi+ecx]
    cmp al, byte ptr [edi]
    jne MH_hud
    cmp byte ptr [ebp+ecx*2+1], 0
    je MH_sw
    cmp byte ptr [ebp+ecx*2], al
    je MH_nx
    mov byte ptr [ebp+ecx*2+1], 0
MH_sw:
    mov edx, dword ptr [{n('BLP')}]
    mov dword ptr [edx], ecx
    add edx, 4
    mov dword ptr [{n('BLP')}], edx
    jmp M3_nx
MH_hud:
    mov byte ptr [ebp+ecx*2], al
    mov byte ptr [ebp+ecx*2+1], 1
MH_nx:
    inc ecx
    add edi, 3
    cmp ecx, ebx
    jl MH
enterH:
    cmp ecx, dword ptr [{n('G4')}]
    jg tailH
LH:
    mov eax, dword ptr [esi+ecx]
    mov dl, byte ptr [edi+6]
    mov dh, byte ptr [edi+9]
    shl edx, 16
    mov dl, byte ptr [edi]
    mov dh, byte ptr [edi+3]
    xor edx, eax
    lea ebx, [edx-0x1010101]
    not edx
    and ebx, edx
    test ebx, 0x80808080
    jnz mixH
    movzx edx, ah
    shl edx, 16
    mov dl, al
    or edx, 0x1000100
    mov dword ptr [ebp+ecx*2], edx
    shr eax, 16
    movzx edx, ah
    shl edx, 16
    mov dl, al
    or edx, 0x1000100
    mov dword ptr [ebp+ecx*2+4], edx
    add ecx, 4
    add edi, 12
    cmp ecx, dword ptr [{n('G4')}]
    jle LH
tailH:
    mov ebx, dword ptr [{n('HX1')}]
    cmp ecx, ebx
    jl MH
    mov edx, dword ptr [{n('BLP')}]
    jmp cls_fin
mixH:
    lea ebx, [ecx+4]
    jmp MH

cls_fin:
    mov dword ptr [edx], 320
    mov dword ptr [{n('BLAST')}], edx
    add edx, 4
    mov dword ptr [{n('BEND')}], edx
    mov esi, dword ptr [{n('S')}]
    mov eax, {BL:#x}
spr_lp:
    cmp eax, dword ptr [{n('BLAST')}]
    jae spr_done
    mov ecx, dword ptr [eax]
    mov ebx, dword ptr [eax+4]
    add eax, 8
    mov dword ptr [{n('SP')}], eax
    cmp dword ptr [{v('HSTR')}], 0
    je spr_c
    lea eax, [ecx+4]
    cmp eax, ebx
    jg sps_t
sps4:
    mov eax, dword ptr [esi+ecx]
    add ecx, 4
    movzx edx, al
    imul edx, edx, 0x1010101
    shr eax, 8
    movzx edi, al
    imul edi, edi, 0x1010101
    shr eax, 8
    movzx ebp, al
    imul ebp, ebp, 0x1010101
    shr eax, 8
    imul eax, eax, 0x1010101
    mov dword ptr [ecx*4+{OUTROW - 16:#x}], edx
    mov dword ptr [ecx*4+{OUTROW - 12:#x}], edi
    mov dword ptr [ecx*4+{OUTROW - 8:#x}], ebp
    mov dword ptr [ecx*4+{OUTROW - 4:#x}], eax
    lea eax, [ecx+4]
    cmp eax, ebx
    jle sps4
sps_t:
    cmp ecx, ebx
    jge spr_nx
sps1:
    movzx eax, byte ptr [esi+ecx]
    inc ecx
    imul eax, eax, 0x1010101
    mov dword ptr [ecx*4+{OUTROW - 4:#x}], eax
    cmp ecx, ebx
    jl sps1
    jmp spr_nx
spr_c:
    lea edi, [ecx+ecx*2+{OUTROW + OX:#x}]
    cmp ecx, ebx
    jge spr_nx
spc1:
    movzx eax, byte ptr [esi+ecx]
    inc ecx
    imul eax, eax, 0x1010101
    mov dword ptr [edi], eax
    add edi, 3
    cmp ecx, ebx
    jl spc1
spr_nx:
    mov eax, dword ptr [{n('SP')}]
    jmp spr_lp
spr_done:
    mov eax, {BL:#x}
    mov edx, dword ptr [{n('BEND')}]
    cmp dword ptr [{v('HSTR')}], 0
    je cv_c
cv_s:
    mov ecx, dword ptr [eax]
    shl ecx, 2
    mov dword ptr [eax], ecx
    add eax, 4
    cmp eax, edx
    jb cv_s
    jmp cv_done
cv_c:
    mov ecx, dword ptr [eax]
    lea ecx, [ecx+ecx*2+{OX}]
    mov dword ptr [eax], ecx
    add eax, 4
    cmp eax, edx
    jb cv_c
cv_done:
    mov eax, dword ptr [{n('RA')}]
em_row:
    mov dword ptr [{n('R')}], eax
    lea edi, [eax+eax*4]
    shl edi, 8
    add edi, dword ptr [{v('LFB')}]
    mov dword ptr [{n('DST')}], edi
    mov edx, dword ptr [{v('FWW')}]
    sub eax, dword ptr [{v('VB0')}]
    imul eax, edx
    add eax, dword ptr [{v('HBUF')}]
    mov dword ptr [{n('HRW')}], eax
    cmp dword ptr [{v('HSTR')}], 0
    jne em_runs
    cmp dword ptr [{v('SIDEOK')}], 0
    je em_sz
    mov esi, eax
    mov ecx, {OX // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    lea esi, [eax+{OX + 960}]
    mov edi, dword ptr [{n('DST')}]
    add edi, {OX + 960}
    mov ecx, {(OW - OX - 960) // 4}
    rep movsd dword ptr es:[edi], dword ptr [esi]
    jmp em_runs
em_sz:
    xor eax, eax
    mov ecx, {OX // 4}
    rep stosd dword ptr es:[edi], eax
    mov edi, dword ptr [{n('DST')}]
    add edi, {OX + 960}
    mov ecx, {(OW - OX - 960) // 4}
    rep stosd dword ptr es:[edi], eax
em_runs:
    mov ebx, {OUTROW:#x}
    mov edx, {BL:#x}
em_pass:
    cmp edx, dword ptr [{n('BLAST')}]
    jae em_pend
em_r:
    mov esi, dword ptr [edx]
    mov ecx, dword ptr [edx+4]
    sub ecx, esi
    mov edi, dword ptr [{n('DST')}]
    add edi, esi
    add esi, ebx
    mov eax, ecx
    shr ecx, 2
    rep movsd dword ptr es:[edi], dword ptr [esi]
    mov ecx, eax
    and ecx, 3
    rep movsb byte ptr es:[edi], byte ptr [esi]
    add edx, 8
    cmp edx, dword ptr [{n('BLAST')}]
    jb em_r
em_pend:
    test dl, 4
    jnz em_rowdone
    mov ebx, dword ptr [{n('HRW')}]
    mov edx, {BL + 4:#x}
    jmp em_pass
em_rowdone:
    mov eax, dword ptr [{n('R')}]
    inc eax
    cmp eax, dword ptr [{n('RB')}]
    jb em_row
    jmp nr_next

nb_full:
    cmp dword ptr [{v('HSTR')}], 0
    je nbf_c
    xor ecx, ecx
nbf_s:
    mov eax, dword ptr [esi+ecx]
    add ecx, 4
    movzx edx, al
    imul edx, edx, 0x1010101
    shr eax, 8
    movzx edi, al
    imul edi, edi, 0x1010101
    shr eax, 8
    movzx ebp, al
    imul ebp, ebp, 0x1010101
    shr eax, 8
    imul eax, eax, 0x1010101
    mov dword ptr [ecx*4+{OUTROW - 16:#x}], edx
    mov dword ptr [ecx*4+{OUTROW - 12:#x}], edi
    mov dword ptr [ecx*4+{OUTROW - 8:#x}], ebp
    mov dword ptr [ecx*4+{OUTROW - 4:#x}], eax
    cmp ecx, 320
    jb nbf_s
    ret
nbf_c:
    xor ecx, ecx
    mov edi, {OUTROW + OX:#x}
nbf_c1:
    movzx eax, byte ptr [esi+ecx]
    inc ecx
    imul eax, eax, 0x1010101
    mov dword ptr [edi], eax
    add edi, 3
    cmp ecx, 320
    jb nbf_c1
    xor eax, eax
    mov edi, {OUTROW:#x}
    mov ecx, {OX // 4}
    rep stosd dword ptr es:[edi], eax
    mov edi, {OUTROW + OX + 960:#x}
    mov ecx, {(OW - OX - 960) // 4}
    rep stosd dword ptr es:[edi], eax
    ret

occl:
    push eax
    mov eax, dword ptr [0x44b250]
    movzx eax, word ptr [eax+8]
    dec eax
    mov dword ptr [{v('TMP')}], eax
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
    jne rainx_sv
    movzx edx, word ptr [edx+8]
    cmp edx, dword ptr [{v('W_CUR')}]
    jne rainx_ret
    lea ecx, [ecx+ecx*2]
    add ecx, dword ptr [{v('SIDE')}]
    jmp rainx_ret
rainx_sv:
    mov edx, dword ptr [{v('SVCUR')}]
    test edx, edx
    jz rainx_ret
    imul ecx, dword ptr [edx+40]
rainx_ret:
    pop edx
    ret
"""
# ---------------------------------------------------------------- small cockpit views in HD (MFD cameras)
# Every other view the render function draws (the two MFD cameras: 82x104 game pixels in the cockpit) gets the main
# view's treatment at a smaller scale: its canvas points to a buffer of its own, KX x wider (4 with the HUD stretched,
# 3 centred) and 1.5 x taller, its view fields and 1/x, 1/y tables follow (the tables' checksum too), the library's
# pixel ratio x KX/3; after the render the canvas gets every KX-th column back (the game composes its screen as
# before) and the flush puts the HD pixels on the screen wherever the game's pixel is still that sample (the HUD
# drawn over the view keeps its own). Position independent: TN+ copies it into the XCODE buffer (page + 0x1800),
# then sets SVPTR. Two slots, allocated on the first small view; per slot a block: variables, row offsets, the two
# tables, the buffer (up to 128 x 132 game pixels).
SV_B = dict(VIEW=0, CANV=4, BITS=8, W=12, H=16, ROW=20, CLIP=24, CLIPY=28, X0=32, Y0=36, KX=40, WH=44, HS=48, R0=52,
            STAMP=56, CXK=60, CYK=64, SV=68, RATIO=124, SUM=128, DLT=132, CXSUM=136, CYSUM=140, VALID=144, RTK=148)
SV_ROWT, SV_CXT, SV_CYT, SV_BUF = 256, 1280, 2304, 3328
SV_WMAX, SV_HMAX = 128, 132                       # game pixels
SV_BLOCK = SV_BUF + 4 * SV_WMAX * 216             # 216 HD rows: 1.5 x 132 + 1 and a margin
SV_XOFF = 0x1800                                  # in the XCODE buffer's first full page + 0x1800 (after affine)
b = lambda k: '%d' % SV_B[k]
SV_ASM = f"""
sv_pre:
    xor eax, eax
    cmp dword ptr [0x35f978], 19
    jne svp_ret
    cmp dword ptr [{v('HBUF')}], 0
    je svp_ret
    cmp dword ptr [{v('SVCUR')}], 0
    jne svp_ret
    cmp dword ptr [{v('SVFAIL')}], 0
    jne svp_ret
    cmp ebp, {VIEW1:#x}
    je svp_ret
    mov esi, dword ptr [ebp+0x1c]
    test esi, esi
    je svp_ret
    cmp dword ptr [0x44b250], esi
    jne svp_ret
    cmp dword ptr [ebp+0x4c], 0
    je svp_ret
    cmp dword ptr [ebp+0x50], 0
    je svp_ret
    movzx ecx, word ptr [esi+8]
    cmp ecx, 16
    jb svp_ret
    cmp ecx, {SV_WMAX}
    ja svp_ret
    movzx ebx, word ptr [esi+0xa]
    cmp ebx, 16
    jb svp_ret
    cmp ebx, {SV_HMAX}
    ja svp_ret
    mov edx, dword ptr [ebp+0x10]
    lea edx, [edx+edx+1]
    sub edx, ecx
    cmp edx, 2
    ja svp_ret
    mov eax, dword ptr [esi]
    sub eax, dword ptr [0x447028]
    jb svp_no
    movzx edi, word ptr [esi+0xc]
    cmp edi, 320
    jb svp_no
    xor edx, edx
    div edi
    lea edi, [edx+ecx]
    cmp edi, 320
    ja svp_no
    lea edi, [eax+ebx]
    cmp edi, 400
    ja svp_no
    push ecx
    push ebx
    push eax
    push edx
    call sv_alloc
    test eax, eax
    jz svp_pop
    mov edi, dword ptr [{v('SVB0')}]
    mov edx, dword ptr [{v('SVB1')}]
    cmp dword ptr [edi+{b('VIEW')}], ebp
    je svp_have
    cmp dword ptr [edx+{b('VIEW')}], ebp
    je svp_have1
    cmp dword ptr [edi+{b('VIEW')}], 0
    je svp_have
    cmp dword ptr [edx+{b('VIEW')}], 0
    je svp_have1
    mov eax, dword ptr [edi+{b('STAMP')}]
    cmp eax, dword ptr [edx+{b('STAMP')}]
    jbe svp_have
svp_have1:
    mov edi, edx
svp_have:
    mov dword ptr [edi+{b('VIEW')}], ebp
    mov dword ptr [edi+{b('CANV')}], esi
    pop edx
    pop eax
    pop ebx
    pop ecx
    mov dword ptr [edi+{b('X0')}], edx
    mov dword ptr [edi+{b('Y0')}], eax
    mov dword ptr [edi+{b('W')}], ecx
    mov dword ptr [edi+{b('H')}], ebx
    mov edx, 3
    cmp dword ptr [{v('HSTR')}], 0
    je svp_k3
    inc edx
svp_k3:
    mov dword ptr [edi+{b('KX')}], edx
    imul ecx, edx
    mov dword ptr [edi+{b('WH')}], ecx
    lea edx, [eax+eax*2]
    shr edx, 1
    mov dword ptr [edi+{b('R0')}], edx
    add eax, ebx
    lea eax, [eax+eax*2]
    shr eax, 1
    sub eax, edx
    mov dword ptr [edi+{b('HS')}], eax
    cmp ecx, dword ptr [edi+{b('RTK')}]
    je svp_rt_ok
    mov dword ptr [edi+{b('RTK')}], ecx
    lea ebx, [edi+{SV_ROWT}]
    xor edx, edx
    mov eax, 256
svp_rt:
    mov dword ptr [ebx], edx
    add edx, ecx
    add ebx, 4
    dec eax
    jnz svp_rt
svp_rt_ok:
    mov ebx, dword ptr [ebp+0x10]
    imul ebx, dword ptr [edi+{b('KX')}]
    cmp ebx, dword ptr [edi+{b('CXK')}]
    je svp_cx_ok
    mov dword ptr [edi+{b('CXK')}], ebx
    lea esi, [edi+{SV_CXT}]
    call sv_tab
    mov dword ptr [edi+{b('CXSUM')}], eax
svp_cx_ok:
    mov ebx, dword ptr [edi+{b('HS')}]
    shr ebx, 1
    cmp ebx, dword ptr [edi+{b('CYK')}]
    je svp_cy_ok
    mov dword ptr [edi+{b('CYK')}], ebx
    lea esi, [edi+{SV_CYT}]
    call sv_tab
    mov dword ptr [edi+{b('CYSUM')}], eax
svp_cy_ok:
    mov esi, dword ptr [ebp+0x4c]
    call sv_sum
    mov edx, dword ptr [edi+{b('CXSUM')}]
    sub edx, eax
    mov esi, dword ptr [ebp+0x50]
    call sv_sum
    add edx, dword ptr [edi+{b('CYSUM')}]
    sub edx, eax
    mov dword ptr [edi+{b('DLT')}], edx
    mov esi, dword ptr [edi+{b('CANV')}]
    mov eax, dword ptr [esi]
    mov dword ptr [edi+{b('BITS')}], eax
    movzx eax, word ptr [esi+0xc]
    mov dword ptr [edi+{b('ROW')}], eax
    mov eax, dword ptr [esi+0x34]
    mov dword ptr [edi+{b('CLIP')}], eax
    mov eax, dword ptr [esi+0x38]
    mov dword ptr [edi+{b('CLIPY')}], eax
    lea eax, [edi+{SV_BUF}]
    mov dword ptr [esi], eax
    mov eax, dword ptr [edi+{b('WH')}]
    mov word ptr [esi+8], ax
    mov word ptr [esi+0xc], ax
    shl eax, 16
    mov dword ptr [esi+0x34], eax
    mov eax, dword ptr [edi+{b('HS')}]
    mov word ptr [esi+0xa], ax
    shl eax, 16
    mov dword ptr [esi+0x38], eax
    mov esi, ebp
    lea ebx, [edi+{b('SV')}]
    call sv_vset
    mov esi, {VIEW2:#x}
    lea ebx, [edi+{SV_B['SV'] + 28}]
    call sv_vset
    mov ebx, dword ptr [0x35f974]
    mov eax, dword ptr [ebx]
    mov dword ptr [edi+{b('RATIO')}], eax
    cmp dword ptr [edi+{b('KX')}], 3
    je svp_r3
    shl eax, 2
    xor edx, edx
    mov ecx, 3
    div ecx
svp_r3:
    mov dword ptr [ebx], eax
    mov eax, dword ptr [0x427ea8]
    mov dword ptr [edi+{b('SUM')}], eax
    add eax, dword ptr [edi+{b('DLT')}]
    mov dword ptr [0x427ea8], eax
    mov dword ptr [{v('SVCUR')}], edi
    mov eax, 1
    ret
svp_pop:
    add esp, 16
svp_no:
    xor eax, eax
svp_ret:
    ret

sv_alloc:
    cmp dword ptr [{v('SVB1')}], 0
    jne sva_ok
    pushad
    mov eax, {SV_BLOCK}
    call dword ptr [0x35f54c]
    test eax, eax
    jz sva_fail
    mov dword ptr [{v('SVB0')}], eax
    mov edi, eax
    mov ecx, 64
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
    mov eax, {SV_BLOCK}
    call dword ptr [0x35f54c]
    test eax, eax
    jz sva_fail
    mov edi, eax
    mov ecx, 64
    xor eax, eax
    rep stosd dword ptr es:[edi], eax
    lea eax, [edi-256]
    mov dword ptr [{v('SVB1')}], eax
    popad
sva_ok:
    mov eax, 1
    ret
sva_fail:
    mov dword ptr [{v('SVFAIL')}], 1
    popad
    xor eax, eax
    ret

sv_tab:
    push ecx
    push edx
    push edi
    mov edi, esi
    mov ecx, 65536
    xor esi, esi
svt_l:
    mov edx, ebx
    xor eax, eax
    div ecx
    mov dword ptr [edi], eax
    add esi, eax
    add edi, 4
    add ecx, 256
    cmp ecx, 131072
    jb svt_l
    mov eax, esi
    pop edi
    pop edx
    pop ecx
    ret

sv_sum:
    push ecx
    xor eax, eax
    mov ecx, 256
svs_l:
    add eax, dword ptr [esi]
    add esi, 4
    dec ecx
    jnz svs_l
    pop ecx
    ret

sv_vset:
    mov eax, dword ptr [esi+8]
    mov dword ptr [ebx], eax
    imul eax, dword ptr [edi+{b('KX')}]
    mov dword ptr [esi+8], eax
    mov eax, dword ptr [esi+0xc]
    mov dword ptr [ebx+4], eax
    imul dword ptr [edi+{b('HS')}]
    idiv dword ptr [edi+{b('H')}]
    mov dword ptr [esi+0xc], eax
    mov eax, dword ptr [esi+0x10]
    mov dword ptr [ebx+8], eax
    imul eax, dword ptr [edi+{b('KX')}]
    mov dword ptr [esi+0x10], eax
    mov eax, dword ptr [esi+0x14]
    mov dword ptr [ebx+12], eax
    mov eax, dword ptr [edi+{b('HS')}]
    shr eax, 1
    mov dword ptr [esi+0x14], eax
    mov eax, dword ptr [esi+0x48]
    mov dword ptr [ebx+16], eax
    lea eax, [edi+{SV_ROWT}]
    mov dword ptr [esi+0x48], eax
    mov eax, dword ptr [esi+0x4c]
    mov dword ptr [ebx+20], eax
    lea eax, [edi+{SV_CXT}]
    mov dword ptr [esi+0x4c], eax
    mov eax, dword ptr [esi+0x50]
    mov dword ptr [ebx+24], eax
    lea eax, [edi+{SV_CYT}]
    mov dword ptr [esi+0x50], eax
    ret

sv_vrest:
    mov eax, dword ptr [ebx]
    mov dword ptr [esi+8], eax
    mov eax, dword ptr [ebx+4]
    mov dword ptr [esi+0xc], eax
    mov eax, dword ptr [ebx+8]
    mov dword ptr [esi+0x10], eax
    mov eax, dword ptr [ebx+12]
    mov dword ptr [esi+0x14], eax
    mov eax, dword ptr [ebx+16]
    mov dword ptr [esi+0x48], eax
    mov eax, dword ptr [ebx+20]
    mov dword ptr [esi+0x4c], eax
    mov eax, dword ptr [ebx+24]
    mov dword ptr [esi+0x50], eax
    ret

sv_post:
    mov edi, dword ptr [{v('SVCUR')}]
    test edi, edi
    jz spo_ret
    mov dword ptr [{v('SVCUR')}], 0
    mov esi, dword ptr [edi+{b('CANV')}]
    mov eax, dword ptr [edi+{b('BITS')}]
    mov dword ptr [esi], eax
    mov eax, dword ptr [edi+{b('W')}]
    mov word ptr [esi+8], ax
    mov eax, dword ptr [edi+{b('H')}]
    mov word ptr [esi+0xa], ax
    mov eax, dword ptr [edi+{b('ROW')}]
    mov word ptr [esi+0xc], ax
    mov eax, dword ptr [edi+{b('CLIP')}]
    mov dword ptr [esi+0x34], eax
    mov eax, dword ptr [edi+{b('CLIPY')}]
    mov dword ptr [esi+0x38], eax
    mov ebp, dword ptr [edi+{b('KX')}]
    xor edx, edx
spo_row:
    cmp edx, dword ptr [edi+{b('H')}]
    jae spo_rest
    mov eax, edx
    add eax, dword ptr [edi+{b('Y0')}]
    lea eax, [eax+eax*2]
    shr eax, 1
    sub eax, dword ptr [edi+{b('R0')}]
    imul eax, dword ptr [edi+{b('WH')}]
    lea esi, [edi+eax+{SV_BUF}]
    mov ebx, edx
    imul ebx, dword ptr [edi+{b('ROW')}]
    add ebx, dword ptr [edi+{b('BITS')}]
    mov ecx, dword ptr [edi+{b('W')}]
spo_px:
    mov al, byte ptr [esi]
    mov byte ptr [ebx], al
    add esi, ebp
    inc ebx
    dec ecx
    jnz spo_px
    inc edx
    jmp spo_row
spo_rest:
    mov esi, dword ptr [edi+{b('VIEW')}]
    lea ebx, [edi+{b('SV')}]
    call sv_vrest
    mov esi, {VIEW2:#x}
    lea ebx, [edi+{SV_B['SV'] + 28}]
    call sv_vrest
    mov ebx, dword ptr [0x35f974]
    mov eax, dword ptr [edi+{b('RATIO')}]
    mov dword ptr [ebx], eax
    mov eax, dword ptr [edi+{b('SUM')}]
    mov dword ptr [0x427ea8], eax
    mov eax, dword ptr [edi+{b('W')}]
    mov dword ptr [0x36237a], eax
    shl eax, 15
    mov dword ptr [0x362392], eax
    mov dword ptr [0x36238a], eax
    sar eax, 16
    mov dword ptr [0x362382], eax
    mov eax, dword ptr [edi+{b('H')}]
    mov dword ptr [0x36237e], eax
    shl eax, 15
    mov dword ptr [0x36238e], eax
    mov dword ptr [0x362396], eax
    sar eax, 16
    mov dword ptr [0x362386], eax
    mov eax, dword ptr [{v('COPIES')}]
    mov dword ptr [edi+{b('STAMP')}], eax
    mov dword ptr [edi+{b('VALID')}], 1
spo_ret:
    ret

sv_flush:
    cmp dword ptr [{v('LFB')}], 0
    je sf_ret
    cmp dword ptr [{v('SBUF')}], 0
    je sf_ret
    mov edi, dword ptr [{v('SVB0')}]
    test edi, edi
    jz sf_ret
    call sv_fl1
    mov edi, dword ptr [{v('SVB1')}]
    test edi, edi
    jz sf_ret
    call sv_fl1
sf_ret:
    ret

sv_fl1:
    cmp dword ptr [edi+{b('VALID')}], 0
    je sf1_ret
    mov eax, dword ptr [{v('COPIES')}]
    sub eax, dword ptr [edi+{b('STAMP')}]
    cmp eax, 2
    jbe sf1_go
    mov dword ptr [edi+{b('VALID')}], 0
sf1_ret:
    ret
sf1_go:
    xor edx, edx
sf1_row:
    cmp edx, dword ptr [edi+{b('H')}]
    jae sf1_ret
    mov eax, edx
    add eax, dword ptr [edi+{b('Y0')}]
    cmp dword ptr [{v('VALID')}], 0
    je sf1_in
    mov ecx, eax
    sub ecx, dword ptr [{v('FY0')}]
    cmp ecx, dword ptr [{v('FH')}]
    jb sf1_next
sf1_in:
    push edx
    imul esi, eax, 640
    add esi, dword ptr [{v('SBUF')}]
    add esi, dword ptr [edi+{b('X0')}]
    lea ecx, [eax+eax*2]
    mov ebx, ecx
    shr ebx, 1
    add ecx, 3
    shr ecx, 1
    push ecx
    push ebx
    mov eax, ebx
    sub eax, dword ptr [edi+{b('R0')}]
    imul eax, dword ptr [edi+{b('WH')}]
    lea eax, [edi+eax+{SV_BUF}]
    push eax
sf1_hd:
    mov eax, dword ptr [esp+4]
    cmp eax, dword ptr [esp+8]
    jae sf1_done
    push esi
    mov ebx, dword ptr [esp+4]
    mov ecx, eax
    sub ecx, dword ptr [edi+{b('R0')}]
    imul ecx, dword ptr [edi+{b('WH')}]
    lea ecx, [edi+ecx+{SV_BUF}]
    imul edx, eax, {OW}
    add edx, dword ptr [{v('LFB')}]
    mov eax, dword ptr [edi+{b('X0')}]
    imul eax, dword ptr [edi+{b('KX')}]
    add edx, eax
    cmp dword ptr [edi+{b('KX')}], 4
    je sf1_x4
    add edx, {OX}
sf1_x4:
    mov ebp, dword ptr [edi+{b('W')}]
sf1_px:
    mov al, byte ptr [esi]
    cmp al, byte ptr [ebx]
    jne sf1_skip
    mov eax, dword ptr [ecx]
    cmp dword ptr [edi+{b('KX')}], 4
    jne sf1_k3
    mov dword ptr [edx], eax
    jmp sf1_skip
sf1_k3:
    mov word ptr [edx], ax
    shr eax, 16
    mov byte ptr [edx+2], al
sf1_skip:
    inc esi
    mov eax, dword ptr [edi+{b('KX')}]
    add ebx, eax
    add ecx, eax
    add edx, eax
    dec ebp
    jnz sf1_px
    pop esi
    inc dword ptr [esp+4]
    jmp sf1_hd
sf1_done:
    add esp, 12
    pop edx
sf1_next:
    inc edx
    jmp sf1_row
"""

# auto-patch des tableaux par colonne (ecritures du jeu, une fois)
sp = []
for a, o, op in SITES_A: sp.append(f"    mov eax, dword ptr [{v('NEWA')}]\n    mov dword ptr [{M(a) + o:#x}], eax")
for a, o, op in SITES_N: sp.append(f"    mov dword ptr [{M(a) + o:#x}], {WMAX * 4:#x}")
for a, o, op in SITES_B: sp.append(f"    mov eax, dword ptr [{v('NEWB')}]\n    mov dword ptr [{M(a) + o:#x}], eax")
for a, o, op in SITES_B4: sp.append(f"    mov eax, dword ptr [{v('NEWB')}]\n    add eax, 4\n    mov dword ptr [{M(a) + o:#x}], eax")
ASM = ASM.replace('SELFPATCH\n', '\n'.join(sp) + '\n')
# adresses du jeu selon la version (pas celles deja anglaises : VIEW1/VIEW2 et les sites ci-dessus le sont)
ASM = re.sub(r'0x[0-9a-fA-F]{5,6}', lambda m: '0x%x' % (M(int(m.group(), 16)) if int(m.group(), 16) in MAP and EN else int(m.group(), 16)), ASM)


SV_ASM = re.sub(r'0x[0-9a-fA-F]{5,6}', lambda m: '0x%x' % (M(int(m.group(), 16)) if int(m.group(), 16) in MAP and EN else int(m.group(), 16)), SV_ASM)


def sv_assemble():
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    a = bytes(ks.asm(SV_ASM, 0x100000)[0])
    assert a == bytes(ks.asm(SV_ASM, 0x200000)[0]), 'SV code not position independent'
    off = {}
    for name in ('sv_post', 'sv_flush'):
        r = bytes(ks.asm(SV_ASM + '\n    mov eax, %s\n' % name, 0)[0])
        off[name] = struct.unpack('<I', r[-4:])[0]
    assert SV_ASM.lstrip().startswith('sv_pre:')
    assert len(a) <= 0x7E0, len(a)
    return a, off


SV_CODE, SV_OFF = sv_assemble()
ASM = ASM.replace('SVOFF_POST', str(SV_OFF['sv_post'])).replace('SVOFF_FLUSH', str(SV_OFF['sv_flush']))


def assemble():
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    code = bytes(ks.asm(ASM, CODE)[0])
    labels = {}
    for name in ('camratio', 'pre', 'post', 'flush', 'occl', 'rainx'):
        r = bytes(ks.asm(ASM + '\n    mov eax, %s\n' % name, CODE)[0])
        labels[name] = struct.unpack('<I', r[-4:])[0]
    return code, labels


def call_rel(src, dst):
    return b'\xe8' + struct.pack('<i', dst - (src + 5))


code, LBL = assemble()
assert len(code) <= DATA - CODE, len(code)
DATA_INIT = bytearray(FIN - DATA)
DATA_INIT[GP - DATA:GP - DATA + len(GP_BLOB)] = GP_BLOB
struct.pack_into('<I', DATA_INIT, V['HSTR'] - DATA, 1)        # HUD etire sur toute la largeur (TN+ : wide_hud)
_F = ['111101101101111', '010110010010111', '111001111100111', '111001111001111', '101101111001001',
      '111100111001111', '111100111101111', '111001010010010', '111101111101111', '111101111001111']
for _i, _b in enumerate(_F): struct.pack_into('<H', DATA_INIT, FONT - DATA + 2 * _i, int(_b, 2))
# tableaux par ligne : (champ, ecart depuis ROWB) par site, puis (origine, ecart, dwords) par tableau ; 0 = fin
for _k, (a, o, op, t, tg) in enumerate(ROW_SITES):
    struct.pack_into('<Ii', DATA_INIT, ROWTAB - DATA + 8 * _k, RC(a) + o, t * ROWSZ + tg - ROW_ARRAYS[t][0])
for _k, (a, n) in enumerate(ROW_ARRAYS):
    struct.pack_into('<III', DATA_INIT, ROWCPY - DATA + 12 * _k, RD(a), _k * ROWSZ, n)

PATCHES = [
    # les tableaux par colonne : verifies ici, repointes par le jeu au 1er rendu (alloc_all)
] + [(M(a), bytes.fromhex(op), bytes.fromhex(op)) for a, o, op in SITES_A + SITES_N + SITES_B + SITES_B4] + [
    (RC(a), bytes.fromhex(op), bytes.fromhex(op)) for a, o, op, t, tg in ROW_SITES] + [
    # segment de nuages : compteur 16 bits
    (M(0x2b274a), bytes.fromhex('8ac8c1e51081e60000ffff33c090'), bytes.fromhex('668bc8c1e51081e60000ffff33c0')),
    (M(0x2b276f), bytes.fromhex('fec9'), bytes.fromhex('6649')),
    # tampon 0x41ED30 (copie largeur x hauteur du canvas 3D a chaque image) : 320x400 -> 1280x600
    (M(0x2614c3), bytes.fromhex('b8') + P(0x1f400), bytes.fromhex('b8') + P(WMAX * HMAX)),
    (M(0x26152d), bytes.fromhex('b8') + P(0x1f400), bytes.fromhex('b8') + P(WMAX * HMAX)),
    (M(0x262f80), bytes.fromhex('b8') + P(0x1f400), bytes.fromhex('b8') + P(WMAX * HMAX)),
    # test objet / relief : colonnes bornees a 319 -> largeur reelle de la vue - 1
    (M(0x2a1011), bytes.fromhex('817c24103f0100007e08c74424103f010000'), call_rel(M(0x2a1011), LBL['occl']) + b'\x90' * 13),
    (M(0x2a1032), bytes.fromhex('81fb3f010000'), bytes.fromhex('3b1d') + P(V['TMP'])),
    # pluie : colonne d'une goutte x3 + cote pendant une image HD
    (M(0x2afa24), bytes.fromhex('31c0a0') + P(M(0x35a560)) + bytes.fromhex('c1f904d1f8'), call_rel(M(0x2afa24), LBL['rainx']) + b'\x90' * 7),
    # crochets avant / apres le rendu 3D
    (M(0x29e746), bytes.fromhex('807d2c007405e83bea0000'), call_rel(M(0x29e746), LBL['pre']) + b'\x90' * 6),
    (M(0x29e80c), bytes.fromhex('807d2c007405e8b5e90000'), call_rel(M(0x29e80c), LBL['post']) + b'\x90' * 6),
]
ECRAN = [
    (M(0x262f64), bytes.fromhex('b802000000'), bytes.fromhex('b813000000')),
    (M(0x35cdae), struct.pack('<h', 2), struct.pack('<h', 19)),
    (M(0x29ea41), bytes.fromhex('a1') + P(M(0x35f974)), call_rel(M(0x29ea41), LBL['camratio'])),
    (M(0x261fe4), call_rel(M(0x261fe4), M(0x26282c)), call_rel(M(0x261fe4), LBL['flush'])),
    # mode 19 du pilote : VBE 0x100 (640x400) -> 0x17D (1280x960 de DOSBox, vesa_modes = all), CRTC mis a 600 lignes
    (0x364ed4 if EN else 0x364f7c, bytes.fromhex('1300000180029001'), bytes.fromhex('13007d0180029001')),
]


def export(path, cls):
    def arr(b):
        toks = ['0x%02x' % x for x in b]
        lines = [', '.join(toks[i:i + 16]) for i in range(0, len(toks), 16)]
        return 'new byte[] {\n            ' + ',\n            '.join(lines) + '\n        }'
    out = ['// GENERE par tools/hd720.py export - ne pas modifier a la main.',
           '// Terra Nova HD 720 (3D 3 x colonnes / 1,5 x lignes, ecran VESA 1280x960 a 600 lignes, affiche en 1280x720).',
           '// Adresses de la version %s (__FF.EXE prepare).' % ('anglaise GOG / Steam' if EN else 'francaise GOG'),
           'static class ' + cls, '{',
           '    public const uint Zone = 0x%x, End = 0x%x, PoolBase = 0x%x;' % (ZONE, FIN, ZONE + PLACE),
           '    public const uint Code = 0x%x, Data = 0x%x;' % (CODE, DATA),
           '    public const uint Smoothing = 0x%x, HudFilter = 0x%x, Frames = 0x%x, PoolMax = 0x%x, DriverMode = 0x%x;' % (
               V['LISSE'], V['HUDF'], V['FRAMES'], M(0x35d03c), M(0x35f978)),
           '    public const uint Valid = 0x%x, HdWidth = 0x%x, HdHeight = 0x%x;' % (V['VALID'], V['FWW'], V['FH']),
           '    public const uint HudStretch = 0x%x;' % V['HSTR'],
           '    public const uint FpsValue = 0x%x, FpsColour = 0x%x;   // frames per second shown top right (0 = hidden), palette index' % (V['FPSV'], V['FPSCOL']),
           '    public const uint PoolVar = 0x%x;' % M(0x43e360),
           '    public const uint XCode = 0x%x;' % V['XCODE'],
           '    public const uint SvPtr = 0x%x, SvOff = 0x%x;   // small cockpit views in HD: code at the XCODE page + SvOff, then its address in SvPtr' % (V['SVPTR'], SV_XOFF),
           '    public static readonly byte[] SvCode = %s;' % arr(SV_CODE),
           '    public const uint SpeedDone = 0x%x, SpeedOn = 0x%x, SpeedBad = 0x%x;   // speed fixes written by the game: done, bit per group' % (
               V['GPDONE'], V['GPOK'], V['GPBAD']),
           '    public static readonly string[] SpeedNames = { %s };' % ', '.join('"%s"' % n for n, r in GP_GROUPS),
           '    public const uint SpeedOptional = 0x%x;   // groups left alone quietly when their code already ran' % speed_fix.optional_mask(GP_GROUPS),
           '    public static readonly byte[] CodeBytes = %s;' % arr(code),
           '    public static readonly byte[] DataInit = %s;' % arr(bytes(DATA_INIT)),
           '    public static readonly uint[] PatchAt = { %s };' % ', '.join('0x%x' % a for a, o, n in PATCHES + ECRAN),
           '    public static readonly byte[][] PatchOld = {']
    for a, o, n in PATCHES + ECRAN: out.append('        new byte[] { %s },' % ', '.join('0x%02x' % x for x in o))
    out.append('    };')
    out.append('    public static readonly byte[][] PatchNew = {')
    for a, o, n in PATCHES + ECRAN: out.append('        new byte[] { %s },' % ', '.join('0x%02x' % x for x in n))
    out += ['    };', '}']
    with open(path, 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('\n'.join(out) + '\n')
    print('exporte', path, '(%d o de code, %d patchs)' % (len(code), len(PATCHES + ECRAN)))


cmd = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith('--') else 'inject'
if cmd == 'export':
    args = [x for x in sys.argv[2:] if not x.startswith('--')]
    cls = 'HdPayload' + ('En' if EN else 'Fr') + '720'
    export(args[0] if args else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', '%s.cs' % cls), cls)
elif cmd == 'asm':
    print('code %d o, etiquettes %s' % (len(code), {k: hex(a) for k, a in LBL.items()}))
else:
    if POOL_BASE != ZONE + PLACE:
        raise SystemExit('__FF.EXE non prepare : reserve en %#x' % POOL_BASE)
    if any(g.read(ZONE, FIN - ZONE)):
        raise SystemExit('zone du mod non nulle (deja injecte ?)')
    liste = PATCHES + ECRAN
    st = [g.read(a, len(old)) == old for a, old, new in liste]
    if not all(st):
        raise SystemExit('octets inattendus : %s' % st)
    g.write(DATA, bytes(DATA_INIT))
    g.write(CODE, code)
    for a, old, new in liste:
        g.write(a, new)
    print('injecte (RAM seulement)')
