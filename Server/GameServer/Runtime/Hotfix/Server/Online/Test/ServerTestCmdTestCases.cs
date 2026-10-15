using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [FriendOf(typeof(OnlineTestComponent))]
    public static partial class OnlineTestComponentSystem
    {
        private static async ETTask<int> RunServerTestCmd(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("SERVER-TCMD-DEFINITIONS", async () =>
            {
                ServerTestCmdDefinition a = Definition("server.a", "a", "server.general", 2);
                ServerTestCmdDefinition b = Definition("server.b", "b", "server.general", 1);
                ServerTestCmdCatalogDefinition catalog = ServerTestCmdDispatcher.ValidateDefinitions(new[] { a, b });
                Ensure(catalog.Entries[0].Metadata.Id == "server.b", "definition sorting is unstable");
                ExpectInvalid(new[] { a, Definition("server.a", "c", "server.general", 3) }, "duplicate id");
                ExpectInvalid(new[] { a, Definition("server.c", "A", "server.general", 3) }, "duplicate name");
                ExpectInvalid(new[] { Definition("server.x", "x", "bad", 1) }, "invalid category");
                ExpectInvalid(new[] { Definition("server.x", "x", "server.general", 1, "$wrong") }, "invalid template");
                ExpectInvalid(Enumerable.Range(0, 257).Select(i => Definition($"server.x{i}", $"x{i}", "server.general", i)), "entry limit");
                ExpectInvalid(Enumerable.Range(0, 33).Select(i => Definition($"server.x{i}", $"x{i}", $"server.c{i}", i)), "category limit");
                await ETTask.CompletedTask;
            });

            // 首个网络动作必须是匿名 Session 的目录请求。
            using Session session = self.CreateSession();
            failed += await self.Case("SERVER-TCMD-ANON-CATALOG", async () =>
            {
                using G2C_GetServerTestCmdCatalog response = await session.Call(C2G_GetServerTestCmdCatalog.Create()) as G2C_GetServerTestCmdCatalog;
                Ensure(response.Error == ErrorCode.ERR_Success, $"catalog error={response.Error}");
                Ensure(response.Categories.Any(x => x.Id == "server.general"), "server.general missing");
                Ensure(response.Categories.Any(x => x.Id == "server.arcade"), "server.arcade missing");
                Ensure(response.Entries.Any(x => x.CommandTemplate == "$ping"), "$ping missing");
                Ensure(response.Entries.Any(x => x.Id == "server.arcade" &&
                                                 x.CommandTemplate == "$arcade <子指令>"), "$arcade missing");
                Ensure(response.Entries.All(x => x.CommandTemplate != "$help"), "$help must not exist");
            });

            failed += await self.Case("SERVER-TCMD-ANON-PING", async () =>
            {
                long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                using C2G_ExecuteServerTestCmd request = C2G_ExecuteServerTestCmd.Create(); request.CommandText = "ping";
                using G2C_ExecuteServerTestCmd response = await session.Call(request) as G2C_ExecuteServerTestCmd;
                long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Ensure(response.Error == 0 && response.Recognized && response.Success && response.ResultCode == 1, "ping failed");
                Ensure(response.ServerTime >= before - 5000 && response.ServerTime <= after + 5000, $"invalid ServerTime={response.ServerTime}");
            });

            failed += await self.Case("SERVER-TCMD-ANON-ARCADE-DENIED", async () =>
            {
                using C2G_ExecuteServerTestCmd request = C2G_ExecuteServerTestCmd.Create();
                request.CommandText = "arcade status";
                using G2C_ExecuteServerTestCmd response = await session.Call(request) as G2C_ExecuteServerTestCmd;
                Ensure(response.Error == 0 && response.Recognized && !response.Success && response.ResultCode == 4,
                    "anonymous arcade command must be denied");
            });

            failed += await self.Case("SERVER-TCMD-BOUNDARIES", async () =>
            {
                await EnsureCode(session, string.Empty, 2);
                await EnsureCode(session, "ping\n", 2);
                await EnsureCode(session, new string('a', 513), 2);
                await EnsureCode(session, new string('测', 400), 2);
                await EnsureCode(session, "missing", 3);
                await EnsureCode(session, "ping extra", 2);
            });
            return failed;
        }

        private static async ETTask EnsureCode(Session session, string body, int code)
        {
            using C2G_ExecuteServerTestCmd request = C2G_ExecuteServerTestCmd.Create(); request.CommandText = body;
            using G2C_ExecuteServerTestCmd response = await session.Call(request) as G2C_ExecuteServerTestCmd;
            Ensure(response.Error == 0 && response.ResultCode == code, $"body boundary expected={code} actual={response.ResultCode}");
        }

        private static ServerTestCmdDefinition Definition(string id, string name, string category, int order, string template = null) =>
            new(new ServerTestCmdHandlerAttribute(id, name, category, 0, "分类", order, id, template ?? "$" + name, "说明", "$" + name, "$" + name), new DummyHandler());

        private static void ExpectInvalid(IEnumerable<ServerTestCmdDefinition> values, string name)
        {
            try { ServerTestCmdDispatcher.ValidateDefinitions(values); }
            catch (InvalidOperationException) { return; }
            throw new Exception($"{name} should fail");
        }

        [EnableClass]
        private sealed class DummyHandler: IServerTestCmdHandler
        {
            public async ETTask<ServerTestCmdRunResult> Run(Scene root, Session session, string argumentText)
            { await ETTask.CompletedTask; return new ServerTestCmdRunResult(true, true, ServerTestCmdResultCode.Success, "ok"); }
        }
    }
}
