using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
/// <summary>正式金币掉落与正式主角同框的独立临时预览，不修改正式素材。</summary>
public static class SurvivorXpPrefabPreview
{
    /// <summary>输出现有Prefab原始比例的静态比较图，经验用途尚待视觉确认。</summary>
    public static string Run()
    {
        if(EditorApplication.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("工程或状态不符");
        var previous=SceneManager.GetActiveScene();var active=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        RenderTexture rt=null;Texture2D image=null;
        try{
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));player.transform.position=new Vector3(-1.2f,0,0);
            var coin=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDropCoin.prefab"));coin.transform.position=new Vector3(1.2f,0,0);
            var bounds=new Bounds(Vector3.zero,Vector3.zero);bool first=true;
            foreach(var go in new[]{player,coin}){
                foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                foreach(var b in go.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
                foreach(var r in go.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
            }
            var light=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(50,-30,0);light.cullingMask=1<<31;
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=Mathf.Max(2,bounds.extents.magnitude*.8f);camera.transform.position=bounds.center+new Vector3(0,5,-8);camera.transform.LookAt(bounds.center);camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.17f,.14f);
            rt=new RenderTexture(1600,1000,24);image=new Texture2D(1600,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
            var path=Path.GetFullPath("../Backup/Artifacts/SurvivorDesign-20261029/xp-coin-prefab-v1.png");File.WriteAllBytes(path,image.EncodeToPNG());camera.targetTexture=null;return path;
        }
        finally{RenderTexture.active=active;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);if(rt!=null)Object.DestroyImmediate(rt);if(image!=null)Object.DestroyImmediate(image);}
    }
}
