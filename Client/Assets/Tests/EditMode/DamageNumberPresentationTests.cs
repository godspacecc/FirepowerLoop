using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class DamageNumberPresentationTests
    {
        private const string NumberPrefabPath = "Assets/Game/Prefabs/WorldHud/DamageNumberBox.prefab";

        [Test]
        public void DamageNumberBox_HasCompleteWorldHudContract()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NumberPrefabPath);
            Assert.NotNull(prefab);
            Assert.That(prefab.activeSelf, Is.False);
            Assert.That(prefab.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(prefab.GetComponent<Canvas>().sortingOrder, Is.EqualTo(200));
            Assert.That(prefab.GetComponent<DamageNumberView>().Validate(out string error), Is.True, error);
            TMP_Text label = prefab.GetComponentInChildren<TMP_Text>(true);
            Assert.NotNull(label);
            Assert.That(label.raycastTarget, Is.False);
            Assert.That(AssetDatabase.GetAssetPath(label.fontSharedMaterial), Is.EqualTo("Assets/Game/Materials/DamageNumberOverlay.mat"));
        }

        [TestCase("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab")]
        [TestCase("Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab")]
        [TestCase("Assets/Game/Prefabs/Characters/GrayboxArcher.prefab")]
        public void UnitPrefab_ReferencesFormalDamageNumberBox(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(prefab);
            DamageNumberEmitter emitter = prefab.GetComponent<DamageNumberEmitter>();
            Assert.NotNull(emitter);
            Assert.That(emitter.Validate(out string error), Is.True, error);
            SerializedObject serialized = new SerializedObject(emitter);
            Object reference = serialized.FindProperty("numberPrefab").objectReferenceValue;
            Assert.That(AssetDatabase.GetAssetPath(reference), Is.EqualTo(NumberPrefabPath));
        }

        [Test]
        public void ArcadeEntryPreparesNumbersAndRepeatedHitsReuseThem()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            try
            {
                instance.GetComponent<PlayerView>().SetArcadePresentationActive(true);
                var prepared = instance.GetComponentsInChildren<DamageNumberView>(true);
                Assert.That(prepared, Is.Not.Empty, "首次伤害不能承担飘字实例化成本");
                Assert.That(prepared.All(number => !number.gameObject.activeSelf), Is.True);
                var emitter = instance.GetComponent<DamageNumberEmitter>();
                for (int hit = 0; hit < 20; hit++) emitter.Show(17, true);
                var after = instance.GetComponentsInChildren<DamageNumberView>(true);
                Assert.That(after, Is.EquivalentTo(prepared), "超过并发上限也必须复用已准备的飘字");
                Assert.That(after.Where(number => number.gameObject.activeSelf).All(number =>
                    number.GetComponentInChildren<TMP_Text>(true).text == "-17"), Is.True);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void Emitter_ShowsBoundDamageAndReusesItsOwnChildren()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                instance.SetActive(true);
                DamageNumberEmitter emitter = instance.GetComponent<DamageNumberEmitter>();
                emitter.Show(17, false);
                DamageNumberView[] numbers = instance.GetComponentsInChildren<DamageNumberView>(true);
                Assert.That(numbers.Length, Is.EqualTo(1));
                Assert.That(numbers.Single().gameObject.activeSelf, Is.True);
                Assert.That(numbers.Single().GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("-17"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
