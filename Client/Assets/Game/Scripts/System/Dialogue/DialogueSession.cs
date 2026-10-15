using System;

namespace Game
{
    /// <summary>一行台词的不可变快照；Revision 随每次推进变化，拒绝旧按钮重复提交。</summary>
    public sealed class DialogueSnapshot
    {
        public DialogueSnapshot(long sessionId, long revision, int npcUnitId, long mapInstanceId, string nameKey, DialogueNodeDefinition node)
        { SessionId = sessionId; Revision = revision; NpcUnitId = npcUnitId; MapInstanceId = mapInstanceId; NameKey = nameKey; Node = node; }
        public long SessionId { get; }
        public long Revision { get; }
        public int NpcUnitId { get; }
        public long MapInstanceId { get; }
        public string NameKey { get; }
        public DialogueNodeDefinition Node { get; }
    }

    /// <summary>会话节点状态机，由 DialogueSystem 唯一持有；不访问 Unity、执行功能或写存档。</summary>
    public sealed class DialogueSession
    {
        private DialogueDefinition definition;
        private long generation;
        private long revision;
        public DialogueSnapshot Current { get; private set; }

        /// <summary>只有空闲会话允许开始；代次跨关闭保留，防止旧会话点击命中新会话。</summary>
        public bool TryBegin(DialogueDefinition graph, int npcUnitId, long mapInstanceId, string nameKey)
        {
            if (Current != null || graph == null || npcUnitId <= 0 || mapInstanceId <= 0 || string.IsNullOrWhiteSpace(nameKey)) return false;
            definition = graph; generation++; revision = 1;
            Current = new DialogueSnapshot(generation, revision, npcUnitId, mapInstanceId, nameKey, graph.GetNode(graph.StartNodeId));
            return true;
        }

        /// <summary>空 optionId 表示继续；先校验当前行身份，再原子推进或关闭，失败不改变状态。</summary>
        public bool TryChoose(long sessionId, long expectedRevision, string optionId, out global::DialogueOptionAction action)
        {
            action = default;
            var current = Current;
            if (current == null || current.SessionId != sessionId || current.Revision != expectedRevision) return false;
            string next = null;
            if (string.IsNullOrEmpty(optionId))
            {
                if (current.Node.Options.Count > 0) return false;
                next = current.Node.NextNodeId;
                action = string.IsNullOrEmpty(next) ? global::DialogueOptionAction.End : global::DialogueOptionAction.GoToNode;
            }
            else
            {
                DialogueOptionDefinition selected = null;
                foreach (var option in current.Node.Options) if (option.Id == optionId) selected = option;
                if (selected == null) return false;
                action = selected.Action; next = selected.TargetNodeId;
            }
            if (action == global::DialogueOptionAction.GoToNode)
                Current = new DialogueSnapshot(generation, ++revision, current.NpcUnitId, current.MapInstanceId, current.NameKey, definition.GetNode(next));
            else Close(sessionId);
            return true;
        }

        /// <summary>只关闭匹配代次；过期 UI 卸载不能结束后来打开的会话。</summary>
        public bool Close(long sessionId)
        {
            if (Current == null || Current.SessionId != sessionId) return false;
            Current = null; definition = null; return true;
        }
    }
}
