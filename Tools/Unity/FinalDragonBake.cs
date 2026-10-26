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

/// <summary>把用户采用的FinalDragonModel v1持久化；保留根位移权威，不修改场景或玩法。</summary>
public static class FinalDragonBake
{
    private const string Art="Assets/Game/Art/Enemies/Creatures/FinalDragon";
    private const string Prefab="Assets/Game/Prefabs/Characters/ArcadeFinalDragon.prefab";
    private const string Aggregate="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="F:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)
            throw new InvalidOperationException("仅允许FirepowerLoop空闲编辑态烘焙");
        var before=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        FinalDragonCandidate.Rig rig=null;var clips=new List<AnimationClip>();
        try
        {
            SceneManager.SetActiveScene(scene);rig=FinalDragonCandidate.Create(null);rig.Root.name="ArcadeFinalDragon";
            if(rig.Root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)!=3808)throw new InvalidOperationException("采用版几何发生变化");
            foreach(var spec in new[]{("Idle",1.5f,0f),("Move",1.05f,2f),("Windup",.9f,4.5f),("Charge",.76f,5.4f),("Roar",2.1f,7.4f),("Wings",2.2f,9.5f)})
                clips.Add(Bake(rig,spec.Item1,spec.Item2,spec.Item3));
            clips.Add(Bake(rig,"RoarUpper",2.1f,7.4f));clips.Add(Bake(rig,"WingsUpper",2.2f,9.5f));
            if(clips.Any(c=>AnimationUtility.GetCurveBindings(c).Any(b=>string.IsNullOrEmpty(b.path))))throw new InvalidOperationException("曲线不得包含演员根");
            EnsureFolder(Art);int meshCount=0,materialCount=0;
            AnimationClip[] savedClips;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var obj in rig.Owned)
                {
                    string path=Art+"/"+(obj is Mesh?"Mesh"+(meshCount++):"Material"+(materialCount++))+(obj is Mesh?".asset":".mat");
                    var saved=Save(obj,path);
                    if(saved==obj)continue;
                    foreach(var f in rig.Root.GetComponentsInChildren<MeshFilter>())if(f.sharedMesh==obj)f.sharedMesh=(Mesh)saved;
                    foreach(var r in rig.Root.GetComponentsInChildren<Renderer>())if(r.sharedMaterial==obj)r.sharedMaterial=(Material)saved;
                }
                savedClips=clips.Select(c=>(AnimationClip)Save(c,Art+"/"+c.name+".anim")).ToArray();
            }
            finally{AssetDatabase.StopAssetEditing();}
            FinalDragonCandidate.Sample(rig,0);rig.Root.transform.localPosition=Vector3.zero;
            var view=rig.Root.AddComponent<ArcadeFinalDragonView>();var so=new SerializedObject(view);var poses=so.FindProperty("poses");poses.arraySize=savedClips.Length;
            for(int i=0;i<savedClips.Length;i++)poses.GetArrayElementAtIndex(i).objectReferenceValue=savedClips[i];so.ApplyModifiedPropertiesWithoutUndo();
            if(!view.IsReady)throw new InvalidOperationException("正式片段不完整");
            PrefabUtility.SaveAsPrefabAsset(rig.Root,Prefab);
            var aggregate=PrefabUtility.LoadPrefabContents(Aggregate);
            try
            {
                var content=aggregate.transform.Find("Content");if(content==null)throw new InvalidOperationException("聚合缺少Content");
                var old=content.Find("FinalDragon");if(old!=null)Object.DestroyImmediate(old.gameObject);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab),content);instance.name="FinalDragon";instance.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(aggregate,Aggregate);
            }
            finally{PrefabUtility.UnloadPrefabContents(aggregate);}
            string report=$"Approved geometry: {meshCount} meshes, 3808 triangles, {materialCount} materials, {savedClips.Length} clips; no root curves; aggregate bound.";
            File.WriteAllText(Path.Combine(Application.dataPath,"../../Backup/Artifacts/FinalDragonApplied-20261026/bake.txt"),report);return report;
        }
        finally
        {
            if(rig!=null){Object.DestroyImmediate(rig.Root);foreach(var obj in rig.Owned)if(obj!=null&&!EditorUtility.IsPersistent(obj))Object.DestroyImmediate(obj);}
            foreach(var clip in clips)if(clip!=null&&!EditorUtility.IsPersistent(clip))Object.DestroyImmediate(clip);
            SceneManager.SetActiveScene(before);EditorSceneManager.CloseScene(scene,true);
        }
    }
    /// <summary>只取骨节局部姿态；预览根轨迹永远不写入正式片段。</summary>
    private static AnimationClip Bake(FinalDragonCandidate.Rig rig,string name,float length,float offset)
    {
        var nodes=new List<Transform>{rig.Body,rig.Neck,rig.Head,rig.Jaw};nodes.AddRange(rig.Tail);nodes.AddRange(rig.Wings);
        foreach(var leg in rig.Legs)nodes.AddRange(new[]{leg.Upper,leg.Lower,leg.Foot});
        if(name.EndsWith("Upper",StringComparison.Ordinal))nodes=nodes.Where(n=>n==rig.Neck||n==rig.Head||n==rig.Jaw||rig.Wings.Contains(n)).ToList();
        var curves=nodes.Select(n=>Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray()).ToArray();
        int count=Mathf.CeilToInt(length*48);
        for(int sample=0;sample<=count;sample++)
        {
            float t=length*sample/count;FinalDragonCandidate.Sample(rig,offset+t);
            for(int i=0;i<nodes.Count;i++)
            {
                var n=nodes[i];var p=n.localPosition;var q=n.localRotation;var s=n.localScale;
                for(int a=0;a<3;a++){curves[i][a].AddKey(t,p[a]);curves[i][a+7].AddKey(t,s[a]);}
                for(int a=0;a<4;a++)curves[i][a+3].AddKey(t,q[a]);
            }
        }
        var clip=new AnimationClip{name=name,frameRate=48};
        for(int i=0;i<nodes.Count;i++)for(int a=0;a<10;a++)
        {
            var curve=curves[i][a];float first=curve.keys[0].value;
            if(curve.keys.All(k=>Mathf.Abs(k.value-first)<.000001f))curve=new AnimationCurve(new Keyframe(0,first),new Keyframe(length,first));
            for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
            clip.SetCurve(AnimationUtility.CalculateTransformPath(nodes[i],rig.Root.transform),typeof(Transform),a<3?"m_LocalPosition."+"xyz"[a]:a<7?"m_LocalRotation."+"xyzw"[a-3]:"m_LocalScale."+"xyz"[a-7],curve);
        }
        clip.EnsureQuaternionContinuity();return clip;
    }
    private static Object Save(Object source,string path)
    {
        source.name=Path.GetFileNameWithoutExtension(path);
        var existing=AssetDatabase.LoadAssetAtPath<Object>(path);if(existing==null){AssetDatabase.CreateAsset(source,path);return source;}
        EditorUtility.CopySerialized(source,existing);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);return existing;
    }
    private static void EnsureFolder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');EnsureFolder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
}
