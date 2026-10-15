using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Game;

/// <summary>把背包与装备人物区域写入正式 Prefab；引用和 View 始终交给项目生成器。</summary>
public static class InventoryCharacterPreviewAuthoring
{
    const string BagPath="Assets/Game/Prefabs/UI/Inventory/Bag.prefab";
    const string PagePath="Assets/Game/Prefabs/UI/Inventory/Page/EquipmentManagement.prefab";
    public static string Run()
    {
        var page=PrefabUtility.LoadPrefabContents(PagePath);
        try
        {
            var panel=(RectTransform)page.transform.Find("PaperDollPanel");
            panel.GetComponent<Image>().color=new Color(.09f,.115f,.13f,1f);
            panel.anchorMax=new Vector2(.62f,1);
            var details=(RectTransform)page.transform.Find("DetailPanel");details.anchorMin=new Vector2(.64f,0);
            var image=panel.Find("CharacterPreview").GetComponent<RawImage>();
            FitPreview(image,new Vector2(.28f,.025f),new Vector2(.72f,.975f));
            PrefabUtility.SaveAsPrefabAsset(page,PagePath);
        }
        finally{PrefabUtility.UnloadPrefabContents(page);}
        PageCodeSpawner.AutoUpdatePage(AssetDatabase.LoadAssetAtPath<GameObject>(PagePath));

        var bag=PrefabUtility.LoadPrefabContents(BagPath);
        try
        {
            var pageRoot=bag.transform.Find("ManagementRoot/InventoryPageRoot");
            var pageRect=(RectTransform)pageRoot;pageRect.anchorMin=Vector2.zero;pageRect.anchorMax=Vector2.one;
            var storage=(RectTransform)pageRoot.Find("Storage");storage.anchorMin=new Vector2(.27f,0);storage.anchorMax=new Vector2(.65f,1);
            FitGrid(storage.Find("PocketList").gameObject);
            FitGrid(storage.Find("BagViewport/FullPlayerBagList").gameObject);
            foreach(var name in new[]{"PocketHeader","BackpackHeader"})
            {
                var label=storage.Find(name).GetComponent<TMPro.TextMeshProUGUI>();label.fontSize=24;
                var labelRect=(RectTransform)label.transform;labelRect.sizeDelta=new Vector2(labelRect.sizeDelta.x,44);
            }
            foreach(var name in new[]{"FullDetailRoot","EmptyDetailHint","FullActionRoot"})
                ((RectTransform)pageRoot.Find(name)).anchorMin=new Vector2(.67f,((RectTransform)pageRoot.Find(name)).anchorMin.y);
            var previewPanel=pageRoot.Find("CharacterPanel");
            if(previewPanel==null)
            {
                var go=new GameObject("CharacterPanel",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));go.transform.SetParent(pageRoot,false);
                go.GetComponent<Image>().raycastTarget=false;
                previewPanel=go.transform;
            }
            previewPanel.GetComponent<Image>().color=bag.transform.Find("ManagementRoot").GetComponent<Image>().color;
            var rect=(RectTransform)previewPanel;rect.anchorMin=Vector2.zero;rect.anchorMax=new Vector2(.25f,1);rect.offsetMin=rect.offsetMax=Vector2.zero;
            var image=previewPanel.Find("CharacterPreview")?.GetComponent<RawImage>();
            if(image==null)
            {
                var go=new GameObject("CharacterPreview",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));go.transform.SetParent(previewPanel,false);image=go.GetComponent<RawImage>();
            }
            FitPreview(image,new Vector2(.03f,.025f),new Vector2(.97f,.975f));
            bag.GetComponent<ReferenceCollector>().Add("BackpackCharacterPreview",image);
            PrefabUtility.SaveAsPrefabAsset(bag,BagPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(bag);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(BagPath));
        return "两页人物布局与 Bag View 已生成；编译后调用 Bind";
    }
    public static string Bind()
    {
        // Run 的生成器包含延迟绑定，必须等待编译/编辑器回调后再修复宿主 Page 引用，不能重复生成 Page。
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        typeof(PageCodeSpawner).GetMethod("BindGeneratedComponent",flags).Invoke(null,new object[]{PagePath,"Game.EquipmentManagementPageUI"});
        var bag=PrefabUtility.LoadPrefabContents(BagPath);
        try
        {
            bag.GetComponent<ReferenceCollector>().Add("EquipmentPage",bag.GetComponentInChildren<EquipmentManagementPageUI>(true));
            PrefabUtility.SaveAsPrefabAsset(bag,BagPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(bag);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(BagPath));
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",flags).Invoke(null,new object[]{BagPath,"Game.BagUI"});
        return "两页生成引用已绑定";
    }
    static void FitPreview(RawImage image,Vector2 min,Vector2 max)
    {
        var rect=image.rectTransform;rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;
        image.raycastTarget=true;
        var fit=image.GetComponent<AspectRatioFitter>()??image.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=.5f;
        // 生成发生在运行时代码编译之前；类型就绪后 Bind 再补组件。
        var drag=typeof(BagUI).Assembly.GetType("Game.ClothingPreviewDragView");
        if(drag!=null&&image.GetComponent(drag)==null)image.gameObject.AddComponent(drag);
    }
    static void FitGrid(GameObject list)
    {
        var old=list.GetComponent<GridLayoutGroup>();
        if(old is InventoryManagementGridLayout)return;
        var spacing=old.spacing;var padding=old.padding;var cell=old.cellSize;var alignment=old.childAlignment;
        Object.DestroyImmediate(old);
        var grid=list.AddComponent<InventoryManagementGridLayout>();
        grid.spacing=spacing;grid.padding=padding;grid.cellSize=cell;grid.childAlignment=alignment;
        grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=4;
    }
}
