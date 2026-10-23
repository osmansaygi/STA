"""Conservative project cleanup: reviewed plan -> verified ZIP -> unchanged files only.
Never follows directory links, removes tracked files, or recursively deletes folders.
"""
import argparse, hashlib, json, os, re, subprocess, time, zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
WORK=Path(__file__).resolve().parent
PLAN=WORK/'cleanup-plan.json'
def checked(rel):
    p=ROOT/rel
    resolved=p.resolve(strict=True)
    resolved.relative_to(ROOT.resolve())
    if p.is_symlink() or p.stat(follow_symlinks=False).st_file_attributes & 0x400:
        raise RuntimeError('Refusing reparse point: '+rel)
    return p
def digest(stream):
    h=hashlib.sha256()
    for block in iter(lambda:stream.read(1024*1024),b''):h.update(block)
    return h.hexdigest()
def make_plan():
    inv=json.loads((WORK/'inventory.json').read_text(encoding='utf-8'))
    tracked=set(subprocess.check_output(['git','ls-files','-z'],cwd=ROOT).decode('utf-8').split('\0'))
    cutoff=time.time()-20*60
    code='\n'.join((ROOT/f['path']).read_text(encoding='utf-8',errors='ignore').lower()
        for f in inv['files'] if Path(f['path']).suffix.lower() in {'.cs','.ps1','.py','.csproj','.scr','.targets'}
        and '/obj/' not in f['path'] and '/bin/' not in f['path'] and f['bytes']<2*1024**2)
    candidates={}
    def add(f,category):
        if f['path'] in tracked or f['mtime']>=cutoff:return
        p=checked(f['path'])
        s=p.stat()
        if s.st_size!=f['bytes'] or s.st_mtime!=f['mtime']:return
        candidates[f['path']]={**f,'category':category}
    for f in inv['files']:
        p=Path(f['path'])
        if len(p.parts)==1 and p.suffix.lower()=='.log':add(f,'root-autocad-logs')
    # Only reproducible RunAll cases with COMPLETE evidence; keep two newest per case.
    rx=re.compile(r'-(ck-plain|ck-antet|ck-repeat|ms|akdag|nodata)$')
    outputs={}
    for f in inv['files']:
        p=Path(f['path'])
        if p.parent.as_posix()!='tools/SurekliTemelViewCheck' or p.suffix.lower() not in {'.dwg','.dxf'}:continue
        m=rx.search(p.stem)
        if not m or p.name.lower() in code:continue
        result=ROOT/p.with_suffix('.txt')
        if not result.is_file() or b'COMPLETE' not in result.read_bytes():continue
        key=(m.group(1),p.suffix.lower())
        outputs.setdefault(key,[]).append(f)
    archived_stems=set()
    for group in outputs.values():
        for f in sorted(group,key=lambda x:x['mtime'],reverse=True)[2:]:
            add(f,'reproducible-foundation-outputs')
            if f['path'] in candidates:archived_stems.add(Path(f['path']).stem)
    for f in inv['files']:
        p=Path(f['path'])
        if p.parent.as_posix()=='tools/SurekliTemelViewCheck':
            if p.suffix=='.log' or any(p.name.startswith(stem+'.') for stem in archived_stems):
                if p.suffix in {'.txt','.log','.png'} and p.name.lower() not in code:add(f,'old-foundation-diagnostics')
        if f['path'].startswith('tools/netload-yedek/') and p.suffix.lower() in {'.dll','.pdb','.json'}:
            if p.name.lower() not in code:add(f,'old-version-backups')
    # Keep two most recent complete rollback packages and the live GUNCEL_2027 package.
    archive=ROOT/'NETLOAD/arsiv'
    dirs=sorted([p for p in archive.glob('GUNCEL_2027_*') if p.is_dir()],key=lambda p:p.name,reverse=True)
    old_dirs=[p.relative_to(ROOT).as_posix()+'/' for p in dirs[2:]]
    for f in inv['files']:
        if any(f['path'].startswith(prefix) for prefix in old_dirs):add(f,'old-common-packages')
    plan={'root':str(ROOT),'created':time.time(),'policy':'Only listed untracked stale artifacts; source and input fixtures protected; archive before unlink',
          'files':sorted(candidates.values(),key=lambda f:f['path'])}
    PLAN.write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
    for category in sorted({f['category'] for f in plan['files']}):
        fs=[f for f in plan['files'] if f['category']==category]
        print(category,len(fs),'files',round(sum(f['bytes'] for f in fs)/1024**2,1),'MiB')
    print('PLAN',len(plan['files']),'files',round(sum(f['bytes'] for f in plan['files'])/1024**3,3),'GiB',flush=True)
def apply_plan():
    plan=json.loads(PLAN.read_text(encoding='utf-8'))
    if plan['root']!=str(ROOT):raise RuntimeError('Plan root mismatch')
    protected=('NETLOAD/GUNCEL_2027/','ST4_Yardimci/','ST4_Plan_ID_Ciz/','Ortak/','.git/','dosyalar/','PDF/')
    for f in plan['files']:
        if f['path'].startswith(protected):raise RuntimeError('Protected target')
        p=checked(f['path']);s=p.stat()
        if s.st_size!=f['bytes'] or s.st_mtime!=f['mtime']:raise RuntimeError('Changed after plan: '+f['path'])
    folder=WORK/'arsiv';folder.mkdir(exist_ok=True)
    archive=folder/(time.strftime('temizlik-%Y%m%d-%H%M%S')+'.zip')
    hashes={}
    with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=1,allowZip64=True) as z:
        for i,f in enumerate(plan['files']):
            p=checked(f['path'])
            h=hashlib.sha256()
            with p.open('rb') as source,z.open(f['path'],'w',force_zip64=True) as dest:
                for block in iter(lambda:source.read(1024*1024),b''):h.update(block);dest.write(block)
            hashes[f['path']]=h.hexdigest()
            if i%200==0:print('Archiving',i,'/',len(plan['files']),flush=True)
        z.writestr('_cleanup-manifest.json',json.dumps({'plan':plan,'sha256':hashes},ensure_ascii=False))
    # Verify every archived byte before deleting any original.
    with zipfile.ZipFile(archive) as z:
        for i,f in enumerate(plan['files']):
            with z.open(f['path']) as source:
                if digest(source)!=hashes[f['path']]:raise RuntimeError('ZIP verification failed')
            if i%400==0:print('Verified',i,'/',len(plan['files']),flush=True)
    deleted=[];skipped=[]
    for f in plan['files']:
        p=checked(f['path']);s=p.stat()
        try:
            if s.st_size!=f['bytes'] or s.st_mtime!=f['mtime']:skipped.append(f['path']);continue
            with p.open('rb') as source:
                if digest(source)!=hashes[f['path']]:skipped.append(f['path']);continue
            p.unlink();deleted.append(f)
        except OSError as ex:skipped.append({'path':f['path'],'error':str(ex)})
    # Only empty artifact folders, never recursive deletion. Validate each resolved path.
    empty_removed=[]
    for parent in sorted({(ROOT/f['path']).parent for f in deleted},key=lambda p:len(p.parts),reverse=True):
        if parent==ROOT:continue
        try:
            parent.resolve().relative_to(ROOT.resolve())
            if parent.is_dir() and not any(parent.iterdir()):parent.rmdir();empty_removed.append(str(parent.relative_to(ROOT)))
        except OSError:pass
    result={'archive':str(archive.relative_to(ROOT)),'archive_bytes':archive.stat().st_size,
            'files_deleted':len(deleted),'logical_bytes_removed':sum(f['bytes'] for f in deleted),
            'skipped':skipped,'empty_directories_removed':empty_removed,'verified_sha256':hashes}
    (WORK/'cleanup-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print('COMPLETE',result['files_deleted'],'files archived and removed;',round(result['logical_bytes_removed']/1024**3,3),'GiB originals;',round(result['archive_bytes']/1024**2,1),'MiB verified ZIP;',len(skipped),'skipped',flush=True)
if __name__=='__main__':
    args=argparse.ArgumentParser();args.add_argument('--apply',action='store_true')
    if args.parse_args().apply:apply_plan()
    else:make_plan()
