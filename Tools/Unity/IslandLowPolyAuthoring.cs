/// <summary>复用已采用 v2 的精确生成过程，将临时网格与材质保存为正式依赖；不改配置或碰撞。</summary>
public static partial class IslandLowPolyPreview
{
    private const string FormalIsland="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeIslandVisualSet.prefab";
    private const string FormalBank="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    private const string LowPolyFolder="Assets/Game/Art/Arcade/Island/LowPolyV2";
    private const string LibraryPath=LowPolyFolder+"/IslandLowPolyV2.asset";

    /// <summary>仅在用户明确采用 v2 后执行；旧资源及 GUID 保留，相同版本重复执行不叠加缩放或地表。</summary>
    public static string Bake()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在 FirepowerLoop 编辑态接入已确认海岛");
        var prefab=PrefabUtility.LoadPrefabContents(FormalIsland);
        try
        {
            var rounds=prefab.transform.Find("FixedRounds");
            if(rounds==null||rounds.childCount!=4)throw new InvalidOperationException("缺少四轮正式地形");
            int applied=rounds.Cast<Transform>().Count(t=>t.Find("LowPolyDetails")!=null);
            if(applied!=0&&applied!=4)throw new InvalidOperationException("海岛存在部分接入状态，请先核查资源");
            if(applied==0)
            {
                SetupPalette();palm=Palm();boulder=Icosahedron();
                for(int round=1;round<=4;round++)
                {
                    var owner=rounds.Find("Round"+round);owner.gameObject.SetActive(true);
                    ApplyCandidate(owner.gameObject,round);AddRichness(owner.gameObject,round);
                    owner.Find("TEMP_Richness").name="LowPolyDetails";
                    owner.Find("WaterPalette").GetComponent<Renderer>().sharedMaterial=sea;
                    var palette=new GameObject("SurfacePalette",typeof(MeshRenderer));palette.transform.SetParent(owner,false);
                    var renderer=palette.GetComponent<MeshRenderer>();renderer.sharedMaterials=new[]{sand,shallows,foam};renderer.enabled=false;
                    owner.gameObject.SetActive(false);
                }
                var stored=PersistLibrary();
                foreach(var filter in rounds.GetComponentsInChildren<MeshFilter>(true))
                    if(filter.sharedMesh!=null&&stored.TryGetValue(filter.sharedMesh,out var mesh))filter.sharedMesh=(Mesh)mesh;
                foreach(var renderer in rounds.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m!=null&&stored.TryGetValue(m,out var material)?(Material)material:m).ToArray();
                PrefabUtility.SaveAsPrefabAsset(prefab,FormalIsland);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
            foreach(var asset in Owned)if(asset!=null&&!AssetDatabase.Contains(asset))Object.DestroyImmediate(asset);
            Owned.Clear();
        }
        // 集合沿用原入口；若是嵌套 Prefab，导入即可继承更新，避免重建无关内容与覆盖。
        AssetDatabase.ImportAsset(FormalIsland,ImportAssetOptions.ForceUpdate);
        var bank=PrefabUtility.LoadPrefabContents(FormalBank);
        try
        {
            var old=bank.transform.Find("Island");
            if(old==null)throw new InvalidOperationException("场景集合缺少 Island");
            var source=PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject);
            if(AssetDatabase.GetAssetPath(source)!=FormalIsland)
            {
                int index=old.GetSiblingIndex();Object.DestroyImmediate(old.gameObject);
                var copy=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FormalIsland),bank.transform);
                copy.name="Island";copy.SetActive(false);copy.transform.SetSiblingIndex(index);
                PrefabUtility.SaveAsPrefabAsset(bank,FormalBank);
            }
            for(int round=1;round<=4;round++)
                if(bank.transform.Find("Island/FixedRounds/Round"+round+"/LowPolyDetails")==null)
                    throw new InvalidOperationException("正式集合未继承第"+round+"轮低多边形地形");
        }
        finally{PrefabUtility.UnloadPrefabContents(bank);}
        return "四轮已采用 v2 正式资源及集合依赖已保存；中央不新增阻挡";
    }

    /// <summary>所有新素材集中到一个带稳定 GUID 的资源库；重跑时按稳定名称更新子资源，不删除旧素材。</summary>
    private static Dictionary<Object,Object> PersistLibrary()
    {
        Directory.CreateDirectory(LowPolyFolder);AssetDatabase.Refresh();
        var result=new Dictionary<Object,Object>();
        var existing=AssetDatabase.LoadAllAssetsAtPath(LibraryPath);
        for(int index=0;index<Owned.Count;index++)
        {
            var source=Owned[index];source.name="IslandLP_"+index.ToString("D3")+"_"+source.name.Replace("TEMP_LP_","").Replace("TEMP_","");
            var target=existing.FirstOrDefault(p=>p!=null&&p.name==source.name&&p.GetType()==source.GetType());
            if(target!=null){EditorUtility.CopySerialized(source,target);EditorUtility.SetDirty(target);result.Add(source,target);}
            else
            {
                if(!File.Exists(LibraryPath))AssetDatabase.CreateAsset(source,LibraryPath);
                else AssetDatabase.AddObjectToAsset(source,LibraryPath);
                result.Add(source,source);
            }
        }
        // 只落盘本资源库，避免把编辑器中不属于本任务的脏材质、字体等一起保存。
        var library=AssetDatabase.LoadMainAssetAtPath(LibraryPath);
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssetIfDirty(library);return result;
    }
}
