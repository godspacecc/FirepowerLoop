using UnityEngine;

namespace Game
{
    public enum CivilisationPointKind { Search, Preparation, Return, Workshop, Clue, Shortcut }

    /// <summary>文明交互物的序列化标识与触发入口；业务判断交给 ViewCtrl 和领域 System。</summary>
    public sealed class CivilisationPointView : PlayerInteractableView
    {
        [SerializeField] private CivilisationPointKind kind;
        /// <summary>配置中的稳定搜索点 ID；非搜索物件可为空，不用场景实例 ID 存档。</summary>
        [SerializeField] private string pointId;
        /// <summary>采集成功后隐藏的资源根节点；必须独立于交互物，不能停用地图或建筑根节点。</summary>
        [SerializeField] private GameObject harvestedVisualRoot;
        private CivilisationPointViewCtrl controller;
        /// <summary>生成入口的提示键；世界标签与交互快照共用，按当前语言读取。</summary>
        private string generatedNameKey;
        public override string DisplayName=>string.IsNullOrEmpty(generatedNameKey)?base.DisplayName:Loc.T(generatedNameKey);
        public CivilisationPointKind Kind => kind;
        public string PointId => pointId;
        internal GameObject HarvestedVisualRoot=>harvestedVisualRoot;
        /// <summary>生成地图复用提示层级，仅更换本地化显示；不影响领域身份。</summary>
        internal void ConfigureGeneratedPrompt(string key)
        {
            generatedNameKey=key;
            if(FocusedPresentationRoot==null)return;
            foreach(var text in FocusedPresentationRoot.GetComponentsInChildren<LocalizedText>(true))
            {text.Key=key;text.Refresh();}
        }
        public override bool CanInteract => base.CanInteract && controller != null && controller.CanInteract;
        /// <summary>只由地图提交后的场景 Controller 绑定；候选地图中物件不自行建立权威入口。</summary>
        internal void Bind(CivilisationPointViewCtrl owner) => controller = owner;
        /// <summary>候选生成时只设置稳定身份；模板中的提示和资源引用随实例保留，提交后才能绑定交互。</summary>
        internal void ConfigureGenerated(CivilisationPointKind pointKind,string stableId)
        { kind=pointKind;pointId=stableId;controller=null; }
        public override void Interact() => controller?.Interact();
        /// <summary>地图绑定或采集提交后投影已存档状态，停用绑定树木或石块的表现和实体碰撞；重复调用无副作用。</summary>
        internal void RefreshHarvestPresentation(bool harvested)
        {
            if (harvestedVisualRoot == null) return;
            harvestedVisualRoot.SetActive(!harvested);
            GetComponent<Collider>().enabled = !harvested;
            RefreshFocusedPresentation();
        }
        protected override bool ShouldShowFocusedPresentation => CanInteract;
        /// <summary>由地图 Controller 在 LateUpdate 调用，保持名称面向当前玩家相机，并隐藏已失效目标。</summary>
        public void RefreshPrompt(Vector3 cameraPosition, Vector3 cameraUp)
        {
            RefreshFocusedPresentation();
            var root = FocusedPresentationRoot;
            if (root == null || !root.activeSelf) return;
            var direction = root.transform.position - cameraPosition;
            if (direction.sqrMagnitude > 0.0001f) root.transform.rotation = Quaternion.LookRotation(direction, cameraUp);
        }
    }
}
