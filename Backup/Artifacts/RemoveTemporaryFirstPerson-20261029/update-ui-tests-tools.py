from pathlib import Path
root=Path('E:/GitHub/FirepowerLoop')
def edit(f,fn):
 p=root/f;s=p.read_text(encoding='utf-8-sig');t=fn(s)
 if s!=t:p.write_text(t,encoding='utf-8',newline='\r\n')
def remove_test(s,name):
 i=s.index('        [Test]\n        public void '+name);j=s.index('{',i);n=1;k=j+1
 while n:n+=(s[k]=='{')-(s[k]=='}');k+=1
 return s[:i]+s[k:]
def layout(s):
 s=remove_test(s,'Editor_FirstPersonPreviewKeepsFireSeparateAndCancelDiscardsItsDraft')
 for text in [', "ExitFirstPersonPreview", "FirstPersonFirePreview"', ', preview.ExitFirstPersonPreview.transform, preview.FirstPersonFirePreview.transform', ', hud.BtnExitFirstPerson.transform, hud.FirstPersonFirePress.transform',', ui.ExitFirstPersonPreview, ui.FirstPersonFirePreview']:
  s=s.replace(text,'')
 s=s.replace('"InventoryPreview", "BuildPreview"','"ExitFirstPersonPreview", "FirstPersonFirePreview", "ModeButton", "ModeText", "InventoryPreview", "BuildPreview"')
 s=s.replace('EditorAndHud_ProjectSameControlsForBothModesAndInsetSafeArea','EditorAndHud_ProjectSameControlsForInsetSafeArea')
 s=s.replace('                foreach (var firstPerson in new[] { false, true })\n','')
 s=s.replace('                    if (firstPerson) preview.ModeButton.onClick.Invoke();\n','')
 s=s.replace('FirstPersonSeconds = firstPerson ? 10 : 0, ','').replace('(firstPerson ? 7 : 5)','5')
 s=s.replace('            Assert.That(legacy.MobileControlLayout.FirstPersonFire, Is.Not.Null);\n','')
 return s
edit('Client/Assets/Tests/EditMode/MobileControlLayoutTests.cs',layout)
def hud(s):
 s=remove_test(s,'FirstPersonTouchControls_ReleaseFireOnPauseAndRestoreAimOnExit')
 s=remove_test(s,'FirstPersonCountdown_DoesNotCoverConcurrentEffectsOnTablet')
 where=s.index('        [TestCase(false, false)]')
 contract='''        [Test]
        public void FormalHudExcludesRetiredCameraControls()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            try
            {
                var collector = root.GetComponent<ReferenceCollector>();
                foreach (var key in new[] { "BtnExitFirstPerson", "FirstPersonRoot", "FirstPersonFirePress", "TextFirstPerson" })
                {
                    Assert.That(collector.data.Any(item => item.key == key), Is.False, key);
                    Assert.That(typeof(ArcadeHudUI).GetField(key), Is.Null, key);
                }
                var ui = root.GetComponent<ArcadeHudUI>();
                Assert.That(ui.MoveJoystickRoot, Is.Not.Null);
                Assert.That(ui.AimJoystickRoot, Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

'''
 return s[:where]+contract+s[where:]
edit('Client/Assets/Tests/EditMode/ArcadeCompactHudTests.cs',hud)
edit('Client/Assets/Tests/EditMode/Flow/MobileInputAndScreenAdaptationTests.cs',lambda s:s.replace(', ui.BtnExitFirstPerson',''))
edit('Tools/Unity/ArcadeCompactHudAuthoring.cs',lambda s:'\n'.join(l for l in s.split('\n') if 'FirstPerson' not in l))
edit('Tools/Unity/ArcadeMobileLayoutAuthoring.cs',lambda s:s.replace(', hud.BtnExitFirstPerson, hud.FirstPersonFirePress.GetComponent<Button>()','').replace(', "ExitFirstPerson", "FirstPersonFire"','').replace(', "ui.arcade.first_person.exit", "ui.mobile_base.fire"',''))
def preview(s):
 s=s.replace('Path.GetFullPath("Temp/ArcadeMobileLayoutPreview")','Path.GetFullPath("../Backup/Artifacts/ArcadeMobileLayoutPreview")')
 s=s.replace('1920, 1080, false','1920, 1080').replace('2340, 1080, true','2340, 1080').replace('1440, 1080, false','1440, 1080')
 s=s.replace('                    if (spec.Item4) ui.ModeButton.onClick.Invoke();\n','').replace('(spec.Item4 ? "-first-person" : "-top-down")','"-top-down"')
 return s
edit('Tools/Unity/ArcadeMobileLayoutPreview.cs',preview)
for name in ['ArcadeMobileFirstPersonAuthoring.cs','ArcadeMobileLayoutFlowProbe.cs']:
 (root/'Tools/Unity'/name).unlink()
