using System;
using QFramework;

namespace Game
{
    /// <summary>
    /// 定义表现控制器的最小生命周期，并接入架构与标准释放合同。
    /// </summary>
    public interface IViewCtrl : IController, IDisposable
    {
        int ViewCtrlId { get; }
        bool IsDisposed { get; }
        bool IsReleased { get; }
        void SetVisible(bool visible);
        void Release();
    }
}
