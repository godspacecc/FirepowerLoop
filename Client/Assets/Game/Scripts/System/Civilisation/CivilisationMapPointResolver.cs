using System.Linq;

namespace Game
{
    /// <summary>统一查询当前地图点身份；生成图只查当局目录，不能回退到同名静态坐标。</summary>
    public static class CivilisationMapPointResolver
    {
        public static bool TryGetPoint(IMapContentHandle handle,CivilisationCatalog templates,string id,out CivilisationSearchPointDefinition point)
        {
            point=null;if(string.IsNullOrWhiteSpace(id))return false;
            if(handle is GeneratedValleyContentHandle generated)
            {return generated.TryGetPoint(id, out point);}
            else point=templates?.Points.FirstOrDefault(p=>p.Id==id&&(handle==null||p.MapId==handle.MapId));
            return point!=null;
        }
    }
}
