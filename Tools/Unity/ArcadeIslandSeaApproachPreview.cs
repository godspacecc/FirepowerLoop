using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>以正式生成、移动链路和资源隔离观察深海来敌；不推进存档或进入正式战斗。</summary>
public static class ArcadeIslandSeaApproachPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本工程编辑态隔离观察");
        var fixtureType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.Tests.IslandSeaApproachTests")).First(t => t != null);
        var fixture = Activator.CreateInstance(fixtureType);
        fixtureType.GetMethod("Setup").Invoke(fixture, null);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var enemy = fixtureType.GetMethod("Spawn", flags).Invoke(fixture, new object[] { 0, 0 });
        var runtime = fixtureType.GetField("runtime", flags).GetValue(fixture);
        var system = fixtureType.GetField("system", flags).GetValue(fixture);
        var advance = system.GetType().GetMethod("TickEnemies", flags);
        var region = new ArcadeIslandRegion(19.5f, 19.5f, 1);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("SeaApproachPreview"); SceneManager.MoveGameObjectToScene(root, scene);
        var meshes = new List<Mesh>();
        RenderTexture target = null; Texture2D pixels = null; Camera camera = null;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/IslandSeaApproachPreview"));
        Directory.CreateDirectory(folder);
        var captures = new List<string>();
        Vector2 Position() => (Vector2)enemy.GetType().GetField("Position").GetValue(enemy);
        try
        {
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            var island = Object.Instantiate(kit.transform.Find("Island").gameObject, root.transform, false).transform;
            island.gameObject.SetActive(true);
            string[] surfaces = { "Sand", "Water", "Shoreline" };
            meshes.Add(ArcadeIslandMesh.Sand(region)); meshes.Add(ArcadeIslandMesh.Water(region)); meshes.Add(ArcadeIslandMesh.Shoreline(region));
            for (int i = 0; i < surfaces.Length; i++)
            {
                var surface = island.Find(surfaces[i]); surface.localScale = Vector3.one;
                surface.localPosition = Vector3.up * (i == 2 ? .024f : .02f); surface.GetComponent<MeshFilter>().sharedMesh = meshes[i];
            }
            island.Find("Ocean").localScale = new Vector3(78, .05f, 78);
            island.Find("Campfire").gameObject.SetActive(false); island.Find("ShoreDecor").gameObject.SetActive(false);
            var cameraObject = new GameObject("SeaApproachCamera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.nearClipPlane = .1f; camera.farClipPlane = 150;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .17f, .22f);
            camera.fieldOfView = 45f; camera.transform.rotation = Quaternion.Euler(72, 0, 0);
            camera.transform.position = new Vector3(Position().x * .5f, .6f, -19) - camera.transform.forward * 24;
            var lightObject = new GameObject("SeaApproachLight", typeof(Light)); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");
            var actor = Object.Instantiate(prefab, root.transform, false).GetComponent<EnemyView>();
            actor.BeginPresentationUse(); actor.SetNavigationEnabled(false); actor.SetPresentationVariant((int)enemy.GetType().GetField("Id").GetValue(enemy));
            actor.Animator.Rebind(); actor.Animator.Update(0f); actor.SetMovementSpeed((float)enemy.GetType().GetField("Speed").GetValue(enemy));
            target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            void Capture(string name, float time)
            {
                camera.Render(); var prior = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply(); RenderTexture.active = prior;
                File.WriteAllBytes(Path.Combine(folder, name), pixels.EncodeToPNG());
                captures.Add(name + " t=" + time.ToString("F2") + " position=" + Position());
            }
            bool water = false, land = false;
            for (int frame = 0; frame < 800 && !land; frame++)
            {
                if (frame > 0) advance.Invoke(system, new object[] { runtime, .05f });
                actor.transform.localPosition = new Vector3(Position().x, 0, Position().y);
                actor.transform.localRotation = Quaternion.LookRotation(new Vector3(-Position().x, 0, -Position().y));
                actor.Animator.Update(.05f);
                if (frame == 0) Capture("01-deep-sea.png", 0);
                if (!water && region.IsShallow(Position().x, Position().y)) { water = true; Capture("02-shallow-water.png", frame * .05f); }
                if (water && region.Contains(Position().x, Position().y) && !region.IsShallow(Position().x, Position().y))
                { land = true; Capture("03-on-land.png", frame * .05f); }
            }
            if (!water || !land) throw new InvalidOperationException("正式移动未完成深海到浅水再到沙地");
            string report = "Editor isolated preview: formal single-player spawn/movement and map/enemy assets; fixed inspection camera; not live gameplay.\n" + string.Join("\n", captures);
            File.WriteAllText(Path.Combine(folder, "verification.txt"), report);
            return folder + "\n" + report;
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }
}
