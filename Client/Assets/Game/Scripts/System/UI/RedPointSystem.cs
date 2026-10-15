using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 管理预定义红点模块树，统一安排帧末刷新、父级聚合和状态变化通知。
    /// </summary>
    public sealed class RedPointSystem : AbstractSystem
    {
        private readonly Dictionary<string, RedPointMod> modules = new Dictionary<string, RedPointMod>(StringComparer.Ordinal);
        private readonly List<RedPointMod> roots = new List<RedPointMod>();
        private readonly HashSet<RedPointMod> dirtyModules = new HashSet<RedPointMod>();
        private bool acceptingRefresh;
        private bool flushScheduled;

        protected override void OnInit()
        {
            acceptingRefresh = true;
            var resourceUpdate = new ResourceUpdateRedPointMod();
            Register(new RedPointGroupMod(RedPointIds.Start, resourceUpdate));
        }

        public bool Register(RedPointMod rootModule)
        {
            if (rootModule == null || rootModule.Parent != null || rootModule.IsAttached)
            {
                return false;
            }

            var tree = new List<RedPointMod>();
            if (!ValidateTree(rootModule, tree))
            {
                return false;
            }

            var attached = new List<RedPointMod>();
            try
            {
                AttachTree(rootModule, null, attached);
                roots.Add(rootModule);
                foreach (var module in tree)
                {
                    MarkDirty(module);
                }

                return true;
            }
            catch
            {
                for (var i = attached.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        attached[i].Detach();
                    }
                    catch
                    {
                        // 注册异常优先，回滚仍必须继续。
                    }
                }

                foreach (var module in tree)
                {
                    modules.Remove(module.Id);
                    dirtyModules.Remove(module);
                }

                roots.Remove(rootModule);
                throw;
            }
        }

        public RedPointState GetState(string id)
        {
            return modules.TryGetValue(id, out var module)
                ? new RedPointState(module.Id, module.IsActive, module.Count)
                : new RedPointState(id, false, 0);
        }

        public bool Unregister(string rootId)
        {
            if (string.IsNullOrWhiteSpace(rootId) || !modules.TryGetValue(rootId, out var root) || !roots.Contains(root))
            {
                return false;
            }

            var tree = GetTree(root);
            var previous = tree.Select(module => new RedPointState(module.Id, module.IsActive, module.Count)).ToArray();
            foreach (var module in tree)
            {
                modules.Remove(module.Id);
                dirtyModules.Remove(module);
            }

            roots.Remove(root);
            Exception firstError = null;
            for (var i = tree.Count - 1; i >= 0; i--)
            {
                try
                {
                    tree[i].Detach();
                }
                catch (Exception e)
                {
                    firstError ??= e;
                }
            }

            foreach (var state in previous.Reverse())
            {
                if (state.Active || state.Count != 0)
                {
                    this.SendEvent(new RedPointChangedEvent(state.Id, false, 0));
                }
            }

            if (firstError != null)
            {
                throw firstError;
            }

            return true;
        }

        protected override void OnDeinit()
        {
            acceptingRefresh = false;
            flushScheduled = false;
            var allRoots = roots.ToArray();
            roots.Clear();
            modules.Clear();
            dirtyModules.Clear();

            foreach (var root in allRoots)
            {
                var tree = GetTree(root);
                for (var i = tree.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        tree[i].Detach();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[RedPointSystem] 红点模块释放失败，模块 ID：{tree[i].Id}，异常：{e}");
                    }
                }
            }
        }

        internal void MarkDirty(RedPointMod module)
        {
            if (!acceptingRefresh || module == null || !modules.TryGetValue(module.Id, out var registered) || !ReferenceEquals(module, registered))
            {
                return;
            }

            for (var current = module; current != null; current = current.Parent)
            {
                current.SetDirty();
                dirtyModules.Add(current);
            }

            if (!flushScheduled)
            {
                flushScheduled = true;
                LifeCycle.ScheduleFrameEnd(this, FlushDirty);
            }
        }

        private bool ValidateTree(RedPointMod root, List<RedPointMod> tree)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var allModules = new HashSet<RedPointMod>();
            var stack = new HashSet<RedPointMod>();
            return ValidateTreeRecursive(root, tree, ids, allModules, stack);
        }

        private bool ValidateTreeRecursive(
            RedPointMod module,
            List<RedPointMod> tree,
            HashSet<string> ids,
            HashSet<RedPointMod> allModules,
            HashSet<RedPointMod> stack)
        {
            if (module == null || string.IsNullOrWhiteSpace(module.Id) || modules.ContainsKey(module.Id) ||
                !ids.Add(module.Id) || !allModules.Add(module) || !stack.Add(module) || module.IsAttached)
            {
                return false;
            }

            tree.Add(module);
            var children = module.ChildModules;
            if (children == null)
            {
                return false;
            }

            foreach (var child in children)
            {
                if (!ValidateTreeRecursive(child, tree, ids, allModules, stack))
                {
                    return false;
                }
            }

            stack.Remove(module);
            return true;
        }

        private void AttachTree(RedPointMod module, RedPointMod parent, List<RedPointMod> attached)
        {
            attached.Add(module);
            module.Attach(this, parent);
            modules.Add(module.Id, module);
            foreach (var child in module.ChildModules)
            {
                AttachTree(child, module, attached);
            }
        }

        private static List<RedPointMod> GetTree(RedPointMod root)
        {
            var result = new List<RedPointMod>();
            CollectTree(root, result);
            return result;
        }

        private static void CollectTree(RedPointMod module, List<RedPointMod> result)
        {
            result.Add(module);
            foreach (var child in module.ChildModules)
            {
                CollectTree(child, result);
            }
        }

        private void FlushDirty()
        {
            flushScheduled = false;
            if (!acceptingRefresh || dirtyModules.Count == 0)
            {
                return;
            }

            dirtyModules.Clear();
            var changes = new List<RedPointState>();
            foreach (var root in roots.ToArray())
            {
                RefreshTree(root, changes);
            }

            foreach (var state in changes)
            {
                this.SendEvent(new RedPointChangedEvent(state.Id, state.Active, state.Count));
            }
        }

        private static void RefreshTree(RedPointMod module, List<RedPointState> changes)
        {
            foreach (var child in module.ChildModules)
            {
                RefreshTree(child, changes);
            }

            if (module.RefreshIfDirty())
            {
                changes.Add(new RedPointState(module.Id, module.IsActive, module.Count));
            }
        }
    }
}
