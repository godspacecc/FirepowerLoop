using System;

namespace Game
{
    /// <summary>地图铺怪与外围候选纯规则；地图坐标固定，玩家位置只用于拒绝安全区。</summary>
    public static class SurvivorHordeRules
    {
        /// <summary>每个分区有界尝试一组，不生成逐敌人对象；返回实际成功的群数。</summary>
        public static int Seed(SurvivorHordeState state,int columns,int rows,float halfWidth,float halfHeight,
            float playerX,float playerY,float safeDistance,int countPerGroup,int attemptsPerRegion,int seed,
            Func<float,float,bool> reachable,Func<int,(string EnemyId,float Speed)> selectEnemy)
        {
            if(state==null||reachable==null||selectEnemy==null)throw new ArgumentNullException();
            if(columns<=0||rows<=0||countPerGroup<=0||attemptsPerRegion<=0||!Positive(halfWidth)||!Positive(halfHeight)||
                !Finite(playerX)||!Finite(playerY)||!Finite(safeDistance)||safeDistance<0)
                throw new ArgumentException("地图敌群分布参数无效");
            int regions=checked(columns*rows),created=0;var random=new Random(seed);
            for(int region=0;region<regions;region++)
            {
                int column=region%columns,row=region/columns;
                for(int attempt=0;attempt<attemptsPerRegion;attempt++)
                {
                    float x=(float)(-halfWidth+(column+random.NextDouble())*(2d*halfWidth/columns));
                    float y=(float)(-halfHeight+(row+random.NextDouble())*(2d*halfHeight/rows));
                    double dx=(double)x-playerX,dy=(double)y-playerY;
                    if(dx*dx+dy*dy<(double)safeDistance*safeDistance||!reachable(x,y))continue;
                    var enemy=selectEnemy(region);
                    state.Add(region,enemy.EnemyId,x,y,enemy.Speed,countPerGroup);created++;break;
                }
            }
            return created;
        }

        /// <summary>外围分区判定；地图内部外围可达性仍由导航验证，不生成场外怪。</summary>
        public static bool IsPeripheralRegion(int region,int columns,int rows)
        {
            if(columns<=0||rows<=0||region<0||region>=checked(columns*rows))throw new ArgumentOutOfRangeException();
            int x=region%columns,y=region/columns;
            return x==0||y==0||x==columns-1||y==rows-1;
        }
        private static bool Positive(float value)=>Finite(value)&&value>0;
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
