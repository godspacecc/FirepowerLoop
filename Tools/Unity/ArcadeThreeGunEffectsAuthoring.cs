using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>原位补三种短弹视觉子节点；保留既有载体、碰撞、材质、GUID及特殊模型。</summary>
public static class ArcadeThreeGunEffectsAuthoring
{
    public static string Run()
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅本工程编辑态允许制作");
        const string path = "Assets/Game/Prefabs/Combat/GrayboxBullet.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = new SerializedObject(root.GetComponent<Game.ProjectileView>());
            var bindings = view.FindProperty("arcadeVisuals");
            var source = root.GetComponent<MeshRenderer>();
            var names = new[] { "ArcadeBulletBasic", "ArcadeBulletRapid", "ArcadeBulletSpread" };
            var scales = new[] { new Vector3(.9f, .9f, .8f), new Vector3(.65f, .65f, 1.05f), new Vector3(.72f, .72f, .48f) };
            for (int i = 0; i < names.Length; i++)
            {
                var existing = root.transform.Find(names[i]);
                GameObject child = existing != null ? existing.gameObject : new GameObject(names[i]);
                child.transform.SetParent(root.transform, false); child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity; child.transform.localScale = scales[i];
                var filter = child.GetComponent<MeshFilter>();
                if (filter == null) filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = root.GetComponent<MeshFilter>().sharedMesh;
                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false; child.SetActive(false);
                int index = -1;
                for (int j = 0; j < bindings.arraySize; j++)
                    if (bindings.GetArrayElementAtIndex(j).FindPropertyRelative("resourceName").stringValue == names[i]) index = j;
                if (index < 0) { index = bindings.arraySize; bindings.InsertArrayElementAtIndex(index); }
                var binding = bindings.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("resourceName").stringValue = names[i];
                binding.FindPropertyRelative("visualRoot").objectReferenceValue = child;
            }
            view.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "三种短弹变体已原位保存；未修改碰撞或默认弹体";
    }
}
