from pathlib import Path
import subprocess,json,difflib,hashlib
r=Path('Backup/Artifacts/SurvivorGoldRate-20261101');out=r/'isolated';head=(r/'isolated-head.txt').read_text().strip();patch='';manifest=[]
paths=['Config/Defines/beans.xml','Config/DataCsv/survivor_rule.csv','Client/Assets/Game/Scripts/Utility/Arcade/SurvivorConfiguration.cs','Client/Assets/Game/Scripts/System/Arcade/SurvivorSystem.Gold.cs','Client/Assets/Tests/EditMode/SurvivorConfigTests.cs','Docs/Plans/执行总清单.md','Client/Assets/Game/Scripts/Model/Config/SurvivorRuleConfig.cs','Client/Assets/Game/Config/tbsurvivorrule.bytes','Server/GameServer/Runtime/Model/Generated/Config/SurvivorRuleConfig.cs','Server/GameServer/Config/Luban/tbsurvivorrule.bytes']
for name in paths:
 p=out/name;data=p.read_bytes();old=subprocess.check_output(['git','show',head+':'+name]);
 if not name.endswith('.bytes'):
  text=data.decode('utf-8-sig').replace('\r\n','\n');data=(b'\xef\xbb\xbf' if old.startswith(b'\xef\xbb\xbf') else b'')+text.encode('utf-8');p.write_bytes(data)
 manifest.append({'path':name,'source':str(p.resolve()),'sha256':hashlib.sha256(data).hexdigest()})
 try:patch+=''.join(difflib.unified_diff(old.decode('utf-8-sig').splitlines(True),data.decode('utf-8-sig').splitlines(True),fromfile='a/'+name,tofile='b/'+name))
 except UnicodeDecodeError:patch+='Binary files a/'+name+' and b/'+name+' differ\n'
(r/'commit-review.patch').write_text(patch,encoding='utf-8');(r/'commit-manifest-final.json').write_text(json.dumps({'head':head,'files':manifest},ensure_ascii=False,indent=2),encoding='utf-8')
for base in ['Client/Assets/Game/Scripts/Model/Config','Server/GameServer/Runtime/Model/Generated/Config']:
 s=(out/base/'TbSurvivorRule.cs').read_text(encoding='utf-8-sig');assert 'Get(string key)' in s and '_dataMap.Add(_v.Id, _v)' in s
 s=(out/base/'SurvivorRuleConfig.cs').read_text(encoding='utf-8-sig');assert 'GoldDropMultiplier' in s and 'NightAimRange' not in s
assert 'NightAimRange' not in (out/'Config/Defines/beans.xml').read_text(encoding='utf-8-sig')
print('10-file final manifest and primary key isolation checks passed')
