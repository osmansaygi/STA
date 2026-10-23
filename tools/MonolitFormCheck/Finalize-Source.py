from pathlib import Path
import re, subprocess

root = Path(__file__).resolve().parents[2]
for module in ('ST4_Yardimci', 'ST4_Plan_ID_Ciz'):
    for path in (root/module).glob('*Form.cs'):
        current = path.read_bytes()
        text = current.decode('utf-8-sig')
        if 'BeginCompact(' not in text:
            continue
        text = text.replace('\r\n', '\n')
        text = re.sub(r'\n[ \t]+\n(?=            // Kompakt,)', '\n\n', text)
        old = subprocess.run(['git', 'show', 'HEAD:'+path.relative_to(root).as_posix()], cwd=root, capture_output=True)
        # Preserve the repository's existing newline convention; do not reformat code.
        if old.returncode == 0 and old.stdout.count(b'\r\n') > old.stdout.count(b'\n') * 0.8:
            text = text.replace('\n', '\r\n')
        result = text.encode('utf-8')
        if current.startswith(b'\xef\xbb\xbf'):
            result = b'\xef\xbb\xbf' + result
        if result != current:
            path.write_bytes(result)
