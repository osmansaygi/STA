import json
from pathlib import Path
root=Path('tools/PerdeBoslukSmoke')
a=json.loads((root/'DUZENLENMEMIS.json').read_text(encoding='utf-8-sig'))
b=json.loads((root/'ACTUAL.json').read_text(encoding='utf-8-sig'))
byid={e['id']:e for e in b}
unchanged=[]
for e in a:
 # Existing starter hook dimension is replaced along with the starter, and
 # the ciroz density note moves together with its diagram.
 if e['id'].upper() not in ['5DD','5CE'] and (e['layer'] not in ['DONATI (BEYKENT)','DONATI YAZISI (BEYKENT)','CIROZ (BEYKENT)'] or 0x5e0<=int(e['id'],16)<=0x5f5):
  assert e['id'] in byid,('removed unrelated',e['id'])
  other=byid[e['id']]
  assert e==other,('modified unrelated',e['id'],e,other)
  unchanged.append(e['id'])
print('PASS unchanged structural and neighboring entities',len(unchanged))
texts=[e.get('text','') for e in b]
for t in ['2x4ø12/20 L=135','2x5ø12/20 L=135','2x11ø12/20 L=135','2x5ø12/20 L=255','2x4ø12 L=469','10ø10/10 L=168','15ø8/15 L=469']:
 assert t in texts,('missing',t)
 print('PASS',t)
for e in b:
 if e.get('text') and ('ø' in e['text'] or e['text']=='216'):print(e['id'],e['text'])
