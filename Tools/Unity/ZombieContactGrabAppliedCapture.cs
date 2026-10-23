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

/// <summary>正式丧尸配置出生并由真实TickEnemies和EnemyView推进的隔离同框录像；不冒充完整闯关或真人联机。</summary>
public static class ZombieContactGrabAppliedCapture
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
        string folder = Path.GetFullPath("Temp/ZombieContactGrabApplied-20261024");
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
            var view=actor.Root.GetComponent<EnemyView>();
            if(view!=null)view.LateTickCombatFeedback();
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
            foreach (var actor in actors) foreach (var animator in actor.Root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var fixtureType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Game.Tests.ArcadeEnemyDropTests")).First(t=>t!=null);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            const float dt=.05f;
            var csv=new List<string>{"kind,frame,time,distance,inRange,windup,remaining,recovery,health,hit,cancel,handToBody"};
            for(int kind=0;kind<3;kind++)
            {
                var enemy=enemies[kind];enemy.Root.SetActive(true);var view=enemy.Root.GetComponent<EnemyView>();
                var fixture=Activator.CreateInstance(fixtureType);fixtureType.GetMethod("Setup").Invoke(fixture,null);
                var runtime=fixtureType.GetField("runtime",flags).GetValue(fixture);
                var system=fixtureType.GetField("system",flags).GetValue(fixture);
                var rt=runtime.GetType();
                string id=new[]{"enemy.arcade.zombie.shambler","enemy.arcade.zombie.gaunt","enemy.arcade.zombie.bloated"}[kind];
                object state=fixtureType.GetMethod("SpawnLowTierEnemy",flags).Invoke(fixture,new object[]{id});
                var et=state.GetType();
                object Get(string n)=>et.GetField(n).GetValue(state);
                void Set(string n,object v)=>et.GetField(n).SetValue(state,v);
                void Player(string n,object v)=>rt.GetField(n).SetValue(runtime,v);
                int Health()=>(int)rt.GetField("PlayerHealth").GetValue(runtime);
                bool Active()=>(bool)Get("MeleeWindupActive");
                float Rem()=>(float)Get("MeleeWindupRemaining");
                float Recovery()=>(float)Get("MeleeRecoveryRemaining");
                void Reset()
                {
                    Set("Position",new Vector2(-2.7f,0));Set("AttackCooldown",0f);Set("Damage",10);
                    Set("ContentSpecialKind",0);Set("MeleeWindupActive",false);Set("MeleeWindupRemaining",0f);Set("MeleeRecoveryRemaining",0f);
                    Player("PlayerHealth",100);Player("PlayerInvulnerability",0f);view.ResetPhysicalFeedback();view.BeginPresentationUse();view.SetNavigationEnabled(false);
                    foreach(var skin in enemy.Skins)skin.enabled=false;
                }
                Reset();float reach=(float)Get("AttackRange"),playerX=.6f,startAt=-1,hitGap=-1;
                bool second=false,dodged=false,hitAlready=false;int hits=0,cancels=0,illegalStarts=0;
                if(Mathf.Abs(reach-.9f)>.0001f)throw new InvalidOperationException("正式配置距离不符");
                var leftTip=enemy.Animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="IndexFinger_04");
                var rightTip=enemy.Animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="IndexFinger_04.001");
                string frames=Path.Combine(folder,new[]{"shambler-claw","gaunt-claw","bloated-claw"}[kind]);Directory.CreateDirectory(frames);
                for(int frame=0;frame<100;frame++)
                {
                    float t=frame*dt;
                    if(!second&&t>=2.5f){second=true;Reset();playerX=.6f;startAt=-1;hitAlready=false;}
                    if(second&&startAt>=0&&t-startAt>=.2f-.0001f)dodged=true;
                    if(dodged)playerX+=6.5f*dt;
                    Player("PlayerPosition",new Vector2(playerX,0));
                    bool wasActive=Active();int before=Health();
                    // 每段只观察一次交互，完成收招后保持画面，冷却续攻由独立回归覆盖。
                    if(!hitAlready||Recovery()>0)typeof(ArcadeCombatSystem).GetMethod("TickEnemies",flags).Invoke(system,new object[]{runtime,dt});
                    bool hit=Health()<before,cancel=wasActive&&!Active()&&!hit;
                    var pos=(Vector2)Get("Position");float distance=Mathf.Abs(playerX-pos.x);bool inRange=distance<=reach+.0001f;
                    if(!wasActive&&Active()){startAt=t;if(!inRange)illegalStarts++;}
                    if(hit){hitAlready=true;hits++;if(Mathf.Abs(t-startAt-.4f)>.001f)throw new InvalidOperationException("命中未等待完整0.4秒");}
                    if(cancel)cancels++;
                    view.TickCombatFeedback(dt);
                    enemy.Root.transform.SetPositionAndRotation(new Vector3(pos.x,0,pos.y),Quaternion.Euler(0,90,0));
                    player.Root.transform.SetPositionAndRotation(new Vector3(playerX,0,0),Quaternion.Euler(0,-90,0));
                    view.SetMeleeAttack((float)Get("MeleeWindupSeconds"),Rem(),Recovery(),Vector3.right);
                    Pose(enemy,Active()||Recovery()>0?enemy.Idle:enemy.Move,Active()||Recovery()>0?.1f:t);
                    Pose(player,dodged?player.Move:player.Idle,dodged?t:.1f);
                    float gap=-1;
                    if(hit)
                    {
                        gap=Mathf.Min(DistanceToSkin(leftTip.position,player),DistanceToSkin(rightTip.position,player));hitGap=gap;
                        report.Add(id+": realTickHit="+t.ToString("F2")+", fullWindup="+(t-startAt).ToString("F2")+", distance="+distance.ToString("F3")+", handToBody="+gap.ToString("F4")+", health="+before+"->"+Health());
                    }
                    csv.Add(string.Join(",",kind,frame,t.ToString("F2"),distance.ToString("F3"),inRange,Active(),Rem().ToString("F3"),Recovery().ToString("F3"),Health(),hit,cancel,gap.ToString("F4")));
                    camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,900,580),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(frames,"frame-"+frame.ToString("000")+".png"),pixels.EncodeToPNG());
                }
                report.Add(id+": hits="+hits+", cancels="+cancels+", outsideStarts="+illegalStarts+", scale="+enemy.Root.transform.localScale);
                if(hits!=1||cancels!=1||illegalStarts!=0||hitGap<0||hitGap>.15f)throw new InvalidOperationException("正式接入采样未通过: "+string.Join(";",report));
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
