using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>采用的任务箱只取已有纯显示子物体，不携带拾取触发器。</summary>
    public sealed class MissionSupplyVisualTests
    {
        [Test] public void SupplyTemplateUsesApprovedCrateWithoutPickupCollider()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab");
            var method=typeof(ArcadeCombatViewController).GetMethod("MissionSupplyTemplate",BindingFlags.NonPublic|BindingFlags.Static);
            Assert.That(method,Is.Not.Null);
            var template=(GameObject)method.Invoke(null,new object[]{prefab});
            Assert.That(template.name,Is.EqualTo("WeaponDeliveryCrate"));
            Assert.That(template.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(template.transform.localScale,Is.EqualTo(Vector3.one));
        }
    }
}
