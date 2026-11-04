using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>挑战正式界面引用与弹药快照渲染检查，不制作或修复资源。</summary>
    public sealed class ChallengeUiTests
    {
        [Test] public void UnfinishedChallengeCardIsHiddenInPublicMenu()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            var ui=root.GetComponent<StartUI>();
            var field=typeof(StartUI).GetField("BtnChallenge");
            Assert.That(field,Is.Not.Null,"挑战入口未生成");
            var button=field.GetValue(ui) as Button;
            Assert.That(button,Is.Not.Null);
            Assert.That(button.transform.parent.gameObject.activeSelf, Is.False);
            Assert.That(button.transform.IsChildOf(ui.ArcadeModePanel.transform),Is.True);
            Assert.That(root.GetComponentsInChildren<RectTransform>(true).Count(t=>t.name.StartsWith("ModeCard_")),Is.EqualTo(4));
            Assert.That(button.GetComponentsInChildren<LocalizedText>(true).Any(t=>t.Key=="ui.mode.challenge"),Is.True);
        }

        [Test] public void HudReadsAmmoAndReloadThenClearsStoppedSession()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Challenge/ChallengeHud.prefab");
            Assert.That(prefab,Is.Not.Null,"正式挑战HUD尚未制作");
            GameObject instance=null;
            var architectureField=typeof(QFramework.Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
            var previousArchitecture=architectureField.GetValue(null);
            var previousLoader=ResourceLoader.Current;
            var cacheField=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
            var previousCache=cacheField.GetValue(null);
            try
            {
                ResourceLoader.Use(new LocalizationAssetResourceLoader());LocalizationLanguages.ResetForTests();
                var architecture=new Game();architectureField.SetValue(null,architecture);
                architecture.RegisterModel(new LocalizationModel());
                architecture.RegisterUtility(new LocalizationUtility());
                instance=Object.Instantiate(prefab);
                var view=instance.GetComponent<BaseUI>();
                Assert.That(view,Is.Not.Null);
                var type=typeof(StartUICtrl).Assembly.GetType("Game.ChallengeHudUICtrl");
                Assert.That(type,Is.Not.Null);
                var render=type.GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Static);
                Assert.That(render,Is.Not.Null);
                var gun=new ChallengeWeaponState("challenge.rifle",3,5,2f,.1f);
                var model=new ChallengeModel{Active=true,Weapons=new[]{gun}};
                var ammo=(TextMeshProUGUI)view.GetType().GetField("AmmoText").GetValue(view);
                var status=(TextMeshProUGUI)view.GetType().GetField("ReloadText").GetValue(view);
                var content=(RectTransform)view.GetType().GetField("Content").GetValue(view);
                gun.TryFire();gun.TryReload();gun.Advance(.5f);
                render.Invoke(null,new object[]{view,model.Snapshot});
                Assert.That(ammo.text,Does.Contain("02 / 5"));
                Assert.That(status.text,Does.Contain("1.5"));
                Assert.That(gun.Reserve,Is.EqualTo(5));
                gun.Advance(1.5f);render.Invoke(null,new object[]{view,model.Snapshot});
                Assert.That(ammo.text,Does.Contain("03 / 4"));
                Assert.That(status.text,Does.Not.Contain("1.5"));
                model.Active=false;render.Invoke(null,new object[]{view,model.Snapshot});
                Assert.That(content.gameObject.activeSelf,Is.False);
                Assert.That(prefab.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g=>!g.raycastTarget),Is.True);
            }
            finally
            {
                if(instance!=null)Object.DestroyImmediate(instance);
                architectureField.SetValue(null,previousArchitecture);
                ResourceLoader.Use(previousLoader);cacheField.SetValue(null,previousCache);
            }
        }
    }
}
