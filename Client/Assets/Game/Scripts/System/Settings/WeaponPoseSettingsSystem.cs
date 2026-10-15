using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    public enum WeaponPosePart { Position, Rotation, Scale }

    /// <summary>本机姿态调整的唯一写入口。处理校验、覆盖优先级和保存；不修改目录资产或装备规则。</summary>
    public sealed class WeaponPoseSettingsSystem : AbstractSystem
    {
        private WeaponPoseSettingsModel model;
        private IWeaponPoseFileStore store;
        // 撤销仅保存本机会话数据，最多 32 步；长按期间的重复写入共用一次快照。
        private readonly List<WeaponPoseSettingsData> undo = new List<WeaponPoseSettingsData>();
        private bool editGroup;
        private bool groupCaptured;
        private string savedJson;
        public bool CanUndo => undo.Count > 0;
        public bool HasUnsavedChanges => model.HasUnsavedChanges;
        public int Revision => model.Revision;
        public string FilePath => store.FilePath;
        public string LoadWarning { get; private set; }
        public WeaponPoseSettingsSystem() { }
        /// <summary>独立测试通过显式依赖创建，不启动完整游戏架构。</summary>
        public WeaponPoseSettingsSystem(WeaponPoseSettingsModel state, IWeaponPoseFileStore files)
        { model = state; store = files; Load(); }
        protected override void OnInit()
        {
            model = this.GetModel<WeaponPoseSettingsModel>();
            store = this.GetUtility<IWeaponPoseFileStore>();
            Load();
        }
        private void Load()
        {
            model.Data = store.Load(out var warning);
            LoadWarning = warning;
            model.HasUnsavedChanges = false;
            model.Revision++;
            savedJson = Export();
            undo.Clear();
        }

        /// <summary>资源默认 → 本机默认 → 资源动作覆盖 → 本机动作覆盖，未启用分量继续继承。</summary>
        public void Resolve(CharacterWeaponCatalog.Entry weapon, CharacterAnimationDefinition animation,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = weapon.HandLocalPosition; rotation = weapon.HandLocalRotation; scale = weapon.HandLocalScale;
            Find(weapon.Id, string.Empty)?.Apply(ref position, ref rotation, ref scale);
            if (animation == null) return;
            if (weapon.AnimationPoseOverrides != null)
                foreach (var pose in weapon.AnimationPoseOverrides)
                    if (pose?.Definition == animation)
                    {
                        if (pose.OverridePosition) position = pose.LocalPosition;
                        if (pose.OverrideRotation) rotation = pose.LocalRotation;
                        if (pose.OverrideScale) scale = pose.LocalScale;
                        break;
                    }
            Find(weapon.Id, animation.name)?.Apply(ref position, ref rotation, ref scale);
        }

        /// <summary>只修改一个分量组，实时生效但不自动落盘；失败保持旧状态。</summary>
        public bool Set(CharacterWeaponCatalog.Entry weapon, CharacterAnimationDefinition animation,
            WeaponPosePart part, Vector3 value, out string error)
        {
            error = null;
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.Id) || !ValidVector(value) ||
                part == WeaponPosePart.Scale && (value.x <= 0 || value.y <= 0 || value.z <= 0) ||
                !Enum.IsDefined(typeof(WeaponPosePart), part))
            { error = "请输入有限数值，缩放的三个分量都必须大于 0。"; return false; }
            CaptureUndo();
            var id = animation == null ? string.Empty : animation.name;
            var pose = Find(weapon.Id, id);
            if (pose == null)
            {
                pose = new WeaponPoseSetting { weaponId = weapon.Id, animationId = id };
                model.Data.poses.Add(pose);
            }
            switch (part)
            {
                case WeaponPosePart.Position: pose.positionEnabled = true; pose.position = value; break;
                case WeaponPosePart.Rotation: pose.rotationEnabled = true; pose.eulerAngles = value; break;
                case WeaponPosePart.Scale: pose.scaleEnabled = true; pose.scale = value; break;
            }
            Changed(); return true;
        }

        /// <summary>按武器当前局部轴叠加角度，不增减欧拉角分量；剑身滚转不会改变长轴方向。</summary>
        public bool RotateLocal(CharacterWeaponCatalog.Entry weapon, CharacterAnimationDefinition animation,
            int axis, float degrees, out string error)
        {
            error = null;
            if (weapon == null || axis < 0 || axis > 2 || !ValidVector(new Vector3(degrees, 0, 0)))
            { error = "旋转轴或角度无效。"; return false; }
            Resolve(weapon, animation, out _, out var rotation, out _);
            var direction = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            // 右乘将增量作用在武器自身坐标系；文件继续保存旧版欧拉角，兼容已有配置。
            return Set(weapon, animation, WeaponPosePart.Rotation,
                (rotation * Quaternion.AngleAxis(degrees, direction)).eulerAngles, out error);
        }

        /// <summary>一次提交完整的相对手握持参数；先校验所有分量，失败不留下部分覆盖。</summary>
        public bool SetPose(CharacterWeaponCatalog.Entry weapon, CharacterAnimationDefinition animation,
            Vector3 position, Quaternion rotation, Vector3 scale, out string error)
        {
            error = null;
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.Id) || !ValidVector(position) ||
                !ValidVector(scale) || scale.x <= 0 || scale.y <= 0 || scale.z <= 0 ||
                !ValidVector(new Vector3(rotation.x, rotation.y, rotation.z)) ||
                float.IsNaN(rotation.w) || float.IsInfinity(rotation.w) ||
                rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w < .000001f)
            { error = "握持参数无效，未应用复制。"; return false; }
            var id = animation == null ? string.Empty : animation.name;
            CaptureUndo();
            var pose = Find(weapon.Id, id);
            if (pose == null)
            {
                pose = new WeaponPoseSetting { weaponId = weapon.Id, animationId = id };
                model.Data.poses.Add(pose);
            }
            pose.positionEnabled = pose.rotationEnabled = pose.scaleEnabled = true;
            pose.position = position; pose.eulerAngles = rotation.normalized.eulerAngles; pose.scale = scale;
            Changed(); return true;
        }

        /// <summary>输入按下时开启；没有实际写入时不产生空撤销步骤。</summary>
        public void BeginEditGroup() { EndEditGroup(); editGroup = true; }
        /// <summary>松开、切换目标或失去焦点时关闭，后续操作不归入旧长按。</summary>
        public void EndEditGroup() { editGroup = false; groupCaptured = false; }
        /// <summary>恢复上一完整快照，包括未启用的覆盖标志；不把继承姿态冻结为显式覆盖。</summary>
        public bool Undo()
        {
            EndEditGroup();
            if (!CanUndo) return false;
            model.Data = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); Changed(); return true;
        }
        private void CaptureUndo()
        {
            if (editGroup && groupCaptured) return;
            if (undo.Count == 32) undo.RemoveAt(0);
            undo.Add(model.Data.Clone());
            if (editGroup) groupCaptured = true;
        }

        /// <summary>只清除当前参数组的本机覆盖，其余参数及动作保持原样。</summary>
        public void ResetPart(string weaponId, CharacterAnimationDefinition animation, WeaponPosePart part)
        {
            var pose = Find(weaponId, animation == null ? string.Empty : animation.name);
            if (pose == null || !Enum.IsDefined(typeof(WeaponPosePart), part)) return;
            CaptureUndo();
            if (part == WeaponPosePart.Position) pose.positionEnabled = false;
            else if (part == WeaponPosePart.Rotation) pose.rotationEnabled = false;
            else pose.scaleEnabled = false;
            if (!pose.positionEnabled && !pose.rotationEnabled && !pose.scaleEnabled) model.Data.poses.Remove(pose);
            Changed();
        }

        /// <summary>仅删除所选作用域的本机覆盖，恢复该作用域原来的继承关系。</summary>
        public void Reset(string weaponId, CharacterAnimationDefinition animation)
        {
            var id = animation == null ? string.Empty : animation.name;
            var pose = Find(weaponId, id);
            if (pose == null) return;
            CaptureUndo(); model.Data.poses.Remove(pose); Changed();
        }
        public bool Save(out string error)
        {
            if (!store.Save(model.Data.Clone(), out error)) return false;
            savedJson = Export(); model.HasUnsavedChanges = false; return true;
        }
        public string Export() => JsonUtility.ToJson(model.Data, true);

        /// <summary>导入整份本机配置。完整验证成功才替换会话，仍需点击保存才落盘。</summary>
        public bool Import(string json, CharacterWeaponCatalog catalog, out string error)
        {
            if (!TryParse(json, out var candidate, out error)) return false;
            if (catalog == null) { error = "武器目录未加载。"; return false; }
            foreach (var pose in candidate.poses)
            {
                var weapon = catalog.Find(pose.weaponId);
                if (weapon == null || pose.animationId.Length > 0 && weapon.FindAnimation(pose.animationId) == null)
                { error = "配置中的武器或动画不存在：" + pose.weaponId + "/" + pose.animationId; return false; }
            }
            EndEditGroup(); CaptureUndo(); model.Data = candidate; Changed(); return true;
        }
        private WeaponPoseSetting Find(string weapon, string animation)
        {
            // 正式动画每帧读取，避免捕获参数的谓词在大量角色上产生临时分配。
            foreach (var pose in model.Data.poses)
                if (pose.weaponId == weapon && pose.animationId == animation) return pose;
            return null;
        }
        private void Changed() { model.HasUnsavedChanges = Export() != savedJson; model.Revision++; }
        private static bool ValidVector(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        /// <summary>版本与条目必须显式存在，避免空对象被反序列化为默认配置后误清除调整。</summary>
        public static bool TryParse(string json, out WeaponPoseSettingsData data, out string error)
        {
            data = new WeaponPoseSettingsData { version = 0, poses = null };
            try
            {
                if (string.IsNullOrWhiteSpace(json)) { error = "JSON 为空。"; return false; }
                JsonUtility.FromJsonOverwrite(json, data);
                return Validate(data, out error);
            }
            catch (Exception exception) { error = "JSON 无效：" + exception.Message; return false; }
        }

        /// <summary>文件和导入共用结构校验，拒绝重复身份、未来版本和非有限姿态。</summary>
        public static bool Validate(WeaponPoseSettingsData data, out string error)
        {
            error = null;
            if (data == null || data.version != 1 || data.poses == null || data.poses.Count > 4096)
            { error = "姿态配置版本或内容无效。"; return false; }
            var keys = new HashSet<(string Weapon, string Animation)>();
            foreach (var p in data.poses)
                if (p == null || string.IsNullOrWhiteSpace(p.weaponId) || p.animationId == null ||
                    !keys.Add((p.weaponId, p.animationId)) ||
                    !ValidVector(p.position) || !ValidVector(p.eulerAngles) || !ValidVector(p.scale) ||
                    p.scaleEnabled && (p.scale.x <= 0 || p.scale.y <= 0 || p.scale.z <= 0))
                { error = "姿态配置包含重复身份或无效数值。"; return false; }
            return true;
        }
    }
}
