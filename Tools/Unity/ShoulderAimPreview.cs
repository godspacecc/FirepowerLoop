using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>隔离场景中渲染正式玩家与相机配置的两种构图；不保存场景、配置或玩家数据。</summary>
public static class ShoulderAimPreview
{
    public static string Render()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("预览仅用于编辑模式");
        const string scenePath = "Assets/Game/Scenes/BaseMap.unity";
        if (SceneManager.GetSceneByPath(scenePath).isLoaded)
            throw new InvalidOperationException("基地场景已打开，预览不接管其未保存状态");
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var graph = PlayableGraph.Create("ShoulderAimPreviewPose");
        var previousTarget = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        Camera camera = null;
        PlayerCameraCtrl ctrl = null;
        ShoulderCameraProfile profile = null;
        try
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var context in root.GetComponentsInChildren<MapContext>(true))
                    if (context.MapContentRoot != null) context.MapContentRoot.SetActive(true);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), scene);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var canvas in player.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
            foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.forceMatrixRecalculationPerRender = true;
            var presenter = player.GetComponentInChildren<CharacterWeaponPresenter>(true);
            var entry = presenter.Catalog.FindByWorldItemId(ItemIds.Bow);
            if (!presenter.SetWeaponId(entry.Id)) throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true);
            var animator = presenter.BodyAnimator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            var pose = entry.Animations.Idle;
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput.Create(graph, "Pose", animator)
                .SetSourcePlayable(AnimationClipPlayable.Create(graph, pose.Clip));
            graph.Play();
            graph.Evaluate(pose.Clip.length * .4f);
            presenter.ApplyOffhandGrip(pose);
            var cameraObject = new GameObject("ShoulderAimPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.55f, .67f, .75f);
            camera.targetTexture = target;
            var lightObject = new GameObject("ShoulderAimPreviewSun", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var sun = lightObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.8f;
            sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var view = player.GetComponent<PlayerView>();
            profile = Object.Instantiate(view.ShoulderCameraProfile);
            // 静态预览只跳过过渡时间，使用正式常态、瞄准及避障参数。
            profile.AimTransitionSeconds = 0f;
            ctrl = new PlayerCameraCtrl(camera, player.transform, view.CameraProfile, profile);
            var folder = Path.GetFullPath("Assets/Tests/Results~/ShoulderAim-20261011");
            Directory.CreateDirectory(folder);
            var report = "";
            foreach (var aiming in new[] { false, true })
            {
                ctrl.TrySetShoulderAim(aiming);
                ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, profile.DefaultPitchDegrees);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                var name = aiming ? "aim" : "travel";
                var file = Path.Combine(folder, name + ".png");
                File.WriteAllBytes(file, pixels.EncodeToPNG());
                var head = camera.WorldToViewportPoint(player.transform.position + Vector3.up * 1.6f);
                report += name + ": FOV=" + camera.fieldOfView + ", head=" + head + ", file=" + file + "\n";
            }
            return report;
        }
        finally
        {
            ctrl?.Dispose();
            graph.Destroy();
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = previousTarget;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            if (profile != null) Object.DestroyImmediate(profile);
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previousScene);
        }
    }
}
