using QFramework;
using UnityEngine;

namespace Game
{
    public interface ICursorLockPort : IUtility
    {
        void SetLocked(bool locked);
    }

    public sealed class UnityCursorLockPort : ICursorLockPort
    {
        public void SetLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
