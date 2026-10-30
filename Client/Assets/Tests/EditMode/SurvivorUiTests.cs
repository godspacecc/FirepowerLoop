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
