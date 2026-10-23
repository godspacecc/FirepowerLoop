using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>诊断正式角色动画求值前后与火焰发射方向；只创建临时实例，不保存资源或启动对局。</summary>
public static class FlamethrowerFlowProbe
{
    /// <summary>对比已校正、下一帧动画求值后、再次校正后的方向；发射顺序仅为隔离探针，不冒充自然PlayerLoop。</summary>
    public static string Run()
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态执行隔离诊断");
        var scene = EditorSceneManager.NewPreviewScene();
        var report = new List<string>();
        try
        {
            report.Add("隔离正式资源采样；不表示实际PlayerLoop已复现侧喷。");
            report.Add("此探针只读取正式角色目录及粒子资源，不创建玩法或配置替代值。");
            foreach (var movement in new[] { Vector3.zero, Vector3.forward, Vector3.right })
            {
                var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
                SceneManager.MoveGameObjectToScene(actor, scene);
                try
                {
                    var presenter = actor.GetComponent<CharacterWeaponPresenter>();
                    actor.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);
                    if (!presenter.SetWeaponId("arcade.weapon.flamethrower")) throw new Exception(presenter.LastError);
                    var animator = presenter.BodyAnimator;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind(); animator.Update(.05f);
                    presenter.SetMovementDirection(movement);
                    animator.SetFloat("MoveSpeed", movement == Vector3.zero ? 0 : 1);
                    var flame = presenter.Visual.GetComponentInChildren<ParticleSystem>();
                    if (flame == null) throw new Exception("正式喷嘴缺少粒子系统");
                    var main = flame.main;
                    report.Add($"movement={movement},近端速度={main.startSpeed.constantMin}..{main.startSpeed.constantMax},空间={main.simulationSpace},继承移动={flame.inheritVelocity.enabled}");
                    for (int frame = 0; frame < 24; frame++)
                    {
                        presenter.TickUpperBodyGunFire(1f / 60);
                        if (frame % 6 == 0) presenter.PlayUpperBodyGunFire();
                        presenter.ApplyOffhandGrip();
                        float before = Angle(flame.transform.forward, actor.transform);
                        animator.Update(1f / 60);
                        float animated = Angle(flame.transform.forward, actor.transform);
                        // 只发射一个确定粒子，记录此时真正写入的世界速度，而非仅测枪模。
                        flame.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        flame.Emit(1);
                        var particles = new ParticleSystem.Particle[2];
                        int count = flame.GetParticles(particles);
                        float velocity = count == 1 ? Angle(particles[0].velocity, actor.transform) : float.NaN;
                        presenter.ApplyOffhandGrip();
                        float corrected = Angle(flame.transform.forward, actor.transform);
                        report.Add($"frame={frame},校正后={before:F3},动画求值后={animated:F3},此时发射速度角={velocity:F3},再次校正后={corrected:F3}");
                    }
                }
                finally { Object.DestroyImmediate(actor); }
            }
            string folder = Path.GetFullPath("Temp/FlamethrowerFlowProbe");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "probe.txt");
            File.WriteAllLines(path, report);
            return path + "\n" + string.Join("\n", report);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>水平角度用于区分枪模校正与粒子速度，不将动画俯仰计为侧喷。</summary>
    private static float Angle(Vector3 direction, Transform actor)
        => Vector3.SignedAngle(actor.forward, Vector3.ProjectOnPlane(direction, actor.up), actor.up);
}
