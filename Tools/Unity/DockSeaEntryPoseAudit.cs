using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>采用版骨架与正式片段的同坐标回读；只在独立临时场景采样，不修改正式资源。</summary>
public static class DockSeaEntryPoseAudit
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("仅允许空闲编辑态回读");
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Material foam = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            foam = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var rows = new List<string>(); float maximum = 0;
            foreach (var kind in new[] { "Shambler", "Gaunt", "Bloated" })
            {
                var adopted = DockSeaEntryPreview.Enemy(-3, 0, foam, kind);
                var formal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie" + kind + ".prefab"));
                var view = formal.GetComponent<EnemyView>(); view.BeginPresentationUse(); view.SetNavigationEnabled(false);
                var names = new[] { "Hips", "UpperArm.L", "LowerLeg.L", "Foot.L", "Foot.R" };
                var original = adopted.Root.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name)).ToDictionary(t => t.name);
                var applied = formal.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name)).ToDictionary(t => t.name);
                foreach (float age in new[] { 0f, 2.5f, 3.4f, 4.1f, 5f, 5.6f })
                {
                    DockSeaEntryPreview.Pose(adopted, age);
                    var pose = ArcadeDockSeaEntryRules.Sample(age);
                    formal.transform.SetPositionAndRotation(new Vector3(pose.X, 0, -3), Quaternion.LookRotation(Vector3.left));
                    view.SetDockSeaEntry(new ArcadeDockSeaEntryState { Started = true, Age = age, Lane = -3 }, Vector3.left, Vector3.up);
                    view.LateTickCombatFeedback();
                    float error = names.Max(n => Vector3.Distance(original[n].position, applied[n].position));
                    maximum = Mathf.Max(maximum, error);
                    if (error > .01f) throw new InvalidOperationException(kind + "@" + age + " 与采用版关节偏差=" + error);
                    rows.Add(FormattableString.Invariant($"{kind}|{age:F3}|maxJointErrorMetres={error:F6}"));
                }
                Object.DestroyImmediate(adopted.Root); Object.DestroyImmediate(formal);
            }
            rows.Add("maximumJointErrorMetres=" + maximum.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join("\n", rows);
        }
        finally
        {
            if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (foam != null) Object.DestroyImmediate(foam);
        }
    }
}
