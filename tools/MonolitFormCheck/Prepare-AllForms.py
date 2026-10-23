"""Compile the real form sources against a detached host; redirect file I/O in copies only."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
target = root/'tools/MonolitFormCheck/generated'
target.mkdir(exist_ok=True)
sources = [p for module in ('ST4_Yardimci', 'ST4_Plan_ID_Ciz') for p in (root/module).glob('*Form.cs')
           if ': Beykent.Monolit.MonolitForm' in p.read_text(encoding='utf-8-sig')]
sources += [root/'ST4_Yardimci'/name for name in ('HasirDonatiSettings.cs', 'OtoDonatiSettings.cs',
    'DosemeDonatiSettings.cs', 'DosemeKonsolSettings.cs', 'PlanOlcuSettings.cs', 'ParapetSettings.cs', 'BaIni.cs')]
sources.append(root/'Ortak/BeykentStil/BeykentStilAyar.cs')
for source in sources:
    text = source.read_text(encoding='utf-8-sig')
    text = text.replace('System.IO.File.', 'TestStore.')
    text = re.sub(r'(?<![\w.])File\.', 'TestStore.', text)
    text = text.replace('Directory.CreateDirectory(', 'TestStore.CreateDirectory(')
    (target/source.name).write_text(text, encoding='utf-8')
print('Detached copies prepared:', len(sources), '(UI code unchanged; disk access redirected)')
