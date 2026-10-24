from pathlib import Path
from PIL import Image

# 将隔离渲染帧编码为可播放预览；不修改正式资源或原始帧。
folder = Path(__file__).resolve().parent
for name in ("bull", "race"):
    sources = sorted((folder / "frames").glob(f"{name}-*.png"))
    if len(sources) != 60:
        raise RuntimeError(f"{name}: expected 60 frames, got {len(sources)}")
    frames = [Image.open(path).convert("RGB") for path in sources]
    frames[0].save(folder / f"{name}-motion.gif", save_all=True,
                   append_images=frames[1:], duration=100, loop=0, optimize=True)
    for frame in frames:
        frame.close()
    check = Image.open(folder / f"{name}-motion.gif")
    duration = 0
    for index in range(check.n_frames):
        check.seek(index)
        duration += check.info.get("duration", 0)
    # GIF 会合并相同的停留帧，以总播放时长和确有动作验证编码完整性。
    assert check.n_frames > 1 and duration == 6000, f"{name}: animation duration changed"
    print(f"{name}: {check.n_frames} encoded frames, {check.size}, {duration} ms")

sources = [path for name in ("bull", "race")
           for path in sorted((folder / "frames").glob(f"{name}-*.png"))]
frames = [Image.open(path).convert("RGB") for path in sources]
frames[0].save(folder / "early-motion.gif", save_all=True,
               append_images=frames[1:], duration=100, loop=0, optimize=True)
for frame in frames:
    frame.close()
check = Image.open(folder / "early-motion.gif")
duration = 0
for index in range(check.n_frames):
    check.seek(index)
    duration += check.info.get("duration", 0)
assert duration == 12000 and check.n_frames > 60
print(f"combined: {check.n_frames} encoded frames, {duration} ms")
