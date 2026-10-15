using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>用正式角色验证整套造型、装备恢复与第一人称绑定；不修改产品资源。</summary>
    public sealed class PlayerAppearanceTests
    {
        static bool Select(ClothingAppearanceView view, int index)
        {
            var method = typeof(ClothingAppearanceView).GetMethod("ApplyWholeBody");
            Assert.That(method, Is.Not.Null, "外观 View 尚未提供整套造型入口");
            return (bool)method.Invoke(view, new object[] { index });
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
        public void WholeBodyUsesExistingSkeletonAndMatchingFirstPersonArms(int index)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var cameraObject = new GameObject("AppearanceCamera", typeof(Camera));
            PlayerCameraCtrl camera = null;
            try
            {
                var view = player.GetComponent<PlayerView>();
                view.Animator.Rebind(); view.Animator.Update(.1f);
                var appearance = view.ClothingAppearance;
                appearance.Apply("Top_OliveUtility", "Pants_TanCargo");
                var oldSleeves = appearance.CurrentSleeves;
                Assert.That(Select(appearance, index), Is.True);
                Assert.That(view.Animator.transform.Find("ClothingParts").gameObject.activeSelf, Is.False);
                int visible = 0;
                foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.name.StartsWith("Character_"))
                    {
                        if (skin.gameObject.activeSelf)
                        {
                            // 激活节点不代表可绘制：整套网格必须同时启用 Renderer。
                            Assert.That(skin.enabled, Is.True, skin.name);
                            Assert.That(skin.forceRenderingOff, Is.False, skin.name);
                            visible++;
                        }
                        Assert.That(skin.bones.Length, Is.EqualTo(49));
                    }
                Assert.That(visible, Is.EqualTo(1));
                Assert.That(appearance.CurrentSleeves, Is.Not.Null.And.Not.SameAs(oldSleeves));
                camera = new PlayerCameraCtrl(cameraObject.GetComponent<Camera>(), player.transform, view.CameraProfile, view.ShoulderCameraProfile);
                camera.ApplyMode(PlayerCameraMode.FirstPerson, 0, 0);
                var arms = cameraObject.transform.Find("PlayerFirstPersonRig").GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(arms.sharedMesh, Is.SameAs(appearance.CurrentSleeves));
                var baked = new Mesh();
                try { arms.BakeMesh(baked); Assert.That(baked.bounds.size.magnitude, Is.InRange(.1f, 5f)); }
                finally { Object.DestroyImmediate(baked); }
                camera.ApplyMode(PlayerCameraMode.Shoulder, 0, 0);
                foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.name.StartsWith("Character_") && skin.gameObject.activeSelf)
                    {
                        Assert.That(skin.enabled, Is.True, skin.name);
                        Assert.That(skin.forceRenderingOff, Is.False, skin.name);
                    }
                camera.ApplyMode(PlayerCameraMode.FirstPerson, 0, 0);
                // 退出第一人称会释放旧手臂，重新进入后读取新实例。
                arms = cameraObject.transform.Find("PlayerFirstPersonRig").GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(Select(appearance, 0), Is.True);
                camera.LateUpdate(PlayerCameraMode.FirstPerson, 0, 0);
                Assert.That(arms.sharedMesh, Is.SameAs(oldSleeves));
            }
            finally { camera?.Dispose(); Object.DestroyImmediate(player); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void ResetRestoresLatestClothesAndInvalidIndexPreservesAppearance()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            try
            {
                var view = player.GetComponent<PlayerView>(); var appearance = view.ClothingAppearance;
                Assert.That(Select(appearance, 8), Is.True);
                var arms = appearance.CurrentSleeves;
                Assert.That(Select(appearance, -1), Is.False);
                Assert.That(Select(appearance, 10), Is.False);
                Assert.That(appearance.CurrentSleeves, Is.SameAs(arms));
                Assert.That(appearance.Apply("Top_RedHoodie", "Pants_BlueDenim", "Head_OliveCap"), Is.True);
                Assert.That(appearance.CurrentSleeves, Is.SameAs(arms));
                Assert.That(Select(appearance, 0), Is.True);
                var parts = view.Animator.transform.Find("ClothingParts");
                Assert.That(parts.gameObject.activeSelf, Is.True);
                foreach (var name in new[] { "Top_RedHoodie", "Pants_BlueDenim", "Head_OliveCap" })
                    Assert.That(parts.Find(name).gameObject.activeSelf, Is.True, name);
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void PortraitPrefabSupportsTheSameNineWholeBodyAppearances()
        {
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/OutfitPrototype/ClothingCharacterPreview.prefab"));
            try { for (int i = 1; i <= 9; i++) Assert.That(Select(actor.GetComponent<ClothingAppearanceView>(), i), Is.True, $"造型 {i}"); }
            finally { Object.DestroyImmediate(actor); }
        }
    }
}
