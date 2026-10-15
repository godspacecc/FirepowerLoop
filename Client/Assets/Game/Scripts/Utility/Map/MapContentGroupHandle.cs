using System;
using System.Collections.Generic;

namespace Game
{
    public sealed class MapContentGroupHandle : IMapContentHandle
    {
        private readonly IMapContentHandle[] children;

        public MapContentGroupHandle(IEnumerable<IMapContentHandle> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            this.children = System.Linq.Enumerable.ToArray(children);
            if (this.children.Length == 0) throw new ArgumentException("地图内容组不能为空", nameof(children));

            var first = this.children[0];
            if (first == null || !first.IsValid || first.Context == null)
                throw new ArgumentException("地图内容组主句柄无效", nameof(children));

            MapId = first.MapId;
            Context = first.Context;
            foreach (var child in this.children)
            {
                if (child == null || !child.IsValid || child.Context == null)
                    throw new ArgumentException("地图内容组包含无效句柄", nameof(children));
                if (!string.Equals(child.MapId, MapId, StringComparison.Ordinal))
                    throw new ArgumentException("地图内容组包含不同地图编号", nameof(children));
            }

            Children = Array.AsReadOnly(this.children);
        }

        public string MapId { get; }
        public MapContext Context { get; }
        public IReadOnlyList<IMapContentHandle> Children { get; }

        public bool IsValid
        {
            get
            {
                foreach (var child in children)
                {
                    if (child == null || !child.IsValid || child.Context == null) return false;
                }
                return true;
            }
        }
    }
}
