using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Tests
{
    /// <summary>正式武器槽随权威弹药快照更新，并在步枪、无人机和空槽之间完整恢复。</summary>
    public sealed class SurvivorAmmoIconTests
    {
        [Test] public void SlotReflectsAmmoReloadAndReuseWithoutNumbers()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/Box/SurvivorWeaponSlot.prefab"));
            try {
                var box=go.GetComponent<SurvivorWeaponSlotBoxUI>();
                var render=typeof(SurvivorHudUICtrl).GetMethod("RenderWeaponSlot",BindingFlags.Static|BindingFlags.NonPublic);
                Assert.That(render,Is.Not.Null);
                var model=new SurvivorModel();
                Set(model,"RifleCapacity",30);Set(model,"RifleRounds",15);
                render.Invoke(null,new object[]{box,model,"Rifle"});
                var ring=go.transform.Find("AmmoRing").GetComponent<Image>();
                Assert.That(ring.fillAmount,Is.EqualTo(.5f).Within(.001f));
                Assert.That(go.GetComponentsInChildren<TMPro.TMP_Text>(true),Is.Empty);
                Set(model,"RifleReloadDuration",2f);Set(model,"RifleReloadRemaining",.5f);
                render.Invoke(null,new object[]{box,model,"Rifle"});
                Assert.That(ring.fillAmount,Is.EqualTo(.75f).Within(.001f));Assert.That(ring.color.b,Is.GreaterThan(ring.color.r));
                render.Invoke(null,new object[]{box,model,"Drone"});Assert.That(ring.gameObject.activeSelf,Is.False);
                render.Invoke(null,new object[]{box,model,"Rifle"});Assert.That(ring.gameObject.activeSelf,Is.True);
                render.Invoke(null,new object[]{box,model,null});Assert.That(ring.gameObject.activeSelf,Is.False);
                Assert.That(go.transform.Find("Icon").gameObject.activeSelf,Is.False);
            }finally{Object.DestroyImmediate(go);}
        }
        private static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    }
}
