using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>只把已采用的Creatures v1烘焙为正式持久资源；不修改玩法或配置。</summary>
public static class BurrowerHoundBake
{
    private const string Art="Assets/Game/Art/Enemies/Creatures/";
    private const string Prefabs="Assets/Game/Prefabs/Characters/";
    private const string Aggregate="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";

    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)
            throw new InvalidOperationException("仅允许本工程空闲编辑态正式烘焙");
        var before=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var report=new List<string>();
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach(int kind in new[]{4,5})
            {
                string name=kind==4?"Burrower":"Hound",folder=Art+name+"/";EnsureFolder(folder.TrimEnd('/'));
                var owned=new List<Object>();var actor=BurrowerHoundPreview.Create(kind,null,owned);
                try
                {
                    actor.name="Arcade"+name;
                    int meshes=0,materials=0;
                    foreach(var obj in owned)
                    {
                        string path=folder+(obj is Mesh?"Mesh"+(meshes++):"Material"+(materials++))+(obj is Mesh?".asset":".mat");
                        var saved=Save(obj,path);
                        if(saved==obj)continue;
                        foreach(var filter in actor.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh==obj)filter.sharedMesh=(Mesh)saved;
                        foreach(var renderer in actor.GetComponentsInChildren<Renderer>(true))if(renderer.sharedMaterial==obj)renderer.sharedMaterial=(Material)saved;
                    }
                    var clips=new List<AnimationClip>();
                    foreach(var spec in new[]{("Idle",.25f,0f),("Move",kind==4?1f:.5f,kind==4?5.15f:0f),("Sink",1.35f,0f),("Underground",1.65f,1.35f),("Emerge",1.65f,3f),("Windup",1.01f,2.75f),("Pounce",1.74f,3.76f),("Bite",1.6f,6.65f),("BiteFace",1.6f,6.65f)})
                    {
                        var clip=Bake(actor,kind,spec.Item1,spec.Item2,spec.Item3);clips.Add((AnimationClip)Save(clip,folder+spec.Item1+".anim"));
                    }
                    BurrowerHoundPreview.Sample(actor,kind,0);
                    var view=actor.AddComponent<ArcadeCreatureView>();var serialized=new SerializedObject(view);var array=serialized.FindProperty("poses");array.arraySize=clips.Count;
                    for(int i=0;i<clips.Count;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=clips[i];serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(actor,Prefabs+actor.name+".prefab");
                    report.Add(name+": "+meshes+" meshes; "+materials+" materials; "+clips.Count+" clips");
                }
                finally{Object.DestroyImmediate(actor);foreach(var obj in owned)if(obj!=null&&!EditorUtility.IsPersistent(obj))Object.DestroyImmediate(obj);}
            }
            var aggregate=PrefabUtility.LoadPrefabContents(Aggregate);
            try
            {
                var content=aggregate.transform.Find("Content");if(content==null)throw new InvalidOperationException("正式聚合模板缺少Content");
                foreach(string name in new[]{"Burrower","Hound"})
                {
                    var old=content.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"Arcade"+name+".prefab");
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,content);instance.name=name;instance.SetActive(false);
                }
                PrefabUtility.SaveAsPrefabAsset(aggregate,Aggregate);
            }
            finally{PrefabUtility.UnloadPrefabContents(aggregate);}
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/HengeVolcanoPolish-v1-20261025/Creatures/Applied/bake.txt"));
            File.WriteAllLines(evidence,report);return string.Join("\n",report);
        }
        finally{SceneManager.SetActiveScene(before);EditorSceneManager.CloseScene(scene,true);}
    }

    /// <summary>只对采样器实际控制的节点写曲线，常量通道压为两点；不包含根变换。</summary>
    private static AnimationClip Bake(GameObject actor,int kind,string pose,float length,float offset)
    {
        var nodes=actor.GetComponentsInChildren<Transform>(true).Where(t=>t!=actor.transform&&(t.name=="Visual"||t.name=="Body"||t.name=="Neck"||t.name=="Head"||t.name=="Jaw"||t.name=="Tail"||t.name=="Tip"||t.parent.name.StartsWith("Leg",StringComparison.Ordinal)||t.parent.name=="Earth")).ToArray();
        if(pose=="BiteFace")nodes=nodes.Where(n=>n.name=="Neck"||n.name=="Head"||n.name=="Jaw").ToArray();
        var curves=nodes.Select(n=>Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray()).ToArray();
        var active=nodes.Select(n=>new AnimationCurve()).ToArray();
        int count=Mathf.CeilToInt(length*48);
        for(int sample=0;sample<=count;sample++)
        {
            float t=length*sample/count;
            if(pose=="Idle")BurrowerHoundPreview.Sample(actor,kind,0);
            else if((pose=="Bite"||pose=="BiteFace")&&kind==5)SampleHoundBite(actor,t);
            else BurrowerHoundPreview.Sample(actor,kind,offset+t);
            for(int i=0;i<nodes.Length;i++)
            {
                var n=nodes[i];var p=n.localPosition;var q=n.localRotation;var s=n.localScale;
                for(int a=0;a<3;a++){curves[i][a].AddKey(t,p[a]);curves[i][7+a].AddKey(t,s[a]);}
                for(int a=0;a<4;a++)curves[i][3+a].AddKey(t,q[a]);active[i].AddKey(t,n.gameObject.activeSelf?1:0);
            }
        }
        var clip=new AnimationClip{name=pose,frameRate=48};
        for(int i=0;i<nodes.Length;i++)
        {
            string path=AnimationUtility.CalculateTransformPath(nodes[i],actor.transform);
            for(int a=0;a<10;a++)
            {
                string property=a<3?"m_LocalPosition."+"xyz"[a]:a<7?"m_LocalRotation."+"xyzw"[a-3]:"m_LocalScale."+"xyz"[a-7];
                Curve(clip,path,typeof(Transform),property,curves[i][a],length,false);
            }
            Curve(clip,path,typeof(GameObject),"m_IsActive",active[i],length,true);
        }
        clip.EnsureQuaternionContinuity();return clip;
    }

    /// <summary>普通近战只取采用扑咬中的颈、头和下颌，四足保留地面支撑。</summary>
    private static void SampleHoundBite(GameObject actor,float t)
    {
        BurrowerHoundPreview.Sample(actor,5,3.62f+t);
        var selected=new[]{"Visual/Body/Neck","Visual/Body/Neck/Head","Visual/Body/Neck/Head/Jaw"}.Select(p=>actor.transform.Find(p)).ToArray();
        var positions=selected.Select(n=>n.localPosition).ToArray();var rotations=selected.Select(n=>n.localRotation).ToArray();
        BurrowerHoundPreview.Sample(actor,5,0);for(int i=0;i<selected.Length;i++){selected[i].localPosition=positions[i];selected[i].localRotation=rotations[i];}
    }

    private static void Curve(AnimationClip clip,string path,Type type,string property,AnimationCurve source,float length,bool discrete)
    {
        var values=source.keys;float first=values[0].value;
        if(values.All(k=>Mathf.Abs(k.value-first)<.000001f))source=new AnimationCurve(new Keyframe(0,first),new Keyframe(length,first));
        for(int k=0;k<source.length;k++)
        {AnimationUtility.SetKeyLeftTangentMode(source,k,discrete?AnimationUtility.TangentMode.Constant:AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(source,k,discrete?AnimationUtility.TangentMode.Constant:AnimationUtility.TangentMode.Linear);}
        clip.SetCurve(path,type,property,source);
    }

    private static Object Save(Object source,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<Object>(path);
        if(existing==null){AssetDatabase.CreateAsset(source,path);return source;}
        EditorUtility.CopySerialized(source,existing);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);if(source is AnimationClip)Object.DestroyImmediate(source);return existing;
    }

    private static void EnsureFolder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');EnsureFolder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
}
