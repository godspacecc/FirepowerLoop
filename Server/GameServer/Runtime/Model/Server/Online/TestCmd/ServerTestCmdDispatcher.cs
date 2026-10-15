using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [Code]
    public sealed class ServerTestCmdDispatcher: Singleton<ServerTestCmdDispatcher>, ISingletonAwake
    {
        private Dictionary<string, ServerTestCmdDefinition> handlers = new(StringComparer.OrdinalIgnoreCase);

        public bool IsReady { get; private set; }
        public ServerTestCmdCatalogDefinition Catalog { get; private set; }

        public void Awake()
        {
            try
            {
                List<ServerTestCmdDefinition> definitions = new();
                foreach (Type type in CodeTypes.Instance.GetTypes(typeof(ServerTestCmdHandlerAttribute)))
                {
                    ServerTestCmdHandlerAttribute metadata = type.GetCustomAttributes(typeof(ServerTestCmdHandlerAttribute), false)
                            .Cast<ServerTestCmdHandlerAttribute>().FirstOrDefault();
                    if (metadata == null)
                    {
                        continue;
                    }

                    if (Activator.CreateInstance(type) is not IServerTestCmdHandler handler)
                    {
                        throw new InvalidOperationException($"服务器测试指令处理器未实现接口: {type.FullName}");
                    }

                    definitions.Add(new ServerTestCmdDefinition(metadata, handler));
                }

                this.Catalog = ValidateDefinitions(definitions);
                this.handlers = this.Catalog.Entries.ToDictionary(x => x.Metadata.CommandName, StringComparer.OrdinalIgnoreCase);
                this.IsReady = true;
            }
            catch (Exception exception)
            {
                this.IsReady = false;
                this.Catalog = null;
                this.handlers.Clear();
                Log.Error($"[ServerTestCmdDispatcher] 初始化失败: {exception}");
            }
        }

        public static ServerTestCmdCatalogDefinition ValidateDefinitions(IEnumerable<ServerTestCmdDefinition> source)
        {
            ServerTestCmdDefinition[] definitions = (source ?? throw new ArgumentNullException(nameof(source))).ToArray();
            if (definitions.Length > 256)
            {
                throw new InvalidOperationException($"服务器测试指令数量超限: {definitions.Length}");
            }

            HashSet<string> ids = new(StringComparer.Ordinal);
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ServerTestCmdCategoryDefinition> categories = new(StringComparer.Ordinal);
            foreach (ServerTestCmdDefinition definition in definitions)
            {
                ServerTestCmdHandlerAttribute value = definition?.Metadata ?? throw new InvalidOperationException("服务器测试指令定义为空");
                Require(value.Id, "稳定 ID");
                Require(value.CommandName, $"{value.Id} 命令名");
                Require(value.CategoryId, $"{value.Id} 分类 ID");
                Require(value.CategoryLabelText, $"{value.Id} 分类显示文本");
                Require(value.LabelText, $"{value.Id} 显示文本");
                Require(value.CommandTemplate, $"{value.Id} 模板");
                Require(value.DescriptionText, $"{value.Id} 说明");
                Require(value.UsageText, $"{value.Id} 用法");
                Require(value.ExampleText, $"{value.Id} 示例");
                if (!ids.Add(value.Id)) throw new InvalidOperationException($"服务器测试指令稳定 ID 重复: {value.Id}");
                if (!names.Add(value.CommandName)) throw new InvalidOperationException($"服务器测试指令命令名重复: {value.CommandName}");
                if (!value.CategoryId.StartsWith("server.", StringComparison.Ordinal))
                    throw new InvalidOperationException($"服务器测试指令分类 ID 非法: {value.CategoryId}");
                if (!IsCommandName(value.CommandName)) throw new InvalidOperationException($"服务器测试指令命令名非法: {value.CommandName}");
                string firstToken = value.CommandTemplate.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.Equals(firstToken, "$" + value.CommandName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"服务器测试指令模板非法: {value.Id}");

                if (categories.TryGetValue(value.CategoryId, out ServerTestCmdCategoryDefinition category))
                {
                    if (category.Order != value.CategoryOrder || !string.Equals(category.LabelText, value.CategoryLabelText, StringComparison.Ordinal))
                        throw new InvalidOperationException($"服务器测试指令分类定义冲突: {value.CategoryId}");
                }
                else
                {
                    categories.Add(value.CategoryId, new ServerTestCmdCategoryDefinition(value.CategoryId, value.CategoryOrder, value.CategoryLabelText));
                }
            }

            if (categories.Count > 32) throw new InvalidOperationException($"服务器测试指令分类数量超限: {categories.Count}");
            return new ServerTestCmdCatalogDefinition(
                categories.Values.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(),
                definitions.OrderBy(x => x.Metadata.CategoryOrder).ThenBy(x => x.Metadata.CategoryId, StringComparer.Ordinal)
                    .ThenBy(x => x.Metadata.Order).ThenBy(x => x.Metadata.Id, StringComparer.Ordinal).ToArray());
        }

        public async ETTask<ServerTestCmdRunResult> Execute(Scene root, Session session, string body)
        {
            if (!this.IsReady) return new ServerTestCmdRunResult(false, false, ServerTestCmdResultCode.ExecutionFailed, "服务器测试指令不可用");
            string normalized = (body ?? string.Empty).Trim();
            int separator = normalized.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            string name = separator < 0 ? normalized : normalized[..separator];
            string arguments = separator < 0 ? string.Empty : normalized[(separator + 1)..].Trim();
            if (!this.handlers.TryGetValue(name, out ServerTestCmdDefinition definition))
                return new ServerTestCmdRunResult(false, false, ServerTestCmdResultCode.UnknownCommand, "未知服务器指令");

            try
            {
                return await definition.Handler.Run(root, session, arguments);
            }
            catch (Exception exception)
            {
                Log.Error($"[ServerTestCmdDispatcher] Handler={definition.Handler.GetType().FullName} exception={exception}");
                return new ServerTestCmdRunResult(true, false, ServerTestCmdResultCode.ExecutionFailed, "服务器指令执行失败");
            }
        }

        private static void Require(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"服务器测试指令{field}为空");
        }

        private static bool IsCommandName(string value)
        {
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            return value.Length > 0;
        }
    }
}
