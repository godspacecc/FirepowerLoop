using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>首章场景资源契约：搜索证物与持久布景分离，装饰不额外阻断路线。</summary>
    public sealed class HanSceneArtTests
    {
        private const string KitPath="Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab";
        [TestCase("han.iron-tools")]
        [TestCase("han.steel-blade")]
        [TestCase("han.bow")]
        [TestCase("han.grain")]
        [TestCase("han.pottery")]
        [TestCase("han.proof")]
        public void SearchRetainsSceneAfterEvidenceCollected(string id)
        {
            var kit=AssetDatabase.LoadAssetAtPath<GameObject>(KitPath).GetComponent<ValleyGenerationKit>();
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var go=Object.Instantiate(kit.FindTemplate(id).gameObject);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                var view=go.GetComponent<CivilisationPointView>();
                var scenery=go.transform.Find("StoryScene");Assert.That(scenery,Is.Not.Null,id);
                Assert.That(view.HarvestedVisualRoot,Is.Not.Null);
                Assert.That(view.HarvestedVisualRoot.transform.IsChildOf(scenery),Is.False);
                view.RefreshHarvestPresentation(true);
                Assert.That(scenery.gameObject.activeSelf,Is.True);
                Assert.That(view.HarvestedVisualRoot.activeSelf,Is.False);
                Assert.That(scenery.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(2));
                Assert.That(scenery.GetComponentsInChildren<Collider>(true),Is.Empty);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
            Assert.That(AssetDatabase.GetDependencies(KitPath).Any(p=>p.StartsWith("Assets/Art/")),Is.False);
        }

        [TestCase("transportClueTemplate")]
        [TestCase("trailClueTemplate")]
        public void RouteClueHasDedicatedScene(string field)
        {
            var kit=AssetDatabase.LoadAssetAtPath<GameObject>(KitPath).GetComponent<ValleyGenerationKit>();
            var property=new SerializedObject(kit).FindProperty(field);Assert.That(property,Is.Not.Null);
            var view=property.objectReferenceValue as CivilisationPointView;Assert.That(view,Is.Not.Null);
            Assert.That(view,Is.Not.EqualTo(kit.PreparationTemplate));
            Assert.That(view.transform.Find("StoryScene"),Is.Not.Null);
            Assert.That(view.GetComponentsInChildren<Collider>(true).All(c=>c.isTrigger),Is.True);
        }
    }
}
