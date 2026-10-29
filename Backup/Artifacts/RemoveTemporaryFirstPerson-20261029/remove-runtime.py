from pathlib import Path
import re
root=Path('E:/GitHub/FirepowerLoop')
base=root/'Client/Assets/Game/Scripts'
def edit(path, fn):
 p=path if isinstance(path,Path) else base/path
 s=p.read_text(encoding='utf-8-sig'); t=fn(s)
 if s!=t:p.write_text(t,encoding='utf-8',newline='\r\n')
def block(s, start):
 i=s.index(start); j=s.index('{',i); n=1;k=j+1
 while n:
  n+=(s[k]=='{')-(s[k]=='}');k+=1
 return s[:i]+s[k:]
def span(s,a,b):return s[:s.index(a)]+s[s.index(b,s.index(a)):]
fp=base/'System/Arcade/ArcadeCombatSystem.FirstPerson.cs'
s=fp.read_text(encoding='utf-8-sig')
s=span(s,'        /// <summary>只保存当前触屏','        private void ClearArcadeInput')
s=span(s,'        /// <summary>镜头只水平转向','        private bool ReadArcadeFiring')
s=span(s,'        /// <summary>输入以视线为前方','    }\n}')
s=s.replace('            mobileFirstPersonFireHeld = false;\n','').replace('视角切换及暂停后的旧持续按压须释放一次','暂停及模式切换后的旧持续按压须释放一次').replace('共享旧按压释放闸门；触屏第一人称只传独立按钮，滑屏或右摇杆不得夹带射击','共享旧按压释放闸门；暂停恢复后先释放，再接受新开火').replace('同时观察仍按着的触点；新视角不能把旧摇杆手指的 Began 当成新滑屏','清理残留触点及技能脉冲，避免暂停恢复后延续旧操作')
fp.write_text(s,encoding='utf-8',newline='\r\n');fp.rename(fp.with_name('ArcadeCombatSystem.Input.cs'));fp.with_suffix('.cs.meta').rename(fp.with_name('ArcadeCombatSystem.Input.cs.meta'))
def combat(s):
 s=re.sub(r'            if \(definition.Kind == global::ArcadeDropKind.FirstPerson &&\n                !TryBeginFirstPerson\(runtime, definition, out error\)\) return false;\n','',s)
 s=span(s,'                case ArcadeHudActionKind.ExitFirstPerson:','            }\n        }')
 s=span(s,'            // 死亡/区域变化可以来自本帧之外','            var app = this.GetModel<AppStateModel>();')
 s=block(s,'            if (runtime.FirstPersonRemaining > 0 && !runtime.IslandFirstPersonSuspended &&')
 s=block(s,'            if (runtime.FirstPersonRemaining > 0)')
 s='\n'.join(l for l in s.split('\n') if not any(x in l for x in ['EndFirstPerson(', 'ReadFirstPersonInput(', 'bool firstPerson =', 'if (firstPerson', 'case global::ArcadeDropKind.FirstPerson:', 'FirstPersonSeconds =']))
 return s.replace('冲刺动作只读取当前移动和朝向；第一人称镜头增量仍由正常帧更新消费一次','冲刺动作只读取当前移动和朝向，不重复消费本帧操作')
edit('System/Arcade/ArcadeCombatSystem.cs',combat)
for name in ['IslandRounds','RoundLayouts','SlayerMart','Treasure']:
 def clean(s):
  if '        private void SuspendIslandFirstPerson' in s:
   s=block(s,'        /// <summary>只释放地图相机租约')
  while '            if (runtime.FirstPersonRemaining' in s:s=block(s,'            if (runtime.FirstPersonRemaining')
  s=s.replace('SuspendIslandFirstPerson(runtime);','')
  return '\n'.join(l for l in s.split('\n') if not any(x in l for x in ['FirstPersonRemaining','IslandFirstPersonSuspended','else EndFirstPerson']))
 edit('System/Arcade/ArcadeCombatSystem.'+name+'.cs',clean)
edit('System/Arcade/ArcadeRunSystem.SlayerChallenge.cs',lambda s:s.replace('entry.FirstPersonRemaining=actual.FirstPersonRemaining;',''))
for file in ['Model/Arcade/ArcadeCombatModel.cs','Model/Arcade/ArcadeRunContracts.cs']:
 edit(file,lambda s:re.sub(r'        /// <summary>[^\n]*</summary>\n        public (?:float|bool) (?:FirstPersonRemaining|FirstPersonSeconds|IslandFirstPersonSuspended)[^\n]*\n','',s).replace('RetryCompletion, ExitFirstPerson, FieldUpgrade, DonateLife','RetryCompletion, FieldUpgrade = 5, DonateLife = 6'))
edit('Utility/Save/ArcadeRunFileStore.cs',lambda s:s.replace(' || !ValidTimer(value.FirstPersonRemaining)',''))
def view(s):
 s='\n'.join(l for l in s.split('\n') if not any(x in l for x in ['bool IsFirstPersonActive => false','void EndFirstPerson() { }','void ApplyFirstPersonLook(Vector2 delta) { }','bool TryReadFirstPersonAim(out Vector2 aim) {','void PlayFirstPersonPickup() { }','bool TryBeginFirstPerson(out string error) {','firstPersonProjectileHeights =','private long firstPersonMapInstanceId','            EndFirstPerson();','firstPersonProjectileHeights.Clear();','firstPersonProjectileHeights.Remove(id);']))
 s=span(s,'                if (IsFirstPersonActive','                else if (hasLastShotMuzzle)').replace('                else if (hasLastShotMuzzle)','                if (hasLastShotMuzzle)')
 s=s.replace('            if (firstPersonProjectileHeights.TryGetValue(id, out var height))\n                authoritative = mapRoot.TransformPoint(World(position, height));\n','')
 s=span(s,'        public bool IsFirstPersonActive =>','        /// <summary>沿用浮动提示显示实际补充次数')
 return s
edit('Controller/Arcade/ArcadeCombatViewController.cs',view)
def camera(s):
 s=span(s,'        public bool IsArcadeFirstPerson','        protected override void OnInit()')
 s=s.replace('            if (mode != PlayerCameraMode.FirstPerson)\n                arcadeFirstPersonMapInstanceId = 0;\n','')
 s=s.replace('IsArcadeFirstPerson(mapInstanceId) ? 0 : ','')
 s=s.replace('                if (mapInstanceId == restoreFirstPerson && Mode == PlayerCameraMode.FirstPerson)\n                    arcadeFirstPersonMapInstanceId = restoreFirstPerson;\n','')
 s=re.sub(r'        /// <summary>(?:候选加载失败时恢复旧地图的第一人称租约。|临时街机视角所属地图；0 为无租约，解绑或其他视角接管后立即失效。)</summary>\n','',s)
 return '\n'.join(l for l in s.split('\n') if not any(x in l for x in ['arcadeFirstPersonMapInstanceId','previousArcadeFirstPersonMapInstanceId']))
edit('System/Player/PlayerCameraSystem.cs',camera)
def provider(s):
 s=span(s,'            // 单人相机道具仅来自客户端专表','            foreach (var row in tables.TbArcadeDrop.DataList.Concat')
 return s.replace('row.Kind == global::ArcadeDropKind.Powerup ||\n                            row.Kind == global::ArcadeDropKind.FirstPerson','row.Kind == global::ArcadeDropKind.Powerup')
edit('Utility/Config/LubanRuntimeConfigProvider.cs',provider)
edit(root/'Config/Defines/enums.xml',lambda s:s.replace('        <var name="FirstPerson" value="7"/>\n',''))
