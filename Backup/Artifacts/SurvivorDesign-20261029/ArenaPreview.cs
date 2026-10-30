using System;
using System.IO;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Game;
using Luban;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
/// <summary>隔离场景中的正式素材组合预览，不保存或修改正式场景和资源。</summary>
public static class SurvivorArenaPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("工程或状态不符");
        var tables = new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
        var config = SurvivorConfiguration.FromTables(tables);
        if (config.LayoutId != "arena.1.round.1") throw new Exception("预览仅适用于当前已选海岛第一轮");
        var prior = SceneManager.GetActiveScene(); var oldRt = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var meshes = new List<Mesh>(); RenderTexture rt = null; Texture2D image = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeIslandVisualSet.prefab");
            var island = Object.Instantiate(prefab); island.SetActive(true);
            float width = tables.TbArcadeRule.DataList[0].ArenaHalfWidth, height = tables.TbArcadeRule.DataList[0].ArenaHalfHeight;
            var region = new ArcadeIslandRegion(width,height,1);
            meshes.Add(ArcadeIslandMesh.Sand(region)); meshes.Add(ArcadeIslandMesh.Water(region)); meshes.Add(ArcadeIslandMesh.Shoreline(region));
            string[] surfaces = {"Sand","Water","Shoreline"};
            var rounds = island.transform.Find("FixedRounds"); rounds.gameObject.SetActive(true); rounds.localScale = new Vector3(width/19.5f,1,height/19.5f);
            for(int i=0;i<rounds.childCount;i++) rounds.GetChild(i).gameObject.SetActive(rounds.GetChild(i).name=="Round1");
            var round = rounds.Find("Round1"); var palette = round.Find("SurfacePalette").GetComponent<Renderer>().sharedMaterials;
            for(int i=0;i<3;i++){var surface=island.transform.Find(surfaces[i]);surface.GetComponent<MeshFilter>().sharedMesh=meshes[i];surface.GetComponent<Renderer>().sharedMaterial=palette[i];}
            island.transform.Find("Ocean").GetComponent<Renderer>().sharedMaterial=round.Find("WaterPalette").GetComponent<Renderer>().sharedMaterial;
            foreach(string name in new[]{"Campfire","ShoreDecor","CoastDecor"}) island.transform.Find(name)?.gameObject.SetActive(false);
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            player.transform.position=new Vector3(0,0,-8); player.name="正式主角_保留Prefab比例";
            foreach(var go in new[]{island,player})foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            foreach(var go in new[]{island,player})foreach(var b in go.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            var light=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(50,-30,0);light.cullingMask=1<<31;
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>(); camera.orthographic=true;camera.orthographicSize=27;camera.transform.position=new Vector3(0,42,-35);camera.transform.LookAt(Vector3.zero);camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.25f,.31f);
            rt=new RenderTexture(1600,1000,24);image=new Texture2D(1600,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;
            var folder=Path.GetFullPath("../Backup/Artifacts/SurvivorDesign-20261029");Directory.CreateDirectory(folder);
            camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"arena-with-player-preview.png"),image.EncodeToPNG());
            camera.orthographicSize=8;camera.transform.position=new Vector3(0,14,-22);camera.transform.LookAt(new Vector3(0,0,-8));camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"arena-player-close-preview.png"),image.EncodeToPNG());
            camera.targetTexture=null;return folder;
        }
        finally
        {
            RenderTexture.active=oldRt;SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            if(rt!=null)Object.DestroyImmediate(rt);if(image!=null)Object.DestroyImmediate(image);foreach(var mesh in meshes)Object.DestroyImmediate(mesh);
        }
    }
}
