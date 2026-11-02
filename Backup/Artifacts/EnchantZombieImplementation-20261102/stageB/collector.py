from pathlib import Path
import re,json
p=Path('Client/Assets/AssetBundleCollectorSetting.asset');s=p.read_text(encoding='utf-8-sig');block=re.search(r'      - CollectPath: Assets/Game/Prefabs/Characters/ArcadeZombieCaster.prefab\n.*?(?=      - CollectPath:)',s,re.S).group();add=''
for n in ['Health','Speed','Shield']:
 path='Assets/Game/Prefabs/Characters/ArcadeZombieEnchant'+n+'.prefab'; guid=re.search(r'guid: (\w+)',Path('Client/'+path+'.meta').read_text()).group(1);add+=re.sub(r'CollectorGUID: \w+','CollectorGUID: '+guid,block.replace('Assets/Game/Prefabs/Characters/ArcadeZombieCaster.prefab',path))
s=s.replace(block,block+add);p.write_text(s,encoding='utf8')
