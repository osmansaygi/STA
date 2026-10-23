"""Read-only probe for the observed STA4CAD V14.01 RAD/SO1 pair.

The SO1 result channels are deliberately unnamed: their physical meaning,
combination rules and nodal As recovery are not yet validated. This exports
source data; it does not calculate or certify a reinforcement heatmap.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


def inspect(project, output):
    rad_path = project.with_suffix('.RAD')
    so1_path = project.with_suffix('.SO1')
    rad_bytes = rad_path.read_bytes()
    so1_bytes = so1_path.read_bytes()
    lines = rad_bytes.decode('cp1254').splitlines()
    if lines[0] != 'V14.01':
        raise ValueError('Only the observed V14.01 layout is supported.')
    plate_count, node_count, element_count = map(int, lines[1].split(',')[:3])
    start = 7 + 2 * plate_count
    elements = np.array([
        [int(v) for v in line.split(',')[:5]]
        for line in lines[start:start + element_count]
    ], dtype=np.int32)
    start += element_count
    nodes = np.array([
        [float(v) for v in line.split(',')]
        for line in lines[start:start + node_count]
    ], dtype=np.float64)
    assert nodes.shape == (node_count, 6)
    assert np.isfinite(nodes).all()
    assert ((elements[:, 1:] >= 0) & (elements[:, 1:] <= node_count)).all()
    assert (np.count_nonzero(elements[:, 1:], axis=1) >= 3).all()
    stride = 2772
    body_length = element_count * stride
    if len(so1_bytes) < body_length:
        raise ValueError('SO1 is shorter than the observed result block layout.')
    raw = np.frombuffer(so1_bytes[:body_length], dtype='<f4').reshape(
        element_count, 21, 33)
    assert np.isfinite(raw).all()
    tail = so1_bytes[body_length:]
    if len(tail) != 350 or any(tail):
        raise ValueError('Unexpected SO1 tail; do not apply the observed layout.')
    base = raw[0, 0, 2:6]
    assert (base > 0).all()
    assert np.all(raw[:, 0, 2:6] == base)
    peak = raw[0, 0, 6:10]
    extra = np.maximum(peak - base, 0)
    labels = ['X_ust', 'X_alt', 'Y_ust', 'Y_alt']
    # PDF values below are manual reference readings, never input to parsing.
    pdf_legend = {'X_alt': 3.14, 'Y_ust': 0.53, 'Y_alt': 1.45}
    channels = {}
    for i, label in enumerate(labels):
        result = {
            'base_As_cm2_per_m': float(base[i]),
            'peak_As_cm2_per_m': float(peak[i]),
            'peak_extra_As_cm2_per_m': float(extra[i]),
        }
        if label in pdf_legend:
            result['pdf_reference_legend_peak'] = pdf_legend[label]
            result['matches_pdf_at_2_decimals'] = (
                round(float(extra[i]), 2) == pdf_legend[label])
            assert result['matches_pdf_at_2_decimals']
        channels[label] = result
    summary = {
        'project': str(project),
        'source_sha256': {
            str(rad_path): hashlib.sha256(rad_bytes).hexdigest(),
            str(so1_path): hashlib.sha256(so1_bytes).hexdigest(),
        },
        'plates': plate_count, 'nodes': node_count, 'elements': element_count,
        'SO1_observed_record_bytes': stride,
        'SO1_observed_record_shape': [21, 33],
        'SO1_zero_tail_bytes': len(tail),
        'channels': channels,
        'limitation': 'Per-point As recovery, force-channel semantics, load '
                      'envelopes and interpolation have not been validated. '
                      'The exported raw arrays are not a finished As heatmap.',
    }
    output.mkdir(parents=True, exist_ok=True)
    (output / 'summary.json').write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    np.savez_compressed(output / 'raw-mesh-results.npz',
                        node_rows=nodes, element_rows=elements,
                        SO1_unclassified_blocks=raw.copy())
    # Prove the probe did not change either input, including during export.
    assert rad_path.read_bytes() == rad_bytes
    assert so1_path.read_bytes() == so1_bytes
    print(json.dumps(summary, ensure_ascii=True, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path, help='Project path without extension')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    inspect(args.project, args.output)
