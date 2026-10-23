import struct, sys, re
from pathlib import Path
for filename in sys.argv[1:]:
    data=Path(filename).read_bytes()
    u32=lambda i: struct.unpack_from('<I',data,i)[0]
    u64=lambda i: struct.unpack_from('<Q',data,i)[0]
    streams={u32(u32(12)+12*i):u32(u32(12)+12*i+8) for i in range(u32(8))}
    print('\nDUMP',filename)
    at=streams[4]
    for i in range(u32(at)):
        m=at+4+108*i; n=u32(m+20)
        name=data[n+4:n+4+u32(n)].decode('utf-16le')
        if re.search(r'ST4|coreclr|clrjit|acdbmgd|acmgd|accoremgd',name,re.I):
            print('MODULE', name)
    for encoding in ('utf-16le','latin1'):
        decoded=data.decode(encoding,errors='ignore')
        names=set(re.findall(r'[\w:\\/. -]{0,150}ST4_(?:Plan_ID_Ciz|Yardimci)[\w:/\\. -]{0,180}',decoded))
        for name in sorted(names):
            if '.dll' in name: print('STRING',repr(name[:250]))
