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
        private const string ExecutionFailedText = "指令执行失败。";
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
                result = ExecuteClientBody(body);
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



            if (string.Equals(parts[0], "unitbar", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteUnitHealthBars(trimmed, parts);
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

        private static string StateRejectedText(object reason)
        {
            return $"当前状态不允许执行指令：{reason}";
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
