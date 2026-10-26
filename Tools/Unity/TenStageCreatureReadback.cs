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

/// <summary>逐顶点、法线、索引、颜色和局部曲线采样比对；验证正式持久资源而非文件存在。</summary>
public static class TenStageCreatureReadback
{
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="F:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("错误项目或编辑器忙");
        var before=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);var reports=new List<string>();
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach(int kind in TenStageCreatureBake.Kinds)
            {
                var owned=new List<Object>();GameObject source=null,actual=null;
                try
                {
                    source=TenStageCreatureCandidates.Create(kind==2?0:kind,kind==2?2:0,null,owned);actual=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TenStageCreatureBake.Prefab(kind)));
                    var a=source.GetComponentsInChildren<MeshFilter>();var b=actual.GetComponentsInChildren<MeshFilter>();if(a.Length!=b.Length)throw new Exception("网格数量变化");
                    for(int i=0;i<a.Length;i++)
                    {
                        if(!a[i].sharedMesh.vertices.SequenceEqual(b[i].sharedMesh.vertices)||!a[i].sharedMesh.normals.SequenceEqual(b[i].sharedMesh.normals)||!a[i].sharedMesh.triangles.SequenceEqual(b[i].sharedMesh.triangles))throw new Exception("网格内容变化："+i);
                        if(a[i].GetComponent<Renderer>().sharedMaterial.color!=b[i].GetComponent<Renderer>().sharedMaterial.color)throw new Exception("材质颜色变化："+i);
                    }
                    int samples=0;float maxPosition=0,maxAngle=0;var view=actual.GetComponent<ArcadeAdoptedCreatureView>();
                    foreach(var spec in TenStageCreatureBake.Specs(kind))
                    {
                        var nodes=TenStageCreatureBake.Nodes(source,spec.upper);int count=Mathf.CeilToInt(spec.length*48);
                        for(int sample=0;sample<=count;sample++)
                        {
                            float t=spec.length*sample/count;TenStageCreatureBake.Sample(source,kind,spec,t);view.SamplePose("Idle",0);view.SamplePose(spec.name,t);
                            foreach(var n in nodes)
                            {
                                var read=actual.transform.Find(AnimationUtility.CalculateTransformPath(n,source.transform));
                                maxPosition=Mathf.Max(maxPosition,Vector3.Distance(n.localPosition,read.localPosition));maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(n.localRotation,read.localRotation));
                                if(Vector3.Distance(n.localScale,read.localScale)>.0001f)throw new Exception("正式缩放与采用源不符");
                            }
                            if(actual.transform.position!=Vector3.zero)throw new Exception("动画移动根位置");samples++;
                        }
                    }
                    if(maxPosition>.0001f||maxAngle>.1f)throw new Exception($"姿态偏差超限：{maxPosition}m/{maxAngle}deg");
                    reports.Add($"PASS {TenStageCreatureBake.Name(kind)}: {a.Length} mesh vertex/normal/index arrays and colors identical; {samples} samples; max position delta={maxPosition}, angle delta={maxAngle}; actor roots unchanged.");
                }
                finally{if(source!=null)Object.DestroyImmediate(source);if(actual!=null)Object.DestroyImmediate(actual);foreach(var obj in owned)Object.DestroyImmediate(obj);}
            }
            string result=string.Join("\n",reports);File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/TenStageCreaturesApplied-20261026/readback.txt")),result);return result;
        }
        finally{SceneManager.SetActiveScene(before);EditorSceneManager.CloseScene(scene,true);}
    }
}
