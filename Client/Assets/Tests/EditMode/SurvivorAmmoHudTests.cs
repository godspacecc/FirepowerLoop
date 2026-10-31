using System.Reflection;
using Game;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>真实主角头顶弹量的显示、容量变化与清理，不写正式资源。</summary>
    public sealed class SurvivorAmmoHudTests
    {
        [Test] public void AmmoUsesPlainNumbersAndClearsWithoutHidingReload()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            try
            {
                var view=go.GetComponent<PlayerView>();
                var method=typeof(PlayerView).GetMethod("SetAmmoHud");
                Assert.That(method,Is.Not.Null);
                var field=typeof(PlayerView).GetField("ammoText",BindingFlags.Instance|BindingFlags.NonPublic);
                var text=(TMP_Text)field.GetValue(view);Assert.That(text,Is.Not.Null);
                method.Invoke(view,new object[]{true,0,30});
                Assert.That(text.text,Is.EqualTo("0 / 30"));Assert.That(text.gameObject.activeInHierarchy,Is.True);
                method.Invoke(view,new object[]{true,12,33});Assert.That(text.text,Is.EqualTo("12 / 33"));
                Assert.That(text.canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true),Is.Empty);
                method.Invoke(view,new object[]{false,0,0});Assert.That(text.gameObject.activeInHierarchy,Is.False);
                method.Invoke(view,new object[]{true,30,30});Assert.That(text.text,Is.EqualTo("30 / 30"));
                view.SetAlivePresentation(false,false);Assert.That(text.gameObject.activeInHierarchy,Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
