using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using Game;

public static class ClothingUiAuthoring
{
    /// <summary>View 编译完成后调用；用项目生成器绑定新增字段，再修复宿主对新 Page 组件的引用。</summary>
    public static string BindAfterCompilation()
    {
        const string pagePath="Assets/Game/Prefabs/UI/Inventory/Page/EquipmentManagement.prefab";
        const string bagPath="Assets/Game/Prefabs/UI/Inventory/Bag.prefab";
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        typeof(PageCodeSpawner).GetMethod("BindGeneratedComponent",flags).Invoke(null,new object[]{pagePath,"Game.EquipmentManagementPageUI"});
        var bag=PrefabUtility.LoadPrefabContents(bagPath);
        try{bag.GetComponent<ReferenceCollector>().Add("EquipmentPage",bag.GetComponentInChildren<EquipmentManagementPageUI>(true));PrefabUtility.SaveAsPrefabAsset(bag,bagPath);}
        finally{PrefabUtility.UnloadPrefabContents(bag);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(bagPath));
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",flags).Invoke(null,new object[]{bagPath,"Game.BagUI"});
        return "Page 与 Bag 的生成引用已绑定";
    }
    /// <summary>把稳定按钮与预览区域写进原装备页，View 由原生成器重新生成。</summary>
    public static string Run()
    {
        const string path="Assets/Game/Prefabs/UI/Inventory/Page/EquipmentManagement.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var rc=root.GetComponent<ReferenceCollector>();
            var panel=root.transform.Find("PaperDollPanel");panel.Find("BodyOutline").gameObject.SetActive(false);panel.Find("BodyNote").gameObject.SetActive(false);
            foreach(var part in new[]{"BtnHead","BtnTorso","BtnHands","BtnLegs","BtnFeet","BtnBack","BtnAccessory"})panel.Find(part+"/Unavailable").gameObject.SetActive(false);
            var armorLabel=panel.Find("BtnAccessory/Label");
            armorLabel.GetComponent<TextMeshProUGUI>().text="护甲";
            var image=panel.Find("CharacterPreview")?.GetComponent<RawImage>();
            if(image==null)
            {
                var obj=new GameObject("CharacterPreview",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));obj.transform.SetParent(panel,false);
                var rect=obj.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(210,440);rect.anchoredPosition=Vector2.zero;
                image=obj.GetComponent<RawImage>();image.raycastTarget=false;obj.transform.SetSiblingIndex(2);
            }
            var button=root.transform.Find("DetailPanel/BtnUnequip")?.GetComponent<Button>();
            if(button==null)
            {
                var obj=Object.Instantiate(panel.Find("BtnHead").gameObject,root.transform.Find("DetailPanel"));obj.name="BtnUnequip";
                Object.DestroyImmediate(obj.transform.Find("Unavailable").gameObject);
                var rect=obj.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(180,54);rect.anchoredPosition=new Vector2(0,36);
                var label=obj.transform.Find("Label");var text=label.GetComponent<TextMeshProUGUI>();text.text="卸下";
                var loc=label.GetComponent<LocalizedText>();var so=new SerializedObject(loc);so.FindProperty("Key").stringValue="ui.inventory.unequip";so.ApplyModifiedPropertiesWithoutUndo();
                var lr=label.GetComponent<RectTransform>();lr.anchorMin=Vector2.zero;lr.anchorMax=Vector2.one;lr.offsetMin=lr.offsetMax=Vector2.zero;
                button=obj.GetComponent<Button>();
            }
            rc.Add("BtnUnequip",button);rc.Add("CharacterPreview",image);
            rc.Add("CharacterPreviewPrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/OutfitPrototype/ClothingCharacterPreview.prefab"));
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        PageCodeSpawner.AutoUpdatePage(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        return "装备页 Prefab 已更新并触发 View 生成";
    }
}

