from pathlib import Path
import json,hashlib
p=Path('Backup/Artifacts/EnchantZombieImplementation-20261102/stageB');a=json.loads((p/'changes.json').read_text(encoding='utf8'));bad=[x['path'] for x in a if hashlib.sha256(Path(x['path']).read_bytes()).hexdigest()!=x['sha256']];print('41 task file hashes verified, mismatches:',bad)
from PIL import Image
for k in range(3):
 im=Image.open(p/f'formal-{k}.gif');duration=0
 for i in range(im.n_frames):im.seek(i);duration+=im.info.get('duration',0)
 print(k,'frames',im.n_frames,'duration_ms',duration)
