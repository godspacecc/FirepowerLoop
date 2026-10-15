using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed class DamageNumberEmitter : MonoBehaviour
    {
        private sealed class Entry
        {
            public DamageNumberView View;
            public float Elapsed;
            public Vector3 Start;
        }

        private static readonly Color EnemyDamageColor = new Color(1f, 0.78f, 0.22f, 1f);
        private static readonly Color PlayerDamageColor = new Color(1f, 0.25f, 0.2f, 1f);

        [SerializeField] private DamageNumberView numberPrefab;
        [SerializeField] private Vector3 localOrigin = new Vector3(0f, 2.2f, 0f);
        [SerializeField, Min(0.1f)] private float lifetimeSeconds = 0.75f;
        [SerializeField, Min(0f)] private float riseDistance = 0.9f;
        [SerializeField, Min(1)] private int maxConcurrent = 8;

        private readonly List<Entry> active = new List<Entry>();
        private readonly Stack<DamageNumberView> pooled = new Stack<DamageNumberView>();
        private int sequence;
        private bool ticking;

        private void OnEnable()
        {
            if (ticking) return;
            ticking = true;
            LifeCycle.RegisterUpdate(Tick);
        }

        private void OnDisable()
        {
            if (ticking)
            {
                LifeCycle.UnregisterUpdate(Tick);
                ticking = false;
            }
            for (int i = active.Count - 1; i >= 0; i--) Recycle(active[i].View);
            active.Clear();
        }

        public void Show(int damage, bool playerTarget)
        {
            if (damage <= 0 || !isActiveAndEnabled || numberPrefab == null) return;
            if (active.Count >= Mathf.Max(1, maxConcurrent))
            {
                Recycle(active[0].View);
                active.RemoveAt(0);
            }

            DamageNumberView view = pooled.Count > 0 ? pooled.Pop() : Instantiate(numberPrefab, transform);
            if (view == null) return;
            float horizontal = ((sequence++ % 3) - 1) * 0.18f;
            Vector3 start = localOrigin + Vector3.right * horizontal;
            view.transform.localPosition = start;
            view.gameObject.SetActive(true);
            view.Bind(damage, playerTarget ? PlayerDamageColor : EnemyDamageColor);
            view.FaceCamera(Camera.main);
            active.Add(new Entry { View = view, Start = start });
        }

        public bool Validate(out string error)
        {
            if (numberPrefab == null) { error = "单位缺少伤害飘字 Prefab"; return false; }
            if (!numberPrefab.Validate(out error)) return false;
            if (lifetimeSeconds <= 0f || riseDistance < 0f || maxConcurrent <= 0)
            { error = "伤害飘字参数无效"; return false; }
            error = null;
            return true;
        }

        private void Tick()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;
            Camera camera = Camera.main;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Entry entry = active[i];
                entry.Elapsed += deltaTime;
                float progress = Mathf.Clamp01(entry.Elapsed / lifetimeSeconds);
                entry.View.transform.localPosition = entry.Start + Vector3.up * (riseDistance * progress);
                entry.View.SetAlpha(1f - progress);
                entry.View.FaceCamera(camera);
                if (entry.Elapsed < lifetimeSeconds) continue;
                Recycle(entry.View);
                active.RemoveAt(i);
            }
        }

        private void Recycle(DamageNumberView view)
        {
            if (view == null) return;
            view.gameObject.SetActive(false);
            pooled.Push(view);
        }
    }
}
