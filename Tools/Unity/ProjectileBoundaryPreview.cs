using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>全地图开放边缘弹道的临时对比；人工轨迹只展示规则意图，不修改正式资源或模拟实际对局。</summary>
public static class ProjectileBoundaryPreview
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态制作临时预览");
        var prior=SceneManager.GetActiveScene();var oldRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var materials=new List<Material>();RenderTexture target=null;Texture2D pixels=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var cameras=new Camera[2];var shots=new GameObject[2,3];
            Color[] colors={new Color(1,.85f,.15f),new Color(.1f,.9f,1),new Color(1,.15f,.7f)};
            for(int side=0;side<2;side++)
            {
                float offset=side*100;
                var map=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab"));
                map.transform.position=new Vector3(offset,0,0);
                for(int kind=0;kind<3;kind++)
                {
                    var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.color=colors[kind];materials.Add(material);
                    var shot=GameObject.CreatePrimitive(PrimitiveType.Cube);Object.DestroyImmediate(shot.GetComponent<Collider>());
                    shot.GetComponent<Renderer>().sharedMaterial=material;shot.transform.localScale=new Vector3(kind==2?1.4f:.85f,.12f,.14f);shots[side,kind]=shot;
                }
                // 青色细线仅为说明旧外框的位置，不属于候选游戏表现。
                var lineMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));lineMaterial.color=new Color(.25f,.8f,.9f);materials.Add(lineMaterial);
                for(int dash=0;dash<12;dash++)
                {
                    var line=GameObject.CreatePrimitive(PrimitiveType.Cube);Object.DestroyImmediate(line.GetComponent<Collider>());
                    line.transform.position=new Vector3(offset+20.5f,.12f,-19+dash*3);line.transform.localScale=new Vector3(.06f,.04f,1.5f);line.GetComponent<Renderer>().sharedMaterial=lineMaterial;
                }
                var hero=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
                hero.transform.position=new Vector3(offset+4,0,-3);hero.transform.rotation=Quaternion.Euler(0,90,0);
                foreach(var canvas in hero.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
                var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();cameras[side]=camera;
                camera.orthographic=true;camera.orthographicSize=18;camera.aspect=1;camera.cullingMask=1<<30;
                camera.transform.position=new Vector3(offset+18,50,-29);camera.transform.LookAt(new Vector3(offset+18,0,-2));
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.14f,.19f);camera.nearClipPlane=.1f;camera.farClipPlane=200;
                camera.rect=new Rect(side*.5f,0,.5f,1);
            }
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var sun=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.cullingMask=1<<30;sun.transform.rotation=Quaternion.Euler(50,-30,0);
            target=new RenderTexture(1280,640,24);pixels=new Texture2D(1280,640,TextureFormat.RGB24,false);
            foreach(var camera in cameras)camera.targetTexture=target;
            string folder=Path.GetFullPath("Temp/ProjectileBoundaryPreview-v1");Directory.CreateDirectory(folder);
            for(int frame=0;frame<80;frame++)
            {
                float seconds=frame/20f;
                for(int side=0;side<2;side++)for(int kind=0;kind<3;kind++)
                {
                    float age=(seconds-kind*.14f)%1.8f;
                    float x=6+age*22;
                    var shot=shots[side,kind];shot.SetActive(age>=0&&age<1.45f&&(side==1||x<=20.5f));
                    shot.transform.position=new Vector3(side*100+x,1.1f,-5+kind*3);
                }
                foreach(var camera in cameras)camera.Render();
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,640),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            foreach(var camera in cameras)camera.targetTexture=null;
            return folder;
        }
        finally
        {
            RenderTexture.active=oldRT;
            if(pixels!=null)Object.DestroyImmediate(pixels);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var material in materials)if(material!=null)Object.DestroyImmediate(material);
        }
    }
}
