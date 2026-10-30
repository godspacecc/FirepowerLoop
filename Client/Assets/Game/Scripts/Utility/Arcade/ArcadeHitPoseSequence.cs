using System;
using System.Globalization;

namespace FirepowerLoop.Arcade
{
    /// <summary>离线身体胶囊采样序列；仅由调用方提供的年龄与世界朝向决定几何，不维护时钟。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeHitPoseSequence
    {
        /// <summary>按帧、体积排列的不可变内部样本。</summary>
        private readonly ArcadeHitCapsule[] samples;
        /// <summary>总帧数。</summary>
        public int FrameCount { get; }
        /// <summary>每帧胶囊数量；所有帧必须相同。</summary>
        public int CapsulesPerFrame { get; }
        /// <summary>相邻采样帧间隔。</summary>
        public double IntervalSeconds { get; }

        /// <summary>解析根局部坐标；格式错误或非有限值抛出 ArgumentException。</summary>
        public ArcadeHitPoseSequence(int frameCount, int capsulesPerFrame, double intervalSeconds, string encodedData)
        {
            if(frameCount<2 || capsulesPerFrame<1 || !Finite(intervalSeconds) || intervalSeconds<=0 || !Finite(intervalSeconds*(frameCount-1)) || encodedData==null)
                throw new ArgumentException("姿态序列帧数、体积数量、时间间隔或数据无效。");
            long count=(long)frameCount*capsulesPerFrame;
            if(count>int.MaxValue/7)throw new ArgumentException("姿态序列过大。");
            var tokens=encodedData.Split('|');
            if(tokens.Length!=count*7)throw new ArgumentException("姿态样本数量与帧声明不一致。");
            samples=new ArcadeHitCapsule[(int)count];
            for(int i=0;i<samples.Length;i++)
            {
                var values=new double[7];
                for(int j=0;j<7;j++)
                    if(!double.TryParse(tokens[i*7+j],NumberStyles.Float,CultureInfo.InvariantCulture,out values[j]) || !Finite(values[j]))
                        throw new ArgumentException("姿态样本必须为有限实数。");
                if(values[6]<0)throw new ArgumentException("姿态胶囊半径不能为负数。");
                samples[i]=new ArcadeHitCapsule(new ArcadeHitPoint(values[0],values[1],values[2]),new ArcadeHitPoint(values[3],values[4],values[5]),values[6]);
            }
            FrameCount=frameCount;CapsulesPerFrame=capsulesPerFrame;IntervalSeconds=intervalSeconds;
        }

        /// <summary>从权威配置文本构建序列，使用固定文化格式。</summary>
        public static ArcadeHitPoseSequence Parse(int frameCount,int capsulesPerFrame,double intervalSeconds,string encodedData)
            =>new ArcadeHitPoseSequence(frameCount,capsulesPerFrame,intervalSeconds,encodedData);

        /// <summary>年龄越界夹到端帧，帧间线性插值；非法年龄或索引抛出异常。</summary>
        public ArcadeHitCapsule Sample(double age,int capsuleIndex)
        {
            if(!Finite(age))throw new ArgumentException("姿态年龄必须有限。");
            if(capsuleIndex<0||capsuleIndex>=CapsulesPerFrame)throw new ArgumentOutOfRangeException(nameof(capsuleIndex));
            double frame=Math.Max(0,Math.Min(FrameCount-1,age/IntervalSeconds));
            int left=(int)frame,right=Math.Min(left+1,FrameCount-1);
            double t=frame-left;
            var a=samples[left*CapsulesPerFrame+capsuleIndex];var b=samples[right*CapsulesPerFrame+capsuleIndex];
            return new ArcadeHitCapsule(Lerp(a.A,b.A,t),Lerp(a.B,b.B,t),a.Radius*(1-t)+b.Radius*t);
        }

        /// <summary>世界弹道变换到身体根局部坐标，遍历所有体积返回最早接触；无效输入返回 false。</summary>
        public bool TrySweepSphere(double age,ArcadeHitPoint from,ArcadeHitPoint to,double radius,double enemyX,double enemyZ,double forwardX,double forwardZ,out double earliestFraction)
        {
            earliestFraction=0;
            if(!Finite(age)||!Basis(enemyX,enemyZ,ref forwardX,ref forwardZ))return false;
            var localFrom=Local(from,enemyX,enemyZ,forwardX,forwardZ);var localTo=Local(to,enemyX,enemyZ,forwardX,forwardZ);
            double first=double.PositiveInfinity;
            for(int i=0;i<CapsulesPerFrame;i++)
                if(ArcadeHitGeometry.TrySweepSphere(localFrom,localTo,radius,Sample(age,i),out double hit))first=Math.Min(first,hit);
            if(!Finite(first))return false;
            earliestFraction=first;return true;
        }

        /// <summary>世界球体与指定年龄下任一身体胶囊重叠；无效输入返回 false。</summary>
        public bool OverlapsSphere(double age,ArcadeHitPoint center,double radius,double enemyX,double enemyZ,double forwardX,double forwardZ)
        {
            if(!Finite(age)||!Basis(enemyX,enemyZ,ref forwardX,ref forwardZ))return false;
            var local=Local(center,enemyX,enemyZ,forwardX,forwardZ);
            for(int i=0;i<CapsulesPerFrame;i++)if(ArcadeHitGeometry.OverlapsSphere(local,radius,Sample(age,i)))return true;
            return false;
        }

        /// <summary>归一化平面朝向，拒绝零方向与无效位置。</summary>
        private static bool Basis(double x,double z,ref double fx,ref double fz)
        {
            if(!Finite(x)||!Finite(z)||!Finite(fx)||!Finite(fz))return false;
            double scale=Math.Max(Math.Abs(fx),Math.Abs(fz));if(scale==0)return false;
            fx/=scale;fz/=scale;double length=Math.Sqrt(fx*fx+fz*fz);fx/=length;fz/=length;return true;
        }
        /// <summary>局部右轴为 (forwardZ,-forwardX)，前轴为 forward；高度不变。</summary>
        private static ArcadeHitPoint Local(ArcadeHitPoint p,double x,double z,double fx,double fz)
            =>new ArcadeHitPoint((p.X-x)*fz-(p.Z-z)*fx,p.Y,(p.X-x)*fx+(p.Z-z)*fz);
        /// <summary>用加权和插值避免端点差值溢出。</summary>
        private static ArcadeHitPoint Lerp(ArcadeHitPoint a,ArcadeHitPoint b,double t)
            =>new ArcadeHitPoint(a.X*(1-t)+b.X*t,a.Y*(1-t)+b.Y*t,a.Z*(1-t)+b.Z*t);
        /// <summary>检查有限实数。</summary>
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}
