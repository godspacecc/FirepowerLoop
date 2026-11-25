namespace FirepowerLoop.Arcade
{
    /// <summary>竞技场平面米；双端规则及网格共享，不含 Unity 对象。</summary>
    public readonly struct ArcadeRegionPoint
    {
        public ArcadeRegionPoint(float x, float y) { X = x; Y = y; }
        public float X { get; }
        public float Y { get; }
    }
}
