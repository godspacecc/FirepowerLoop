from pathlib import Path
import re
root=Path(__file__).parent
page=(root/'index.html').read_text(encoding='utf-8')
links=re.findall(r'(?:href|src)="([^"]+)"',page)
missing=[x for x in links if not (root/x).is_file()]
assert not missing,missing
assert len(list(root.glob('*/*-applied.png')))==15
assert len(list(root.glob('*/*-accepted.png')))==15
print(f'HTML links verified: {len(links)}; 30 PNG files present')
