"""Read validated nodal As from a licensed running RADSON v14.1 process.

Inputs: RAD for geometry, SO1 for selection/peak validation. No GPR/PDF/DXF.
This reads live calculation results, not a decoded on-disk As table.
"""
import argparse
import csv
import hashlib
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0,str(Path(__file__).with_name('deps')))
import frida
import psutil

def inputs(project):
    blobs={ext:project.with_suffix(ext).read_bytes() for ext in ['.RAD','.SO1']}
    lines=blobs['.RAD'].decode('cp1254').splitlines()
    if lines[0]!='V14.01':raise ValueError('Unsupported RAD version')
    plates,count,elements=map(int,lines[1].split(',')[:3])
    offset=7+2*plates
    ids=[list(map(int,row.split(',')[1:5])) for row in lines[offset:offset+elements]]
    coordinates=[list(map(float,row.split(',')[1:4])) for row in lines[offset+elements:offset+elements+count]]
    connected={j for row in ids for j in row if j}
    header=struct.unpack_from('<10f',blobs['.SO1'])
    # Runtime record order: X alt, X ust, Y alt, Y ust.
    base=[header[3],header[2],header[5],header[4]]
    peaks=[header[7],header[6],header[9],header[8]]
    return blobs,coordinates,connected,base,peaks

def extract(script,project,output):
    blobs,coordinates,connected,base,peaks=inputs(project)
    pattern=struct.pack('<3f',*coordinates[0]).hex(' ')
    candidates=script.exports_sync.nodal(pattern,coordinates)
    if len(candidates)!=1:raise ValueError('Expected one coordinate-validated node table, found '+str(len(candidates)))
    candidate=candidates[0]
    (output if output.exists() else output.parent).mkdir(parents=True,exist_ok=True)
    output.mkdir(parents=True,exist_ok=True)
    (output/'intermediate-research.json').write_text(json.dumps(candidate.get('intermediate',{})),encoding='utf-8')
    rows=candidate['rows']
    observed=[max(rows[i-1][3+k] for i in connected) for k in range(4)]
    if any(abs(a-b)>1e-5 for a,b in zip(observed,peaks)):raise ValueError('Runtime As peaks differ from SO1')
    if any(project.with_suffix(ext).read_bytes()!=blob for ext,blob in blobs.items()):raise ValueError('Input changed during extraction')
    output.mkdir(parents=True,exist_ok=True)
    with (output/'nodal-As.csv').open('w',newline='',encoding='utf-8') as file:
        writer=csv.writer(file)
        writer.writerow(['node','connected','X_m','Y_m','Z_m','As_X_alt','As_X_ust','As_Y_alt','As_Y_ust','extra_X_alt','extra_X_ust','extra_Y_alt','extra_Y_ust'])
        for i,row in enumerate(rows):
            writer.writerow([i+1,int(i+1 in connected),*coordinates[i],*row[3:7],*[max(row[3+k]-base[k],0) for k in range(4)]])
    result=dict(status='LIVE_ENGINE_RESULTS_VALIDATED',nodes=len(rows),connected=len(connected),values=len(rows)*4,
                base_As=base,peak_As=observed,coordinate_max_error_m=candidate['maxError'],
                source_hashes={ext:hashlib.sha256(blob).hexdigest() for ext,blob in blobs.items()},
                source='RADSON process; geometry RAD; peak/selection SO1; no GPR/PDF/DXF read by this extractor',
                limitation='Requires licensed STA running with matching project. Not yet an on-disk As reader or AutoCAD command.')
    (output/'validation.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project',type=Path)
    parser.add_argument('--pid',type=int)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    if not args.pid:
        processes=[p for p in psutil.process_iter(['name']) if p.info['name'].upper()=='RADSON.EXE']
        if len(processes)!=1:raise ValueError('Specify a unique RADSON pid')
        args.pid=processes[0].pid
    session=frida.attach(args.pid)
    try:
        script=session.create_script(Path(__file__).with_name('nodal_reader.js').read_text())
        script.load()
        print(json.dumps(extract(script,args.project,args.output)))
    finally:session.detach()
