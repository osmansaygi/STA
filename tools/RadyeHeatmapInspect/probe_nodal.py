"""Probe raw RAD/SO1 connectivity and corner averaging against STA UI readings.

This is deliberately not an As calculator or heat-map generator. A failed
recovery hypothesis must remain visible instead of being fitted to samples.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


def probe(project, output, reference):
    paths = [project.with_suffix('.RAD'), project.with_suffix('.SO1')]
    before = [path.read_bytes() for path in paths]
    lines = before[0].decode('cp1254').splitlines()
    if lines[0] != 'V14.01':
        raise ValueError('Unexpected RAD version')
    plates, node_count, element_count = map(int, lines[1].split(',')[:3])
    start = 7 + 2 * plates
    element_rows = np.array([list(map(float, row.split(',')))
                             for row in lines[start:start + element_count]])
    elements = element_rows[:, 1:5].astype(np.int32)
    if not np.array_equal(elements, element_rows[:, 1:5]):
        raise ValueError('Non-integral element node IDs')
    if not ((elements >= 0) & (elements <= node_count)).all():
        raise ValueError('Node ID out of range')
    start += element_count
    nodes = np.array([list(map(float, row.split(',')))
                      for row in lines[start:start + node_count]])
    body_size = element_count * 21 * 33 * 4
    if len(before[1]) != body_size + 350 or any(before[1][body_size:]):
        raise ValueError('Unexpected observed SO1 layout')
    raw = np.frombuffer(before[1][:body_size], dtype='<f4').reshape(element_count, 21, 33)
    if not np.isfinite(raw).all():
        raise ValueError('Non-finite source result')
    corners = raw[:, 1:18, 1:].reshape(element_count, 17, 4, 8)
    mask = elements > 0
    if not np.isin(mask.sum(axis=1), [3, 4]).all():
        raise ValueError('Unexpected element vertex count')
    sums = np.zeros((node_count, 17, 8), dtype=np.float64)
    counts = np.zeros(node_count, dtype=np.int32)
    for element in range(element_count):
        for corner, node_id in enumerate(elements[element]):
            if node_id:
                sums[node_id - 1] += corners[element, :, corner]
                counts[node_id - 1] += 1
    means = np.full_like(sums, np.nan)
    connected = counts > 0
    means[connected] = sums[connected] / counts[connected, None, None]
    ref = json.loads(reference.read_text(encoding='utf-8'))
    checks = []
    for node_id, values in ref['nodes'].items():
        index = int(node_id) - 1
        for component, column in [('Mx', 3), ('My', 4)]:
            shown = values[0 if component == 'Mx' else 1]
            actual = float(means[index, 0, column])
            checks.append(dict(node=int(node_id), component=component,
                               UI=shown, arithmetic_corner_mean=actual,
                               matches_display_precision=abs(actual - shown) <= .0051))
    failed = [check for check in checks if not check['matches_display_precision']]
    report = dict(
        project=str(project), plates=plates, nodes=node_count, elements=element_count,
        triangle_elements=int((mask.sum(axis=1) == 3).sum()),
        connected_corner_nodes=int(connected.sum()),
        nodes_without_corner_contribution=int((~connected).sum()),
        ignored_nonzero_fourth_slots=int(np.count_nonzero(corners.transpose(0, 2, 1, 3)[~mask])),
        source_sha256={str(path): hashlib.sha256(data).hexdigest()
                       for path, data in zip(paths, before)},
        checks=checks, matched=len(checks) - len(failed), failed=len(failed),
        status='INCOMPLETE_RECOVERY' if failed else 'SAMPLE_MOMENTS_ONLY',
        limitation='Corner arithmetic means do not yet reproduce all STA moments. '
                   'No validated nodal As, envelope or reinforcement contour is produced.')
    output.mkdir(parents=True, exist_ok=True)
    (output / 'nodal-probe.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    np.savez_compressed(output / 'raw-corner-node-probe.npz',
                        node_rows=nodes, element_rows=element_rows,
                        source_corner_results=corners.copy(),
                        connected_node_mask=connected, contribution_counts=counts,
                        unvalidated_arithmetic_mean=means)
    if any(path.read_bytes() != data for path, data in zip(paths, before)):
        raise RuntimeError('Source changed during probe')
    print(json.dumps({key: report[key] for key in
                     ['nodes', 'elements', 'triangle_elements', 'connected_corner_nodes',
                      'nodes_without_corner_contribution', 'matched', 'failed', 'status']}))
    print(json.dumps(failed))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--reference', type=Path, default=Path(__file__).with_name('ui-reference.json'))
    args = parser.parse_args()
    probe(args.project, args.output, args.reference)
