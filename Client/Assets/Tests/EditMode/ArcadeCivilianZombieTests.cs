using System.Collections.Generic;
using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>按真实蒙皮顶点比较主角与丧尸尺寸，防止只缩身高却仍保留大头怪物比例。</summary>
    public sealed class ArcadeCivilianZombieTests
    {
        [TestCase("Shambler")][TestCase("Gaunt")][TestCase("Bloated")]
        public void HumanZombieMatchesPlayerHeightAndHeadSize(string kind)
        {
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var zombie=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie"+kind+".prefab"));
            try
            {
                foreach(var a in player.GetComponentsInChildren<Animator>()) { a.Rebind();a.Update(0); }
                zombie.GetComponent<EnemyView>().Animator.Rebind();zombie.GetComponent<EnemyView>().Animator.Update(0);
                var reference=BodyBounds(player); var actual=BodyBounds(zombie);
                Assert.That(actual.size.y/reference.size.y,Is.InRange(.95f,1.05f),"身高应按主角实际身体统一");
                Assert.That(HeadWidth(zombie)/HeadWidth(player),Is.InRange(.8f,1.2f),"头部也要保持人体比例，不能只缩小巨型卡通造型");
                Assert.That(Mathf.Abs(actual.min.y-zombie.transform.position.y),Is.LessThan(.04f),"脚底需落地");
                Assert.That(zombie.GetComponent<EnemyPhysicalReaction>().IsReady,Is.True);
                var flame=zombie.GetComponentInChildren<EnemyFlameFeedback>(true);
                var abdomen=zombie.GetComponentsInChildren<Transform>().Single(t=>t.name=="Abdomen");
                Assert.That(Vector3.Distance(flame.transform.position,abdomen.position),Is.LessThan(.15f),"火焰应附着躯干，不能沿用旧骨架偏移落到腿上");
            }
            finally { Object.DestroyImmediate(player);Object.DestroyImmediate(zombie); }
        }

        private static IEnumerable<SkinnedMeshRenderer> Body(GameObject root) => root.GetComponentsInChildren<SkinnedMeshRenderer>()
            .Where(r=>r.enabled&&r.bones.Length>0);
        private static Bounds BodyBounds(GameObject root)
        {
            var points=new List<Vector3>();
            foreach(var r in Body(root))
            {
                var mesh=new Mesh();r.BakeMesh(mesh,true);
                points.AddRange(mesh.vertices.Select(r.transform.TransformPoint));Object.DestroyImmediate(mesh);
            }
            Assert.That(points,Is.Not.Empty);var b=new Bounds(points[0],Vector3.zero);foreach(var v in points)b.Encapsulate(v);return b;
        }
        private static float HeadWidth(GameObject root)
        {
            var points=new List<Vector3>();
            // 比较头部本体，排除主角帽子与丧尸头发造成的外轮廓差异。
            foreach(var r in Body(root).Where(r=>r.name=="Body_Head"))
            {
                var mesh=new Mesh();r.BakeMesh(mesh,true);var vertices=mesh.vertices;var weights=r.sharedMesh.boneWeights;
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];
                    if(w.weight0>.5f&&r.bones[w.boneIndex0].name=="Head")points.Add(r.transform.TransformPoint(vertices[i]));
                }
                Object.DestroyImmediate(mesh);
            }
            Assert.That(points,Is.Not.Empty);return points.Max(p=>p.x)-points.Min(p=>p.x);
        }
    }
}
