"""汇总墓穴爬出v2的Unity原始帧；不重绘、不修改正式资源。"""
from pathlib import Path
import shutil
from PIL import Image

root = Path(__file__).resolve().parents[2]
source = root / "Client/Temp/NextThreeStagesArt-v2/graveyard"
target = root / "Backup/Artifacts/GraveyardTombs-v2-20261023"
target.mkdir(parents=True, exist_ok=True)
for name in ("overview.png", "landmark.png"):
    shutil.copy2(source / name, target / name)
paths = sorted((source / "frames").glob("*.png"))
if len(paths) != 108:
    raise RuntimeError(f"Expected 108 source frames, found {len(paths)}")
frames = []
for path in paths:
    with Image.open(path) as frame:
        frames.append(frame.convert("RGB").quantize(colors=192))
frames[0].save(target / "motion.gif", save_all=True, append_images=frames[1:],
               duration=[80 if i % 3 != 2 else 90 for i in range(108)], loop=0, disposal=2)
with Image.open(target / "motion.gif") as gif:
    durations = []
    for i in range(gif.n_frames):
        gif.seek(i)
        durations.append(gif.info["duration"])
    print(gif.n_frames, sum(durations), gif.size)
