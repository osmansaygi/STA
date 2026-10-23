"""Read-only diagnostics for a STA results DXF, without assigning As to colors.

Colored faces alone do not prove exact nodal As or an extra-rebar contour.
"""
import argparse
from collections import Counter
import json
from pathlib import Path


def entities(path):
    lines = path.read_bytes().decode('cp1254', errors='replace').splitlines()
    if len(lines) % 2:
        raise ValueError('Incomplete DXF group/value pair')
    pairs = [(int(lines[i].strip()), lines[i + 1].strip())
             for i in range(0, len(lines), 2)]
    section, current, result = '', None, []
    for i, (code, value) in enumerate(pairs):
        if code == 0:
            if current is not None:
                result.append(current)
            current = [(code, value)] if section == 'ENTITIES' and value != 'ENDSEC' else None
            if value == 'ENDSEC':
                section = ''
        elif code == 2 and i and pairs[i - 1] == (0, 'SECTION'):
            section = value
        elif current is not None:
            current.append((code, value))
    return result


def inspect(path):
    records = entities(path)
    kinds, layers, colors, text, coords = Counter(), Counter(), Counter(), [], []
    for record in records:
        values = dict(record)
        kind = values[0]
        kinds[kind] += 1
        layers[values.get(8, '0')] += 1
        if kind in ('SOLID', '3DFACE', 'HATCH', 'LWPOLYLINE', 'POLYLINE'):
            colors[values.get(420, values.get(62, 'BYLAYER'))] += 1
        if kind in ('TEXT', 'MTEXT'):
            text.append(''.join(v for c, v in record if c in (1, 3)))
        for c, v in record:
            if c in range(10, 19):
                coords.append([float(v), float(values.get(c + 10, 0)),
                               float(values.get(c + 20, 0))])
    bounds = [[min(row[i] for row in coords), max(row[i] for row in coords)]
              for i in range(3)] if coords else []
    return dict(path=str(path), entities=len(records), types=dict(kinds),
                layers=dict(layers), face_colors=dict(colors), bounds=bounds,
                text=text, limitation='DXF color bins are not exact nodal As. '
                'Model registration, direction/side and excess-As threshold '
                'must be checked before reinforcement generation.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dxf', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = inspect(args.dxf)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'text'}, ensure_ascii=True))
