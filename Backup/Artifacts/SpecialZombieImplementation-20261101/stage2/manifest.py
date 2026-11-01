from pathlib import Path
import hashlib,json,difflib
root=Path('.').resolve();out=root/'Backup/Artifacts/SpecialZombieImplementation-20261101/stage2';base=out/'baseline'
paths=[p.relative_to(base).as_posix() for p in base.rglob('*') if p.is_file()]
paths=[p for p in paths if p!='Client/Assets/Game/Prefabs/Combat/GrayboxArrow.prefab']
paths+=['Client/Assets/Game/Scripts/Controller/View/Map/EnemyView.SpecialZombies.cs','Client/Assets/Game/Scripts/Controller/Projectile/ProjectileCtrl.SpecialZombies.cs','Client/Assets/Game/Scripts/Controller/Arcade/ArcadeCombatViewController.SpecialZombies.cs','Client/Assets/Tests/EditMode/SurvivorSpecialVisualTests.cs','Client/Assets/Tests/EditMode/SurvivorSpecialFacingTests.cs']
paths+=['Client/Assets/Game/Prefabs/Characters/ArcadeZombie'+n+'.prefab' for n in ['Exploder','Caster','Shield']]
paths+= [p.relative_to(root).as_posix() for p in (root/'Client/Assets/Game/Art/Arcade/SpecialZombies').rglob('*') if p.is_file() and p.suffix!='.meta']
paths+=['Client/Assets/Game/Art/Arcade/SpecialZombies.meta']
paths += [p+'.meta' for p in paths if not (base/p).exists() and not p.endswith('.meta') and (root/(p+'.meta')).exists()]
records=[];patch=[]
for p in sorted(set(paths)):
 f=root/p;b=base/p
 if not f.exists():continue
 new=f.read_bytes();old=b.read_bytes() if b.exists() else b''
 if new==old:continue
 records.append({'path':p,'baselineSha256':hashlib.sha256(old).hexdigest() if b.exists() else None,'sha256':hashlib.sha256(new).hexdigest()})
 try:patch.extend(difflib.unified_diff(old.decode('utf-8-sig').splitlines(True),new.decode('utf-8-sig').splitlines(True),fromfile='a/'+p if b.exists() else '/dev/null',tofile='b/'+p))
 except UnicodeDecodeError:pass
(out/'changes.json').write_text(json.dumps({'scope':'stage2 only; baseline is actual start; no index/commit/build','files':records},ensure_ascii=False,indent=2),encoding='utf-8');(out/'review.patch').write_text(''.join(patch),encoding='utf-8')
print(len(records),'changed files')
