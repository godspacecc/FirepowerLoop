using System;
using QFramework;

namespace Game
{
    public enum TestCmdResultCode
    {
        EmptyInput,
        UnknownCommand,
        UsageError,
        InvalidAmount,
        ItemNotFound,
        StateRejected,
        BagFull,
        CloneComplete,
        ClonePartial,
        ClearBagComplete,
        InvalidGameMinutes,
        AdvanceTimeComplete,
        UnknownAttribute,
        InvalidAttributeValue,
        AttributeOutOfRange,
        AttributeSetComplete,
        NetworkDiagnosticsChanged,
        UnitHealthBarsChanged,
        CameraModeChanged,
        MobileInputModeChanged,
        ExecutionFailed
        ,PrefixRequired
        ,ServerNotConnected
        ,ServerTimeout
        ,ServerDisconnected
        ,Canceled
        ,ServerUnknownCommand
        ,ServerExecutionFailed
        ,ServerCatalogUnavailable
        ,ServerCommandComplete
        ,ArcadeCommandComplete
        ,AppearanceGalleryOpened
        ,RuntimeLogOpenRequested
        ,TerrainDiagnosticsComplete
        ,PlayerAppearanceChanged
        ,WeatherChanged
    }

    public readonly struct TestCmdExecutionResult
    {
        public TestCmdExecutionResult(
            bool recognized,
            bool success,
            TestCmdResultCode code,
            string message,
            int affectedAmount = 0,
            int rejectedAmount = 0,
            string attributeId = null,
            double beforeValue = 0d,
            double afterValue = 0d,
            long serverTime = 0,
            int affectedItemId = 0)
        {
            Recognized = recognized;
            Success = success;
            Code = code;
            Message = message ?? string.Empty;
            AffectedAmount = affectedAmount;
            RejectedAmount = rejectedAmount;
            AttributeId = attributeId ?? string.Empty;
            BeforeValue = beforeValue;
            AfterValue = afterValue;
            ServerTime = serverTime;
            AffectedItemId = affectedItemId;
        }

        public bool Recognized { get; }
        public bool Success { get; }
        public TestCmdResultCode Code { get; }
        public string Message { get; }
        public int AffectedAmount { get; }
        public int RejectedAmount { get; }
        public string AttributeId { get; }
        public double BeforeValue { get; }
        public double AfterValue { get; }
        public long ServerTime { get; }
        public int AffectedItemId { get; }
    }

    public sealed class TestCmdModel : AbstractModel
    {
        public const string RecentCategoryId = "recent";
        public TestCmdExecutionResult LastResult { get; private set; }
        public bool HasLastResult { get; private set; }
        /// <summary>普通地图敌人头顶血条开关；默认关闭，仅当前运行会话有效。</summary>
        public bool UnitHealthBarsVisible { get; private set; }
        /// <summary>仅当前运行会话有效的整套外观编号；0 使用正式衣物，不进入存档或网络协议。</summary>
        public int PlayerAppearanceIndex { get; private set; }
        public static System.Collections.Generic.IReadOnlyList<string> PlayerAppearanceNames { get; } = Array.AsReadOnly(new[]
        {
            "男·衬衫", "男·西装", "女·商务装", "女·长外套", "女·夹克", "女·警服", "男·连帽衫", "男·夹克", "男·警服"
        });
        /// <summary>领域入口校验状态后提交；非法值与同值不产生变化，事件由 TestCmdSystem 发布。</summary>
        public bool SetPlayerAppearanceIndex(int index)
        {
            if (index < 0 || index > PlayerAppearanceNames.Count || index == PlayerAppearanceIndex) return false;
            PlayerAppearanceIndex = index;
            return true;
        }

        protected override void OnInit()
        {
        }

        public void SetLastResult(TestCmdExecutionResult result)
        {
            LastResult = result;
            HasLastResult = true;
        }

        public bool SetUnitHealthBarsVisible(bool visible)
        {
            if (UnitHealthBarsVisible == visible)
            {
                return false;
            }

            UnitHealthBarsVisible = visible;
            return true;
        }
    }
}
