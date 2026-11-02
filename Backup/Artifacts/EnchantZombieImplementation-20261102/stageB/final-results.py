from pathlib import Path
import json
p=Path('Backup/Artifacts/EnchantZombieImplementation-20261102/stageB');r=json.loads((p/'tests-green-raw.json').read_text(encoding='utf-8-sig'));(p/'tests-green.json').write_text(json.dumps(r['structuredContent'],indent=2),encoding='utf8')
