using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>奖励根的位置仍对应配置拾取点；这里只保存该次模型的纯视觉基线。</summary>
        private readonly List<TreasureRewardPose> treasureRewardPoses = new();
        /// <summary>本房间视觉时钟跨剩余物品快照保留，暂停时不推进。</summary>
        private float treasureVisualSeconds;
        /// <summary>全房共用一个有粒子上限的领取闪光实例，离房和离图立即释放。</summary>
        private ParticleSystem treasurePickupFlash;

        /// <summary>当前场地持有的单个奖励姿态，不携带资源句柄或玩法状态。</summary>
        private sealed class TreasureRewardPose
        {
            /// <summary>实例及其配置基点，动画不得写回规则位置。</summary>
            public Transform Transform;
            /// <summary>未叠加动画的本地坐标。</summary>
            public Vector3 Position;
            /// <summary>用于稳定视觉相位的配置奖励身份。</summary>
            public int Id;
        }

        /// <summary>只推进竖直轻浮与缓慢自转；水平坐标、入口、返回门和拾取半径不参与动画。</summary>
        private void TickTreasureVisuals(float deltaTime)
        {
            if (!contentInRoom || root == null || deltaTime <= 0f) return;
            treasureVisualSeconds += deltaTime;
            ApplyTreasureRewardPoses();
        }

        /// <summary>相位由奖励身份固定，拾走其他物品后的重绘不会跳回初始姿态。</summary>
        private void ApplyTreasureRewardPoses()
        {
            foreach (var pose in treasureRewardPoses)
            {
                if (pose.Transform == null) continue;
                float phase = pose.Id * 1.7f;
                pose.Transform.localPosition = pose.Position + Vector3.up * (.018f * Mathf.Sin(treasureVisualSeconds * 1.8f + phase));
                pose.Transform.localRotation = Quaternion.Euler(0, treasureVisualSeconds * 2f + pose.Id * 137.508f, 0);
            }
        }

        /// <summary>成功领取反馈才调用；不依据物品移除推断成功，避免到期、重连和队友领取误播。</summary>
        private void PlayTreasurePickupFlash()
        {
            if (TryGetPlayerPosition(out var position)) EmitTreasurePickupFlash(position);
        }

        /// <summary>正式模板直接引用粒子材质；一帧连续领取共用实例，上限由模板限制。</summary>
        private void EmitTreasurePickupFlash(Vector2 position)
        {
            if (!contentInRoom || root == null || contentTemplates == null) return;
            if (treasurePickupFlash == null)
            {
                var template = contentTemplates.Find("TreasurePickupFlash");
                if (template == null) return;
                treasurePickupFlash = Object.Instantiate(template.GetComponent<ParticleSystem>(), root.transform, false);
                treasurePickupFlash.name = "TreasurePickupFlash";
                treasurePickupFlash.gameObject.SetActive(true);
            }
            treasurePickupFlash.transform.localPosition = new Vector3(position.x, .65f, position.y);
            treasurePickupFlash.Emit(6);
        }

        /// <summary>房间返回、End 与资源加载失败均取消尚未结束的领取闪光。</summary>
        private void ClearTreasurePickupFlash()
        {
            if (treasurePickupFlash == null) return;
            treasurePickupFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            treasurePickupFlash.gameObject.SetActive(false);
            DestroyObject(treasurePickupFlash.gameObject);
            treasurePickupFlash = null;
        }
    }
}
