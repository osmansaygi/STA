"""Lightweight preview of the actual Core Console DXF (not a CAD renderer)."""
from pathlib import Path
import math
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parent
lines=(root/'radye-gpr-otomatik.dxf').read_text(encoding='utf-8',errors='replace').splitlines()
pairs=list(zip((int(s.strip()) for s in lines[::2]),lines[1::2]))
start=next(i+2 for i in range(len(pairs)-1) if pairs[i]==(0,'SECTION') and pairs[i+1]==(2,'ENTITIES'))
entities=[];current=None
for code,value in pairs[start:]:
    if code==0:
        if current:entities.append(current)
        if value=='ENDSEC':break
        current=[value,[]]
    elif current:current[1].append((code,value))

aci={1:'#e84b44',2:'#ffe42e',3:'#42ef48',4:'#35e4f0',5:'#5487ff',6:'#d94be0',7:'#eeeeee',8:'#64737e',9:'#a0a7af'}
def render(name,bounds,width):
    x0,y0,x1,y1=bounds;scale=width/(x1-x0);height=round((y1-y0)*scale)
    im=Image.new('RGB',(width,height),'#20262d');d=ImageDraw.Draw(im)
    def pt(x,y):return((x-x0)*scale,(y1-y)*scale)
    for kind,attrs in entities:
        v=dict(attrs)
        if kind not in ['SOLID','LINE','LWPOLYLINE','TEXT']:continue
        color=aci.get(int(v.get(62,'7')),'#dddddd')
        if 420 in v:color='#'+format(int(v[420]),'06x')
        try:
            if kind=='SOLID':
                points=[pt(float(v[10+i]),float(v[20+i])) for i in [0,1,2,3]]
                d.polygon(points,fill=color)
            elif kind=='LINE':
                d.line([pt(float(v[10]),float(v[20])),pt(float(v[11]),float(v[21]))],fill=color,width=1)
            elif kind=='LWPOLYLINE':
                xs=[float(b) for a,b in attrs if a==10];ys=[float(b) for a,b in attrs if a==20]
                points=[pt(x,y) for x,y in zip(xs,ys)]
                if int(v.get(70,'0'))&1:points.append(points[0])
                if len(points)>1:d.line(points,fill=color,width=2 if 'DONATI' in v.get(8,'') else 1)
            else:
                x,y=float(v[10]),float(v[20]);h=max(5,round(float(v[40])*scale))
                if not(x0-200<x<x1+200 and y0-200<y<y1+200):continue
                text=v.get(1,'').replace('%%c','ø')
                font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',h)
                rot=float(v.get(50,'0'))
                if abs(rot)%180<.01:d.text(pt(x,y+float(v[40])),text,font=font,fill=color)
                else:
                    box=font.getbbox(text);tile=Image.new('RGBA',(box[2]+6,h*2),(0,0,0,0));ImageDraw.Draw(tile).text((0,0),text,font=font,fill=color)
                    tile=tile.rotate(rot,expand=True);pos=pt(x,y);im.paste(tile,(round(pos[0]-tile.width),round(pos[1]-tile.height)),tile)
        except (KeyError,ValueError):continue
    im.save(root/name)
render('radye-gpr-planlar.preview.png',(-300,-850,5300,2200),2800)
render('radye-gpr-isilar.preview.png',(-100,-3000,7550,-900),2400)
