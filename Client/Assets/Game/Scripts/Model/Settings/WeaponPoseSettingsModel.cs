using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>本机武器表现调整的稳定数据；不包含资源引用、装备状态或战斗属性。</summary>
    [Serializable]
    public sealed class WeaponPoseSettingsData
    {
        public int version = 1;
        public List<WeaponPoseSetting> poses = new List<WeaponPoseSetting>();
        public WeaponPoseSettingsData Clone() => JsonUtility.FromJson<WeaponPoseSettingsData>(JsonUtility.ToJson(this));
    }

    /// <summary>一个武器和动作的局部姿态覆盖。空 animationId 表示武器默认姿态。</summary>
    [Serializable]
    public sealed class WeaponPoseSetting
    {
        public string weaponId;
        /// <summary>动作定义资产名是持久化身份，重命名资产前需迁移本机配置。</summary>
        public string animationId = string.Empty;
        public bool positionEnabled;
        public Vector3 position;
        public bool rotationEnabled;
        /// <summary>相对主手挂点的欧拉角，单位为度。</summary>
        public Vector3 eulerAngles;
        public bool scaleEnabled;
        public Vector3 scale = Vector3.one;
        public void Apply(ref Vector3 p, ref Quaternion r, ref Vector3 s)
        {
            if (positionEnabled) p = position;
            if (rotationEnabled) r = Quaternion.Euler(eulerAngles);
            if (scaleEnabled) s = scale;
        }
    }

    /// <summary>保存本机会话的姿态状态和未保存标记；校验、修改和文件事务由 System 负责。</summary>
    public sealed class WeaponPoseSettingsModel : AbstractModel
    {
        internal WeaponPoseSettingsData Data = new WeaponPoseSettingsData();
        public bool HasUnsavedChanges { get; internal set; }
        public int Revision { get; internal set; }
        protected override void OnInit() { }
    }
}
