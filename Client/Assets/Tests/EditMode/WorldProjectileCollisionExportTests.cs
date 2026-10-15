using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    public sealed class WorldProjectileCollisionExportTests
    {
        private GameObject root,obj;
        [SetUp] public void Setup(){root=new GameObject("map");obj=new GameObject("solid");obj.transform.SetParent(root.transform);}
        [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(root);}
        private WorldProjectileShape[] Capture(Collider c)
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Game.Editor.WorldProjectileCollisionExporter")).FirstOrDefault(t=>t!=null);
            Assert.That(type,Is.Not.Null,"缺少地图碰撞资源导出器");
            return (WorldProjectileShape[])type.GetMethod("CaptureCollider").Invoke(null,new object[]{c,root.transform});
        }
        [Test] public void BoxExportUsesMapLocalTransformAndHalfAxes()
        {root.transform.position=new Vector3(100,20,-50);root.transform.rotation=Quaternion.Euler(0,90,0);obj.transform.localPosition=new Vector3(2,3,4);obj.transform.localScale=new Vector3(2,3,4);var c=obj.AddComponent<BoxCollider>();c.size=new Vector3(2,2,2);var s=Capture(c)[0];Assert.That(s.Center.X,Is.EqualTo(2).Within(.001));Assert.That(s.Center.Y,Is.EqualTo(3).Within(.001));Assert.That(s.AxisZ.Length,Is.EqualTo(4).Within(.001));}
        [Test] public void SphereExportMatchesUnityNonUniformRadius()
        {obj.transform.localScale=new Vector3(2,3,4);var c=obj.AddComponent<SphereCollider>();c.radius=.5f;Assert.That(Capture(c)[0].Radius,Is.EqualTo(2).Within(.001));}
        [Test] public void CapsuleExportPreservesEndpoints()
        {var c=obj.AddComponent<CapsuleCollider>();c.height=4;c.radius=.5f;var s=Capture(c)[0];Assert.That(s.A.Y,Is.EqualTo(-1.5).Within(.001));Assert.That(s.B.Y,Is.EqualTo(1.5).Within(.001));}
        [Test] public void TriggerDoesNotBecomeWorldObstacle()
        {var c=obj.AddComponent<BoxCollider>();c.isTrigger=true;Assert.That(Capture(c),Is.Empty);}
        [Test] public void DisabledColliderDoesNotBecomeWorldObstacle()
        {var c=obj.AddComponent<BoxCollider>();c.enabled=false;Assert.That(Capture(c),Is.Empty);}
        [Test] public void DisabledMapRootDoesNotRemoveRuntimeActivatedContent()
        {root.SetActive(false);var c=obj.AddComponent<BoxCollider>();Assert.That(Capture(c).Length,Is.EqualTo(1));}
        [Test] public void MeshExportUsesActualTriangleInsteadOfBounds()
        {var mesh=new Mesh{vertices=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,0,1)},triangles=new[]{0,1,2}};try{var c=obj.AddComponent<MeshCollider>();c.sharedMesh=mesh;var s=Capture(c);Assert.That(s.Length,Is.EqualTo(1));Assert.That(s[0].Kind,Is.EqualTo(WorldCollisionShapeKind.Triangle));Assert.That(s[0].B.X,Is.EqualTo(1));}finally{UnityEngine.Object.DestroyImmediate(mesh);}}
        [Test] public void TerrainHeightfieldGroundMatchesUnityCollider()
        {
            var data=new TerrainData{heightmapResolution=33,size=new Vector3(32,10,32)};
            try
            {
                var heights=new float[33,33];heights[16,16]=.8f;data.SetHeights(0,0,heights);var collider=obj.AddComponent<TerrainCollider>();collider.terrainData=data;
                var shapes=Capture(collider);shapes[0].Id=1;var query=new WorldProjectileCollisionQuery(shapes);
                foreach(var point in new[]{new Vector3(16.1f,15,16.2f),new Vector3(16.8f,15,16.7f),new Vector3(15.8f,15,16.3f)})
                {
                    Assert.That(collider.Raycast(new Ray(point,Vector3.down),out var hit,20),Is.True);
                    Assert.That(query.TryGround(new WorldCollisionVector(point.x,point.y,point.z),out var ground),Is.True);
                    Assert.That(ground.Y,Is.EqualTo(hit.point.y).Within(.002f),"Terrain 三角划分必须与实际 Collider 一致");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(data);}
        }
        [Test] public void ConvexMeshCannotSilentlyUseUncookedTriangles()
        {var c=obj.AddComponent<MeshCollider>();c.convex=true;var error=Assert.Throws<TargetInvocationException>(()=>Capture(c));Assert.That(error.InnerException,Is.TypeOf<NotSupportedException>());}
    }
}
