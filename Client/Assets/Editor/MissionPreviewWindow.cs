using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace FirepowerLoop.Preview
{
    /// <summary>独立编辑器任务玩法预览；不进入正式体系、存档或发布资源链。</summary>
    public sealed class MissionPreviewWindow : EditorWindow
    {
        /// <summary>每个临时敌人的表现和简化战斗数据。</summary>
        private sealed class Enemy
        {
            public int Id, Health=2;
            public GameObject Root;
            public Animator Animator;
            public AnimationClip Walk;
        }
        private MissionPreviewState state=new MissionPreviewState();
        private Scene scene;
        private Camera camera;
        private RenderTexture target;
        private GameObject player;
        private Game.CharacterWeaponPresenter presenter;
        private readonly List<Enemy> enemies=new List<Enemy>();
        private readonly List<Object> owned=new List<Object>();
        private readonly HashSet<KeyCode> keys=new HashSet<KeyCode>();
        private readonly Vector3[] supplies={new Vector3(-8,0,-15),new Vector3(7,0,-12),new Vector3(2,0,-4)};
        private readonly List<GameObject> markers=new List<GameObject>();
        private Vector3 exit=new Vector3(12,0,-3),aim=Vector3.forward;
        private Vector2 mouse;
        private Rect viewport;
        private LineRenderer tracer;
        private Light flash;
        private double lastTime;
        private float fireClock,hurtClock,elapsed,tracerClock;
        private bool firing,paused,ambushCreated;
        private string error;
        /// <summary>原型入口，只在编辑态打开。</summary>
        [MenuItem("Tools/FirepowerLoop/单人任务原型")]
        public static void Open()
        {
            if(EditorApplication.isPlaying){EditorUtility.DisplayDialog("单人任务原型","请先退出Play模式，再打开独立任务预览。","确定");return;}
            var w=GetWindow<MissionPreviewWindow>("单人任务原型");w.minSize=new Vector2(850,650);w.Show();
        }
        private void OnEnable(){lastTime=EditorApplication.timeSinceStartup;EditorApplication.update+=UpdatePreview;EditorApplication.playModeStateChanged+=PlayModeChanged;}
        private void OnDisable(){EditorApplication.update-=UpdatePreview;EditorApplication.playModeStateChanged-=PlayModeChanged;Release();}
        private void OnLostFocus(){keys.Clear();firing=false;paused=true;}
        private void PlayModeChanged(PlayModeStateChange change){if(change==PlayModeStateChange.ExitingEditMode)Close();}
        /// <summary>临时材质统一拥有并在关闭时释放。</summary>
        private Material Material(Color color){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=color;owned.Add(m);return m;}
        private GameObject Actor(string path)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(source==null)throw new InvalidOperationException("预览素材缺失："+path);
            var go=Object.Instantiate(source);foreach(var b in go.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;return go;
        }
        /// <summary>只在独立临时场景组装；任何失败都恢复原活动场景。</summary>
        private void Build()
        {
            Release();state.Reset();elapsed=fireClock=hurtClock=tracerClock=0;ambushCreated=paused=false;keys.Clear();firing=false;error=null;
            var previous=SceneManager.GetActiveScene();scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                Actor("Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab");
                player=Actor("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");player.transform.position=new Vector3(-11,0,-19);
                presenter=player.GetComponent<Game.CharacterWeaponPresenter>();presenter.SetWeaponId("arcade.weapon.basic");presenter.SetManualPoseSampling(true);
                for(int i=0;i<4;i++)
                {
                    var marker=GameObject.CreatePrimitive(i<3?PrimitiveType.Cube:PrimitiveType.Cylinder);marker.name=i<3?"任务物资"+(i+1):"撤离点";
                    marker.transform.position=(i<3?supplies[i]:exit)+Vector3.up*(i<3?.4f:.04f);marker.transform.localScale=i<3?new Vector3(.8f,.8f,.8f):new Vector3(2.4f,.08f,2.4f);
                    marker.GetComponent<Renderer>().sharedMaterial=Material(i<3?new Color(.9f,.58f,.12f):new Color(.2f,.8f,.65f));markers.Add(marker);
                }
                camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=12;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.13f);
                camera.transform.position=new Vector3(0,27,-34);camera.transform.LookAt(new Vector3(0,0,-10));target=new RenderTexture(960,600,24);camera.targetTexture=target;
                var sun=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.color=new Color(.8f,.87f,1);sun.transform.rotation=Quaternion.Euler(50,-30,0);sun.cullingMask=1<<31;
                flash=new GameObject("Muzzle",typeof(Light)).GetComponent<Light>();flash.type=LightType.Point;flash.color=new Color(1,.65f,.25f);flash.range=4;flash.intensity=6;flash.enabled=false;flash.cullingMask=1<<31;
                tracer=new GameObject("Tracer",typeof(LineRenderer)).GetComponent<LineRenderer>();var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.color=new Color(1,.8f,.3f);owned.Add(material);tracer.sharedMaterial=material;tracer.positionCount=2;tracer.startWidth=.04f;tracer.endWidth=.02f;tracer.enabled=false;
                SetLayers();SamplePlayer();camera.Render();
            }
            catch(Exception ex){error=ex.Message;Release();}
            finally{SceneManager.SetActiveScene(previous);}
        }
        private void SetLayers(){foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;}
        /// <summary>尸潮只有任务阶段允许时创建一次。</summary>
        private void SpawnAmbush()
        {
            var previous=SceneManager.GetActiveScene();SceneManager.SetActiveScene(scene);
            try{for(int i=0;i<10;i++)
            {
                var root=Actor("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");root.transform.position=new Vector3(i%2==0?-14:14,0,-18+(i/2)*3.5f);
                var a=root.GetComponentInChildren<Animator>(true);a.Rebind();a.Update(0);a.enabled=false;
                enemies.Add(new Enemy{Id=i,Root=root,Animator=a,Walk=a.runtimeAnimatorController.animationClips.First(c=>c.name.Contains("GroundWalk"))});
            }ambushCreated=true;SetLayers();}finally{SceneManager.SetActiveScene(previous);}
        }
        private void SamplePlayer()
        {
            var definition=presenter.Current.Animations.Idle;definition.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,elapsed%definition.Clip.length);presenter.ApplyOffhandGrip(definition,(elapsed%definition.Clip.length)/definition.Clip.length);
        }
        /// <summary>编辑器预览使用独立时钟，失焦和暂停不推进战斗。</summary>
        private void UpdatePreview()
        {
            double now=EditorApplication.timeSinceStartup;float dt=Mathf.Min(.05f,(float)(now-lastTime));lastTime=now;
            if(camera==null||EditorApplication.isPlaying)return;
            if(focusedWindow==this&&!paused&&state.Active)
            {
                var move=new Vector3((keys.Contains(KeyCode.D)?1:0)-(keys.Contains(KeyCode.A)?1:0),0,(keys.Contains(KeyCode.W)?1:0)-(keys.Contains(KeyCode.S)?1:0));
                var ray=camera.ViewportPointToRay(new Vector3(Mathf.InverseLerp(viewport.xMin,viewport.xMax,mouse.x),1-Mathf.InverseLerp(viewport.yMin,viewport.yMax,mouse.y),0));
                if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out var distance)){var direction=ray.GetPoint(distance)-player.transform.position;direction.y=0;if(direction.sqrMagnitude>.01f)aim=direction.normalized;}
                Step(dt,move,firing,keys.Contains(KeyCode.E));
            }
            camera.Render();Repaint();
        }
        /// <summary>实际预览输入和确定性采样共用此步进；没有写盘副作用。</summary>
        private void Step(float dt,Vector3 move,bool shoot,bool interact)
        {
            if(!state.Active||paused||dt<=0)return;elapsed+=dt;fireClock-=dt;hurtClock-=dt;tracerClock-=dt;
            player.transform.position+=Vector3.ClampMagnitude(move,1)*5*dt;var p=player.transform.position;p.x=Mathf.Clamp(p.x,-15,15);p.z=Mathf.Clamp(p.z,-21,1);player.transform.position=p;player.transform.rotation=Quaternion.LookRotation(aim);SamplePlayer();
            if(interact)for(int i=0;i<3;i++)if(state.Collect(i,Vector3.Distance(p,supplies[i])<1.8f))markers[i].SetActive(false);
            if(state.Phase==MissionPreviewPhase.Ambush&&!ambushCreated)SpawnAmbush();
            if(shoot&&fireClock<=0)
            {
                fireClock=.18f;var hit=enemies.Where(e=>e.Health>0).Select(e=>new{Enemy=e,Along=Vector3.Dot(e.Root.transform.position-p,aim),Side=Vector3.Cross(e.Root.transform.position-p,aim).magnitude}).Where(e=>e.Along>0&&e.Along<18&&e.Side<.7f).OrderBy(e=>e.Along).FirstOrDefault();
                if(hit!=null){hit.Enemy.Health--;hit.Enemy.Root.transform.position+=aim*.25f;if(hit.Enemy.Health==0){state.Defeat(hit.Enemy.Id);hit.Enemy.Root.SetActive(false);}}
                var start=p+Vector3.up*1.2f+aim*.65f;tracer.SetPosition(0,start);tracer.SetPosition(1,hit!=null?hit.Enemy.Root.transform.position+Vector3.up:start+aim*18);flash.transform.position=start;tracerClock=.055f;
            }
            foreach(var enemy in enemies.Where(e=>e.Health>0))
            {
                var direction=(p-enemy.Root.transform.position);direction.y=0;if(direction.sqrMagnitude>.001f)enemy.Root.transform.rotation=Quaternion.LookRotation(direction);
                enemy.Root.transform.position=Vector3.MoveTowards(enemy.Root.transform.position,p,1.15f*dt);enemy.Walk.SampleAnimation(enemy.Animator.gameObject,(elapsed+enemy.Id*.17f)%enemy.Walk.length);
                if(direction.magnitude<.85f&&hurtClock<=0){state.Damage(8);hurtClock=.65f;}
            }
            if(interact)state.Extract(Vector3.Distance(p,exit)<1.8f);tracer.enabled=flash.enabled=state.Active&&tracerClock>0;
        }
        private void OnGUI()
        {
            GUILayout.Label("单人任务原型 · 搜寻农场补给",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("独立可操作预览：不写存档。临时标记与简化战斗数值，尚未接入正式闯关。",MessageType.Info);
            if(camera==null){if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Error);if(GUILayout.Button("打开任务预览"))Build();return;}
            GUILayout.Label("WASD 移动 · 鼠标瞄准/左键射击 · 靠近物资或出口按 E · Esc 暂停");
            string objective=state.Phase==MissionPreviewPhase.Briefing?"任务简报：找回3份补给，击退追兵后到右上方绿圈撤离。":state.Phase==MissionPreviewPhase.Searching?"搜寻黄色物资箱":state.Phase==MissionPreviewPhase.Ambush?"尸潮来袭：击退追兵":state.Phase==MissionPreviewPhase.Extracting?"到右上方绿圈，按E撤离":state.Phase==MissionPreviewPhase.Success?"任务完成！物资已找回，可返回或再次出发。":"任务失败。重试不会损失正式存档。";
            GUILayout.Label(objective,EditorStyles.boldLabel);GUILayout.Label($"生命 {state.Health}/100    物资 {state.Collected}/3    追兵 {state.Remaining}    用时 {elapsed:0.0}秒");
            GUILayout.BeginHorizontal();if(state.Phase==MissionPreviewPhase.Briefing&&GUILayout.Button("出发")){state.Start();paused=false;Focus();}
            if(state.Active&&GUILayout.Button(paused?"继续":"暂停")){paused=!paused;keys.Clear();firing=false;}
            if(GUILayout.Button("重新开始"))Build();if(GUILayout.Button("返回 / 关闭")){Close();GUIUtility.ExitGUI();}GUILayout.EndHorizontal();
            viewport=GUILayoutUtility.GetRect(200,10000,200,10000);GUI.DrawTexture(viewport,target,ScaleMode.StretchToFill,false);
            if(paused&&state.Active)GUI.Label(new Rect(viewport.x+20,viewport.y+20,350,30),"已暂停（点击继续）",EditorStyles.whiteLargeLabel);
            var e=Event.current;mouse=e.mousePosition;
            if(e.type==EventType.KeyDown){if(e.keyCode==KeyCode.Escape){paused=!paused;keys.Clear();firing=false;}else keys.Add(e.keyCode);e.Use();}
            if(e.type==EventType.KeyUp){keys.Remove(e.keyCode);e.Use();}
            if(e.type==EventType.MouseDown&&e.button==0&&viewport.Contains(e.mousePosition)){firing=true;Focus();e.Use();}
            if(e.type==EventType.MouseUp&&e.button==0){firing=false;e.Use();}
        }
        /// <summary>清理所有临时场景和显存；不保存或删除正式资产。</summary>
        private void Release()
        {
            keys.Clear();firing=false;if(camera!=null)camera.targetTexture=null;
            if(scene.IsValid())EditorSceneManager.CloseScene(scene,true);scene=default;
            if(target!=null){target.Release();Object.DestroyImmediate(target);}target=null;camera=null;player=null;presenter=null;
            foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();markers.Clear();enemies.Clear();
        }
    }
}
