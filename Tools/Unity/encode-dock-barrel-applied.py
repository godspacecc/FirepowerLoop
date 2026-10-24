"""编码正式紫桶投影实拍，明确区分自动快照演示与真人对局。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / "Client/Temp/DockBarrelApplied"
target = root / "Backup/Artifacts/DockBarrelApplied-20261023"
target.mkdir(parents=True, exist_ok=True)
files = sorted(source.glob("[0-9][0-9][0-9].png"))
assert len(files) == 96, f"预期96帧，实际{len(files)}"
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 25)
small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 19)
frames = []
for i, file in enumerate(files):
    frame = Image.open(file).convert("RGB")
    draw = ImageDraw.Draw(frame)
    draw.rectangle((0, 0, 1280, 85), fill="#1c3039")
    draw.text((640, 12), "第二关正式紫桶：便服丧尸背桶 → 卸下后继续留在地面", font=font, fill="white", anchor="mt")
    draw.text((640, 51), "正式主角同框 · Unity隔离实拍 · 正式控制器与资源 · 快照演示，非真人对局", font=small, fill="#b8cdd5", anchor="mt")
    draw.rectangle((0, 643, 1280, 720), fill="#1c3039")
    text = "存活未满5秒：背桶附在原有丧尸上，右侧为固定地面桶" if i < 60 else "满5个活动秒：背桶移除、原丧尸保留，卸下桶继续产生浓紫雾"
    draw.text((640, 663), text, font=font, fill="#ddaeff", anchor="mt")
    if i in (24, 80):
        frame.save(target / ("carrying.png" if i == 24 else "released.png"))
    frames.append(frame.resize((960, 540), Image.Resampling.LANCZOS).quantize(colors=128))
output = target / "dock-barrel-applied.gif"
frames[0].save(output, save_all=True, append_images=frames[1:], duration=80, loop=0, disposal=2)
with Image.open(output) as result:
    assert result.n_frames == 96
    assert result.info.get("loop") == 0
print(f"{output}\n96帧 / 7.68秒 / {output.stat().st_size} bytes")
