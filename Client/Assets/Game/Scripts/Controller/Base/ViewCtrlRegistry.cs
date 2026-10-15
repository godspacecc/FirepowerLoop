using System.Collections.Generic;
using QFramework;

namespace Game
{
    /// <summary>
    /// 分配 ViewCtrlId 并维护活动表现控制器注册表，提供重复注册保护和批量释放。
    /// </summary>
    public sealed class ViewCtrlRegistry : IUtility
    {
        private readonly Dictionary<int, IViewCtrl> controllers = new Dictionary<int, IViewCtrl>();
        private int nextId = 1;

        public int Count => controllers.Count;

        public bool TryRegister(IViewCtrl controller, out int viewCtrlId, out string error)
        {
            viewCtrlId = 0;
            if (controller == null)
            {
                error = "ViewCtrl 注册失败：控制器不能为空";
                return false;
            }

            foreach (var pair in controllers)
            {
                if (ReferenceEquals(pair.Value, controller))
                {
                    error = $"ViewCtrl 注册失败：控制器已注册，ViewCtrlId={pair.Key}";
                    return false;
                }
            }

            viewCtrlId = nextId++;
            controllers[viewCtrlId] = controller;
            error = null;
            return true;
        }

        public bool TryGet(int viewCtrlId, out IViewCtrl controller)
        {
            return controllers.TryGetValue(viewCtrlId, out controller);
        }

        public bool Unregister(int viewCtrlId)
        {
            return controllers.Remove(viewCtrlId);
        }

        public void DisposeAll()
        {
            var snapshot = new List<IViewCtrl>(controllers.Values);
            for (var i = 0; i < snapshot.Count; i++)
            {
                snapshot[i]?.Dispose();
            }

            controllers.Clear();
        }

        public void Clear()
        {
            controllers.Clear();
        }
    }
}
