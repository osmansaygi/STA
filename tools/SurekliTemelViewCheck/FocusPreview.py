from pathlib import Path
import sys, subprocess
source=Path(sys.argv[1]); target=sys.argv[2]
lines=source.read_text(encoding='utf-8').splitlines()
pairs=[(int(lines[i]),lines[i+1]) for i in range(0,len(lines)-1,2)]
entities=[]; current=None; active=False
for i,(code,value) in enumerate(pairs):
    if code==2 and value.strip()=='ENTITIES': active=True
    if not active: continue
    if code==0:
        if current: entities.append(current)
        if value.strip()=='ENDSEC': break
        current=[(code,value)]
    elif current is not None: current.append((code,value))
def get(e,k,d='0'): return next((v.strip() for c,v in e if c==k),d)
text=next(e for e in entities if get(e,0)=='TEXT' and get(e,1).startswith('T-'+target+' '))
y=float(get(text,20))
below=float(sys.argv[3]) if len(sys.argv)>3 else 175
selected=[e for e in entities if get(e,0) in ['LINE','LWPOLYLINE','TEXT'] and -below<float(get(e,20))-y<175]
out=source.with_name(source.stem+'-T'+target+'.dxf')
flat=[(0,'SECTION'),(2,'ENTITIES')]+[p for e in selected for p in e]+[(0,'ENDSEC'),(0,'EOF')]
out.write_text('\n'.join(str(v) for p in flat for v in p)+'\n',encoding='utf-8')
subprocess.run([sys.executable,str(Path(__file__).parent.parent/'ParapetSmoke/preview.py'),str(out)],check=True)

