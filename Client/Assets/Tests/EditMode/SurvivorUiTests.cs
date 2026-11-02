using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>只读取正式资源，验证反序列化后的生成View及本地化职责。</summary>
    public sealed class SurvivorUiTests
    {
        private const string Root="Assets/Game/Prefabs/UI/Survivor/";
        /// <summary>元素弹头条件、强度和三选一说明完整容纳于既有正式卡片。</summary>
        [Test] public void ElementDescriptionsFitExistingCardsInBothLanguages()
        {
            var go=Object.Instantiate(Bound<SurvivorUpgradeBoxUI>(Root+"Box/SurvivorUpgrade.prefab").gameObject);
            try
            {
                var ui=go.GetComponent<SurvivorUpgradeBoxUI>();
                foreach(var language in new[]{"zh-CN","en-US"})
                {
                    Assert.That(RuntimeJson.TryParseStringTable(System.IO.File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"),out var table),Is.True);
                    foreach(var kind in new[]{"Fire","Frost"})
                    {
                        string title=table["ui.survivor.modifier."+kind];
                        Assert.That(ui.TitleText.GetPreferredValues(title,ui.TitleText.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(ui.TitleText.rectTransform.rect.height),title);
                        foreach(var level in new[]{new[]{0,20},new[]{30,40}})
                        {
                            string value=string.Format(table["ui.survivor.modifier."+kind+".detail"],kind=="Fire"?3:2,level[0],level[1]);
                            Assert.That(ui.DetailText.GetPreferredValues(value,ui.DetailText.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(ui.DetailText.rectTransform.rect.height),value);
                        }
                    }
                }
            }finally{Object.DestroyImmediate(go);}
        }
        /// <summary>三类恢复首级与末级条件和比例完整容纳于正式卡片，不改变布局。</summary>
        [Test] public void RecoveryDescriptionsFitExistingCardsInBothLanguages()
        {
            var instance=Object.Instantiate(Bound<SurvivorUpgradeBoxUI>(Root+"Box/SurvivorUpgrade.prefab").gameObject);
            try
            {
                var ui=instance.GetComponent<SurvivorUpgradeBoxUI>();
                foreach(var language in new[]{"zh-CN","en-US"})
                {
                    Assert.That(RuntimeJson.TryParseStringTable(System.IO.File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"),out var table),Is.True);
                    foreach(var row in new[]{new[]{"RecoveryStill","Still","2","1","5"},new[]{"RecoverySafe","Safe","5","0.6","3"},new[]{"RecoveryAlways","Always","0","0.3","1.5"}})
                    {
                        var title=table["ui.survivor.upgrade."+row[0]+".title"];
                        Assert.That(ui.TitleText.GetPreferredValues(title,ui.TitleText.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(ui.TitleText.rectTransform.rect.height),title);
                        foreach(var level in new[]{new[]{"0",row[3]},new[]{"1.2",row[4]}})
                        {
                            var value=string.Format(table["ui.survivor.upgrade.Recovery"+row[1]+".detail"],row[2],level[0],level[1]);
                            Assert.That(ui.DetailText.GetPreferredValues(value,ui.DetailText.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(ui.DetailText.rectTransform.rect.height),value);
                        }
                    }
                }
            }finally{Object.DestroyImmediate(instance);}
        }
        /// <summary>入口引用必须由正式生成器反序列化回绑，静态标签不混入动态余额。</summary>
        [Test] public void GoldEntryHasGeneratedReferencesAndDynamicBalance()
        {
            var ui=Bound<SurvivorEntryUI>(Root+"SurvivorEntry.prefab");
            Assert.That(ui.BalanceText,Is.Not.Null);Assert.That(ui.ErrorText,Is.Not.Null);
            Assert.That(ui.BtnStart,Is.Not.Null);Assert.That(ui.BtnBack,Is.Not.Null);
            Assert.That(ui.BalanceText.GetComponent<LocalizedText>(),Is.Null);
        }
        /// <summary>新强化只复用既有卡片，实际容量与换弹单位必须完整放入说明区。</summary>
        [Test] public void RifleUpgradeTextFitsExistingCard()
        {
            var instance=Object.Instantiate(Bound<SurvivorUpgradeBoxUI>(Root+"Box/SurvivorUpgrade.prefab").gameObject);
            try
            {
                var text=instance.GetComponent<SurvivorUpgradeBoxUI>().DetailText;
                foreach(var language in new[]{"zh-CN","en-US"})
                {
                    Assert.That(RuntimeJson.TryParseStringTable(System.IO.File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"),out var table),Is.True);
                    Assert.That(table["ui.survivor.upgrade.Magazine.title"],Is.Not.Empty);Assert.That(table["ui.survivor.upgrade.Reload.title"],Is.Not.Empty);
                    foreach(var value in new[]{string.Format(table["ui.survivor.upgrade.magazine_detail"],"30","33","3"),string.Format(table["ui.survivor.upgrade.reload_detail"],"2","1.8","0.2")})
                        Assert.That(text.GetPreferredValues(value,text.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(text.rectTransform.rect.height),value);
                }
            }finally{Object.DestroyImmediate(instance);}
        }
        /// <summary>已发布的拾取文案带实际距离和单位，并能放入现有卡片说明区域。</summary>
        [Test] public void PickupDetailsFitExistingCardInBothLanguages()
        {
            var source=Bound<SurvivorUpgradeBoxUI>(Root+"Box/SurvivorUpgrade.prefab");
            var instance=Object.Instantiate(source.gameObject);
            try
            {
                var text=instance.GetComponent<SurvivorUpgradeBoxUI>().DetailText;
                foreach(var language in new[]{"zh-CN","en-US"})
                {
                    Assert.That(RuntimeJson.TryParseStringTable(System.IO.File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"),out var table),Is.True);
                    foreach(var current in new[]{2d,4.75d})
                    {
                        var value=string.Format(table["ui.survivor.upgrade.pickup_detail"],current.ToString("0.##"),(current+.25).ToString("0.##"),"0.25");
                        Assert.That(value,Does.Contain(language=="zh-CN"?"米":" m"));Assert.That(value,Does.Contain("0.25"));
                        var size=text.GetPreferredValues(value,text.rectTransform.rect.width,float.PositiveInfinity);
                        Assert.That(size.y,Is.LessThanOrEqualTo(text.rectTransform.rect.height),value);
                    }
                }
            }finally{Object.DestroyImmediate(instance);}
        }
        [Test] public void WeaponModifierDescriptionsFitExistingCardsInBothLanguages()
        {
            var go=Object.Instantiate(Bound<SurvivorUpgradeBoxUI>(Root+"Box/SurvivorUpgrade.prefab").gameObject);
            try {
                var text=go.GetComponent<SurvivorUpgradeBoxUI>().DetailText;
                foreach(var language in new[]{"zh-CN","en-US"})
                {
                    Assert.That(RuntimeJson.TryParseStringTable(System.IO.File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"),out var table),Is.True);
                    foreach(var kind in new[]{"Pierce","Explosion","Split","Multishot"})
                    {
                        var value=string.Format(table["ui.survivor.modifier."+kind+".detail"],2,3,kind=="Explosion"?"1.8":"2",kind=="Explosion"?"2.2":kind=="Split"?"4":"3",45,55);
                        Assert.That(text.GetPreferredValues(value,text.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(text.rectTransform.rect.height),language+" "+kind);
                    }
                }
            }finally{Object.DestroyImmediate(go);}
        }
        /// <summary>时钟采用独立本地时间，跨分钟及日期刷新，同分钟不重复写文本。</summary>
        [Test] public void HudClockUpdatesAcrossMinutesWithoutRepeatedWrites()
        {
            var architectureField=typeof(QFramework.Architecture<Game>).GetField("mArchitecture",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var previous=architectureField.GetValue(null);GameObject instance=null;
            var previousLoader=ResourceLoader.Current;
            var cacheField=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
            var previousCache=cacheField.GetValue(null);
            try{
                ResourceLoader.Use(new LocalizationAssetResourceLoader());LocalizationLanguages.ResetForTests();
                var arch=new Game();architectureField.SetValue(null,arch);var languageModel=new LocalizationModel();arch.RegisterModel(languageModel);
                var loc=new LocalizationUtility(false);loc.LoadFromJson("zh-CN",System.IO.File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));arch.RegisterUtility(loc);
                instance=Object.Instantiate(Bound<SurvivorHudUI>(Root+"SurvivorHud.prefab").gameObject);
                var ctrl=new SurvivorHudUICtrl();typeof(BaseUICtrl).GetField("BaseUI",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ctrl,instance.GetComponent<SurvivorHudUI>());
                var method=typeof(SurvivorHudUICtrl).GetMethod("RenderLocalClock",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.That(method,Is.Not.Null,"本地时钟尚未接入");
                var field=typeof(SurvivorHudUI).GetField("ClockText");Assert.That(field,Is.Not.Null);
                var clock=(TMP_Text)field.GetValue(instance.GetComponent<SurvivorHudUI>());Assert.That(clock,Is.Not.Null);
                Assert.That(method.Invoke(ctrl,new object[]{new System.DateTime(2026,11,1,23,59,1),false}),Is.EqualTo(true));
                Assert.That(clock.text,Does.Contain("23:59"));
                Assert.That(method.Invoke(ctrl,new object[]{new System.DateTime(2026,11,1,23,59,59),false}),Is.EqualTo(false));
                Assert.That(method.Invoke(ctrl,new object[]{new System.DateTime(2026,11,2,0,0,0),false}),Is.EqualTo(true));
                Assert.That(clock.text,Does.Contain("00:00"));
                loc.LoadFromJson("en-US",System.IO.File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
                languageModel.SetLanguageCode("en-US");
                Assert.That(method.Invoke(ctrl,new object[]{new System.DateTime(2026,11,2,0,0,1),true}),Is.EqualTo(true),"语言切换必须强刷");
                Assert.That(clock.text,Does.StartWith("Local time"));
                arch.RegisterSystem(new MobileInputSystem());arch.RegisterModel(new MobileControlLayoutModel());
                arch.RegisterModel(new SurvivorModel{Active=true,Paused=true,State=new SurvivorRunState(10,5)});
                typeof(BaseUICtrl).GetField("_isActive",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ctrl,true);
                typeof(SurvivorHudUICtrl).GetField("renderedClockMinute",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ctrl,long.MinValue);
                var tick=typeof(SurvivorHudUICtrl).GetMethod("RefreshVisibleHud",BindingFlags.Instance|BindingFlags.NonPublic);
                float oldScale=Time.timeScale;
                try{
                    Time.timeScale=0;clock.text="paused";tick.Invoke(ctrl,null);
                    Assert.That(clock.text,Does.StartWith("Local time"),"暂停时仍使用真实时钟");
                    clock.text="same-frame";tick.Invoke(ctrl,null);Assert.That(clock.text,Is.EqualTo("same-frame"),"采样间隔内不重复写");
                }finally{Time.timeScale=oldScale;}
            }finally{if(instance!=null)Object.DestroyImmediate(instance);architectureField.SetValue(null,previous);ResourceLoader.Use(previousLoader);cacheField.SetValue(null,previousCache);}
        }
        /// <summary>时钟随HUD可见期回调一起解除，重复隐藏不会残留或误删其他订阅。</summary>
        [Test] public void HudClockUpdateIsReleasedOnHideAndUnload()
        {
            var instance=Object.Instantiate(Bound<SurvivorHudUI>(Root+"SurvivorHud.prefab").gameObject);
            var ctrl=new SurvivorHudUICtrl();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(BaseUICtrl).GetField("BaseUI",flags).SetValue(ctrl,instance.GetComponent<SurvivorHudUI>());
            var callback=(System.Action)System.Delegate.CreateDelegate(typeof(System.Action),ctrl,typeof(SurvivorHudUICtrl).GetMethod("RefreshVisibleHud",flags));
            var updates=(System.Collections.IList)typeof(LifeCycle).GetField("Updates",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            try{
                int before=updates.Count;LifeCycle.RegisterUpdate(callback);
                typeof(SurvivorHudUICtrl).GetField("joystickRegistered",flags).SetValue(ctrl,true);
                typeof(SurvivorHudUICtrl).GetMethod("OnHide",flags).Invoke(ctrl,null);
                Assert.That(updates.Contains(callback),Is.False);Assert.That(updates.Count,Is.EqualTo(before));
                typeof(SurvivorHudUICtrl).GetMethod("OnUnload",flags).Invoke(ctrl,null);
                Assert.That(updates.Count,Is.EqualTo(before));
            }finally{LifeCycle.UnregisterUpdate(callback);Object.DestroyImmediate(instance);}
        }
        /// <summary>密集金币最多复用十六个Box，到账显示与轻跳不修改真实余额，隐藏清理不重播。</summary>
        [Test] public void GoldFlightsAreBoundedArriveAndClearWithoutChangingBalance()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var architectureField=typeof(QFramework.Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=architectureField.GetValue(null);var previousLoader=ResourceLoader.Current;
            var cacheField=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);var previousCache=cacheField.GetValue(null);
            GameObject root=null;
            try{
                ResourceLoader.Use(new LocalizationAssetResourceLoader());LocalizationLanguages.ResetForTests();
                var arch=new Game();architectureField.SetValue(null,arch);arch.RegisterModel(new LocalizationModel());
                var loc=new LocalizationUtility(false);loc.LoadFromJson("zh-CN",System.IO.File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));arch.RegisterUtility(loc);
                var model=new SurvivorModel();arch.RegisterModel(model);
                root=new GameObject("GoldTestCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                var ui=Object.Instantiate(Bound<SurvivorHudUI>(Root+"SurvivorHud.prefab"),root.transform);
                var ctrl=new SurvivorHudUICtrl();typeof(BaseUICtrl).GetField("BaseUI",flags).SetValue(ctrl,ui);
                typeof(SurvivorHudUICtrl).GetMethod("OnInit",flags).Invoke(ctrl,null);typeof(BaseUICtrl).GetField("_isActive",flags).SetValue(ctrl,true);Canvas.ForceUpdateCanvases();
                Assert.That(ctrl.TryPlayGoldPickup(0,Vector2.zero),Is.False);
                Assert.That(ctrl.TryPlayGoldPickup(1,new Vector2(float.NaN,0)),Is.False);
                for(int i=0;i<100;i++){model.RunGold++;Assert.That(ctrl.TryPlayGoldPickup(1,new Vector2(Screen.width/2f,Screen.height/2f)),Is.True);}
                Assert.That(model.CollectedGold,Is.EqualTo(100));Assert.That(ui.ScoreText.text,Is.EqualTo(Loc.T("ui.survivor.hud.score","0")));
                Assert.That(ui.GoldFlightList.totalCount,Is.EqualTo(16));Assert.That(ui.GoldFlightList.transform.childCount,Is.EqualTo(16));
                var advance=typeof(SurvivorHudUICtrl).GetMethod("AdvanceGoldPickups",flags);
                advance.Invoke(ctrl,new object[]{.55f});Assert.That(ui.ScoreText.text,Is.EqualTo(Loc.T("ui.survivor.hud.score","100")));Assert.That(ui.GoldFlightList.totalCount,Is.Zero);
                advance.Invoke(ctrl,new object[]{.15f});Assert.That(ui.ScoreText.transform.localScale.x,Is.EqualTo(1.22f).Within(.001f));
                advance.Invoke(ctrl,new object[]{.5f});Assert.That(ui.ScoreText.transform.localScale.x,Is.EqualTo(1).Within(.001f));
                for(int i=0;i<20;i++){model.RunGold++;ctrl.TryPlayGoldPickup(1,new Vector2(-1000,-1000));}
                Assert.That(ui.GoldFlightList.transform.childCount,Is.EqualTo(16),"第二批复用原Box");
                typeof(SurvivorHudUICtrl).GetMethod("OnHide",flags).Invoke(ctrl,null);
                Assert.That(ui.GoldFlightList.totalCount,Is.Zero);Assert.That(ui.GoldFlightList.GetDraggableItems(),Is.Empty);
                Assert.That(ui.ScoreText.transform.localScale.x,Is.EqualTo(1));Assert.That(model.CollectedGold,Is.EqualTo(120));
                typeof(BaseUICtrl).GetField("_isActive",flags).SetValue(ctrl,false);Assert.That(ctrl.TryPlayGoldPickup(1,Vector2.zero),Is.False);
            }finally{if(root!=null)Object.DestroyImmediate(root);architectureField.SetValue(null,previous);ResourceLoader.Use(previousLoader);cacheField.SetValue(null,previousCache);}
        }
        [Test] public void Start_HasBoundSurvivorEntry()
        {
            var view=Bound<StartUI>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            Assert.That(view.BtnSurvivor,Is.Not.Null);
            Assert.That(view.BtnSurvivor.gameObject.activeSelf,Is.True);
            Assert.That(view.BtnSurvivor.GetComponentInChildren<LocalizedText>(true).Key,Is.EqualTo("ui.survivor.mode"));
        }
        [Test] public void Upgrade_ReferencesExternalGeneratedBox()
        {
            var host=Bound<SurvivorUpgradeUI>(Root+"SurvivorUpgrade.prefab");
            var box=Bound<SurvivorUpgradeBoxUI>(Root+"Box/SurvivorUpgrade.prefab");
            Assert.That(host.Cards.itemPrefab,Is.EqualTo(box.gameObject));
            Assert.That(AssetDatabase.GetAssetPath(host.Cards.itemPrefab),Is.EqualTo(Root+"Box/SurvivorUpgrade.prefab"));
        }
        [Test] public void HudAndResult_HaveAllGeneratedReferences()
        {
            Bound<SurvivorHudUI>(Root+"SurvivorHud.prefab");
            Bound<SurvivorResultUI>(Root+"SurvivorResult.prefab");
            Assert.That(new SurvivorHudUICtrl().BlocksGame,Is.False);
            Assert.That(new SurvivorResultUICtrl().BlocksGame,Is.True);
        }
        [Test] public void WeaponSlotsStayAboveTextFreeBottomExperienceAcrossAspectRatios()
        {
            var source=Bound<SurvivorHudUI>(Root+"SurvivorHud.prefab");
            var box=Bound<SurvivorWeaponSlotBoxUI>(Root+"Box/SurvivorWeaponSlot.prefab");Assert.That(source.WeaponSlots.itemPrefab,Is.EqualTo(box.gameObject));
            Assert.That(source.transform.Find("ExperiencePanel"),Is.Null);Assert.That(typeof(SurvivorHudUI).GetField("ExperienceText"),Is.Null);
            var instance=Object.Instantiate(source.gameObject);
            try
            {
                var ui=instance.GetComponent<SurvivorHudUI>();var root=(RectTransform)instance.transform;
                foreach(int width in new[]{1440,1920,2340})
                {
                    root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,1080);
                    Canvas.ForceUpdateCanvases();var xp=new Vector3[4];var slots=new Vector3[4];var bounds=new Vector3[4];
                    ((RectTransform)ui.ExperienceBar.transform).GetWorldCorners(xp);((RectTransform)ui.WeaponSlots.transform).GetWorldCorners(slots);root.GetWorldCorners(bounds);
                    Assert.That(xp[0].y,Is.EqualTo(bounds[0].y).Within(.1f));Assert.That(xp[1].y,Is.LessThan(slots[0].y));
                    Assert.That(xp[0].x,Is.EqualTo(bounds[0].x).Within(.1f));Assert.That(xp[2].x,Is.EqualTo(bounds[2].x).Within(.1f));
                }
            }finally{Object.DestroyImmediate(instance);}
        }
        [Test] public void TextOwnership_DynamicFieldsHaveNoLocalizedTextAndFixedTextUsesKeys()
        {
            foreach(var path in new[]{Root+"SurvivorHud.prefab",Root+"SurvivorResult.prefab",Root+"SurvivorUpgrade.prefab",Root+"Box/SurvivorUpgrade.prefab"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);Assert.That(prefab,Is.Not.Null,path);
                var view=(Component)prefab.GetComponent<BaseUI>()??prefab.GetComponent<BaseBoxUI>();
                Assert.That(view,Is.Not.Null,path);
                var dynamicTexts=new System.Collections.Generic.HashSet<TMP_Text>();
                foreach(var field in view.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                    if(field.GetValue(view) is TMP_Text text){dynamicTexts.Add(text);Assert.That(text.GetComponent<LocalizedText>(),Is.Null,path+" "+field.Name);}
                foreach(var text in prefab.GetComponentsInChildren<TMP_Text>(true))
                {
                    Assert.That(text.text??string.Empty,Does.Not.StartWith("ui."),path+" "+text.name);
                    if(dynamicTexts.Contains(text))continue;
                    var localized=text.GetComponent<LocalizedText>();Assert.That(localized,Is.Not.Null,path+" "+text.name);
                    Assert.That(localized.Key,Is.Not.Empty,path+" "+text.name);
                    Assert.That(localized.Texts,Is.Empty,path+" "+text.name);
                }
            }
        }
        [Test] public void HudBars_FillStaysInsideTrack()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"SurvivorHud.prefab");
            Assert.That(prefab,Is.Not.Null);
            var instance=Object.Instantiate(prefab);
            try
            {
                var view=instance.GetComponent<SurvivorHudUI>();
                view.HealthBar.value=.8f;view.ExperienceBar.value=.5f;
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)instance.transform);
                foreach(var slider in new[]{view.HealthBar,view.ExperienceBar})
                {
                    var track=(RectTransform)slider.fillRect.parent;
                    Assert.That(slider.fillRect.rect.width,Is.LessThanOrEqualTo(track.rect.width+.01f),slider.name);
                    Assert.That(slider.fillRect.rect.height,Is.EqualTo(track.rect.height).Within(.01f),slider.name);
                }
            }
            finally{Object.DestroyImmediate(instance);}
        }
        [Test] public void MobileMoveJoystick_KeepsArcadeSourceLayout()
        {
            var arcade=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponent<ArcadeHudUI>();
            var hud=Bound<SurvivorHudUI>(Root+"SurvivorHud.prefab");
            var field=typeof(SurvivorHudUI).GetField("MoveJoystickRoot");Assert.That(field,Is.Not.Null);
            var target=(GameObject)field.GetValue(hud);Assert.That(target,Is.Not.Null);
            Transform a=arcade.MoveJoystickRoot.transform,b=target.transform;
            while(a!=null)
            {
                Assert.That(b,Is.Not.Null);
                var left=(RectTransform)a;var right=(RectTransform)b;
                Assert.That(right.anchorMin,Is.EqualTo(left.anchorMin),a.name);
                Assert.That(right.anchorMax,Is.EqualTo(left.anchorMax),a.name);
                Assert.That(right.pivot,Is.EqualTo(left.pivot),a.name);
                Assert.That(right.anchoredPosition3D,Is.EqualTo(left.anchoredPosition3D),a.name);
                Assert.That(right.sizeDelta,Is.EqualTo(left.sizeDelta),a.name);
                Assert.That(right.localScale,Is.EqualTo(left.localScale),a.name);
                a=a.parent;b=b.parent;
            }
            Assert.That(hud.GetComponentsInChildren<UnityEngine.InputSystem.OnScreen.OnScreenStick>(true),Has.Length.EqualTo(1));
            Assert.That(typeof(SurvivorHudUI).GetField("MoveJoystick"),Is.Not.Null);
            Assert.That(typeof(SurvivorHudUI).GetField("MoveOpacity"),Is.Not.Null);
            Assert.That(typeof(SurvivorHudUI).GetField("JoystickLayoutArea"),Is.Not.Null);
        }
        /// <summary>所有生成字段都必须绑定当前Prefab自身对象，不能引用外部失效对象。</summary>
        private static T Bound<T>(string path) where T:Component
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);Assert.That(root,Is.Not.Null,path);
            Assert.That(root.GetComponent<ReferenceCollector>(),Is.Not.Null,path);
            var view=root.GetComponent<T>();Assert.That(view,Is.Not.Null,path);
            foreach(var field in typeof(T).GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
            {
                if(!typeof(Object).IsAssignableFrom(field.FieldType))continue;
                var value=field.GetValue(view) as Object;Assert.That(value!=null,Is.True,path+" "+field.Name);
                var transform=value is Component c?c.transform:(value as GameObject)?.transform;
                if(transform!=null)Assert.That(transform==root.transform||transform.IsChildOf(root.transform),Is.True,path+" "+field.Name);
            }
            return view;
        }
    }
}
