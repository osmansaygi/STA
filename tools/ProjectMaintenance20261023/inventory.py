import os, json
from pathlib import Path
from collections import defaultdict
root=Path(__file__).resolve().parents[2]
totals=defaultdict(lambda:[0,0])
files=[]; links=[]
for current, dirs, names in os.walk(root,followlinks=False):
    dirs[:]=[d for d in dirs if d!='.git' and not (os.stat(Path(current)/d,follow_symlinks=False).st_file_attributes & 0x400)]
    for name in names:
        p=Path(current)/name
        try: s=p.stat(follow_symlinks=False)
        except OSError: continue
        rel=p.relative_to(root).as_posix()
        if s.st_file_attributes & 0x400:
            links.append(rel); continue
        bits=rel.split('/')
        key=bits[0] if len(bits)==1 or bits[0]!='tools' else '/'.join(bits[:2])
        totals[key][0]+=1;totals[key][1]+=s.st_size
        files.append({'path':rel,'bytes':s.st_size,'mtime':s.st_mtime})
out={'root':str(root),'totals':dict(totals),'files':files,'reparse_files_skipped':links}
(root/'tools/ProjectMaintenance20261023/inventory.json').write_text(json.dumps(out,ensure_ascii=False),encoding='utf-8')
for key,(count,size) in sorted(totals.items(),key=lambda x:x[1][1],reverse=True)[:22]:
    print(f'{size/1024**2:9.1f} MiB {count:6} files {key}')
print('Files',len(files),'Total GiB',round(sum(f['bytes'] for f in files)/1024**3,2),'Reparse skipped',len(links))
