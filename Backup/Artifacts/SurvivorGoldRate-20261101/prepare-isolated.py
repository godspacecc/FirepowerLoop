from pathlib import Path
import subprocess,zipfile,io,json,hashlib
repo=Path.cwd();batch=repo/'Backup/Artifacts/SurvivorGoldRate-20261101';out=batch/'isolated';out.mkdir(exist_ok=True)
head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();(batch/'isolated-head.txt').write_text(head)
hand=['Client/Assets/Game/Scripts/Utility/Arcade/SurvivorConfiguration.cs','Client/Assets/Game/Scripts/System/Arcade/SurvivorSystem.Gold.cs','Client/Assets/Tests/EditMode/SurvivorConfigTests.cs','Docs/Plans/执行总清单.md']
archive=subprocess.check_output(['git','archive','--format=zip',head,'Config','Client/Assets/Game/Config/Localization',*hand]);zipfile.ZipFile(io.BytesIO(archive)).extractall(out)
p=out/'Config/Defines/beans.xml';s=p.read_text(encoding='utf-8-sig');s=s.replace('<bean name="SurvivorRuleConfig" group="e">\n        <var name="Id" type="string"/>','<bean name="SurvivorRuleConfig" group="e">\n        <var name="Id" type="string"/>\n        <var name="GoldDropMultiplier" type="float"/>');assert 'GoldDropMultiplier' in s;p.write_text(s,encoding='utf-8-sig')
p=out/'Config/DataCsv/survivor_rule.csv';lines=p.read_text(encoding='utf-8-sig').splitlines();p.write_text('\n'.join([lines[0]+',GoldDropMultiplier',lines[1]+',float',lines[2]+',0.1'])+'\n',encoding='utf-8-sig')
patch=(batch/'review.patch').read_text(encoding='utf-8');parts=patch.split('--- a/');selected=''
for part in parts[1:]:
 name=part.splitlines()[0]
 if name in hand[:2]:selected+='--- a/'+part
(batch/'isolated-handwritten.patch').write_text(selected,encoding='utf-8')
subprocess.run(['git','apply','--unsafe-paths','--directory='+str(out).replace('\\','/'),str(batch/'isolated-handwritten.patch')],check=True)
current=(repo/hand[2]).read_text(encoding='utf-8-sig');start=current.index('        /// <summary>独立金币倍率');end=current.index('        private static Tables',start)
p=out/hand[2];old=p.read_text(encoding='utf-8-sig');p.write_text(old.replace('        private static Tables',current[start:end]+'        private static Tables',1),encoding='utf-8-sig')
current=(repo/hand[3]).read_text(encoding='utf-8-sig');start=current.index('\n## 幸存者金币掉率十分之一');section=current[start:];end=section.find('\n## ',5);section=section if end<0 else section[:end]
p=out/hand[3];p.write_text(p.read_text(encoding='utf-8-sig')+section,encoding='utf-8-sig')
with (batch/'isolated-generation.log').open('w',encoding='utf-8') as log:
 result=subprocess.run(['cmd','/c',str(out/'Config/gen.bat')],stdout=log,stderr=subprocess.STDOUT)
assert result.returncode==0,result.returncode
candidates=['Config/Defines/beans.xml','Config/DataCsv/survivor_rule.csv',*hand]
for base in ['Client/Assets/Game/Scripts/Model/Config','Client/Assets/Game/Config','Server/GameServer/Runtime/Model/Generated/Config','Server/GameServer/Config/Luban']:
 candidates += [str(p.relative_to(out)).replace('\\','/') for p in (out/base).glob('*') if p.is_file()]
manifest=[]
for name in candidates:
 data=(out/name).read_bytes();old=subprocess.run(['git','show',head+':'+name],capture_output=True)
 if old.returncode==0 and old.stdout==data:continue
 manifest.append({'path':name,'source':str(out/name),'sha256':hashlib.sha256(data).hexdigest()})
(batch/'commit-manifest.json').write_text(json.dumps({'head':head,'files':manifest},ensure_ascii=False,indent=2),encoding='utf-8')
print('Prepared',len(manifest),'files')
