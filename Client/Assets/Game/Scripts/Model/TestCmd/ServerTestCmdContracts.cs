using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game
{
    public enum ServerTestCmdCatalogStatus
    {
        Available,
        NotConnected,
        Unavailable,
        Canceled,
    }

    public sealed class ServerTestCmdCategorySnapshot
    {
        public ServerTestCmdCategorySnapshot(string id, int order, string labelText)
        { Id = id ?? string.Empty; Order = order; LabelText = labelText ?? string.Empty; }
        public string Id { get; }
        public int Order { get; }
        public string LabelText { get; }
    }

    public sealed class ServerTestCmdEntrySnapshot
    {
        public ServerTestCmdEntrySnapshot(string id, string categoryId, int order, string labelText, string commandTemplate,
            string descriptionText, string usageText, string exampleText)
        {
            Id = id ?? string.Empty; CategoryId = categoryId ?? string.Empty; Order = order; LabelText = labelText ?? string.Empty;
            CommandTemplate = commandTemplate ?? string.Empty; DescriptionText = descriptionText ?? string.Empty;
            UsageText = usageText ?? string.Empty; ExampleText = exampleText ?? string.Empty;
        }
        public string Id { get; }
        public string CategoryId { get; }
        public int Order { get; }
        public string LabelText { get; }
        public string CommandTemplate { get; }
        public string DescriptionText { get; }
        public string UsageText { get; }
        public string ExampleText { get; }
    }

    public sealed class ServerTestCmdCatalogSnapshot
    {
        public ServerTestCmdCatalogSnapshot(ServerTestCmdCatalogStatus status, string statusText,
            IEnumerable<ServerTestCmdCategorySnapshot> categories = null, IEnumerable<ServerTestCmdEntrySnapshot> entries = null)
        {
            Status = status; StatusText = statusText ?? string.Empty;
            Categories = new ReadOnlyCollection<ServerTestCmdCategorySnapshot>(new List<ServerTestCmdCategorySnapshot>(categories ?? Array.Empty<ServerTestCmdCategorySnapshot>()));
            Entries = new ReadOnlyCollection<ServerTestCmdEntrySnapshot>(new List<ServerTestCmdEntrySnapshot>(entries ?? Array.Empty<ServerTestCmdEntrySnapshot>()));
        }
        public ServerTestCmdCatalogStatus Status { get; }
        public string StatusText { get; }
        public IReadOnlyList<ServerTestCmdCategorySnapshot> Categories { get; }
        public IReadOnlyList<ServerTestCmdEntrySnapshot> Entries { get; }
    }
}
