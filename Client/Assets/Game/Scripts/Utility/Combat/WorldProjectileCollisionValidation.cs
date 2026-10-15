using System;
using System.Collections.Generic;
namespace Game
{
    /// <summary>校验离线碰撞合同，缺失或非法时禁止新弓箭；不以水平即时命中代替失败。</summary>
    public static class WorldProjectileCollisionValidation
    {
        public static bool TryValidate(WorldProjectileCollisionData data,out string error)
        {
            error="地图碰撞数据缺失或非法";
            if(data==null||data.Version!=WorldProjectileCollisionData.CurrentVersion||string.IsNullOrWhiteSpace(data.MapId)||!IsContentHash(data.ContentHash)||data.Shapes==null||data.Shapes.Length==0||data.Profiles==null||data.Profiles.Length==0||!Finite(data.MaxOriginDistance)||data.MaxOriginDistance<=0)return false;
            var ids=new HashSet<long>();foreach(var s in data.Shapes)if(!ShapeValid(s)||s.Id<=0||!ids.Add(s.Id))return false;
            var profiles=new HashSet<string>(StringComparer.Ordinal);foreach(var p in data.Profiles){if(p==null||string.IsNullOrWhiteSpace(p.ConfigId)||!profiles.Add(p.ConfigId)||p.Shapes==null||p.Shapes.Length==0)return false;foreach(var s in p.Shapes)if(!ShapeValid(s))return false;}
            error=null;return true;
        }
        /// <summary>接受正式导出的 64 位十六进制 SHA-256 标识，防止坏格式索引启用射击。</summary>
        public static bool IsContentHash(string value)
        {if(value==null||value.Length!=64)return false;foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;return true;}
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool ShapeValid(WorldProjectileShape s)
        {
            if(s==null||!s.Center.IsFinite||!s.AxisX.IsFinite||!s.AxisY.IsFinite||!s.AxisZ.IsFinite||!s.A.IsFinite||!s.B.IsFinite||!s.C.IsFinite||!Finite(s.Radius)||s.Radius<0)return false;
            switch(s.Kind)
            {
                case WorldCollisionShapeKind.Box:float volume=WorldCollisionVector.Dot(s.AxisX,WorldCollisionVector.Cross(s.AxisY,s.AxisZ));return Finite(volume)&&Math.Abs(volume)>1e-10f;
                case WorldCollisionShapeKind.Sphere:case WorldCollisionShapeKind.Capsule:return s.Radius>0;
                case WorldCollisionShapeKind.Triangle:return WorldCollisionVector.Cross(s.B-s.A,s.C-s.A).Length>1e-8f;
                case WorldCollisionShapeKind.Heightfield:
                    int n=s.HeightmapResolution;if(n<2||n>4097||s.Heights==null||s.Heights.Length!=(long)n*n||Math.Abs(WorldCollisionVector.Dot(s.AxisX,WorldCollisionVector.Cross(s.AxisY,s.AxisZ)))<1e-8f)return false;
                    foreach(float h in s.Heights)if(!Finite(h)||h<0||h>1)return false;
                    int last=-1;foreach(int hole in s.HoleCells??Array.Empty<int>()){if(hole<=last||hole>=(n-1)*(n-1))return false;last=hole;}return true;
                default:return false;
            }
        }
    }
}
