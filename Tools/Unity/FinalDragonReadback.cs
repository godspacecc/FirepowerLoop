using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>逐项比较采用源与正式资源回读，不使用数量或文件存在替代实际内容检查。</summary>
public static class FinalDragonReadback
{
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="F:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("错误工程或编辑器忙");
        var before=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        FinalDragonCandidate.Rig source=null;GameObject actual=null;
        try
        {
            SceneManager.SetActiveScene(scene);source=FinalDragonCandidate.Create(null);actual=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeFinalDragon.prefab"));
            var a=source.Root.GetComponentsInChildren<MeshFilter>();var b=actual.GetComponentsInChildren<MeshFilter>();
            if(a.Length!=b.Length)throw new Exception("网格数量不一致");
            for(int i=0;i<a.Length;i++)
            {
                if(!a[i].sharedMesh.vertices.SequenceEqual(b[i].sharedMesh.vertices)||!a[i].sharedMesh.normals.SequenceEqual(b[i].sharedMesh.normals)||!a[i].sharedMesh.triangles.SequenceEqual(b[i].sharedMesh.triangles))throw new Exception("网格数据变化："+i);
                if(a[i].GetComponent<Renderer>().sharedMaterial.color!=b[i].GetComponent<Renderer>().sharedMaterial.color)throw new Exception("材质颜色变化："+i);
            }
            var nodes=new List<Transform>{source.Body,source.Neck,source.Head,source.Jaw};nodes.AddRange(source.Tail);nodes.AddRange(source.Wings);foreach(var leg in source.Legs)nodes.AddRange(new[]{leg.Upper,leg.Lower,leg.Foot});
            float maxPosition=0,maxAngle=0;int samples=0;var view=actual.GetComponent<ArcadeFinalDragonView>();
            foreach(var spec in new[]{("Idle",1.5f,0f),("Move",1.05f,2f),("Windup",.9f,4.5f),("Charge",.76f,5.4f),("Roar",2.1f,7.4f),("Wings",2.2f,9.5f),("RoarUpper",2.1f,7.4f),("WingsUpper",2.2f,9.5f)})
            {
                int count=Mathf.CeilToInt(spec.Item2*48);
                for(int k=0;k<=count;k++)
                {
                    float time=spec.Item2*k/count;FinalDragonCandidate.Sample(source,spec.Item3+time);view.SamplePose(spec.Item1,time);
                    foreach(var n in nodes)
                    {
                        if(spec.Item1.EndsWith("Upper",StringComparison.Ordinal)&&n!=source.Neck&&n!=source.Head&&n!=source.Jaw&&!source.Wings.Contains(n))continue;
                        var read=actual.transform.Find(AnimationUtility.CalculateTransformPath(n,source.Root.transform));
                        maxPosition=Mathf.Max(maxPosition,Vector3.Distance(n.localPosition,read.localPosition));maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(n.localRotation,read.localRotation));
                        if(Vector3.Distance(n.localScale,read.localScale)>.0001f)throw new Exception("缩放变化");
                    }
                    if(actual.transform.position!=Vector3.zero)throw new Exception("片段移动演员根");samples++;
                }
            }
            if(maxPosition>.0001f||maxAngle>.1f)throw new Exception($"姿态偏差超限：{maxPosition}m/{maxAngle}deg");
            string result=$"PASS: 118 mesh vertex/normal/index arrays identical; 118 renderer colors identical; {samples} clip samples across 8 clips; max position delta={maxPosition}; max angle delta={maxAngle}; all actor roots unchanged.";
            File.WriteAllText(Path.Combine(Application.dataPath,"../../Backup/Artifacts/FinalDragonApplied-20261026/readback.txt"),result);return result;
        }
        finally
        {
            if(actual!=null)Object.DestroyImmediate(actual);if(source!=null){Object.DestroyImmediate(source.Root);foreach(var obj in source.Owned)Object.DestroyImmediate(obj);}
            SceneManager.SetActiveScene(before);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
