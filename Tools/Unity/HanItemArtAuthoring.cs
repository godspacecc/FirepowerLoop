using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game;
using Object = UnityEngine.Object;

/// <summary>离线装配首章道具、拾取壳和同源图标；不修改运行时规则或第三方资源。</summary>
public static class HanItemArtAuthoring
{
    /// <summary>仅修复正式改良弓的朝向与动态表现，不重新生成其它道具、图标或拾取物。</summary>
    public static string RunBowOnly()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Art + "/Models/HanBow.fbx");
        if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        const string path = "Assets/Game/Prefabs/Characters/Weapons/HanBow.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { ConfigureBow(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "Configured HanBow only";
    }

    private static void ConfigureBow(GameObject root)
    {
        var filter = root.GetComponentInChildren<MeshFilter>();
        var facing = root.transform.Find("BowFacing");
        if (facing == null)
        {
            facing = new GameObject("BowFacing").transform;
            facing.SetParent(root.transform, false);
            filter.transform.SetParent(facing, false);
        }
        // FBX 的弦在负 X，角色拉弦手在正 X；内部节点校准不覆盖共享目录握点。
        facing.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var view = root.GetComponent<BowVisualView>() ?? root.AddComponent<BowVisualView>();
        var serialized = new SerializedObject(view);
        serialized.FindProperty("bowMesh").objectReferenceValue = filter;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private const string Art = "Assets/Game/Art/Civilisation/HanItems";
    private static readonly string[] Names = { "HanForgedAxe", "HanForgedPick", "HanSteelBlade", "HanBow", "HanGrainPouch", "HanWaterPot" };
    private static readonly string[] Labels = { "精锻斧", "精锻镐", "钢刀", "改良弓", "熟粮口粮", "陶水罐" };
    private static readonly string[] Materials = { "HanIron", "HanEdge", "HanWood", "HanWrap", "HanCord", "HanClay", "HanCloth", "HanGrain" };
    private static readonly Color[] Colors = { new Color(.15f,.18f,.20f), new Color(.57f,.64f,.67f), new Color(.27f,.12f,.052f), new Color(.16f,.075f,.032f), new Color(.65f,.48f,.27f), new Color(.25f,.29f,.27f), new Color(.48f,.35f,.19f), new Color(.83f,.66f,.35f) };

    public static string Run()
    {
        Directory.CreateDirectory(Art + "/Materials");
        AssetDatabase.Refresh();
        for (int i=0;i<Materials.Length;i++)
        {
            var path=Art+"/Materials/"+Materials[i]+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",Colors[i]);mat.SetFloat("_Metallic",i<2?.65f:0);mat.SetFloat("_Smoothness",i<2?.4f:.16f);
            EditorUtility.SetDirty(mat);
        }
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            for(int i=0;i<Names.Length;i++)
            {
                var name=Names[i];
                var visual=new GameObject(name);SceneManager.MoveGameObjectToScene(visual,scene);
                try
                {
                    var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Models/"+name+".fbx"),scene);
                    model.transform.SetParent(visual.transform,false);
                    foreach(var r in model.GetComponentsInChildren<Renderer>())
                        r.sharedMaterials=r.sharedMaterials.Select(m=>AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/"+m.name+".mat") ?? throw new InvalidOperationException(m.name)).ToArray();
                    // Blender FBX 保留轴转换与单位缩放，握点仍在原点；刀对齐既有短刀的 +Z 朝向。
                    RenderIcon(visual,name,scene);
                    var prefabPath=i<4?"Assets/Game/Prefabs/Characters/Weapons/"+name+".prefab":Art+"/"+name+".prefab";
                    // 外观姿态放在内部节点，根节点由现有配置的握点契约控制。
                    if(i==2){var pose=new GameObject("BladePose").transform;pose.SetParent(visual.transform,false);model.transform.SetParent(pose,false);pose.localRotation=Quaternion.Euler(90,0,0);visual.transform.rotation=Quaternion.identity;}
                    if(i==3)ConfigureBow(visual);
                    PrefabUtility.SaveAsPrefabAsset(visual,prefabPath);
                    CreatePickup(visual,i,scene);
                }
                finally{Object.DestroyImmediate(visual);}
            }
            UpdateWeaponCatalog();
            UpdateCollectors();
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();return "Created six visuals, six pickup prefabs, six icons and four shared weapon mappings";
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }

    /// <summary>新增资源沿用同类正式收集规则，确保配置地址可解析；不执行资源构建。</summary>
    private static void UpdateCollectors()
    {
        var settings=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
        var serialized=new SerializedObject(settings);var packages=serialized.FindProperty("Packages");
        for(int p=0;p<packages.arraySize;p++)
        {
            var package=packages.GetArrayElementAtIndex(p);if(package.FindPropertyRelative("PackageName").stringValue!="DefaultPackage")continue;
            var groups=package.FindPropertyRelative("Groups");
            for(int g=0;g<groups.arraySize;g++)
            {
                var collectors=groups.GetArrayElementAtIndex(g).FindPropertyRelative("Collectors");
                for(int kind=0;kind<2;kind++)
                {
                    var folder=kind==0?"Assets/Game/Prefabs/Characters/Weapons/":"Assets/Game/Prefabs/SceneObjects/";
                    var basePath=folder+(kind==0?"AxeShared":"TestResourcePickup")+".prefab";
                    int source=-1;
                    for(int c=0;c<collectors.arraySize;c++)if(collectors.GetArrayElementAtIndex(c).FindPropertyRelative("CollectPath").stringValue==basePath)source=c;
                    if(source<0)continue;
                    for(int i=0;i<(kind==0?4:6);i++)
                    {
                        var path=folder+Names[i]+(kind==0?"":"Pickup")+".prefab";bool exists=false;
                        for(int c=0;c<collectors.arraySize;c++)if(collectors.GetArrayElementAtIndex(c).FindPropertyRelative("CollectPath").stringValue==path)exists=true;
                        if(exists)continue;
                        collectors.InsertArrayElementAtIndex(source);collectors.MoveArrayElement(source,collectors.arraySize-1);
                        var entry=collectors.GetArrayElementAtIndex(collectors.arraySize-1);
                        entry.FindPropertyRelative("CollectPath").stringValue=path;
                        entry.FindPropertyRelative("CollectorGUID").stringValue=AssetDatabase.AssetPathToGUID(path);
                    }
                }
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(settings);
    }

    /// <summary>复制基础武器的动作与握持契约；仅创建本章身份，重复运行更新原条目。</summary>
    private static void UpdateWeaponCatalog()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>("Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
        var serialized=new SerializedObject(catalog);var entries=serialized.FindProperty("weapons");
        var bases=new[]{17,18,16,6};var ids=new[]{"weapon.han.axe","weapon.han.pick","weapon.han.blade","weapon.han.bow"};
        for(int i=0;i<4;i++)
        {
            int target=-1,source=-1;
            for(int j=0;j<entries.arraySize;j++)
            {
                int itemId=entries.GetArrayElementAtIndex(j).FindPropertyRelative("worldItemId").intValue;
                if(itemId==32+i)target=j;if(itemId==bases[i])source=j;
            }
            if(source<0)throw new InvalidOperationException("基础武器映射缺失："+bases[i]);
            if(target<0){entries.InsertArrayElementAtIndex(source);entries.MoveArrayElement(source,entries.arraySize-1);target=entries.arraySize-1;}
            var entry=entries.GetArrayElementAtIndex(target);
            entry.FindPropertyRelative("id").stringValue=ids[i];entry.FindPropertyRelative("worldItemId").intValue=32+i;
            entry.FindPropertyRelative("displayName").stringValue=Labels[i];
            entry.FindPropertyRelative("visualSource").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/Weapons/"+Names[i]+".prefab");
            entry.FindPropertyRelative("mesh").objectReferenceValue=null;entry.FindPropertyRelative("material").objectReferenceValue=null;
            // 新模型以握柄中点建模，独立校准握点，避免沿用旧模型偏移后刀刃朝向身体。
            if(i<3)
            {
                var rotation=Quaternion.Euler(71.84f,151.37f,10.59f);
                if(i==2)rotation*=Quaternion.Euler(-90,0,0);
                entry.FindPropertyRelative("handLocalPosition").vector3Value=new Vector3(.05f,.02f,-.03f);
                entry.FindPropertyRelative("handLocalEulerAngles").vector3Value=rotation.eulerAngles;
                entry.FindPropertyRelative("handLocalScale").vector3Value=Vector3.one;
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if(!catalog.ValidateIdentities(out var error))throw new InvalidOperationException(error);
        EditorUtility.SetDirty(catalog);
    }

    private static Bounds BoundsOf(GameObject root)
    {
        var renderers=root.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;
        foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;
    }

    private static void CreatePickup(GameObject visual,int index,Scene scene)
    {
        var pickup=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"));
        SceneManager.MoveGameObjectToScene(pickup,scene);
        try
        {
            pickup.name=Names[index]+"Pickup";pickup.transform.localScale=Vector3.one;
            Object.DestroyImmediate(pickup.GetComponent<MeshRenderer>());Object.DestroyImmediate(pickup.GetComponent<MeshFilter>());
            var appearance=Object.Instantiate(visual,pickup.transform);appearance.name="ItemVisual";
            appearance.transform.localRotation=index<4 && index!=2?Quaternion.Euler(90,0,0):Quaternion.identity;
            var bounds=BoundsOf(appearance);appearance.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            bounds=BoundsOf(appearance);
            var collider=pickup.GetComponent<BoxCollider>();collider.isTrigger=true;
            collider.size=new Vector3(Mathf.Max(.25f,bounds.size.x),Mathf.Max(.1f,bounds.size.y),Mathf.Max(.25f,bounds.size.z));
            collider.center=new Vector3(0,collider.size.y*.5f,0);
            var view=pickup.GetComponent<ResourcePickupView>();var serialized=new SerializedObject(view);
            serialized.FindProperty("displayName").stringValue=Labels[index];
            serialized.FindProperty("defaultRenderer").objectReferenceValue=appearance.GetComponentInChildren<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            view.NameRoot.transform.localPosition=new Vector3(0,Mathf.Max(.45f,bounds.size.y+.18f),0);view.NameRoot.transform.localScale=Vector3.one*.45f;
            PrefabUtility.SaveAsPrefabAsset(pickup,"Assets/Game/Prefabs/SceneObjects/"+pickup.name+".prefab");
        }
        finally{Object.DestroyImmediate(pickup);}
    }

    /// <summary>只渲染正式模型和材质，透明底；纹理作为正式 Sprite 导入。</summary>
    private static void RenderIcon(GameObject root,string name,Scene scene)
    {
        var cameraObject=new GameObject("IconCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);
        var lightObject=new GameObject("IconKey",typeof(Light));SceneManager.MoveGameObjectToScene(lightObject,scene);
        var fillObject=new GameObject("IconFill",typeof(Light));SceneManager.MoveGameObjectToScene(fillObject,scene);
        var target=new RenderTexture(512,512,24);var texture=new Texture2D(512,512,TextureFormat.RGBA32,false);var old=RenderTexture.active;
        try
        {
            var bounds=BoundsOf(root);var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.orthographic=true;camera.orthographicSize=bounds.size.magnitude*.59f;
            camera.transform.position=bounds.center+new Vector3(.65f,.35f,-2).normalized*(bounds.size.magnitude*3+4);camera.transform.LookAt(bounds.center);camera.targetTexture=target;
            var light=lightObject.GetComponent<Light>();light.type=LightType.Directional;light.intensity=2.1f;light.transform.rotation=Quaternion.Euler(35,-30,0);
            var fill=fillObject.GetComponent<Light>();fill.type=LightType.Directional;fill.intensity=.8f;fill.transform.rotation=Quaternion.Euler(20,140,0);
            camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,512,512),0,0);texture.Apply();
            var path="Assets/Game/Art/UI/Items/Icon"+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=512;importer.SaveAndReimport();
        }
        finally{RenderTexture.active=old;Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(fillObject);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
    }
}
