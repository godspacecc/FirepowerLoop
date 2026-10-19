using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>采样正式共享持枪姿态及喷嘴火团，生成隔离预览；不启动对局或写入存档。</summary>
public static class ArcadeFlamethrowerPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态预览");
        var prior = SceneManager.GetActiveScene(); var oldTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var ambient = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode;
        RenderTexture target = null; Texture2D image = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.42f, .46f, .52f);
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var presenter = player.GetComponent<CharacterWeaponPresenter>();
            if (!presenter.SetWeaponId("arcade.weapon.flamethrower")) throw new Exception(presenter.LastError);
            presenter.SetManualPoseSampling(true);
            presenter.BodyAnimator.Rebind(); presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject, 0);
            presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            if (!presenter.TryGetProjectileOrigin(out var muzzle)) throw new Exception("缺少喷火器枪口");
            var config = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(
                File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            if (!config.TryGetArcadeCatalog(out var catalog, out var error)) throw new Exception(error);
            var weapon = System.Linq.Enumerable.Single(catalog.Weapons, w => w.Id == "arcade.weapon.flamethrower");
            // 按实际开火间隔推进完整粒子寿命，整团火焰始终由同一个喷嘴发射器形成。
            int pulses=Mathf.CeilToInt(weapon.ProjectileLifetimeSeconds/weapon.FireIntervalSeconds)+1;
            for(int pulse=0;pulse<pulses;pulse++)
            {
                presenter.PlayWeaponMuzzleEffect();
                foreach(var ps in presenter.Visual.GetComponentsInChildren<ParticleSystem>())
                {ps.Simulate(pulse==pulses-1?.025f:weapon.FireIntervalSeconds,false,false,false);ps.Pause();}
            }
            var bullet=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            // 按真实判定载体的寿命与散射采样软火团，重叠后的整体应是一个连续云团。
            for(float age=.025f;age<weapon.ProjectileLifetimeSeconds;age+=weapon.FireIntervalSeconds)
            for(int i=0;i<weapon.ProjectileCount;i++)
            {
                float angle=weapon.ProjectileCount<=1?0:-weapon.SpreadDegrees*.5f+weapon.SpreadDegrees*i/(weapon.ProjectileCount-1);
                var direction=Quaternion.Euler(0,angle,0)*Vector3.forward;
                var shot=Object.Instantiate(bullet);shot.GetComponent<ProjectileView>().SetArcadePresentation(weapon.ProjectileVisualResourceName,false);
                shot.GetComponent<ProjectileView>().ApplyPose(muzzle+direction*weapon.ProjectileSpeed*age,direction);
                foreach(var ps in shot.GetComponentsInChildren<ParticleSystem>()){ps.Simulate(age,false,true,false);ps.Pause();}
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(30, .1f, 30);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            var light = new GameObject("PreviewKey", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.7f; light.transform.eulerAngles = new Vector3(50, -35, 0); light.cullingMask = 1 << 30;
            var camera = new GameObject("FlamePreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.cullingMask = 1 << 30; camera.orthographic = true; camera.orthographicSize = 3.1f;
            var focus = new Vector3(0, .7f, 1.4f);
            camera.transform.position = focus + new Vector3(6, 8, -6); camera.transform.LookAt(focus);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .12f, .16f);
            target = new RenderTexture(1440, 900, 24); image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); image.Apply(); camera.targetTexture = null;
            string folder = Path.GetFullPath("Temp/FlamethrowerPreview"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "flamethrower.png"); File.WriteAllBytes(file, image.EncodeToPNG());
            return file;
        }
        finally
        {
            RenderTexture.active = oldTarget; RenderSettings.ambientMode = mode; RenderSettings.ambientLight = ambient;
            if (image != null) Object.DestroyImmediate(image); if (target != null) Object.DestroyImmediate(target);
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
