using System;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 为具有 Unit 身份的表现控制器增加 UnitId 和 MapInstanceId。
    /// </summary>
    public abstract class BaseEntityViewCtrl<TView> : BaseViewCtrl<TView> where TView : Component
    {
        protected BaseEntityViewCtrl(
            TView view,
            int unitId,
            long mapInstanceId,
            ViewCtrlRegistry registry,
            IViewInstanceProvider instanceProvider)
            : base(ValidateIdentity(view, unitId, mapInstanceId), registry, instanceProvider)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }

        private static TView ValidateIdentity(TView view, int unitId, long mapInstanceId)
        {
            if (unitId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(unitId), "ViewCtrl 创建失败：UnitId 必须大于 0");
            }

            if (mapInstanceId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mapInstanceId), "ViewCtrl 创建失败：MapInstanceId 必须大于 0");
            }

            return view;
        }
    }
}
