using System.Linq;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Tests.EditMode
{
    /// <summary>用正式骨架和真实胶囊碰撞复现脚底悬浮，检查表现修正不改动权威位置。</summary>
    public sealed class PlayerGroundContactTests
    {
        private GameObject player;
        private GameObject floor;
        private PlayerView view;
        private Animator animator;
        private PlayableGraph graph;

        [SetUp]
        public void SetUp()
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, 999.5f, 0);
            floor.transform.localScale = new Vector3(10, 1, 10);
            player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            player.transform.position = new Vector3(0, 1001, 0);
            view = player.GetComponent<PlayerView>();
            animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.SetManualWeaponPoseSampling(true);
            Physics.SyncTransforms();
            for (var i = 0; i < 120; i++) view.CharacterController.Move(Vector3.down * .02f);
            Assert.That(view.CharacterController.isGrounded, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (graph.IsValid()) graph.Destroy();
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(floor);
        }

        [TestCase("HumanM@Idle01")]
        [TestCase("HumanM@Run01_Forward")]
        public void GroundedAnimation_KeepsSolesOnFloorWithoutMovingController(string clipName)
        {
            var rootPosition = player.transform.position;
            var playable = BindClip(clipName);
            foreach (var fraction in new[] { 0f, .25f, .4f, .75f })
            {
                playable.SetTime(playable.GetAnimationClip().length * fraction);
                graph.Evaluate(0);
                Present();
                AssertSoleOnFloor("Base_Feet");
                Assert.That(player.transform.position, Is.EqualTo(rootPosition));
                var visualPosition = animator.transform.localPosition;
                Present();
                Assert.That(Vector3.Distance(animator.transform.localPosition, visualPosition), Is.LessThan(.001f),
                    "重复校准不能累积偏移");
            }
        }

        [Test]
        public void ChangingToWholeBodyAppearance_UsesVisibleSoles()
        {
            BindClip("HumanM@Idle01");
            graph.Evaluate(.4f);
            Present();
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.gameObject.SetActive(skin.name == "Character_Male_Hoodie");
            var visible = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(skin => skin.name == "Character_Male_Hoodie");
            visible.enabled = true;
            Present();
            AssertSoleOnFloor("Character_Male_Hoodie");
        }

        [Test]
        public void AirborneCharacter_RestoresAuthoredVisualPose()
        {
            var original = animator.transform.localPosition;
            BindClip("HumanM@Idle01");
            graph.Evaluate(.4f);
            Present();
            view.CharacterController.Move(Vector3.up);
            Assert.That(view.CharacterController.isGrounded, Is.False);
            Present();
            Assert.That(animator.transform.localPosition, Is.EqualTo(original));
        }

        [Test]
        public void DeathPresentation_RestoresAuthoredVisualPose()
        {
            var original = animator.transform.localPosition;
            BindClip("HumanM@Idle01");
            graph.Evaluate(.4f);
            Present();
            view.SetAlivePresentation(false);
            Present();
            Assert.That(animator.transform.localPosition, Is.EqualTo(original));
        }

        private AnimationClipPlayable BindClip(string clipName)
        {
            graph = PlayableGraph.Create("PlayerGroundContactTest");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var clip = animator.runtimeAnimatorController.animationClips.First(c => c.name == clipName);
            var playable = AnimationClipPlayable.Create(graph, clip);
            AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
            graph.Play();
            return playable;
        }

        private void Present()
        {
            typeof(PlayerView).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, null);
        }

        private void AssertSoleOnFloor(string rendererName)
        {
            var skin = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r => r.name == rendererName);
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                var sole = mesh.vertices.Min(vertex => skin.transform.TransformPoint(vertex).y);
                Assert.That(sole - 1000f, Is.InRange(-.01f, .01f), "动画脚底必须贴近真实支撑面");
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
