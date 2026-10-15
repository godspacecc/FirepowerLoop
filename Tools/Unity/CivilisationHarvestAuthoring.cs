using System;
using System.Linq;
using Game;
using Luban;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>幂等接入河谷工具采集点；复用正式树木、石块与工具资源，不重建河流、边界或原交互点。</summary>
public static class CivilisationHarvestAuthoring
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("仅允许编辑模式制作");
        var scene = SceneManager.GetSceneByPath("Assets/Game/Scenes/CivilisationValleyMap.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("请先打开河谷场景");
        if (scene.isDirty) throw new InvalidOperationException("河谷场景有未保存修改");
        var content = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "MapContentRoot");
        var config = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
        if (!config.TryGetCivilisationCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
        var npc = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/BaseNpcMapGuide.prefab");
        var promptTemplate = (GameObject)new SerializedObject(npc.GetComponent<NpcView>()).FindProperty("nameRoot").objectReferenceValue;
        // 先验证全部引用，避免缺失资源时留下一半制作内容。
        foreach (var point in catalog.Points.Where(p => p.RequiredWeaponItemId != 0))
        {
            var treeName = BoundResourceName(point);
            var tree = content.GetComponentsInChildren<Transform>(true).Single(t => t.name == treeName);
            if ((new Vector2(tree.position.x, tree.position.z) - new Vector2(point.Position.x, point.Position.z)).sqrMagnitude > .01f)
                throw new InvalidOperationException("资源坐标与配置不一致：" + point.Id);
        }
        var root = content.Find("HarvestPoints");
        if (root == null) { var go = new GameObject("HarvestPoints"); go.transform.SetParent(content, false); root = go.transform; }
        foreach (var point in catalog.Points.Where(p => p.RequiredWeaponItemId != 0 || p.Repeatable || p.Id == "camp.axe" || p.Id == "camp.cloth" || p.Id == "camp.pickaxe"))
        {
            var node = root.Find(point.Id);
            if (node == null) { var go = new GameObject(point.Id); go.SetActive(false); go.transform.SetParent(root, false); node = go.transform; }
            node.position = point.Position;
            var view = node.GetComponent<CivilisationPointView>() ?? node.gameObject.AddComponent<CivilisationPointView>();
            var trigger = node.GetComponent<BoxCollider>(); trigger.isTrigger = true;
            trigger.center = Vector3.up; trigger.size = new Vector3(3.8f, 2.4f, 3.8f);
            GameObject visual;
            // 河流只作为环境保留，不绑定耗尽根节点；重复取水不能隐藏水面、河床或河岸碰撞。
            if (point.Repeatable) visual = null;
            else if (point.RequiredWeaponItemId != 0)
            {
                visual = content.GetComponentsInChildren<Transform>(true).Single(t => t.name == BoundResourceName(point)).gameObject;
                // 正式石块原为纯装饰；只给选中的场景实例补齐网格碰撞，不修改原 Prefab 或其他石块。
                if (point.RequiredWeaponItemId == 18)
                    foreach (var mesh in visual.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var collider = mesh.GetComponent<MeshCollider>();
                        // Unity 组件的空引用可能是托管占位对象，必须用 Unity 的相等判定再创建组件。
                        if (collider == null) collider = mesh.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = mesh.sharedMesh; collider.isTrigger = false; collider.enabled = true;
                    }
            }
            else
            {
                var child = node.Find("ResourceVisual");
                if (child == null)
                {
                    var prefabPath = point.Id == "camp.axe" ? "Assets/Game/Prefabs/Characters/Weapons/AxeShared.prefab"
                        : point.Id == "camp.pickaxe" ? "Assets/Game/Prefabs/Characters/Weapons/PickaxeShared.prefab"
                        : "Assets/Game/Art/Environment/CivilisationExpedition/Sources/RPGPP_LT/Prefabs/Props/Containers/rpgpp_lt_sack_01.prefab";
                    visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), node);
                    visual.name = "ResourceVisual";
                    foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                    foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
                    visual.transform.localPosition = Vector3.up * .15f;
                    if (point.Id == "camp.axe" || point.Id == "camp.pickaxe") visual.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
                else visual = child.gameObject;
            }
            var title = point.Repeatable ? "河岸取水（可重复采集）" : point.FellTree ? "可砍树木（需装备斧头）" : point.RequiredWeaponItemId == 18 ? "可采石块（需装备镐头）"
                : point.Id == "camp.axe" ? "拾取斧头" : point.Id == "camp.pickaxe" ? "拾取镐头" : "搜集布料";
            var prompt = node.Find("InteractionName")?.gameObject;
            if (prompt == null) { prompt = Object.Instantiate(promptTemplate, node, false); prompt.name = "InteractionName"; }
            prompt.SetActive(false); prompt.transform.localPosition = Vector3.up * 1.8f;
            ((RectTransform)prompt.transform).sizeDelta = new Vector2(460, 60);
            var text = prompt.GetComponentInChildren<TMP_Text>(true); text.text = title; text.fontSize = 24; text.raycastTarget = false;
            var localized = text.GetComponent<LocalizedText>() ?? text.gameObject.AddComponent<LocalizedText>();
            var loc = new SerializedObject(localized); loc.FindProperty("Key").stringValue = point.NameKey; loc.FindProperty("Texts").arraySize = 0; loc.ApplyModifiedPropertiesWithoutUndo();
            var data = new SerializedObject(view);
            data.FindProperty("kind").enumValueIndex = (int)CivilisationPointKind.Search;
            data.FindProperty("pointId").stringValue = point.Id; data.FindProperty("displayName").stringValue = title;
            data.FindProperty("nameRoot").objectReferenceValue = prompt; data.FindProperty("harvestedVisualRoot").objectReferenceValue = visual;
            data.ApplyModifiedPropertiesWithoutUndo(); node.gameObject.SetActive(true);
        }
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("河谷采集点保存失败");
        return "已接入4棵可砍树、4处采石点、3处河岸取水点、斧头、镐头及布料；河流与边界未重建。";
    }
    /// <summary>用配置中的稳定采集 ID 绑定已存在的独立资源，拒绝把未知目标挂到地图或建筑根节点。</summary>
    private static string BoundResourceName(CivilisationSearchPointDefinition point)
    {
        if (point.FellTree && point.Id.StartsWith("tree.valley.")) return "ValleyTree_" + point.Id.Substring("tree.valley.".Length);
        if (point.RequiredWeaponItemId == 18 && point.Id.StartsWith("rock.valley.")) return "ValleyRock_" + point.Id.Substring("rock.valley.".Length);
        throw new InvalidOperationException("未知工具采集目标：" + point.Id);
    }
}
