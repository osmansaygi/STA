exec(open('tools/PerdeBoslukSmoke/dump.py',encoding='utf-8-sig').read().split('for name,dat,off')[0])
from PIL import Image,ImageDraw,ImageFont
import math
im=Image.new('RGB',(1800,1900),'#20272d');d=ImageDraw.Draw(im)
f=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',14)
colors={'DONATI (BEYKENT)':'#00e5f0','ETRIYE (BEYKENT)':'#ebaa10','KALIP BOSLUK (BEYKENT)':'#ff008b','PERDE (BEYKENT)':'#bd4dda','KOLON (BEYKENT)':'#54ed63'}
for row,(dat,off,title) in enumerate([(raw,0,'RAW'),(ed,shift,'EDITED')]):
 def point(x,y):return ((x+off-3960)*1.5, row*950+870-(y-1140)*1.5)
 d.text((20,row*950+10),title,font=f,fill='white')
 for e in dat:
  ps=e.get('pts')
  if ps:
   p=[point(x[0],x[1]) for x in ps]
   if e.get('closed'):p.append(p[0])
   d.line(p,fill=colors.get(e['layer'],'#9aa4ab'),width=2)
  if e.get('pos') and e.get('text'):
   p=point(e['pos'][0],e['pos'][1]);text=e['text'];rot=e.get('rot',0)
   if abs(rot)<.1:d.text((p[0],p[1]-14),text,font=f,fill='#40e353' if 'ø' in text else '#c4d1d8')
   else:
    tx=Image.new('RGBA',(int(d.textlength(text,font=f)+4),20));ImageDraw.Draw(tx).text((1,1),text,font=f,fill='#40e353');tx=tx.rotate(90,expand=True);im.paste(tx,(int(p[0]),int(p[1]-tx.height)),tx)
im.save('tools/PerdeBoslukSmoke/comparison.png')
