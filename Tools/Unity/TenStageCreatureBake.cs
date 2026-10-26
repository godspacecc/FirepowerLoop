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

/// <summary>将已采用的四种模型和局部姿态持久化；不采用旧巨猿，不写战斗位置。</summary>
public static class TenStageCreatureBake
{
    public const string Aggregate="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    public static string Name(int kind)=>kind==6?"Spider":kind==10?"Ghoul":kind==17?"RedMangler":"ZRex";
    public static string Art(int kind)=>"Assets/Game/Art/Enemies/Creatures/"+Name(kind);
    public static string Prefab(int kind)=>"Assets/Game/Prefabs/Characters/Arcade"+Name(kind)+".prefab";
    public static readonly int[] Kinds={6,10,17,2};
    /// <summary>片段只截取采用源对应动作；正式窗口由现有权威参数决定。</summary>
    public static (string name,float offset,float length,bool upper)[] Specs(int kind)
    {
        if(kind==6)return new[]{("Idle",4.2f,1f,false),("Move",0f,4f/9,false),("Windup",4.2f,.55f,false),("Action",4.75f,.3f,false),("Recovery",5.05f,.5f,false),("Rush",0f,4f/9,false)};
        if(kind==10)return new[]{("Idle",0f,1f,false),("Move",0f,2f/3,false),("Windup",2f,.9f,true),("Action",5f,.53f,true),("Recovery",5.53f,1.27f,true),("Rush",3.2f,.55f,true)};
        if(kind==17)return new[]{("Idle",0f,1f,false),("Move",0f,.8f,false),("Windup",2f,1.05f,true),("Action",3.08f,.17f,true),("Recovery",3.25f,.55f,true),("Rush",3.55f,.7f,true)};
        return new[]{("Idle",0f,1f,false),("Move",0f,.8f,false),("Windup",2f,1.1f,true),("Action",5.3f,.6f,true),("Recovery",6.5f,.5f,true),("Rush",3.35f,.9f,true)};
    }
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="F:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅允许当前工程空闲编辑态");
        var before=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var report=new List<string>();
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach(int kind in Kinds)report.Add(BakeActor(kind));
            var aggregate=PrefabUtility.LoadPrefabContents(Aggregate);
            try
            {
                var content=aggregate.transform.Find("Content");if(content==null)throw new InvalidOperationException("缺少Content模板根");
                foreach(int kind in Kinds)
                {
                    var old=content.Find(Name(kind));if(old!=null)Object.DestroyImmediate(old.gameObject);
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(kind)),content);instance.name=Name(kind);instance.SetActive(false);
                }
                PrefabUtility.SaveAsPrefabAsset(aggregate,Aggregate);
            }
            finally{PrefabUtility.UnloadPrefabContents(aggregate);}
            AssetDatabase.SaveAssets();string result=string.Join("\n",report);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/TenStageCreaturesApplied-20261026/bake.txt")),result);return result;
        }
        finally{SceneManager.SetActiveScene(before);EditorSceneManager.CloseScene(scene,true);}
    }
    private static string BakeActor(int kind)
    {
        var owned=new List<Object>();var clips=new List<AnimationClip>();GameObject actor=null;
        try
        {
            actor=TenStageCreatureCandidates.Create(kind==2?0:kind,kind==2?2:0,null,owned);actor.name="Arcade"+Name(kind);
            foreach(var spec in Specs(kind))clips.Add(BakeClip(actor,kind,spec));
            EnsureFolder(Art(kind));int meshes=0,materials=0;AnimationClip[] savedClips;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var obj in owned)
                {
                    string path=Art(kind)+"/"+(obj is Mesh?"Mesh"+(meshes++):"Material"+(materials++))+(obj is Mesh?".asset":".mat");var saved=Save(obj,path);
                    if(saved==obj)continue;
                    foreach(var f in actor.GetComponentsInChildren<MeshFilter>())if(f.sharedMesh==obj)f.sharedMesh=(Mesh)saved;
                    foreach(var r in actor.GetComponentsInChildren<Renderer>())if(r.sharedMaterial==obj)r.sharedMaterial=(Material)saved;
                }
                savedClips=clips.Select(c=>(AnimationClip)Save(c,Art(kind)+"/"+c.name+".anim")).ToArray();
            }
            finally{AssetDatabase.StopAssetEditing();}
            Sample(actor,kind,Specs(kind)[0],0);
            var view=actor.AddComponent<ArcadeAdoptedCreatureView>();var so=new SerializedObject(view);var poses=so.FindProperty("poses");poses.arraySize=savedClips.Length;
            for(int i=0;i<savedClips.Length;i++)poses.GetArrayElementAtIndex(i).objectReferenceValue=savedClips[i];so.ApplyModifiedPropertiesWithoutUndo();
            if(!view.IsReady)throw new InvalidOperationException("正式动作不完整");PrefabUtility.SaveAsPrefabAsset(actor,Prefab(kind));
            return Name(kind)+": "+meshes+" meshes, "+actor.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)+" triangles, "+materials+" materials, 6 clips; no actor root curves.";
        }
        finally{if(actor!=null)Object.DestroyImmediate(actor);foreach(var obj in owned.Concat(clips))if(obj!=null&&!EditorUtility.IsPersistent(obj))Object.DestroyImmediate(obj);}
    }
    /// <summary>上身动作去除预览步态与髋部升降，手臂同步平移回基准髋高；下肢另按实际路程采样。</summary>
    public static void Sample(GameObject actor,int kind,(string name,float offset,float length,bool upper) spec,float time)
    {
        TenStageCreatureCandidates.Sample(actor,kind==2?0:kind,kind==2?2:0,spec.offset+(spec.name=="Idle"?0:time));
        if(!spec.upper)return;
        var core=actor.transform.Find("Body/Core");Vector3 baseHip=kind==10?new Vector3(0,.92f,-.08f):kind==17?new Vector3(0,1,-.02f):new Vector3(0,1.55f,-.55f);
        Vector3 delta=core.localPosition-baseHip;core.localPosition=baseHip;
        foreach(string side in new[]{"L","R"})foreach(string part in new[]{"Upper","Lower","Elbow","Hand"})actor.transform.Find("Body/Arm"+side+"/"+part).localPosition-=delta;
    }
    public static Transform[] Nodes(GameObject actor,bool upper)
    {
        return actor.GetComponentsInChildren<Transform>().Where(t=>t!=actor.transform&&
            (!upper||AnimationUtility.CalculateTransformPath(t,actor.transform).StartsWith("Body/Core",StringComparison.Ordinal)||AnimationUtility.CalculateTransformPath(t,actor.transform).StartsWith("Body/Arm",StringComparison.Ordinal))).ToArray();
    }
    private static AnimationClip BakeClip(GameObject actor,int kind,(string name,float offset,float length,bool upper) spec)
    {
        var nodes=Nodes(actor,spec.upper);var curves=nodes.Select(n=>Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray()).ToArray();int count=Mathf.CeilToInt(spec.length*48);
        for(int sample=0;sample<=count;sample++)
        {
            float time=spec.length*sample/count;Sample(actor,kind,spec,time);
            for(int i=0;i<nodes.Length;i++)
            {var n=nodes[i];for(int a=0;a<3;a++){curves[i][a].AddKey(time,n.localPosition[a]);curves[i][a+7].AddKey(time,n.localScale[a]);}for(int a=0;a<4;a++)curves[i][a+3].AddKey(time,n.localRotation[a]);}
        }
        var clip=new AnimationClip{name=spec.name,frameRate=48};
        for(int i=0;i<nodes.Length;i++)for(int a=0;a<10;a++)
        {
            // 身体平移来自步态；上身覆盖不改髋高，也不改局部平面根。
            if(spec.upper&&nodes[i].name=="Core"&&a<3)continue;
            var curve=curves[i][a];float first=curve.keys[0].value;if(curve.keys.All(k=>Mathf.Abs(k.value-first)<.000001f))curve=new AnimationCurve(new Keyframe(0,first),new Keyframe(spec.length,first));
            for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
            clip.SetCurve(AnimationUtility.CalculateTransformPath(nodes[i],actor.transform),typeof(Transform),a<3?"m_LocalPosition."+"xyz"[a]:a<7?"m_LocalRotation."+"xyzw"[a-3]:"m_LocalScale."+"xyz"[a-7],curve);
        }
        clip.EnsureQuaternionContinuity();return clip;
    }
    private static Object Save(Object source,string path)
    {source.name=Path.GetFileNameWithoutExtension(path);var existing=AssetDatabase.LoadAssetAtPath<Object>(path);if(existing==null){AssetDatabase.CreateAsset(source,path);return source;}EditorUtility.CopySerialized(source,existing);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);return existing;}
    private static void EnsureFolder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');EnsureFolder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
}
