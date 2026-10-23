using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>丧尸贴身抓挠v4：按候选距离门禁调用正式前摇函数，记录手指与主角网格接触，不称为正式实战。</summary>
public static class ZombieContactGrabPreview
{
    private sealed class Actor
    {
        public GameObject Root;
        public Animator Animator;
        public CharacterWeaponPresenter Presenter;
        public AnimationClip Idle, Move, Attack;
        public Transform Abdomen, Torso, Head, LeftUpper, LeftLower, LeftHand, RightUpper, RightLower, RightHand;
        public Quaternion AbdomenRest, TorsoRest, HeadRest;
        public readonly List<SkinnedMeshRenderer> Skins = new List<SkinnedMeshRenderer>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本项目编辑态隔离预览");
        string folder = Path.GetFullPath("Temp/ZombieContactGrab-v4-20261023");
        Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var owned = new List<Object>();
        var actors = new List<Actor>();
        var oldTarget = RenderTexture.active;
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        GameObject New(string name)
        { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        Actor Clone(string prefab, bool player)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/" + prefab + ".prefab"));
            SceneManager.MoveGameObjectToScene(go, scene);
            var actor = new Actor { Root = go, Presenter = go.GetComponent<CharacterWeaponPresenter>() };
            var view = go.GetComponent<EnemyView>();
            if (view != null) { view.BeginPresentationUse(); view.SetNavigationEnabled(false);
                view.SetPresentationVariant(prefab == "ArcadeZombieGaunt" ? 1 : prefab == "ArcadeZombieBloated" ? 2 : 0); }
            if (player && actor.Presenter != null)
            {
                if (!actor.Presenter.SetWeaponId("arcade.weapon.basic"))
                    throw new InvalidOperationException(actor.Presenter.LastError);
                actor.Presenter.SetManualPoseSampling(true);
            }
            actor.Animator = player ? actor.Presenter.BodyAnimator : view.Animator;
            actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            actor.Animator.Rebind(); actor.Animator.Update(0);
            if (!player)
            {
                var bones = actor.Animator.GetComponentsInChildren<Transform>(true);
                actor.Abdomen = bones.First(t => t.name == "Abdomen");
                actor.Torso = bones.First(t => t.name == "Torso");
                actor.Head = bones.First(t => t.name == "Head");
                actor.LeftUpper = bones.First(t => t.name == "UpperArm.L");
                actor.LeftLower = bones.First(t => t.name == "LowerArm.L");
                actor.LeftHand = bones.First(t => t.name == "Middle1.L");
                actor.RightUpper = bones.First(t => t.name == "UpperArm.R");
                actor.RightLower = bones.First(t => t.name == "LowerArm.R");
                actor.RightHand = bones.First(t => t.name == "Middle1.R");
                actor.AbdomenRest = actor.Abdomen.localRotation;
                actor.TorsoRest = actor.Torso.localRotation;
                actor.HeadRest = actor.Head.localRotation;
            }
            var clips = actor.Animator.runtimeAnimatorController.animationClips;
            actor.Idle = actor.Presenter != null ? actor.Presenter.Current.Animations.Idle.Clip : clips.First(c => c.name.EndsWith("|Idle"));
            actor.Move = actor.Presenter != null ? actor.Presenter.Current.Animations.MoveForward.Clip :
                clips.First(c => c.name.EndsWith(prefab == "ArcadeZombieGaunt" ? "|Run_Arms" : "|Walk"));
            actor.Attack = actor.Presenter != null ? actor.Presenter.Current.Animations.Attack.Clip : clips.First(c => c.name.EndsWith("|Idle_Attack"));
            foreach (var canvas in go.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
            foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                var proxy = new GameObject("TemporarySampledSkin", typeof(MeshFilter), typeof(MeshRenderer));
                proxy.transform.SetParent(skin.transform, false);
                var mesh = Object.Instantiate(skin.sharedMesh); owned.Add(mesh);
                proxy.GetComponent<MeshFilter>().sharedMesh = mesh;
                proxy.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                actor.Skins.Add(skin); actor.Meshes.Add(mesh); skin.enabled = false;
            }
            actors.Add(actor); return actor;
        }
        void Pose(Actor actor, AnimationClip clip, float seconds, float grabTime = -1, float cancel = 0)
        {
            if (actor.Head != null)
            {
                actor.Abdomen.localRotation = actor.AbdomenRest;
                actor.Torso.localRotation = actor.TorsoRest;
                actor.Head.localRotation = actor.HeadRest;
            }
            clip.SampleAnimation(actor.Animator.gameObject, Mathf.Clamp(seconds, 0, clip.length));
            if (actor.Presenter != null)
                actor.Presenter.ApplyOffhandGrip(clip == actor.Move ? actor.Presenter.Current.Animations.MoveForward :
                    clip == actor.Attack ? actor.Presenter.Current.Animations.Attack : actor.Presenter.Current.Animations.Idle);
            if (grabTime >= 0) ApplyWideGrab(actor, grabTime, cancel);
            for (int i = 0; i < actor.Skins.Count; i++) UpdateSkin(actor.Skins[i], actor.Meshes[i]);
        }
        try
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); owned.Add(mat); mat.color = new Color(.28f, .33f, .29f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0, -.08f, 0); floor.transform.localScale = new Vector3(30, .12f, 30);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var sun = New("TemporaryKeyLight").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.intensity = 1.6f; sun.transform.rotation = Quaternion.Euler(50, -35, 0);
            camera = New("TemporaryPreviewCamera").AddComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 2.15f;
            camera.transform.position = new Vector3(1.8f, 3.1f, -6); camera.transform.LookAt(new Vector3(.2f, .85f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .11f, .13f);
            camera.cullingMask = ~(1 << 5);
            target = new RenderTexture(900, 580, 24); camera.targetTexture = target;
            pixels = new Texture2D(900, 580, TextureFormat.RGB24, false);
            var player = Clone("GrayboxPlayer", true);
            var enemies = new[] { Clone("ArcadeZombieShambler", false), Clone("ArcadeZombieGaunt", false), Clone("ArcadeZombieBloated", false) };
            var report = new List<string>();
            foreach (var enemy in enemies)
            {
                // 先验证正式的Attack触发确实能切入攻击片段，随后候选仅作手动姿态采样。
                enemy.Root.SetActive(true); enemy.Animator.enabled = true; enemy.Animator.Rebind(); enemy.Animator.Update(0);
                enemy.Root.GetComponent<EnemyView>().PlayAttackAnimation(); enemy.Animator.Update(.1f);
                bool attacking = enemy.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack") ||
                    enemy.Animator.GetNextAnimatorStateInfo(0).IsName("Attack");
                report.Add(enemy.Root.name + ": attackTrigger=" + attacking + ", attack=" + enemy.Attack.name +
                    ", length=" + enemy.Attack.length + ", scale=" + enemy.Root.transform.localScale + ", attachedWeapons=" +
                    enemy.Root.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("HeldWeapon_")) + ", jaw=" +
                    enemy.Animator.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Jaw"));
                if (!attacking) throw new InvalidOperationException("正式攻击触发未切入Attack：" + enemy.Root.name);
                enemy.Root.SetActive(false);
            }
            foreach (var actor in actors) foreach (var animator in actor.Root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var runtimeType = typeof(ArcadeCombatSystem).Assembly.GetType("Game.ArcadeEnemyRuntime", true);
            var complete = typeof(ArcadeCombatSystem).GetMethod("TryCompleteMeleeAttack", BindingFlags.Static | BindingFlags.NonPublic);
            var tables = new global::Tables(n => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes"))));
            // 仅候选攻击距离，不写正式配置；正式旧规则仍为0.55+0.95=1.5米。
            const float reach = .9f, dt = .05f;
            var csv = new List<string> { "kind,frame,time,distance,inRange,windup,remaining,hit,cancel,handToBody" };
            for (int kind = 0; kind < 3; kind++)
            {
                var enemy = enemies[kind]; enemy.Root.SetActive(true);
                string id = new[] { "enemy.arcade.zombie.shambler", "enemy.arcade.zombie.gaunt", "enemy.arcade.zombie.bloated" }[kind];
                var config = tables.TbEnemy.DataList.Single(row => row.Id == id);
                string frames = Path.Combine(folder, new[] { "shambler-claw", "gaunt-claw", "bloated-claw" }[kind]); Directory.CreateDirectory(frames);
                object state = null;
                void Set(string name, object value) => runtimeType.GetField(name).SetValue(state, value);
                bool Active() => (bool)runtimeType.GetField("MeleeWindupActive").GetValue(state);
                float Remaining() => (float)runtimeType.GetField("MeleeWindupRemaining").GetValue(state);
                void Reset()
                {
                    state = Activator.CreateInstance(runtimeType, true);
                    Set("MeleeWindupSeconds", config.TelegraphSeconds); Set("AttackCooldown", 0f);
                }
                Reset(); float enemyX = -2.7f, playerX = .6f, startAt = -1, hitAt = -1, cancelAt = -1;
                bool second = false, dodged = false, hitAlready = false;
                int hits = 0, cancels = 0, illegalStarts = 0; float hitGap = -1;
                var leftTip = enemy.Animator.GetComponentsInChildren<Transform>(true).First(t => t.name == "IndexFinger_04");
                var rightTip = enemy.Animator.GetComponentsInChildren<Transform>(true).First(t => t.name == "IndexFinger_04.001");
                for (int frame = 0; frame < 100; frame++)
                {
                    float t = frame * dt;
                    if (!second && t >= 2.5f)
                    { second = true; Reset(); enemyX = -2.7f; playerX = .6f; startAt = hitAt = cancelAt = -1; hitAlready = false; }
                    if (second && startAt >= 0 && t-startAt >= .2f-.0001f) dodged = true;
                    if (dodged) playerX = Mathf.MoveTowards(playerX, 2.8f, 6.5f*dt);
                    bool wasActive = Active(); float distance = Mathf.Abs(playerX-enemyX);
                    if (wasActive && distance > reach+.0001f)
                    {
                        complete.Invoke(null, new object[] { state, false, false, 0f });
                        cancelAt = t; cancels++;
                    }
                    if (!Active() && !hitAlready)
                        enemyX = Mathf.MoveTowards(enemyX, playerX-reach, Mathf.Max(.1f,config.MoveSpeed)*dt);
                    distance = Mathf.Abs(playerX-enemyX); bool inRange = distance <= reach+.0001f;
                    bool before = Active();
                    bool hit = !hitAlready && (bool)complete.Invoke(null, new object[] { state, inRange, false, dt });
                    if (!before && Active()) { startAt = t; if (!inRange) illegalStarts++; }
                    if (hit) { hitAt = t; hitAlready = true; hits++; }
                    enemy.Root.transform.SetPositionAndRotation(new Vector3(enemyX, 0, 0), Quaternion.Euler(0, 90, 0));
                    player.Root.transform.SetPositionAndRotation(new Vector3(playerX, 0, 0), Quaternion.Euler(0, -90, 0));
                    float grab = -1, cancel = 0;
                    if (cancelAt >= 0 && t-cancelAt < .2f)
                    { grab = .2f; cancel = Mathf.SmoothStep(0, 1, (t-cancelAt)/.2f); }
                    else if (Active()) grab = t-startAt;
                    else if (hitAlready && t-hitAt < .55f) grab = config.TelegraphSeconds + t-hitAt;
                    Pose(enemy, grab>=0 ? enemy.Idle : enemy.Move, grab>=0 ? .1f : t, grab, cancel);
                    Pose(player, dodged ? player.Move : player.Idle, t);
                    float gap = -1;
                    if (hit)
                    {
                        gap = Mathf.Min(DistanceToSkin(leftTip.position, player), DistanceToSkin(rightTip.position, player));
                        hitGap = gap;
                        report.Add(id + ": hitAt=" + t.ToString("F2") + ", elapsed=" + (t-startAt).ToString("F2") +
                            ", centerDistance=" + distance.ToString("F3") + ", handToBody=" + gap.ToString("F4"));
                    }
                    csv.Add(string.Join(",", kind, frame, t.ToString("F2"), distance.ToString("F3"), inRange, Active(), Remaining().ToString("F3"), hit, cancelAt==t, gap.ToString("F4")));
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 900, 580), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Path.Combine(frames, "frame-" + frame.ToString("000") + ".png"), pixels.EncodeToPNG());
                }
                report.Add(id + ": hits=" + hits + ", cancels=" + cancels + ", outOfRangeStarts=" + illegalStarts);
                if (hits!=1 || cancels!=1 || illegalStarts!=0 || hitGap<0 || hitGap>.15f)
                    throw new InvalidOperationException("接触候选检查未通过：" + string.Join("; ",report));
                enemy.Root.SetActive(false);
            }
            File.WriteAllLines(Path.Combine(folder, "timeline.csv"), csv);
            File.WriteAllLines(Path.Combine(folder, "inspection.txt"), report);
            return folder + "\n" + string.Join("\n", report);
        }
        finally
        {
            RenderTexture.active = oldTarget;
            if (camera != null) camera.targetTexture = null;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
        }
    }

    /// <summary>临时大幅抓挠：0.3秒后拉，随后0.1秒前抓，0.4秒处达到接触姿态，再向内下方撕扯。</summary>
    private static void ApplyWideGrab(Actor actor, float time, float cancel)
    {
        var idleL = new Vector3(-.32f, 1.0f, .13f); var idleR = new Vector3(.32f, 1.0f, .13f);
        var rearL = new Vector3(-.48f, 1.48f, -.24f); var rearR = new Vector3(.48f, 1.48f, -.24f);
        var reachL = new Vector3(-.20f, 1.25f, .58f); var reachR = new Vector3(.20f, 1.30f, .58f);
        var tearL = new Vector3(-.08f, 1.02f, .45f); var tearR = new Vector3(.08f, 1.02f, .45f);
        Vector3 left, right; float lean, twist;
        if (time < .3f)
        {
            float p = Mathf.SmoothStep(0, 1, time / .3f);
            left = Vector3.Lerp(idleL, rearL, p); right = Vector3.Lerp(idleR, rearR, p); lean = -10 * p; twist = -12 * p;
        }
        else if (time < .4f)
        {
            float p = Mathf.SmoothStep(0, 1, (time - .3f) / .1f);
            left = Vector3.Lerp(rearL, reachL, p); right = Vector3.Lerp(rearR, reachR, p); lean = Mathf.Lerp(-10, 28, p); twist = Mathf.Lerp(-12, 10, p);
        }
        else if (time < .58f)
        {
            float p = Mathf.SmoothStep(0, 1, (time - .4f) / .18f);
            left = Vector3.Lerp(reachL, tearL, p); right = Vector3.Lerp(reachR, tearR, p); lean = Mathf.Lerp(28, 16, p); twist = Mathf.Lerp(10, -8, p);
        }
        else
        {
            float p = Mathf.SmoothStep(0, 1, (time - .58f) / .32f);
            left = Vector3.Lerp(tearL, idleL, p); right = Vector3.Lerp(tearR, idleR, p); lean = 16 * (1-p); twist = -8 * (1-p);
        }
        left = Vector3.Lerp(left, idleL, cancel); right = Vector3.Lerp(right, idleR, cancel);
        lean *= 1-cancel; twist *= 1-cancel;
        var axis = actor.Root.transform.right;
        actor.Abdomen.rotation = Quaternion.AngleAxis(lean * .45f, axis) * actor.Abdomen.rotation;
        actor.Torso.rotation = Quaternion.AngleAxis(twist, Vector3.up) * Quaternion.AngleAxis(lean * .55f, axis) * actor.Torso.rotation;
        actor.Head.rotation = Quaternion.AngleAxis(-lean * .25f, axis) * actor.Head.rotation;
        AimArm(actor, actor.LeftUpper, actor.LeftLower, actor.LeftHand, left, -1);
        AimArm(actor, actor.RightUpper, actor.RightLower, actor.RightHand, right, 1);
    }

    /// <summary>只转动两段手臂骨骼，让手掌走清楚的抓挠轨迹；骨长与角色尺寸不变。</summary>
    private static void AimArm(Actor actor, Transform upper, Transform lower, Transform hand, Vector3 localTarget, float side)
    {
        Vector3 a = upper.position, b = lower.position, c = hand.position;
        float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c);
        Vector3 wanted = actor.Root.transform.TransformPoint(localTarget), delta = wanted-a;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(ab-bc)+.001f, ab+bc-.001f);
        var direction = delta.normalized; wanted = a + direction * distance;
        Vector3 pole = actor.Root.transform.right * side - actor.Root.transform.forward * .25f;
        pole = (pole - direction * Vector3.Dot(pole, direction)).normalized;
        float cosine = Mathf.Clamp((ab*ab+distance*distance-bc*bc)/(2*ab*distance), -1, 1);
        Vector3 elbow = a + direction * (ab*cosine) + pole * (ab*Mathf.Sqrt(1-cosine*cosine));
        upper.rotation = Quaternion.FromToRotation(b-a, elbow-a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position-lower.position, wanted-lower.position) * lower.rotation;
    }

    /// <summary>测量手指到正式主角采样网格的最近距离，避免只按摆位宣称接触。</summary>
    private static float DistanceToSkin(Vector3 point, Actor player)
    {
        float minimum = float.PositiveInfinity;
        for (int m = 0; m < player.Meshes.Count; m++)
        {
            var mesh = player.Meshes[m]; var vertices = mesh.vertices; var triangles = mesh.triangles;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = player.Skins[m].transform.TransformPoint(vertices[i]);
            for (int i = 0; i < triangles.Length; i+=3)
                minimum = Mathf.Min(minimum, Vector3.Distance(point, ClosestTriangle(point, vertices[triangles[i]], vertices[triangles[i+1]], vertices[triangles[i+2]])));
        }
        return minimum;
    }
    private static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0&&d2<=0)return a;
        var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
        float inverse=1/(va+vb+vc);return a+ab*(vb*inverse)+ac*(vc*inverse);
    }

    /// <summary>按采样后的实际骨矩阵蒙皮，避开编辑态BakeMesh缓存造成身体与持刀骨骼错位。</summary>
    private static void UpdateSkin(SkinnedMeshRenderer skin, Mesh output)
    {
        var source = skin.sharedMesh; var vertices = source.vertices; var normals = source.normals;
        var weights = source.boneWeights; var bind = source.bindposes; var bones = skin.bones;
        var matrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) matrices[i] = skin.transform.worldToLocalMatrix * bones[i].localToWorldMatrix * bind[i];
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i]; var v = vertices[i]; var n = normals[i];
            vertices[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            normals[i] = (matrices[w.boneIndex0].MultiplyVector(n) * w.weight0 + matrices[w.boneIndex1].MultiplyVector(n) * w.weight1 +
                matrices[w.boneIndex2].MultiplyVector(n) * w.weight2 + matrices[w.boneIndex3].MultiplyVector(n) * w.weight3).normalized;
        }
        output.vertices = vertices; output.normals = normals; output.RecalculateBounds();
    }
}
