"""Original code-native vector icon concepts; no image generation service calls."""
import json, html
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT=Path('docs/icon-alternatives/minimal');OUT.mkdir(parents=True,exist_ok=True)
NAMES=['Oto Ölçü','Plan Ölçü','Merdiven','Döşeme Donatı','Sehpa Donatısı','Hasır Donatı','Temel Detayı','Kuranglez','Perde Boşluk','Donatı Boyu','Donatı Metraj','Kot Değiştir']
STYLES=[
 dict(name='01 / İZ',sub='Tek çizgi, tek vurgu. En yalın teknik anlatım.',bg='#171E27',fg='#F0F1EC',muted='#A9B3BF',accent='#FF9F43',surface='#253240',secondary='#75B9FA',mode='line'),
 dict(name='02 / KESİT',sub='Dolu geometriler ve boşlukla kurulan güçlü siluet.',bg='#F3F1EB',fg='#202C3A',muted='#647284',accent='#F16548',surface='#DADFD9',secondary='#225BC5',mode='solid'),
 dict(name='03 / DENGE',sub='Yumuşak köşeler, iki ton, sakin ve anlaşılır.',bg='#EDF4F0',fg='#163D3B',muted='#617A72',accent='#E59237',surface='#C4DDD2',secondary='#318477',mode='soft'),
 dict(name='04 / AKS',sub='Ölçü disiplininden doğan hassas geometrik işaretler.',bg='#101F3C',fg='#E4EDFC',muted='#A4B4D1',accent='#60D3E0',surface='#233959',secondary='#E9A36C',mode='technical'),
 dict(name='05 / NOKTA',sub='Kompakt renk alanları ve seçici, güçlü semboller.',bg='#25242C',fg='#F4EDE5',muted='#B8AFBD',accent='#ECAF90',surface='#3E394C',secondary='#B7A5ED',mode='tile'),
]
S=2
class Icon:
 def __init__(self,canvas,x,y,size,style):
  self.d=ImageDraw.Draw(canvas);self.x=x;self.y=y;self.z=size/36;self.s=style;self.svg=[]
 def pts(self,ps):return [((self.x+x*self.z)*S,(self.y+y*self.z)*S) for x,y in ps]
 def line(self,ps,color=None,w=2.2):
  c=color or self.s['fg'];p=self.pts(ps);width=max(1,round(w*self.z*S))
  self.d.line(p,fill=c,width=width,joint='curve')
  for x,y in [p[0],p[-1]]:self.d.ellipse((x-width/2,y-width/2,x+width/2,y+width/2),fill=c)
  self.svg.append(f'<polyline points="{" ".join(f"{x},{y}" for x,y in ps)}" fill="none" stroke="{c}" stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round"/>')
 def poly(self,ps,color=None):
  c=color or self.s['fg'];self.d.polygon(self.pts(ps),fill=c)
  self.svg.append(f'<polygon points="{" ".join(f"{x},{y}" for x,y in ps)}" fill="{c}"/>')
 def box(self,x,y,w,h,fill=None,outline=None,r=0):
  p=self.pts([(x,y),(x+w,y+h)]);bbox=(*p[0],*p[1]);fill=fill or self.s['surface']
  self.d.rounded_rectangle(bbox,radius=r*self.z*S,fill=fill,outline=outline,width=max(1,round(self.z*S)))
  self.svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}"'+(f' stroke="{outline}"' if outline else '')+'/>')
 def arrow(self,ps,color=None,w=2):
  self.line(ps,color,w);x,y=ps[-1];a,b=ps[-2];dx=x-a;dy=y-b;m=(dx*dx+dy*dy)**.5;ux=dx/m;uy=dy/m
  self.poly([(x,y),(x-ux*4-uy*2,y-uy*4+ux*2),(x-ux*4+uy*2,y-uy*4-ux*2)],color)

def draw_icon(k,style,canvas,x,y,size):
 p=Icon(canvas,x,y,size,style);m=style['mode'];fg=style['fg'];a=style['accent'];b=style['secondary'];s=style['surface'];bg=style['bg']
 if m=='tile':p.box(0,0,36,36,s,r=8)
 if m=='technical':
  p.line([(1,6),(1,1),(6,1)],s,.6);p.line([(30,35),(35,35),(35,30)],s,.6)
 fill=fg if m=='solid' else s
 if k==0:
  p.line([(5,11),(5,25)],fg);p.line([(31,11),(31,25)],fg);p.arrow([(7,18),(30,18)],a);p.arrow([(29,18),(6,18)],a)
 elif k==1:
  p.poly([(8,9),(29,9),(29,28),(23,28),(23,24),(25,24),(25,13),(12,13),(12,28),(8,28)],fill)
  if m!='solid':p.line([(8,28),(8,9),(29,9),(29,28),(23,28)],fg,1.5)
  p.line([(8,5),(29,5)],a,1.5);p.line([(8,3),(8,7)],a,1.5);p.line([(29,3),(29,7)],a,1.5);p.line([(4,9),(4,28)],b,1.5)
 elif k==2:
  path=[(5,29),(5,24),(11,24),(11,18),(17,18),(17,12),(23,12),(23,6),(30,6)]
  if m in ['solid','soft','tile']:p.poly(path+[(30,30),(5,30)],fill)
  p.line(path,fg,2.5);p.line([(6,18),(25,2)],a,1.8)
 elif k==3:
  p.box(4,7,28,23,fill,r=2 if m=='soft' else 0)
  for xx in [10,18,26]:p.line([(xx,5),(xx,31)],b,2)
  for yy in [13,23]:p.line([(5,yy),(31,yy)],a,2)
 elif k==4:
  p.line([(3,29),(9,29),(12,9),(24,9),(27,29),(33,29)],fg,3)
  p.line([(12,9),(24,9)],a,3.6);p.line([(11,19),(25,19)],b,1.6)
 elif k==5:
  for xx in [7,14,21,28]:p.line([(xx,5),(xx,31)],fg,1.8)
  for yy in [7,14,21,28]:p.line([(5,yy),(31,yy)],a if yy==21 else b,1.8)
 elif k==6:
  p.poly([(13,4),(23,4),(23,23),(31,23),(31,31),(5,31),(5,23),(13,23)],fill)
  if m!='solid':p.line([(13,5),(13,23),(5,23),(5,31),(31,31),(31,23),(23,23),(23,5)],fg,1.5)
  p.line([(16,7),(16,27),(28,27)],b,1.8);p.line([(20,7),(20,27),(8,27)],a,1.8)
 elif k==7:
  p.poly([(5,5),(10,5),(10,26),(27,26),(27,14),(32,14),(32,31),(5,31)],fill)
  p.line([(7,6),(7,29),(30,29),(30,15)],fg,2)
  p.line([(16,5),(16,18),(24,18)],a,2);p.line([(12,11),(16,11)],b,1.5)
 elif k==8:
  p.box(4,5,28,27,fill,r=2 if m=='soft' else 0);p.box(13,13,10,14,bg)
  if m!='solid':p.line([(4,31),(4,5),(32,5),(32,31),(4,31)],fg,1.2)
  p.line([(10,29),(10,10),(26,10),(26,29)],b,1.8);p.line([(8,9),(28,9)],a,2.4)
 elif k==9:
  p.line([(7,15),(7,9),(28,9),(28,15)],b,3)
  p.line([(5,23),(31,23)],a,1.6);p.line([(5,20),(5,26)],fg,1.6);p.line([(31,20),(31,26)],fg,1.6)
  p.arrow([(10,23),(5,23)],a,1.2);p.arrow([(26,23),(31,23)],a,1.2)
 elif k==10:
  p.box(7,3,23,30,fill,r=2 if m in ['soft','tile'] else 0)
  if m in ['line','technical']:p.line([(7,4),(7,32),(30,32),(30,4),(7,4)],fg,1.3)
  for yy in [11,17,23,28]:p.line([(11,yy),(26,yy)],fg if m!='solid' else bg,1.4)
  p.line([(20,11),(20,28)],b,1.6);p.line([(11,7),(23,7),(23,10)],a,2)
 elif k==11:
  p.poly([(5,8),(23,8),(14,18)],a);p.line([(4,20),(24,20)],fg,2)
  p.arrow([(30,29),(30,5)],b,1.7);p.arrow([(30,5),(30,29)],b,1.7)
 return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 36 36">'+''.join(p.svg)+'</svg>'

def font(n,b=False):return ImageFont.truetype('C:/Windows/Fonts/'+('arialbd.ttf' if b else 'arial.ttf'),n*S)
for idx,st in enumerate(STYLES):
 im=Image.new('RGB',(1440*S,1040*S),st['bg']);d=ImageDraw.Draw(im)
 def txt(x,y,t,n=17,c=None,b=False):d.text((x*S,y*S),t,font=font(n,b),fill=c or st['fg'])
 txt(48,32,st['name'],38,b=True);txt(48,91,st['sub'],19,st['muted'])
 txt(48,135,'YARDIMCI  /  MİNİMALİST SİMGE AİLESİ',12,st['muted'])
 boarddir=OUT/f'{idx+1:02d}';boarddir.mkdir(exist_ok=True)
 for k,name in enumerate(NAMES):
  col=k%4;row=k//4;x=55+col*251;y=220+row*230
  svg=draw_icon(k,st,im,x+54,y,104)
  (boarddir/f'{k+1:02d}.svg').write_text(svg,encoding='utf-8')
  txt(x+125-d.textlength(name,font=font(17))/S/2,y+137,name,17)
 # Actual-size companion panel, not a claim of live AutoCAD screenshot.
 xx=1105;d.rounded_rectangle((xx*S,185*S,1390*S,960*S),radius=12*S,fill=st['surface'])
 txt(xx+22,207,'YARDIMCI',20,b=True);txt(xx+22,242,'32 px / kullanım önizlemesi',12,st['muted'])
 for k,name in enumerate(NAMES):
  yy=293+k*51;draw_icon(k,st,im,xx+20,yy,32);txt(xx+65,yy+8,name,14)
 txt(48,995,'Özgün vektörel taslak · Görsel üretim servisi kullanılmadı · Henüz uygulanmadı',13,st['muted'])
 im.resize((1440,1040),Image.Resampling.LANCZOS).save(OUT/f'{idx+1:02d}-minimal.png')
(OUT/'README.md').write_text('# Minimalist alternatives\nFive original code-native concept boards and 12 SVG assets per direction. Generated locally; no image generation API calls.\n',encoding='utf-8')
print('Saved five minimalist concept boards and 60 SVG assets to',OUT)
