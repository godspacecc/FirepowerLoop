"""将 Unity 原始帧编码为循环预览，不绘制字幕、不改变布局或正式资源。"""
from pathlib import Path
import sys
from PIL import Image

root = Path(__file__).resolve().parents[2]
applied = "--applied" in sys.argv
source = root / ("Client/Temp/FlamethrowerDropPivotApplied" if applied else "Client/Temp/FlamethrowerDropPivotPreview")
target = root / ("Backup/Artifacts/FlamethrowerDropPivotApplied-20261023" if applied else "Backup/Artifacts/FlamethrowerDropPivot-v1-20261023")
target.mkdir(parents=True, exist_ok=True)
files = sorted(source.glob("*.png"))
if len(files) != 80:
    raise RuntimeError(f"应有 80 帧，实际 {len(files)}")
frames = [Image.open(path).convert("RGB").quantize(colors=192) for path in files]
output = target / ("pivot-applied.gif" if applied else "pivot-comparison.gif")
frames[0].save(output, save_all=True,
               append_images=frames[1:], duration=50, loop=0, disposal=2)
print(output)
