using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using QFramework;

namespace Game
{
    public sealed class ServerTestCmdSystem: AbstractSystem
    {
        private const int RpcTimeoutMilliseconds = 10000;
        private INetworkRuntime network;
        private CancellationTokenSource catalogCancellation;
        private CancellationTokenSource executeCancellation;
        private int catalogGeneration;
        private int executeGeneration;

        protected override void OnInit() => network = this.GetUtility<INetworkRuntime>();
        protected override void OnDeinit() { Cancel(ref catalogCancellation, ref catalogGeneration); Cancel(ref executeCancellation, ref executeGeneration); }

        public async Task<ServerTestCmdCatalogSnapshot> RefreshCatalogAsync(CancellationToken cancellationToken)
        {
            if (network == null || network.ConnectionState != NetworkConnectionState.Connected)
                return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.NotConnected, "服务器未连接");

            int generation = Start(ref catalogCancellation, ref catalogGeneration, cancellationToken, out CancellationToken token);
            try
            {
                G2C_GetServerTestCmdCatalog response = await network.CallAsync<G2C_GetServerTestCmdCatalog>(C2G_GetServerTestCmdCatalog.Create(), RpcTimeoutMilliseconds, token);
                if (generation != catalogGeneration) return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.Canceled, "目录请求已取消");
                if (response == null || response.Error != 0) return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.Unavailable, "服务器目录获取失败");

                List<ServerTestCmdCategorySnapshot> categories = (response.Categories ?? new List<ServerTestCmdCategoryInfo>())
                    .Select(x => new ServerTestCmdCategorySnapshot(x.Id, x.Order, x.LabelText)).ToList();
                List<ServerTestCmdEntrySnapshot> entries = (response.Entries ?? new List<ServerTestCmdEntryInfo>())
                    .Select(x => new ServerTestCmdEntrySnapshot(x.Id, x.CategoryId, x.Order, x.LabelText, x.CommandTemplate,
                        x.DescriptionText, x.UsageText, x.ExampleText)).ToList();
                if (!Validate(categories, entries)) return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.Unavailable, "服务器目录无效");
                Dictionary<string, ServerTestCmdCategorySnapshot> categoryById = categories.ToDictionary(x => x.Id, StringComparer.Ordinal);
                return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.Available, string.Empty,
                    categories.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal),
                    entries.OrderBy(x => categoryById[x.CategoryId].Order).ThenBy(x => x.CategoryId, StringComparer.Ordinal)
                        .ThenBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal));
            }
            catch (OperationCanceledException) { return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.Canceled, "目录请求已取消"); }
            catch (TimeoutException) { return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.Unavailable, "服务器目录获取超时"); }
            catch (Exception) { return new ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus.Unavailable,
                network == null || network.ConnectionState != NetworkConnectionState.Connected ? "服务器未连接" : "服务器目录获取失败"); }
            finally { End(ref catalogCancellation, generation, catalogGeneration); }
        }

        public async Task<TestCmdExecutionResult> ExecuteAsync(string body, CancellationToken cancellationToken)
        {
            if (network == null || network.ConnectionState != NetworkConnectionState.Connected)
                return Result(false, false, TestCmdResultCode.ServerNotConnected, "服务器未连接");
            string normalized = (body ?? string.Empty).Trim();
            if (!IsValid(normalized)) return Result(false, false, TestCmdResultCode.ServerExecutionFailed, "服务器指令输入无效");

            int generation = Start(ref executeCancellation, ref executeGeneration, cancellationToken, out CancellationToken token);
            try
            {
                C2G_ExecuteServerTestCmd request = C2G_ExecuteServerTestCmd.Create(); request.CommandText = normalized;
                G2C_ExecuteServerTestCmd response = await network.CallAsync<G2C_ExecuteServerTestCmd>(request, RpcTimeoutMilliseconds, token);
                if (generation != executeGeneration) return Result(false, false, TestCmdResultCode.Canceled, "指令已取消");
                if (response == null || response.Error != 0) return Result(false, false, TestCmdResultCode.ServerExecutionFailed, "服务器指令执行失败");
                TestCmdResultCode code = response.ResultCode switch
                {
                    1 when response.Success => TestCmdResultCode.ServerCommandComplete,
                    2 => TestCmdResultCode.ServerExecutionFailed,
                    3 => TestCmdResultCode.ServerUnknownCommand,
                    4 => TestCmdResultCode.ServerExecutionFailed,
                    _ => TestCmdResultCode.ServerExecutionFailed,
                };
                bool success = code == TestCmdResultCode.ServerCommandComplete && response.Success;
                return new TestCmdExecutionResult(response.Recognized, success, code,
                    string.IsNullOrWhiteSpace(response.Message) ? (success ? "服务器指令执行完成" : "服务器指令执行失败") : response.Message,
                    serverTime: response.ServerTime);
            }
            catch (OperationCanceledException) { return Result(false, false, TestCmdResultCode.Canceled, "指令已取消"); }
            catch (TimeoutException) { return Result(false, false, TestCmdResultCode.ServerTimeout, "服务器指令超时"); }
            catch (Exception) { return Result(false, false,
                network == null || network.ConnectionState != NetworkConnectionState.Connected ? TestCmdResultCode.ServerDisconnected : TestCmdResultCode.ServerExecutionFailed,
                network == null || network.ConnectionState != NetworkConnectionState.Connected ? "服务器连接已断开" : "服务器指令执行失败"); }
            finally { End(ref executeCancellation, generation, executeGeneration); }
        }

        private static bool Validate(IReadOnlyList<ServerTestCmdCategorySnapshot> categories, IReadOnlyList<ServerTestCmdEntrySnapshot> entries)
        {
            if (categories.Count > 32 || entries.Count > 256) return false;
            HashSet<string> categoryIds = new(StringComparer.Ordinal);
            foreach (var x in categories) if (string.IsNullOrWhiteSpace(x.Id) || !x.Id.StartsWith("server.", StringComparison.Ordinal) || !categoryIds.Add(x.Id)) return false;
            HashSet<string> entryIds = new(StringComparer.Ordinal);
            foreach (var x in entries) if (string.IsNullOrWhiteSpace(x.Id) || !entryIds.Add(x.Id) || !categoryIds.Contains(x.CategoryId) || !x.CommandTemplate.StartsWith("$", StringComparison.Ordinal)) return false;
            return true;
        }

        private static bool IsValid(string value)
        {
            if (value.Length is < 1 or > 512 || Encoding.UTF8.GetByteCount(value) > 1024) return false;
            foreach (char c in value) if (char.IsControl(c)) return false;
            return true;
        }
        private static TestCmdExecutionResult Result(bool recognized, bool success, TestCmdResultCode code, string message) => new(recognized, success, code, message);
        private static int Start(ref CancellationTokenSource current, ref int generation, CancellationToken outer, out CancellationToken token)
        { current?.Cancel(); current?.Dispose(); current = CancellationTokenSource.CreateLinkedTokenSource(outer); token = current.Token; return ++generation; }
        private static void End(ref CancellationTokenSource current, int observed, int actual) { if (observed == actual) { current?.Dispose(); current = null; } }
        private static void Cancel(ref CancellationTokenSource current, ref int generation) { current?.Cancel(); current?.Dispose(); current = null; generation++; }
    }
}
