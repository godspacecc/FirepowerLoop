using System;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 敌人可试装的外观来源；只描述网格、材质和挂点姿态，不决定敌人装备、攻击或伤害。
    /// 玩家武器直接读取正式 PlayerView 的外观绑定，避免在造型库维护第二份模型清单。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Enemy Weapon Visual Catalog")]
    public sealed class EnemyWeaponVisualCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class MeleeVisual
        {
            [SerializeField] private string displayName;
            [SerializeField] private Mesh mesh;
            [SerializeField] private Material material;
            [SerializeField] private Vector3 handLocalEulerAngles;
            [SerializeField] private float scaleMultiplier = 1f;

            public string DisplayName => displayName;
            public Mesh Mesh => mesh;
            public Material Material => material;
            public Vector3 HandLocalEulerAngles => handLocalEulerAngles;
            public float ScaleMultiplier => scaleMultiplier;
            public bool IsValid => mesh != null && material != null && !string.IsNullOrWhiteSpace(displayName);
        }

        [SerializeField] private MeleeVisual[] meleeVisuals = Array.Empty<MeleeVisual>();
        [SerializeField] private PlayerView playerWeaponSource;

        public int MeleeCount => meleeVisuals?.Length ?? 0;
        public int PlayerWeaponCount => playerWeaponSource?.ArcadeWeaponVisualCount ?? 0;
        public PlayerView PlayerWeaponSource => playerWeaponSource;

        public MeleeVisual GetMelee(int index) =>
            index >= 0 && index < MeleeCount ? meleeVisuals[index] : null;

        public string GetPlayerWeaponName(int index) =>
            playerWeaponSource?.GetArcadeWeaponVisualName(index);

        public GameObject GetPlayerWeaponVisual(int index) =>
            playerWeaponSource?.GetArcadeWeaponVisualRoot(index);

        /// <summary>读取玩家正式挂点绑定手骨后的姿态，不使用尚未绑定的 Prefab 世界坐标。</summary>
        public bool TryGetPlayerWeaponHandPose(int index, out Vector3 position, out Quaternion rotation,
            out Vector3 scale)
        {
            position = default;
            rotation = Quaternion.identity;
            scale = Vector3.one;
            return playerWeaponSource != null &&
                   playerWeaponSource.TryGetArcadeWeaponHandPose(index, out position, out rotation, out scale);
        }

        public Transform GetPlayerRightHand()
        {
            if (playerWeaponSource == null) return null;
            foreach (var child in playerWeaponSource.GetComponentsInChildren<Transform>(true))
                if (child.name == "Hand_R") return child;
            return null;
        }
    }
}
