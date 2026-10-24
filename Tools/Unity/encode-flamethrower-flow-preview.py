"""编码Unity原始隔离采样帧，不改变画面或正式资源。"""
from pathlib import Path
import sys
from PIL import Image

root = Path(__file__).resolve().parents[2]
applied = "--applied" in sys.argv
source = root / ("Client/Temp/FlamethrowerFlowApplied" if applied else "Client/Temp/FlamethrowerFlowPreview")
target = root / "Backup/Artifacts/FlamethrowerFlow-20261023"
target.mkdir(parents=True, exist_ok=True)
files = sorted(source.glob("*.png"))
if len(files) != 72:
    raise RuntimeError(f"应有72帧，实际{len(files)}")
frames = [Image.open(file).convert("RGB").quantize(colors=192) for file in files]
output = target / ("flow-applied.gif" if applied else "flow-timing.gif")
frames[0].save(output, save_all=True, append_images=frames[1:], duration=50, loop=0, disposal=2)
with Image.open(output) as gif:
    duration = 0
    for index in range(gif.n_frames):
        gif.seek(index)
        duration += gif.info["duration"]
    if gif.n_frames != 72 or duration != 3600 or gif.info.get("loop") != 0:
        raise RuntimeError("GIF帧数、时间或循环不符合采样")
    print(f"{output}: {gif.n_frames} frames, {duration}ms, loop=0, size={gif.size}")
