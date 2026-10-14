# Descrambler text at the original pace (frame-rate bug), after TNova Native (Callemann,
# engine/features/descrambler.py, MIT). The descrambler panel (EN 0x26E834) runs once per frame:
#  - readable signal (> 0xEA00): the text scrolls one character per frame (index at EN 0x35B180),
#  - weak signal (> 0x8000): all 26 characters are replaced by random ones, every frame.
# The game ran at about 18-20 fps, so at 60-90 fps the text ran 3 to 5 times too fast to read. Both now step
# on the game's millisecond timer (EN 0x446AE8): one character / one new noise line every 50 ms, the original pace.
# A frame without a step shows the same line again; the weak path still clears the panel state (EN 0x35B184),
# so the decode deadline (50 s of readable signal in a row, EN 0x26F094) works as before.
#   python descrambler_fix.py         prints the pieces for EN and FR (address, old, new)
#   python descrambler_fix.py test    runs the code in an emulator (Unicorn) at several frame rates
#   python descrambler_fix.py cs      C# table for TN+ (src/TNPlusDescrambler.cs)
import struct, sys
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

STEP_MS = 50                         # one character / one noise line (the original game at ~20 fps)
CODE_R, CODE_W = 0x46A0C0, 0x46A2AC  # free gaps of the tool's code area (after the zoom fix, after true 16:9)
LAST_R, LAST_W = 0x4693A0, 0x4693A4  # clocks (a data page: never write in a code page)
LANG = {   # code moves by +0x10 in the French exe here, data by +0x20 (panel) / +0xB0 (timer)
    'EN': dict(SITE_R=0x26EADA, CONT_R=0x26EAE8, IDX=0x35B180, SITE_W=0x26EB42, CONT_W=0x26EB4A, LOST=0x26EBAC,
               END=0x26EBE3, STATE=0x35B184, TIMER=0x446AE8),
    'FR': dict(SITE_R=0x26EAEA, CONT_R=0x26EAF8, IDX=0x35B1A0, SITE_W=0x26EB52, CONT_W=0x26EB5A, LOST=0x26EBBC,
               END=0x26EBF3, STATE=0x35B1A4, TIMER=0x446B98),
}


def asm(src, at):
    return bytes(Ks(KS_ARCH_X86, KS_MODE_32).asm(src, at)[0])


# readable: mov edx,[idx] / lea eax,[edx+1] / mov [idx],eax  ->  the same, the store only on a step
READ_SRC = '''
    mov ecx, {LAST_R}
    call {TICK}
    mov edx, dword ptr [{IDX}]
    jc hold
    lea eax, [edx+1]
    mov dword ptr [{IDX}], eax
hold:
    jmp {CONT_R}
'''
# weak: cmp ecx,0x8000 / jle lost, then a new noise line only on a step (else: state cleared, same line)
WEAK_SRC = '''
    cmp ecx, 0x8000
    jle lost
    mov ecx, {LAST_W}
    call tick
    jnc go
    mov byte ptr [{STATE}], 0
    jmp {END}
go:
    jmp {CONT_W}
lost:
    jmp {LOST}
tick:
    mov eax, dword ptr [{TIMER}]
    sub eax, dword ptr [ecx]
    cmp eax, 250
    ja resync
    cmp eax, {STEP}
    jb wait
    add dword ptr [ecx], {STEP}
    clc
wait:
    ret
resync:
    mov eax, dword ptr [{TIMER}]
    mov dword ptr [ecx], eax
    clc
    ret
'''
# tick: ecx = a clock; CF = 1 when no step is due. A gap over 250 ms (or a timer that went back, new mission)
# restarts the clock with a step, so the first readable frame shows a character at once.


def build(lang):
    L = LANG[lang]
    v = dict(L, LAST_R=LAST_R, LAST_W=LAST_W, STEP=STEP_MS)
    weak = asm(WEAK_SRC.format(**v), CODE_W)
    tick = CODE_W + len(asm(WEAK_SRC.split('tick:')[0].replace('call tick', 'call %d' % CODE_W).format(**v), CODE_W))
    assert weak[tick - CODE_W] == 0xA1, 'tick'  # mov eax, [timer]
    read = asm(READ_SRC.format(TICK=tick, **v), CODE_R)
    assert CODE_R + len(read) <= 0x46A100 and CODE_W + len(weak) <= 0x46A300, (len(read), len(weak))
    old_r = b'\x8b\x15' + struct.pack('<I', L['IDX']) + b'\x8d\x42\x01\xa3' + struct.pack('<I', L['IDX'])
    new_r = asm('jmp %d' % CODE_R, L['SITE_R']).ljust(len(old_r), b'\x90')
    old_w = b'\x81\xf9\x00\x80\x00\x00\x7e' + bytes([L['LOST'] - L['CONT_W']])
    new_w = asm('jmp %d' % CODE_W, L['SITE_W']).ljust(len(old_w), b'\x90')
    assert L['SITE_R'] + len(old_r) == L['CONT_R'] and L['SITE_W'] + len(old_w) == L['CONT_W']
    # the code first (its areas are zero at load), then the two hooks
    return [(CODE_R, bytes(len(read)), read), (CODE_W, bytes(len(weak)), weak),
            (L['SITE_R'], old_r, new_r), (L['SITE_W'], old_w, new_w)], tick


def test():
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
    from unicorn.x86_const import UC_X86_REG_ECX, UC_X86_REG_ESP, UC_X86_REG_EIP
    L = LANG['EN']
    pieces, _ = build('EN')
    for fps in (15, 20, 30, 60, 90, 144):
        mu = Uc(UC_ARCH_X86, UC_MODE_32)
        mu.mem_map(0x200000, 0x300000)
        for a, o, n in pieces: mu.mem_write(a, n)
        exits = (L['CONT_W'], L['END'], L['LOST'])
        mu.hook_add(UC_HOOK_CODE, lambda uc, a, size, d: uc.emu_stop() if a in exits else None)
        dt = 1000 / fps; t = 7000.0; steps_r = steps_w = 0; frames = 0
        for _ in range(int(fps * 4)):           # 4 s readable
            t += dt; mu.mem_write(L['TIMER'], struct.pack('<I', int(t)))
            mu.reg_write(UC_X86_REG_ESP, 0x4F0000); mu.reg_write(UC_X86_REG_ECX, 0xF000)
            i0 = struct.unpack('<I', mu.mem_read(L['IDX'], 4))[0]
            mu.emu_start(CODE_R, L['CONT_R'])
            steps_r += struct.unpack('<I', mu.mem_read(L['IDX'], 4))[0] - i0
        for _ in range(int(fps * 4)):           # 4 s weak
            t += dt; mu.mem_write(L['TIMER'], struct.pack('<I', int(t)))
            mu.mem_write(L['STATE'], b'\x07')
            mu.reg_write(UC_X86_REG_ESP, 0x4F0000); mu.reg_write(UC_X86_REG_ECX, 0xC000)
            mu.emu_start(CODE_W, 0x500000)
            eip = mu.reg_read(UC_X86_REG_EIP)
            assert eip in (L['CONT_W'], L['END']), hex(eip)
            if eip == L['CONT_W']: steps_w += 1
            else: assert mu.mem_read(L['STATE'], 1)[0] == 0
        mu.reg_write(UC_X86_REG_ESP, 0x4F0000); mu.reg_write(UC_X86_REG_ECX, 0x7000)
        mu.emu_start(CODE_W, 0x500000)
        assert mu.reg_read(UC_X86_REG_EIP) == L['LOST']
        orig = min(fps, 1000 / STEP_MS) * 4
        print('%3d fps: %3d characters in 4 s, %3d noise lines in 4 s (the game at this rate: %d / %d)' % (
            fps, steps_r, steps_w, fps * 4, fps * 4))
        assert abs(steps_r - orig) <= 2 and abs(steps_w - orig) <= 2, (steps_r, steps_w, orig)
    print('ok')


def export_cs():
    at, old, new = [], [], []
    for lang in ('EN', 'FR'):
        p, _ = build(lang)
        at.append(', '.join('0x%X' % a for a, o, n in p))
        old.append(', '.join('"%s"' % o.hex().upper() for a, o, n in p))
        new.append(', '.join('"%s"' % n.hex().upper() for a, o, n in p))
    print('// Generated by tools/descrambler_fix.py (python descrambler_fix.py cs > ../src/TNPlusDescrambler.cs): do not edit.')
    print('// Descrambler text at the original pace: one character / one noise line every %d ms (see descrambler_fix.py).' % STEP_MS)
    print('static class DescramblerFix\n{')
    print('    public static readonly uint[][] At = {\n' + ''.join('        new uint[] { %s },\n' % x for x in at) + '    };')
    print('    public static readonly string[][] Old = {\n' + ''.join('        new string[] { %s },\n' % x for x in old) + '    };')
    print('    public static readonly string[][] New = {\n' + ''.join('        new string[] { %s },\n' % x for x in new) + '    };')
    print('    public const int Zones = 2;                      // the first 2 pieces are code in free areas, then 2 hooks')
    print('}')


if __name__ == '__main__':
    if 'test' in sys.argv: test()
    elif 'cs' in sys.argv: export_cs()
    else:
        for lang in ('EN', 'FR'):
            p, tick = build(lang)
            for a, o, n in p: print(lang, '%#x' % a, o.hex(), n.hex())
            print(lang, 'tick %#x, code ends %#x / %#x' % (tick, CODE_R + len(p[0][2]), CODE_W + len(p[1][2])))
