using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Network;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>解析测试指令并委托领域系统执行；只读诊断不修改地图资源，UI 切换仍由 Controller 处理。</summary>
    public sealed class TestCmdSystem : AbstractSystem
    {
        private const string EmptyInputText = "请输入指令。";
        /// <summary>当前地图对照前的实例化开关；重复关闭不覆盖原值，地图释放后清空。</summary>
        private readonly Dictionary<Terrain, bool> terrainInstancingBefore = new Dictionary<Terrain, bool>();
        private IMapContentHandle terrainProbeOwner;
        private const string TerrainUsage = "用法：#terrain | #terrain instancing off | #terrain instancing restore";
        private const string PrefixRequiredText = "客户端指令请使用 #，服务器指令请使用 $。";
        private const string UnknownCommandText = "未知指令。";
        private const string UsageCloneText = "用法：clone <ItemId或名称> [Amount]（数量默认 1）";
        private const string UsageClearBagText = "用法：clear bag";
        private const string UsageAdvanceTimeText = "用法：advance time <游戏分钟>";
        private const string UsageSetAttributeText = "用法：set <hp|power|tili|maxhp|movespeed|water|maxwater|temp> <值>";
        private const string InvalidAmountText = "数量必须是大于零的十进制整数。";
        private const string BagFullText = "玩家背包已满。";
        private const string ExecutionFailedText = "指令执行失败。";
        private const string InvalidGameMinutesText = "游戏分钟必须是大于 0 的十进制整数。";
        private const string InvalidAttributeValueText = "属性值格式无效。";
        private const string AttributeOutOfRangeText = "属性值超出允许范围。";
        private const string ServerLoginRequiredText = "请先登录后使用服务器指令。";
        private const string UsageCameraText = "用法：camera topdown / camera shoulder / camera firstperson";
        private const string UsageMobileInputText = "用法：mobileinput on / mobileinput off";
        private const string UsageArcadeText =
            "用法：arcade status | heal | damage <正整数> | gold <正整数> | bombs <0-9999> | dashes <0-9999> | drop <DropId> | clear <wave|arena>";

        protected override void OnInit()
        {
            this.RegisterEvent<MapLoadingStartedEvent>(_ => RestoreTerrainProbe());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => ClearTerrainProbe());
        }

        protected override void OnDeinit() => RestoreTerrainProbe();

        /// <summary>切图前恢复仍存活的地形，避免运行时对照开关泄漏到后续地图。</summary>
        private void RestoreTerrainProbe()
        {
            foreach (var pair in terrainInstancingBefore)
                if (pair.Key) pair.Key.drawInstanced = pair.Value;
            ClearTerrainProbe();
        }

        private void ClearTerrainProbe()
        {
            terrainInstancingBefore.Clear();
            terrainProbeOwner = null;
        }

        public async Task<TestCmdExecutionResult> ExecuteAsync(string rawText, CancellationToken cancellationToken)
        {
            var trimmed = (rawText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return FinishUnsubmitted(Result(false, false, TestCmdResultCode.EmptyInput, EmptyInputText));
            }

            if ((trimmed[0] != '#' && trimmed[0] != '$') || trimmed.Length == 1)
            {
                return FinishUnsubmitted(Result(false, false, TestCmdResultCode.PrefixRequired, PrefixRequiredText));
            }

            string body = trimmed.Substring(1).Trim();
            if (body.Length == 0)
            {
                return FinishUnsubmitted(Result(false, false, TestCmdResultCode.PrefixRequired, PrefixRequiredText));
            }

            TestCmdExecutionResult result;
            if (trimmed[0] == '$')
            {
                if (!this.GetModel<OnlineAccountModel>().Snapshot.Value.IsAuthenticated)
                {
                    return FinishUnsubmitted(Result(false, false, TestCmdResultCode.ServerNotConnected, ServerLoginRequiredText));
                }

                result = await this.GetSystem<ServerTestCmdSystem>().ExecuteAsync(body, cancellationToken);
                WriteResultLog(result);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                result = Result(false, false, TestCmdResultCode.Canceled, "指令已取消");
                WriteResultLog(result);
            }
            else
            {
                result = string.Equals(body, "gallery", StringComparison.OrdinalIgnoreCase)
                    ? await ExecuteGalleryAsync(trimmed, cancellationToken)
                    : ExecuteClientBody(body);
            }

            this.GetModel<TestCmdModel>().SetLastResult(result);
            if (this.GetModel<TestToolSettingsModel>().RecordSubmittedCommand(trimmed))
            {
                this.GetSystem<SettingsSystem>().MarkDirty();
            }
            return result;
        }

        private TestCmdExecutionResult ExecuteClientBody(string trimmed)
        {
            var parts = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (string.Equals(parts[0], "weather", StringComparison.OrdinalIgnoreCase))
                return ExecuteWeather(trimmed, parts);
            if (string.Equals(parts[0], "appearance", StringComparison.OrdinalIgnoreCase))
                return ExecutePlayerAppearance(trimmed, parts);
            if (string.Equals(parts[0], "clone", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteClone(trimmed, parts);
            }

            if (string.Equals(parts[0], "clear", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteClearBag(trimmed, parts);
            }

            if (string.Equals(parts[0], "advance", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteAdvanceTime(trimmed, parts);
            }

            if (string.Equals(parts[0], "unitbar", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteUnitHealthBars(trimmed, parts);
            }

            if (string.Equals(parts[0], "set", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteSetAttribute(trimmed, parts);
            }

            if (string.Equals(parts[0], "camera", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteCameraMode(trimmed, parts);
            }

            if (string.Equals(parts[0], "mobileinput", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteMobileInputMode(trimmed, parts);
            }

            if (string.Equals(parts[0], "arcade", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteArcade(trimmed, parts);
            }

            if (string.Equals(parts[0], "log", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteRuntimeLog(trimmed, parts);
            }

            if (string.Equals(parts[0], "terrain", StringComparison.OrdinalIgnoreCase))
                return ExecuteTerrainDiagnostics(trimmed, parts);

            if (IsNetworkDiagnosticsCommand(parts))
            {
                return ExecuteNetworkDiagnostics(trimmed, parts);
            }

            return Finish(trimmed, Result(false, false, TestCmdResultCode.UnknownCommand, UnknownCommandText), false);
        }

        /// <summary>离线测试专用整套外观；不写装备、存档或联机数据，先校验再提交与广播。</summary>
        private TestCmdExecutionResult ExecutePlayerAppearance(string original, string[] parts)
        {
            const string usage = "用法：#appearance list | #appearance <1-9> | #appearance reset";
            if (!Application.isEditor && !Debug.isDebugBuild)
                return Finish(original, Warning(TestCmdResultCode.StateRejected, "造型测试仅在编辑器或开发版开放。"), true);
            if (parts.Length != 2)
                return Finish(original, Warning(TestCmdResultCode.UsageError, usage), true);
            if (string.Equals(parts[1], "list", StringComparison.OrdinalIgnoreCase))
                return Finish(original, Result(true, true, TestCmdResultCode.PlayerAppearanceChanged,
                    string.Join("\n", TestCmdModel.PlayerAppearanceNames.Select((name, i) => $"{i + 1}：{name}")) + "\nreset：恢复分部位换装"), true);
            int index;
            if (string.Equals(parts[1], "reset", StringComparison.OrdinalIgnoreCase)) index = 0;
            else if (!TryParseStrictInt(parts[1], out index) || index < 1 || index > TestCmdModel.PlayerAppearanceNames.Count)
                return Finish(original, Warning(TestCmdResultCode.UsageError, usage), true);
            var map = this.GetModel<MapSceneModel>();
            if (map.CurrentMode != MapPreparationMode.OfflineFull || map.Status.Value != MapLoadStatus.Loaded ||
                string.IsNullOrEmpty(map.CurrentMapId) || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
                return Finish(original, Warning(TestCmdResultCode.StateRejected, "只能在就绪的离线基地或远征中切换造型。"), true);
            if (this.GetModel<TestCmdModel>().SetPlayerAppearanceIndex(index))
                this.SendEvent(new PlayerTestAppearanceChangedEvent());
            return Finish(original, Result(true, true, TestCmdResultCode.PlayerAppearanceChanged,
                index == 0 ? "已恢复分部位换装。" : $"已切换为 {index}：{TestCmdModel.PlayerAppearanceNames[index - 1]}（仅本次运行有效）。"), true);
        }

        /// <summary>天气测试只提交领域意图；不直接操作场景，非法语法不取消正在播放的样板。</summary>
        private TestCmdExecutionResult ExecuteWeather(string original, string[] parts)
        {
            const string usage = "用法：#weather rain | clear | dry | demo | status";
            var mode = parts.Length == 2 ? parts[1].ToLowerInvariant() : string.Empty;
            if (mode != "rain" && mode != "clear" && mode != "dry" && mode != "demo" && mode != "status")
                return Finish(original, Warning(TestCmdResultCode.UsageError, usage), true);
            var weather = this.GetSystem<BaseWeatherSystem>();
            if (mode == "status") return Finish(original, Result(true, true, TestCmdResultCode.WeatherChanged, weather.Describe()), true);
            var success = weather.TrySetMode(mode, out var message);
            return Finish(original, Result(true, success, success ? TestCmdResultCode.WeatherChanged : TestCmdResultCode.StateRejected, message), true);
        }

        private TestCmdExecutionResult FinishUnsubmitted(TestCmdExecutionResult result)
        {
            this.GetModel<TestCmdModel>().SetLastResult(result);
            WriteResultLog(result);
            return result;
        }

        /// <summary>诊断已提交地图，或显式关闭/恢复实例化做画面对照；不改材质、地形数据或存档。</summary>
        private TestCmdExecutionResult ExecuteTerrainDiagnostics(string original, string[] parts)
        {
            var probe = parts.Length == 3 && string.Equals(parts[1], "instancing", StringComparison.OrdinalIgnoreCase);
            var disable = probe && string.Equals(parts[2], "off", StringComparison.OrdinalIgnoreCase);
            var restore = probe && string.Equals(parts[2], "restore", StringComparison.OrdinalIgnoreCase);
            if (parts.Length != 1 && !disable && !restore)
                return Finish(original, Result(true, false, TestCmdResultCode.UsageError, TerrainUsage), true);
            var map = this.GetModel<MapSceneModel>();
            var handle = map.CurrentHandle;
            if (map.Status.Value != MapLoadStatus.Loaded || handle == null || !handle.IsValid || handle.Context == null)
                return Finish(original, Result(true, false, TestCmdResultCode.StateRejected, "当前地图尚未就绪，无法诊断地形。"), true);
            try
            {
                Vector3? position = null;
                if (this.GetSystem<UnitSystem>().TryGet(this.GetModel<PlayerModel>().PlayerUnitId, out var player) &&
                    player.IsActive && player.MapInstanceId == this.GetSystem<MapSceneSystem>().CurrentMapInstanceId)
                    position = player.Position;
                var handles = handle is MapContentGroupHandle group ? group.Children : new[] { handle };
                var report = $"map={map.CurrentMapId} instance={this.GetSystem<MapSceneSystem>().CurrentMapInstanceId}\n";
                if (disable || restore)
                {
                    if (!ReferenceEquals(terrainProbeOwner, handle)) ClearTerrainProbe();
                    var currentTerrains = handles.SelectMany(child => child.Context.MapContentRoot
                        ? child.Context.MapContentRoot.GetComponentsInChildren<Terrain>(true)
                        : Array.Empty<Terrain>()).ToArray();
                    // 当前引擎在最小对照中表现出地形开关联动；其它地图地形存在时禁止改动。
                    if (disable && UnityEngine.Object.FindObjectsOfType<Terrain>(true).Any(terrain => !currentTerrains.Contains(terrain)))
                        return Finish(original, Result(true, false, TestCmdResultCode.StateRejected,
                            "存在当前地图之外的地形，为避免实例化开关联动，本次对照已拒绝。"), true);
                    terrainProbeOwner = handle;
                    var changed = 0;
                    // 先完整捕获，再写入开关，避免相邻地形联动覆盖后续地形的原值。
                    if (disable)
                        foreach (var terrain in currentTerrains)
                            if (!terrainInstancingBefore.ContainsKey(terrain))
                                terrainInstancingBefore.Add(terrain, terrain.drawInstanced);
                    foreach (var child in handles)
                    {
                        var root = child.Context.MapContentRoot;
                        if (!root) continue;
                        foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                        {
                            if (disable)
                            {
                                terrain.drawInstanced = false;
                                changed++;
                            }
                            else if (terrainInstancingBefore.TryGetValue(terrain, out var before))
                            {
                                terrain.drawInstanced = before;
                                changed++;
                            }
                        }
                    }
                    if (restore) ClearTerrainProbe();
                    report += $"InstancingProbe={(disable ? "off" : "restore")} terrains={changed}（仅本次地图运行态，不写存档）\n";
                }
                foreach (var child in handles)
                    report += TerrainDiagnosticReport.Build(child.Context.MapContentRoot, position);
                return Finish(original, Result(true, true, TestCmdResultCode.TerrainDiagnosticsComplete, report), true);
            }
            catch (Exception exception)
            {
                return Finish(original, Result(true, false, TestCmdResultCode.ExecutionFailed, $"地形诊断失败：{exception.GetType().Name}: {exception.Message}"), true);
            }
        }

        /// <summary>只返回打开日志界面的意图；具体 UI 切换由指令窗口 Controller 完成。</summary>
        private TestCmdExecutionResult ExecuteRuntimeLog(string original, string[] parts)
        {
            return parts.Length == 1
                ? Finish(original, Result(true, true, TestCmdResultCode.RuntimeLogOpenRequested, "正在打开运行日志。"), true)
                : Finish(original, Warning(TestCmdResultCode.UsageError, "用法：log"), true);
        }

        /// <summary>测试指令只提交进入意图；基地校验与附加场景句柄由造型库场景系统独占。</summary>
        private async Task<TestCmdExecutionResult> ExecuteGalleryAsync(string original, CancellationToken cancellationToken)
        {
            var (success, message) = await this.GetSystem<AppearanceGallerySceneFlowSystem>()
                .OpenAsync(cancellationToken);
            return Finish(original, Result(true, success,
                success ? TestCmdResultCode.AppearanceGalleryOpened : TestCmdResultCode.StateRejected,
                message), true);
        }

        private TestCmdExecutionResult ExecuteUnitHealthBars(string original, string[] parts)
        {
            if (parts.Length != 2 || !TryParseUnitHealthBarSwitch(parts[1], out var visible))
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, "用法：unitbar on / unitbar off"), true);
            }

            var model = this.GetModel<TestCmdModel>();
            if (model.SetUnitHealthBarsVisible(visible))
            {
                this.SendEvent(new UnitHealthBarVisibilityChangedEvent(visible));
            }

            return Finish(
                original,
                Result(
                    true,
                    true,
                    TestCmdResultCode.UnitHealthBarsChanged,
                    visible ? "敌人头顶生命条已开启" : "敌人头顶生命条已关闭"),
                true);
        }

        private TestCmdExecutionResult ExecuteNetworkDiagnostics(string original, string[] parts)
        {
            bool enabled;
            if (parts.Length == 2 &&
                string.Equals(parts[0], "netlog", StringComparison.OrdinalIgnoreCase) &&
                TryParseNetworkDiagnosticsSwitch(parts[1], out enabled))
            {
                return FinishNetworkDiagnostics(original, enabled);
            }

            if (parts.Length == 2 &&
                string.Equals(parts[0], "网络日志", StringComparison.Ordinal) &&
                TryParseNetworkDiagnosticsSwitch(parts[1], out enabled))
            {
                return FinishNetworkDiagnostics(original, enabled);
            }

            return Finish(original, Warning(TestCmdResultCode.UsageError, "用法：netlog on / netlog off"), true);
        }

        private TestCmdExecutionResult FinishNetworkDiagnostics(string original, bool enabled)
        {
            NetworkDiagnostics.SetEnabled(enabled);
            return Finish(
                original,
                Result(
                    true,
                    true,
                    TestCmdResultCode.NetworkDiagnosticsChanged,
                    enabled ? "网络日志已开启" : "网络日志已关闭"),
                true);
        }

        /// <summary>解析物品 ID 或名称及可选数量（默认 1）；输入无效时拒绝执行，发放仍委托正式背包事务。</summary>
        private TestCmdExecutionResult ExecuteClone(string original, string[] parts)
        {
            if (parts.Length < 2 || parts.Length > 3)
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageCloneText), true);
            }

            if (!IsDecimalIntegerShape(parts[1]))
            {
                var queryAmount = 1;
                if (parts.Length == 3 && (!TryParseStrictInt(parts[2], out queryAmount) || queryAmount <= 0))
                {
                    return Finish(original, Warning(TestCmdResultCode.InvalidAmount, InvalidAmountText), true);
                }

                return ExecuteCloneItemSearch(original, parts[1], queryAmount);
            }

            if (!TryParseStrictInt(parts[1], out var itemId) || itemId <= 0)
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageCloneText), true);
            }

            var amount = 1;
            if (parts.Length == 3 && (!TryParseStrictInt(parts[2], out amount) || amount <= 0))
            {
                return Finish(original, Warning(TestCmdResultCode.InvalidAmount, InvalidAmountText), true);
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _))
            {
                return Finish(original, Warning(TestCmdResultCode.ItemNotFound, ItemNotFoundText(parts[1])), true);
            }

            return ExecuteCloneTransaction(original, item, amount);
        }

        private TestCmdExecutionResult ExecuteCameraMode(string original, string[] parts)
        {
            if (parts.Length != 2)
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageCameraText), true);

            PlayerCameraMode mode;
            if (string.Equals(parts[1], "topdown", StringComparison.OrdinalIgnoreCase)) mode = PlayerCameraMode.TopDown;
            else if (string.Equals(parts[1], "shoulder", StringComparison.OrdinalIgnoreCase)) mode = PlayerCameraMode.Shoulder;
            else if (string.Equals(parts[1], "firstperson", StringComparison.OrdinalIgnoreCase)) mode = PlayerCameraMode.FirstPerson;
            else return Finish(original, Warning(TestCmdResultCode.UsageError, UsageCameraText), true);

            if (!this.GetSystem<PlayerCameraSystem>().TrySetMode(mode, out var error))
                return Finish(original, Warning(TestCmdResultCode.StateRejected, StateRejectedText(error)), true);

            return Finish(original, Result(true, true, TestCmdResultCode.CameraModeChanged,
                mode == PlayerCameraMode.TopDown ? "相机已切换为固定俯视" :
                mode == PlayerCameraMode.FirstPerson ? "相机已切换为第一人称试用视角" : "相机已切换为越肩视角"), true);
        }

        private TestCmdExecutionResult ExecuteMobileInputMode(string original, string[] parts)
        {
            if (parts.Length != 2 || !TryParseUnitHealthBarSwitch(parts[1], out var enabled))
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageMobileInputText), true);
            }

            if (!this.GetSystem<MobileInputSystem>().TrySetEditorSimulation(enabled))
            {
                return Finish(
                    original,
                    Warning(TestCmdResultCode.StateRejected, "mobileinput 仅允许在 Unity Editor Play 会话中使用。"),
                    true);
            }

            return Finish(
                original,
                Result(
                    true,
                    true,
                    TestCmdResultCode.MobileInputModeChanged,
                    enabled ? "手机输入模拟已开启" : "手机输入模拟已关闭"),
                true);
        }

        /// <summary>
        /// 解析本地单人街机测试意图；实际状态变化全部交给 ArcadeCombatSystem，避免指令层成为第二权威写入口。
        /// </summary>
        private TestCmdExecutionResult ExecuteArcade(string original, string[] parts)
        {
            if (parts.Length < 2)
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageArcadeText), true);

            var combat = this.GetSystem<ArcadeCombatSystem>();
            bool success;
            string message;
            string error;
            if (parts.Length == 2 && string.Equals(parts[1], "status", StringComparison.OrdinalIgnoreCase))
                success = combat.TryGetTestStatus(out message, out error);
            else if (parts.Length == 2 && string.Equals(parts[1], "heal", StringComparison.OrdinalIgnoreCase))
                success = combat.TryHealPlayerForTests(out message, out error);
            else if (parts.Length == 3 && string.Equals(parts[1], "damage", StringComparison.OrdinalIgnoreCase) &&
                     TryParseStrictInt(parts[2], out var damage) && damage > 0)
                success = combat.TryDamagePlayerForTests(damage, out message, out error);
            else if (parts.Length == 3 && string.Equals(parts[1], "gold", StringComparison.OrdinalIgnoreCase) &&
                     TryParseStrictInt(parts[2], out var gold) && gold > 0)
                success = combat.TryAddGoldForTests(gold, out message, out error);
            else if (parts.Length == 3 && string.Equals(parts[1], "bombs", StringComparison.OrdinalIgnoreCase) &&
                     TryParseStrictInt(parts[2], out var bombs) && bombs >= 0 && bombs <= 9999)
                success = combat.TrySetResourceForTests(true, bombs, out message, out error);
            else if (parts.Length == 3 && string.Equals(parts[1], "dashes", StringComparison.OrdinalIgnoreCase) &&
                     TryParseStrictInt(parts[2], out var dashes) && dashes >= 0 && dashes <= 9999)
                success = combat.TrySetResourceForTests(false, dashes, out message, out error);
            else if (parts.Length == 3 && string.Equals(parts[1], "drop", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(parts[2]))
                success = combat.TryApplyDropForTests(parts[2], out message, out error);
            else if (parts.Length == 3 && string.Equals(parts[1], "clear", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(parts[2], "wave", StringComparison.OrdinalIgnoreCase))
                success = combat.TryClearWaveForTests(out message, out error);
            else if (parts.Length == 3 && string.Equals(parts[1], "clear", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(parts[2], "arena", StringComparison.OrdinalIgnoreCase))
                success = combat.TryClearArenaForTests(out message, out error);
            else
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageArcadeText), true);

            return Finish(
                original,
                success
                    ? Result(true, true, TestCmdResultCode.ArcadeCommandComplete, message)
                    : Warning(TestCmdResultCode.StateRejected, StateRejectedText(error)),
                true);
        }

        private TestCmdExecutionResult ExecuteCloneTransaction(string original, ItemDefinition item, int amount)
        {
            var itemId = item.Id;
            var itemName = ResolveItemName(item, itemId);
            var inventory = this.GetSystem<InventorySystem>();
            if (!inventory.CanModifyPlayerBag(out var stateError, allowPaused: true))
            {
                return Finish(original, Warning(TestCmdResultCode.StateRejected, StateRejectedText(stateError)), true);
            }

            var snapshot = inventory.CapturePlayerBagSnapshot();
            try
            {
                if (!inventory.TryPreviewAddPlayerBagItem(itemId, amount, out var accepted, out _))
                {
                    inventory.RestorePlayerBagSnapshot(snapshot);
                    return Finish(original, Warning(TestCmdResultCode.BagFull, BagFullText), true);
                }

                if (accepted <= 0)
                {
                    inventory.RestorePlayerBagSnapshot(snapshot);
                    return Finish(original, Warning(TestCmdResultCode.BagFull, BagFullText), true);
                }

                inventory.CommitPlayerBagChanged("test command clone");
                var rejected = Math.Max(0, amount - accepted);
                return Finish(
                    original,
                    Result(
                        true,
                        true,
                        rejected == 0 ? TestCmdResultCode.CloneComplete : TestCmdResultCode.ClonePartial,
                        rejected == 0
                            ? CloneCompleteText(itemName, accepted)
                            : ClonePartialText(itemName, accepted, rejected),
                        accepted,
                        rejected,
                        affectedItemId: itemId),
                    true);
            }
            catch (Exception e)
            {
                inventory.RestorePlayerBagSnapshot(snapshot);
                Log.Error($"[TestCmdSystem] 克隆 失败: {e.GetType().Name}: {e.Message}");
                return Finish(
                    original,
                    Result(true, false, TestCmdResultCode.ExecutionFailed, ExecutionFailedText),
                    true,
                    logAlreadyWritten: true);
            }
        }

        private TestCmdExecutionResult ExecuteCloneItemSearch(string original, string query, int amount)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageCloneText), true);
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItems(out var items, out var error))
            {
                return Finish(original, Warning(TestCmdResultCode.ExecutionFailed, error), true);
            }

            var matches = items
                .Select(item => new
                {
                    Item = item,
                    Name = ResolveItemName(item, item.Id)
                })
                .Where(entry => ItemMatchesQuery(entry.Item, entry.Name, query))
                .OrderBy(entry => entry.Item.Id)
                .Take(20)
                .ToArray();

            if (matches.Length == 0)
            {
                return Finish(original, Warning(TestCmdResultCode.ItemNotFound, ItemSearchNotFoundText(query)), true);
            }

            if (matches.Length == 1)
            {
                return ExecuteCloneTransaction(original, matches[0].Item, amount);
            }

            return Finish(
                original,
                Result(
                    true,
                    true,
                    TestCmdResultCode.CloneComplete,
                    CloneSearchResultText(query, matches.Select(entry => $"{entry.Name}: {entry.Item.Id}"))),
                true);
        }

        private static bool IsDecimalIntegerShape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var index = value[0] == '+' || value[0] == '-' ? 1 : 0;
            if (index == value.Length)
            {
                return false;
            }

            for (; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private TestCmdExecutionResult ExecuteClearBag(string original, string[] parts)
        {
            if (parts.Length != 2 || !string.Equals(parts[1], "bag", StringComparison.OrdinalIgnoreCase))
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageClearBagText), true);
            }

            var inventory = this.GetSystem<InventorySystem>();
            if (!inventory.CanModifyPlayerBag(out var stateError, allowPaused: true))
            {
                return Finish(original, Warning(TestCmdResultCode.StateRejected, StateRejectedText(stateError)), true);
            }

            var snapshot = inventory.CapturePlayerBagSnapshot();
            var affected = snapshot.Slots.Values.Sum(stack => stack.Amount);
            try
            {
                inventory.ClearPlayerBag();
                if (affected > 0)
                {
                    inventory.CommitPlayerBagChanged("test command clear bag");
                }

                return Finish(
                    original,
                    Result(true, true, TestCmdResultCode.ClearBagComplete, ClearBagCompleteText(affected), affected),
                    true);
            }
            catch (Exception e)
            {
                inventory.RestorePlayerBagSnapshot(snapshot);
                Log.Error($"[TestCmdSystem] 清空背包 失败: {e.GetType().Name}: {e.Message}");
                return Finish(
                    original,
                    Result(true, false, TestCmdResultCode.ExecutionFailed, ExecutionFailedText),
                    true,
                    logAlreadyWritten: true);
            }
        }

        private TestCmdExecutionResult ExecuteAdvanceTime(string original, string[] parts)
        {
            if (parts.Length != 3 || !string.Equals(parts[1], "time", StringComparison.OrdinalIgnoreCase))
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageAdvanceTimeText), true);
            }

            if (!int.TryParse(parts[2], out var gameMinutes) || gameMinutes <= 0)
            {
                return Finish(original, Warning(TestCmdResultCode.InvalidGameMinutes, InvalidGameMinutesText), true);
            }

            if (!this.GetSystem<PlayerConditionSystem>().TryAdvanceGameMinutes(
                    gameMinutes,
                    out var result,
                    out var error))
            {
                return Finish(original, Warning(TestCmdResultCode.StateRejected, StateRejectedText(error)), true);
            }

            return Finish(
                original,
                Result(
                    true,
                    true,
                    TestCmdResultCode.AdvanceTimeComplete,
                    AdvanceTimeCompleteText(
                        FormatFloat(result.AdvancedGameMinutes),
                        FormatFloat(result.PowerDelta),
                        FormatFloat(result.WaterDelta),
                        result.PowerDepletionDamage,
                        result.WaterDepletionDamage),
                    Mathf.RoundToInt(result.AdvancedGameMinutes),
                    result.PowerDepletionDamage + result.WaterDepletionDamage),
                true);
        }

        private TestCmdExecutionResult ExecuteSetAttribute(string original, string[] parts)
        {
            if (parts.Length != 3)
            {
                return Finish(original, Warning(TestCmdResultCode.UsageError, UsageSetAttributeText), true);
            }

            var field = parts[1].ToLowerInvariant();
            var attribute = this.GetSystem<PlayerAttributeSystem>();
            PlayerAttributeChange change;
            string error;
            switch (field)
            {
                case "hp":
                    if (!TryParseStrictInt(parts[2], out var hp))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    if (hp < 0 || hp > attribute.EffectiveMaxHP)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetHP(hp, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                case "power":
                    if (!TryParseFiniteFloat(parts[2], out var power))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    if (power < 0f || power > PlayerConditionRules.MaxPower)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetPower(power, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                case "tili":
                    if (!TryParseFiniteFloat(parts[2], out var tili))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    if (tili < 0f || tili > PlayerConditionRules.MaxTili)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetTili(tili, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                case "maxhp":
                    if (!TryParseStrictInt(parts[2], out var maxHP))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    if (maxHP <= 0)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetMaxHP(maxHP, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                case "movespeed":
                    if (!TryParseFiniteFloat(parts[2], out var moveSpeed))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    if (moveSpeed <= 0f)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetMoveSpeed(moveSpeed, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                case "water":
                    if (!TryParseFiniteFloat(parts[2], out var water))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    var maxWater = this.GetModel<PlayerConditionModel>().MaxWater;
                    if (water < 0f || water > maxWater)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetWater(water, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                case "maxwater":
                    if (!TryParseFiniteFloat(parts[2], out var maxWaterValue))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    if (maxWaterValue <= 0f)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetMaxWater(maxWaterValue, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                case "temp":
                    if (!TryParseFiniteFloat(parts[2], out var temperature))
                    {
                        return Finish(original, Warning(TestCmdResultCode.InvalidAttributeValue, InvalidAttributeValueText), true);
                    }

                    if (temperature < PlayerConditionRules.MinTemperature ||
                        temperature > PlayerConditionRules.MaxTemperature)
                    {
                        return Finish(original, Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText), true);
                    }

                    if (!attribute.TrySetTemperature(temperature, out change, out error))
                    {
                        return Finish(original, AttributeFailure(attribute.LastFailureKind, error), true);
                    }

                    return FinishAttributeSet(original, change);
                default:
                    return Finish(original, Warning(TestCmdResultCode.UnknownAttribute, UnknownAttributeText(field)), true);
            }
        }

        private TestCmdExecutionResult Finish(
            string original,
            TestCmdExecutionResult result,
            bool recordHistory,
            bool logAlreadyWritten = false)
        {
            if (!logAlreadyWritten)
            {
                WriteResultLog(result);
            }

            return result;
        }

        private static TestCmdExecutionResult Warning(TestCmdResultCode code, string message)
        {
            return Result(true, false, code, message);
        }

        private static TestCmdExecutionResult AttributeFailure(PlayerAttributeFailureKind failureKind, string error)
        {
            switch (failureKind)
            {
                case PlayerAttributeFailureKind.OutOfRange:
                    return Warning(TestCmdResultCode.AttributeOutOfRange, AttributeOutOfRangeText);
                case PlayerAttributeFailureKind.ExecutionFailed:
                    return Result(true, false, TestCmdResultCode.ExecutionFailed, ExecutionFailedText);
                default:
                    return Warning(TestCmdResultCode.StateRejected, StateRejectedText(error));
            }
        }

        private static TestCmdExecutionResult Result(
            bool recognized,
            bool success,
            TestCmdResultCode code,
            string message,
            int affectedAmount = 0,
            int rejectedAmount = 0,
            string attributeId = null,
            double beforeValue = 0d,
            double afterValue = 0d,
            int affectedItemId = 0)
        {
            return new TestCmdExecutionResult(
                recognized,
                success,
                code,
                message,
                affectedAmount,
                rejectedAmount,
                attributeId,
                beforeValue,
                afterValue,
                affectedItemId: affectedItemId);
        }

        private TestCmdExecutionResult FinishAttributeSet(string original, PlayerAttributeChange change)
        {
            return Finish(
                original,
                Result(
                    true,
                    true,
                    TestCmdResultCode.AttributeSetComplete,
                    AttributeSetCompleteText(
                        change.AttributeId,
                        FormatDouble(change.Before),
                        FormatDouble(change.After)),
                    attributeId: change.AttributeId,
                    beforeValue: change.Before,
                    afterValue: change.After),
                true);
        }

        private static void WriteResultLog(TestCmdExecutionResult result)
        {
            var message = $"[TestCmd] {result.Message}";
            switch (result.Code)
            {
                case TestCmdResultCode.CloneComplete:
                case TestCmdResultCode.ClonePartial:
                case TestCmdResultCode.ClearBagComplete:
                case TestCmdResultCode.AdvanceTimeComplete:
                case TestCmdResultCode.AttributeSetComplete:
                case TestCmdResultCode.NetworkDiagnosticsChanged:
                case TestCmdResultCode.UnitHealthBarsChanged:
                case TestCmdResultCode.CameraModeChanged:
                case TestCmdResultCode.MobileInputModeChanged:
                case TestCmdResultCode.ArcadeCommandComplete:
                case TestCmdResultCode.AppearanceGalleryOpened:
                case TestCmdResultCode.RuntimeLogOpenRequested:
                case TestCmdResultCode.TerrainDiagnosticsComplete:
                case TestCmdResultCode.WeatherChanged:
                    Log.Debug(message);
                    break;
                case TestCmdResultCode.ExecutionFailed:
                    Log.Error(message);
                    break;
                default:
                    Log.Warning(message);
                    break;
            }
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string ItemNotFoundText(object itemId)
        {
            return $"未找到物品：{itemId}";
        }

        private static string StateRejectedText(object reason)
        {
            return $"当前状态不允许执行指令：{reason}";
        }

        private static string CloneCompleteText(object itemId, object accepted)
        {
            return $"获得{itemId}x{accepted}。";
        }

        private static string ClonePartialText(object itemId, object accepted, object rejected)
        {
            return $"获得{itemId}x{accepted}，未接收{rejected}。";
        }

        private static string ItemSearchNotFoundText(object query)
        {
            return $"未找到匹配道具：{query}";
        }

        private static string CloneSearchResultText(object query, System.Collections.Generic.IEnumerable<string> lines)
        {
            return $"通过模糊搜索“{query}”找到：\n{string.Join("\n", lines)}";
        }

        private static bool ItemMatchesQuery(ItemDefinition item, string localizedName, string query)
        {
            return Contains(localizedName, query) ||
                   Contains(item.Name, query) ||
                   Contains(item.NameKey, query) ||
                   Contains(item.Desc, query) ||
                   Contains(item.DescKey, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolveItemName(ItemDefinition item, int fallbackItemId)
        {
            if (!string.IsNullOrWhiteSpace(item.NameKey))
            {
                var localized = Loc.T(item.NameKey);
                if (!string.IsNullOrWhiteSpace(localized) && localized != item.NameKey)
                {
                    return localized;
                }
            }

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                return item.Name;
            }

            return fallbackItemId.ToString(CultureInfo.InvariantCulture);
        }

        private static string ClearBagCompleteText(object affected)
        {
            return $"已清空玩家背包，移除 {affected} 个物品。";
        }

        private static string AdvanceTimeCompleteText(object minutes, object powerDelta, object waterDelta, object powerDamage, object waterDamage)
        {
            return $"已推进 {minutes} 游戏分钟；能量变化 {powerDelta}；水分变化 {waterDelta}；能量耗尽伤害 {powerDamage}；缺水伤害 {waterDamage}。";
        }

        private static string UnknownAttributeText(object attributeId)
        {
            return $"未知玩家属性：{attributeId}";
        }

        private static string AttributeSetCompleteText(object attributeId, object before, object after)
        {
            return $"已设置 {attributeId}：{before} -> {after}。";
        }

        private static bool TryParseStrictInt(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value) || value.Contains("."))
            {
                return false;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseFiniteFloat(string value, out float result)
        {
            result = 0f;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                   !float.IsNaN(result) &&
                   !float.IsInfinity(result);
        }

        private static bool IsNetworkDiagnosticsCommand(string[] parts)
        {
            return parts.Length > 0 &&
                   (string.Equals(parts[0], "netlog", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parts[0], "网络日志", StringComparison.Ordinal));
        }

        private static bool TryParseNetworkDiagnosticsSwitch(string value, out bool enabled)
        {
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "开", StringComparison.Ordinal))
            {
                enabled = true;
                return true;
            }

            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "关", StringComparison.Ordinal))
            {
                enabled = false;
                return true;
            }

            enabled = false;
            return false;
        }

        private static bool TryParseUnitHealthBarSwitch(string value, out bool visible)
        {
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            {
                visible = true;
                return true;
            }

            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                visible = false;
                return true;
            }

            visible = false;
            return false;
        }
    }
}
