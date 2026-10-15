using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>只读对话选项；动作只允许节点跳转、退出或当前 NPC 的既有功能。</summary>
    public sealed class DialogueOptionDefinition
    {
        public DialogueOptionDefinition(string id, int order, string textKey, global::DialogueOptionAction action, string targetNodeId)
        { Id = id; Order = order; TextKey = textKey; Action = action; TargetNodeId = targetNodeId ?? string.Empty; }
        public string Id { get; }
        public int Order { get; }
        public string TextKey { get; }
        public global::DialogueOptionAction Action { get; }
        public string TargetNodeId { get; }
    }

    /// <summary>不可变台词节点；下一节点与选项互斥，两者为空表示结束。</summary>
    public sealed class DialogueNodeDefinition
    {
        public DialogueNodeDefinition(string id, string textKey, string nextNodeId, IEnumerable<DialogueOptionDefinition> options)
        {
            Id = id; TextKey = textKey; NextNodeId = nextNodeId ?? string.Empty;
            var entries = (options ?? Array.Empty<DialogueOptionDefinition>()).ToArray();
            if (entries.Any(x => x == null)) throw new ArgumentException("对话选项不能为空");
            Options = Array.AsReadOnly(entries.OrderBy(x => x.Order).ToArray());
        }
        public string Id { get; }
        public string TextKey { get; }
        public string NextNodeId { get; }
        public IReadOnlyList<DialogueOptionDefinition> Options { get; }
    }

    /// <summary>校验后的只读对话图；允许玩家主动选择形成循环，不执行自动剧情或奖励。</summary>
    public sealed class DialogueDefinition
    {
        private readonly Dictionary<string, DialogueNodeDefinition> nodes;
        public DialogueDefinition(string id, string npcId, string startNodeId, IEnumerable<DialogueNodeDefinition> values)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(startNodeId))
                throw new ArgumentException("对话入口身份或起始节点为空");
            Id = id; NpcId = npcId; StartNodeId = startNodeId;
            nodes = new Dictionary<string, DialogueNodeDefinition>(StringComparer.Ordinal);
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in values ?? Array.Empty<DialogueNodeDefinition>())
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.TextKey) || nodes.ContainsKey(node.Id))
                    throw new ArgumentException("对话节点缺失文本或身份重复");
                if (node.Options.Count > 0 && !string.IsNullOrEmpty(node.NextNodeId)) throw new ArgumentException("节点不能同时有顺序推进与选项");
                var orders = new HashSet<int>();
                foreach (var option in node.Options)
                    if (option == null || string.IsNullOrWhiteSpace(option.Id) || string.IsNullOrWhiteSpace(option.TextKey) ||
                        !optionIds.Add(option.Id) || option.Order < 0 || !orders.Add(option.Order) ||
                        !Enum.IsDefined(typeof(global::DialogueOptionAction), option.Action) ||
                        (option.Action == global::DialogueOptionAction.GoToNode) != !string.IsNullOrWhiteSpace(option.TargetNodeId))
                        throw new ArgumentException("对话选项身份、排序、动作或目标非法");
                nodes.Add(node.Id, node);
            }
            if (!nodes.ContainsKey(startNodeId)) throw new ArgumentException("对话起始节点不存在");
            foreach (var node in nodes.Values)
            {
                if (!string.IsNullOrEmpty(node.NextNodeId) && !nodes.ContainsKey(node.NextNodeId)) throw new ArgumentException("顺序节点引用不存在");
                foreach (var option in node.Options)
                    if (option.Action == global::DialogueOptionAction.GoToNode && !nodes.ContainsKey(option.TargetNodeId)) throw new ArgumentException("选项目标引用不存在");
            }
            var reachable = new HashSet<string>();
            var pending = new Stack<string>(); pending.Push(startNodeId);
            while (pending.Count > 0)
            {
                var current = pending.Pop(); if (!reachable.Add(current)) continue;
                var node = nodes[current];
                if (!string.IsNullOrEmpty(node.NextNodeId)) pending.Push(node.NextNodeId);
                foreach (var option in node.Options)
                    if (option.Action == global::DialogueOptionAction.GoToNode) pending.Push(option.TargetNodeId);
            }
            if (reachable.Count != nodes.Count) throw new ArgumentException("对话包含不可到达节点");
        }
        public string Id { get; }
        public string NpcId { get; }
        public string StartNodeId { get; }
        public IEnumerable<string> TextKeys => nodes.Values.SelectMany(node => new[] { node.TextKey }.Concat(node.Options.Select(option => option.TextKey)));
        public DialogueNodeDefinition GetNode(string id) => nodes[id];
    }
}
