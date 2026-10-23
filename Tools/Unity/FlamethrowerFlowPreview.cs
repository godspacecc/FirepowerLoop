using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>隔离重放动画/粒子/帧末校正顺序，仅比较近端火焰；不改正式资源或自动运行对局。</summary>
public static class FlamethrowerFlowPreview
{
    /// <summary>左侧按当前PlayerLoop顺序采样，右侧只在临时实例中先校正再推进粒子。</summary>
    public static string Run() => Capture(false);

    /// <summary>右侧通过正式帧末入口推进已接入的修复，左侧只在临时实例重放旧时序。</summary>
    public static string RunApplied() => Capture(true);

    private static string Capture(bool applied)
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态预览");
        var scene = EditorSceneManager.NewPreviewScene();
        var owned = new List<Object>(); var previous = RenderTexture.active;
        RenderTexture target = null; Texture2D pixels = null;
        GameObject New(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        Material Mat(Color color) { var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.color = color; owned.Add(m); return m; }
        try
        {
            var floorMat = Mat(new Color(.16f, .19f, .18f)); var markerMat = Mat(new Color(.26f, .30f, .28f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0, -.12f, 8); floor.transform.localScale = new Vector3(50, .2f, 45);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            for (int z = -4; z < 25; z += 2)
            {
                var mark = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(mark, scene);
                mark.transform.position = new Vector3(0, -.008f, z); mark.transform.localScale = new Vector3(40, .015f, .025f);
                mark.GetComponent<Renderer>().sharedMaterial = markerMat;
            }
            var light = New("Key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var camera = New("FlowCamera").AddComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 3.05f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .1f, .09f);
            camera.cullingMask = ~(1 << 5);
            var actors = new GameObject[2]; var presenters = new CharacterWeaponPresenter[2]; var flames = new ParticleSystem[2];
            for (int variant = 0; variant < 2; variant++)
            {
                var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
                SceneManager.MoveGameObjectToScene(actor, scene); actor.transform.position = new Vector3(variant == 0 ? -2.8f : 2.8f, 0, 0);
                actor.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);
                var p = actor.GetComponent<CharacterWeaponPresenter>();
                if (!p.SetWeaponId("arcade.weapon.flamethrower")) throw new Exception(p.LastError);
                p.BodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate; p.BodyAnimator.Rebind(); p.BodyAnimator.Update(.05f); p.ApplyOffhandGrip();
                foreach (var c in actor.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);
                var ps = p.Visual.GetComponentInChildren<ParticleSystem>(); ps.useAutoRandomSeed = false; ps.randomSeed = 123;
                actors[variant] = actor; presenters[variant] = p; flames[variant] = ps;
            }
            // 当前正式移动速度从权威CSV读取，只用于移动临时实例，不向游戏注入配置。
            var lines = File.ReadAllLines("../Config/DataCsv/arcade_rule.csv"); var headers = lines[0].Split(','); var values = lines[2].Split(',');
            float speed = float.Parse(values[Array.IndexOf(headers, "PlayerMoveSpeed")], System.Globalization.CultureInfo.InvariantCulture);
            var labels = new List<KeyValuePair<TextMesh, Vector2>>();
            void Label(string text, float x, float y, int size)
            { var t = New("Caption").AddComponent<TextMesh>(); t.text = text; t.anchor = TextAnchor.MiddleCenter; t.fontSize = size; t.characterSize = .035f; t.color = Color.white; labels.Add(new KeyValuePair<TextMesh, Vector2>(t, new Vector2(x, y))); }
            Label(applied ? "OLD TIMING REPLAY" : "CURRENT TIMING", .25f, .92f, 39);
            Label(applied ? "APPLIED FIX" : "CANDIDATE TIMING", .75f, .92f, 39);
            Label(applied ? "ISOLATED NOZZLE PARTICLES  |  FORMAL RUNTIME METHOD" :
                "ISOLATED NOZZLE PARTICLES  |  FORMAL ACTORS  |  NOT APPLIED", .5f, .06f, 24);
            Label("ANIMATION > PARTICLES > ALIGN", .25f, .83f, 23); Label("ANIMATION > ALIGN > PARTICLES", .75f, .83f, 23);
            Label("", .5f, .16f, 28); var phaseLabel = labels[labels.Count - 1].Key;
            target = new RenderTexture(1120, 630, 24); pixels = new Texture2D(1120, 630, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            string folder = Path.GetFullPath(applied ? "Temp/FlamethrowerFlowApplied" : "Temp/FlamethrowerFlowPreview"); Directory.CreateDirectory(folder);
            var latePresentation = typeof(CharacterWeaponPresenter).GetMethod("UpdateLatePresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int frame = 0; frame < 72; frame++)
            {
                int phase = frame / 24; var move = phase == 0 ? Vector3.zero : phase == 1 ? Vector3.forward : Vector3.right;
                phaseLabel.text = phase == 0 ? "STANDING" : phase == 1 ? "MOVING TOWARD FIRE" : "STRAFING";
                for (int sub = 0; sub < 3; sub++) for (int variant = 0; variant < 2; variant++)
                {
                    var p = presenters[variant]; var ps = flames[variant]; var animator = p.BodyAnimator; const float dt = 1f / 60;
                    actors[variant].transform.position += move * speed * dt;
                    p.SetMovementDirection(move); animator.SetFloat("MoveSpeed", phase == 0 ? 0 : 1); p.TickUpperBodyGunFire(dt);
                    if ((frame * 3 + sub) % 6 == 0) { p.TryGetProjectileOrigin(out _); p.PlayUpperBodyGunFire(); p.PlayWeaponMuzzleEffect(); }
                    animator.Update(dt);
                    if (variant == 1 && applied) latePresentation.Invoke(p, new object[] { dt });
                    else
                    {
                        if (variant == 1) p.ApplyOffhandGrip();
                        ps.Simulate(dt, false, false, false); ps.Pause();
                        if (variant == 0) p.ApplyOffhandGrip();
                    }
                }
                var focus = (actors[0].transform.position + actors[1].transform.position) * .5f + new Vector3(0, .6f, .55f);
                camera.transform.position = focus + new Vector3(0, 7, -4.8f); camera.transform.LookAt(focus);
                foreach (var pair in labels) { pair.Key.transform.position = camera.ViewportToWorldPoint(new Vector3(pair.Value.x, pair.Value.y, 10)); pair.Key.transform.rotation = camera.transform.rotation; }
                camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1120, 630), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, frame.ToString("D3") + ".png"), pixels.EncodeToPNG());
            }
            return folder + "\n72 frames at 20fps; isolated timing replay, nozzle only; " +
                (applied ? "right side uses applied runtime method" : "candidate not applied");
        }
        finally
        {
            RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            foreach (var material in owned) Object.DestroyImmediate(material);
        }
    }
}
