using QFramework;
using UnityEngine;

namespace Game
{
    public class BaseUI : MonoBehaviour
    {
        public readonly EasyEvent OnTriggerDestroyEvent = new EasyEvent();

        protected virtual void Awake()
        {
            UITheme.Apply(gameObject);
        }

        private void OnDestroy()
        {
            OnTriggerDestroyEvent.Trigger();
        }
    }
}
