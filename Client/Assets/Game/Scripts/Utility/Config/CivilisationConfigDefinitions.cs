using System.Collections.Generic;

namespace Game
{
    /// <summary>文明远征只读配置集合；数值与坐标来自 Luban，集合不持有玩家状态。</summary>
    public sealed class CivilisationCatalog
    {
        public IReadOnlyList<CivilisationTechnologyDefinition> Technologies { get; }
        public IReadOnlyList<CivilisationProductionDefinition> Productions { get; }
        public IReadOnlyDictionary<int, int> ReturnValues { get; }
        public IReadOnlyList<CivilisationSearchPointDefinition> Points { get; }
        public CivilisationCatalog(IReadOnlyList<CivilisationTechnologyDefinition> tech, IReadOnlyList<CivilisationProductionDefinition> production,
            IReadOnlyDictionary<int, int> values, IReadOnlyList<CivilisationSearchPointDefinition> points)
        { Technologies = tech; Productions = production; ReturnValues = values; Points = points; }
    }
    public sealed class CivilisationTechnologyDefinition
    {
        public string Id { get; }
        public string NameKey { get; }
        public string UnlockKey { get; }
        public CivilisationTechnologyDefinition(string id, string name, string unlock) { Id = id; NameKey = name; UnlockKey = unlock; }
    }
    public sealed class CivilisationProductionDefinition
    {
        public string Id { get; }
        public string TechnologyId { get; }
        public int OutputItemId { get; }
        public int CostGold { get; }
        public CivilisationProductionDefinition(string id, string tech, int item, int cost) { Id = id; TechnologyId = tech; OutputItemId = item; CostGold = cost; }
    }
    public sealed class CivilisationSearchPointDefinition
    {
        public string Id { get; }
        public string MapId { get; }
        public string TechnologyId { get; }
        public int ItemId { get; }
        /// <summary>剧情凭证关联的稳定章节 ID；空字符串表示普通搜索，成功返程才提交永久完成。</summary>
        public string ChapterId { get; }
        public int Amount { get; }
        /// <summary>完成调查所需现实秒数。</summary>
        public float Seconds { get; }
        public UnityEngine.Vector3 Position { get; }
        public string NameKey { get; }
        public CivilisationSearchPointDefinition(string id, string map, string technology, int item, int amount, float seconds, float x, float z, string name)
            : this(id, map, technology, item, amount, seconds, x, z, name, 0, false) { }
        /// <summary>采集要求的已装备近战武器 ID；0 表示无需工具。工具不在采集时消费。</summary>
        public int RequiredWeaponItemId { get; }
        /// <summary>成功采集后隐藏绑定树身；仅影响当前远征地图，记录通过稳定点 ID 保存。</summary>
        public bool FellTree { get; }
        /// <summary>可重复采集的环境资源；不写入一次性访问记录，不隐藏资源表现。默认 false 保持旧采集语义。</summary>
        public bool Repeatable { get; }
        public CivilisationSearchPointDefinition(string id, string map, string technology, int item, int amount, float seconds, float x, float z, string name, int requiredWeaponItemId, bool fellTree, bool repeatable = false, string chapterId = null)
        { Id = id; MapId = map; TechnologyId = technology; ItemId = item; Amount = amount; Seconds = seconds; Position = new UnityEngine.Vector3(x, 0, z); NameKey = name; RequiredWeaponItemId = requiredWeaponItemId; FellTree = fellTree; Repeatable = repeatable; ChapterId = chapterId; }
        /// <summary>生成点保留实际地表高度；规则来自原模板，不能把坡地投影成平地距离。</summary>
        public CivilisationSearchPointDefinition(string id,string map,string technology,int item,int amount,float seconds,UnityEngine.Vector3 position,string name,int requiredWeaponItemId,bool fellTree,bool repeatable,string chapterId = null)
        {Id=id;MapId=map;TechnologyId=technology;ItemId=item;Amount=amount;Seconds=seconds;Position=position;NameKey=name;RequiredWeaponItemId=requiredWeaponItemId;FellTree=fellTree;Repeatable=repeatable;ChapterId=chapterId;}
    }
}
