using System;
using System.Linq;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FirepowerLoop.Tests.EditMode.Flow
{
    public sealed class OnlineRoomMemberPreviewTests
    {
        private const string BoxPath = "Assets/Game/Prefabs/UI/Online/Box/OnlineRoomMember.prefab";
        private const string StagePath = "Assets/Game/Prefabs/Presentation/OnlineRoomMemberStage.prefab";
        private GameObject first;
        private GameObject second;

        [TearDown]
        public void Cleanup()
        {
            // EditMode 的普通 MonoBehaviour 不接收运行时启停回调；测试拥有显式创建的舞台。
            foreach (var box in new[] { first, second })
                if (box != null && PreviewType() != null)
                    foreach (var component in box.GetComponentsInChildren(PreviewType(), true))
                        Call(component, "ReleasePreview");
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
        }

        [Test]
        public void Stage_UsesActualDefaultPlayerMeshAndOnlyIdlePresentation()
        {
            var stage = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            Assert.That(stage, Is.Not.Null, "缺少正式模型展示舞台");
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var source = player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Character_Male_Hoodie");
            var target = stage.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.gameObject.activeSelf);
            Assert.That(target.sharedMesh, Is.SameAs(source.sharedMesh));
            Assert.That(target.sharedMaterial, Is.SameAs(source.sharedMaterial));
            Assert.That(stage.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(stage.GetComponentsInChildren<MonoBehaviour>(true).All(c => c.GetType().Namespace == "UnityEngine.Rendering.Universal"), Is.True);
            var animator = stage.GetComponentInChildren<Animator>(true);
            Assert.That(animator.avatar, Is.EqualTo(player.transform.Find("PlayerHumanoid").GetComponent<Animator>().avatar));
            Assert.That(animator.runtimeAnimatorController.animationClips.Select(c => c.name), Is.EquivalentTo(new[] { "HumanM@Idle01" }));
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime), "待机展示不应被游戏时间暂停冻结");
        }

        [Test]
        public void Box_BindsCharacterImageAndFormalStageWithoutBlockingInput()
        {
            var box = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);
            var image = box.GetComponent<ReferenceCollector>().GetObject("CharacterImage") as RawImage;
            Assert.That(image, Is.Not.Null, "生成的成员 View 需要角色图像引用");
            Assert.That(image.raycastTarget, Is.False);
            Assert.That(typeof(OnlineRoomMemberBoxUI).GetField("CharacterImage").GetValue(box.GetComponent<OnlineRoomMemberBoxUI>()), Is.EqualTo(image));
            Assert.That(PreviewType(), Is.Not.Null);
            var component = image.GetComponent(PreviewType());
            Assert.That(component, Is.Not.Null);
            Assert.That(PreviewType().GetField("stagePrefab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component), Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(StagePath)));
            var name = box.GetComponent<OnlineRoomMemberBoxUI>().DisplayNameText;
            Assert.That(name.rectTransform.rect.height, Is.GreaterThanOrEqualTo(name.GetPreferredValues("林间风", name.rectTransform.rect.width, 1000).y), "中文姓名不应被省略模式因高度不足隐藏");
        }

        [Test]
        public void RepeatedBeginAndRelease_ReuseThenDestroyOwnedResources()
        {
            first = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath));
            var component = Preview(first);
            Call(component, "BeginPreview");
            var image = first.GetComponentInChildren<RawImage>(true);
            var texture = image.texture as RenderTexture;
            var stage = OwnedStage(component);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.IsCreated(), Is.True);
            Call(component, "BeginPreview");
            Assert.That(image.texture, Is.SameAs(texture));
            Assert.That(OwnedStage(component), Is.SameAs(stage));
            Call(component, "ReleasePreview");
            Call(component, "ReleasePreview");
            Assert.That(image.texture, Is.Null);
            Assert.That(stage == null, Is.True);
            Assert.That(texture == null, Is.True);
        }

        [Test]
        public void HiddenBox_ReleasesPreviewAndCanCreateFreshResources()
        {
            first = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath));
            var component = Preview(first);
            Call(component, "BeginPreview");
            var original = OwnedStage(component);
            first.SetActive(false);
            // 显式验证实际运行时隐藏会调用的 OnDisable，避免测试依赖编辑器回调差异。
            PreviewType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
            Assert.That(original == null, Is.True);
            first.SetActive(true);
            Call(component, "BeginPreview");
            Assert.That(OwnedStage(component), Is.Not.Null);
            Assert.That(first.GetComponentInChildren<RawImage>(true).texture, Is.Not.Null);
        }

        [Test]
        public void TwoMembers_HaveIndependentTexturesAndNonOverlappingCameraFrusta()
        {
            first = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath));
            second = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath));
            var a = Preview(first);
            var b = Preview(second);
            Call(a, "BeginPreview");
            Call(b, "BeginPreview");
            Assert.That(first.GetComponentInChildren<RawImage>(true).texture, Is.Not.SameAs(second.GetComponentInChildren<RawImage>(true).texture));
            var camera = OwnedStage(a).GetComponentInChildren<Camera>(true);
            var otherMesh = OwnedStage(b).GetComponentsInChildren<Renderer>(true).Single(r => r.gameObject.activeInHierarchy);
            Assert.That(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), otherMesh.bounds), Is.False);
        }

        [TestCase(1920, 1080)]
        [TestCase(2400, 1080)]
        public void FourMembers_FitInsideLandscapeViewport(int width, int height)
        {
            first = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas));
            first.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)first.transform;
            root.sizeDelta = new Vector2(width, height);
            second = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab"), root);
            var ui = second.GetComponent<OnlineRoomUI>();
            ui.JoinedRoot.SetActive(true);
            ui.MemberList.totalCount = 4;
            ui.MemberList.RefreshCells();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)second.transform);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)ui.MemberList.transform);
            var viewport = (RectTransform)ui.MemberList.transform.parent;
            var corners = new Vector3[4];
            foreach (var row in ui.MemberList.GetDraggableItems())
            {
                var image = row.GetComponentInChildren<RawImage>(true);
                Assert.That(image, Is.Not.Null, "每个成员应显示实际模型");
                Assert.That(image.rectTransform.rect.height, Is.GreaterThanOrEqualTo(350f));
                ((RectTransform)row).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = viewport.InverseTransformPoint(corner);
                    Assert.That(point.x, Is.InRange(viewport.rect.xMin - 1f, viewport.rect.xMax + 1f));
                    Assert.That(point.y, Is.InRange(viewport.rect.yMin - 1f, viewport.rect.yMax + 1f));
                }
            }
        }

        private static Type PreviewType() => typeof(OnlineRoomUICtrl).Assembly.GetType("Game.OnlineRoomMemberPreview");
        private static Component Preview(GameObject box)
        {
            Assert.That(PreviewType(), Is.Not.Null, "缺少角色预览资源所有者");
            var component = box.GetComponentInChildren(PreviewType(), true);
            Assert.That(component, Is.Not.Null);
            return component;
        }
        private static void Call(Component component, string method) => PreviewType().GetMethod(method).Invoke(component, null);
        private static GameObject OwnedStage(Component component) => (GameObject)PreviewType().GetField("stageInstance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
    }
}
