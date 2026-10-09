# passes supplementaires du constructeur de terrain : bandes laterales hors de la fenetre de 3 octants
import math, struct
from keystone import Ks, KS_ARCH_X86, KS_MODE_32
ks = Ks(KS_ARCH_X86, KS_MODE_32)
W0, SAVE, VARS, SIN = 0x46A300, 0x469440, 0x469480, 0x469500
MODE = 0x4694F0
VSTART, VEND = 0x469410, 0x469414   # indice du premier sommet de la passe / sommets utilises a la fin
VC1, VC2, VC3 = 0x46A9C0, 0x46A9D8, 0x46A9F0
SORT = 0x46A780        # tri de la liste par anneau carre (proche -> loin), apres les bandes
CNT = 0x469900         # 64 compteurs
SINTAB = b''.join(struct.pack('<i', int(round(65536 * math.sin(2 * math.pi * i / 256)))) for i in range(256))
out = {}
for lang, builder, hook, setup in (('FR', 0x2AA388, 0x29E2F0, 0x2A8968), ('EN', 0x2AA398, 0x29E300, 0x2A8978)):
    H, h, B, T, OFF, HALF, C1, HS, E1C, E1S, E2C, E2S = (VARS + 4 * i for i in range(12))
    alloc, free = (0x2C4FD0, 0x2C5074) if lang == 'FR' else (0x2C4F40, 0x2C4FE4)
    LIST, NCELL = (0x427E90, 0x427E94) if lang == 'FR' else (0x427DE0, 0x427DE4)   # liste des cellules, nombre
    VMAP, VBUF = (0x438CE4, 0x438CE8) if lang == 'FR' else (0x438C34, 0x438C38)   # carte et tampon des sommets
    VHOOK = (0x2AE01B, 0x2AE033, 0x2AF4B9) if lang == 'FR' else (0x2AE02B, 0x2AE043, 0x2AF4C9)
    a = f"""push ecx
push ebp
push esi
push edi
mov ebp, eax
mov esi, edx
mov edi, ebx
mov dword ptr [{VSTART:#x}], 0
mov ecx, {builder:#x}
call ecx
mov eax, dword ptr [{VEND:#x}]
mov dword ptr [{VSTART:#x}], eax
mov dword ptr [{VARS + 56:#x}], esi
cmp dword ptr [{MODE:#x}], 0
je done
movzx eax, word ptr [esi+0xc]
mov dword ptr [{H:#x}], eax
movzx ecx, word ptr [ebp+0x18]
mov dword ptr [{h:#x}], ecx
and eax, 0x1fff
sub eax, 0x1000
jge p1
neg eax
p1:
neg eax
add eax, 0x3080
cmp ecx, eax
jbe done
sub eax, 0x80
mov dword ptr [{VARS + 84:#x}], eax
cmp dword ptr [{VSTART:#x}], 6400
jae done
movzx edx, word ptr [{NCELL:#x}]
cmp edx, 2900
jae done
mov dword ptr [{T:#x}], edx
mov dword ptr [{C1:#x}], edx
mov edx, ecx
add edx, eax
shr edx, 1
mov dword ptr [{OFF:#x}], edx
sub ecx, eax
shr ecx, 1
add ecx, 0x100
mov dword ptr [{HALF:#x}], ecx
mov edx, dword ptr [{LIST:#x}]
mov dword ptr [{B:#x}], edx
mov edx, dword ptr [esi+0xe]
mov dword ptr [{VARS + 60:#x}], edx
mov edx, dword ptr [esi+0x12]
mov dword ptr [{VARS + 64:#x}], edx
push esi
push edi
mov esi, edi
mov edi, {SAVE:#x}
mov ecx, 16
rep movsd dword ptr es:[edi], dword ptr [esi]
pop edi
pop esi
mov eax, 2048
mov ecx, {alloc:#x}
call ecx
mov dword ptr [{VARS + 100:#x}], eax
test eax, eax
je nobmp
push edi
mov edi, eax
xor eax, eax
mov ecx, 512
rep stosd
pop edi
mov ebx, dword ptr [{B:#x}]
mov ecx, dword ptr [{C1:#x}]
test ecx, ecx
je nobmp
bmark:
movzx eax, word ptr [ebx+0x12]
and eax, 0x7f
shl eax, 7
movzx edx, word ptr [ebx+0x10]
and edx, 0x7f
or eax, edx
mov edx, dword ptr [{VARS + 100:#x}]
bts dword ptr [edx], eax
add ebx, 32
dec ecx
jnz bmark
nobmp:
mov eax, dword ptr [{H:#x}]
add eax, dword ptr [{VARS + 84:#x}]
sub eax, 0x300
mov dword ptr [{VARS + 76:#x}], eax
mov eax, dword ptr [{H:#x}]
add eax, dword ptr [{h:#x}]
add eax, 0x200
mov dword ptr [{VARS + 80:#x}], eax
call strip
cmp dword ptr [{MODE:#x}], 5
je nostrip2
mov eax, dword ptr [{H:#x}]
sub eax, dword ptr [{h:#x}]
sub eax, 0x200
mov dword ptr [{VARS + 76:#x}], eax
mov eax, dword ptr [{H:#x}]
sub eax, dword ptr [{VARS + 84:#x}]
add eax, 0x300
mov dword ptr [{VARS + 80:#x}], eax
call strip
nostrip2:
mov eax, dword ptr [{VARS + 100:#x}]
test eax, eax
je nofree
mov ecx, {free:#x}
call ecx
nofree:
mov eax, dword ptr [{H:#x}]
mov word ptr [esi+0xc], ax
mov eax, dword ptr [{VARS + 60:#x}]
mov dword ptr [esi+0xe], eax
mov eax, dword ptr [{VARS + 64:#x}]
mov dword ptr [esi+0x12], eax
mov eax, dword ptr [{h:#x}]
mov word ptr [ebp+0x18], ax
mov eax, dword ptr [{B:#x}]
mov dword ptr [{LIST:#x}], eax
mov eax, dword ptr [{T:#x}]
cmp dword ptr [{MODE:#x}], 1
je reset
cmp dword ptr [{MODE:#x}], 5
jne keep
reset:
mov eax, dword ptr [{C1:#x}]
keep:
mov word ptr [{NCELL:#x}], ax
cmp dword ptr [{MODE:#x}], 7
je nosort
mov ecx, {SORT:#x}
call ecx
nosort:
push esi
push edi
mov esi, {SAVE:#x}
mov ecx, 16
rep movsd dword ptr es:[edi], dword ptr [esi]
pop edi
pop esi
done:
mov dword ptr [{VSTART:#x}], 0
pop edi
pop esi
pop ebp
pop ecx
ret

strip:
mov eax, dword ptr [{VARS + 80:#x}]
sub eax, dword ptr [{VARS + 76:#x}]
shr eax, 1
mov ecx, eax
add ecx, dword ptr [{VARS + 76:#x}]
add eax, 0x200
mov dword ptr [{HALF:#x}], eax
mov eax, ecx
and eax, 0xffff
mov dword ptr [{HS:#x}], eax
mov word ptr [esi+0xc], ax
mov ebx, {VARS + 68:#x}
call sincos
mov eax, dword ptr [{VARS + 68:#x}]
mov dword ptr [esi+0xe], eax
mov eax, dword ptr [{VARS + 72:#x}]
mov dword ptr [esi+0x12], eax
mov eax, dword ptr [{HALF:#x}]
mov word ptr [ebp+0x18], ax
mov eax, dword ptr [{T:#x}]
cmp eax, 2900
jae sret
shl eax, 5
add eax, dword ptr [{B:#x}]
mov dword ptr [{LIST:#x}], eax
cmp dword ptr [{MODE:#x}], 4
je sret
mov eax, ebp
mov edx, esi
mov ebx, edi
mov ecx, {builder:#x}
call ecx
mov eax, dword ptr [{VEND:#x}]
mov dword ptr [{VSTART:#x}], eax
mov eax, dword ptr [esi]
mov dword ptr [{VARS + 48:#x}], eax
mov eax, dword ptr [esi+4]
mov dword ptr [{VARS + 52:#x}], eax
movzx eax, word ptr [{NCELL:#x}]
mov dword ptr [{VARS + 88:#x}], eax
cmp dword ptr [{MODE:#x}], 5
je sret
cmp dword ptr [{MODE:#x}], 3
jne filt
movzx eax, word ptr [{NCELL:#x}]
add dword ptr [{T:#x}], eax
ret
filt:
mov eax, dword ptr [{VARS + 76:#x}]
mov ebx, {E1C:#x}
call sincos
mov eax, dword ptr [{VARS + 80:#x}]
mov ebx, {E2C:#x}
call sincos
push ebp
push edi
movzx ecx, word ptr [{NCELL:#x}]
mov ebx, dword ptr [{LIST:#x}]
mov edi, ebx
test ecx, ecx
je floop_end
floop:
mov edx, dword ptr [{VARS + 100:#x}]
test edx, edx
je nobt
movzx eax, word ptr [ebx+0x12]
and eax, 0x7f
shl eax, 7
movzx ebp, word ptr [ebx+0x10]
and ebp, 0x7f
or eax, ebp
bt dword ptr [edx], eax
jb skip
nobt:
movsx eax, word ptr [ebx+0x10]
shl eax, 16
add eax, 0x8000
sub eax, dword ptr [esi]
sar eax, 8
movsx edx, word ptr [ebx+0x12]
shl edx, 16
add edx, 0x8000
sub edx, dword ptr [esi+4]
sar edx, 8
far:
push eax
push edx
push ecx
mov ecx, eax
imul ecx, dword ptr [{VARS + 60:#x}]
imul edx, dword ptr [{VARS + 64:#x}]
add ecx, edx
cmp ecx, 0x2000000
pop ecx
pop edx
pop eax
jl skip
push eax
push edx
imul edx, dword ptr [{E1C:#x}]
imul eax, dword ptr [{E1S:#x}]
cmp edx, eax
pop edx
pop eax
jl skip
imul eax, dword ptr [{E2S:#x}]
imul edx, dword ptr [{E2C:#x}]
cmp eax, edx
jl skip
take:
mov edx, dword ptr [{VARS + 100:#x}]
test edx, edx
je nomark
movzx eax, word ptr [ebx+0x12]
and eax, 0x7f
shl eax, 7
movzx ebp, word ptr [ebx+0x10]
and ebp, 0x7f
or eax, ebp
bts dword ptr [edx], eax
nomark:
mov eax, edi
sub eax, dword ptr [{B:#x}]
shr eax, 5
cmp eax, 3600
jae skip
cmp edi, ebx
je same
push esi
push ecx
mov esi, ebx
mov ecx, 8
push edi
rep movsd dword ptr es:[edi], dword ptr [esi]
pop edi
pop ecx
pop esi
same:
add edi, 32
skip:
add ebx, 32
dec ecx
jnz floop
floop_end:
sub edi, dword ptr [{LIST:#x}]
shr edi, 5
add dword ptr [{T:#x}], edi
pop edi
pop ebp
sret:
ret

sincos:
mov ecx, eax
shr ecx, 8
and ecx, 0xff
mov edx, dword ptr [ecx*4 + {SIN:#x}]
mov dword ptr [ebx+4], edx
add ecx, 64
and ecx, 0xff
mov edx, dword ptr [ecx*4 + {SIN:#x}]
mov dword ptr [ebx], edx
ret"""
    vbase = VBUF
    c1 = f"""mov ebx, dword ptr [{VSTART:#x}]
imul ebx, ebx, 0x27
add ebx, dword ptr [{vbase:#x}]
ret"""
    c2 = f"""mov ecx, dword ptr [{VSTART:#x}]
cmp ah, 3
sete al
ret"""
    c3 = f"""push eax
push ecx
push edx
mov eax, ebx
sub eax, dword ptr [{vbase:#x}]
xor edx, edx
mov ecx, 0x27
div ecx
mov dword ptr [{VEND:#x}], eax
pop edx
pop ecx
pop eax
add esp, 0x130
pop ebp
pop edi
pop esi
ret"""
    c4 = f"""pushad
cmp dword ptr [{MODE:#x}], 8
jne m_norm
mov esi, dword ptr [{VARS + 24:#x}]
shl esi, 5
add esi, dword ptr [{LIST:#x}]
mov edi, dword ptr [{LIST:#x}]
mov ecx, dword ptr [{VARS + 12:#x}]
sub ecx, dword ptr [{VARS + 24:#x}]
mov word ptr [{NCELL:#x}], cx
shl ecx, 3
rep movsd dword ptr es:[edi], dword ptr [esi]
jmp m_out
m_norm:
mov eax, dword ptr [{VARS + 12:#x}]
sub eax, dword ptr [{VARS + 24:#x}]
jbe m_out
mov edi, {CNT:#x}
xor eax, eax
mov ecx, 64
rep stosd
mov ebp, dword ptr [{VARS + 56:#x}]
mov ebx, dword ptr [{VARS + 24:#x}]
shl ebx, 5
add ebx, dword ptr [{LIST:#x}]
mov ecx, dword ptr [{VARS + 12:#x}]
sub ecx, dword ptr [{VARS + 24:#x}]
m_cnt:
call s_key
inc dword ptr [eax*4 + {CNT:#x}]
add ebx, 32
dec ecx
jnz m_cnt
xor eax, eax
xor ecx, ecx
m_pre:
mov edx, dword ptr [ecx*4 + {CNT:#x}]
mov dword ptr [ecx*4 + {CNT:#x}], eax
add eax, edx
inc ecx
cmp ecx, 64
jb m_pre
mov eax, dword ptr [{VARS + 12:#x}]
shl eax, 6
mov ecx, {alloc:#x}
call ecx
test eax, eax
je m_out
mov dword ptr [{VARS + 96:#x}], eax
mov ebx, dword ptr [{VARS + 24:#x}]
shl ebx, 5
add ebx, dword ptr [{LIST:#x}]
mov ecx, dword ptr [{VARS + 12:#x}]
sub ecx, dword ptr [{VARS + 24:#x}]
m_mov:
push ecx
call s_key
mov edi, dword ptr [eax*4 + {CNT:#x}]
inc dword ptr [eax*4 + {CNT:#x}]
shl edi, 5
add edi, dword ptr [{VARS + 96:#x}]
mov esi, ebx
mov ecx, 8
rep movsd dword ptr es:[edi], dword ptr [esi]
pop ecx
add ebx, 32
dec ecx
jnz m_mov
mov edi, dword ptr [{VARS + 12:#x}]
shl edi, 5
add edi, dword ptr [{VARS + 96:#x}]
mov dword ptr [{VARS + 104:#x}], edi
mov edx, dword ptr [{VARS + 96:#x}]
mov eax, dword ptr [{VARS + 12:#x}]
sub eax, dword ptr [{VARS + 24:#x}]
shl eax, 5
add eax, edx
mov dword ptr [{VARS + 108:#x}], eax
mov ebx, dword ptr [{LIST:#x}]
mov ecx, dword ptr [{VARS + 24:#x}]
mov dword ptr [{VARS + 120:#x}], 0
m_p1:
test ecx, ecx
je m_rest
push ecx
call s_key
cmp eax, dword ptr [{VARS + 120:#x}]
jbe m_rm
mov dword ptr [{VARS + 120:#x}], eax
m_rm:
mov eax, ebx
mov ebx, edx
m_ins:
cmp ebx, dword ptr [{VARS + 108:#x}]
jae m_insd
push eax
call s_key
mov ecx, eax
pop eax
cmp ecx, dword ptr [{VARS + 120:#x}]
ja m_insd
mov esi, ebx
mov ecx, 8
rep movsd dword ptr es:[edi], dword ptr [esi]
add ebx, 32
jmp m_ins
m_insd:
mov edx, ebx
mov ebx, eax
mov esi, ebx
mov ecx, 8
rep movsd dword ptr es:[edi], dword ptr [esi]
add ebx, 32
pop ecx
dec ecx
jmp m_p1
m_rest:
mov esi, edx
mov ecx, dword ptr [{VARS + 108:#x}]
sub ecx, edx
shr ecx, 2
rep movsd dword ptr es:[edi], dword ptr [esi]
mov esi, dword ptr [{VARS + 104:#x}]
mov edi, dword ptr [{LIST:#x}]
mov ecx, dword ptr [{VARS + 12:#x}]
shl ecx, 3
rep movsd dword ptr es:[edi], dword ptr [esi]
mov eax, dword ptr [{VARS + 96:#x}]
mov ecx, {free:#x}
call ecx
m_out:
popad
ret
s_key:
push edx
push esi
movsx eax, word ptr [ebx+0x10]
movsx edx, word ptr [ebp+2]
sub eax, edx
jge skk1
neg eax
skk1:
movsx esi, word ptr [ebx+0x12]
movsx edx, word ptr [ebp+6]
sub esi, edx
jge skk2
neg esi
skk2:
cmp eax, esi
jge skk3
mov eax, esi
skk3:
pop esi
pop edx
cmp eax, 63
jbe skk4
mov eax, 63
skk4:
ret"""
    v1, _ = ks.asm(c1, VC1); v2, _ = ks.asm(c2, VC2); v3, _ = ks.asm(c3, VC3); v4, _ = ks.asm(c4, SORT)
    assert SORT + len(v4) <= VC1, len(v4)
    assert VC1 + len(v1) <= VC2 and VC2 + len(v2) <= VC3 and VC3 + len(v3) <= 0x46AA40, (len(v1), len(v2), len(v3))
    e, _ = ks.asm(a, W0)
    assert W0 + len(e) <= SORT, len(e)
    rel = lambda at, to: bytes([0xE8]) + (to - (at + 5)).to_bytes(4, 'little', signed=True)
    out[lang] = (bytes(e).hex().upper(), rel(hook, builder).hex().upper(), rel(hook, W0).hex().upper(), SINTAB.hex().upper())
    print(lang, len(e)); [print('  ', x) for x in out[lang][:3]]
    jmp = lambda at, to: bytes([0xE9]) + (to - (at + 5)).to_bytes(4, 'little', signed=True)
    if True:
        h1, h2, h3 = VHOOK
        hooks = [(h1, '8b1d' + VBUF.to_bytes(4, 'little').hex(), rel(h1, VC1) + bytes([0x90])),
                 (h2, '31c980fc030f94c0', rel(h2, VC2) + bytes([0x90]) * 3),
                 (h3, '81c4300100005d5f5ec3', jmp(h3, VC3) + bytes([0x90]) * 5)]
        parts = ['poke=%x:%s:%s' % (a, '00' * len(c), bytes(c).hex()) for a, c in ((SORT, v4), (VC1, v1), (VC2, v2), (VC3, v3))]
        parts += ['poke=%x:%s:%s' % (a, o, n.hex()) for a, o, n in hooks]
        print(('VPOKES ' if lang == 'FR' else 'VPOKES_EN ') + ' '.join(parts))
print('SIN', out['FR'][3])
