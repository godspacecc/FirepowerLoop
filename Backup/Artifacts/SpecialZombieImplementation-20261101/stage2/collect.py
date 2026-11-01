from pathlib import Path
import re
p=Path('Client/Assets/AssetBundleCollectorSetting.asset');s=p.read_text(encoding='utf-8-sig');anchor='      - CollectPath: Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab'; pos=s.index(anchor)
add=''
for name in ['Exploder','Caster','Shield']:
 path='Assets/Game/Prefabs/Characters/ArcadeZombie'+name+'.prefab';guid=re.search(r'guid: (\w+)',Path('Client/'+path+'.meta').read_text()).group(1)
 add+=f'      - CollectPath: {path}\n        CollectorGUID: {guid}\n        CollectorType: 0\n        AddressRuleName: AddressByGameRelativePathNoExtension\n        PackRuleName: PackSeparately\n        FilterRuleName: CollectAll\n        AssetTags: arcade\n        UserData: \n'
s=s[:pos]+add+s[pos:];p.write_text(s,encoding='utf-8')
