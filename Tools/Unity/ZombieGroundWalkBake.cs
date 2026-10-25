using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>只烘焙用户已采用v2的三种地面步态；复用预览采样，不重做角色或其它动画。</summary>
public static class ZombieGroundWalkBake
{
    /// <summary>采用片段一个完整左右步周期，参考位移速度只影响Run状态的播放倍率。</summary>
    private const float Duration = .82f, ReferenceSpeed = .9f;
    private const int Samples = 82;
    private const string Art = "Assets/Game/Art/Enemies/Zombies/Civilian/";

    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许本工程空闲编辑态烘焙已采用步态");
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var lines = new List<string>();
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach (var kind in new[] { "Shambler", "Gaunt", "Bloated" })
            {
                var actor = ZombieGroundWalkPreview.Create(kind, true);
                actor.Root.SetActive(true);
                try
                {
                    var result = BakeClip(actor, kind);
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Game/Art/Enemies/Zombies/ArcadeZombie" + kind + ".controller");
                    var run = controller.layers[0].stateMachine.states.Single(s => s.state.name == "Run").state;
                    run.motion = result;
                    run.speed = 1 / ReferenceSpeed;
                    run.speedParameter = "MoveSpeed";
                    run.speedParameterActive = true;
                    EditorUtility.SetDirty(run); EditorUtility.SetDirty(controller);
                    AssetDatabase.SaveAssetIfDirty(controller);
                    lines.Add(kind + " -> " + AssetDatabase.GetAssetPath(result) + "; cycle=" + result.length + "; speed=MoveSpeed/0.9");
                }
                finally { Object.DestroyImmediate(actor.Root); }
            }
        }
        finally
        {
            SceneManager.SetActiveScene(prior);
            EditorSceneManager.CloseScene(scene, true);
        }
        return string.Join("\n", lines);
    }

    /// <summary>原片段上身曲线按周期重排，只覆盖采用版腿部旋转及骨盆位移；运行时无需IK。</summary>
    private static AnimationClip BakeClip(ZombieGroundWalkPreview.Actor actor, string kind)
    {
        var clip = Object.Instantiate(actor.Clip);
        clip.name = "Civilian|GroundWalk" + kind;
        clip.frameRate = 100;
        float timeRatio = Duration / actor.Clip.length;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].time *= timeRatio;
                keys[i].inTangent /= timeRatio;
                keys[i].outTangent /= timeRatio;
            }
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        var legs = actor.Upper.Concat(actor.Knee).Concat(actor.Foot).Distinct().ToArray();
        var rotationCurves = legs.Select(_ => Enumerable.Range(0, 4).Select(i => new AnimationCurve()).ToArray()).ToArray();
        var positionCurves = Enumerable.Range(0, 3).Select(i => new AnimationCurve()).ToArray();
        for (int sample = 0; sample <= Samples; sample++)
        {
            float time = Duration * sample / Samples;
            ZombieGroundWalkPreview.Pose(actor, time, null);
            for (int i = 0; i < legs.Length; i++)
            {
                var q = legs[i].localRotation;
                for (int axis = 0; axis < 4; axis++) rotationCurves[i][axis].AddKey(time, q[axis]);
            }
            for (int axis = 0; axis < 3; axis++) positionCurves[axis].AddKey(time, actor.Hips.localPosition[axis]);
        }
        for (int i = 0; i < legs.Length; i++)
            for (int axis = 0; axis < 4; axis++)
                SetLinearCurve(clip, AnimationUtility.CalculateTransformPath(legs[i], actor.Animator.transform), "m_LocalRotation." + "xyzw"[axis], rotationCurves[i][axis]);
        for (int axis = 0; axis < 3; axis++)
            SetLinearCurve(clip, AnimationUtility.CalculateTransformPath(actor.Hips, actor.Animator.transform), "m_LocalPosition." + "xyz"[axis], positionCurves[axis]);
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0; settings.stopTime = Duration; settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        string path = Art + kind + "GroundWalk.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null) { AssetDatabase.CreateAsset(clip, path); existing = clip; }
        else { EditorUtility.CopySerialized(clip, existing); Object.DestroyImmediate(clip); }
        EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }

    /// <summary>采样间线性插值避免切线过冲使脚掌穿地或抬脚超出采用幅度。</summary>
    private static void SetLinearCurve(AnimationClip clip, string path, string property, AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        clip.SetCurve(path, typeof(Transform), property, curve);
    }
}
