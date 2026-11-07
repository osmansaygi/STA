import json
import hashlib
from pathlib import Path

root = Path(r'D:\projeler\00_sablon\WBLOCK')
local = Path(r'C:\Users\BEYKENT\AppData\Roaming\Autodesk\AutoCAD 2027\R26.0\enu')
work = Path(__file__).parent
client = '1348cafd-f573-4bdd-9bd3-d659c0352f41'
recent = json.loads((root/'RecentBlocks'/f'{client}.json').read_text(encoding='utf-8-sig'))['Blocks']
prefix = 'Local\\NULL\\eef19c54306daa69eda49c0272623bdb5e2b341f\\'
source112 = r'D:\projeler\projeler2026\ali_kuzucu\112_acil\CZM_RPR\112_AC_SU.dwg'
fallback = {
    '.597f792b55': prefix + str(root/'GES.dwg'),
    '.c282df20ee': prefix + str(root/'DILATASYON.dwg'),
    'DOLGU_YAZISI.ec8cf57721': 'Local\\NULL\\NULL\\' + str(root/'DOLGU.dwg'),
    'TEMEL_ALTI_YAZISI.4f6a91c292': 'Local\\NULL\\NULL\\' + source112,
    'TEMEL_HATIL_DET.4f6a91c292': 'Local\\NULL\\NULL\\' + source112,
}
blocks = {}
manifest = []
for dwg in sorted((root/'FavoriteBlocks').glob('*.dwg')):
    key = dwg.stem
    meta = json.loads((dwg.parent/'.blocksMetadata'/(dwg.name+'.json')).read_text(encoding='utf-8-sig'))
    old = recent.get(key)
    host = old['HostPath'] if old else fallback[key]
    modified = old['ModifiedTime'] if old else meta['Header']['Att']['ModifiedTime']
    blocks[key] = dict(FavoriteTime=int(dwg.stat().st_mtime), HostPath=host, ModifiedTime=modified)
    manifest.append(dict(file=dwg.name, sha256=hashlib.sha256(dwg.read_bytes()).hexdigest(), host=host, source='RecentBlocks' if old else 'reconstructed'))
assert len(blocks) == 15
out = work/'recovered-favorites.json'
out.write_text(json.dumps(dict(ClientId=client, Blocks=blocks), ensure_ascii=True, separators=(',', ':')), encoding='utf-8')
assert len(json.loads(out.read_text(encoding='utf-8-sig'))['Blocks']) == 15
(work/'manifest.json').write_text(json.dumps(manifest, ensure_ascii=True, indent=2), encoding='utf-8')
print('15 entries prepared; existing RecentBlocks paths: ', sum(x['source']=='RecentBlocks' for x in manifest))

