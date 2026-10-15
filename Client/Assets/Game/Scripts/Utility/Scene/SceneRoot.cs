using UnityEngine;

namespace Game
{
    /// <summary>
    /// 场景根节点查询工具。
    /// </summary>
    public static class SceneRoot
    {
        public const string PoolRootName = "PoolRoot";

        public static Transform PoolRoot => Require(PoolRootName);

        public static Transform Require(string rootName)
        {
            var root = GameObject.Find(rootName);
            if (root != null)
            {
                return root.transform;
            }

            Log.Error($"[SceneRoot] 缺少必需场景根节点：{rootName}。");
            return null;
        }
    }
}
