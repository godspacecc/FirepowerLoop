using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>隔离场景中以正式轮廓、角色和涉水网格观察登陆；不运行存档、战斗伤害或Player Build。</summary>
public static class ArcadeIslandWadingPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本工程编辑态隔离观察");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("WadingPreviewMap"); SceneManager.MoveGameObjectToScene(root, scene);
        var meshes = new List<Mesh>(); var actors = new List<EnemyView>(); var positions = new List<Vector2>();
        RenderTexture target = null; Texture2D pixels = null; Camera camera = null; IDisposable effect = null;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Docs/Plans/Artifacts/IslandWading-20261018"));
        Directory.CreateDirectory(folder);
        try
        {
            var region = new ArcadeIslandRegion(19.5f, 19.5f, 1);
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
            var cameraObject = new GameObject("WadingPreviewCamera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.nearClipPlane = .1f; camera.farClipPlane = 150;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .17f, .22f);
            var lightObject = new GameObject("WadingPreviewLight", typeof(Light)); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var type = typeof(ArcadeCombatViewController).Assembly.GetType("Game.ArcadeIslandWadingPresentation");
            effect = (IDisposable)Activator.CreateInstance(type, root.transform, region, Shader.Find("FirepowerLoop/BombAdditive"), true, null);
            var set = type.GetMethod("Set"); var tick = type.GetMethod("Tick");
            var count = type.GetProperty("ActivePulseCount");
            target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            void Capture(string name, bool overview, Vector2 follow)
            {
                camera.orthographic = overview; camera.orthographicSize = 23f; camera.fieldOfView = 45f;
                if (overview) { camera.transform.position = new Vector3(0, 44, -25); camera.transform.LookAt(Vector3.zero); }
                else
                {
                    camera.transform.rotation = Quaternion.Euler(72, 0, 0);
                    camera.transform.position = new Vector3(follow.x, .6f, follow.y) - camera.transform.forward * 16;
                }
                camera.Render(); var prior = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply(); RenderTexture.active = prior;
                File.WriteAllBytes(Path.Combine(folder, name), pixels.EncodeToPNG());
            }
            int maximum = 0, next = 0; float nextSpawn = 0;
            for (int frame = 0; frame < 240; frame++)
            {
                float elapsed = frame * .02f;
                if (elapsed + .0001f >= nextSpawn)
                {
                    region.EntryFromFourSides(next, (next / 4 % 5) * .25f, .95f, out var start);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab");
                    var go = Object.Instantiate(prefab, root.transform, false); var view = go.GetComponent<EnemyView>();
                    view.BeginPresentationUse(); view.SetNavigationEnabled(false); view.SetPresentationVariant(next + 1);
                    view.Animator.Rebind(); view.Animator.Update(0f);
                    actors.Add(view); positions.Add(new Vector2(start.X, start.Y)); next++; nextSpawn += .45f;
                    set.Invoke(effect, new object[] { next, positions[next - 1] });
                }
                for (int i = 0; i < actors.Count; i++)
                {
                    var before = positions[i]; var step = -before.normalized * (2.8f * .02f);
                    var moved = region.Move(before.x, before.y, step.x, step.y, .95f); positions[i] = new Vector2(moved.X, moved.Y);
                    var go = actors[i].transform; go.localPosition = new Vector3(moved.X, .6f, moved.Y);
                    go.localRotation = Quaternion.LookRotation(new Vector3(step.x, 0, step.y));
                    actors[i].SetMovementSpeed(2.8f); actors[i].Animator.Update(.02f);
                    set.Invoke(effect, new object[] { i + 1, positions[i] });
                }
                tick.Invoke(effect, new object[] { .02f }); maximum = Math.Max(maximum, (int)count.GetValue(effect));
                if (frame == 49) { Capture("01-four-entrances-overview-100.png", true, Vector2.zero); Capture("02-start-camera-100.png", false, new Vector2(-3, -8)); }
                if (frame == 114) Capture("03-shore-camera-230.png", false, new Vector2(-3, -11));
                if (frame == 139) Capture("04-shore-camera-280.png", false, new Vector2(-3, -11));
            }
            string report = "{\"scope\":\"Editor isolated visual preview; formal geometry/models, fixed 0.45s four-side cadence; not a live battle or phone\",\"simulatedFrames\":240,\"spawned\":" + actors.Count + ",\"mobilePeakPulses\":" + maximum + ",\"mobileBudget\":20,\"effectRenderers\":1}";
            File.WriteAllText(Path.Combine(folder, "verification.json"), report);
            return report + "\n" + folder;
        }
        finally
        {
            effect?.Dispose(); if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }
}
