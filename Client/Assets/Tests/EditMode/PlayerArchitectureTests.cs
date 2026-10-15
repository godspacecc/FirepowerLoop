using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class PlayerArchitectureTests
    {
        [Test]
        public void PlayerPrefabHasNoWorldHealthBar()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.NotNull(prefab);
            Assert.That(prefab.transform.Find("HealthBarRoot"), Is.Null,
                "玩家生命由屏幕 HUD 展示，角色 Prefab 不再保留头顶血条。");
        }

        [Test]
        public void PlayerPrefabKeepsOnlyApprovedPresentationBehaviours()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");

            Assert.NotNull(prefab);
            Assert.NotNull(prefab.GetComponent<CharacterController>());
            Assert.NotNull(prefab.GetComponent<PlayerView>());
            var productBehaviours = prefab.GetComponents<MonoBehaviour>();
            Assert.That(productBehaviours, Has.Length.EqualTo(2));
            Assert.That(productBehaviours, Has.Exactly(1).TypeOf<PlayerView>());
            Assert.That(productBehaviours, Has.Exactly(1).TypeOf<DamageNumberEmitter>());
            Assert.NotNull(prefab.GetComponent<PlayerView>().CameraProfile);
            Assert.That(prefab.transform.Find("HealthBarRoot"), Is.Null,
                "玩家生命由屏幕 HUD 展示，角色 Prefab 不再保留头顶血条。");
        }

        [Test]
        public void PlayerPrefabUsesTrackedHumanoidPresentation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");

            Assert.NotNull(prefab);
            var visual = prefab.transform.Find("PlayerHumanoid");
            Assert.NotNull(visual);
            var animator = visual.GetComponent<Animator>();
            Assert.NotNull(animator);
            Assert.NotNull(animator.avatar);
            Assert.That(animator.avatar.isHuman, Is.True);
            Assert.NotNull(animator.runtimeAnimatorController);

            var visibleMeshes = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(visibleMeshes, Has.Exactly(1).Matches<SkinnedMeshRenderer>(renderer =>
                renderer.gameObject.activeSelf && renderer.enabled));
            Assert.That(AssetDatabase.GetAssetPath(visibleMeshes[0].sharedMesh),
                Does.StartWith("Assets/Game/Art/Player/"));
            Assert.That(prefab.transform.Find("Visual").gameObject.activeSelf, Is.False);
        }

        [Test]
        public void PlayerHumanoidReceivesFootstepAnimationEvents()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.NotNull(prefab);

            var visual = prefab.transform.Find("PlayerHumanoid");
            Assert.NotNull(visual);
            var receiver = visual.GetComponent("CharacterAnimationEventReceiver");
            Assert.NotNull(receiver);

            foreach (var eventName in new[] { "FootL", "FootR" })
            {
                var method = receiver.GetType().GetMethod(eventName,
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.NotNull(method, eventName);
                Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), eventName);
                Assert.That(method.GetParameters(), Is.Empty, eventName);
            }
        }

        [Test]
        public void EnemyHealthFillCalculationIsClamped()
        {
            Assert.That(EnemyView.CalculateHealthFill(50, 100), Is.EqualTo(0.5f));
            Assert.That(EnemyView.CalculateHealthFill(0, 100), Is.EqualTo(0f));
            Assert.That(EnemyView.CalculateHealthFill(150, 100), Is.EqualTo(1f));
            Assert.That(EnemyView.CalculateHealthFill(10, 0), Is.EqualTo(0f));
        }

        [Test]
        public void SpriteLessHealthFillShrinksRectTransform()
        {
            var root = new GameObject("HealthBarRoot", typeof(RectTransform));
            var fillObject = new GameObject(
                "Fill",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            fillObject.transform.SetParent(root.transform, false);

            try
            {
                var image = fillObject.GetComponent<Image>();
                image.sprite = null;

                HealthFillUtility.Apply(image, 0.37f);

                Assert.That(image.fillAmount, Is.EqualTo(0.37f).Within(0.001f));
                Assert.That(image.rectTransform.anchorMin.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(image.rectTransform.anchorMax.x, Is.EqualTo(0.37f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InputControllerUsesInputActionWithoutMonoBehaviour()
        {
            Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(PlayerInputCtrl)));
            var source = File.ReadAllText(
                "Assets/Game/Scripts/Controller/Player/PlayerInputCtrl.cs");

            Assert.That(source, Does.Contain("InputAction"));
            Assert.That(source, Does.Contain("2DVector"));
            Assert.That(source, Does.Contain("<Keyboard>/w"));
            Assert.That(source, Does.Contain("<Keyboard>/upArrow"));
            Assert.That(source, Does.Not.Contain("Keyboard.current"));
        }

        [Test]
        public void MoveAndCameraDriversAreArchitectureSystems()
        {
            Assert.IsTrue(typeof(QFramework.AbstractSystem).IsAssignableFrom(typeof(PlayerMoveSystem)));
            Assert.IsTrue(typeof(QFramework.AbstractSystem).IsAssignableFrom(typeof(PlayerCameraSystem)));
            Assert.IsTrue(typeof(QFramework.AbstractSystem).IsAssignableFrom(typeof(PlayerInputSystem)));
            Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(PlayerCameraCtrl)));
        }

        [Test]
        public void PlayerReadyEventOnlyCarriesUnitIdentity()
        {
            var fields = typeof(PlayerReadyEvent).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields, Has.Length.EqualTo(2));
            Assert.That(typeof(PlayerReadyEvent).GetProperty(nameof(PlayerReadyEvent.UnitId)), Is.Not.Null);
            Assert.That(typeof(PlayerReadyEvent).GetProperty(nameof(PlayerReadyEvent.MapInstanceId)), Is.Not.Null);
            foreach (var field in fields)
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType));
            }
        }

        [Test]
        public void PlayerInputBoundaryOwnsMoveAttackAndInteract()
        {
            var inputSource = File.ReadAllText(
                "Assets/Game/Scripts/Controller/Player/PlayerInputCtrl.cs");
            var inputSystemSource = File.ReadAllText(
                "Assets/Game/Scripts/System/Player/PlayerInputSystem.cs");
            var combatSource = File.ReadAllText(
                "Assets/Game/Scripts/System/Combat/CombatSystem.cs");
            var moveSource = File.ReadAllText(
                "Assets/Game/Scripts/System/Player/PlayerMoveSystem.cs");
            var interactionSource = File.ReadAllText(
                "Assets/Game/Scripts/System/Interaction/PlayerInteractionSystem.cs");

            Assert.That(inputSource, Does.Contain("\"PlayerMove\""));
            Assert.That(inputSource, Does.Contain("\"PlayerAttack\""));
            Assert.That(inputSource, Does.Contain("\"PlayerInteract\""));
            Assert.That(inputSource, Does.Contain("<Keyboard>/e"));
            Assert.That(inputSource, Does.Not.Contain("\"PlayerSearch\""));
            Assert.That(inputSource, Does.Not.Contain("<Keyboard>/f"));
            Assert.That(inputSystemSource, Does.Contain("PlayerAttackRequestedEvent"));
            Assert.That(inputSystemSource, Does.Contain("PlayerInteractRequestedEvent"));
            Assert.That(inputSystemSource, Does.Not.Contain("RequestSearch"));
            Assert.That(interactionSource, Does.Contain("RequestInteract"));
            Assert.That(interactionSource, Does.Contain("IPlayerSearchable"));
            Assert.That(interactionSource, Does.Contain("searchable.CanSearch"));
            Assert.That(interactionSource.IndexOf("searchable.Search()", System.StringComparison.Ordinal),
                Is.LessThan(interactionSource.IndexOf("current.Interact()", System.StringComparison.Ordinal)));
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(SearchContainerSystem)), Is.False);
            Assert.That(inputSystemSource, Does.Contain("input.IsEnabled"));
            Assert.That(combatSource, Does.Not.Contain("new InputAction"));
            Assert.That(moveSource, Does.Not.Contain("new PlayerInputCtrl"));
        }

        [Test]
        public void CombatUpdateDoesNotSearchSceneOrAllocateEnemyKeyList()
        {
            var combatSource = File.ReadAllText(
                "Assets/Game/Scripts/System/Combat/CombatSystem.cs");
            var taskExitSource = File.ReadAllText(
                "Assets/Game/Scripts/Controller/View/Map/TaskExitView.cs");

            Assert.That(combatSource, Does.Not.Contain("new InputAction"));
            Assert.That(combatSource, Does.Not.Contain("FindObjectOfType<PlayerView>"));
            Assert.That(combatSource, Does.Not.Contain("new List<int>(enemies.Keys)"));
            Assert.That(combatSource, Does.Not.Contain("GetSystem<InventorySystem>"));
            Assert.That(combatSource, Does.Not.Contain("GetSystem<ExpeditionFlowSystem>"));
            Assert.That(taskExitSource, Does.Not.Contain("LifeCycle.RegisterUpdate"));
        }

        [Test]
        public void PlayerCtrlDoesNotListenForMapCompletionOrSearchScene()
        {
            var source = File.ReadAllText("Assets/Game/Scripts/Controller/Player/PlayerCtrl.cs");

            Assert.That(source, Does.Not.Contain("MapLoadingCompletedEvent"));
            Assert.That(source, Does.Not.Contain("FindObject"));
            Assert.That(source, Does.Not.Contain("FindObjects"));
        }

        [Test]
        public void PlayerCtrlReleaseDoesNotRemoveWholeSession()
        {
            var source = File.ReadAllText("Assets/Game/Scripts/Controller/Player/PlayerCtrl.cs");

            Assert.That(source, Does.Not.Contain("RemoveMapInstance"));
            Assert.That(source, Does.Contain("Remove(activePlayer.UnitId"));
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

    }
}
