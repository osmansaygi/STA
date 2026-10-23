"""Create a CAD comparison artifact from existing RAD/GPR/SO1 only.

DXF is an output for review, never a required user export or an input.
No reinforcement bars are designed by this research exporter.
"""
import argparse
from pathlib import Path

import numpy as np

from read_gpr_heatmap import CHANNELS, clip, read, triangles


def export(data, output):
    pairs=[]
    handle=[0x100]
    def put(*items):
        if len(items)>1 and items[0]==0 and items[1] in ['LINE','TEXT','SOLID']:
            handle[0]+=1
            items=items[:2]+(5,format(handle[0],'X'))+items[2:]
        clean=[]
        # Minimal R12 entities can be read without synthetic owner/block tables.
        # Use indexed colors; the PNG preserves the exact comparison palette.
        palette={int(s,16):aci for s,aci in zip(['c8e6f3','7fc8de','47b9a5','a4d858','f2ce49','ed933b','d64936','eeeeee'],[151,141,91,61,41,31,11,254])}
        for code,value in zip(items[::2],items[1::2]):
            if code==100:continue
            if code==420:code,value=62,palette[value]
            clean.extend([code,value])
        items=clean
        pairs.extend(str(x) for x in items)
    def line(a,b,color=8,layer='RADYE_AG'):
        put(0,'LINE',100,'AcDbEntity',8,layer,62,color,100,'AcDbLine',10,a[0],20,a[1],30,0,11,b[0],21,b[1],31,0)
    def text(p,value,height=18):
        put(0,'TEXT',100,'AcDbEntity',8,'RADYE_BILGI',62,7,100,'AcDbText',10,p[0],20,p[1],30,0,40,height,1,value)
    def face(points,color,layer):
        for j in range(1,len(points)-1):
            a,b,c=points[0],points[j],points[j+1]
            put(0,'SOLID',100,'AcDbEntity',8,layer,420,color,100,'AcDbTrace',10,a[0],20,a[1],30,0,
                11,b[0],21,b[1],31,0,12,c[0],22,c[1],32,0,13,c[0],23,c[1],33,0)
    put(0,'SECTION',2,'HEADER',9,'$ACADVER',1,'AC1009',9,'$HANDSEED',5,'10000000',0,'ENDSEC',0,'SECTION',2,'ENTITIES')
    xy=data['nodes'][:,1:3];lo=xy.min(0);hi=xy.max(0)
    width=(hi[0]-lo[0])*100; height=(hi[1]-lo[1])*100
    colors=[int(s,16) for s in ['c8e6f3','7fc8de','47b9a5','a4d858','f2ce49','ed933b','d64936']]
    for panel,ch in enumerate([1,2,3]):
        origin=panel*(width+450)
        transform=lambda p:(origin+(p[0]-lo[0])*100,(hi[1]-p[1])*100)
        peak=max(0,float(data['peak'][ch]-data['base'][ch]))
        for tri in triangles(data,ch):
            face([transform(p) for p in tri],int('eeeeee',16),'RADYE_ZEMIN')
            for level,color in zip(np.linspace(0,peak,8)[:-1],colors):
                poly=clip(tri,max(1e-9,float(level)))
                if len(poly)>2:
                    face([transform(p) for p in poly],color,'RADYE_AS_'+CHANNELS[ch].upper())
        for ids in data['elements']:
            ids=ids[ids>0]-1
            pts=[transform(data['nodes'][i,1:3]) for i in ids]
            for a,b in zip(pts,pts[1:]+pts[:1]):line(a,b)
        text((origin,height+110),CHANNELS[ch].upper()+' ILAVE AS - GPR/RAD/SO1',28)
        text((origin,-70),f'Surekli As={data["base"][ch]:.4f} cm2/m; ilave tepe={peak:.2f} cm2/m')
        text((origin,-110),'GPR hassasiyeti 0.01 cm2/m. Kontur enterpolasyonu karsilastirma icindir.',15)
        for id in np.flatnonzero(data['connected'] & (data['As'][:,ch] > data['base'][ch])):
            p=transform(data['nodes'][id,1:3])
            text((p[0]+5,p[1]+5),f'{id+1}: {data["As"][id,ch]-data["base"][ch]:.3f}',10)
    put(0,'ENDSEC',0,'EOF')
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text('\n'.join(pairs)+'\n',encoding='ascii')


if __name__=='__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('project',type=Path)
    ap.add_argument('output',type=Path)
    a=ap.parse_args()
    export(read(a.project),a.output)
