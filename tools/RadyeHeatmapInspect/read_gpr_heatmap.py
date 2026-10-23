"""Recover recorded STA nodal As from existing GPR, RAD and SO1 files.

No PDF, DXF, running STA instance or new engineering calculation is required.
GPR precision is 0.01 cm2/m; rendered interpolation is explicitly a comparison.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


CHANNELS = ['X_ust', 'X_alt', 'Y_ust', 'Y_alt']


def read(project):
    paths = [project.with_suffix(ext) for ext in ['.RAD', '.SO1', '.GPR']]
    blobs = [p.read_bytes() for p in paths]
    lines = blobs[0].decode('cp1254').splitlines()
    if lines[0] != 'V14.01':
        raise ValueError('Unsupported RAD version')
    plates, count, ec = map(int, lines[1].split(',')[:3])
    if Path(lines[6]).stem.casefold() != project.name.casefold():
        raise ValueError('RAD project identity mismatch')
    start = 7 + 2 * plates
    erows = np.array([list(map(float, s.split(','))) for s in lines[start:start+ec]])
    elements = erows[:, 1:5].astype(int)
    nodes = np.array([list(map(float, s.split(','))) for s in lines[start+ec:start+ec+count]])
    if not np.array_equal(erows[:, 1:5], elements) or np.any(elements < 0) or np.any(elements > count):
        raise ValueError('Invalid element IDs')
    if not np.isin((elements > 0).sum(axis=1), [3, 4]).all():
        raise ValueError('Unsupported element connectivity')
    body = ec * 2772
    if len(blobs[1]) != body + 350 or any(blobs[1][body:]):
        raise ValueError('Unsupported SO1 layout')
    raw = np.frombuffer(blobs[1][:body], dtype='<f4').reshape(ec, 21, 33)
    if not np.isfinite(raw).all() or not np.all(raw[:, 0, 2:6] == raw[0, 0, 2:6]):
        raise ValueError('Invalid SO1 values')
    if abs(float(raw[0, 0, 0])) > 1e-6:
        raise ValueError('Nonzero reinforcement axis angle is not supported')
    base, peak = raw[0, 0, 2:6].astype(float), raw[0, 0, 6:10].astype(float)
    if np.any(base <= 0) or np.any(peak < 0):
        raise ValueError('Invalid SO1 As summary')
    text = blobs[2].decode('cp1254', errors='replace')
    if f'({project.name.upper()}.ST4)' not in text.upper():
        raise ValueError('GPR project identity mismatch')
    in_table = False
    rows, locations = [], []
    for line_no, line in enumerate(text.splitlines(), 1):
        if all(s in line for s in ['Nokta', 'Mxalt', 'Mxust', 'Myalt', 'Myust', 'Asax', 'Asux', 'Asay', 'Asuy']):
            in_table = True
            continue
        if not in_table or not line.startswith('\xa1'):
            continue
        cells = line.split('\xa1')[1:-1]
        if len(cells) != 14:
            continue
        try:
            values = list(map(float, cells))
        except ValueError:
            continue
        if values[0] != len(rows) + 1:
            raise ValueError(f'Nonsequential/duplicate GPR node at line {line_no}')
        rows.append(values)
        locations.append(line_no)
        if len(rows) == count:
            break
    if len(rows) != count:
        raise ValueError(f'Incomplete GPR nodal As table: {len(rows)}/{count}')
    table = np.array(rows)
    if not np.isfinite(table).all() or np.any(table[:, 10:14] < 0):
        raise ValueError('Invalid GPR nodal results')
    error = np.abs(table[:, 3:6] - nodes[:, 1:4])
    if error.max() > .000511:
        raise ValueError('GPR coordinates do not match current RAD (0.001 m precision)')
    # GPR: X alt, X ust, Y alt, Y ust. SO1: X ust, X alt, Y ust, Y alt.
    as_values = table[:, [11, 10, 13, 12]]
    connected = np.zeros(count, dtype=bool)
    connected[elements[elements > 0]-1] = True
    if np.any(table[connected, 1:3] != 40):
        raise ValueError('Current comparison is validated only for 40 cm raft nodes')
    observed_peak = as_values[connected].max(axis=0)
    if np.any(np.abs(observed_peak - peak) > .00511):
        raise ValueError('GPR As maxima do not match current SO1')
    if any(p.read_bytes() != b for p, b in zip(paths, blobs)):
        raise ValueError('Source changed during reading')
    return dict(project=str(project), nodes=nodes, elements=elements, table=table,
                As=as_values, base=base, peak=peak, connected=connected,
                source_lines=locations,
                source_sha256={str(p): hashlib.sha256(b).hexdigest() for p,b in zip(paths,blobs)},
                coordinate_max_error_m=float(error.max()))


def clip(poly, level):
    result = []
    for a, b in zip(poly, poly[1:]+poly[:1]):
        ia, ib = a[2] >= level, b[2] >= level
        if ia:
            result.append(a)
        if ia != ib:
            t = (level-a[2])/(b[2]-a[2])
            result.append((a[0]+t*(b[0]-a[0]), a[1]+t*(b[1]-a[1]), level))
    return result


def triangles(data, channel):
    # Four subtriangles avoid choosing an arbitrary diagonal in quadrilaterals.
    # This comparison interpolation is not claimed to duplicate STA contours.
    for ids in data['elements']:
        ids = ids[ids > 0]-1
        p = [(float(data['nodes'][i,1]), float(data['nodes'][i,2]),
              max(0.0,float(data['As'][i,channel]-data['base'][channel]))) for i in ids]
        if len(p) == 3:
            yield p
        else:
            c = tuple(np.mean(p,axis=0))
            for a,b in zip(p,p[1:]+p[:1]):
                yield [a,b,c]


def draw(data, out):
    font_path = 'C:/Windows/Fonts/arial.ttf'
    font = ImageFont.truetype(font_path, 23)
    small = ImageFont.truetype(font_path, 17)
    canvas = Image.new('RGB',(1800,650),'#f4f2eb')
    d = ImageDraw.Draw(canvas)
    colors = ['#c8e6f3','#7fc8de','#47b9a5','#a4d858','#f2ce49','#ed933b','#d64936']
    for panel,ch in enumerate([1,2,3]):
        x0=panel*600+30
        scale=24.0
        transform=lambda p:(x0+(p[0]+.4)*scale,85+(p[1]+.4)*scale)
        d.text((x0,15),CHANNELS[ch].replace('_',' ').upper()+' — ilave As',font=font,fill='#17344a')
        upper=max(0,float(data['peak'][ch]-data['base'][ch]))
        levels=np.linspace(0,upper,len(colors)+1)[:-1]
        positive=[]
        for tri in triangles(data,ch):
            d.polygon([transform(p) for p in tri],fill='#dce0dc')
            for level,color in zip(levels,colors):
                poly=clip(tri,max(1e-9,float(level)))
                if len(poly)>2:
                    d.polygon([transform(p) for p in poly],fill=color)
            poly=clip(tri,0)
            if len(poly)>2:positive.extend(poly)
        for ids in data['elements']:
            ids=ids[ids>0]-1
            pts=[transform(data['nodes'][i,1:3]) for i in ids]
            d.line(pts+[pts[0]],fill='#8e9b9e',width=1)
        for i in [int(np.argmax(data['As'][:,ch]))]:
            pos=transform(data['nodes'][i,1:3]);d.ellipse((pos[0]-3,pos[1]-3,pos[0]+3,pos[1]+3),fill='#202d34')
            d.text((pos[0]+4,pos[1]),str(i+1),font=small,fill='#202d34')
        for j,color in enumerate(colors):
            d.rectangle((x0+j*74,530,x0+j*74+74,550),fill=color)
            d.text((x0+j*74,555),f'{levels[j]:.2f}',font=small,fill='#17344a')
        d.text((x0+480,575),f'Tepe: {upper:.2f}',font=small,fill='#17344a')
        d.text((x0,590),f'Sürekli As: {data["base"][ch]:.4f} cm²/m',font=small,fill='#17344a')
        d.text((x0,614),'Kaynak: GPR + RAD + SO1 • GPR hassasiyeti: 0.01',font=small,fill='#17344a')
    canvas.save(out/'gpr-ilave-isi-haritasi.png')


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('project',type=Path)
    ap.add_argument('--output',type=Path,required=True)
    args=ap.parse_args()
    data=read(args.project)
    out=args.output;out.mkdir(parents=True,exist_ok=True)
    ref=json.loads(Path(__file__).with_name('ui-reference.json').read_text(encoding='utf-8'))
    checks=[]
    for id, values in ref['nodes'].items():
        a=data['As'][int(id)-1]
        actual=[max(a[:2]),max(a[2:])]
        checks.append(dict(node=int(id),UI_As=values[2:],GPR_As=actual,
                           match=bool(np.allclose(actual,values[2:],atol=.0001,rtol=0))))
    if not all(c['match'] for c in checks):
        raise ValueError('GPR does not reproduce STA numeric As reference')
    channels={}
    for j,label in enumerate(CHANNELS):
        exceeding=np.flatnonzero(data['connected'] & (data['As'][:,j] > data['base'][j]))
        uncertain=np.flatnonzero(data['connected'] & (np.abs(data['As'][:,j]-data['base'][j]) <= .005))
        channels[label]=dict(base=float(data['base'][j]),SO1_peak=float(data['peak'][j]),
                             GPR_peak=float(data['As'][data['connected'],j].max()),
                             exceeding_node_ids=(exceeding+1).tolist(),uncertain_threshold_node_ids=(uncertain+1).tolist())
    report={k:data[k] for k in ['project','source_sha256','coordinate_max_error_m']}
    report.update(status='RECORDED_NODAL_AS_VALIDATED',node_count=len(data['nodes']),element_count=len(data['elements']),
                  connected_node_count=int(data['connected'].sum()),channels=channels,UI_checks=checks,
                  limitation='GPR recorded As precision 0.01 cm2/m, coordinates 0.001 m. '
                  'RAD coordinates are used. Interpolated preview is a comparison, not an exact STA contour or bar design. '
                  'SO1-only nodal recovery remains unresolved. Reject missing/mismatched GPR; never fall back to guessed As.')
    (out/'gpr-heatmap-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    np.savez_compressed(out/'recorded-nodal-As.npz',**{k:data[k] for k in ['nodes','elements','As','base','peak','connected','table']})
    header='node,x_m,y_m,z_m,X_ust_cm2_m,X_alt_cm2_m,Y_ust_cm2_m,Y_alt_cm2_m,connected\n'
    rows=[','.join(map(str,[i+1,*data['nodes'][i,1:4],*data['As'][i],int(data['connected'][i])])) for i in range(len(data['nodes']))]
    (out/'recorded-nodal-As.csv').write_text(header+'\n'.join(rows),encoding='utf-8')
    draw(data,out)
    print(json.dumps(report,ensure_ascii=True,indent=2))


if __name__=='__main__':
    main()
