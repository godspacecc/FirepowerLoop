using System.Collections.Generic;
using System.Globalization;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>宿主内的身体状态展示页；只读取战斗、条件模型与当前配置，不裁决伤害、恢复、疾病或存档。</summary>
    public sealed class StatusManagementPageCtrl : BasePageCtrl
    {
        private StatusManagementPageUI UI => (StatusManagementPageUI)BaseUI;
        /// <summary>索引只代表本页选择；重绑时归零，隐藏重显时保留。</summary>
        private int selected;
        /// <summary>稳定的状态身份及本地化后缀，不用玩家显示文字决定行为。</summary>
        private static readonly string[] Keys = { "health", "power", "water", "temperature", "tili", "injury", "disease" };
        /// <summary>圆形仪表的展示顺序；生命独立放在人体下方，不参与左侧集合。</summary>
        private static readonly int[] GaugeIndices = { 3, 4, 2, 1 };
        /// <summary>刷新时替换的展示快照，不作为属性权威或规则缓存。</summary>
        private readonly List<Presentation> items = new List<Presentation>();
        /// <summary>仅可见期间拥有订阅；隐藏、重绑和销毁统一解除。</summary>
        private readonly List<IUnRegister> subscriptions = new List<IUnRegister>();

        protected override void OnInit()
        {
            selected = 0;
            UI.StatusList.AddItemRefreshListener((item, index) => RefreshGauge(item, GaugeIndices[index]));
            UI.AfflictionList.AddItemRefreshListener(RefreshAffliction);
            BindSelection(UI.HealthButton, 0);
            var owner=UI;
            UI.BtnRest.onClick.AddListener(()=>
            {
                if(!IsShow()||!ReferenceEquals(owner,BaseUI))return;
                var success=this.GetSystem<BasePreparationSystem>().TryRest(out var error);
                Notify.ShowFloatTips(success?Loc.T("ui.base_prep.rest_success"):error);Refresh();
            });
        }

        /// <summary>隐藏到显示时建立一次订阅并读取最新值；重复显示由基类保证幂等。</summary>
        protected override void OnShow()
        {
            subscriptions.Add(this.RegisterEvent<PlayerConditionChangedEvent>(_ => Refresh()));
            subscriptions.Add(this.RegisterEvent<CombatHealthChangedEvent>(e => { if (e.Faction == CombatFaction.Player) Refresh(); }));
            subscriptions.Add(this.RegisterEvent<LanguageChangedEvent>(_ => Refresh()));
            subscriptions.Add(this.RegisterEvent<PlayerReadyEvent>(_ => Refresh()));
            subscriptions.Add(this.RegisterEvent<BasePreparationChangedEvent>(_=>Refresh()));
            subscriptions.Add(this.RegisterEvent<ShopChangedEvent>(_=>Refresh()));
            Refresh();
        }

        /// <summary>解除外部事件并清空快照；销毁时不访问已经失效的 View。</summary>
        protected override void OnHide()
        {
            foreach (var subscription in subscriptions) subscription.UnRegister();
            subscriptions.Clear();
            items.Clear();
        }

        /// <summary>先读取权威模型并完成快照，再刷新列表和当前详情。</summary>
        private void Refresh()
        {
            if (!IsShow()) return;
            var condition = this.GetModel<PlayerConditionModel>();
            var combat = this.GetModel<CombatModel>();
            var preparation=this.GetSystem<BasePreparationSystem>();
            var offline=this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OfflineFull&&this.GetModel<BasePreparationModel>().Initialized;
            UI.PreparationRoot.SetActive(offline);
            if(offline)
            {
                UI.BaseDateText.text=preparation.DateText;
                UI.BaseGoldText.text=Loc.T("ui.shop.gold",this.GetModel<CurrencyModel>().Gold);
                UI.BtnRest.gameObject.SetActive(preparation.IsStableBase);UI.BtnRest.interactable=preparation.IsStableBase;
                UI.RestInfoText.text=this.GetUtility<IRuntimeConfigProvider>().TryGetPreparationRule(out var prepRule,out _)?Loc.T("ui.base_prep.rest_info",prepRule.DailyGold):string.Empty;
            }
            items.Clear();
            // 上限未初始化时保留实际当前生命，但不把零当作有效上限或推断健康程度。
            items.Add(new Presentation(combat.PlayerMaxHP > 0 ? Ratio(combat.PlayerHP.Value, combat.PlayerMaxHP) :
                    Loc.T("ui.status.value_unavailable", Number(combat.PlayerHP.Value)),
                combat.PlayerMaxHP > 0 ? Level(combat.PlayerHP.Value, combat.PlayerMaxHP) : Loc.T("ui.status.config_unavailable"),
                HealthDescription()));
            items.Add(new Presentation(ConditionRatio(condition.Power, condition.MaxPower), Level(condition.Power, condition.MaxPower),
                Loc.T("ui.status.power_detail", Number(PlayerConditionRules.PowerDrainPerGameMinute),
                    Number(PlayerConditionRules.PowerDepletionDamagePerRealSecond), Number(PlayerConditionRules.BasePowerRecoveryPerRealSecond))));
            items.Add(new Presentation(ConditionRatio(condition.Water, condition.MaxWater), Level(condition.Water, condition.MaxWater),
                Loc.T("ui.status.water_detail", Number(PlayerConditionRules.WaterDrainPerGameMinute),
                    Number(PlayerConditionRules.WaterDepletionDamagePerRealSecond), Number(PlayerConditionRules.BaseWaterRecoveryPerRealSecond))));
            items.Add(new Presentation(Loc.T("ui.status.temperature_value", Number(condition.Temperature)),
                Loc.T(condition.Temperature < 36f ? "ui.status.cold" : condition.Temperature > 38f ? "ui.status.hot" : "ui.status.normal"),
                Loc.T("ui.status.temperature_detail", Number(PlayerConditionRules.NormalTemperature), Number(PlayerConditionRules.BaseTemperatureRecoveryPerRealSecond))));
            items.Add(new Presentation(Ratio(condition.Tili, condition.MaxTili),
                condition.SprintReleaseRequired ? Loc.T("ui.status.sprint_locked") : Level(condition.Tili, condition.MaxTili),
                Loc.T("ui.status.tili_detail", Number(PlayerConditionRules.SprintSpeedMultiplier),
                    Number(PlayerConditionRules.SprintTiliCostPerRealSecond), Number(PlayerConditionRules.WalkingTiliRecoveryPerRealSecond),
                    Number(PlayerConditionRules.StandingTiliRecoveryPerRealSecond), Number(PlayerConditionRules.SprintRestartThreshold))));
            for (var i = 5; i < Keys.Length; i++)
                items.Add(new Presentation("—", Loc.T("ui.status.unavailable"), Loc.T("ui.status." + Keys[i] + "_detail")));
            UI.StatusList.totalCount = GaugeIndices.Length;
            UI.StatusList.RefreshCells();
            UI.AfflictionList.totalCount = 2;
            UI.AfflictionList.RefreshCells();
            UI.HealthValueText.text = items[0].Value;
            UI.HealthFill.fillAmount = Progress(0);
            UI.HealthFill.color = selected == 0 ? new Color(.85f,.73f,.44f) : new Color(.9f,.92f,.9f);
            var detail = items[selected];
            UI.DetailNameText.text = Loc.T("ui.status." + Keys[selected]);
            UI.DetailValueText.text = detail.Value;
            UI.DetailStateText.text = detail.State;
            UI.DetailDescriptionText.text = detail.Description;
        }

        /// <summary>刷新时读取当前单位实际配置；配置缺失时显示不可用，不建立第二份恢复参数。</summary>
        private string HealthDescription()
        {
            if (this.GetModel<UnitModel>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) &&
                this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer(player.ConfigId, out var config, out _))
                return Loc.T("ui.status.health_detail", Number(config.BaseRecoveryPerSecond));
            // 未取得当前角色配置时不套用默认值，以免界面伪造恢复速度。
            return Loc.T("ui.status.health_config_unavailable");
        }

        /// <summary>复用时覆盖全部显示和回调；旧 View 回调不能修改重绑后的页面。</summary>
        private void RefreshGauge(Transform item, int index)
        {
            var row = item.GetComponent<StatusItemBoxUI>();
            var data = items[index];
            row.NameText.text = Loc.T("ui.status." + Keys[index]);
            // 数值集中在详情区；左侧只显示图标和短状态，避免回到行式属性表。
            row.ValueText.text = string.Empty;
            row.ValueText.gameObject.SetActive(false);
            row.StateText.text = data.State;
            row.StateText.color = index >= 5 ? new Color(.65f,.68f,.69f) : new Color(.82f,.83f,.76f);
            row.Icon.gameObject.SetActive(true);
            row.MeterRoot.SetActive(true);
            row.Icon.sprite = index == 1 ? row.PowerSprite :
                index == 2 ? row.WaterSprite : index == 3 ? row.TemperatureSprite : row.StaminaSprite;
            row.MeterFill.fillAmount = Progress(index);
            var condition = this.GetModel<PlayerConditionModel>();
            var warning = index == 3 && (condition.Temperature < 36f || condition.Temperature > 38f);
            var selectedColor = selected == index ? new Color(.85f,.73f,.44f) : new Color(.9f,.92f,.9f);
            row.NameText.color = selectedColor;
            row.Icon.color = row.MeterFill.color = warning ? new Color(.86f,.34f,.32f) : selectedColor;
            row.Selected.SetActive(selected == index);
            row.ClickButton.interactable = true;
            row.ClickButton.image.color = Color.clear;
            BindSelection(row.ClickButton, index);
        }

        /// <summary>伤病使用独立文字入口，不复用圆形比例，也不生成不存在的身体警告。</summary>
        private void RefreshAffliction(Transform item, int position)
        {
            var index = position + 5;
            var row = item.GetComponent<StatusAfflictionBoxUI>();
            row.NameText.text = Loc.T("ui.status." + Keys[index]);
            row.StateText.text = items[index].State;
            row.NameText.color = selected == index ? new Color(.85f,.73f,.44f) : new Color(.9f,.92f,.9f);
            row.Selected.SetActive(selected == index);
            row.ClickButton.interactable = true;
            BindSelection(row.ClickButton, index);
        }

        /// <summary>绑定稳定身份并覆盖旧回调；隐藏或重绑后的旧按钮不能改变当前选择。</summary>
        private void BindSelection(Button button, int index)
        {
            var bound = BaseUI;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (!IsShow() || !ReferenceEquals(bound, BaseUI)) return;
                selected = index;
                Refresh();
            });
        }

        /// <summary>生命与资源显示真实比例；体温外圈固定完整，仅由状态决定警告颜色。</summary>
        private float Progress(int index)
        {
            var condition = this.GetModel<PlayerConditionModel>();
            var combat = this.GetModel<CombatModel>();
            switch (index)
            {
                // 战斗生命是整数，先转浮点；否则非满血会被整数除法显示为零。
                case 0: return combat.PlayerMaxHP > 0 ? Mathf.Clamp01((float)combat.PlayerHP.Value / combat.PlayerMaxHP) : 0;
                case 1: return Mathf.Clamp01(condition.Power / condition.MaxPower);
                case 2: return Mathf.Clamp01(condition.Water / condition.MaxWater);
                case 3: return 1;
                case 4: return Mathf.Clamp01(condition.Tili / condition.MaxTili);
                default: return 0;
            }
        }

        private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        /// <summary>水分和能量的当前值与上限固定显示一位小数，仅影响展示，不回写模型或改变耗损精度。</summary>
        private static string ConditionRatio(float value, float maximum) =>
            value.ToString("0.0", CultureInfo.InvariantCulture) + " / " + maximum.ToString("0.0", CultureInfo.InvariantCulture);
        private static string Ratio(float value, float maximum) => Number(value) + " / " + Number(maximum);
        private static string Level(float value, float maximum) => Loc.T(value <= 0 ? "ui.status.depleted" :
            maximum > 0 && value / maximum < .2f ? "ui.status.low" : "ui.status.available");

        /// <summary>一次刷新内的不可变显示值；不包含可写模型或 Unity 资源所有权。</summary>
        private readonly struct Presentation
        {
            public readonly string Value, State, Description;
            public Presentation(string value, string state, string description)
            { Value = value; State = state; Description = description; }
        }
    }
}
