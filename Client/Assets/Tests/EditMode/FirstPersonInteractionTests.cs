using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace Game.Tests
{
    public sealed class FirstPersonInteractionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var root in objects) Object.DestroyImmediate(root);
            objects.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void LookingAwayDoesNotSelectNearbyObject()
        {
            var target = Target(new Vector3(2f, 0f, 2f));
            Assert.That(Resolve(new Ray(Vector3.zero, Vector3.forward), target), Is.Null);
        }

        [Test]
        public void LookSelectsClosestEligibleObject()
        {
            var near = Target(new Vector3(0f, 0f, 2f));
            var far = Target(new Vector3(0f, 0f, 4f));
            Assert.That(Resolve(new Ray(Vector3.zero, Vector3.forward), far, near), Is.SameAs(near));
        }

        [Test]
        public void WallHidesObjectBehindIt()
        {
            var target = Target(new Vector3(0f, 0f, 3f));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(wall);
            wall.transform.position = new Vector3(0f, 0f, 1f);
            Physics.SyncTransforms();
            Assert.That(Resolve(new Ray(Vector3.zero, Vector3.forward), target), Is.Null);
        }

        [Test]
        public void RangeTriggerDoesNotMakeLookingBesideModelSelectIt()
        {
            var target = Target(new Vector3(2f, 0f, 2f));
            var range = target.gameObject.AddComponent<SphereCollider>();
            range.isTrigger = true;
            range.radius = 4f;
            Assert.That(Resolve(new Ray(Vector3.zero, Vector3.forward), target), Is.Null);
        }

        [Test]
        public void DisabledOrUnavailableObjectIsNotSelected()
        {
            var target = Target(new Vector3(0f, 0f, 2f));
            target.Available = false;
            Assert.That(Resolve(new Ray(Vector3.zero, Vector3.forward), target), Is.Null);
            target.Available = true;
            target.gameObject.SetActive(false);
            Assert.That(Resolve(new Ray(Vector3.zero, Vector3.forward), target), Is.Null);
        }

        [Test]
        public void ObjectsBeyondSixMetersAreNotSelected()
        {
            var target = Target(new Vector3(0f, 0f, 7f));
            Assert.That(Resolve(new Ray(Vector3.zero, Vector3.forward), target), Is.Null);
        }

        [Test]
        public void ScreenPromptPrefabHasGeneratedReferencesBelowReticle()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/GameplayGuideHud.prefab");
            var view = prefab.GetComponent<GameplayGuideHudUI>();
            Assert.That(prefab.GetComponent<ReferenceCollector>(), Is.Not.Null);
            Assert.That(view.InteractionRoot, Is.Not.Null);
            Assert.That(view.InteractionText, Is.Not.Null);
            Assert.That(view.InteractionText.transform.IsChildOf(view.InteractionRoot.transform), Is.True);
            var rect = view.InteractionRoot.GetComponent<RectTransform>();
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(rect.anchoredPosition.y, Is.LessThan(0f));
            Assert.That(view.InteractionText.raycastTarget, Is.False);
            Assert.That(view.InteractionText.GetComponent<LocalizedText>(), Is.Null);
        }

        [Test]
        public void FormalNpcCanBeSelectedAtPlayerEyeHeight()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/BaseNpcMerchant.prefab");
            var root = Object.Instantiate(prefab);
            objects.Add(root);
            root.transform.position = new Vector3(20f, 0f, 20f);
            var npc = root.GetComponent<NpcView>();
            Physics.SyncTransforms();
            Assert.That(Resolve(new Ray(root.transform.position + new Vector3(0f, 1.6f, -2f), Vector3.forward), npc),
                Is.SameAs(npc), "第一人称看向正式 NPC 身体应选中 NPC，无需抬头看提示根");
        }

        [Test]
        public void PickupInteractionKeepsNameAndRejectsUnavailableRequests()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab");
            var root = Object.Instantiate(prefab);
            objects.Add(root);
            var view = root.GetComponent<ResourcePickupView>();
            var requests = 0;
            view.InteractionRequested += () => requests++;
            view.RefreshDisplay("金属零件", 4);
            view.SetInteractionAvailable(true);
            Assert.That(view.DisplayName, Is.EqualTo("金属零件x4"));
            view.Interact();
            Assert.That(requests, Is.EqualTo(1));
            view.SetInteractionAvailable(false);
            view.Interact();
            Assert.That(requests, Is.EqualTo(1));
        }

        private AimTargetView Target(Vector3 position)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(root);
            root.transform.position = position;
            Physics.SyncTransforms();
            return root.AddComponent<AimTargetView>();
        }

        private static IPlayerInteractable Resolve(Ray ray, params IPlayerInteractable[] candidates)
        {
            return FirstPersonInteractionTargeting.Resolve(ray, candidates);
        }

        private sealed class AimTargetView : MonoBehaviour, IPlayerInteractable
        {
            public bool Available = true;
            public string DisplayName => "目标";
            public bool CanInteract => Available && isActiveAndEnabled;
            public void SetFocused(bool focused) { }
            public void Interact() { }
        }
    }
}
