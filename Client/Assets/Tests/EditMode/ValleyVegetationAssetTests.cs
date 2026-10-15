using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ValleyVegetationAssetTests
    {
        [Test]
        public void MissingGroundCoverReference_RejectsPartialPalette()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab"));
            try
            {
                var kit = root.GetComponent<ValleyGenerationKit>();
                var data = new SerializedObject(kit);
                data.FindProperty("shrubPrefabs").GetArrayElementAtIndex(0).objectReferenceValue = null;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(kit.Validate(out _), Is.False, "损坏的植被引用应在候选可见前失败");
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void GroundCover_SourceMeshesAreReadableOutsideEditor()
        {
            var names = new[] { "Bush_01", "Bush_02", "Bush_03", "Grass_01", "Grass_02" };
            foreach (var name in names)
            {
                var path = "Assets/Game/Art/Environment/CivilisationExpedition/Sources/SimpleNaturePack/Models/" + name + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.isReadable, Is.True, name + "：运行时合批必须能够读取正式网格顶点");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().All(m => m.isReadable), Is.True, name);
            }
        }
        [Test]
        public void Palette_PreservesFormalDependenciesWithoutCollisionOrWhiteEmission()
        {
            var names = new[] { "Tree_01", "Tree_02", "Tree_03", "Tree_04", "Tree_05", "Bush_01", "Bush_02", "Bush_03", "Grass_01", "Grass_02" };
            foreach (var name in names)
            {
                var path = "Assets/Game/Art/Environment/CivilisationExpedition/Vegetation/" + name + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, name);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, name);
                Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty, name);
                Assert.That(AssetDatabase.GetDependencies(path, true).Any(p => p.StartsWith("Assets/Art/")), Is.False, name);
                foreach (var material in prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials))
                {
                    Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                    Assert.That(material.GetColor("_EmissionColor").maxColorComponent, Is.EqualTo(0));
                }
            }
        }
    }
}
