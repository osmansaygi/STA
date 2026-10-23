import json,math
from pathlib import Path
base=Path('tools')
def tips(file):
 r=json.loads(file.read_text(encoding='utf-8')); out=[]
 for g in r['geometry']:
  if g['layer']!='ETIKET CIZGISI (BEYKENT)':continue
  p=g['points']
  if g['type']=='LWPOLYLINE':out += [p[0],p[-1]]
  elif len(p)==2 and abs(math.dist(*p)-10)<.01 or (g['layer']=='ETIKET CIZGISI (BEYKENT)' and len(p)==2 and abs(math.dist(*p)-15)<.01):out.append(p[0])
 x=min(p[0] for p in out);y=min(p[1] for p in out)
 return set((round(a-x,3),round(b-y,3)) for a,b in out)
a=tips(base/'cukur-reference/inspection.json');b=tips(base/'CukurSmoke/inspection.json')
print('Reference tips:',len(a),'Generated tips:',len(b))
print('Missing:', sorted(a-b));print('Additional:',sorted(b-a))
assert a==b,'Leader endpoints differ from reference'
print('PASS: all reference leader endpoints match generated rebar groups after translation.')
