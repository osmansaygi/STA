"""Read-only search for SO1 As peak values in the licensed results process.

Research only: this is not an on-disk nodal As reader. No license, calculation,
file contents or process code is patched by this probe.
"""
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).with_name('deps')))
import frida
import psutil

processes = [p for p in psutil.process_iter(['name'])
             if p.info['name'].upper() == 'RADSON.EXE']
if len(processes) != 1:
    raise RuntimeError('Expected one RADSON process')
session = frida.attach(processes[0].pid)
try:
    code = '''
rpc.exports = {
  nodes(address, stride, count) {
    const base = ptr(address);
    const rows = [];
    for(let i=0;i<count;i++) {
      const p = base.add(i*stride);
      rows.push([p.readFloat(),p.add(4).readFloat(),p.add(8).readFloat(),p.add(12).readFloat()]);
    }
    const before=[];
    for(let k=-60;k<0;k++)before.push(base.add(146*stride+k*4).readFloat());
    return {rows,before};
  },
  vicinity(address) {
    const p=ptr(address), values=[];
    for(let k=-240;k<260;k++)values.push(p.add(k*4).readFloat());
    return values;
  },
  peaks(patterns) {
    const ranges = Process.enumerateRanges({protection:'rw-', coalesce:true});
    const found = [];
    for (const range of ranges) {
      for (const pattern of patterns) {
        let matches;
        try { matches = Memory.scanSync(range.base, range.size, pattern); }
        catch (_) { continue; }
        for (const match of matches.slice(0,100)) {
          let floats = [];
          try { for(let i=-12;i<=24;i++) floats.push(match.address.add(i*4).readFloat()); }
          catch (_) { continue; }
          found.push({pattern,address:match.address.toString(),
                      range:range.base.toString(),size:range.size,floats});
        }
      }
    }
    return {process:Process.id, pointerSize:Process.pointerSize,found};
  }
};
'''
    script = session.create_script(code)
    script.load()
    so1 = (Path(__file__).parent / 'data/N_ULK_02.SO1').read_bytes()
    patterns = [so1[offset:offset+4].hex(' ') for offset in [24,28,32,36]]
    result = script.exports_sync.peaks(patterns)
    Path(__file__).with_name('live-peak-locations.json').write_text(
        json.dumps(result, indent=2), encoding='utf-8')
    print('pid', result['process'], 'pointer size', result['pointerSize'],
          'peak occurrences', len(result['found']))
    for match in result['found'][:30]:
        print(match['address'], match['pattern'], match['floats'][8:24])
    candidates = [match for match in result['found']
                  if match['pattern'] == patterns[1]
                  and abs(match['floats'][13]-3.3)<.001
                  and abs(match['floats'][14]-5.63928)<.001]
    if len(candidates)==1:
        # Research hypothesis only, checked against all reference nodes below.
        base = int(candidates[0]['address'],16)-146*960
        nodes = script.exports_sync.nodes(hex(base),960,733)
        Path(__file__).with_name('live-node-As-candidate.json').write_text(
            json.dumps(dict(base=hex(base),stride=960,**nodes),indent=2), encoding='utf-8')
        print('candidate base',hex(base),'node1',nodes['rows'][0],
              'node147',nodes['rows'][146],'before node147',nodes['before'])
        vicinity=script.exports_sync.vicinity(hex(base))
        Path(__file__).with_name('node1-vicinity.json').write_text(json.dumps(vicinity))
finally:
    session.detach()
