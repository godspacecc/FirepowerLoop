using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>当前权威快照的有效效果；只保留显示数据，不推进计时。</summary>
        private readonly List<EffectDisplay> effects = new();
        /// <summary>本帧临时武器占据的条目数（零或一），辅助列表索引从其后开始。</summary>
        private int weaponEffectCount;
        /// <summary>只缓存末三秒的图标引用，条目仍由 CustomListView 创建、复用和回收。</summary>
        private readonly List<Image> skullWarningIcons=new();
        /// <summary>输入模式只切换 Prefab 内两组展示；权威快照与已保存触屏位置不随之改变。</summary>
        private bool mobileHudLayout;

        /// <summary>仅切换 Prefab 内预制的两端能力区；联机保留原二次确认退出入口。</summary>
        private void ApplyInputLayout(bool mobile, bool online)
        {
            mobileHudLayout = mobile;
            UI.MobileInformationRoot.SetActive(mobile);
            UI.DesktopInformationRoot.SetActive(!mobile);
            UI.PlayerStatusList.gameObject.SetActive(!mobile);
            UI.DesktopAbilityRoot.SetActive(false);
            UI.DesktopWaveStatus.SetActive(!mobile);
            UI.MobileWaveStatus.SetActive(mobile);
            SyncDesktopStage();
            UI.BtnBomb.gameObject.SetActive(mobile);
            UI.BtnDash.gameObject.SetActive(mobile);
            UI.BtnPause.gameObject.SetActive(mobile);
            UI.BtnDesktopOnlineExit.gameObject.SetActive(!mobile && online);
            // 联机入口原本用于退出，不能以单机暂停图标误示会冻结服务器时间。
            UI.PauseIconRoot.SetActive(!online);
            UI.TextPauseOnlineExit.gameObject.SetActive(online);
            UI.TextPauseOnlineExit.text = Loc.T("ui.arcade.hud.online_exit");
            ApplyFieldUpgradeLayout(mobile);
            ApplyEffectGroupVisibility();
        }

        /// <summary>坐标避让前先切换能力按钮的正式尺寸，保证首次显示及布局编辑页使用相同触控几何。</summary>
        private void ApplyFieldUpgradeLayout(bool mobile)
        {
            UI.MobileFieldUpgradeFace.gameObject.SetActive(mobile);
            UI.DesktopFieldUpgradeFace.gameObject.SetActive(!mobile);
            var fieldGraphic = UI.BtnFieldUpgrade.GetComponent<Image>();
            fieldGraphic.enabled = false;
            UI.BtnFieldUpgrade.targetGraphic = (mobile ? UI.MobileFieldUpgradeFace : UI.DesktopFieldUpgradeFace).GetComponent<Image>();
            ((RectTransform)UI.BtnFieldUpgrade.transform).sizeDelta = mobile
                ? UI.MobileFieldUpgradeFace.rect.size : UI.DesktopFieldUpgradeFace.rect.size;
        }

        /// <summary>两端舞台标签共享已经本地化的权威状态文本，不复制休息、过图或 Boss 判断。</summary>
        private void SyncDesktopStage()
        {
            UI.DesktopTextArena.text = UI.TextArena.text;
            UI.DesktopTextWave.text = UI.TextWave.text;
        }

        /// <summary>能力次数只读取现有库存；触屏零次数禁用沿用原按钮裁决。</summary>
        private void RenderAbilityCounts(int bombs, int dashes)
        {
            Set(UI.TextMobileBombCount, bombs.ToString());
            Set(UI.TextMobileDashCount, dashes.ToString());
            Set(UI.TextBombCount, bombs.ToString());
            Set(UI.TextDashCount, dashes.ToString());
            UI.DesktopBombIcon.color = bombs > 0 ? Color.white : new Color(.45f, .45f, .45f);
            UI.DesktopDashIcon.color = dashes > 0 ? Color.white : new Color(.45f, .45f, .45f);
        }

        /// <summary>剩余时间来自单人规则快照或联机服务器；分母读取双方共用的正式掉落持续时间，不另起 UI 计时器。</summary>
        private void RenderTimedStatus(string temporaryNameKey, float temporarySeconds,
            IReadOnlyList<ArcadePowerupHudEntry> powerups)
        {
            effects.Clear();
            skullWarningIcons.Clear();
            AddEffect(temporaryNameKey, temporarySeconds, 0, true);
            weaponEffectCount = effects.Count;
            if (powerups != null)
                foreach (var powerup in powerups) AddEffect(powerup.NameKey, powerup.Seconds, powerup.Charges, false);
            UI.WeaponEffectList.totalCount = weaponEffectCount;
            UI.WeaponEffectList.RefreshCells();
            UI.EffectList.totalCount = effects.Count - weaponEffectCount;
            UI.EffectList.RefreshCells();
            UI.MobileWeaponEffectList.totalCount = weaponEffectCount;
            UI.MobileWeaponEffectList.RefreshCells();
            UI.MobileEffectList.totalCount = effects.Count - weaponEffectCount;
            UI.MobileEffectList.RefreshCells();
            ApplyEffectGroupVisibility();
            UpdateSkullWarning(Time.unscaledTimeAsDouble);
        }

        /// <summary>手机效果在玩家列表后自动排版，空组不留洞；桌面保留原分类与位置。</summary>
        private void ApplyEffectGroupVisibility()
        {
            UI.WeaponEffectRoot.SetActive(!mobileHudLayout && UI.WeaponEffectList.totalCount > 0);
            UI.SupportEffectRoot.SetActive(!mobileHudLayout && UI.EffectList.totalCount > 0);
            UI.DesktopEffectsRoot.SetActive(!mobileHudLayout &&
                (UI.WeaponEffectList.totalCount > 0 || UI.EffectList.totalCount > 0));
            UI.MobileWeaponEffectList.gameObject.SetActive(UI.MobileWeaponEffectList.totalCount > 0);
            UI.MobileEffectList.gameObject.SetActive(UI.MobileEffectList.totalCount > 0);
            UI.MobileEffectsRoot.SetActive(mobileHudLayout &&
                (UI.MobileWeaponEffectList.totalCount > 0 || UI.MobileEffectList.totalCount > 0));
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
            => RefreshEffectBox(transform, index + weaponEffectCount, true);

        private void RefreshWeaponEffect(Transform transform, int index) => RefreshEffectBox(transform, index, true);

        private void RefreshMobileEffect(Transform transform, int index) => RefreshEffectBox(transform, index + weaponEffectCount, false);
        private void RefreshMobileWeaponEffect(Transform transform, int index) => RefreshEffectBox(transform, index, false);

        private void RefreshEffectBox(Transform transform, int index, bool desktop)
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
                "arcade.powerup.skull.small" => UI.SkullRedIcon,
                "arcade.powerup.skull.large" => UI.SkullPurpleIcon,
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
            bool skull=effect.Id=="arcade.powerup.skull.small"||effect.Id=="arcade.powerup.skull.large";
            // 秒数与环只投影效果余量；手机继续使用原表现，不增加自己的计时或强化槽。
            box.DesktopDuration.gameObject.SetActive(desktop);
            box.DesktopDuration.text = Mathf.CeilToInt(effect.Seconds) + "s";
            box.DesktopSkullRing.gameObject.SetActive(desktop && skull);
            box.DesktopSkullRing.fillAmount = effect.Duration > 0 ? Mathf.Clamp01(effect.Seconds / effect.Duration) : 1;
            box.DesktopSkullRing.color = effect.Id == "arcade.powerup.skull.large"
                ? new Color(.65f,.22f,1) : new Color(1,.2f,.22f);
            // 骷髅只保留彩色本体，不显示卡片底板、文字、外圈或时间条；复用时完整还原普通道具。
            box.ProgressRoot.SetActive(!skull);
            box.Backdrop.enabled=!skull&&!desktop;
            HealthFillUtility.Apply(box.ProgressFill, effect.Duration > 0f ? Mathf.Clamp01(effect.Seconds / effect.Duration) : 1f);
            box.ProgressFill.color = effect.Seconds <= 3f ? new Color(1f, .36f, .22f) : effect.Weapon
                ? new Color(1f, .78f, .28f) : new Color(.3f, .85f, .92f);
            box.Icon.color = !skull&&effect.Seconds <= 3f ? new Color(1f, .65f, .45f) : Color.white;
            box.TextFallback.color = box.Icon.color;
            if(skull&&effect.Seconds<=3f)skullWarningIcons.Add(box.Icon);
        }

        /// <summary>权威剩余量仅决定是否提醒；未缩放时间只驱动每秒两次的轻微明暗，不延长强化。</summary>
        private void UpdateSkullWarning(double now)
        {
            float alpha=(float)(.775+.225*Math.Cos(now*Math.PI*4));
            foreach(var icon in skullWarningIcons)
                if(icon!=null)icon.color=new Color(1,1,1,alpha);
        }

        private void ClearTimedEffects()
        {
            skullWarningIcons.Clear();
            effects.Clear();
            weaponEffectCount = 0;
            UI.WeaponEffectList.ClearCells();
            UI.EffectList.ClearCells();
            UI.MobileWeaponEffectList.ClearCells();
            UI.MobileEffectList.ClearCells();
            UI.MobileEffectsRoot.SetActive(false);
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

