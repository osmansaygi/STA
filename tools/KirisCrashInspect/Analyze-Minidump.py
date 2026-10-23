"""Read-only native exception/module/stack-slot inspection; stack slots are not an unwound trace."""
import ctypes as C
import struct as S
import sys
import bisect
from pathlib import Path

data = Path(sys.argv[1]).read_bytes()
u32 = lambda at: S.unpack_from('<I', data, at)[0]
u64 = lambda at: S.unpack_from('<Q', data, at)[0]
assert data[:4] == b'MDMP'
streams = {}
for i in range(u32(8)):
    at = u32(12) + 12 * i
    streams[u32(at)] = (u32(at + 4), u32(at + 8))
mods = []
at = streams[4][1]
for i in range(u32(at)):
    m = at + 4 + 108 * i
    name = u32(m + 20)
    path = data[name + 4:name + 4 + u32(name)].decode('utf-16le')
    mods.append((u64(m), u32(m + 8), path))

kernel = C.WinDLL('kernel32', use_last_error=True)
kernel.GetCurrentProcess.restype = C.c_void_p
proc = kernel.GetCurrentProcess()
dbg = C.WinDLL('dbghelp', use_last_error=True)
dbg.SymInitializeW.argtypes = [C.c_void_p, C.c_wchar_p, C.c_bool]
dbg.SymInitializeW.restype = C.c_bool
dbg.SymSetOptions.argtypes = [C.c_ulong]
dbg.SymLoadModuleExW.argtypes = [C.c_void_p, C.c_void_p, C.c_wchar_p, C.c_wchar_p, C.c_ulonglong, C.c_ulong, C.c_void_p, C.c_ulong]
dbg.SymLoadModuleExW.restype = C.c_ulonglong
dbg.SymFromAddr.argtypes = [C.c_void_p, C.c_ulonglong, C.POINTER(C.c_ulonglong), C.c_void_p]
dbg.SymFromAddr.restype = C.c_bool
dbg.SymCleanup.argtypes = [C.c_void_p]
dbg.SymSetOptions(0x2 | 0x4 | 0x200)  # undecorate/deferred/fail critical errors, local symbols only
dbg.SymInitializeW(proc, '', False)
loaded = set()

def describe(address):
    for base, size, path in mods:
        if base <= address < base + size:
            if base not in loaded:
                dbg.SymLoadModuleExW(proc, None, path, None, base, size, None, 0)
                loaded.add(base)
            buf = C.create_string_buffer(1200)
            S.pack_into('<I', buf, 0, 88)
            S.pack_into('<I', buf, 80, 1024)
            displacement = C.c_ulonglong()
            symbol = ''
            if dbg.SymFromAddr(proc, address, C.byref(displacement), buf):
                length = S.unpack_from('<I', buf, 76)[0]
                symbol = ' ' + buf.raw[84:84 + length].decode('utf-8', errors='replace') + '+0x%x' % displacement.value
            return '%s+0x%x%s' % (Path(path).name, address - base, symbol)
    return None

exc = streams[6][1]
thread_id = u32(exc)
context = u32(exc + 164)
rsp, rip = u64(context + 152), u64(context + 248)
print('Exception code=0x%x thread=%d RIP=%s' % (u32(exc + 8), thread_id, describe(rip)))
at = streams[3][1]
for i in range(u32(at)):
    t = at + 4 + 48 * i
    if u32(t) != thread_id:
        continue
    stack_base, stack_size, stack_rva = u64(t + 24), u32(t + 32), u32(t + 36)
    # Isolated helper process only: map the recorded stack at its original address so
    # Windows can unwind installed DLL unwind tables without executing their code.
    kernel.VirtualAlloc.argtypes = [C.c_void_p, C.c_size_t, C.c_ulong, C.c_ulong]
    kernel.VirtualAlloc.restype = C.c_void_p
    kernel.VirtualFree.argtypes = [C.c_void_p, C.c_size_t, C.c_ulong]
    kernel.LoadLibraryExW.argtypes = [C.c_wchar_p, C.c_void_p, C.c_ulong]
    kernel.LoadLibraryExW.restype = C.c_void_p
    kernel.FreeLibrary.argtypes = [C.c_void_p]
    unwind = C.WinDLL('ntdll').RtlVirtualUnwind
    unwind.argtypes = [C.c_ulong, C.c_ulonglong, C.c_ulonglong, C.c_void_p, C.c_void_p, C.POINTER(C.c_void_p), C.POINTER(C.c_ulonglong), C.c_void_p]
    unwind.restype = C.c_void_p
    alloc_base = stack_base & ~0xffff
    alloc_size = (stack_base + stack_size - alloc_base + 4095) & ~4095
    allocated = kernel.VirtualAlloc(alloc_base, alloc_size, 0x3000, 4)
    libraries = {}
    if allocated == alloc_base:
        C.memmove(stack_base, data[stack_rva:stack_rva + stack_size], stack_size)
        context_size = u32(exc + 160)
        ctx = C.create_string_buffer(data[context:context + context_size], context_size)
        print('Windows unwind of native exception frames:')
        for frame in range(45):
            pc, sp = S.unpack_from('<Q', ctx, 248)[0], S.unpack_from('<Q', ctx, 152)[0]
            desc = describe(pc)
            if not desc or not stack_base <= sp < stack_base + stack_size:
                print('  [end of available native frames]'); break
            print('  #%d %s' % (frame, desc))
            mod = next(m for m in mods if m[0] <= pc < m[0] + m[1])
            base, size, path = mod
            if base not in libraries:
                pe = Path(path).read_bytes()
                header = S.unpack_from('<I', pe, 0x3c)[0]
                sections = S.unpack_from('<H', pe, header + 6)[0]
                opt_size = S.unpack_from('<H', pe, header + 20)[0]
                opt = header + 24
                pdata_rva, pdata_size = S.unpack_from('<II', pe, opt + 112 + 3 * 8)
                def raw(rva):
                    for section in range(sections):
                        entry = opt + opt_size + section * 40
                        vs, va, rs, rp = S.unpack_from('<IIII', pe, entry + 8)
                        if va <= rva < va + max(vs, rs): return rp + rva - va
                    return rva
                entries = [S.unpack_from('<III', pe, raw(pdata_rva) + n) for n in range(0, pdata_size, 12)]
                mapped = kernel.LoadLibraryExW(path, None, 1)  # DONT_RESOLVE_DLL_REFERENCES
                libraries[base] = mapped, entries, pdata_rva
            mapped, entries, pdata_rva = libraries[base]
            relative = pc - base
            index = bisect.bisect_right([e[0] for e in entries], relative) - 1
            if mapped and index >= 0 and entries[index][0] <= relative < entries[index][1]:
                handler = C.c_void_p(); estab = C.c_ulonglong()
                S.pack_into('<Q', ctx, 248, mapped + relative)
                unwind(0, mapped, mapped + relative, mapped + pdata_rva + index * 12, ctx, C.byref(handler), C.byref(estab), None)
            else:
                S.pack_into('<Q', ctx, 248, C.c_ulonglong.from_address(sp).value)
                S.pack_into('<Q', ctx, 152, sp + 8)
        for mapped, _, _ in libraries.values():
            if mapped: kernel.FreeLibrary(mapped)
        kernel.VirtualFree(allocated, 0, 0x8000)
    print('Candidate native return-address slots (not an unwound stack):')
    count = 0
    for offset in range(max(0, rsp - stack_base), stack_size - 7, 8):
        address = u64(stack_rva + offset)
        desc = describe(address)
        if desc:
            print('  RSP+0x%x %s' % (stack_base + offset - rsp, desc))
            count += 1
            if count >= 55:
                break
    break
dbg.SymCleanup(proc)
