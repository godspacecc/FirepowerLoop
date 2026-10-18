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

        [Test]
        public void ArcadeSnapshots_GroundRaisedSpawnAndRegionTeleportBeforeAligningSoles()
        {
            var map = new GameObject("PlayerGroundContactMap");
            map.transform.position = new Vector3(0, 1000, 0);
            try
            {
                var controller = new ArcadeCombatViewController();
                var motor = new PlayerViewCtrl(view, 1, 1, new ViewCtrlRegistry(), new UnityViewInstanceProvider());
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ArcadeCombatViewController).GetField("mapRoot", flags).SetValue(controller, map.transform);
                typeof(ArcadeCombatViewController).GetField("playerMotor", flags).SetValue(controller, motor);
                var playable = BindClip("HumanM@Idle01");
                foreach (var teleport in new[] { false, true })
                foreach (var spawnHeight in new[] { .1f, .6f })
                {
                    view.CharacterController.enabled = false;
                    player.transform.position = new Vector3(0, 1000f + spawnHeight, 0);
                    view.CharacterController.enabled = true;
                    typeof(ArcadeCombatViewController).GetField("pendingRegionPosition", flags).SetValue(controller, teleport);
                    for (int frame = 0; frame < 8; frame++)
                    {
                        controller.SetPlayer(new Vector2(frame * .02f, 0), Vector2.up, true, false);
                        playable.SetTime(.4f); graph.Evaluate(0); Present();
                        Assert.That(view.CharacterController.isGrounded, Is.True, "正式街机快照必须落地，不能保留出生高度");
                        AssertSoleOnFloor("Base_Feet");
                        Assert.That(player.transform.position.x, Is.EqualTo(frame * .02f).Within(.001f));
                    }
                    controller.SetPlayer(Vector2.zero, Vector2.up, false, false);
                    controller.SetPlayer(Vector2.zero, Vector2.up, true, false);
                    graph.Evaluate(0); Present();
                    Assert.That(view.CharacterController.isGrounded, Is.True, "复活后同样恢复落地");
                    AssertSoleOnFloor("Base_Feet");
                }
            }
            finally { Object.DestroyImmediate(map); }
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
