using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>共用拾取表现缩放不能移动拾取根或重复累积。</summary>
    public sealed class ArcadeTreasureRewardVisualTests
    {
        private GameObject root;

        [SetUp] public void Setup() => root = new GameObject("RewardVisualTest");
        [TearDown] public void Cleanup() => Object.DestroyImmediate(root);

        [Test] public void RoomDropSizingKeepsRootAndDoesNotAccumulateAcrossRefreshes()
        {
            var method = typeof(ResourcePickupView).GetMethod("FitArcadeVisual");
            Assert.That(method, Is.Not.Null);
            foreach (string name in new[] { "ArcadeDropBomb", "ArcadeDropDash", "ArcadeRifle", "ArcadePiercingGun", "ArcadePanzerschreck", "ArcadeGrenade" })
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"), root.transform);
                try
                {
                    instance.transform.position = new Vector3(2, .3f, 3);
                    var pickup = instance.GetComponent<ResourcePickupView>();
                    pickup.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/" + name + ".prefab"), "Prefabs/SceneObjects/" + name);
                    method.Invoke(pickup, new object[] { 1.1f });
                    var visual = instance.transform.Find("ArcadeDropVisual");
                    var scale = visual.localScale; var position = visual.localPosition;
                    var renderers = visual.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    Assert.That(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), Is.EqualTo(1.1f).Within(.01f), name);
                    Assert.That(bounds.min.y, Is.EqualTo(.3f).Within(.01f), name);
                    method.Invoke(pickup, new object[] { 1.1f });
                    Assert.That(Vector3.Distance(visual.localScale, scale), Is.LessThan(.001f), name);
                    Assert.That(Vector3.Distance(visual.localPosition, position), Is.LessThan(.001f), name);
                    Assert.That(instance.transform.position, Is.EqualTo(new Vector3(2, .3f, 3)), "不能移动领取根");
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }
    }
}
