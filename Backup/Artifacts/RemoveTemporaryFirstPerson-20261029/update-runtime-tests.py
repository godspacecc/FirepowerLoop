from pathlib import Path
import re
root=Path('E:/GitHub/FirepowerLoop')
def edit(rel,fn):
 p=root/rel;s=p.read_text(encoding='utf-8-sig');t=fn(s)
 if s!=t:p.write_text(t,encoding='utf-8',newline='\r\n')
# 原用例仅服务于退役道具，替换为退役契约并保留 Unity meta 身份。
p=root/'Client/Assets/Tests/EditMode/ArcadeFirstPersonTests.cs'
p.write_text((root/'Client/Assets/Tests/EditMode/TemporaryCameraRetirementTests.cs').read_text(encoding='utf-8-sig'),encoding='utf-8',newline='\r\n')
(root/'Client/Assets/Tests/EditMode/TemporaryCameraRetirementTests.cs').unlink()
(root/'Client/Assets/Tests/EditMode/TemporaryCameraRetirementTests.cs.meta').unlink(missing_ok=True)
p.rename(p.with_name('TemporaryCameraRetirementTests.cs'))
p.with_suffix('.cs.meta').rename(p.with_name('TemporaryCameraRetirementTests.cs.meta'))
edit('Client/Assets/Tests/EditMode/IslandRoundSaveTests.cs',lambda s:s.replace('FirstPersonRemaining = 2f, ',''))
def flow(s):
 s=re.sub(r'        \[Test\] public void TreasureActivityKeepsFirstPersonIdentityAndConsumesActualTime\(\)\n        \{.*?\n        }\n','',s,flags=re.S)
 s=re.sub(r'runtime.FirstPersonRemaining = 10; ?', '',s)
 s=s.replace(' view.FirstPerson = true;','').replace(', ArcadeHudActionKind.ExitFirstPerson','')
 s=re.sub(r'Assert.That\(runtime.FirstPersonRemaining, Is\.(?:EqualTo\(\d+\)|Zero)\); ?', '',s)
 s=re.sub(r'Assert.That\(view.FirstPerson, Is.True\);','',s)
 s='\n'.join(l for l in s.split('\n') if not any(x in l for x in ['public bool FirstPerson;', 'public bool IsFirstPersonActive', 'public bool TryBeginFirstPerson','public bool TryReadFirstPersonAim','public void EndFirstPerson']))
 return s
edit('Client/Assets/Tests/EditMode/IslandRoundFlowTests.cs',flow)
edit('Client/Assets/Game/Scripts/Controller/Arcade/ArcadeCombatViewController.cs',lambda s:'\n'.join(l for l in s.split('\n') if not any(x in l for x in ['/// <summary>第一人称弹丸生成时','/// <summary>当前视角表现持有的地图租约'])))
edit('Client/Assets/Game/Scripts/System/Arcade/ArcadeCombatSystem.Input.cs',lambda s:s.replace('using QFramework;\n','').replace('        private void ClearArcadeInput','        /// <summary>清空活动输入并要求重新按下，防止暂停和转场后误射。</summary>\n        private void ClearArcadeInput').replace('        private bool ReadArcadeFiring','        /// <summary>统一摇杆与设备射击状态，沿用同一个释放闸门。</summary>\n        private bool ReadArcadeFiring'))
