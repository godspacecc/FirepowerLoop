using System.Text;

namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    public sealed class C2G_GetServerTestCmdCatalogHandler: MessageSessionHandler<C2G_GetServerTestCmdCatalog, G2C_GetServerTestCmdCatalog>
    {
        protected override async ETTask Run(Session session, C2G_GetServerTestCmdCatalog request, G2C_GetServerTestCmdCatalog response)
        {
            await ETTask.CompletedTask;
            ServerTestCmdDispatcher dispatcher = ServerTestCmdDispatcher.Instance;
            if (!dispatcher.IsReady || dispatcher.Catalog == null)
            {
                response.Error = ErrorCore.ERR_RpcFail;
                response.Message = "服务器测试指令目录不可用";
                return;
            }

            foreach (ServerTestCmdCategoryDefinition category in dispatcher.Catalog.Categories)
            {
                ServerTestCmdCategoryInfo item = ServerTestCmdCategoryInfo.Create();
                item.Id = category.Id; item.Order = category.Order; item.LabelText = category.LabelText;
                response.Categories.Add(item);
            }
            foreach (ServerTestCmdDefinition definition in dispatcher.Catalog.Entries)
            {
                ServerTestCmdHandlerAttribute m = definition.Metadata;
                ServerTestCmdEntryInfo item = ServerTestCmdEntryInfo.Create();
                item.Id = m.Id; item.CategoryId = m.CategoryId; item.Order = m.Order; item.LabelText = m.LabelText;
                item.CommandTemplate = m.CommandTemplate; item.DescriptionText = m.DescriptionText;
                item.UsageText = m.UsageText; item.ExampleText = m.ExampleText;
                response.Entries.Add(item);
            }
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    public sealed class C2G_ExecuteServerTestCmdHandler: MessageSessionHandler<C2G_ExecuteServerTestCmd, G2C_ExecuteServerTestCmd>
    {
        protected override async ETTask Run(Session session, C2G_ExecuteServerTestCmd request, G2C_ExecuteServerTestCmd response)
        {
            if (!TryNormalize(request.CommandText, out string body))
            {
                Apply(response, new ServerTestCmdRunResult(false, false, ServerTestCmdResultCode.InvalidInput, "服务器指令输入无效"));
                return;
            }

            ServerTestCmdRunResult result = await ServerTestCmdDispatcher.Instance.Execute(session.Root(), session, body);
            Apply(response, result);
        }

        private static bool TryNormalize(string raw, out string value)
        {
            raw ??= string.Empty;
            foreach (char c in raw) if (char.IsControl(c)) { value = string.Empty; return false; }
            value = raw.Trim();
            if (value.Length is < 1 or > 512 || Encoding.UTF8.GetByteCount(value) > 1024) return false;
            return true;
        }

        private static void Apply(G2C_ExecuteServerTestCmd response, ServerTestCmdRunResult result)
        {
            response.Recognized = result.Recognized; response.Success = result.Success;
            response.ResultCode = (int)result.Code; response.Message = result.Message; response.ServerTime = result.ServerTime;
        }
    }
}
