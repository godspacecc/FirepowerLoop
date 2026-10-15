using System.Collections.Generic;
using System.Linq;
using AnotherDay.Arcade;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>保护规则轮廓与真实步进路径，避免画面曲线而运行时仍剪矩形。</summary>
    public sealed class ArcadeIslandRegionTests
    {
        [Test] public void ContourKeepsBroadContinuousAreaAndSlidesInsideOuterEdge()
        {
            var region=new ArcadeIslandRegion(19.5f,19.5f);double area=0;
            for(int i=0;i<region.Outer.Count;i++)
            {
                var a=region.Outer[i];var b=region.Outer[(i+1)%region.Outer.Count];area+=a.X*b.Y-b.X*a.Y;
                var start=region.Constrain(a.X,a.Y,.55f);var path=new List<ArcadeRegionPoint>();
                var end=region.Move(start.X,start.Y,a.X*2,a.Y*2,.55f,path);
                Assert.That(region.Contains(end.X,end.Y),Is.True);
                var predicted=OnlineArcadePresentationSystem.PredictLocalPosition(new Vector2(start.X,start.Y),
                    Vector2.right,6f,.05f,.55f,19.5f,19.5f,null,region);
                var expected=region.Move(start.X,start.Y,.3f,0,.55f);
                Assert.That(predicted,Is.EqualTo(new Vector2(expected.X,expected.Y)));
                for(int p=1;p<path.Count;p++)
                {
                    Assert.That(region.Contains(path[p].X,path[p].Y),Is.True);
                    Assert.That(Vector2.Distance(new Vector2(path[p-1].X,path[p-1].Y),new Vector2(path[p].X,path[p].Y)),Is.LessThan(.3f));
                }
            }
            Assert.That(area*.5,Is.GreaterThan(39*39*.84));
            foreach(float x in new[]{-19f,19f})foreach(float y in new[]{-19f,19f})Assert.That(region.Contains(x,y),Is.False);
            Assert.That(region.Contains(0,0),Is.True);Assert.That(region.IsShallow(0,0),Is.False);
        }
        [Test] public void MeshesUseTheRuleContoursAndKeepUpwardNormals()
        {
            var region=new ArcadeIslandRegion(19.5f,19.5f);
            var sand=ArcadeIslandMesh.Sand(region);var water=ArcadeIslandMesh.Water(region);var shore=ArcadeIslandMesh.Shoreline(region);
            try
            {
                for(int i=0;i<region.Sand.Count;i++)
                {
                    Assert.That(sand.vertices[i+1],Is.EqualTo(new Vector3(region.Sand[i].X,0,region.Sand[i].Y)));
                    Assert.That(water.vertices[i*2+1],Is.EqualTo(new Vector3(region.Outer[i].X,0,region.Outer[i].Y)));
                    var s=region.Sand[i];var o=region.Outer[i];
                    Assert.That(region.IsShallow((s.X+o.X)*.5f,(s.Y+o.Y)*.5f),Is.True);
                }
                Assert.That(sand.normals.All(n=>n.y>.99f),Is.True);Assert.That(water.normals.All(n=>n.y>.99f),Is.True);
                Assert.That(shore.normals.All(n=>n.y>.99f),Is.True);
            }
            finally {Object.DestroyImmediate(sand);Object.DestroyImmediate(water);Object.DestroyImmediate(shore);}
        }
    }
}
