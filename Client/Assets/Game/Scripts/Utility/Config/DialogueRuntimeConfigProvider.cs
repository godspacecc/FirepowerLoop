using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    public sealed partial class LubanRuntimeConfigProvider
    {
        /// <summary>无绑定时成功返回 null，保留旧 NPC 的直接功能入口；损坏配置明确失败。</summary>
        public bool TryGetNpcDialogue(string npcId, out DialogueDefinition value, out string error)
        {
            value = null;
            if (!EnsureValid(out error)) return false;
            if (!TryBuildDialogueCatalog(Tables, out var catalog, out error)) return false;
            catalog.TryGetValue(npcId, out value);
            return true;
        }

        private static bool TryBuildDialogueCatalog(global::Tables tables, out Dictionary<string, DialogueDefinition> values, out string error)
        {
            values = new Dictionary<string, DialogueDefinition>(StringComparer.Ordinal);
            try
            {
                foreach (var entry in tables.TbDialogueEntry.DataList)
                {
                    if (tables.TbNpc.GetOrDefault(entry.NpcId) == null) throw new ArgumentException("对话 NPC 不存在：" + entry.NpcId);
                    var nodes = tables.TbDialogueNode.DataList.Where(x => x.DialogueId == entry.Id).Select(node =>
                        new DialogueNodeDefinition(node.Id, node.TextKey, node.NextNodeId,
                            tables.TbDialogueOption.DataList.Where(x => x.NodeId == node.Id).Select(option =>
                                new DialogueOptionDefinition(option.Id, option.Order, option.TextKey, option.Action, option.TargetNodeId))));
                    values.Add(entry.NpcId, new DialogueDefinition(entry.Id, entry.NpcId, entry.StartNodeId, nodes));
                }
                foreach (var node in tables.TbDialogueNode.DataList)
                    if (tables.TbDialogueEntry.GetOrDefault(node.DialogueId) == null) throw new ArgumentException("孤立对话节点：" + node.Id);
                foreach (var option in tables.TbDialogueOption.DataList)
                    if (tables.TbDialogueNode.GetOrDefault(option.NodeId) == null) throw new ArgumentException("孤立对话选项：" + option.Id);
                error = null; return true;
            }
            catch (ArgumentException exception)
            {
                values.Clear(); error = "对话配置非法：" + exception.Message; return false;
            }
        }
    }
}
