"""仅归档实际Controller隔离采样帧并编码GIF，不改变正式资源或原始图像。"""
from pathlib import Path
import json
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Client/Temp/NextStagesControllerApplied"
TARGET = ROOT / "Backup/Artifacts/NextStagesControllerApplied-20261023"
report = []
for name, count in (("graveyard", 108), ("temple", 96), ("marketplace", 96)):
    output = TARGET / name
    output.mkdir(parents=True, exist_ok=True)
    paths = sorted((SOURCE / name / "frames").glob("*.png"))
    if len(paths) != count:
        raise RuntimeError(f"{name}: expected {count} source frames, found {len(paths)}")
    frames = []
    for path in paths:
        with Image.open(path) as frame:
            frames.append(frame.convert("RGB").quantize(colors=192))
    frames[0].save(output / "motion.gif", save_all=True,
                   append_images=frames[1:], duration=[80 if i % 3 != 2 else 90 for i in range(count)],
                   loop=0, disposal=2)
    for index in (0, 18, 40, 64, count-1):
        shutil.copy2(paths[index], output / f"frame-{index:03}.png")
    shutil.copy2(SOURCE / name / "source.txt", output / "source.txt")
    with Image.open(output / "motion.gif") as gif:
        duration = 0
        for index in range(gif.n_frames):
            gif.seek(index)
            duration += gif.info.get("duration", 0)
        entry = dict(stage=name, sourceFrames=count, encodedFrames=gif.n_frames,
                     durationMs=duration, size=list(gif.size), loop=gif.info.get("loop"))
        report.append(entry)
        print(entry)
(TARGET / "encoding.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(TARGET)
