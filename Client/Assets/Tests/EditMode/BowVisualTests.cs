using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证改良弓的握持方向、拉弦和实例网格寿命，不运行战斗结算。</summary>
    public sealed class BowVisualTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Characters/Weapons/HanBow.prefab";

        [Test]
        public void TiltedGripDoesNotTurnDrawnBowHorizontal()
        {
            var bow = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var hand = new GameObject("DrawingHand");
            try
            {
                bow.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                hand.transform.position = new Vector3(.65f, .04f, .02f);
                var view = bow.GetComponent<global::Game.BowVisualView>();
                view.Refresh(hand.transform, 1f);
                Assert.That(Vector3.Dot((view.UpperTipWorldPosition - bow.transform.position).normalized, Vector3.up),
                    Is.GreaterThan(.8f), "手腕翻转不能将瞄准中的弓转横");
                view.Dispose();
            }
            finally { bow.GetComponent<global::Game.BowVisualView>()?.Dispose(); Object.DestroyImmediate(bow); Object.DestroyImmediate(hand); }
        }

        [Test]
        public void ShotEnvelopeDrawsAfterNockingAndRelaxesAfterRelease()
        {
            Assert.That(global::Game.BowVisualView.AttackDrawWeight(.25f), Is.EqualTo(0f));
            Assert.That(global::Game.BowVisualView.AttackDrawWeight(.6f), Is.EqualTo(1f));
            Assert.That(global::Game.BowVisualView.AttackDrawWeight(.8f), Is.EqualTo(0f));
        }

        [Test]
        public void RestBowFacesTheDrawingHandSide()
        {
            var bow = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var filter = bow.GetComponentInChildren<MeshFilter>();
                var center = bow.transform.InverseTransformPoint(filter.GetComponent<Renderer>().bounds.center);
                Assert.That(center.x, Is.GreaterThan(.05f), "弓弦和弓梢应朝拉弦手的正 X 方向");
            }
            finally { Object.DestroyImmediate(bow); }
        }

        [Test]
        public void DrawMovesStringAndLimbsAndReleaseRestoresThem()
        {
            var bow = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var hand = new GameObject("DrawingHand");
            try
            {
                var type = typeof(global::Game.CharacterWeaponPresenter).Assembly.GetType("Game.BowVisualView");
                Assert.That(type, Is.Not.Null, "弓需要独立的变形表现");
                var view = bow.GetComponent(type);
                Assert.That(view, Is.Not.Null, "正式弓 Prefab 必须接入变形表现");
                hand.transform.position = bow.transform.TransformPoint(new Vector3(.65f, .04f, .02f));
                var refresh = type.GetMethod("Refresh");
                refresh.Invoke(view, new object[] { hand.transform, 1f });
                var nock = (Vector3)type.GetProperty("NockWorldPosition").GetValue(view);
                Assert.That(Vector3.Distance(nock, hand.transform.position), Is.LessThan(.001f));
                var tip = bow.transform.InverseTransformPoint((Vector3)type.GetProperty("UpperTipWorldPosition").GetValue(view));
                Assert.That(tip.x, Is.GreaterThan(.17f));
                Assert.That(tip.y, Is.LessThan(.61f));
                var filter = bow.GetComponentInChildren<MeshFilter>();
                var drawn = filter.sharedMesh.vertices;
                refresh.Invoke(view, new object[] { hand.transform, 1f });
                Assert.That(filter.sharedMesh.vertices.SequenceEqual(drawn), Is.True, "重复采样不累计形变");
                refresh.Invoke(view, new object[] { hand.transform, 0f });
                nock = bow.transform.InverseTransformPoint((Vector3)type.GetProperty("NockWorldPosition").GetValue(view));
                Assert.That(nock.x, Is.EqualTo(.17f).Within(.002f));
                tip = bow.transform.InverseTransformPoint((Vector3)type.GetProperty("UpperTipWorldPosition").GetValue(view));
                Assert.That(tip.x, Is.EqualTo(.17f).Within(.002f));
            }
            finally { bow.GetComponent<global::Game.BowVisualView>()?.Dispose(); Object.DestroyImmediate(bow); Object.DestroyImmediate(hand); }
        }

        [Test]
        public void DrawUsesInstanceMeshAndOwnerReleaseDisposesIt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var shared = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            var bow = Object.Instantiate(prefab);
            var hand = new GameObject("DrawingHand");
            Mesh instance = null;
            try
            {
                var type = typeof(global::Game.CharacterWeaponPresenter).Assembly.GetType("Game.BowVisualView");
                Assert.That(type, Is.Not.Null);
                var view = bow.GetComponent(type);
                hand.transform.position = new Vector3(.65f, 0f, 0f);
                type.GetMethod("Refresh").Invoke(view, new object[] { hand.transform, 1f });
                instance = bow.GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.That(instance, Is.Not.SameAs(shared));
                Assert.That(prefab.GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(shared));
                ((IDisposable)view).Dispose();
                ((IDisposable)view).Dispose();
            }
            finally { bow.GetComponent<global::Game.BowVisualView>()?.Dispose(); Object.DestroyImmediate(bow); Object.DestroyImmediate(hand); }
            Assert.That(instance == null, Is.True, "武器销毁须释放动态网格");
            Assert.That(shared != null, Is.True, "不能销毁源网格");
        }
    }
}
