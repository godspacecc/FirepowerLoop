param([string]$OutputPath = (Join-Path $PSScriptRoot '../../Client/Assets/Game/Art/UI/UtilityButtons/DashLightning.png'))

# 按已确认的D方案输出透明图标；矢量几何与预览一致，不依赖字体字形。
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class DashLightningSprite
{
    public static void Render(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        using (var image = new Bitmap(288, 288))
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.TranslateTransform(144, 144);
            graphics.ScaleTransform(3, 3);
            var ink = Color.FromArgb(244, 240, 218);
            using (var fill = new SolidBrush(ink))
                graphics.FillPolygon(fill, new[] {
                    new PointF(-2,-31), new PointF(22,-31), new PointF(5,-7),
                    new PointF(27,-7), new PointF(-11,33), new PointF(-4,7), new PointF(-24,7) });
            using (var trail = new Pen(Color.FromArgb(130, ink), 4))
            {
                trail.StartCap = trail.EndCap = LineCap.Round;
                graphics.DrawLine(trail, -37, -12, -24, -12);
                graphics.DrawLine(trail, -40, 0, -31, 0);
                graphics.DrawLine(trail, -32, 18, -24, 18);
            }
            image.Save(path, ImageFormat.Png);
        }
    }
}
'@ -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives,System.Drawing,System.Private.Windows.Core,System.Private.Windows.GdiPlus
[DashLightningSprite]::Render($OutputPath)
Write-Output $OutputPath
