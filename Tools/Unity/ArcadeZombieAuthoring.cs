using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>从本工程主角人体派生便服丧尸；独立网格、材质和抓击动画，不改变主角与后期敌人。</summary>
public static class ArcadeZombieAuthoring
{
    private const string Art="Assets/Game/Art/Enemies/Zombies/Civilian/";
    private const string Prefabs="Assets/Game/Prefabs/Characters/";
    private const string Human="Assets/Game/Art/Player/OutfitPrototype/PlayerModularOutfit.fbx";
    private static readonly string[] BoneNames={"Body","Torso","Head","UpperArm.L","LowerArm.L","UpperArm.R","LowerArm.R","UpperLeg.L","LowerLeg.L","UpperLeg.R","LowerLeg.R"};
    private static readonly string[] Ends={"Hips","Neck",null,"LowerArm.L","Middle1.L","LowerArm.R","Middle1.R","LowerLeg.L","Foot.L","LowerLeg.R","Foot.R"};
    private static readonly int[] Parents={-1,0,1,1,3,1,5,0,7,0,9};
    private static readonly string[] Parts={"Body_Head","Base_Hands","Base_Feet","Base_Torso","Pants_BlueDenim"};

    public static string Run()
    {
        if(Application.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作丧尸");
        Directory.CreateDirectory(Art);AssetDatabase.Refresh();
        {
            float height=PlayerHeight();
            var result=new List<string>();
            for(int i=0;i<3;i++)result.Add(Create(new[]{"Shambler","Gaunt","Bloated"}[i],i,height));
            AssetDatabase.SaveAssets();return string.Join("\n",result);
        }
    }

    /// <summary>使用主角正式默认站姿的实际蒙皮高度，不假定模型源文件的单位和身高。</summary>
    private static float PlayerHeight()
    {
        var prior=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"GrayboxPlayer.prefab"));
        try{foreach(var a in player.GetComponentsInChildren<Animator>()){a.Rebind();a.Update(0);}return BoundsOf(player).size.y;}
        finally{Object.DestroyImmediate(player);SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);}
    }
    private static Bounds BoundsOf(GameObject root)
    {
        var points=new List<Vector3>();
        foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled&&r.bones.Length>0))
        {var mesh=new Mesh();r.BakeMesh(mesh,true);points.AddRange(mesh.vertices.Select(r.transform.TransformPoint));Object.DestroyImmediate(mesh);}
        if(points.Count==0)throw new InvalidOperationException("缺少有效人体网格");
        var b=new Bounds(points[0],Vector3.zero);foreach(var p in points)b.Encapsulate(p);return b;
    }

    private static string Create(string kind,int variant,float height)
    {
        var root=PrefabUtility.LoadPrefabContents(Prefabs+"ArcadeEnemyBasic.prefab");
        try
        {
            root.name="ArcadeZombie"+kind;
            Object.DestroyImmediate(root.GetComponent<CharacterWeaponPresenter>());
            var flame=root.GetComponentInChildren<EnemyFlameFeedback>(true);flame.transform.SetParent(root.transform,true);
            var visual=root.transform.Find("ArcadeVisual");
            foreach(var child in visual.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            visual.localPosition=Vector3.zero;visual.localRotation=Quaternion.identity;visual.localScale=Vector3.one;
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Human),root.scene);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            model.name="CivilianInfectedBody";model.transform.SetParent(visual,false);
            var materials=Materials(variant);
            foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!Parts.Contains(r.name)){Object.DestroyImmediate(r.gameObject);continue;}
                Customize(r,kind,variant,materials);r.gameObject.SetActive(true);r.enabled=true;
            }
            RenameBones(model);
            AddFaceAndCollar(model,kind,materials);
            model.transform.localScale=new Vector3(variant==1?.95f:variant==2?1.04f:1,1,variant==1?.96f:variant==2?1.04f:1);
            var rest=BoundsOf(model);visual.localScale=Vector3.one*height/rest.size.y;
            rest=BoundsOf(model);visual.position-=Vector3.up*(rest.min.y-root.transform.position.y);
            var animator=model.GetComponent<Animator>();if(animator==null)animator=model.AddComponent<Animator>();
            animator.avatar=null;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController=Controller(model,kind,variant);
            flame.transform.SetParent(model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Abdomen"),true);
            // 新人体骨架的躯干位置不同，清掉旧敌人骨架留下的局部偏移。
            flame.transform.localPosition=Vector3.zero;
            ConfigurePhysics(root,animator,model.transform,height);
            var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var view=new SerializedObject(root.GetComponent<EnemyView>());
            view.FindProperty("animator").objectReferenceValue=animator;view.FindProperty("defaultWeaponId").stringValue="weapon.zombie-claw";
            view.FindProperty("projectileFirePoint").objectReferenceValue=null;view.FindProperty("presentationVariants").arraySize=0;
            var living=view.FindProperty("livingRenderers");living.arraySize=renderers.Length;
            for(int i=0;i<renderers.Length;i++)living.GetArrayElementAtIndex(i).objectReferenceValue=renderers[i];
            view.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,Prefabs+root.name+".prefab");
            return root.name+" | reference="+height.ToString("F3")+"m | human meshes="+renderers.Length;
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    /// <summary>统一为既有Generic物理和燃烧节点名；Transform引用与蒙皮绑定矩阵不变。</summary>
    private static void RenameBones(GameObject model)
    {
        var names=new Dictionary<string,string>{{"Hips","Body"},{"Spine_01","Hips"},{"Spine_02","Abdomen"},{"Spine_03","Torso"}};
        foreach(var side in new[]{"L","R"})
        {names["Shoulder_"+side]="UpperArm."+side;names["Elbow_"+side]="LowerArm."+side;names["Hand_"+side]="Middle1."+side;names["UpperLeg_"+side]="UpperLeg."+side;names["LowerLeg_"+side]="LowerLeg."+side;names["Ankle_"+side]="Foot."+side;}
        foreach(var t in model.GetComponentsInChildren<Transform>(true))if(names.TryGetValue(t.name,out var name))t.name=name;
    }

    /// <summary>独立材质区分别表现灰白皮肤、旧衬衫、裤子和污渍，不以整身染绿代替感染外观。</summary>
    private static Material[] Materials(int variant)
    {
        var shirt=new[]{new Color(.28f,.34f,.40f),new Color(.52f,.46f,.34f),new Color(.36f,.17f,.14f)}[variant];
        var colors=new[]{new Color(.49f,.52f,.48f),shirt,new Color(.18f,.21f,.23f),new Color(.12f,.11f,.095f),new Color(.74f,.72f,.59f),new Color(.12f,.115f,.105f),new Color(.21f,.15f,.13f),new Color(.16f,.17f,.14f)};
        var names=new[]{"InfectedSkin","Shirt"+variant,"Trousers","Hair","EyeIvory","Shoes","Grime","EyeSocket"};
        return names.Select((n,i)=>{string p=Art+n+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,p);}m.color=colors[i];m.SetFloat("_Smoothness",.04f);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;}).ToArray();
    }

    private static void Customize(SkinnedMeshRenderer r,string kind,int variant,Material[] materials)
    {
        var mesh=Object.Instantiate(r.sharedMesh);mesh.name=kind+"_"+r.name;
        var vertices=mesh.vertices;
        for(int i=0;i<vertices.Length;i++)
        {
            var v=vertices[i];
            if(r.name=="Base_Torso"&&Mathf.Abs(v.x)>.44f){v.y=1.34f+(v.y-1.34f)*.8f;v.z=-.035f+(v.z+.035f)*.8f;}
            if(r.name=="Base_Torso"&&v.y<1.03f&&Mathf.Abs(v.x)<.24f)v.y+=Mathf.Sin(v.x*81+v.z*53)*.024f;
            if(r.name=="Body_Head"&&v.y>1.55f&&v.y<1.69f)v.x*=variant==1?.92f:.97f;
            vertices[i]=v;
        }
        mesh.vertices=vertices;
        var groups=Enumerable.Range(0,materials.Length).Select(_=>new List<int>()).ToArray();var triangles=mesh.triangles;
        for(int i=0;i<triangles.Length;i+=3)
        {
            int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var center=(vertices[a]+vertices[b]+vertices[c])/3;
            int m=0;
            if(r.name=="Base_Feet")m=5;
            else if(r.name=="Base_Torso")
            {m=Mathf.Abs(center.x)>.46f+Mathf.Sin(center.y*120)*.02f?0:1;if(m==1&&i%39==0)m=6;}
            else if(r.name=="Pants_BlueDenim")
            {m=2;if(center.z>.035f&&Mathf.Abs(center.y-(center.x>0?.49f:.42f))<.045f)m=0;else if(i%51==0)m=6;}
            else if(r.name=="Body_Head")
            {m=0;}
            groups[m].AddRange(new[]{a,b,c});
        }
        mesh.subMeshCount=groups.Length;for(int i=0;i<groups.Length;i++)mesh.SetTriangles(groups[i],i);
        mesh.RecalculateNormals();mesh.RecalculateBounds();r.sharedMesh=Save(mesh,mesh.name+".asset");r.sharedMaterials=materials;
        r.localBounds=r.sharedMesh.bounds;r.updateWhenOffscreen=true;
    }

    /// <summary>小面积凹眼、灰白眼仁、微张嘴和翻领都绑定真实骨骼，受击与死亡时随身体移动。</summary>
    private static void AddFaceAndCollar(GameObject model,string kind,Material[] materials)
    {
        var all=model.GetComponentsInChildren<Transform>(true);var head=all.Single(t=>t.name=="Head");var torso=all.Single(t=>t.name=="Torso");
        var v=new List<Vector3>();var faces=new List<int[]>();var matIds=new List<int>();
        Action<Vector3,Vector2,int> plate=(center,size,material)=>{int n=v.Count;v.AddRange(new[]{center+new Vector3(-size.x,-size.y,0),center+new Vector3(size.x,-size.y,0),center+new Vector3(size.x,size.y,0),center+new Vector3(-size.x,size.y,0)});faces.Add(new[]{n,n+1,n+2,n,n+2,n+3});matIds.Add(material);};
        foreach(float x in new[]{-.047f,.047f}){plate(new Vector3(x,1.662f,.146f),new Vector2(.028f,.015f),7);plate(new Vector3(x,1.660f,.148f),new Vector2(.013f,.0055f),4);plate(new Vector3(x,1.660f,.150f),new Vector2(.0035f,.0055f),3);}
        plate(new Vector3(0,1.548f,.149f),new Vector2(.030f,.014f),7);plate(new Vector3(0,1.557f,.151f),new Vector2(.021f,.0035f),4);
        RigidMesh(model,head,kind+"_InfectedFace",v,faces,matIds,materials);
        // 独立短发轮廓，不能把兜帽染黑当头发；参差发际线避开眼睛。
        v.Clear();faces.Clear();matIds.Clear();
        for(int ring=0;ring<3;ring++)for(int i=0;i<12;i++)
        {
            float angle=i*Mathf.PI/6;float z=Mathf.Sin(angle);
            float y=ring==0?(z>0?1.733f:1.689f)+Mathf.Sin(i*2.7f)*.009f:ring==1?1.792f:1.825f+Mathf.Sin(i*2.1f)*.012f;
            float radius=ring==2?.042f:.112f;
            v.Add(new Vector3(Mathf.Cos(angle)*radius,y,z*radius-.012f));
        }
        for(int ring=0;ring<2;ring++)for(int i=0;i<12;i++)
        {int a=ring*12+i,b=ring*12+(i+1)%12,c=(ring+1)*12+i,d=(ring+1)*12+(i+1)%12;faces.Add(new[]{a,c,b,b,c,d,a,b,c,b,d,c});matIds.Add(3);}
        faces.Add(Enumerable.Range(24,12).SelectMany((x,i)=>i>0&&i<11?new[]{24,x,x+1}:new int[0]).ToArray());matIds.Add(3);
        RigidMesh(model,head,kind+"_ShortHair",v,faces,matIds,materials);

        v.Clear();faces.Clear();matIds.Clear();
        foreach(int side in new[]{-1,1})
        {int n=v.Count;v.AddRange(new[]{new Vector3(side*.025f,1.425f,.083f),new Vector3(side*.105f,1.381f,.10f),new Vector3(side*.057f,1.306f,.131f)});faces.Add(new[]{n,n+1,n+2,n+2,n+1,n});matIds.Add(1);}
        RigidMesh(model,torso,kind+"_TornCollar",v,faces,matIds,materials);
    }
    private static void RigidMesh(GameObject model,Transform bone,string name,List<Vector3> vertices,List<int[]> faces,List<int> ids,Material[] materials)
    {
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.boneWeights=vertices.Select(_=>new BoneWeight{boneIndex0=0,weight0=1}).ToArray();
        mesh.bindposes=new[]{bone.worldToLocalMatrix*model.transform.localToWorldMatrix};mesh.subMeshCount=materials.Length;
        for(int m=0;m<materials.Length;m++)mesh.SetTriangles(faces.Where((f,i)=>ids[i]==m).SelectMany(f=>f).ToArray(),m);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(name,typeof(SkinnedMeshRenderer));go.transform.SetParent(model.transform,false);
        var r=go.GetComponent<SkinnedMeshRenderer>();r.sharedMesh=Save(mesh,name+".asset");r.sharedMaterials=materials;r.bones=new[]{bone};r.rootBone=bone;r.localBounds=r.sharedMesh.bounds;r.updateWhenOffscreen=true;
    }
    private static T Save<T>(T value,string file) where T:Object
    {
        var existing=AssetDatabase.LoadAssetAtPath<T>(Art+file);
        if(existing==null){AssetDatabase.CreateAsset(value,Art+file);return value;}
        EditorUtility.CopySerialized(value,existing);Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;
    }

    private static RuntimeAnimatorController Controller(GameObject model,string kind,int variant)
    {
        string path="Assets/Game/Art/Enemies/Zombies/ArcadeZombie"+kind+".controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        var clips=new Dictionary<string,AnimationClip>();
        foreach(var name in new[]{"Idle","Walk","Run_Arms","Idle_Attack","HitReact","Death"})clips[name]=Motion(model,name);
        // 角色重制时保留已采用v2地面步态，禁止重新绑定旧小幅腿部动画或丢失移速步频。
        var groundWalk=AssetDatabase.LoadAssetAtPath<AnimationClip>(Art+kind+"GroundWalk.anim");
        foreach(var state in controller.layers[0].stateMachine.states.Select(s=>s.state))
        {
            string motion=state.name=="Run"?(variant==1?"Run_Arms":"Walk"):state.name=="Attack"?"Idle_Attack":state.name=="Hit"?"HitReact":state.name;
            bool adopted=state.name=="Run"&&groundWalk!=null;
            state.motion=adopted?groundWalk:clips[motion];state.speed=adopted?1/.9f:1;
            state.speedParameterActive=adopted;state.speedParameter=adopted?"MoveSpeed":string.Empty;
            EditorUtility.SetDirty(state);
        }
        EditorUtility.SetDirty(controller);return controller;
    }

    /// <summary>Generic曲线只驱动可见骨骼：慢行前伸双手、单次扑抓与受击；根不移动，不携带伤害规则。</summary>
    private static AnimationClip Motion(GameObject model,string name)
    {
        var bones=model.GetComponentsInChildren<Transform>(true).Where(t=>BoneNames.Contains(t.name)||new[]{"Hips","Abdomen","Neck","Middle1.L","Middle1.R","Foot.L","Foot.R"}.Contains(t.name)).ToArray();
        var rotations=bones.Select(t=>t.localRotation).ToArray();var positions=bones.Select(t=>t.localPosition).ToArray();
        var curves=bones.Select(_=>Enumerable.Range(0,4).Select(i=>new AnimationCurve()).ToArray()).ToArray();
        var body=bones.Single(t=>t.name=="Body");var y=new AnimationCurve();
        float duration=name=="Idle"?1.6f:name=="Walk"?1.1f:name=="Run_Arms"?.75f:name=="Idle_Attack"?.65f:name=="HitReact"?.35f:.9f;
        var clip=new AnimationClip{name="Civilian|"+name,frameRate=30};
        for(int frame=0;frame<=24;frame++)
        {
            float t=frame/24f,cycle=t*Mathf.PI*2;
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=positions[i];bones[i].localRotation=rotations[i];}
            var torso=bones.Single(b=>b.name=="Torso");torso.rotation=Quaternion.Euler(7+Mathf.Sin(cycle)*1.5f,0,Mathf.Sin(cycle)*1.5f)*torso.rotation;
            float attack=name=="Idle_Attack"?Mathf.Sin(t*Mathf.PI):0;
            foreach(var side in new[]{".L",".R"})
            {
                float sign=side==".L"?-1:1;
                Point(bones.Single(b=>b.name=="UpperArm"+side),bones.Single(b=>b.name=="LowerArm"+side),new Vector3(sign*.14f,-.65f+attack*1.05f,.55f));
                Point(bones.Single(b=>b.name=="LowerArm"+side),bones.Single(b=>b.name=="Middle1"+side),new Vector3(sign*.08f,-.18f+attack*.3f,.8f));
                var hand=bones.Single(b=>b.name=="Middle1"+side);var index=hand.GetComponentsInChildren<Transform>().First(b=>b.name.StartsWith("IndexFinger_01"));Point(hand,index,new Vector3(0,-.13f,1));
                if(name=="Walk"||name=="Run_Arms")
                {
                    float stride=Mathf.Sin(cycle)*sign*(name=="Walk"?.28f:.42f);
                    Point(bones.Single(b=>b.name=="UpperLeg"+side),bones.Single(b=>b.name=="LowerLeg"+side),new Vector3(sign*.025f,-1,stride));
                    Point(bones.Single(b=>b.name=="LowerLeg"+side),bones.Single(b=>b.name=="Foot"+side),new Vector3(0,-1,-Mathf.Max(0,-stride)*.7f));
                    var foot=bones.Single(b=>b.name=="Foot"+side);var toe=foot.GetComponentsInChildren<Transform>().First(b=>b!=foot);
                    Point(foot,toe,Vector3.forward);
                }
            }
            if(name=="HitReact")torso.rotation=Quaternion.Euler(-Mathf.Sin(t*Mathf.PI)*18,0,0)*torso.rotation;
            if(name=="Death")body.rotation=Quaternion.Euler(-t*60,0,t*12)*body.rotation;
            if(name=="Walk"||name=="Run_Arms")body.localPosition+=Vector3.up*Mathf.Abs(Mathf.Sin(cycle))*.012f;
            for(int i=0;i<bones.Length;i++){var q=bones[i].localRotation;for(int c=0;c<4;c++)curves[i][c].AddKey(t*duration,q[c]);}
            y.AddKey(t*duration,body.localPosition.y);
        }
        for(int i=0;i<bones.Length;i++)
        {string path=AnimationUtility.CalculateTransformPath(bones[i],model.transform);for(int c=0;c<4;c++)clip.SetCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[c],curves[i][c]);}
        clip.SetCurve(AnimationUtility.CalculateTransformPath(body,model.transform),typeof(Transform),"m_LocalPosition.y",y);
        clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=name=="Idle"||name=="Walk"||name=="Run_Arms";AnimationUtility.SetAnimationClipSettings(clip,settings);
        for(int i=0;i<bones.Length;i++){bones[i].localPosition=positions[i];bones[i].localRotation=rotations[i];}
        return Save(clip,name+".anim");
    }
    private static void Point(Transform bone,Transform end,Vector3 direction)=>bone.rotation=Quaternion.FromToRotation(end.position-bone.position,direction)*bone.rotation;
    /// <summary>独立动画脚骨只在物理阶段挂到小腿，保证活体动画与死亡落地都保留正确层级。</summary>
    private static void ConfigurePhysics(GameObject root, Animator animator, Transform model, float height)
    {
        var all = model.GetComponentsInChildren<Transform>(true);
        Func<string, Transform> bone = name => all.Single(t => t.name == name);
        var bodies = new Rigidbody[BoneNames.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            var transform = bone(BoneNames[i]); transform.gameObject.layer = LayerMask.NameToLayer(EnemyPhysicalReaction.BodyLayerName);
            var body = transform.gameObject.AddComponent<Rigidbody>(); bodies[i] = body;
            body.isKinematic = true; body.detectCollisions = false; body.mass = i == 0 ? 8 : i == 1 ? 10 : i == 2 ? 3 : 2;
            body.drag = .25f; body.angularDrag = 1f; body.solverIterations = 4; body.solverVelocityIterations = 1;
            var end = Ends[i] == null ? Vector3.up * height * .13f : bone(Ends[i]).position - transform.position;
            if (i == 0) end = Vector3.up * height * .18f;
            var localEnd = transform.InverseTransformVector(end);
            int axis = Mathf.Abs(localEnd.x) > Mathf.Abs(localEnd.y) ? 0 : 1;
            if (Mathf.Abs(localEnd.z) > Mathf.Abs(localEnd[axis])) axis = 2;
            var capsule = transform.gameObject.AddComponent<CapsuleCollider>(); capsule.direction = axis; capsule.center = localEnd * .5f;
            capsule.radius = height * (i < 2 ? .10f : i == 2 ? .085f : .04f) / Mathf.Max(.001f, Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z)));
            capsule.height = Mathf.Max(capsule.radius * 2, Mathf.Abs(localEnd[axis])); capsule.enabled = false;
        }
        for (int i = 1; i < bodies.Length; i++)
        {
            var joint = bodies[i].gameObject.AddComponent<CharacterJoint>(); joint.connectedBody = bodies[Parents[i]];
            joint.enableProjection = true; joint.projectionDistance = .08f; joint.projectionAngle = 15f;
            joint.lowTwistLimit = new SoftJointLimit { limit = -25 }; joint.highTwistLimit = new SoftJointLimit { limit = 25 };
            joint.swing1Limit = new SoftJointLimit { limit = i < 3 ? 25 : 45 }; joint.swing2Limit = new SoftJointLimit { limit = i < 3 ? 20 : 35 };
        }
        var serialized = new SerializedObject(root.GetComponent<EnemyPhysicalReaction>());
        serialized.FindProperty("animator").objectReferenceValue = animator; serialized.FindProperty("chest").objectReferenceValue = bone("Torso"); serialized.FindProperty("hips").objectReferenceValue = bone("Body");
        foreach (string field in new[] { "bodies", "restPositions", "restRotations" }) serialized.FindProperty(field).arraySize = bodies.Length;
        for (int i = 0; i < bodies.Length; i++)
        {
            serialized.FindProperty("bodies").GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            serialized.FindProperty("restPositions").GetArrayElementAtIndex(i).vector3Value = bodies[i].transform.localPosition;
            serialized.FindProperty("restRotations").GetArrayElementAtIndex(i).quaternionValue = bodies[i].transform.localRotation;
        }
        serialized.FindProperty("detachedEndBones").arraySize = 2; serialized.FindProperty("endBonePhysicsParents").arraySize = 2;
        for (int i = 0; i < 2; i++)
        {
            string side = i == 0 ? ".L" : ".R";
            serialized.FindProperty("detachedEndBones").GetArrayElementAtIndex(i).objectReferenceValue = bone("Foot" + side);
            serialized.FindProperty("endBonePhysicsParents").GetArrayElementAtIndex(i).objectReferenceValue = bone("LowerLeg" + side);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
