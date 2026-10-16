"""Local DXF geometry QA preview; not an AutoCAD plotting replacement."""
import sys, math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

src=Path(sys.argv[1])
rows=src.read_text(encoding='utf-8').splitlines()
pairs=[(int(rows[i]),rows[i+1].strip()) for i in range(0,len(rows)-1,2)]
entities=[]; current=None; section=''
for i,(code,value) in enumerate(pairs):
    if code==0:
        if current is not None: entities.append(current)
        current=[] if section=='ENTITIES' and value!='ENDSEC' else None
        if current is not None: current.append((code,value))
        if value=='ENDSEC': section=''
    elif code==2 and i and pairs[i-1]==(0,'SECTION'): section=value
    elif current is not None: current.append((code,value))
def get(e,c,default='0'): return next((v for k,v in e if k==c),default)
def n(e,c,default='0'): return float(get(e,c,default))
def points(e):
    p=[]
    for c,v in e:
        if c==10:p.append([float(v),0,0])
        elif c==20 and p:p[-1][1]=float(v)
        elif c==42 and p:p[-1][2]=float(v)
    return p

allpoints=[]
for e in entities:
    if get(e,0)=='LINE': allpoints.extend([(n(e,10),n(e,20)),(n(e,11),n(e,21))])
    if get(e,0)=='LWPOLYLINE': allpoints.extend([(x,y) for x,y,b in points(e)])
    if get(e,0)=='TEXT': allpoints.append((n(e,10),n(e,20)))
x0=min(x for x,y in allpoints)-15; x1=max(x for x,y in allpoints)+30
y0=min(y for x,y in allpoints)-20; y1=max(y for x,y in allpoints)+30
scale=min(1800/(x1-x0),1000/(y1-y0))
im=Image.new('RGB',(int((x1-x0)*scale)+80,int((y1-y0)*scale)+80),'#202831')
d=ImageDraw.Draw(im)
def xy(x,y):return (40+(x-x0)*scale,40+(y1-y)*scale)
for e in entities:
    layer=get(e,8); typ=get(e,0)
    col='#75818a' if 'DONATI IZI' in layer else '#51c4e8' if layer=='DONATI (BEYKENT)' else '#85db49' if 'DONATI YAZISI' in layer else '#e9edf1'
    if typ=='LINE':d.line([xy(n(e,10),n(e,20)),xy(n(e,11),n(e,21))],fill=col,width=2)
    elif typ=='LWPOLYLINE':
        p=points(e)
        closed=int(n(e,70))&1
        if closed and len(p)==2 and abs(p[0][2]-1)<0.001:
            x=(p[0][0]+p[1][0])/2; y=p[0][1]; r=abs(p[1][0]-p[0][0])/2
            d.ellipse([xy(x-r,y+r),xy(x+r,y-r)],fill=col)
            continue
        if closed:p=p+[p[0]]
        curve=[]
        for a,b in zip(p,p[1:]):
            x,y,bulge=a; xx,yy,_=b
            curve.append(xy(x,y))
            if abs(bulge)>1e-8:
                chord=math.hypot(xx-x,yy-y); angle=4*math.atan(bulge)
                cx=(x+xx)/2-(yy-y)*(1-bulge*bulge)/(4*bulge)
                cy=(y+yy)/2+(xx-x)*(1-bulge*bulge)/(4*bulge)
                radius=math.hypot(x-cx,y-cy); start=math.atan2(y-cy,x-cx)
                curve.extend(xy(cx+radius*math.cos(start+angle*j/20),cy+radius*math.sin(start+angle*j/20)) for j in range(1,20))
        if p:curve.append(xy(p[-1][0],p[-1][1]))
        if len(curve)>1:d.line(curve,fill=col,width=2)
    elif typ=='TEXT':
        text=get(e,1,'').replace('%%c','ø'); size=max(9,int(n(e,40)*scale))
        font=ImageFont.truetype('C:/Windows/Fonts/bahnschrift.ttf',size)
        box=font.getbbox(text); w=box[2]-box[0]; h=box[3]-box[1]
        tile=Image.new('RGBA',(w+4,h+4)); ImageDraw.Draw(tile).text((2-box[0],2-box[1]),text,font=font,fill=col)
        angle=n(e,50)
        if angle:tile=tile.rotate(angle,expand=True)
        px,py=xy(n(e,10),n(e,20))
        im.paste(tile,(int(px),int(py-tile.height)),tile)
im.save(src.with_suffix('.preview.png'))
