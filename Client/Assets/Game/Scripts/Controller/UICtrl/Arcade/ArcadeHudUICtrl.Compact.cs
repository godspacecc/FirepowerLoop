using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>当前权威快照的有效效果；只保留显示数据，不推进计时。</summary>
        private readonly List<EffectDisplay> effects = new();
        /// <summary>本帧临时武器占据的条目数（零或一），辅助列表索引从其后开始。</summary>
        private int weaponEffectCount;

        /// <summary>仅切换 Prefab 内预制的两端能力区；联机保留原二次确认退出入口。</summary>
        private void ApplyInputLayout(bool mobile, bool online)
        {
            UI.DesktopAbilityRoot.SetActive(!mobile);
            UI.BtnBomb.gameObject.SetActive(mobile);
            UI.BtnDash.gameObject.SetActive(mobile);
            UI.BtnPause.gameObject.SetActive(mobile || online);
            // 联机入口原本用于退出，不能以单机暂停图标误示会冻结服务器时间。
            UI.PauseIconRoot.SetActive(!online);
            UI.TextPauseOnlineExit.gameObject.SetActive(online);
            UI.TextPauseOnlineExit.text = Loc.T("ui.arcade.hud.online_exit");
        }

        /// <summary>能力次数只读取现有库存；触屏零次数禁用沿用原按钮裁决。</summary>
        private void RenderAbilityCounts(int bombs, int dashes)
        {
            Set(UI.TextMobileBombCount, bombs.ToString());
            SetButton(UI.BtnDash, dashes.ToString());
            Set(UI.TextBombCount, bombs.ToString());
            Set(UI.TextDashCount, dashes.ToString());
            UI.DesktopBombIcon.color = bombs > 0 ? Color.white : new Color(.45f, .45f, .45f);
            UI.DesktopDashIcon.color = dashes > 0 ? Color.white : new Color(.45f, .45f, .45f);
            UI.DesktopDashIcon.text = Loc.T("ui.arcade.hud.icon.dash");
        }

        /// <summary>剩余时间来自单人规则快照或联机服务器；分母读取双方共用的正式掉落持续时间，不另起 UI 计时器。</summary>
        private void RenderTimedStatus(string temporaryNameKey, float temporarySeconds,
            IReadOnlyList<ArcadePowerupHudEntry> powerups)
        {
            effects.Clear();
            AddEffect(temporaryNameKey, temporarySeconds, 0, true);
            weaponEffectCount = effects.Count;
            if (powerups != null)
                foreach (var powerup in powerups) AddEffect(powerup.NameKey, powerup.Seconds, powerup.Charges, false);
            UI.WeaponEffectList.totalCount = weaponEffectCount;
            UI.WeaponEffectList.RefreshCells();
            UI.WeaponEffectRoot.SetActive(weaponEffectCount > 0);
            UI.EffectList.totalCount = effects.Count - weaponEffectCount;
            UI.EffectList.RefreshCells();
            UI.SupportEffectRoot.SetActive(UI.EffectList.totalCount > 0);
        }

        private void AddEffect(string nameKey, float seconds, int charges, bool weapon)
        {
            if (string.IsNullOrWhiteSpace(nameKey) || seconds <= 0f) return;
            const string prefix = "ui.arcade.product.";
            const string suffix = ".name";
            string id = nameKey.StartsWith(prefix, StringComparison.Ordinal) && nameKey.EndsWith(suffix, StringComparison.Ordinal)
                ? nameKey.Substring(prefix.Length, nameKey.Length - prefix.Length - suffix.Length) : nameKey;
            var drop = hudCatalog.Drops.FirstOrDefault(value => value.ProductId == id &&
                value.Kind == (weapon ? global::ArcadeDropKind.TemporaryWeapon : global::ArcadeDropKind.Powerup));
            float duration = drop.DurationSeconds;
            // 缺配置时仍显示有效效果的可辨识占位；不猜造总时长或隐藏状态。
            effects.Add(new EffectDisplay(id, seconds, duration, charges, weapon));
        }

        /// <summary>复用条目时完整覆盖图像、占位、次数、颜色与进度，避免刷新或替换后残留旧类型。</summary>
        private void RefreshEffect(Transform transform, int index)
            => RefreshEffectBox(transform, index + weaponEffectCount);

        private void RefreshWeaponEffect(Transform transform, int index) => RefreshEffectBox(transform, index);

        private void RefreshEffectBox(Transform transform, int index)
        {
            var box = transform.GetComponent<ArcadeEffectBoxUI>();
            var effect = effects[index];
            Sprite icon = effect.Id switch
            {
                "arcade.weapon.rapid" => UI.RapidIcon,
                "arcade.weapon.spread" => UI.SpreadIcon,
                "arcade.weapon.piercing" => UI.PiercingIcon,
                "arcade.weapon.rocket" => UI.RocketIcon,
                "arcade.weapon.grenade" => UI.GrenadeIcon,
                "arcade.weapon.raygun" => UI.RaygunIcon,
                "arcade.powerup.boots" => UI.BootsIcon,
                "arcade.powerup.gloves" => UI.GlovesIcon,
                _ => null
            };
            box.Icon.sprite = icon;
            box.Icon.gameObject.SetActive(icon != null);
            box.TextFallback.gameObject.SetActive(icon == null);
            string shortId = effect.Id.Substring(effect.Id.LastIndexOf('.') + 1);
            box.TextFallback.text = icon == null ? Loc.T("ui.arcade.hud.icon." + shortId) : string.Empty;
            // 未知新类型仍显示拾取名称的前两个字符，避免泄漏内部 key 或静默隐藏。
            if (box.TextFallback.text.StartsWith("ui.", StringComparison.Ordinal))
            {
                string name = Loc.T(ProductNameKey(effect.Id));
                box.TextFallback.text = name.StartsWith("ui.", StringComparison.Ordinal) ? "?" : name.Substring(0, Math.Min(2, name.Length));
            }
            box.TextCharges.text = effect.Charges > 0 ? effect.Charges.ToString() : string.Empty;
            box.TextCharges.gameObject.SetActive(effect.Charges > 0);
            HealthFillUtility.Apply(box.ProgressFill, effect.Duration > 0f ? Mathf.Clamp01(effect.Seconds / effect.Duration) : 1f);
            box.ProgressFill.color = effect.Seconds <= 3f ? new Color(1f, .36f, .22f) : effect.Weapon
                ? new Color(1f, .78f, .28f) : new Color(.3f, .85f, .92f);
            box.Icon.color = effect.Seconds <= 3f ? new Color(1f, .65f, .45f) : Color.white;
            box.TextFallback.color = box.Icon.color;
        }

        private void ClearTimedEffects()
        {
            effects.Clear();
            weaponEffectCount = 0;
            UI.WeaponEffectList.ClearCells();
            UI.EffectList.ClearCells();
            UI.WeaponEffectRoot.SetActive(false);
            UI.SupportEffectRoot.SetActive(false);
        }

        /// <summary>单帧效果展示值；有效期及消耗始终由原规则层管理。</summary>
        private readonly struct EffectDisplay
        {
            public EffectDisplay(string id, float seconds, float duration, int charges, bool weapon)
            { Id = id; Seconds = seconds; Duration = duration; Charges = charges; Weapon = weapon; }
            public string Id { get; }
            public float Seconds { get; }
            public float Duration { get; }
            public int Charges { get; }
            public bool Weapon { get; }
        }
    }
}

