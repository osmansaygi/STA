"""Observe STA process file handles. Sampling can miss short-lived opens.

This is not a complete I/O trace and cannot prove an unobserved file was unused.
No process memory, engineering settings or source files are modified.
"""
import argparse
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).with_name('deps')))
import psutil

parser = argparse.ArgumentParser()
parser.add_argument('--seconds', type=float, default=60)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
seen = set()
start = time.monotonic()
iterations = 0
with args.output.open('w', encoding='utf-8', buffering=1) as out:
    while time.monotonic() - start < args.seconds:
        for process in psutil.process_iter(['name', 'pid']):
            if process.info['name'].upper() not in {'STA.EXE', 'RADSTA.EXE', 'RADSON.EXE'}:
                continue
            try:
                paths = [item.path for item in process.open_files()]
                for path in paths:
                    key = (process.pid, path)
                    if key in seen:
                        continue
                    seen.add(key)
                    out.write(json.dumps(dict(seconds=round(time.monotonic()-start, 3),
                                              pid=process.pid, name=process.name(),
                                              path=path), ensure_ascii=True) + '\n')
            except (psutil.NoSuchProcess, psutil.AccessDenied) as error:
                key = (process.pid, type(error).__name__)
                if key not in seen:
                    seen.add(key)
                    out.write(json.dumps(dict(pid=process.pid, error=type(error).__name__))+'\n')
        iterations += 1
        time.sleep(.01)
    out.write(json.dumps(dict(iterations=iterations, elapsed=round(time.monotonic()-start, 3),
                              limitation='Sampled handles, not a complete file I/O trace'))+'\n')
print('observed', len(seen), 'file/process pairs in', iterations, 'samples')
