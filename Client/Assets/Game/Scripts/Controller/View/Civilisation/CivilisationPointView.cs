using UnityEngine;

namespace Game
{
    public enum CivilisationPointKind { Search, Preparation, Return, Workshop, Clue, Shortcut }

    /// <summary>保留旧文明物件的原序列化标识和资源引用，不启用业务交互。</summary>
    public sealed class CivilisationPointView : PlayerInteractableView
    {
        [SerializeField] private CivilisationPointKind kind;
        /// <summary>配置中的稳定搜索点 ID；非搜索物件可为空，不用场景实例 ID 存档。</summary>
        [SerializeField] private string pointId;
        /// <summary>采集成功后隐藏的资源根节点；必须独立于交互物，不能停用地图或建筑根节点。</summary>
        [SerializeField] private GameObject harvestedVisualRoot;
        public CivilisationPointKind Kind => kind;
        public string PointId => pointId;
        internal GameObject HarvestedVisualRoot=>harvestedVisualRoot;
        public override bool CanInteract => false;
        public override void Interact() { }
        protected override bool ShouldShowFocusedPresentation => CanInteract;
    }
}
