using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证正式屋顶拆分保留几何和碰撞、遮挡材质成为资源依赖。</summary>
    public sealed class BaseCampOcclusionPrefabTests
    {
        [TestCase("shed_wood_01")]
        [TestCase("awning_standing_01a")]
        public void SplitRoofPreservesEveryTriangleAndOriginalPhysicalMesh(string kind)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/Camp_"+kind+".prefab");
            var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Environment/BaseCamp/Sources/RPGPP_LT/Models/Exterior/rpgpp_lt_"+kind+".fbx").OfType<Mesh>().First();
            var body=prefab.transform.Find("Model").GetComponent<MeshFilter>().sharedMesh;
            var roof=prefab.transform.Find("Roof");Assert.That(roof,Is.Not.Null);
            var roofMesh=roof.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(body.triangles.Length+roofMesh.triangles.Length,Is.EqualTo(source.triangles.Length));
            var actual=TriangleKeys(body).Concat(TriangleKeys(roofMesh)).OrderBy(value=>value).ToArray();
            Assert.That(actual,Is.EqualTo(TriangleKeys(source).OrderBy(value=>value).ToArray()));
            Assert.That(body.vertices,Is.EqualTo(source.vertices));Assert.That(roofMesh.uv,Is.EqualTo(source.uv));
            Assert.That(prefab.transform.Find("Model").GetComponent<MeshCollider>().sharedMesh,Is.SameAs(source));
            var view=prefab.GetComponent<CameraOccluderView>();Assert.That(view,Is.Not.Null);
            Assert.That(view.InteriorCollider,Is.SameAs(prefab.GetComponent<TemporaryBuildingView>().FootprintCollider));
            Assert.That(view.RoofRenderers.Single(),Is.SameAs(roof.GetComponent<Renderer>()));
        }
        [Test]
        public void EveryCampPrefabHasExplicitRendererReferencesAndTransparentShaderDependency()
        {
            var paths=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Game/Prefabs/Building"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path=>System.IO.Path.GetFileName(path).StartsWith("Camp_")).ToArray();
            Assert.That(paths.Length,Is.EqualTo(11));
            foreach(var path in paths)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);var view=prefab.GetComponent<CameraOccluderView>();
                Assert.That(view,Is.Not.Null,path);Assert.That(view.BodyRenderers.Length,Is.GreaterThan(0),path);
                Assert.That(view.TransparentMaterial,Is.Not.Null,path);
                Assert.That(view.TransparentMaterial.GetFloat("_Surface"),Is.EqualTo(1),path);
                Assert.That(view.TransparentMaterial.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"),Is.True,path);
                foreach(var renderer in view.BodyRenderers.Concat(view.RoofRenderers))
                { Assert.That(renderer,Is.Not.Null,path);Assert.That(renderer.transform.IsChildOf(prefab.transform),Is.True,path); }
                Assert.That(AssetDatabase.GetDependencies(path),Does.Contain(AssetDatabase.GetAssetPath(view.TransparentMaterial)),path);
            }
        }
        [Test]
        public void BaseSceneContainsMarkedNaturalBlockersAndNoMissingRendererReferences()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity",OpenSceneMode.Additive);
            try
            {
                var views=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CameraOccluderView>(true)).ToArray();
                Assert.That(views.Length,Is.GreaterThan(0));
                foreach(var view in views)
                {
                    Assert.That(view.TransparentMaterial,Is.Not.Null);Assert.That(view.GetComponent<Collider>(),Is.Not.Null);
                    Assert.That(view.BodyRenderers.All(renderer=>renderer!=null),Is.True);
                    Assert.That(view.RoofRenderers,Is.Empty);
                }
            }
            finally {EditorSceneManager.CloseScene(scene,true);}
        }
        [Test]
        public void ValleyNaturalBlockersHaveCompleteLodCoverageAndTransparentDependency()
        {
            const string path = "Assets/Game/Scenes/CivilisationValleyMap.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            var opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (var prefix in new[] { "ValleyTree_", "ValleyRock_", "RiverbankRock_", "BoundaryRock_" })
                {
                    var blockers = transforms.Where(t => t.name.StartsWith(prefix, System.StringComparison.Ordinal)).ToArray();
                    Assert.That(blockers, Is.Not.Empty, prefix);
                    foreach (var blocker in blockers)
                    {
                        var view = blocker.GetComponent<CameraOccluderView>();
                        Assert.That(view, Is.Not.Null, blocker.name);
                        Assert.That(view.BodyRenderers, Is.EquivalentTo(blocker.GetComponentsInChildren<Renderer>(true)), blocker.name);
                        Assert.That(view.RoofRenderers, Is.Empty, blocker.name);
                        Assert.That(view.InteriorCollider, Is.Null, blocker.name);
                        Assert.That(view.TransparentMaterial, Is.Not.Null, blocker.name);
                        Assert.That(view.TransparentMaterial.GetFloat("_Surface"), Is.EqualTo(1f), blocker.name);
                        Assert.That(AssetDatabase.GetDependencies(path), Does.Contain(AssetDatabase.GetAssetPath(view.TransparentMaterial)));
                    }
                }
                foreach (var name in new[] { "ValleyGround", "RiverbedAndValley", "ApproachPath", "NorthBankPath" })
                    Assert.That(transforms.Single(t => t.name == name).GetComponent<CameraOccluderView>(), Is.Null, "地面和道路不参与遮挡淡化");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        private static string[] TriangleKeys(Mesh mesh)
        {
            var triangles=mesh.triangles;var result=new string[triangles.Length/3];
            for(var i=0;i<result.Length;i++)result[i]=triangles[i*3]+","+triangles[i*3+1]+","+triangles[i*3+2];
            return result;
        }
    }
}
