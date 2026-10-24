"""仅汇总 Unity 临时实例原图并编码动态；不改变素材、正式资源或原图内容。"""
from pathlib import Path
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Client/Temp/NextThreeStagesArt-v1"
TARGET = ROOT / "Backup/Artifacts/NextThreeStagesArt-v1-20261023"
TARGET.mkdir(parents=True, exist_ok=True)

for name, count in (("graveyard", 84), ("temple", 96), ("marketplace", 96)):
    output = TARGET / name
    output.mkdir(exist_ok=True)
    for static in ("overview.png", "detail.png", "landmark.png"):
        shutil.copy2(SOURCE / name / static, output / static)
    paths = sorted((SOURCE / name / "frames").glob("*.png"))
    if len(paths) != count:
        raise RuntimeError(f"{name}: expected {count}, found {len(paths)}")
    frames = []
    for path in paths:
        with Image.open(path) as img:
            frames.append(img.convert("RGB").quantize(colors=192))
    duration = [80 if i % 3 != 2 else 90 for i in range(count)]
    frames[0].save(output / "motion.gif", save_all=True,
                   append_images=frames[1:], duration=duration, loop=0, disposal=2)
    with Image.open(output / "motion.gif") as gif:
        total = 0
        for index in range(gif.n_frames):
            gif.seek(index)
            total += gif.info.get("duration", 0)
        print(f"{name}: {gif.n_frames} frames, {total}ms, {gif.size}, loop={gif.info.get('loop')}")
print(TARGET)
