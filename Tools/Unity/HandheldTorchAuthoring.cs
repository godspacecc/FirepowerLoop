using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Game;

/// <summary>离线制作木柄、包布和束绳火把；只生成本功能资源，不改第三方资产或场景。</summary>
public static class HandheldTorchAuthoring
{
    const string Art="Assets/Game/Art/Items/HandheldTorch";
    static void Set(object target,string name,object value) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    static Material Mat(string name,Color color)
    {
        var path=Art+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) {material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",0.08f);
        EditorUtility.SetDirty(material);return material;
    }
    static GameObject Part(GameObject root,string name,PrimitiveType shape,Vector3 pos,Vector3 scale,Material mat)
    {
        var go=GameObject.CreatePrimitive(shape);go.name=name;go.transform.SetParent(root.transform,false);
        go.transform.localPosition=pos;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    public static string Run()
    {
        Directory.CreateDirectory(Art);AssetDatabase.Refresh();
        var wood=Mat("TorchWood",new Color(.23f,.105f,.04f));
        var cloth=Mat("TorchCloth",new Color(.31f,.24f,.145f));
        var cord=Mat("TorchCord",new Color(.40f,.28f,.13f));
        var charred=Mat("TorchChar",new Color(.035f,.025f,.016f));
        var root=new GameObject("HandheldTorch");
        try
        {
            // 握点原点，上半截燃烧头远离手部；长度以米计。
            Part(root,"木柄",PrimitiveType.Cylinder,new Vector3(0,.20f,0),new Vector3(.035f,.34f,.035f),wood);
            Part(root,"包布燃烧头",PrimitiveType.Capsule,new Vector3(0,.49f,0),new Vector3(.09f,.095f,.09f),cloth);
            Part(root,"炭化顶端",PrimitiveType.Sphere,new Vector3(0,.568f,0),new Vector3(.086f,.045f,.083f),charred);
            for(int i=0;i<8;i++)
            {
                var wrap=Part(root,"束绳"+i,PrimitiveType.Cylinder,new Vector3(0,.415f+i*.017f,0),new Vector3(.098f,.0024f,.098f),cord);
                wrap.transform.localRotation=Quaternion.Euler(4f,i*37f,6f);
            }
            var camp=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/Camp_RPGFXCampfire.prefab");
            if(camp==null||camp.GetComponent<TemporaryBuildingView>().CommittedEffectRoot==null)throw new InvalidOperationException("正式营火效果缺失");
            var effects=UnityEngine.Object.Instantiate(camp.GetComponent<TemporaryBuildingView>().CommittedEffectRoot,root.transform);
            effects.name="燃烧效果";effects.transform.localPosition=new Vector3(0,.55f,0);effects.transform.localRotation=Quaternion.identity;effects.transform.localScale=Vector3.one*.3f;
            foreach(var light in effects.GetComponentsInChildren<Light>(true)) {light.range=5f;light.intensity=2.2f;light.shadows=LightShadows.None;}
            var effect=effects.GetComponent<CampfireVisualEffect>();
            if(effect!=null) {var so=new SerializedObject(effect);so.FindProperty("baseIntensity").floatValue=2.2f;so.FindProperty("flickerAmplitude").floatValue=.2f;so.ApplyModifiedPropertiesWithoutUndo();}
            if(effect!=null)
            {
                var so=new SerializedObject(effect);
                var ps=(ParticleSystem)so.FindProperty("flameParticles").objectReferenceValue;
                var flameMat=(Material)so.FindProperty("flameMaterial").objectReferenceValue;
                ps.transform.localPosition=Vector3.zero;
                typeof(CampfireVisualEffect).GetMethod("ConfigureParticles",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{ps,flameMat});
                foreach(var particles in effects.GetComponentsInChildren<ParticleSystem>(true))
                {var main=particles.main;main.scalingMode=ParticleSystemScalingMode.Hierarchy;}
            }
            effects.SetActive(false);
            var view=root.AddComponent<HandheldTorchView>();var serialized=new SerializedObject(view);
            serialized.FindProperty("burningRoot").objectReferenceValue=effects;serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/Game/Prefabs/Characters/Weapons/HandheldTorch.prefab");
            if(prefab==null)throw new InvalidOperationException("火把 Prefab 保存失败");
            var catalog=AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>("Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            if(catalog==null)throw new InvalidOperationException("共享手持目录缺失");
            var list=Enumerable.Range(0,catalog.Count).Select(catalog.Get).Where(e=>e.WorldItemId!=48).ToList();
            var entry=JsonUtility.FromJson<CharacterWeaponCatalog.Entry>(JsonUtility.ToJson(catalog.FindByWorldItemId(5)));
            Set(entry,"id","tool.torch");Set(entry,"worldItemId",48);Set(entry,"displayName","火把");Set(entry,"visualSource",prefab);
            Set(entry,"mesh",null);Set(entry,"material",null);Set(entry,"handLocalPosition",new Vector3(.015f,.025f,0));
            Set(entry,"handLocalEulerAngles",new Vector3(0,0,90));Set(entry,"handLocalScale",Vector3.one);
            var rig=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            try
            {
                var animator=rig.GetComponent<PlayerView>().Animator;
                entry.Animations.Idle.Clip.SampleAnimation(animator.gameObject,.2f);
                var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
                Set(entry,"handLocalEulerAngles",(Quaternion.Inverse(hand.rotation)*Quaternion.LookRotation(rig.transform.forward,Vector3.up)).eulerAngles);
            }
            finally {UnityEngine.Object.DestroyImmediate(rig);}
            Set(entry,"animationPoseOverrides",Array.Empty<CharacterWeaponCatalog.Entry.AnimationPoseOverride>());
            list.Add(entry);typeof(CharacterWeaponCatalog).GetField("weapons",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(catalog,list.ToArray());EditorUtility.SetDirty(catalog);
            var pickup=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"));
            try
            {
                pickup.name="HandheldTorchPickup";
                foreach(var renderer in pickup.GetComponentsInChildren<Renderer>())renderer.enabled=false;
                var model=UnityEngine.Object.Instantiate(prefab,pickup.transform);model.name="火把模型";
                UnityEngine.Object.DestroyImmediate(model.GetComponent<HandheldTorchView>());
                model.transform.localPosition=new Vector3(0,.07f,0);model.transform.localRotation=Quaternion.Euler(0,0,90);
                PrefabUtility.SaveAsPrefabAsset(pickup,"Assets/Game/Prefabs/SceneObjects/HandheldTorchPickup.prefab");
            }
            finally {UnityEngine.Object.DestroyImmediate(pickup);}
            Directory.CreateDirectory("Assets/Game/Art/UI/Items");
            RenderIcon(root,"Assets/Game/Art/UI/Items/IconHandheldTorch.png");
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            return "Created handheld torch, pickup, icon and shared catalog entry.";
        }
        finally {UnityEngine.Object.DestroyImmediate(root);}
    }
    static void RenderIcon(GameObject target,string path)
    {
        var oldPos=target.transform.position;target.transform.position=new Vector3(1000,1000,1000);
        var camGo=new GameObject("TorchIconCamera");var lightGo=new GameObject("TorchIconLight");
        var rt=new RenderTexture(512,512,24);var previous=RenderTexture.active;Texture2D texture=null;
        try
        {
            var cam=camGo.AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.clear;cam.orthographic=true;cam.orthographicSize=.43f;cam.targetTexture=rt;cam.nearClipPlane=.01f;cam.farClipPlane=5;
            var centre=target.transform.position+new Vector3(0,.22f,0);cam.transform.position=centre+new Vector3(.75f,.25f,-1.2f);cam.transform.LookAt(centre);
            var light=lightGo.AddComponent<Light>();light.type=LightType.Point;light.range=4;light.intensity=4;light.transform.position=centre+new Vector3(.5f,1,-1);
            cam.Render();RenderTexture.active=rt;texture=new Texture2D(512,512,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,512,512),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.SaveAndReimport();
        }
        finally {target.transform.position=oldPos;RenderTexture.active=previous;camGo.GetComponent<Camera>().targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(camGo);UnityEngine.Object.DestroyImmediate(lightGo);}
    }
}
