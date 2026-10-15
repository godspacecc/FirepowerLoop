using System;
using System.Collections.Generic;
using System.Linq;
using AnotherDay.Arcade;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        private readonly Dictionary<int, GameObject> crateVisuals = new();
        private readonly Dictionary<int, GameObject> armorVisuals = new();
        private ObstacleVisualLoadState babylonLoad;
        private GameObject crateTemplate, armorTemplate;
        private ArcadeBabylonParameters babylonParameters;
        private int[] remainingCrateIds = Array.Empty<int>();
        private readonly Dictionary<int, Vector2> armoredPositions = new();
        private int babylonGeneration;
        private bool babylonFailed;

        public void SetBabylon(ArcadeBabylonParameters parameters, IReadOnlyList<int> remainingIds)
        {
            if (parameters == null || parameters.Crates.Count == 0) { ReleaseBabylon(); return; }
            babylonParameters = parameters; remainingCrateIds = remainingIds?.ToArray() ?? Array.Empty<int>();
            if (crateTemplate != null) SyncCrates();
            else if (root != null && babylonLoad == null && !babylonFailed) LoadBabylonAsync(++babylonGeneration).Forget();
        }
        /// <summary>只在原地图根和加载代次仍有效时提交模板；任何失败/过期成对释放。</summary>
        private async UniTaskVoid LoadBabylonAsync(int generation)
        {
            var state = new ObstacleVisualLoadState(); babylonLoad = state; var expectedRoot = root;
            var completion = new UniTaskCompletionSource<GameObject>();
            state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(ObstacleVisualAddress, p => completion.TrySetResult(p));
            try
            {
                var prefab = await completion.Task;
                if (state.Released || generation != babylonGeneration || root == null || root != expectedRoot) return;
                crateTemplate = prefab?.transform.Find("Crate")?.gameObject;
                armorTemplate = prefab?.transform.Find("ArmorPlate")?.gameObject;
                if (crateTemplate == null || armorTemplate == null) throw new InvalidOperationException("Babylon 模板缺失");
                SyncCrates();
                foreach (var pair in armoredPositions.ToArray()) SetEnemyArmor(pair.Key, pair.Value, 1);
            }
            catch (Exception e)
            {
                if (!state.Released) Log.Error("[ArcadeCombatViewController] Babylon 加载失败：" + e.Message);
                if (generation == babylonGeneration) babylonFailed = true;
                ReleaseObstacleVisualLoad(state); if (ReferenceEquals(babylonLoad, state)) babylonLoad = null;
                crateTemplate = null; armorTemplate = null;
            }
        }
        private void SyncCrates()
        {
            foreach (var id in crateVisuals.Keys.ToArray())
                if (!remainingCrateIds.Contains(id)) { DestroyObject(crateVisuals[id]); crateVisuals.Remove(id); }
            foreach (var c in babylonParameters.Crates)
            {
                if (!remainingCrateIds.Contains(c.Id) || crateVisuals.ContainsKey(c.Id)) continue;
                var go = UnityEngine.Object.Instantiate(crateTemplate, root.transform, false); go.SetActive(true);
                go.name = "ArcadeCrate_" + c.Id; go.transform.localPosition = World(new Vector2(c.X, c.Y), 0);
                go.transform.localScale = Vector3.one * c.Radius * 2; crateVisuals.Add(c.Id, go);
            }
        }
        public void SetEnemyArmor(int id, Vector2 position, int health)
        {
            if (health <= 0) { RemoveArmor(id); return; }
            armoredPositions[id] = position;
            if (armorTemplate == null || root == null) return;
            if (!armorVisuals.TryGetValue(id, out var go))
            {
                go = UnityEngine.Object.Instantiate(armorTemplate, root.transform, false); go.SetActive(true);
                go.name = "ArcadeArmor_" + id; armorVisuals.Add(id, go);
            }
            go.transform.localPosition = World(position, 1.2f);
        }
        private void RemoveArmor(int id)
        {
            armoredPositions.Remove(id);
            if (!armorVisuals.TryGetValue(id, out var go)) return;
            DestroyObject(go); armorVisuals.Remove(id);
        }
        private void ReleaseBabylon()
        {
            babylonGeneration++; babylonFailed = false;
            foreach (var go in crateVisuals.Values) DestroyObject(go); crateVisuals.Clear();
            foreach (var go in armorVisuals.Values) DestroyObject(go); armorVisuals.Clear(); armoredPositions.Clear();
            crateTemplate = null; armorTemplate = null; babylonParameters = null; remainingCrateIds = Array.Empty<int>();
            ReleaseObstacleVisualLoad(babylonLoad); babylonLoad = null;
        }
    }
}
