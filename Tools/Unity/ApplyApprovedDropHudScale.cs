using System;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>持久化用户已采用的v1尺寸；只更新目标Prefab，不制作新模型或改变玩法。</summary>
public static class ApplyApprovedDropHudScale
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本项目编辑态接入已采用资源");
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var coin = Pickup("ArcadeDropCoin");
            float target = Longest(coin.transform.Find("ArcadeDropVisual"));
            foreach (var name in new[] { "ArcadeDropSkullRed", "ArcadeDropSkullPurple" })
            {
                var sample = Pickup(name);
                var visual = sample.transform.Find("ArcadeDropVisual");
                SaveScale(name, visual.localScale * (target / Longest(visual)));
            }
            // 保留采用预览的原始精度，各轴乘70%；绝对值写入，重复执行不会继续缩小。
            SaveScale("ArcadeTimedDroneVisual", Vector3.one * (.60283685f * .7f));
            const string path = "Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var serialized = new SerializedObject(root.GetComponent<ResourcePickupView>());
                var poses = serialized.FindProperty("weaponDeliveryPoses");
                SerializedProperty drone = null;
                for (int index = 0; index < poses.arraySize; index++)
                {
                    var pose = poses.GetArrayElementAtIndex(index);
                    if (pose.FindPropertyRelative("prefabAddress").stringValue == "Prefabs/SceneObjects/ArcadeSupportDroneVisual")
                        drone = pose;
                }
                if (drone == null) { poses.arraySize++; drone = poses.GetArrayElementAtIndex(poses.arraySize - 1); }
                drone.FindPropertyRelative("prefabAddress").stringValue = "Prefabs/SceneObjects/ArcadeSupportDroneVisual";
                drone.FindPropertyRelative("localPosition").vector3Value = Vector3.zero;
                drone.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;
                drone.FindPropertyRelative("maxSize").floatValue = .85f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return "已持久化采用版：红紫掉落实体最长边=" + target.ToString("F6") +
                "米；随身无人机0.595米；空投专用尺寸0.85米；其它武器保留默认1.7米";
        }
        finally { SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true); }
    }

    /// <summary>使用正式拾取根的真实缩放，尺寸依据与已采用预览保持一致。</summary>
    private static GameObject Pickup(string name)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"));
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/" + name + ".prefab");
        root.GetComponent<ResourcePickupView>().SetArcadePresentation(source, "Prefabs/SceneObjects/" + name);
        root.transform.rotation = Quaternion.Euler(0, 180, 0);
        return root;
    }
    private static float Longest(Transform root)
    {
        var meshes = root.GetComponentsInChildren<MeshRenderer>(true);
        var bounds = meshes.First().bounds;
        foreach (var mesh in meshes.Skip(1)) bounds.Encapsulate(mesh.bounds);
        return Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
    }
    private static void SaveScale(string name, Vector3 scale)
    {
        string path = "Assets/Game/Prefabs/SceneObjects/" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { root.transform.localScale = scale; PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
