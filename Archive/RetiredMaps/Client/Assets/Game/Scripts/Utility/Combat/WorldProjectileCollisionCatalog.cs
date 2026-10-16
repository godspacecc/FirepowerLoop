using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game
{
    /// <summary>客户端只持有导出版本和依赖哈希，不拥有服务器命中结论。</summary>
    [Serializable] public sealed class WorldProjectileCollisionIndex
    {public int Version;public string MapId,ContentHash;}
    /// <summary>保存本次已准备地图的资源哈希，资源 Handle 仍由地图投射物控制器负责。</summary>
    public static class WorldProjectileCollisionCatalog
    {
        private static readonly Dictionary<string,WorldProjectileCollisionIndex> indices=new Dictionary<string,WorldProjectileCollisionIndex>(StringComparer.Ordinal);
        public static bool TryGet(string mapId,out WorldProjectileCollisionIndex index,out string error)
        {error="地图碰撞索引尚未准备";if(mapId!=null&&indices.TryGetValue(mapId,out index)){error=null;return true;}index=null;return false;}
        public static bool Prepare(string mapId,string json,out string error)
        {
            error="地图碰撞索引缺失或非法";try{var index=JsonUtility.FromJson<WorldProjectileCollisionIndex>(json);if(index==null||index.Version!=WorldProjectileCollisionData.CurrentVersion||index.MapId!=mapId||!WorldProjectileCollisionValidation.IsContentHash(index.ContentHash))return false;indices[mapId]=index;error=null;return true;}catch{return false;}
        }
        public static void Release(string mapId){if(mapId!=null)indices.Remove(mapId);}
    }
}
