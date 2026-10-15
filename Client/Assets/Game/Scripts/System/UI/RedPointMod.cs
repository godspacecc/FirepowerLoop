using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using QFramework;

namespace Game
{
    /// <summary>
    /// 表示可订阅领域事件并从当前模型投影红点状态的模块基类。
    /// </summary>
    public abstract class RedPointMod : ICanGetModel, ICanRegisterEvent
    {
        private readonly List<IUnRegister> registrations = new List<IUnRegister>();
        private RedPointSystem owner;
        private RedPointMod parent;
        private bool attached;

        protected RedPointMod(string id)
        {
            Id = id;
            IsDirty = true;
        }

        public string Id { get; }
        public RedPointMod Parent => parent;
        public bool IsDirty { get; private set; }
        public bool IsActive { get; private set; }
        public int Count { get; private set; }
        public virtual IReadOnlyList<RedPointMod> ChildModules => Array.Empty<RedPointMod>();

        internal bool IsAttached => attached;

        public IArchitecture GetArchitecture()
        {
            return Game.Interface;
        }

        internal void Attach(RedPointSystem newOwner, RedPointMod newParent)
        {
            if (attached)
            {
                return;
            }

            attached = true;
            owner = newOwner;
            parent = newParent;
            OnAttach();
        }

        internal void Detach()
        {
            if (!attached)
            {
                return;
            }

            attached = false;
            Exception firstError = null;
            try
            {
                OnDetach();
            }
            catch (Exception e)
            {
                firstError = e;
            }
            finally
            {
                foreach (var registration in registrations)
                {
                    try
                    {
                        registration?.UnRegister();
                    }
                    catch (Exception e)
                    {
                        firstError ??= e;
                    }
                }

                registrations.Clear();
                owner = null;
                parent = null;
                IsDirty = true;
            }

            if (firstError != null)
            {
                throw firstError;
            }
        }

        protected void Track(IUnRegister registration)
        {
            if (registration != null)
            {
                registrations.Add(registration);
            }
        }

        protected void NotifyChange()
        {
            if (!attached)
            {
                return;
            }

            IsDirty = true;
            owner?.MarkDirty(this);
        }

        internal void SetDirty()
        {
            IsDirty = true;
        }

        internal bool RefreshIfDirty()
        {
            if (!IsDirty)
            {
                return false;
            }

            var previousActive = IsActive;
            var previousCount = Count;
            if (ChildModules.Count > 0)
            {
                var count = 0;
                var active = false;
                foreach (var child in ChildModules)
                {
                    active |= child.IsActive;
                    count += Math.Max(0, child.Count);
                }

                IsActive = active;
                Count = count;
            }
            else
            {
                Count = Math.Max(0, OnGetCount());
                IsActive = OnCheckActive() || Count > 0;
            }

            IsDirty = false;
            return previousActive != IsActive || previousCount != Count;
        }

        protected virtual void OnAttach()
        {
        }

        protected virtual void OnDetach()
        {
        }

        protected virtual bool OnCheckActive()
        {
            return false;
        }

        protected virtual int OnGetCount()
        {
            return 0;
        }
    }

    /// <summary>
    /// 表示只按固定直接子模块聚合状态、不包含业务判断的红点组合模块。
    /// </summary>
    internal sealed class RedPointGroupMod : RedPointMod
    {
        private readonly IReadOnlyList<RedPointMod> childModules;

        public RedPointGroupMod(string id, params RedPointMod[] children) : base(id)
        {
            childModules = new ReadOnlyCollection<RedPointMod>(new List<RedPointMod>(children ?? Array.Empty<RedPointMod>()));
        }

        public override IReadOnlyList<RedPointMod> ChildModules => childModules;
    }
}
