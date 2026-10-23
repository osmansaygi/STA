import json, math
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
root=Path('tools/PerdeBoslukSmoke')
raw=json.loads((root/'DUZENLENMEMIS.json').read_text(encoding='utf-8-sig'))
ed=json.loads((root/'DUZENLENMIS.json').read_text(encoding='utf-8-sig'))
shift=next(x for x in raw if x.get('text')=='PB-32')['pos'][0]-next(x for x in ed if x.get('text')=='PB-32')['pos'][0]
print('SHIFT',shift)
fig,axs=plt.subplots(2,1,figsize=(19,15))
colors={'DONATI (BEYKENT)':'#00b6cc','ETRIYE (BEYKENT)':'#ca8b00','KALIP BOSLUK (BEYKENT)':'red','PERDE (BEYKENT)':'#b04ac0','KOLON (BEYKENT)':'green'}
for ax,dat,title in zip(axs,[raw,ed],['RAW','EDITED']):
 for x in dat:
  off=shift if title=='EDITED' else 0
  pts=x.get('pts')
  if pts:
   pp=[(p[0]+off,p[1]) for p in pts]
   if x.get('closed'):pp.append(pp[0])
   ax.plot(*zip(*pp),color=colors.get(x['layer'],'#8b8b8b'),lw=.75)
  if x.get('pos') and x.get('text'):
   ax.text(x['pos'][0]+off,x['pos'][1],x['text'],rotation=x.get('rot',0)*180/math.pi,fontsize=5,color='#156929' if 'ø' in x['text'] else '#555555')
 ax.set_aspect('equal');ax.set_title(title);ax.grid(alpha=.2)
fig.tight_layout();fig.savefig(root/'comparison.png',dpi=150)
for name,dat,off in [('RAW',raw,0),('EDITED',ed,shift)]:
 print(name)
 for x in dat:
  if x.get('pts') and (x['type']=='Polyline' or x['layer']=='ETRIYE (BEYKENT)'):
   print(x['id'],x['layer'], 'CLOSED',x.get('closed'),[[round(p[0]+off-4000,2),round(p[1]-1300,2),round(p[2],3)] for p in x['pts']])
