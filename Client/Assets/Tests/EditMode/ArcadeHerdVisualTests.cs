using System.Linq;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式牛资源及地图代次只读投影，不载入实际地图。</summary>
    public sealed class ArcadeHerdVisualTests
    {
        private const string Path="Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab";
        [Test] public void ConfirmedCowUsesPersistedMeshesAndMaterialsWithoutColliders()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);Assert.That(prefab,Is.Not.Null);
            Assert.That(prefab.GetComponent<ArcadeHerdVisual>(),Is.Not.Null);Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true)){Assert.That(filter.sharedMesh,Is.Not.Null);Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh),Does.StartWith("Assets/Game/Art/Arcade/Farm/"));}
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials){Assert.That(material,Is.Not.Null);Assert.That(AssetDatabase.GetAssetPath(material),Does.StartWith("Assets/Game/Art/Arcade/Farm/"));}
        }
        [Test] public void PoseUsesOnlyAuthorityClockAndPreservesCandidateBodyScale()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var view=go.GetComponent<ArcadeHerdVisual>();view.Bind(10,2);var snapshot=new ArcadeHerdSnapshot(10,2,ArcadeHerdPhase.Charging,3,4,1,0,.23);
                Assert.That(view.Project(snapshot),Is.True);Assert.That(go.transform.localPosition,Is.EqualTo(new Vector3(3,0,4)));
                Assert.That(Vector3.Dot(go.transform.forward,Vector3.right),Is.GreaterThan(.999f));
                var hips=go.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Leg_")).ToArray();Assert.That(hips.Length,Is.EqualTo(4));
                var rotations=hips.Select(t=>t.localRotation).ToArray();Assert.That(rotations.Any(q=>q!=Quaternion.identity),Is.True);
                view.Project(snapshot);Assert.That(hips.Select((t,i)=>t.localRotation==rotations[i]).All(v=>v),Is.True,"冻结快照不得继续播放");
                Assert.That(go.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="CoatBody").localScale,Is.EqualTo(new Vector3(.53f,.43f,.93f)));
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void StaleMapOrCowAndReleaseCannotMoveReboundView()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var view=go.GetComponent<ArcadeHerdVisual>();view.Bind(10,2);var good=new ArcadeHerdSnapshot(10,2,ArcadeHerdPhase.Windup,1,2,0,1,.4);
                Assert.That(view.Project(good),Is.True);var position=go.transform.localPosition;
                Assert.That(view.Project(new ArcadeHerdSnapshot(11,2,ArcadeHerdPhase.Charging,9,9,1,0,1)),Is.False);
                Assert.That(view.Project(new ArcadeHerdSnapshot(10,3,ArcadeHerdPhase.Charging,9,9,1,0,1)),Is.False);Assert.That(go.transform.localPosition,Is.EqualTo(position));
                view.Release();Assert.That(view.Project(good),Is.False);Assert.That(go.activeSelf,Is.False);
                view.Bind(11,3);Assert.That(view.Project(new ArcadeHerdSnapshot(11,3,ArcadeHerdPhase.Windup,0,0,0,1,0)),Is.True);Assert.That(go.activeSelf,Is.True);
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
