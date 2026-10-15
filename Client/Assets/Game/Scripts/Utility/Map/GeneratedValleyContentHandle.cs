using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace Game
{
    /// <summary>唯一拥有候选河谷的生成几何、导航与资源 Handle；原场景仍由包装加载器卸载。</summary>
    public sealed class GeneratedValleyContentHandle:IMapContentHandle
    {
        internal readonly IResourceLoader Loader;
        internal ResourceLoadHandle KitHandle;
        internal GameObject GeneratedRoot;
        internal GameObject KitInstance;
        internal readonly List<Mesh> Meshes=new List<Mesh>();
        internal readonly List<GameObject> LegacyRoots=new List<GameObject>();
        internal readonly List<NavMeshBuildSource> Sources=new List<NavMeshBuildSource>();
        internal NavMeshData Navigation;
        internal NavMeshDataInstance NavigationInstance;
        internal ValleyVisibilityCtrl Visibility;
        internal GameObject ShortcutBlocker;
        internal int ShortcutSourceIndex;
        internal NavMeshBuildSource ClosedShortcutSource;
        /// <summary>导航异步写入期间禁止释放 NavMeshData；释放会等待更新与回滚结束。</summary>
        internal bool NavigationUpdating;
        private UniTaskCompletionSource<MapContentUnloadResult> releaseCompletion;
        private bool released;
        public GeneratedValleyContentHandle(IMapContentHandle original,IResourceLoader loader,ValleyLayoutSaveData layout)
        {Original=original;Loader=loader;Layout=layout.Clone();}
        public IMapContentHandle Original{get;}
        public string MapId=>Original.MapId;
        public MapContext Context=>Original.Context;
        public bool IsValid=>!released&&Original.IsValid&&GeneratedRoot!=null;
        public ValleyLayoutSaveData Layout{get;}
        private IReadOnlyList<CivilisationSearchPointDefinition> points;
        /// <summary>只属于此地图实例的稳定 ID 索引；安装清单时构建，释放时清空，不跨地图复用。</summary>
        private readonly Dictionary<string, CivilisationSearchPointDefinition> pointsById = new Dictionary<string, CivilisationSearchPointDefinition>(System.StringComparer.Ordinal);
        public IReadOnlyList<CivilisationSearchPointDefinition> Points
        {
            get => points;
            internal set
            {
                var index = new Dictionary<string, CivilisationSearchPointDefinition>(System.StringComparer.Ordinal);
                var copy = value == null ? System.Array.Empty<CivilisationSearchPointDefinition>() : new CivilisationSearchPointDefinition[value.Count];
                for (var i = 0; i < copy.Length; i++)
                {
                    var point = value[i];
                    if (point == null || string.IsNullOrWhiteSpace(point.Id) || point.MapId != MapId || !index.TryAdd(point.Id, point))
                        throw new System.InvalidOperationException("河谷采集点身份无效或重复");
                    copy[i] = point;
                }
                pointsById.Clear(); foreach (var pair in index) pointsById.Add(pair.Key, pair.Value);
                points = System.Array.AsReadOnly(copy);
            }
        }
        /// <summary>仅查询有效的当前候选；过期句柄不能返回旧地图配置或回退静态点位。</summary>
        internal bool TryGetPoint(string id, out CivilisationSearchPointDefinition point)
        { point = null; return IsValid && id != null && pointsById.TryGetValue(id, out point); }
        public float RouteLength{get;internal set;}
        public float AlternateRouteLength{get;internal set;}
        public float DeepWaterSourceCount{get;internal set;}
        public float ValidationOffsetX=>16384;
        internal bool ShortcutOpen{get;set;}
        public void SetActive(bool active)=>Visibility?.SetActive(active);
        /// <summary>重复释放无副作用；先解除导航和更新，再释放实例、Mesh 与绑定加载器的资源。</summary>
        public UniTask<MapContentUnloadResult> ReleaseGeneratedAsync()
        {
            if(releaseCompletion!=null)return releaseCompletion.Task;
            releaseCompletion=new UniTaskCompletionSource<MapContentUnloadResult>();
            released=true;Visibility?.Dispose();Visibility=null;
            pointsById.Clear(); points = null;
            ReleaseOwnedAsync().Forget();
            return releaseCompletion.Task;
        }
        private async UniTaskVoid ReleaseOwnedAsync()
        {
            try
            {
                while(NavigationUpdating)await UniTask.Yield();
                if(NavigationInstance.valid)NavigationInstance.Remove();
                DestroyOwned(GeneratedRoot);DestroyOwned(KitInstance);DestroyOwned(Navigation);
                foreach(var mesh in Meshes)DestroyOwned(mesh);Meshes.Clear();
                foreach(var legacy in LegacyRoots)if(legacy!=null)legacy.SetActive(true);
                LegacyRoots.Clear();
                Loader.Release(KitHandle);KitHandle=ResourceLoadHandle.Empty;
                releaseCompletion.TrySetResult(MapContentUnloadResult.Ok());
            }
            catch(System.Exception exception){releaseCompletion.TrySetResult(MapContentUnloadResult.Fail("河谷资源释放失败："+exception.Message));}
        }
        internal static void DestroyOwned(Object obj)
        {if(obj==null)return;if(Application.isPlaying)Object.Destroy(obj);else Object.DestroyImmediate(obj);}
    }
}
