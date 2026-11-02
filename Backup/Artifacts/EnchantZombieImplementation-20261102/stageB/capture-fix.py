from pathlib import Path
p=Path('Backup/Artifacts/EnchantZombieImplementation-20261102/stageB/Capture.cs');s=p.read_text(encoding='utf8');s=s.replace('foreach(var e in all.Values)C(s,"ProjectSpecialEnemy",e);','foreach(var e in all.Values){C(s,"ProjectSpecialEnemy",e);if(ep.TryGetEnemyPresentationRoot((int)G(e,"Id"),out var er))Layer(er.gameObject);}')
p.write_text(s,encoding='utf8')
