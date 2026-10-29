using System;
using Newtonsoft.Json;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>单个摇杆的共用设备设置；位置为安全区域内中心比例，大小和透明度相对 Prefab 默认值。</summary>
    [Serializable]
    public sealed class MobileStickLayoutData
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("scale")] public float Scale { get; set; } = 1f;
        [JsonProperty("opacity")] public float Opacity { get; set; } = 1f;

        public MobileStickLayoutData Clone() => new MobileStickLayoutData
        { X = X, Y = Y, Scale = Scale, Opacity = Opacity };

        internal void Sanitize(bool move)
        {
            X = Finite(X, move ? .18f : .82f, move ? 0f : 0.52f, move ? 0.48f : 1f);
            Y = Finite(Y, .24f, 0f, 0.55f);
            Scale = Finite(Scale, 1f, 0.7f, 1.5f);
            Opacity = Finite(Opacity, 1f, 0.25f, 1f);
        }

        private static float Finite(float value, float fallback, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    /// <summary>现役街机触屏动作的稳定身份；顺序同时用于编辑预览和实战布局，不依赖显示文字。</summary>
    public enum MobileLayoutButton { Pause, Bomb, Dash, FieldUpgrade, DonateLife }

    /// <summary>按钮中心在安全区内的比例位置；不保存 Unity 对象，不提供大小或透明度设置。</summary>
    [Serializable]
    public sealed class MobileButtonLayoutData
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        public MobileButtonLayoutData Clone() => new MobileButtonLayoutData { X = X, Y = Y };
        internal void Sanitize(MobileButtonLayoutData defaults)
        {
            X = float.IsNaN(X) || float.IsInfinity(X) ? defaults.X : Mathf.Clamp01(X);
            Y = float.IsNaN(Y) || float.IsInfinity(Y) ? defaults.Y : Mathf.Clamp01(Y);
        }
    }

    /// <summary>三块玩家信息栏的稳定身份；顺序与默认从上到下的排列一致。</summary>
    public enum MobileLayoutHud { Status, Health, Weapon }

    /// <summary>信息栏中心比例与相对 Prefab 的缩放；仅保存设备设置，不含玩家状态。</summary>
    [Serializable]
    public sealed class MobileHudLayoutData
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("scale")] public float Scale { get; set; } = 1f;
        public MobileHudLayoutData Clone() => new MobileHudLayoutData { X = X, Y = Y, Scale = Scale };
        internal void Sanitize(MobileHudLayoutData defaults)
        {
            X = float.IsNaN(X) || float.IsInfinity(X) ? defaults.X : Mathf.Clamp01(X);
            Y = float.IsNaN(Y) || float.IsInfinity(Y) ? defaults.Y : Mathf.Clamp01(Y);
            Scale = float.IsNaN(Scale) || float.IsInfinity(Scale) ? 1f : Mathf.Clamp(Scale, .7f, 1.5f);
        }
    }

    /// <summary>本机共用摇杆、按钮与玩家信息布局；旧设置补默认值，保留已有设备设置。</summary>
    [Serializable]
    public sealed class MobileControlLayoutData
    {
        [JsonProperty("move")] public MobileStickLayoutData Move { get; set; } =
            new MobileStickLayoutData { X = .18f, Y = .24f };
        [JsonProperty("aim")] public MobileStickLayoutData Aim { get; set; } =
            new MobileStickLayoutData { X = .82f, Y = .24f };
        [JsonProperty("pause")] public MobileButtonLayoutData Pause { get; set; } = DefaultButton(MobileLayoutButton.Pause);
        [JsonProperty("bomb")] public MobileButtonLayoutData Bomb { get; set; } = DefaultButton(MobileLayoutButton.Bomb);
        [JsonProperty("dash")] public MobileButtonLayoutData Dash { get; set; } = DefaultButton(MobileLayoutButton.Dash);
        [JsonProperty("fieldUpgrade")] public MobileButtonLayoutData FieldUpgrade { get; set; } = DefaultButton(MobileLayoutButton.FieldUpgrade);
        [JsonProperty("donateLife")] public MobileButtonLayoutData DonateLife { get; set; } = DefaultButton(MobileLayoutButton.DonateLife);
        [JsonProperty("status")] public MobileHudLayoutData Status { get; set; } = DefaultHud(MobileLayoutHud.Status);
        [JsonProperty("health")] public MobileHudLayoutData Health { get; set; } = DefaultHud(MobileLayoutHud.Health);
        [JsonProperty("weapon")] public MobileHudLayoutData Weapon { get; set; } = DefaultHud(MobileLayoutHud.Weapon);

        public MobileControlLayoutData Clone() => new MobileControlLayoutData
        {
            Move = Move?.Clone(), Aim = Aim?.Clone(), Pause = Pause?.Clone(), Bomb = Bomb?.Clone(),
            Dash = Dash?.Clone(), FieldUpgrade = FieldUpgrade?.Clone(), DonateLife = DonateLife?.Clone(),
            Status = Status?.Clone(), Health = Health?.Clone(), Weapon = Weapon?.Clone()
        }.Sanitize();

        public MobileButtonLayoutData Button(MobileLayoutButton id) => id switch
        {
            MobileLayoutButton.Pause => Pause, MobileLayoutButton.Bomb => Bomb,
            MobileLayoutButton.Dash => Dash, MobileLayoutButton.FieldUpgrade => FieldUpgrade,
            MobileLayoutButton.DonateLife => DonateLife,
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        private static MobileButtonLayoutData DefaultButton(MobileLayoutButton id) => id switch
        {
            MobileLayoutButton.Pause => new MobileButtonLayoutData { X = 1818f / 1920f, Y = 1016f / 1080f },
            MobileLayoutButton.Bomb => new MobileButtonLayoutData { X = 1818f / 1920f, Y = 910f / 1080f },
            MobileLayoutButton.Dash => new MobileButtonLayoutData { X = 1818f / 1920f, Y = 794f / 1080f },
            MobileLayoutButton.FieldUpgrade => new MobileButtonLayoutData { X = 1818f / 1920f, Y = 678f / 1080f },
            MobileLayoutButton.DonateLife => new MobileButtonLayoutData { X = .6875f, Y = .75f },
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        public MobileHudLayoutData Hud(MobileLayoutHud id) => id switch
        {
            MobileLayoutHud.Status => Status, MobileLayoutHud.Health => Health,
            MobileLayoutHud.Weapon => Weapon, _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        private static MobileHudLayoutData DefaultHud(MobileLayoutHud id) => id switch
        {
            MobileLayoutHud.Status => new MobileHudLayoutData { X = 904f / 1920f, Y = 130f / 1080f },
            MobileLayoutHud.Health => new MobileHudLayoutData { X = .5f, Y = 67f / 1080f },
            MobileLayoutHud.Weapon => new MobileHudLayoutData { X = 1034f / 1920f, Y = 130f / 1080f },
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        /// <summary>未调整的位置保留 Prefab 默认像素间距，宽屏仍沿血条左边缘排列；返回副本不改写设置。</summary>
        public MobileHudLayoutData HudForDisplay(MobileLayoutHud id, Vector2 areaSize)
        {
            var value = Hud(id).Clone();
            var defaults = DefaultHud(id);
            // 仅迁移旧版原始默认位置；玩家自行拖动过的中心与缩放保持原值。
            var oldX = id == MobileLayoutHud.Status ? 904f / 1920f : id == MobileLayoutHud.Health ? .5f : 984f / 1920f;
            var oldY = id == MobileLayoutHud.Status ? 170f / 1080f : id == MobileLayoutHud.Health ? 107f / 1080f : 54f / 1080f;
            if (Mathf.Approximately(value.Y, oldY) &&
                (Mathf.Approximately(value.X, oldX) || id == MobileLayoutHud.Weapon && Mathf.Approximately(value.X, .5f)))
            { value.X = defaults.X; value.Y = defaults.Y; }
            // 旧版默认武器栏中心为屏幕中线；仅适配这一默认位置，保留已拖动的玩家布局。
            if (id == MobileLayoutHud.Weapon && Mathf.Approximately(value.X, .5f) && Mathf.Approximately(value.Y, defaults.Y))
                value.X = defaults.X;
            if (areaSize.x > 0 && areaSize.y > 0 && Mathf.Approximately(value.X, defaults.X) && Mathf.Approximately(value.Y, defaults.Y))
            {
                value.X = .5f + (defaults.X - .5f) * 1920f / areaSize.x;
                value.Y = defaults.Y * 1080f / areaSize.y;
            }
            return value;
        }

        /// <summary>所有预览与实战使用同一摇杆中心；返回副本，不再应用历史基地偏移。</summary>
        public MobileStickLayoutData OrdinaryStick(bool move) => (move ? Move : Aim).Clone();

        public MobileControlLayoutData Sanitize()
        {
            Move ??= new MobileControlLayoutData().Move;
            Aim ??= new MobileControlLayoutData().Aim;
            Move.Sanitize(true);
            Aim.Sanitize(false);
            MigrateDefaultStick(Move, true);
            MigrateDefaultStick(Aim, false);
            Pause ??= DefaultButton(MobileLayoutButton.Pause);
            Bomb ??= DefaultButton(MobileLayoutButton.Bomb);
            Dash ??= DefaultButton(MobileLayoutButton.Dash);
            FieldUpgrade ??= DefaultButton(MobileLayoutButton.FieldUpgrade);
            DonateLife ??= DefaultButton(MobileLayoutButton.DonateLife);
            // 仅迁移旧默认按钮位置；保留玩家主动拖动保存的位置。
            MigrateDefaultButton(Pause, .95f, .93f, MobileLayoutButton.Pause);
            MigrateDefaultButton(Pause, .05f, .93f, MobileLayoutButton.Pause);
            MigrateDefaultButton(FieldUpgrade, .6875f, 1000f / 1080f, MobileLayoutButton.FieldUpgrade);
            foreach (MobileLayoutButton id in Enum.GetValues(typeof(MobileLayoutButton))) Button(id).Sanitize(DefaultButton(id));
            Status ??= DefaultHud(MobileLayoutHud.Status);
            Health ??= DefaultHud(MobileLayoutHud.Health);
            Weapon ??= DefaultHud(MobileLayoutHud.Weapon);
            foreach (MobileLayoutHud id in Enum.GetValues(typeof(MobileLayoutHud))) Hud(id).Sanitize(DefaultHud(id));
            return this;
        }

        /// <summary>旧默认布局载入时更新一次，重复清理幂等，不覆盖自定义位置。</summary>
        private static void MigrateDefaultButton(MobileButtonLayoutData value, float oldX, float oldY, MobileLayoutButton id)
        {
            if (!Mathf.Approximately(value.X, oldX) || !Mathf.Approximately(value.Y, oldY)) return;
            var defaults = DefaultButton(id);
            value.X = defaults.X;
            value.Y = defaults.Y;
        }

        /// <summary>仅迁移旧版原始默认中心；保留尺寸、透明度及任何已自定义的位置，重复加载结果不变。</summary>
        private static void MigrateDefaultStick(MobileStickLayoutData value, bool move)
        {
            var oldX = move ? 220f / 1920f : 1690f / 1920f;
            if (!Mathf.Approximately(value.X, oldX) || !Mathf.Approximately(value.Y, 205f / 1080f)) return;
            value.X = move ? .18f : .82f;
            value.Y = .24f;
        }
    }

    public readonly struct MobileControlLayoutChangedEvent { }

    /// <summary>持有已保存的本机布局；不持有 UI 对象和编辑草稿，不负责输入及战斗规则。</summary>
    public sealed class MobileControlLayoutModel : AbstractModel
    {
        private MobileControlLayoutData current = new MobileControlLayoutData();
        protected override void OnInit() { }
        public MobileControlLayoutData ToData() => current.Clone();
        public void Apply(MobileControlLayoutData data) =>
            current = (data ?? new MobileControlLayoutData()).Clone();
    }
}
