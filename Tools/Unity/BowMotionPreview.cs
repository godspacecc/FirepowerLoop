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

/// <summary>隔离场景采样正式弓的完整放箭动作，逐帧渲染侧面；不保存场景或玩家状态。</summary>
public static class BowMotionPreview
{
    public static string Render()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var graph = PlayableGraph.Create("BowMotionPreview");
        var target = new RenderTexture(720, 720, 24);
        var image = new Texture2D(720, 720, TextureFormat.RGB24, false);
        var previousTarget = RenderTexture.active;
        Camera camera = null;
        CharacterWeaponPresenter presenter = null;
        try
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), scene);
            presenter = player.GetComponentInChildren<CharacterWeaponPresenter>(true);
            var entry = presenter.Catalog.FindByWorldItemId(35);
            if (!presenter.SetWeaponId(entry.Id)) throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true);
            var animator = presenter.BodyAnimator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.Rebind(); animator.Update(0f);
            foreach (var canvas in player.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
            foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.forceMatrixRecalculationPerRender = true;
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var clip = AnimationClipPlayable.Create(graph, entry.Animations.Attack.Clip);
            AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(clip);
            graph.Play();
            var cameraObject = new GameObject("BowSideCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.17f, .19f, .21f);
            camera.orthographic = true; camera.orthographicSize = 1.08f;
            camera.transform.position = new Vector3(-.22f, 1.1f, 4f);
            camera.transform.LookAt(new Vector3(-.22f, 1.1f, 0f));
            var lightObject = new GameObject("BowSideLight", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f;
            light.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var folder = Path.GetFullPath("Assets/Tests/Results~/BowMotion-20261011");
            Directory.CreateDirectory(folder);
            for (var frame = 0; frame < 40; frame++)
            {
                var time = frame / 39f;
                clip.SetTime(entry.Animations.Attack.Clip.length * time);
                graph.Evaluate(.0001f);
                presenter.ApplyOffhandGrip(entry.Animations.Attack, time);
                camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 720, 720), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(folder, "frame-" + frame.ToString("D2") + ".png"), image.EncodeToPNG());
            }
            return folder;
        }
        finally
        {
            presenter?.Visual?.GetComponentInChildren<BowVisualView>()?.Dispose();
            graph.Destroy();
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = previousTarget; target.Release();
            Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
