using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>正式掉落模型贴地、反复同步及对象复用的高度回归。</summary>
    public sealed class SurvivorGroundDropTests
    {
        private const string Folder="Assets/Game/Prefabs/SceneObjects/";
        private static void Ground(ResourcePickupView view,float height)
        {
            var method=typeof(ResourcePickupView).GetMethod("GroundArcadePresentation");
            Assert.That(method,Is.Not.Null);method.Invoke(view,new object[]{height});
        }
        private static float Bottom(ResourcePickupView view)
        {
            float bottom=float.PositiveInfinity;
            foreach(var r in view.transform.Find("ArcadeDropVisual").GetComponentsInChildren<Renderer>())
                if(r is MeshRenderer||r is SkinnedMeshRenderer)bottom=Mathf.Min(bottom,r.bounds.min.y);
            return bottom;
        }
        [TestCase("SurvivorExperienceCrystal")][TestCase("ArcadeDropCoin")]
        public void RealDropStaysOnGroundAcrossRotationAndRepeatedProjection(string name)
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"TestResourcePickup.prefab"));
            try{
                var view=go.GetComponent<ResourcePickupView>();view.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+name+".prefab"),name=="ArcadeDropCoin"?"Prefabs/SceneObjects/"+name:name);
                for(int i=0;i<8;i++){
                    go.transform.position=new Vector3(2,3.3f,4);go.transform.rotation=Quaternion.Euler(0,i*45,0);
                    Ground(view,3);Ground(view,3);
                    Assert.That(Bottom(view),Is.EqualTo(3.01f).Within(.0001f));
                    Assert.That(go.transform.position.x,Is.EqualTo(2));Assert.That(go.transform.position.z,Is.EqualTo(4));
                    Assert.That(Quaternion.Angle(go.transform.rotation,Quaternion.Euler(0,i*45,0)),Is.LessThan(.01f));
                }
            }finally{Object.DestroyImmediate(go);}
        }
        private sealed class LootSpy:IArcadeLootPresentationPort
        {
            public int GroundCalls;public float Height;public bool Success=true;
            public bool TrySetDrop(int id,Vector3 position,string name,int amount,out string error){error=null;return Success;}
            public void GroundDrop(int id,float groundY){GroundCalls++;Height=groundY;}
            public void RemoveDrop(int id){} public void ClearArcadeDrops(){}
        }
        [Test] public void GroundingRoutesOnlySuccessfulSurvivorDropsToMapHeight()
        {
            var root=new GameObject("GroundRouteTest");var port=new LootSpy();var view=new ArcadeCombatViewController();
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            try{
                root.transform.position=Vector3.up*3;
                typeof(ArcadeCombatViewController).GetField("mapRoot",hidden).SetValue(view,root.transform);
                typeof(ArcadeCombatViewController).GetField("lootPresentation",hidden).SetValue(view,port);
                view.SetDrop(1,default,Vector2.zero);Assert.That(port.GroundCalls,Is.Zero);
                typeof(ArcadeCombatViewController).GetField("survivorEnvironmentAddress",hidden).SetValue(view,"SurvivorFarmVisual");
                view.SetDrop(2,default,Vector2.zero);Assert.That(port.GroundCalls,Is.EqualTo(1));Assert.That(port.Height,Is.EqualTo(3));
                view.SetExperienceDrop(3,"SurvivorExperienceCrystal",Vector2.zero,2);Assert.That(port.GroundCalls,Is.EqualTo(2));
                port.Success=false;view.SetDrop(4,default,Vector2.zero);view.SetExperienceDrop(5,"SurvivorExperienceCrystal",Vector2.zero,2);
                Assert.That(port.GroundCalls,Is.EqualTo(2));
            }finally{Object.DestroyImmediate(root);}
        }
        [Test] public void ReusedPickupUsesReplacementModelsBottom()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"TestResourcePickup.prefab"));
            try{
                var view=go.GetComponent<ResourcePickupView>();
                foreach(string name in new[]{"ArcadeDropCoin","SurvivorExperienceCrystal","ArcadeDropCoin"}){
                    view.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+name+".prefab"),name);
                    go.transform.position=Vector3.up*.3f;Ground(view,0);
                    Assert.That(Bottom(view),Is.EqualTo(.01f).Within(.0001f));
                }
            }finally{Object.DestroyImmediate(go);}
        }
    }
}
