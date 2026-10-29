from pathlib import Path
import re,json,csv,collections
root=Path.cwd();out=root/'Backup/Artifacts/FullRetiredContentCleanup-20261029';rows={};by_stem=collections.defaultdict(set)
for s in (out/'asset-graph-after-core.tsv').read_text(encoding='utf-8-sig').splitlines():
 fields=s.split('\t'); fields += [''] * (5 - len(fields))
 p,g,t,b,d=fields;rows[p]={'guid':g,'type':t,'bytes':int(b),'deps':d.split('|') if d else []};by_stem[Path(p).stem].add(p)
collect=[x.strip() for x in re.findall(r'CollectPath: (.+)',(root/'Client/Assets/AssetBundleCollectorSetting.asset').read_text(encoding='utf-8-sig')) if x.strip()!='Assets/Game/Art/UI/Items'];roots={p for p in rows if any(p==x or p.startswith(x+'/') for x in collect)}
texts=[];literals=set()
for base in ['Client/Assets/Game/Scripts','Client/Assets/Editor','Client/Assets/Tests','Tools','Config']:
 for f in (root/base).rglob('*'):
  if f.is_file() and f.suffix in ['.cs','.csv','.json','.xml','.ps1','.py'] and not any(x in f.parts for x in ['obj','bin']):
   s=f.read_text(encoding='utf-8-sig',errors='replace');texts.append(s)
   if '/Tests/' not in f.as_posix():literals.update(re.findall(r'"((?:[^"\\\r\n]|\\.)*)"',s))
joined='\n'.join(texts)
for p in rows:
 rel=p.removeprefix('Assets/Game/')
 if p in joined or (rel!=p and rel in joined):roots.add(p)
for token in literals:roots.update(by_stem.get(token,set()))
for f in (root/'Config/DataCsv').glob('*.csv'):
 for line in csv.reader(f.read_text(encoding='utf-8-sig').splitlines()):
  for v in line:
   if v:roots.update(by_stem.get(v,set()));roots.update(p for p in ['Assets/Game/'+v+'.prefab','Assets/Game/'+v+'.unity'] if p in rows)
guids={v['guid']:k for k,v in rows.items()}
for f in (root/'Client/ProjectSettings').glob('*'):
 if f.is_file():roots.update(guids[g] for g in re.findall(r'\b[0-9a-f]{32}\b',f.read_text(encoding='utf-8-sig',errors='replace')) if g in guids)
for p in json.loads((out/'unrelated-baseline.json').read_text(encoding='utf-8')):roots.add(p.removeprefix('Client/'))
roots.add('Assets/Game/Scenes/Maps/AppearanceGallery.unity');roots.update(p for p in rows if p.startswith('Assets/Game/Art/UI/AppIcon/'))
# 主运行时与作者工具按Shader声明名查找，而不是按文件路径。
for p in rows:
 if p.endswith('.shader'):
  s=(root/'Client'/p).read_text(encoding='utf-8-sig',errors='replace');m=re.search(r'Shader\s+"([^"]+)"',s)
  if m and m[1] in literals:roots.add(p)
# 保留不在本次退役资源范围内的包、启动资源和原始备用库的依赖。
def in_scope(p):return p.startswith(('Assets/Game/Art/','Assets/Game/Materials/','Assets/Game/Prefabs/','Assets/Game/Audio/','Assets/Game/Scenes/','Assets/Settings/','Assets/Samples/'))
roots.update(p for p in rows if not in_scope(p) and not p.endswith(('.cs','.dll','.asmdef')))
def closure(roots):
 reached=set();todo=list(roots)
 while todo:
  p=todo.pop()
  if p in reached:continue
  reached.add(p);todo.extend(rows.get(p,{}).get('deps',[]))
 return reached
reached=closure(roots)
# 正式资源的来源、许可证和说明文档是伴随资料，不依赖Unity序列化引用。
for p in rows:
 if p.startswith('Assets/Game/Art/') and Path(p).suffix in ['.md','.txt','.json']:
  parent=str(Path(p).parent).replace('\\','/')+'/'
  if any(x.startswith(parent) and Path(x).suffix not in ['.md','.txt','.json'] for x in reached):roots.add(p)
reached=closure(roots);c=[p for p in rows if in_scope(p) and p not in reached and (root/'Client'/p).is_file()]
# Sample来源元信息可保留，普通示例场景/按钮不作为游戏依赖。
c=[p for p in c if not p.endswith('/.sample.json')]
report={'rootCount':len(roots),'reachedCount':len(reached),'roots':sorted(roots),'candidates':c,'candidateBytes':sum(rows[p]['bytes'] for p in c)};(out/'unreachable-assets.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('Candidates',len(c),'MiB',round(report['candidateBytes']/1048576,2));print('Critical protection',[(n,not any(p.endswith(n) for p in c)) for n in ['Run_Arms.anim','Walk.anim','ArcadeWeaponPiercingEvolved.prefab','ArcadeWeaponGrenadeEvolved.prefab','IslandCoastWater.shader','ArcadeChickenCompanion.SOURCE.md']]);print('Settings/Samples:',[p for p in c if p.startswith(('Assets/Settings/','Assets/Samples/'))])
