using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Game
{
    public sealed partial class ChallengeWorldCtrl
    {
        private GameObject enemyPrefab;
        private NavMeshData navigation;
        private NavMeshDataInstance navigationInstance;
        private readonly Dictionary<int, EnemyView> enemyViews = new();
        private readonly NavMeshPath path = new();
        private readonly Dictionary<int, Vector3> entryTargets = new();

        /// <summary>按真实路面和车辆碰撞生成本局独立寻路数据，卸载时归还。</summary>
        public async UniTask PrepareCombatAsync(string address, CancellationToken token)
        {
            enemyPrefab = await LoadAsync(address, token);
            if (enemyPrefab.GetComponent<EnemyView>() == null) throw new InvalidOperationException("挑战敌人缺少表现组件");
            var sources = new List<NavMeshBuildSource>();
            foreach (var surface in new[] { ground, road })
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = surface.sharedMesh, transform = surface.transform.localToWorldMatrix, area = 0 });
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = box.size,
                    transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center), area = 1 });
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .3f;
            settings.agentHeight = 1.7f;
            settings.agentClimb = .25f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .1f;
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, new Vector3(100, 40, 100)), Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("挑战通路导航生成失败");
            navigationInstance = NavMesh.AddNavMeshData(navigation);
        }

        public bool SpawnEnemy(int id, int gateIndex)
        {
            var gate = spawnGates.Find("Gate" + (gateIndex + 1));
            var entry = gate.Find("Entry").position;
            if (!NavMesh.SamplePosition(entry, out var hit, 1, NavMesh.AllAreas) || Mathf.Abs(hit.position.y-entry.y) > .5f) return false;
            var instance = Object.Instantiate(enemyPrefab, root.transform);
            var view = instance.GetComponent<EnemyView>();
            view.SetNavigationEnabled(false);
            instance.transform.position = gate.position;
            entryTargets[id] = hit.position;
            view.ConfigureRuntime(id, MapInstanceId);
            view.SetHealthBarVisible(false);
            enemyViews.Add(id, view);
            return true;
        }

        /// <summary>仅沿完整寻路路径行走，避免高层直接跨空或跨层追击。</summary>
        public Vector3 MoveEnemy(int id, float speed, float seconds)
        {
            var view = enemyViews[id];
            var start = view.transform.position;
            if (entryTargets.TryGetValue(id, out var entrance))
            {
                view.transform.position = Vector3.MoveTowards(start, entrance, speed * seconds);
                if ((view.transform.position - entrance).sqrMagnitude < .0025f) entryTargets.Remove(id);
            }
            else if (NavMesh.CalculatePath(start, Position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 1)
            {
                var next = Vector3.MoveTowards(start, path.corners[1], speed * seconds);
                if ((Position-start).sqrMagnitude > .8f) view.transform.position = next;
            }
            var motion = view.transform.position - start;
            if (motion.sqrMagnitude > .00001f) view.transform.rotation = Quaternion.LookRotation(new Vector3(motion.x, 0, motion.z));
            view.SetMovementSpeed(seconds > 0 ? motion.magnitude / seconds : 0);
            view.TickCombatFeedback(seconds);
            return view.transform.position;
        }

        /// <summary>攻击资格同时检查脚底层高和躯干之间的实际遮挡。</summary>
        public bool CanContactPlayer(int id)
        {
            var enemy = enemyViews[id];
            if (entryTargets.ContainsKey(id) || Mathf.Abs(enemy.transform.position.y - Position.y) > .5f) return false;
            var from = enemy.transform.position + Vector3.up;
            var offset = Position + Vector3.up - from;
            foreach (var hit in Physics.RaycastAll(from, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(enemy.transform) || hit.transform.IsChildOf(player.transform)) continue;
                if (hit.transform.IsChildOf(root.transform)) return false;
            }
            return true;
        }

        public void RemoveEnemy(int id)
        {
            if (!enemyViews.TryGetValue(id, out var view)) return;
            enemyViews.Remove(id);
            entryTargets.Remove(id);
            view.ReleasePresentationResources();
            Object.Destroy(view.gameObject);
        }

        /// <summary>枪线取最近敌人，同时用真实场景碰撞阻止穿越车辆与高架路面。</summary>
        public int FindShotTarget(float range)
        {
            Vector3 origin = Position + Vector3.up;
            Vector3 direction = player.transform.forward;
            int target = 0;
            float nearest = range;
            foreach (var pair in enemyViews)
            {
                Vector3 offset = pair.Value.transform.position + Vector3.up - origin;
                float along = Vector3.Dot(offset, direction);
                if (along < 0 || along >= nearest || (offset-direction*along).sqrMagnitude > .45f*.45f) continue;
                bool blocked = false;
                foreach (var hit in Physics.RaycastAll(origin, direction, along, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(player.transform) || hit.transform.IsChildOf(pair.Value.transform)) continue;
                    if (hit.transform.IsChildOf(root.transform)) { blocked = true; break; }
                }
                if (!blocked) { target = pair.Key; nearest = along; }
            }
            return target;
        }

        private void ReleaseCombat()
        {
            foreach (var view in enemyViews.Values) view.ReleasePresentationResources();
            enemyViews.Clear();
            entryTargets.Clear();
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation != null) Object.Destroy(navigation);
        }
    }
}
