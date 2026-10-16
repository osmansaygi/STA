from pathlib import Path
from collections import Counter
from PIL import Image, ImageDraw, ImageFont
import json, math, sys

source = Path(sys.argv[1]) if len(sys.argv)>1 else Path(__file__).parent/'parapet.dxf'
root = source.parent
lines = source.read_text(encoding='utf-8', errors='replace').splitlines()
pairs = [(int(lines[i].strip()), lines[i+1].strip()) for i in range(0,len(lines)-1,2)]
entities=[]; active=False; e=None
for i,(c,v) in enumerate(pairs):
    if c==2 and v=='ENTITIES': active=True; continue
    if active and c==0:
        if e: entities.append(e)
        e=None
        if v=='ENDSEC': break
        e={'type':v,'pairs':[]}
    elif active and e is not None: e['pairs'].append((c,v))
def val(e,c,d=''): return next((v for k,v in e['pairs'] if k==c),d)
def num(e,c,d=0): return float(val(e,c,str(d)))
def pts(e):
    p=[]
    for c,v in e['pairs']:
        if c==10: p.append([float(v),0])
        elif c==20 and p: p[-1][1]=float(v)
    return p
entities=[e for e in entities if val(e,67,'0')=='0']
report={'counts':dict(Counter(e['type'] for e in entities)), 'layers':dict(Counter(val(e,8) for e in entities)), 'texts':[], 'geometry':[]}
for e in entities:
    t=e['type']; layer=val(e,8)
    if t in ['TEXT','MTEXT','DIMENSION','INSERT']:
        report['texts'].append({'type':t,'layer':layer,'text':val(e,1),'name':val(e,2),'x':num(e,10),'y':num(e,20),'measurement':num(e,42)})
    if t in ['LINE','LWPOLYLINE']:
        report['geometry'].append({'type':t,'layer':layer,'points':pts(e) if t=='LWPOLYLINE' else [[num(e,10),num(e,20)],[num(e,11),num(e,21)]]})
(root/'inspection.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
allp=[p for g in report['geometry'] for p in g['points']]
x0=min(p[0] for p in allp); x1=max(p[0] for p in allp); y0=min(p[1] for p in allp); y1=max(p[1] for p in allp)
s=min(1700/(x1-x0),1100/(y1-y0)); im=Image.new('RGB',(1900,1300),'#20242a'); d=ImageDraw.Draw(im)
def xy(x,y): return (80+(x-x0)*s,1200-(y-y0)*s)
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
for e in entities:
    t=e['type']; layer=val(e,8); col='#ef5a60' if 'DONATI' in layer else '#75c9dd' if 'OLCU' in layer else '#ececec'
    if t=='LINE': d.line([xy(num(e,10),num(e,20)),xy(num(e,11),num(e,21))],fill=col,width=2)
    elif t=='LWPOLYLINE':
        p=pts(e)
        if int(num(e,70))&1 and p: p=p+[p[0]]
        if len(p)>1: d.line([xy(*q) for q in p],fill=col,width=2)
    elif t=='CIRCLE':
        x=num(e,10); y=num(e,20); r=num(e,40); d.ellipse([xy(x-r,y+r),xy(x+r,y-r)],outline=col,width=2)
    elif t in ['TEXT','MTEXT']: d.text(xy(num(e,10),num(e,20)),val(e,1),font=font,fill=col)
im.save(source.with_suffix('.png'))
print(json.dumps({k:v for k,v in report.items() if k!='geometry'},ensure_ascii=False,indent=2))
