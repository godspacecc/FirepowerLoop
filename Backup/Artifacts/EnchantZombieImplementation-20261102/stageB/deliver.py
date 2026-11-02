from pathlib import Path
import json,hashlib,difflib,shutil
r=Path('.').resolve();d=r/'Backup/Artifacts/EnchantZombieImplementation-20261102/stageB';files=['Client/Assets/AssetBundleCollectorSetting.asset']
for n in ['ArcadeEnemyArcher','ArcadeEnemyBasic','ArcadeEnemyBossArcher','ArcadeEnemyBrute','ArcadeShadowZombie','ArcadeZombieBloated','ArcadeZombieCaster','ArcadeZombieExploder','ArcadeZombieGaunt','ArcadeZombieShambler','ArcadeZombieShield','GrayboxArcher','GrayboxEnemy','ArcadeZombieEnchantHealth','ArcadeZombieEnchantSpeed','ArcadeZombieEnchantShield']:files.append('Client/Assets/Game/Prefabs/Characters/'+n+'.prefab')
for p in ['Controller/Enemy/EnemyCtrl.cs','Controller/Enemy/EnemyCtrl.Enchantments.cs','Controller/View/Map/EnemyView.Enchantments.cs','Controller/View/Map/EnemyView.SpecialZombies.cs','Controller/ViewCtrl/Enemy/EnemyRuntimePorts.cs','Controller/Arcade/ArcadeCombatViewController.SpecialZombies.cs']:files.append('Client/Assets/Game/Scripts/'+p)
files.append('Client/Assets/Tests/EditMode/Flow/SurvivorEnchantPresentationTests.cs');files.append('Client/Assets/Game/Art/Arcade/SpecialZombies/Enchant.meta')
files += [str(p.relative_to(r)).replace('\\','/') for p in (r/'Client/Assets/Game/Art/Arcade/SpecialZombies/Enchant').glob('*') if p.is_file()]
files=list(dict.fromkeys(files+[p+'.meta' for p in files if not p.endswith('.meta')]))
a=[];patch=[]
for f in files:
 p=r/f;b=d/'baseline'/f
 if not p.exists():continue
 before=b.read_bytes() if b.exists() else b'';after=p.read_bytes()
 if before==after:continue
 a.append({'path':f,'baselineSha256':hashlib.sha256(before).hexdigest() if b.exists() else None,'sha256':hashlib.sha256(after).hexdigest(),'new':not b.exists(),'generated':False})
 dst=d/'implementation'/f;dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dst)
 try:patch+=list(difflib.unified_diff(before.decode('utf-8-sig').splitlines(True),after.decode('utf-8-sig').splitlines(True),fromfile='a/'+f,tofile='b/'+f))
 except UnicodeDecodeError:patch.append('Binary files differ: '+f+'\n')
(d/'changes.json').write_text(json.dumps(a,indent=2,ensure_ascii=False),encoding='utf8');(d/'review.patch').write_text(''.join(patch),encoding='utf8');print('Task manifest:',len(a),'files')
# Exact structured result recorded from the final tool reply.
tests={'Summary':{'Status':'Passed','TotalTests':4,'PassedTests':4,'FailedTests':0,'SkippedTests':0,'Duration':'00:00:06.8464446'},'Results':[]}
for n,duration in [('EveryReceiverRendersCoexistingSnapshotAndClearsOnReuse','00:00:00.1990000'),('FormalControllerOverridesBothProjectionPorts','00:00:00'),('PulsePoolIsBoundedFreezesAndClearsIndependently','00:00:00.0060000'),('ThreeFormalAddressesHaveUniqueArcadeCollectionAndRingDependencies','00:00:05.1740000')]:tests['Results'].append({'Name':'Game.Tests.SurvivorEnchantPresentationTests.'+n,'Status':'Passed','Duration':duration})
(d/'tests-green.json').write_text(json.dumps({'result':tests},indent=2),encoding='utf8')
