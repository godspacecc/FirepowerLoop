using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using FirepowerLoop.Arcade;

/// <summary>仅在临时Prefab内容场景采样正式骨骼碰撞体，不保存或修改正式美术。</summary>
public static class BakeDockHitPose
{
    public static string Run()
    {
        if(EditorApplication.isPlaying || EditorApplication.isCompiling || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许本工程空闲编辑态采样");
        var folder="E:/GitHub/FirepowerLoop/Backup/Artifacts/EnemyOutsideEntry-20261030";
        var csv=new StringBuilder("##var,Id,IntervalSeconds,FrameCount,CapsuleCount,Data\n##type,string,float,int,int,string\n");
        var audit=new StringBuilder("prefab,age,minY,maxY,capsules\n");
        foreach(var kind in new[]{"Shambler","Gaunt","Bloated"})
        {
            string address="Prefabs/Characters/ArcadeZombie"+kind;
            var go=PrefabUtility.LoadPrefabContents("Assets/Game/"+address+".prefab");
            try
            {
                var enemy=go.GetComponent<Game.EnemyView>();
                var clip=(AnimationClip)new SerializedObject(enemy).FindProperty("dockSeaEntryClip").objectReferenceValue;
                var animator=go.GetComponentInChildren<Animator>(true);
                var visual=animator.transform.parent;
                var hips=animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hips");
                var colliders=go.GetComponentsInChildren<CapsuleCollider>(true).OrderBy(c=>c.name,StringComparer.Ordinal).ToArray();
                if(clip==null||colliders.Length!=11)throw new InvalidOperationException(address+"缺少正式动作或11个骨骼体积");
                Vector3 rest=visual.localPosition;Quaternion rotation=visual.localRotation;
                var data=new StringBuilder();
                for(int frame=0;frame<=675;frame++)
                {
                    float age=frame*.01f;
                    visual.SetLocalPositionAndRotation(rest,rotation);
                    clip.SampleAnimation(animator.gameObject,age);
                    var pivot=go.transform.InverseTransformPoint(hips.position);
                    var pose=ArcadeDockSeaEntryRules.Sample(age);var lean=Quaternion.Euler(pose.Tilt,0,0);
                    visual.localRotation=lean*rotation;
                    visual.localPosition=pivot-lean*(pivot-rest)+Vector3.up*pose.Height;
                    float min=float.PositiveInfinity,max=float.NegativeInfinity;
                    foreach(var collider in colliders)
                    {
                        var axis=collider.direction==0?Vector3.right:collider.direction==1?Vector3.up:Vector3.forward;
                        var scale=collider.transform.lossyScale;
                        float along=Mathf.Abs(collider.direction==0?scale.x:collider.direction==1?scale.y:scale.z);
                        float across=collider.direction==0?Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)):collider.direction==1?Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z)):Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y));
                        float radius=collider.radius*across;
                        float half=Mathf.Max(0,collider.height*along*.5f-radius);
                        var center=collider.transform.TransformPoint(collider.center);
                        var direction=collider.transform.TransformDirection(axis).normalized;
                        var a=go.transform.InverseTransformPoint(center-direction*half);
                        var b=go.transform.InverseTransformPoint(center+direction*half);
                        foreach(float value in new[]{a.x,a.y,a.z,b.x,b.y,b.z,radius})
                        {if(data.Length>0)data.Append('|');data.Append(value.ToString("R",CultureInfo.InvariantCulture));}
                        min=Mathf.Min(min,a.y-radius,b.y-radius);max=Mathf.Max(max,a.y+radius,b.y+radius);
                    }
                    audit.AppendLine(string.Join(",",address,age.ToString("F2",CultureInfo.InvariantCulture),min.ToString("R",CultureInfo.InvariantCulture),max.ToString("R",CultureInfo.InvariantCulture),colliders.Length));
                }
                csv.AppendLine(","+address+",0.01,676,"+colliders.Length+","+data);
            }
            finally {PrefabUtility.UnloadPrefabContents(go);}
        }
        File.WriteAllText(folder+"/arcade_dock_hit_pose.candidate.csv",csv.ToString());
        File.WriteAllText(folder+"/pose-height-audit.csv",audit.ToString());
        return "3 formal actors x 676 poses x 11 capsules; candidate only";
    }
}
