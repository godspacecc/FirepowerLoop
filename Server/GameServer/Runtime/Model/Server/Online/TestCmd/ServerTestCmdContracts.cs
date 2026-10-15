using System;

namespace ET.Server
{
    public enum ServerTestCmdResultCode
    {
        None = 0,
        Success = 1,
        InvalidInput = 2,
        UnknownCommand = 3,
        ExecutionFailed = 4,
    }

    public readonly struct ServerTestCmdRunResult
    {
        public ServerTestCmdRunResult(bool recognized, bool success, ServerTestCmdResultCode code, string message, long serverTime = 0)
        {
            this.Recognized = recognized;
            this.Success = success;
            this.Code = code;
            this.Message = message ?? string.Empty;
            this.ServerTime = serverTime;
        }

        public bool Recognized { get; }
        public bool Success { get; }
        public ServerTestCmdResultCode Code { get; }
        public string Message { get; }
        public long ServerTime { get; }
    }

    public interface IServerTestCmdHandler
    {
        ETTask<ServerTestCmdRunResult> Run(Scene root, Session session, string argumentText);
    }

    public sealed class ServerTestCmdHandlerAttribute: BaseAttribute
    {
        public ServerTestCmdHandlerAttribute(string id, string commandName, string categoryId, int categoryOrder,
            string categoryLabelText, int order, string labelText, string commandTemplate, string descriptionText,
            string usageText, string exampleText)
        {
            this.Id = id;
            this.CommandName = commandName;
            this.CategoryId = categoryId;
            this.CategoryOrder = categoryOrder;
            this.CategoryLabelText = categoryLabelText;
            this.Order = order;
            this.LabelText = labelText;
            this.CommandTemplate = commandTemplate;
            this.DescriptionText = descriptionText;
            this.UsageText = usageText;
            this.ExampleText = exampleText;
        }

        public string Id { get; }
        public string CommandName { get; }
        public string CategoryId { get; }
        public int CategoryOrder { get; }
        public string CategoryLabelText { get; }
        public int Order { get; }
        public string LabelText { get; }
        public string CommandTemplate { get; }
        public string DescriptionText { get; }
        public string UsageText { get; }
        public string ExampleText { get; }
    }

    [EnableClass]
    public sealed class ServerTestCmdDefinition
    {
        public ServerTestCmdDefinition(ServerTestCmdHandlerAttribute metadata, IServerTestCmdHandler handler)
        {
            this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            this.Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public ServerTestCmdHandlerAttribute Metadata { get; }
        public IServerTestCmdHandler Handler { get; }
    }

    [EnableClass]
    public sealed class ServerTestCmdCategoryDefinition
    {
        public ServerTestCmdCategoryDefinition(string id, int order, string labelText)
        {
            this.Id = id;
            this.Order = order;
            this.LabelText = labelText;
        }

        public string Id { get; }
        public int Order { get; }
        public string LabelText { get; }
    }

    [EnableClass]
    public sealed class ServerTestCmdCatalogDefinition
    {
        public ServerTestCmdCatalogDefinition(ServerTestCmdCategoryDefinition[] categories, ServerTestCmdDefinition[] entries)
        {
            this.Categories = Array.AsReadOnly((ServerTestCmdCategoryDefinition[])categories.Clone());
            this.Entries = Array.AsReadOnly((ServerTestCmdDefinition[])entries.Clone());
        }

        public System.Collections.Generic.IReadOnlyList<ServerTestCmdCategoryDefinition> Categories { get; }
        public System.Collections.Generic.IReadOnlyList<ServerTestCmdDefinition> Entries { get; }
    }
}
