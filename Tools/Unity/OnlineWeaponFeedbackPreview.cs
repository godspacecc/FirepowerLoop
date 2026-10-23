using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>隔离克隆现有单人反馈，不修改正式材质、模板或联机表现接线。</summary>
public static class OnlineWeaponFeedbackPreview
{
    public static string Run()
        =>Capture(false);
    /// <summary>右下保留橙色主体，仅去掉外围Sparks，候选仍在隔离场景。</summary>
    public static string RunRefined()=>Capture(true);
    private static string Capture(bool refined)
    {
        string output=refined?"Temp/OnlineWeaponFeedbackRefined/frames":"Temp/OnlineWeaponFeedbackPreview/frames";
        Directory.CreateDirectory(output);
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var rt=new RenderTexture(1000,650,24);var tex=new Texture2D(1000,650,TextureFormat.RGB24,false);
        Material groundMat=null;
        var oldRt=RenderTexture.active;
        try
        {
            var root=new GameObject("ExistingFeedbackClones");
            var view=new Game.ArcadeCombatViewController();
            var type=view.GetType();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            type.GetField("root",flags).SetValue(view,root);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            type.GetField("bulletImpactTemplate",flags).SetValue(view,prefab.transform.Find("Content/BulletImpact").GetComponent<ParticleSystem>());
            type.GetField("projectileBlastTemplate",flags).SetValue(view,prefab.transform.Find("Content/ProjectileBlast"));
            type.GetMethod("PlayBulletImpactBurst",flags).Invoke(view,new object[]{new Vector2(-3,2),Vector2.zero,false});
            type.GetMethod("PlayBulletImpactBurst",flags).Invoke(view,new object[]{new Vector2(3,2),Vector2.right,true});
            type.GetMethod("PlayProjectileBlast",flags).Invoke(view,new object[]{new Vector2(-3,-2),2.8f,false});
            type.GetMethod("PlayProjectileBlast",flags).Invoke(view,new object[]{new Vector2(3,-2),refined?2.8f:1.4f,!refined});
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.transform.localScale=Vector3.one*2;
            groundMat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));groundMat.SetColor("_BaseColor",new Color(.13f,.18f,.19f));ground.GetComponent<Renderer>().sharedMaterial=groundMat;
            var cam=new GameObject("FeedbackPreviewCamera").AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=6;cam.transform.rotation=Quaternion.Euler(72,0,0);cam.transform.position=Vector3.up*.75f-cam.transform.forward*18;cam.targetTexture=rt;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.03f,.04f,.05f);
            var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(55,-30,0);light.intensity=1.2f;
            var systems=root.GetComponentsInChildren<ParticleSystem>(true);
            for(int frame=0;frame<32;frame++)
            {
                float time=frame/20f;
                foreach(var ps in systems)
                {
                    if(refined&&ps.name=="Sparks"&&ps.transform.parent.localPosition.x>0)
                    {ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);continue;}
                    if(!refined&&(ps.name=="Burst"||ps.name=="Smoke"))
                    {if(ps.transform.root==root.transform&&ps.transform.parent.localPosition.x>0)continue;}
                    ps.useAutoRandomSeed=false;ps.randomSeed=123;
                    ps.Simulate(time% .8f,false,true,false);
                }
                cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1000,650),0,0);tex.Apply();File.WriteAllBytes(output+"/frame-"+frame.ToString("D3")+".png",tex.EncodeToPNG());
            }
            return Path.GetFullPath(output);
        }
        finally
        {
            RenderTexture.active=oldRt;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);if(groundMat!=null)Object.DestroyImmediate(groundMat);
            SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
