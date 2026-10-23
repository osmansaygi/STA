import json
from pathlib import Path
root=Path('tools/PerdeBoslukSmoke')
raw=json.loads((root/'DUZENLENMEMIS.json').read_text(encoding='utf-8-sig'))
ed=json.loads((root/'DUZENLENMIS.json').read_text(encoding='utf-8-sig'))
shift=next(x for x in raw if x.get('text')=='PB-32')['pos'][0]-next(x for x in ed if x.get('text')=='PB-32')['pos'][0]
for name,dat,off in [('RAW',raw,0),('EDITED',ed,shift)]:
 print(name)
 for x in dat:
  if x.get('pts') and (x['type']=='Polyline' or x['layer']=='ETRIYE (BEYKENT)'):
   print(x['id'],x['layer'], 'CLOSED',x.get('closed'),[[round(p[0]+off-4000,2),round(p[1]-1300,2),round(p[2],3)] for p in x['pts']])

